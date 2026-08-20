namespace Transpose.HttpClient.Tests;

/// <summary>
/// What the package puts on the wire for each shorthand, how it resolves a URI against
/// <c>BaseAddress</c>, and the state machine around a request message and a client.
/// </summary>
[TestClass]
public class RequestTests : HttpClientTestBase
{
    [TestMethod]
    public async Task EachShorthandSendsItsVerb()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("*", "*", 200, "ok");

        var client = new HttpClient();
        await client.GetAsync("https://api.test/r");
        await client.PostAsync("https://api.test/r", new StringContent("p"));
        await client.PutAsync("https://api.test/r", new StringContent("u"));
        await client.PatchAsync("https://api.test/r", new StringContent("a"));
        await client.DeleteAsync("https://api.test/r");

        for (var i = 0; i < Xhr.RequestCount(); i++)
        {
            Console.WriteLine(Xhr.RequestMethod(i) + " body=" + Xhr.RequestBody(i));
        }
    }
}
""", """
GET body=(none)
POST body=p
PUT body=u
PATCH body=a
DELETE body=(none)
""");
    }

    [TestMethod]
    public async Task AnAbsoluteUriIsSentAsGiven()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("*", "*", 200, "ok");

        var client = new HttpClient();
        client.BaseAddress = new Uri("https://base.test/");

        await client.GetAsync("https://other.test/thing?q=1");
        Console.WriteLine(Xhr.RequestUrl(0));
    }
}
""", """
https://other.test/thing?q=1
""");
    }

    /// <summary>
    /// <b>Divergence.</b> A relative URI is resolved by string concatenation
    /// (<c>BaseAddress.ToString() + uri</c>) rather than by <c>Uri</c> combination. For the ordinary
    /// shape — a base ending in "/" and a relative path that does not start with one — the two agree.
    /// They part company as soon as either slash convention differs: .NET treats a leading "/" as
    /// root-relative and a base with no trailing "/" as naming a resource, and both rules drop path
    /// segments the concatenation keeps.
    /// </summary>
    [TestMethod]
    public async Task ARelativeUriIsAppendedToTheBaseAddress()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("*", "*", 200, "ok");

        await Send("https://api.test/v1/", "things");
        await Send("https://api.test/v1/", "/things");
        await Send("https://api.test/v1", "things");
    }

    static async Task Send(string baseAddress, string relative)
    {
        var client = new HttpClient();
        client.BaseAddress = new Uri(baseAddress);
        await client.GetAsync(relative);
        Console.WriteLine(baseAddress + " + " + relative + " -> " + Xhr.RequestUrl(Xhr.RequestCount() - 1));
    }
}
""", """
https://api.test/v1/ + things -> https://api.test/v1/things
https://api.test/v1/ + /things -> https://api.test/v1//things
https://api.test/v1 + things -> https://api.test/v1things
""", nativePrints: """
https://api.test/v1/ + things -> https://api.test/v1/things
https://api.test/v1/ + /things -> https://api.test/things
https://api.test/v1 + things -> https://api.test/things
""", nativeCode: """
using System;

public class Program
{
    public static void Main()
    {
        Show("https://api.test/v1/", "things");
        Show("https://api.test/v1/", "/things");
        Show("https://api.test/v1", "things");
    }

    static void Show(string baseAddress, string relative)
    {
        Console.WriteLine(baseAddress + " + " + relative + " -> " + new Uri(new Uri(baseAddress), relative));
    }
}
""");
    }

    [TestMethod]
    public async Task ARelativeUriWithNoBaseAddressThrows()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        try
        {
            await new HttpClient().GetAsync("relative/path");
            Console.WriteLine("sent");
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine(e.Message);
        }
        Console.WriteLine("requests: " + Xhr.RequestCount());
    }
}
""", """
An invalid request URI was provided. Either the request URI must be an absolute URI or BaseAddress must be set.
requests: 0
""");
    }

    [TestMethod]
    public async Task ARelativeBaseAddressIsRejected()
    {
        await RunJs("""
using System;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        try
        {
            new HttpClient().BaseAddress = new Uri("v1/");
            Console.WriteLine("accepted");
        }
        catch (ArgumentException e)
        {
            // .NET appends a "Parameter name:" line; only the message itself is being pinned here.
            Console.WriteLine("ArgumentException: " + e.Message.Split('\n')[0].Trim());
        }
    }
}
""", """
ArgumentException: The base address must be an absolute URI.
""");
    }

    /// <summary>A request message carries one send; the second is refused, as in .NET.</summary>
    [TestMethod]
    public async Task ARequestMessageCannotBeSentTwice()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("*", "*", 200, "ok");

        var client = new HttpClient();
        var message = new HttpRequestMessage(HttpMethod.Get, "https://api.test/x");

        await client.SendAsync(message);
        try
        {
            await client.SendAsync(message);
            Console.WriteLine("sent twice");
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine(e.Message);
        }
        Console.WriteLine("requests: " + Xhr.RequestCount());
    }
}
""", """
The request message was already sent. Cannot send the same request message multiple times.
requests: 1
""");
    }

    /// <summary>Properties lock down once the client has started a request, as in .NET.</summary>
    [TestMethod]
    public async Task PropertiesLockDownAfterTheFirstRequest()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("*", "*", 200, "ok");

        var client = new HttpClient();
        await client.GetAsync("https://api.test/x");

        try { client.BaseAddress = new Uri("https://other.test/"); Console.WriteLine("base set"); }
        catch (InvalidOperationException) { Console.WriteLine("base: InvalidOperationException"); }

        try { client.Timeout = TimeSpan.FromSeconds(5); Console.WriteLine("timeout set"); }
        catch (InvalidOperationException) { Console.WriteLine("timeout: InvalidOperationException"); }
    }
}
""", """
base: InvalidOperationException
timeout: InvalidOperationException
""");
    }

    [TestMethod]
    public async Task UsingADisposedClientThrows()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("*", "*", 200, "ok");

        var client = new HttpClient();
        client.Dispose();

        try
        {
            await client.GetAsync("https://api.test/x");
            Console.WriteLine("sent");
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine("ObjectDisposedException");
        }
        Console.WriteLine("requests: " + Xhr.RequestCount());
    }
}
""", """
ObjectDisposedException
requests: 0
""");
    }

    [TestMethod]
    public async Task TimeoutIsValidatedLikeDotNet()
    {
        await RunAndCompare("""
using System;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        Console.WriteLine("default: " + new HttpClient().Timeout);

        foreach (var seconds in new[] { -5, 0, 30 })
        {
            try
            {
                var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(seconds);
                Console.WriteLine(seconds + ": " + client.Timeout);
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine(seconds + ": ArgumentOutOfRangeException");
            }
        }

        // An infinite timeout is accepted where any other non-positive value is refused. Compared as a
        // value rather than as text: formatting a negative TimeSpan drops its fractional part in the
        // Transpose BCL, which is a runtime bug of its own and not this package's business.
        var infinite = new HttpClient();
        infinite.Timeout = TimeSpan.FromMilliseconds(-1);
        Console.WriteLine("infinite: " + (infinite.Timeout == TimeSpan.FromMilliseconds(-1)));
    }
}
""");
    }

    /// <summary>
    /// <c>HttpRequestOptions</c>, the typed option bag. This one is a straight port, so it is compared
    /// against .NET.
    /// </summary>
    [TestMethod]
    public async Task RequestOptionsRoundTrip()
    {
        await RunAndCompare("""
using System;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        var message = new HttpRequestMessage();
        var name = new HttpRequestOptionsKey<string>("name");
        var count = new HttpRequestOptionsKey<int>("count");
        var missing = new HttpRequestOptionsKey<string>("missing");

        message.Options.Set(name, "widget");
        message.Options.Set(count, 3);

        Console.WriteLine("name: " + (message.Options.TryGetValue(name, out var n) ? n : "(none)"));
        Console.WriteLine("count: " + (message.Options.TryGetValue(count, out var c) ? c.ToString() : "(none)"));
        Console.WriteLine("missing: " + (message.Options.TryGetValue(missing, out var m) ? m : "(none)"));

        // A key of the wrong type reads as absent rather than throwing.
        Console.WriteLine("wrongType: " + (message.Options.TryGetValue(new HttpRequestOptionsKey<int>("name"), out var w) ? w.ToString() : "(none)"));

        message.Options.Set(name, "gadget");
        Console.WriteLine("overwritten: " + (message.Options.TryGetValue(name, out var o) ? o : "(none)"));
    }
}
""");
    }

    /// <summary>
    /// <b>Divergence.</b> <c>HttpRequestMessage.Version</c> starts as null; .NET defaults it to 1.1.
    /// Nothing in a browser can act on it either way — XHR chooses the protocol — but a caller that
    /// prints or compares it sees the difference.
    /// </summary>
    [TestMethod]
    public async Task VersionStartsUnset()
    {
        await RunJs("""
using System;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        var message = new HttpRequestMessage();
        Console.WriteLine("version: " + (message.Version == null ? "(null)" : message.Version.ToString()));
        Console.WriteLine("method: " + message.Method);
        Console.WriteLine("uri: " + (message.RequestUri == null ? "(null)" : message.RequestUri.ToString()));
    }
}
""", """
version: (null)
method: GET
uri: (null)
""", nativePrints: """
version: 1.1
method: GET
uri: (null)
""");
    }
}
