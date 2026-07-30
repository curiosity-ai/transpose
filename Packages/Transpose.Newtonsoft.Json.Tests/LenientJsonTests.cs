using System.IO;
using System.Threading.Tasks;

namespace Transpose.Newtonsoft.Json.Tests;

/// <summary>
/// Json.NET's reader accepts a superset of strict JSON — comments, single-quoted strings, unquoted
/// member names, trailing commas, hexadecimal numbers, NaN/Infinity — and payloads in the wild use it
/// (a hand-edited settings blob, a JavaScript-style literal). <c>JSON.parse</c> rejects all of it, so
/// the binding library falls back to its own reader (<c>parseLenient</c> in JsonConvert.js).
///
/// That fallback used to be <c>eval('(' + value + ')')</c>, which executed whatever the payload
/// contained and was blocked outright by a Content-Security-Policy without <c>'unsafe-eval'</c>.
/// <see cref="TheJavaScriptContainsNoEval"/> keeps it gone.
/// </summary>
[TestClass]
public class LenientJsonTests : JsonTestBase
{
    private const string Header = @"
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
public class Item { public string Name { get; set; } public int Value { get; set; } public double D { get; set; } }
public class App
{
    // Json.NET's reader exceptions (JsonReaderException, …) derive from JsonException, which is all
    // the binding library models — so report the base type both sides agree on.
    static void Try(string label, string json)
    {
        try
        {
            var item = JsonConvert.DeserializeObject<Item>(json);
            Console.WriteLine(label + "" => "" + (item == null ? ""<null>"" : (item.Name ?? ""<null>"") + ""/"" + item.Value + ""/"" + item.D));
        }
        catch (JsonException) { Console.WriteLine(label + "" => JsonException""); }
    }
";

    [TestMethod]
    public async Task TheLenientSyntaxJsonNetAcceptsIsAccepted()
    {
        var code = Header + @"
    public static void Main()
    {
        Try(""unquoted-key"",     ""{Name:\""a\"",Value:1}"");
        Try(""single-quotes"",    ""{'Name':'a','Value':1}"");
        Try(""mixed-quotes"",     ""{'Name':\""a\""}"");
        Try(""trailing-comma"",   ""{\""Name\"":\""a\"",\""Value\"":1,}"");
        Try(""block-comment"",    ""{/* which */\""Name\"":\""a\""}"");
        Try(""line-comment"",     ""{\""Name\"":\""a\"" // trailing note\n}"");
        Try(""nan"",              ""{\""D\"":NaN}"");
        Try(""infinity"",         ""{\""D\"":Infinity}"");
        Try(""negative-infinity"","" {\""D\"":-Infinity}"");
        Try(""hex"",              ""{\""Value\"":0x10}"");
        Try(""leading-dot"",      ""{\""D\"":.5}"");
        Try(""exponent"",         ""{\""D\"":1.5e3}"");
        Try(""all-at-once"",      ""{ /* c */ Name : 'a' , Value : 0x2 , }"");
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task WhatJsonNetRejectsIsStillRejected()
    {
        var code = Header + @"
    public static void Main()
    {
        Try(""undefined"",    ""{\""Name\"":undefined}"");
        Try(""signed-hex"",   ""{\""Value\"":-0xff}"");
        Try(""plus-number"",  ""{\""Value\"":+5}"");
        Try(""bare-word"",    ""{Name:a}"");
        Try(""garbage"",      ""not json at all"");
        Try(""unclosed-obj"", ""{\""Name\"":\""a\"""");
        Try(""unclosed-str"", ""{\""Name\"":\""a}"");
        Try(""unterminated-comment"", ""{\""Name\"":\""a\""/* open"");
        Try(""double-value"", ""{\""Name\"":\""a\""} {\""Name\"":\""b\""}"");
        Try(""missing-colon"", ""{\""Name\"" \""a\""}"");
        Try(""lone-comma"",   ""{,}"");
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task LenientCollectionsAndNestingParse()
    {
        var code = @"
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
public class Inner { public string Name { get; set; } }
public class Outer { public List<Inner> Items { get; set; } public Inner Child { get; set; } }
public class App
{
    public static void Main()
    {
        var json = ""{ Items : [ { Name : 'a' } , { Name : 'b' } , ] , Child : { /* c */ Name : 'c' } }"";
        var outer = JsonConvert.DeserializeObject<Outer>(json);
        Console.WriteLine(outer.Items.Count + ""|"" + outer.Items[0].Name + ""|"" + outer.Items[1].Name + ""|"" + outer.Child.Name);

        var array = JsonConvert.DeserializeObject<int[]>(""[ 1, 0x2, 3, ]"");
        Console.WriteLine(array.Length + ""|"" + array[1]);

        var map = JsonConvert.DeserializeObject<Dictionary<string, string>>(""{ a : 'x', 'b' : \""y\"", }"");
        Console.WriteLine(map.Count + ""|"" + map[""a""] + ""|"" + map[""b""]);

        var empty = JsonConvert.DeserializeObject<Outer>(""{ /* nothing */ }"");
        Console.WriteLine((empty.Items == null) + ""|"" + (empty.Child == null));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task EscapesInsideLenientStringsAreHonoured()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item { public string Name { get; set; } }
public class App
{
    public static void Main()
    {
        // A single-quoted string (so JSON.parse fails and the lenient reader takes over) carrying
        // every escape that matters.
        var item = JsonConvert.DeserializeObject<Item>(""{ Name : 'tab:\\t nl:\\n quote:\\\"" apos:\\' slash:\\/ back:\\\\ u:\\u0041' }"");
        Console.WriteLine(item.Name);
        Console.WriteLine(item.Name.Length);

        var doubleQuoted = JsonConvert.DeserializeObject<Item>(""{ Name : \""it's fine\"" }"");
        Console.WriteLine(doubleQuoted.Name);

        var singleQuoted = JsonConvert.DeserializeObject<Item>(""{ Name : 'say \\\""hi\\\""' }"");
        Console.WriteLine(singleQuoted.Name);
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>Strict JSON still goes through <c>JSON.parse</c> — the fallback changes nothing for it.</summary>
    [TestMethod]
    public async Task StrictJsonIsUnaffected()
    {
        var code = @"
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
public class Item { public string Name { get; set; } public int Value { get; set; } public List<int> Numbers { get; set; } }
public class App
{
    public static void Main()
    {
        var json = ""{\""Name\"":\""a\"",\""Value\"":1,\""Numbers\"":[1,2,3]}"";
        var item = JsonConvert.DeserializeObject<Item>(json);
        Console.WriteLine(item.Name + ""|"" + item.Value + ""|"" + item.Numbers.Count);
        Console.WriteLine(JsonConvert.SerializeObject(item));
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// The guard: no <c>eval</c> and no <c>new Function</c> in the package's JavaScript. Both execute
    /// their argument as code, so a deserializer must not reach for either — and a page served with a
    /// Content-Security-Policy that omits <c>'unsafe-eval'</c> cannot run them at all.
    /// </summary>
    [TestMethod]
    public void TheJavaScriptContainsNoEval()
    {
        foreach (var file in Directory.EnumerateFiles(TranslatedJsonRunner.PackageDir, "*.js", SearchOption.AllDirectories))
        {
            if (file.Replace('\\', '/').Contains("/Resources/.generated/")) continue; // compiler output

            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            foreach (var forbidden in new[] { "eval(", "new Function(", "setTimeout(\"", "setInterval(\"" })
            {
                Assert.IsFalse(text.Contains(forbidden, System.StringComparison.Ordinal),
                    $"{name} contains '{forbidden}': it would execute the payload as code and is blocked " +
                    "by a Content-Security-Policy without 'unsafe-eval'. Parse the input instead " +
                    "(see JsonConvert.parseLenient).");
            }
        }
    }
}
