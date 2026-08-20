namespace Transpose.HttpClient.Tests;

/// <summary>
/// The header collections: what a caller can put in them, what comes back out, and what reaches the
/// wire.
///
/// <b>The header model is deliberately simpler than .NET's</b>, and that is a design decision rather
/// than something to fix: <c>HttpHeaders</c> here is one <c>Dictionary&lt;string, string&gt;</c>, not a
/// multi-value store with per-header parsers, strongly typed collections
/// (<c>HttpRequestHeaders.Accept</c> and friends) and validation. A browser application does not need
/// that, and it is a large amount of code to carry into a bundle. Every difference that follows from
/// the simpler model is therefore recorded here as a divergence — asserted on both sides so the note
/// cannot rot — and NOT as a defect.
/// </summary>
[TestClass]
public class HeaderTests : HttpClientTestBase
{
    /// <summary>
    /// <b>The reported bug.</b> <c>HttpHeaders.GetEnumerator()</c> is an iterator method declared to
    /// return the cursor (<c>IEnumerator&lt;T&gt;</c>) rather than the sequence, and Transpose used to
    /// compile any iterator to an *enumerable* — so <c>AddHeaders</c>' <c>foreach</c> over a
    /// header collection died with "TypeError: e.MoveNext is not a function", taking down every
    /// request made by a client with a default header set. Fixed in the emitter
    /// (<c>TransposeR.iterEnumerator</c>); this is the end-to-end guard.
    /// </summary>
    [TestMethod]
    public async Task DefaultRequestHeadersReachTheWire()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("GET", "https://api.test/x", 200, "ok");

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret");

        await client.GetAsync("https://api.test/x");

        Console.WriteLine(Xhr.RequestHeaders(0));
    }
}
""", """
Accept: application/json
X-Api-Key: secret
""");
    }

    /// <summary>The same collection enumerated directly — the cursor the fix is about.</summary>
    [TestMethod]
    public async Task HeadersEnumerate()
    {
        await RunJs("""
using System;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        var message = new HttpRequestMessage(HttpMethod.Get, "https://api.test/x");
        message.Headers.Add("A", "1");
        message.Headers.Add("B", "2");

        foreach (var header in message.Headers) Console.WriteLine(header.Key + " = " + header.Value);

        var cursor = message.Headers.GetEnumerator();
        while (cursor.MoveNext()) Console.WriteLine("cursor " + cursor.Current.Key);
    }
}
""", """
A = 1
B = 2
cursor A
cursor B
""");
    }

    /// <summary>An empty collection enumerates to nothing rather than throwing.</summary>
    [TestMethod]
    public async Task EmptyHeadersEnumerateToNothing()
    {
        await RunJs("""
using System;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        var client = new HttpClient();
        var count = 0;
        foreach (var header in client.DefaultRequestHeaders) count++;
        Console.WriteLine("count: " + count);
        Console.WriteLine("moveNext: " + client.DefaultRequestHeaders.GetEnumerator().MoveNext());
    }
}
""", """
count: 0
moveNext: False
""");
    }

    [TestMethod]
    public async Task ContainsRemoveAndClear()
    {
        await RunJs("""
using System;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        var headers = new HttpRequestMessage().Headers;
        Console.WriteLine("contains (empty): " + headers.Contains("A"));
        Console.WriteLine("remove (empty): " + headers.Remove("A"));

        headers.Add("A", "1");
        headers.Add("B", "2");
        Console.WriteLine("contains A: " + headers.Contains("A"));
        Console.WriteLine("contains a: " + headers.Contains("a"));
        Console.WriteLine("remove A: " + headers.Remove("A"));
        Console.WriteLine("contains A: " + headers.Contains("A"));

        headers.Clear();
        Console.WriteLine("contains B: " + headers.Contains("B"));
    }
}
""", """
contains (empty): False
remove (empty): False
contains A: True
contains a: False
remove A: True
contains A: False
contains B: False
""");
    }

    /// <summary>
    /// A header set on the request message is sent alongside the client's defaults, and both are
    /// applied after <c>open()</c> — a real XHR discards anything set before it.
    /// </summary>
    [TestMethod]
    public async Task RequestAndClientHeadersAreBothSent()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("GET", "https://api.test/x", 200, "ok");

        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var message = new HttpRequestMessage(HttpMethod.Get, "https://api.test/x");
        message.Headers.Add("X-Request-Id", "abc");

        await client.SendAsync(message);

        Console.WriteLine(Xhr.RequestHeaders(0));
    }
}
""", """
Accept: application/json
X-Request-Id: abc
""");
    }

    /// <summary>
    /// <b>Divergence, by design.</b> The store is one value per name, so adding a second value for a
    /// header throws instead of appending; .NET combines them into one comma-separated field. A caller
    /// that wants a list writes it as a list ("application/json, text/plain") in a single Add.
    /// </summary>
    [TestMethod]
    public async Task AddingASecondValueForAHeaderThrows()
    {
        await RunJs("""
using System;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        var headers = new HttpRequestMessage().Headers;
        headers.Add("Accept", "application/json");

        try
        {
            headers.Add("Accept", "text/plain");
            Console.WriteLine("added");
        }
        catch (Exception e)
        {
            Console.WriteLine(e.GetType().Name);
        }

        foreach (var header in headers) Console.WriteLine(header.Key + " = " + header.Value);
    }
}
""", """
ArgumentException
Accept = application/json
""", nativePrints: """
added
Accept = application/json,text/plain
""", nativeCode: """
using System;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        var headers = new HttpRequestMessage().Headers;
        headers.Add("Accept", "application/json");

        try
        {
            headers.Add("Accept", "text/plain");
            Console.WriteLine("added");
        }
        catch (Exception e)
        {
            Console.WriteLine(e.GetType().Name);
        }

        foreach (var header in headers) Console.WriteLine(header.Key + " = " + string.Join(",", header.Value));
    }
}
""");
    }

    /// <summary>
    /// <b>Bug, not a model difference.</b> The simplified design reads a response header straight off
    /// the <c>XMLHttpRequest</c> (<c>HttpHeaders.GetHeaderString</c>) instead of parsing the response
    /// into a store, which is a reasonable simplification — but the wiring is broken, so it can never
    /// run: <c>HttpResponseMessage</c> takes the <c>XMLHttpRequest</c> in its constructor and never
    /// stores it, and its <c>Headers</c> getter reaches for it through <c>RequestMessage</c>, which the
    /// handler never sets either. So <c>response.Headers</c> is a null dereference for every response
    /// the package produces — and <c>GetHeaderString</c> is <c>internal</c>, so there is no other way
    /// in.
    /// </summary>
    [TestMethod]
    public async Task ReadingResponseHeadersThrows()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("GET", "https://api.test/x", 200, "ok", "Content-Type: text/plain\nX-Total-Count: 12");

        var response = await new HttpClient().GetAsync("https://api.test/x");

        try
        {
            Console.WriteLine("count: " + response.Headers.Contains("X-Total-Count"));
        }
        catch (Exception e)
        {
            Console.WriteLine(e.GetType().Name);
        }
    }
}
""", """
TypeError
""");
    }
}
