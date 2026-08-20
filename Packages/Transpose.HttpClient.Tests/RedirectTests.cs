namespace Transpose.HttpClient.Tests;

/// <summary>
/// Redirects.
///
/// Note first that in a real browser this code path barely runs: XHR follows a 3xx itself and never
/// surfaces the intermediate response, so <c>status == 302</c> only happens where the browser cannot
/// follow (and then it usually reports a network error instead). The path exists nonetheless, it is
/// reachable, and it is broken in three separate ways — recorded here so a fix has something to turn
/// green.
/// </summary>
[TestClass]
public class RedirectTests : HttpClientTestBase
{
    /// <summary>
    /// <b>Bug.</b> A 302 is handed back to the caller as the final response even though the handler
    /// went on to re-send the request. Three things are wrong in <c>BrowserHttpHandler.SendAsync</c>:
    /// <list type="number">
    ///   <item>the redirect branch falls through to the ordinary
    ///     <c>tcs.TrySetResult(httpResponse)</c> below it, and since the re-send is asynchronous that
    ///     synchronous set always wins — the followed response is computed and then dropped;</item>
    ///   <item>the <c>Location</c> header is read into a local and never used, so the re-send goes to
    ///     the same URL — with the fall-through fixed this would recurse to the redirect limit rather
    ///     than arrive anywhere;</item>
    ///   <item>the re-send reuses the request message's single <c>XMLHttpRequest</c>, so the
    ///     in-flight response is being read from the object the retry is re-opening.</item>
    /// </list>
    /// The observable result: the caller gets 302 with an empty body, and the request count shows the
    /// wasted follow-up.
    /// </summary>
    [TestMethod]
    public async Task A302IsReturnedToTheCallerInsteadOfBeingFollowed()
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
        Console.WriteLine("body: [" + response.Content.ReadAsString() + "]");
        Console.WriteLine("urls requested:");
        for (var i = 0; i < Xhr.RequestCount(); i++) Console.WriteLine("  " + Xhr.RequestUrl(i));
    }
}
""", """
status: 302
body: []
urls requested:
  https://api.test/moved
""");
    }

    /// <summary>
    /// <b>Bug.</b> <c>HttpClientHandler.AllowAutoRedirect</c> is settable and forwarded to
    /// <c>BrowserHttpHandler</c>, which never reads it: the redirect path is taken whenever the
    /// redirect budget is left, so turning it off changes nothing. (Both settings produce the same
    /// broken result above, which is what this pins.)
    /// </summary>
    [TestMethod]
    public async Task AllowAutoRedirectIsIgnored()
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

        var handler = new HttpClientHandler();
        handler.AllowAutoRedirect = false;
        Console.WriteLine("allowAutoRedirect: " + handler.AllowAutoRedirect);

        var response = await new HttpClient(handler).GetAsync("https://api.test/moved");

        Console.WriteLine("status: " + (int)response.StatusCode);
        Console.WriteLine("requests: " + Xhr.RequestCount());
    }
}
""", """
allowAutoRedirect: False
status: 302
requests: 1
""");
    }
}
