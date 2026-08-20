namespace Transpose.HttpClient.Tests;

/// <summary>
/// Redirects.
///
/// In a real browser this path barely runs: XHR follows a 3xx itself and never surfaces the
/// intermediate response, so a caller only sees <c>status == 302</c> where the browser could not follow.
/// It is reachable nonetheless, and it used to be broken in three separate ways — the redirect branch
/// fell through to the ordinary "hand the response back" path (so the response it went on to fetch was
/// computed and dropped), the <c>Location</c> header was read into a local and never used, and
/// <c>AllowAutoRedirect</c> was forwarded to the handler and never read.
/// </summary>
[TestClass]
public class RedirectTests : HttpClientTestBase
{
    [TestMethod]
    public async Task A302IsFollowedToItsLocation()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("GET", "https://api.test/moved", 302, "", "Location: https://api.test/final");
        Xhr.Route("GET", "https://api.test/final", 200, "arrived");

        var response = await new HttpClient().GetAsync("https://api.test/moved");

        Console.WriteLine("status: " + (int)response.StatusCode);
        Console.WriteLine("body: " + response.Content.ReadAsString());
        Console.WriteLine("urls requested:");
        for (var i = 0; i < Xhr.RequestCount(); i++) Console.WriteLine("  " + Xhr.RequestUrl(i));
    }
}
""", """
status: 200
body: arrived
urls requested:
  https://api.test/moved
  https://api.test/final
""");
    }

    /// <summary>Every status the web treats as a redirect is followed; a 304 is not one of them.</summary>
    [TestMethod]
    public async Task EveryRedirectStatusIsFollowed()
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
        Xhr.Route("GET", "https://api.test/final", 200, "arrived");

        foreach (var status in new[] { 301, 302, 303, 307, 308, 304 })
        {
            Xhr.Route("GET", "https://api.test/from", status, "", "Location: https://api.test/final");

            var response = await new HttpClient().GetAsync("https://api.test/from");
            Console.WriteLine(status + " -> " + (int)response.StatusCode);
        }
    }
}
""", """
301 -> 200
302 -> 200
303 -> 200
307 -> 200
308 -> 200
304 -> 304
""");
    }

    /// <summary>A relative <c>Location</c> resolves against the URI it was returned for.</summary>
    [TestMethod]
    public async Task ARelativeLocationIsResolved()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("GET", "*", 200, "arrived");

        await Follow("https://api.test/v1/old", "new");        // sibling
        await Follow("https://api.test/v1/old", "/root");      // root-relative
        await Follow("https://api.test/v1/old", "//other.test/x"); // protocol-relative
        await Follow("https://api.test/v1/old", "https://other.test/x"); // absolute
    }

    static async Task Follow(string from, string location)
    {
        Xhr.Route("GET", from, 302, "", "Location: " + location);
        await new HttpClient().GetAsync(from);
        Console.WriteLine(location + " -> " + Xhr.RequestUrl(Xhr.RequestCount() - 1));
    }
}
""", """
new -> https://api.test/v1/new
/root -> https://api.test/root
//other.test/x -> https://other.test/x
https://other.test/x -> https://other.test/x
""");
    }

    /// <summary>
    /// A 303 becomes a GET, and so does a 301/302 that carried a body — what every client and server on
    /// the web has settled on. A 307 is the status that exists to keep the method and body.
    /// </summary>
    [TestMethod]
    public async Task ARedirectedPostBecomesAGetExceptFor307()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("*", "https://api.test/final", 200, "arrived");

        foreach (var status in new[] { 302, 303, 307 })
        {
            Xhr.Reset();
            Xhr.Route("*", "https://api.test/final", 200, "arrived");
            Xhr.Route("POST", "https://api.test/from", status, "", "Location: https://api.test/final");

            await new HttpClient().PostAsync("https://api.test/from", new StringContent("payload"));

            Console.WriteLine(status + " -> " + Xhr.RequestMethod(1) + " body=" + Xhr.RequestBody(1));
        }
    }
}
""", """
302 -> GET body=(none)
303 -> GET body=(none)
307 -> POST body=payload
""");
    }

    [TestMethod]
    public async Task AllowAutoRedirectFalseHandsBackTheRedirect()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("GET", "https://api.test/moved", 302, "", "Location: https://api.test/final");
        Xhr.Route("GET", "https://api.test/final", 200, "arrived");

        var handler = new HttpClientHandler();
        handler.AllowAutoRedirect = false;

        var response = await new HttpClient(handler).GetAsync("https://api.test/moved");

        Console.WriteLine("status: " + (int)response.StatusCode);
        Console.WriteLine("requests: " + Xhr.RequestCount());
    }
}
""", """
status: 302
requests: 1
""");
    }

    /// <summary>A redirect loop stops at the budget and hands back the last 3xx.</summary>
    [TestMethod]
    public async Task ARedirectLoopStopsAtTheLimit()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("GET", "https://api.test/a", 302, "", "Location: https://api.test/b");
        Xhr.Route("GET", "https://api.test/b", 302, "", "Location: https://api.test/a");

        var handler = new HttpClientHandler();
        handler.MaxAutomaticRedirections = 3;

        var response = await new HttpClient(handler).GetAsync("https://api.test/a");

        Console.WriteLine("status: " + (int)response.StatusCode);
        Console.WriteLine("requests: " + Xhr.RequestCount());
    }
}
""", """
status: 302
requests: 4
""");
    }

    /// <summary>A 3xx with no <c>Location</c> has nowhere to go, so it is simply the response.</summary>
    [TestMethod]
    public async Task ARedirectWithNoLocationIsReturned()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("GET", "https://api.test/moved", 302, "no location here");

        var response = await new HttpClient().GetAsync("https://api.test/moved");

        Console.WriteLine("status: " + (int)response.StatusCode);
        Console.WriteLine("body: " + response.Content.ReadAsString());
        Console.WriteLine("requests: " + Xhr.RequestCount());
    }
}
""", """
status: 302
body: no location here
requests: 1
""");
    }
}
