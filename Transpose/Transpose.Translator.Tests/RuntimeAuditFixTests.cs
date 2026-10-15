using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Regression tests for runtime (tps.js / BCL) bugs found by an audit. Each one pins a single fix;
    /// where the behaviour is observable from plain C#, the output is diffed against native .NET, and
    /// where it needs a JavaScript stand-in (a time zone, a WebSocket, raw JSON) the expected output is
    /// written down.
    /// </summary>
    [TestClass]
    public class RuntimeAuditFixTests : TranslatorTestBase
    {
        /// Transpose.merge is what JsonConvert/System.Text.Json run JSON.parse output through. An own
        /// "__proto__" key used to be merged into Object.prototype.
        [TestMethod]
        public async Task MergeIgnoresPrototypeKeysFromParsedJsonAsync()
        {
            var output = await RunTest("""
using System;
using Transpose;

public class Program
{
    public static void Main()
    {
        var target = Script.Write<object>("Transpose.merge({}, JSON.parse('{\"__proto__\":{\"isAdmin\":true},\"constructor\":{\"prototype\":{\"polluted\":true}},\"name\":\"n\"}'))");
        Console.WriteLine(Script.Write<bool>("({}).isAdmin === undefined && ({}).polluted === undefined && Object.prototype.isAdmin === undefined"));
        Console.WriteLine(Script.Write<string>("{0}.name", target));
        Console.WriteLine(Script.Write<bool>("Object.getPrototypeOf({0}) === Object.prototype", target));
    }
}
""", skipRoslyn: true);

            Assert.AreEqual("True\nn\nTrue", output,
                "a parsed \"__proto__\"/\"constructor\" key must not reach any prototype; ordinary keys still merge");
        }

        /// Dictionary's simple-key buckets were a plain {}: "__proto__" was never stored or found and
        /// a "hasOwnProperty" key broke every later lookup.
        [TestMethod]
        public async Task DictionaryAcceptsKeysThatNameObjectPrototypeMembersAsync()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public class Program
{
    public static void Main()
    {
        var d = new Dictionary<string, int>();
        d.Add("__proto__", 1);
        Console.WriteLine(d.ContainsKey("__proto__") + " " + d.Count + " " + d["__proto__"]);
        try { d.Add("__proto__", 2); Console.WriteLine("duplicate added"); }
        catch (ArgumentException) { Console.WriteLine("duplicate rejected"); }

        d["hasOwnProperty"] = 2;
        Console.WriteLine(d.ContainsKey("x") + " " + d.ContainsKey("hasOwnProperty") + " " + d.ContainsKey("constructor") + " " + d.ContainsKey("toString"));

        // Enough keys to force a resize, which rebuilds the buckets.
        for (var i = 0; i < 20; i++) d["k" + i] = i;
        Console.WriteLine(d.Count + " " + d["__proto__"] + " " + d["hasOwnProperty"]);

        Console.WriteLine(d.Remove("__proto__") + " " + d.ContainsKey("__proto__") + " " + d.Count);
        d.Clear();
        d["__proto__"] = 3;
        int v;
        Console.WriteLine(d.TryGetValue("__proto__", out v) + " " + v + " " + d.Count);

        var chars = new Dictionary<char, int> { ['a'] = 1 };
        Console.WriteLine(chars.ContainsKey('a') + " " + chars.ContainsKey('b'));
    }
}
""");
        }

        /// A Utc DateTime's DayOfWeek/DayOfYear were read with the LOCAL getters, so the answer moved
        /// with the machine's time zone.
        [TestMethod]
        public async Task UtcDayOfWeekAndDayOfYearIgnoreTheLocalTimeZoneAsync()
        {
            var output = await RunTest("""
using System;
using Transpose;

public class Program
{
    static string Describe()
    {
        var utc = new DateTime(2026, 3, 5, 20, 0, 0, DateTimeKind.Utc);
        var early = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc);
        var local = new DateTime(2026, 3, 5, 20, 0, 0, DateTimeKind.Local);
        return utc.DayOfWeek + " " + utc.DayOfYear + " " + early.DayOfYear + " " + local.DayOfWeek + " " + local.DayOfYear;
    }

    public static void Main()
    {
        // Node re-reads TZ when it is assigned.
        Script.Write("process.env.TZ = 'Asia/Tokyo'");
        Console.WriteLine(Describe());
        Script.Write("process.env.TZ = 'America/New_York'");
        Console.WriteLine(Describe());
    }
}
""", skipRoslyn: true);

            Assert.AreEqual(
                "Thursday 64 1 Thursday 64\n"
                + "Thursday 64 1 Thursday 64",
                output,
                "a Utc value's calendar day is the UTC one in every time zone");
        }

        /// Each Task.Delay overwrote the token's single cancellation slot, so cancelling reached only
        /// the last one; Delay(-1) completed at once instead of never.
        [TestMethod]
        public async Task TaskDelayCancellationAndInfiniteTimeoutAsync()
        {
            await RunTest("""
using System;
using System.Threading;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        var cts = new CancellationTokenSource();
        var a = Task.Delay(10000, cts.Token);
        var b = Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
        cts.Cancel();
        try { await a; Console.WriteLine("a completed"); } catch (TaskCanceledException) { Console.WriteLine("a canceled " + a.IsCanceled); }
        try { await b; Console.WriteLine("b completed"); } catch (TaskCanceledException) { Console.WriteLine("b canceled " + b.IsCanceled); }

        var infinite = Task.Delay(-1);
        var infiniteSpan = Task.Delay(TimeSpan.FromMilliseconds(-1));
        await Task.Delay(30);
        Console.WriteLine("infinite completed: " + infinite.IsCompleted + " " + infiniteSpan.IsCompleted);

        var cts2 = new CancellationTokenSource();
        var forever = Task.Delay(Timeout.Infinite, cts2.Token);
        cts2.Cancel();
        try { await forever; Console.WriteLine("forever completed"); } catch (TaskCanceledException) { Console.WriteLine("forever canceled"); }

        try { var t = Task.Delay(-2); Console.WriteLine("-2 accepted"); }
        catch (ArgumentOutOfRangeException) { Console.WriteLine("-2 rejected"); }

        // A delay that finishes releases its registration and still completes normally.
        var cts3 = new CancellationTokenSource();
        await Task.Delay(1, cts3.Token);
        cts3.Cancel();
        Console.WriteLine("short delay done");
    }
}
""");
        }

        /// ClientWebSocket moved text as UTF-16 code units rather than UTF-8 bytes, and a receive
        /// pending when the server closed polled forever.
        [TestMethod]
        public async Task ClientWebSocketTextIsUtf8AndAReceiveSeesTheCloseAsync()
        {
            var output = await RunTest("""
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Transpose;

public class Program
{
    public static async Task Main()
    {
        Script.Write(@"
            globalThis.__sent = [];
            globalThis.WebSocket = function (url) {
                var self = this;
                globalThis.__ws = this;
                this.readyState = 0;
                setTimeout(function () { self.readyState = 1; self.onopen(); }, 0);
            };
            WebSocket.prototype.send = function (d) { __sent.push(typeof d === 'string' ? d : 'binary:' + d.length); };
            WebSocket.prototype.close = function () { };
        ");

        var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri("ws://socket.test/"), CancellationToken.None);

        var bytes = Encoding.UTF8.GetBytes("héllo €");
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        Console.WriteLine(Script.Write<string>("__sent[0]") == "héllo €");

        Script.Write("__ws.onmessage({ data: 'gr\\u00f6\\u00dfe \\u2713' })");
        var buffer = new byte[64];
        var received = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        Console.WriteLine(received.Count + " " + (Encoding.UTF8.GetString(buffer, 0, received.Count) == "größe ✓"));

        var pending = ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        Script.Write("setTimeout(function () { __ws.readyState = 3; __ws.onclose({ code: 1000, reason: 'bye' }); }, 5)");
        var closed = await pending;
        Console.WriteLine((closed.MessageType == WebSocketMessageType.Close) + " " + closed.Count + " "
            + (closed.CloseStatus == WebSocketCloseStatus.NormalClosure) + " " + closed.CloseStatusDescription);
    }
}
""", skipRoslyn: true);

            Assert.AreEqual("True\n11 True\nTrue 0 True bye", output,
                "text frames are UTF-8 both ways, and a receive pending at close completes with a Close result");
        }

        /// "hh" was never zero-padded, and "z"/"zz"/"zzz" printed "Z" or nothing (the "K" rule)
        /// instead of an offset.
        [TestMethod]
        public async Task CustomDateFormatHourAndOffsetSpecifiersAsync()
        {
            await RunTest("""
using System;
using System.Globalization;

public class Program
{
    public static void Main()
    {
        var inv = CultureInfo.InvariantCulture;
        var utc = new DateTime(2026, 3, 5, 7, 4, 9, DateTimeKind.Utc);
        var unspecified = new DateTime(2026, 3, 5, 7, 4, 9);

        Console.WriteLine(utc.ToString("hh:mm tt", inv));
        Console.WriteLine(new DateTime(2026, 3, 5, 0, 4, 9).ToString("hh h", inv));
        Console.WriteLine(new DateTime(2026, 3, 5, 13, 4, 9).ToString("hh h", inv));
        Console.WriteLine(new DateTime(2026, 3, 5, 23, 4, 9).ToString("hh h", inv));
        Console.WriteLine(utc.ToString("z|zz|zzz", inv));
        Console.WriteLine(utc.ToString("%K", inv) + "|" + unspecified.ToString("%K", inv) + "|");
        Console.WriteLine(utc.ToString("o", inv));
        Console.WriteLine(unspecified.ToString("o", inv));

        // Unspecified prints the local offset, whatever this machine's zone is.
        var offset = unspecified - unspecified.ToUniversalTime();
        var minutes = (int)Math.Abs(offset.TotalMinutes);
        var expected = (offset < TimeSpan.Zero ? "-" : "+") + (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
        Console.WriteLine(unspecified.ToString("zzz", inv) == expected);
    }
}
""");
        }

        /// long.Parse / Convert.ToInt64 judged overflow by comparing the raw input with the parsed
        /// value, so "-05" and "-0" overflowed, and surrounding white space was a format error.
        [TestMethod]
        public async Task Int64ParsingAcceptsSignsLeadingZerosAndWhiteSpaceAsync()
        {
            await RunTest("""
using System;

public class Program
{
    static void Try(string label, Func<object> parse)
    {
        try { Console.WriteLine(label + " = " + parse()); }
        catch (Exception e) { Console.WriteLine(label + " threw " + e.GetType().Name); }
    }

    public static void Main()
    {
        Try("long -05", () => long.Parse("-05"));
        Try("long -0", () => long.Parse("-0"));
        Try("long +7", () => long.Parse("+7"));
        Try("long ' 42 '", () => long.Parse(" 42 "));
        Try("long 007", () => long.Parse("007"));
        Try("long min", () => long.Parse("-9223372036854775808"));
        Try("long overflow", () => long.Parse("9223372036854775808"));
        Try("long '1 2'", () => long.Parse("1 2"));
        Try("ulong -0", () => ulong.Parse("-0"));
        Try("ulong max", () => ulong.Parse(" +18446744073709551615 "));
        Try("ulong -1", () => ulong.Parse("-1"));
        Try("Convert.ToInt64 -01", () => Convert.ToInt64("-01"));
        Try("Convert.ToUInt64 00", () => Convert.ToUInt64("00"));
        Try("Convert.ToInt32 '  12  '", () => Convert.ToInt32("  12  "));
        Try("Convert.ToInt32 -0", () => Convert.ToInt32("-0"));
        Try("Convert.ToInt32 ''", () => Convert.ToInt32(""));

        long v;
        Console.WriteLine(long.TryParse(" -0009 ", out v) + " " + v);
        ulong u;
        Console.WriteLine(ulong.TryParse("-0", out u) + " " + u);
    }
}
""");
        }

        /// Cloning the invariant culture (and new CultureInfo("")) copied getter-only TextInfo
        /// properties and died with a TypeError.
        [TestMethod]
        public async Task InvariantCultureCanBeClonedAndConstructedAsync()
        {
            await RunTest("""
using System;
using System.Globalization;

public class Program
{
    public static void Main()
    {
        var clone = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        Console.WriteLine(clone.TextInfo.IsReadOnly + " " + CultureInfo.InvariantCulture.TextInfo.IsReadOnly);
        Console.WriteLine(clone.TextInfo.ListSeparator + " " + clone.TextInfo.ANSICodePage);
        Console.WriteLine(1.5.ToString(clone));

        var empty = new CultureInfo("");
        Console.WriteLine((empty.Name == "") + " " + empty.TextInfo.LCID + " " + empty.TextInfo.IsReadOnly);
        Console.WriteLine(2.25.ToString(empty));
    }
}
""");
        }
    }
}
