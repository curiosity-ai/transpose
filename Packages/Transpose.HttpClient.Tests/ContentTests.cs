namespace Transpose.HttpClient.Tests;

/// <summary>
/// The content types the package can send and the four ways it can read a response body.
///
/// A browser decides how to decode a response from the <c>responseType</c> set on the request before
/// it is sent, which is what <c>GetStringAsync</c>/<c>GetByteArrayAsync</c>/<c>GetBlobAsync</c>/
/// <c>GetObjectLiteralAsync</c> are choosing between — so the tests here assert on the request that
/// went out as much as on the value that came back.
/// </summary>
[TestClass]
public class ContentTests : HttpClientTestBase
{
    [TestMethod]
    public async Task StringContentIsSentAsTheBody()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("POST", "https://api.test/items", 201, "created");

        var content = new StringContent("{\"name\":\"widget\"}", "application/json");
        Console.WriteLine("mediaType: " + content.MediaType);
        Console.WriteLine("content: " + content.Content);

        var response = await new HttpClient().PostAsync("https://api.test/items", content);

        Console.WriteLine("sent: " + Xhr.RequestBody(0));
        Console.WriteLine("status: " + (int)response.StatusCode);
        Console.WriteLine("body: " + response.Content.ReadAsString());
    }
}
""", """
mediaType: application/json
content: {"name":"widget"}
sent: {"name":"widget"}
status: 201
body: created
""");
    }

    [TestMethod]
    public async Task StringContentDefaultsToTextPlain()
    {
        await RunJs("""
using System;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new StringContent("hi").MediaType);
    }
}
""", """
text/plain
""");
    }

    /// <summary>
    /// <b>Bug.</b> <c>StringContent</c> keeps its media type in a <c>MediaType</c> property and never
    /// turns it into a <c>Content-Type</c> header, so a JSON POST goes out with no content type at all
    /// and a server that dispatches on it rejects the request. This is not the simplified header model
    /// talking — the request has a header collection, the value simply never gets put in it. The
    /// workaround today is to set it on the request message by hand
    /// (<see cref="AContentTypeSetOnTheRequestIsSent"/>), which does work.
    /// </summary>
    [TestMethod]
    public async Task StringContentSendsNoContentTypeHeader()
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

        await new HttpClient().PostAsync("https://api.test/items", new StringContent("{}", "application/json"));

        Console.WriteLine("headers: [" + Xhr.RequestHeaders(0) + "]");
        Console.WriteLine("content-type: " + Xhr.RequestHeader(0, "Content-Type"));
    }
}
""", """
headers: []
content-type: (absent)
""");
    }

    [TestMethod]
    public async Task AContentTypeSetOnTheRequestIsSent()
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

        var message = new HttpRequestMessage(HttpMethod.Post, "https://api.test/items");
        message.Headers.Add("Content-Type", "application/json");
        message.Content = new StringContent("{}");

        await new HttpClient().SendAsync(message);

        Console.WriteLine("content-type: " + Xhr.RequestHeader(0, "Content-Type"));
    }
}
""", """
content-type: application/json
""");
    }

    [TestMethod]
    public async Task ARequestWithNoContentSendsNoBody()
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
        await client.PostAsync("https://api.test/x", null);

        Console.WriteLine("get: " + Xhr.RequestBody(0));
        Console.WriteLine("post(null): " + Xhr.RequestBody(1));
    }
}
""", """
get: (none)
post(null): (none)
""");
    }

    /// <summary>
    /// <c>GetObjectLiteralAsync&lt;T&gt;</c> hands back the browser's parsed JSON as a
    /// <c>[ObjectLiteral]</c>-shaped value — the package's answer to "deserialize this response"
    /// without a serializer in the bundle.
    /// </summary>
    [TestMethod]
    public async Task GetObjectLiteralAsyncReadsTheParsedResponse()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Transpose;

[ObjectLiteral]
public class Widget
{
    public string name;
    public int count;
}

public class Program
{
    public static async Task Main()
    {
        Xhr.RouteJson("GET", "https://api.test/widget", 200, "{\"name\":\"bolt\",\"count\":7}");

        var widget = await new HttpClient().GetObjectLiteralAsync<Widget>("https://api.test/widget");

        Console.WriteLine("name: " + widget.name);
        Console.WriteLine("count: " + widget.count);
    }
}
""", """
name: bolt
count: 7
""");
    }

    /// <summary>
    /// <b>Bug.</b> The typed reads set <c>HttpRequestMessage.ResponseType</c>, but nothing ever copies
    /// it onto the <c>XMLHttpRequest</c> — <c>BrowserHttpHandler</c> does not read the property at all.
    /// So every request goes out with the default responseType and the browser decodes every body as
    /// text: <c>GetByteArrayAsync</c> and <c>GetBlobAsync</c> return the response *string*, cast to
    /// <c>ArrayBuffer</c>/<c>Blob</c>, and <c>GetObjectLiteralAsync</c> works only because a JSON
    /// string happens to be indexable in the shapes people test with. The test above passes for that
    /// reason and not because the plumbing is right.
    /// </summary>
    [TestMethod]
    public async Task TheResponseTypeIsNeverAppliedToTheRequest()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("GET", "https://api.test/text", 200, "plain");
        Xhr.RouteJson("GET", "https://api.test/json", 200, "{}");
        Xhr.Route("GET", "https://api.test/bytes", 200, "binary");

        var client = new HttpClient();
        await client.GetStringAsync("https://api.test/text");
        await client.GetObjectLiteralAsync<object>("https://api.test/json");
        await client.GetByteArrayAsync("https://api.test/bytes");

        Console.WriteLine("string: " + Xhr.RequestResponseType(0));
        Console.WriteLine("json: " + Xhr.RequestResponseType(1));
        Console.WriteLine("bytes: " + Xhr.RequestResponseType(2));
    }
}
""", """
string: (default)
json: (default)
bytes: (default)
""");
    }

    /// <summary>A <c>FormData</c> body is passed to <c>send()</c> untouched, as a browser requires.</summary>
    [TestMethod]
    public async Task FormContentIsSentAsTheFormDataObject()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Transpose.Core;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("POST", "https://api.test/upload", 200, "ok");

        var form = new dom.FormData();
        form.append("name", "widget");
        form.append("qty", "2");

        await new HttpClient().PostAsync("https://api.test/upload", new FormContent(form));

        Console.WriteLine("sent: " + Xhr.RequestBody(0));
    }
}
""", """
sent: FormData(name=widget&qty=2)
""");
    }
}
