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
    /// The media type reaches the wire as a <c>Content-Type</c> header. It used to live in the
    /// <c>MediaType</c> property and nowhere else, so a JSON POST went out with no content type and any
    /// server that dispatches on one rejected it.
    ///
    /// <b>Divergence:</b> no <c>; charset=utf-8</c> is appended, where .NET writes
    /// <c>text/plain; charset=utf-8</c>. The body is handed to <c>XMLHttpRequest.send</c> as a string,
    /// which a browser encodes as UTF-8 and says so itself.
    /// </summary>
    [TestMethod]
    public async Task StringContentSendsItsMediaTypeAsContentType()
    {
        await RunJs("""
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        Xhr.Route("POST", "*", 200, "ok");

        var client = new HttpClient();
        await client.PostAsync("https://api.test/json", new StringContent("{}", "application/json"));
        await client.PostAsync("https://api.test/text", new StringContent("hi"));

        Console.WriteLine("json: " + Xhr.RequestHeader(0, "Content-Type"));
        Console.WriteLine("default: " + Xhr.RequestHeader(1, "Content-Type"));
    }
}
""", """
json: application/json
default: text/plain
""", nativePrints: """
json: application/json; charset=utf-8
default: text/plain; charset=utf-8
""", nativeCode: """
using System;
using System.Linq;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        Console.WriteLine("json: " + new StringContent("{}", System.Text.Encoding.UTF8, "application/json").Headers.ContentType);
        Console.WriteLine("default: " + new StringContent("hi").Headers.ContentType);
    }
}
""");
    }

    /// <summary>
    /// A <c>Content-Type</c> the caller set on the request message itself wins over the one its content
    /// contributes — the same "already present, leave it alone" rule the client's default headers follow.
    /// </summary>
    [TestMethod]
    public async Task AContentTypeOnTheRequestWinsOverTheContentsOwn()
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
        message.Headers.Add("Content-Type", "application/vnd.api+json");
        message.Content = new StringContent("{}", "application/json");

        await new HttpClient().SendAsync(message);

        Console.WriteLine("content-type: " + Xhr.RequestHeader(0, "Content-Type"));
    }
}
""", """
content-type: application/vnd.api+json
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
    /// without a serializer in the bundle. It asks the transport for <c>responseType: "json"</c>, so
    /// the parsing is the browser's (see <see cref="EachTypedReadSetsItsResponseType"/>).
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
    /// Each typed read declares how the browser should decode the body, and the handler applies it to
    /// the <c>XMLHttpRequest</c> before sending. Nothing used to copy it across, so every body came
    /// back as text however it was asked for — <c>GetByteArrayAsync</c> and <c>GetBlobAsync</c>
    /// returned the response *string* cast to <c>ArrayBuffer</c>/<c>Blob</c>.
    /// </summary>
    [TestMethod]
    public async Task EachTypedReadSetsItsResponseType()
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

        Xhr.Route("GET", "https://api.test/plain", 200, "plain");

        var client = new HttpClient();
        await client.GetStringAsync("https://api.test/text");
        await client.GetObjectLiteralAsync<object>("https://api.test/json");
        await client.GetByteArrayAsync("https://api.test/bytes");
        await client.GetAsync("https://api.test/plain");

        Console.WriteLine("string: " + Xhr.RequestResponseType(0));
        Console.WriteLine("json: " + Xhr.RequestResponseType(1));
        Console.WriteLine("bytes: " + Xhr.RequestResponseType(2));
        // GetAsync makes no claim about the body, so the transport keeps its default.
        Console.WriteLine("untyped: " + Xhr.RequestResponseType(3));
    }
}
""", """
string: text
json: json
bytes: arraybuffer
untyped: (default)
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
