using System;
using System.Threading.Tasks;

namespace Transpose.SystemTextJson.Tests;

/// <summary>
/// Runs one program twice in the browser: once against <c>Transpose.Newtonsoft.Json</c> and once
/// against <c>Transpose.System.Text.Json</c>, so a migration can be judged call site by call site.
///
/// A single template is written in a small dialect and rendered into each package's API:
/// <list type="bullet">
///   <item><c>#USINGS#</c> — the JSON usings for that package.</item>
///   <item><c>Json.Write(v)</c> / <c>Json.WriteIndented(v)</c> / <c>Json.Read&lt;T&gt;(s)</c> — the
///     entry points, supplied by a per-dialect helper class appended to the program.</item>
///   <item><c>[#PROP("n")]</c> — the rename attribute (<c>JsonProperty</c> / <c>JsonPropertyName</c>).</item>
///   <item><c>#JSONEX#</c> — that package's exception type.</item>
/// </list>
/// Everything else is ordinary C# and identical in both renderings, which is the point: what differs
/// in the output differs because the serializer differs.
/// </summary>
public abstract class CrossPackageTestBase
{
    private const string NewtonsoftShim = """

        internal static class Json
        {
            public static string Write(object v)         => Newtonsoft.Json.JsonConvert.SerializeObject(v);
            public static string WriteIndented(object v) => Newtonsoft.Json.JsonConvert.SerializeObject(v, Newtonsoft.Json.Formatting.Indented);
            public static T      Read<T>(string s)       => Newtonsoft.Json.JsonConvert.DeserializeObject<T>(s);
        }
        """;

    private const string SystemTextJsonShim = """

        internal static class Json
        {
            public static string Write(object v)         => System.Text.Json.JsonSerializer.Serialize(v);
            public static string WriteIndented(object v) => System.Text.Json.JsonSerializer.Serialize(v, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            public static T      Read<T>(string s)       => System.Text.Json.JsonSerializer.Deserialize<T>(s);
        }
        """;

    protected static string RenderNewtonsoft(string template) => template
        .Replace("#USINGS#", "using Newtonsoft.Json;\nusing Newtonsoft.Json.Serialization;")
        .Replace("#PROP(", "JsonProperty(")
        .Replace("#JSONEX#", "Newtonsoft.Json.JsonException")
        + NewtonsoftShim;

    protected static string RenderSystemTextJson(string template) => template
        .Replace("#USINGS#", "using System.Text.Json;\nusing System.Text.Json.Serialization;")
        .Replace("#PROP(", "JsonPropertyName(")
        .Replace("#JSONEX#", "System.Text.Json.JsonException")
        + SystemTextJsonShim;

    /// <summary>
    /// Asserts the two packages produce identical output — the migration is transparent for this shape.
    /// </summary>
    protected async Task AssertSame(string template)
    {
        var (newtonsoft, systemTextJson) = await RunBoth(template);

        var a = TestOutput.CanonicalizeJson(newtonsoft);
        var b = TestOutput.CanonicalizeJson(systemTextJson);

        if (!string.Equals(a, b, StringComparison.Ordinal))
        {
            Assert.Fail(
                "The two packages disagree, so migrating this shape is not transparent.\n" +
                $"\n--- Transpose.Newtonsoft.Json ---\n{a}\n" +
                $"\n--- Transpose.System.Text.Json ---\n{b}\n");
        }
    }

    /// <summary>
    /// Asserts a known, deliberate difference between the two packages, pinning both sides so the
    /// migration note cannot rot. Member order is *not* canonicalized here — a difference is only
    /// worth recording when it is a real one, and both packages order members alphabetically.
    /// </summary>
    protected async Task AssertDiffers(string template, string newtonsoft, string systemTextJson)
    {
        var (actualNewtonsoft, actualSystemTextJson) = await RunBoth(template);

        if (!string.Equals(TestOutput.Normalize(newtonsoft), actualNewtonsoft, StringComparison.Ordinal))
        {
            Assert.Fail(
                "The recorded Transpose.Newtonsoft.Json output is stale.\n" +
                $"\n--- recorded ---\n{TestOutput.Normalize(newtonsoft)}\n" +
                $"\n--- actual ---\n{actualNewtonsoft}\n");
        }

        if (!string.Equals(TestOutput.Normalize(systemTextJson), actualSystemTextJson, StringComparison.Ordinal))
        {
            Assert.Fail(
                "The recorded Transpose.System.Text.Json output is stale.\n" +
                $"\n--- recorded ---\n{TestOutput.Normalize(systemTextJson)}\n" +
                $"\n--- actual ---\n{actualSystemTextJson}\n");
        }

        if (string.Equals(actualNewtonsoft, actualSystemTextJson, StringComparison.Ordinal))
        {
            Assert.Fail("The two packages now agree — this is no longer a divergence, so use AssertSame.");
        }
    }

    private static async Task<(string Newtonsoft, string SystemTextJson)> RunBoth(string template)
    {
        // Sequentially: both runners build a package on first use and shell out to Node.
        var newtonsoft     = await NewtonsoftPackageRunner.RunAsync(RenderNewtonsoft(template));
        var systemTextJson = await TranslatedJsonRunner.RunAsync(RenderSystemTextJson(template));

        return (newtonsoft, systemTextJson);
    }
}
