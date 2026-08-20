namespace Transpose.HttpClient.Tests;

/// <summary>
/// The suite's floor: the harness itself works, and the shortest useful request/response round trip
/// does what a browser application expects.
/// </summary>
[TestClass]
public class SmokeTests : HttpClientTestBase
{
    [TestMethod]
    public async Task GetStringAsyncReturnsTheBody()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("GET", "https://api.test/hello", 200, "hello world");

        var client = new HttpClient();
        var body = await client.GetStringAsync("https://api.test/hello");

        Console.WriteLine("body: " + body);
        Console.WriteLine("requests: " + Xhr.RequestCount());
        Console.WriteLine("method: " + Xhr.RequestMethod(0));
        Console.WriteLine("url: " + Xhr.RequestUrl(0));
    }
}
""", """
body: hello world
requests: 1
method: GET
url: https://api.test/hello
""");
    }

    [TestMethod]
    public async Task GetAsyncExposesStatusAndBody()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("GET", "https://api.test/thing", 201, "created");

        var client = new HttpClient();
        var response = await client.GetAsync("https://api.test/thing");

        Console.WriteLine("status: " + (int)response.StatusCode);
        Console.WriteLine("statusName: " + response.StatusCode);
        Console.WriteLine("reason: " + response.ReasonPhrase);
        Console.WriteLine("success: " + response.IsSuccessStatusCode);
        Console.WriteLine("body: " + response.Content.ReadAsString());
    }
}
""", """
status: 201
statusName: Created
reason: Created
success: True
body: created
""");
    }

    [TestMethod]
    public async Task PostAsyncSendsTheBody()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("POST", "https://api.test/items", 200, "ok");

        var client = new HttpClient();
        var response = await client.PostAsync("https://api.test/items", new StringContent("{\"a\":1}", "application/json"));

        Console.WriteLine("status: " + (int)response.StatusCode);
        Console.WriteLine("sent: " + Xhr.RequestBody(0));
        Console.WriteLine("method: " + Xhr.RequestMethod(0));
    }
}
""", """
status: 200
sent: {"a":1}
method: POST
""");
    }
}
