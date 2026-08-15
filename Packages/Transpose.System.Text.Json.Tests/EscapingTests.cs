namespace Transpose.SystemTextJson.Tests;

/// <summary>
/// System.Text.Json escapes far more than JSON requires — everything outside a short allow-list of
/// basic-latin characters — which is what makes its output safe to embed in an HTML
/// <c>&lt;script&gt;</c> block. <c>JSON.stringify</c> escapes the bare minimum, so this is the area
/// where a browser implementation is most likely to drift, and every rule is pinned here.
/// </summary>
[TestClass]
public sealed class EscapingTests : JsonTestBase
{
    private const string Harness = """
        using System;
        using System.Collections.Generic;
        using System.Text.Json;

        public static class Program
        {
            static void P(string s) => Console.WriteLine(JsonSerializer.Serialize(s));

            public static void Main()
            {
        __BODY__
            }
        }
        """;

    private static string With(string body) => Harness.Replace("__BODY__", body);

    [TestMethod]
    public async Task HtmlSensitiveCharactersAreEscaped() => await RunAndCompare(With("""
                P("a+b");
                P("<script>");
                P("a&b");
                P("it's");
                P("q\"q");
                P("back\\slash");
                P("tick`tick");
        """));

    [TestMethod]
    public async Task TheSafePunctuationAllowListPassesThrough() => await RunAndCompare(With("""
                P(" !#$%()*,-./:;=?@[]^_{|}~");
                P("abcXYZ0189");
        """));

    [TestMethod]
    public async Task ControlCharactersUseTheirShortEscapesWhereJsonHasOne() => await RunAndCompare(With("""
                P("a\nb");
                P("a\tb");
                P("a\rb");
                P("a\bb");
                P("a\fb");
                P("ab");
                P("ab");
        """));

    [TestMethod]
    public async Task NonAsciiIsEscapedAsUppercaseHex() => await RunAndCompare(With("""
                P("café");
                P("—");
                P("ünïcode");
                P("");
                P("");
                P("日本語");
        """));

    [TestMethod]
    public async Task ASurrogatePairIsEscapedAsTwoUnits() => await RunAndCompare(With("""
                P("x\U0001F600y");
        """));

    [TestMethod]
    public async Task MemberNamesAreEscapedTheSameWay() => await RunAndCompare("""
        using System;
        using System.Collections.Generic;
        using System.Text.Json;

        public static class Program
        {
            public static void Main()
            {
                var map = new Dictionary<string, int> { ["a+b<c>"] = 1, ["plain"] = 2, ["é"] = 3 };
                Console.WriteLine(JsonSerializer.Serialize(map));
            }
        }
        """);

    [TestMethod]
    public async Task AnEscapedPayloadRoundTrips() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string Value { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var original = "a+b <script> café 日本語 \"quoted\" \\ back\nnewline";
                var json     = JsonSerializer.Serialize(new T { Value = original });
                var back     = JsonSerializer.Deserialize<T>(json);

                Console.WriteLine(json);
                Console.WriteLine(back.Value == original);
            }
        }
        """);

    [TestMethod]
    public async Task AnEscapedDocumentIsSafeToEmbedInAScriptBlock() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string Payload { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                // The self-contained page exports drop a serialized model straight into a <script>
                // block, so no '<' may survive into the output.
                var json = JsonSerializer.Serialize(new T { Payload = "</script><script>alert(1)</script>" });

                Console.WriteLine(json);
                Console.WriteLine(json.IndexOf('<') < 0);
            }
        }
        """);

    [TestMethod]
    public async Task EscapesSurviveIndentedOutputToo() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string A { get; set; } }

        public static class Program
        {
            public static void Main()
                => Console.WriteLine(JsonSerializer.Serialize(new T { A = "x+y<z>é" }, new JsonSerializerOptions { WriteIndented = true }));
        }
        """);

    [TestMethod]
    public async Task ReadingUnescapesEveryForm() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string A { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var json = "{\"A\":\"\\u002B\\u003C\\u0026\\u00E9\\n\\t\\\"\\\\\\/\"}";
                var back = JsonSerializer.Deserialize<T>(json);

                Console.WriteLine(back.A.Length);
                Console.WriteLine(JsonSerializer.Serialize(back));
            }
        }
        """);
}
