namespace Transpose.HttpClient.Tests;

/// <summary>
/// <c>HttpResponseMessage</c>: the status, the reason phrase, success classification, and what the
/// package makes of a response body.
/// </summary>
[TestClass]
public class ResponseTests : HttpClientTestBase
{
    [TestMethod]
    public async Task StatusAndBodyComeBackFromTheTransport()
    {
        await RunJs("""
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("GET", "https://api.test/ok", 200, "yes");
        Xhr.Route("GET", "https://api.test/gone", 410, "vanished");

        var client = new HttpClient();

        var ok = await client.GetAsync("https://api.test/ok");
        Console.WriteLine("ok: " + (int)ok.StatusCode + " " + ok.StatusCode + " success=" + ok.IsSuccessStatusCode);
        Console.WriteLine("ok body: " + ok.Content.ReadAsString());

        var gone = await client.GetAsync("https://api.test/gone");
        Console.WriteLine("gone: " + (int)gone.StatusCode + " " + gone.StatusCode + " success=" + gone.IsSuccessStatusCode);
        Console.WriteLine("gone body: " + gone.Content.ReadAsString());
    }
}
""", """
ok: 200 OK success=True
ok body: yes
gone: 410 Gone success=False
gone body: vanished
""");
    }

    /// <summary>
    /// The reason phrase for a code the response did not carry one for, against the real .NET table.
    /// (A browser's XHR does expose <c>statusText</c>, but the package never reads it, so this is
    /// always the derived phrase.)
    /// </summary>
    [TestMethod]
    public async Task ReasonPhraseMatchesDotNet()
    {
        await RunAndCompare("""
using System;
using System.Net;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        foreach (var code in new[] { 100, 200, 201, 202, 204, 301, 302, 304, 400, 401, 403, 404, 405,
                                     409, 410, 418, 422, 429, 500, 501, 502, 503, 504, 599 })
        {
            Console.WriteLine(code + " -> [" + new HttpResponseMessage((HttpStatusCode)code, null).ReasonPhrase + "]");
        }
    }
}
""", nativeCode: """
using System;
using System.Net;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        foreach (var code in new[] { 100, 200, 201, 202, 204, 301, 302, 304, 400, 401, 403, 404, 405,
                                     409, 410, 418, 422, 429, 500, 501, 502, 503, 504, 599 })
        {
            Console.WriteLine(code + " -> [" + new HttpResponseMessage((HttpStatusCode)code).ReasonPhrase + "]");
        }
    }
}
""");
    }

    [TestMethod]
    public async Task AnExplicitReasonPhraseWins()
    {
        await RunAndCompare("""
using System;
using System.Net;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        var response = new HttpResponseMessage((HttpStatusCode)404, null);
        response.ReasonPhrase = "Nothing Here";
        Console.WriteLine(response.ReasonPhrase);

        try { response.ReasonPhrase = "bad\nphrase"; Console.WriteLine("accepted"); }
        catch (Exception e) { Console.WriteLine(e.GetType().Name); }

        response.ReasonPhrase = null;
        Console.WriteLine("[" + response.ReasonPhrase + "]");
    }
}
""", nativeCode: """
using System;
using System.Net;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        var response = new HttpResponseMessage((HttpStatusCode)404);
        response.ReasonPhrase = "Nothing Here";
        Console.WriteLine(response.ReasonPhrase);

        try { response.ReasonPhrase = "bad\nphrase"; Console.WriteLine("accepted"); }
        catch (Exception e) { Console.WriteLine(e.GetType().Name); }

        response.ReasonPhrase = null;
        Console.WriteLine("[" + response.ReasonPhrase + "]");
    }
}
""");
    }

    [TestMethod]
    public async Task EnsureSuccessStatusCodeMatchesDotNet()
    {
        await RunAndCompare("""
using System;
using System.Net;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        Console.WriteLine("200: " + (new HttpResponseMessage((HttpStatusCode)200, null).EnsureSuccessStatusCode() != null));

        foreach (var code in new[] { 300, 404, 500 })
        {
            try
            {
                new HttpResponseMessage((HttpStatusCode)code, null).EnsureSuccessStatusCode();
                Console.WriteLine(code + ": no throw");
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine(code + ": " + e.Message + " | status=" + e.StatusCode);
            }
        }
    }
}
""", nativeCode: """
using System;
using System.Net;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        Console.WriteLine("200: " + (new HttpResponseMessage((HttpStatusCode)200).EnsureSuccessStatusCode() != null));

        foreach (var code in new[] { 300, 404, 500 })
        {
            try
            {
                new HttpResponseMessage((HttpStatusCode)code).EnsureSuccessStatusCode();
                Console.WriteLine(code + ": no throw");
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine(code + ": " + e.Message + " | status=" + e.StatusCode);
            }
        }
    }
}
""");
    }

    /// <summary>
    /// <b>Divergence.</b> For a status with no reason phrase, .NET leaves the phrase out of the
    /// exception message entirely while the package writes an empty parenthesis. Cosmetic, but it is in
    /// a message that ends up in logs.
    /// </summary>
    [TestMethod]
    public async Task TheFailureMessageForAStatusWithNoReasonPhraseHasEmptyParentheses()
    {
        await RunJs("""
using System;
using System.Net;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        try { new HttpResponseMessage((HttpStatusCode)199, null).EnsureSuccessStatusCode(); }
        catch (HttpRequestException e) { Console.WriteLine(e.Message); }
    }
}
""", """
Response status code does not indicate success: 199 ().
""", nativePrints: """
Response status code does not indicate success: 199.
""", nativeCode: """
using System;
using System.Net;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        try { new HttpResponseMessage((HttpStatusCode)199).EnsureSuccessStatusCode(); }
        catch (HttpRequestException e) { Console.WriteLine(e.Message); }
    }
}
""");
    }

    [TestMethod]
    public async Task AnOutOfRangeStatusCodeIsRejected()
    {
        await RunAndCompare("""
using System;
using System.Net;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        try { var r = new HttpResponseMessage((HttpStatusCode)1000, null); Console.WriteLine("accepted"); }
        catch (Exception e) { Console.WriteLine(e.GetType().Name); }

        try { var r = new HttpResponseMessage((HttpStatusCode)(-1), null); Console.WriteLine("accepted"); }
        catch (Exception e) { Console.WriteLine(e.GetType().Name); }
    }
}
""", nativeCode: """
using System;
using System.Net;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        try { var r = new HttpResponseMessage((HttpStatusCode)1000); Console.WriteLine("accepted"); }
        catch (Exception e) { Console.WriteLine(e.GetType().Name); }

        try { var r = new HttpResponseMessage((HttpStatusCode)(-1)); Console.WriteLine("accepted"); }
        catch (Exception e) { Console.WriteLine(e.GetType().Name); }
    }
}
""");
    }

    /// <summary>
    /// A non-success status makes the typed reads throw, since they go through
    /// <c>EnsureSuccessStatusCode</c> — unlike <c>GetAsync</c>, which hands the response back.
    /// </summary>
    [TestMethod]
    public async Task GetStringAsyncThrowsForANonSuccessStatus()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("GET", "https://api.test/missing", 404, "not found");

        try
        {
            await new HttpClient().GetStringAsync("https://api.test/missing");
            Console.WriteLine("no throw");
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine(e.Message + " | status=" + e.StatusCode);
        }
    }
}
""", """
Response status code does not indicate success: 404 (Not Found). | status=NotFound
""");
    }

    /// <summary>
    /// <b>Divergence — bug.</b> A response the caller built (or one the handler produced with no
    /// content) exposes an <c>EmptyContent</c> whose underlying <c>XMLHttpRequest</c> is null, so
    /// reading it is a null dereference. .NET reads an empty body as the empty string.
    /// </summary>
    [TestMethod]
    public async Task ReadingContentWithNoRequestBehindItThrows()
    {
        await RunJs("""
using System;
using System.Net;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        var response = new HttpResponseMessage((HttpStatusCode)204, null);
        try { Console.WriteLine("[" + response.Content.ReadAsString() + "]"); }
        catch (Exception e) { Console.WriteLine(e.GetType().Name); }
    }
}
""", """
TypeError
""", nativePrints: """
[]
""", nativeCode: """
using System;
using System.Net;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        var response = new HttpResponseMessage((HttpStatusCode)204);
        Console.WriteLine("[" + response.Content.ReadAsStringAsync().Result + "]");
    }
}
""");
    }

    /// <summary>
    /// <b>Divergence.</b> A transport-level failure — a CORS rejection, DNS failure, or an offline
    /// browser, all of which surface as readyState 4 with status 0 — is handed back as a *response*
    /// with status 0 rather than raised as an <c>HttpRequestException</c>. So `IsSuccessStatusCode`
    /// is the only signal, and code that only catches <c>HttpRequestException</c> sees a response
    /// object it cannot make sense of. (Native .NET cannot be compared here: it has no
    /// status-0 response to produce.)
    /// </summary>
    [TestMethod]
    public async Task ATransportFailureBecomesAStatusZeroResponse()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.RouteNetworkError("GET", "https://api.test/dead");

        try
        {
            var response = await new HttpClient().GetAsync("https://api.test/dead");
            Console.WriteLine("status: " + (int)response.StatusCode);
            Console.WriteLine("success: " + response.IsSuccessStatusCode);
            Console.WriteLine("reason: [" + response.ReasonPhrase + "]");
        }
        catch (Exception e)
        {
            Console.WriteLine("threw " + e.GetType().Name);
        }
    }
}
""", """
status: 0
success: False
reason: []
""");
    }
}
