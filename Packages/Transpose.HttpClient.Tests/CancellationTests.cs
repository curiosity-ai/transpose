namespace Transpose.HttpClient.Tests;

/// <summary>
/// Cancellation. A browser cancels a request by calling <c>abort()</c> on the XHR, which the package
/// wires to the linked token — so these tests check both that the task faults and that the transport
/// was actually told to stop.
/// </summary>
[TestClass]
public class CancellationTests : HttpClientTestBase
{
    [TestMethod]
    public async Task CancellingMidFlightAbortsTheRequest()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("GET", "https://api.test/slow", 200, "too late");

        var source = new CancellationTokenSource();
        var task = new HttpClient().GetAsync("https://api.test/slow", source.Token);
        source.Cancel();

        try
        {
            await task;
            Console.WriteLine("completed");
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("TaskCanceledException");
        }

        Console.WriteLine("aborted: " + Xhr.Aborted(0));
    }
}
""", """
TaskCanceledException
aborted: True
""");
    }

    [TestMethod]
    public async Task CancelPendingRequestsCancelsInFlightRequests()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("GET", "https://api.test/slow", 200, "too late");

        var client = new HttpClient();
        var task = client.GetAsync("https://api.test/slow");
        client.CancelPendingRequests();

        try
        {
            await task;
            Console.WriteLine("completed");
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("TaskCanceledException");
        }

        Console.WriteLine("aborted: " + Xhr.Aborted(0));
    }
}
""", """
TaskCanceledException
aborted: True
""");
    }

    /// <summary>
    /// A client keeps working after <c>CancelPendingRequests</c> — it swaps in a fresh source rather
    /// than cancelling itself for good.
    /// </summary>
    [TestMethod]
    public async Task AClientIsStillUsableAfterCancelPendingRequests()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("GET", "https://api.test/x", 200, "second");

        var client = new HttpClient();
        client.CancelPendingRequests();

        var response = await client.GetAsync("https://api.test/x");
        Console.WriteLine("body: " + response.Content.ReadAsString());
    }
}
""", """
body: second
""");
    }

    /// <summary>
    /// A token that is <i>already</i> cancelled when the request starts cancels like any other, and
    /// nothing reaches the transport. It used to produce a raw JavaScript error: the request was
    /// registered on the cancelled token, which runs its callback synchronously inside
    /// <c>CancellationToken.Register</c>, and that callback disposed the very
    /// <c>CancellationTokenSource</c> whose registration list <c>Register</c> was still appending to.
    /// Passing an already-cancelled token is what a component does when it re-fires a request after its
    /// own scope was torn down, so this was reachable in ordinary code.
    /// </summary>
    [TestMethod]
    public async Task AnAlreadyCancelledTokenCancels()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("GET", "https://api.test/x", 200, "ok");

        var source = new CancellationTokenSource();
        source.Cancel();

        try
        {
            await new HttpClient().GetAsync("https://api.test/x", source.Token);
            Console.WriteLine("completed");
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("TaskCanceledException");
        }
        catch (Exception e)
        {
            Console.WriteLine("not a cancellation: " + e.GetType().Name);
        }

        Console.WriteLine("requests: " + Xhr.RequestCount());
    }
}
""", """
TaskCanceledException
requests: 0
""", nativePrints: """
TaskCanceledException
""", nativeCode: """
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        var source = new CancellationTokenSource();
        source.Cancel();

        try
        {
            await new HttpClient().GetAsync("https://api.test/x", source.Token);
            Console.WriteLine("completed");
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("TaskCanceledException");
        }
        catch (Exception e)
        {
            Console.WriteLine("not a cancellation: " + e.GetType().Name);
        }
    }
}
""");
    }
}
