using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Transpose.SystemTextJson.Tests;

/// <summary>Normalizes captured console output so the two runners are comparable.</summary>
public static class TestOutput
{
    public static string Normalize(string output)
    {
        if (string.IsNullOrEmpty(output)) return "";
        return string.Join("\n", output.Trim()
            .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
            .Select(s => s.TrimEnd()));
    }

    /// <summary>
    /// Rewrites every JSON document in the output with its object members sorted by name, so a
    /// comparison tests structure and values rather than member order.
    ///
    /// Member order is the one systematic difference between the two implementations: System.Text.Json
    /// writes properties in declaration order (most-derived type first, then fields), while the
    /// binding library reads the type's Transpose reflection metadata, which lists them
    /// alphabetically. <c>SerializationTests.MemberOrderIsAlphabetical</c> pins that down; every other
    /// test compares canonically so it is not re-asserted a hundred times.
    ///
    /// Json.NET is used here purely as a JSON *parser* for the comparison — it is never the oracle,
    /// and no snippet under test references it.
    /// </summary>
    public static string CanonicalizeJson(string output)
    {
        if (string.IsNullOrEmpty(output)) return output;

        // An indented document spans several lines, so try the whole output first, then line by line.
        if (TrySort(output, out var whole)) return whole;

        return string.Join("\n", output
            .Split('\n')
            .Select(line => TrySort(line, out var sorted) ? sorted : line));
    }

    private static bool TrySort(string text, out string sorted)
    {
        sorted = text;
        var trimmed = text.Trim();
        if (trimmed.Length < 2 || (trimmed[0] != '{' && trimmed[0] != '[')) return false;

        try
        {
            using var reader = new JsonTextReader(new System.IO.StringReader(trimmed))
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Decimal,
            };
            var token = JToken.ReadFrom(reader);
            if (reader.Read()) return false; // trailing content: not a single document

            sorted = Sort(token).ToString(Formatting.None);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JToken Sort(JToken token)
    {
        switch (token)
        {
            case JObject obj:
                var sorted = new JObject();
                foreach (var property in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                    sorted.Add(property.Name, Sort(property.Value));
                return sorted;

            case JArray array:
                return new JArray(array.Select(Sort));

            default:
                return token;
        }
    }
}

/// <summary>
/// Base class for the <c>Transpose.System.Text.Json</c> test suite.
///
/// Every snippet is a small C# program run twice — natively against the <b>real</b> System.Text.Json
/// (which ships in the shared framework, so the snippet's <c>using System.Text.Json;</c> binds to it
/// with no package involved) and as translated JavaScript against this package — and the two console
/// outputs are diffed. The package exists to behave like System.Text.Json, so "what does
/// System.Text.Json print" is the specification.
///
/// Two ways to assert, and the choice is the point of the suite:
/// <list type="bullet">
///   <item><see cref="RunAndCompare"/> — require the two to agree. Use this wherever the package is
///     meant to match, which is nearly everywhere.</item>
///   <item><see cref="RunJs(string,string,string)"/> — run only the translated JavaScript and assert
///     its output directly. Use this for the documented divergences, passing what native prints so
///     the note is asserted rather than just claimed.</item>
/// </list>
/// </summary>
public abstract class JsonTestBase
{
    /// <summary>
    /// Runs <paramref name="csharpCode"/> both natively (real System.Text.Json) and as translated
    /// JavaScript (this package), asserting the console output matches. Returns the JavaScript output.
    /// </summary>
    /// <param name="exactMemberOrder">
    /// Compare the output verbatim instead of canonicalizing JSON member order. Only useful for a
    /// test that is *about* member order — see <see cref="TestOutput.CanonicalizeJson"/>.
    /// </param>
    protected async Task<string> RunAndCompare(string csharpCode, bool exactMemberOrder = false)
    {
        var jsOutput = await TranslatedJsonRunner.RunAsync(csharpCode);
        var nativeOutput = NativeJsonRunner.CompileAndRun(csharpCode);

        var expected = exactMemberOrder ? nativeOutput : TestOutput.CanonicalizeJson(nativeOutput);
        var actual = exactMemberOrder ? jsOutput : TestOutput.CanonicalizeJson(jsOutput);

        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            Assert.Fail(
                "Output mismatch between the real System.Text.Json and the Transpose package.\n" +
                $"\n--- expected (native System.Text.Json) ---\n{expected}\n" +
                $"\n--- actual (Transpose / JS) ---\n{actual}\n");
        }

        return jsOutput;
    }

    /// <summary>
    /// Runs <paramref name="csharpCode"/> as translated JavaScript only and asserts its console
    /// output verbatim. For documented divergences — pass <paramref name="nativePrints"/> to record
    /// what the real System.Text.Json prints instead (it is asserted too, so the note cannot rot).
    /// </summary>
    protected async Task RunJs(string csharpCode, string expected, string? nativePrints = null)
    {
        var jsOutput = await TranslatedJsonRunner.RunAsync(csharpCode);

        if (!string.Equals(TestOutput.Normalize(expected), jsOutput, StringComparison.Ordinal))
        {
            Assert.Fail(
                "Translated JavaScript output changed.\n" +
                $"\n--- expected ---\n{TestOutput.Normalize(expected)}\n" +
                $"\n--- actual ---\n{jsOutput}\n");
        }

        if (nativePrints is not null)
        {
            var nativeOutput = NativeJsonRunner.CompileAndRun(csharpCode);
            if (!string.Equals(TestOutput.Normalize(nativePrints), nativeOutput, StringComparison.Ordinal))
            {
                Assert.Fail(
                    "The recorded native System.Text.Json output for this documented divergence is stale.\n" +
                    $"\n--- recorded ---\n{TestOutput.Normalize(nativePrints)}\n" +
                    $"\n--- actual (native System.Text.Json) ---\n{nativeOutput}\n");
            }
        }
    }

    /// <summary>Runs the snippet as translated JavaScript only, returning its output (no assertion).</summary>
    protected Task<string> RunJs(string csharpCode) => TranslatedJsonRunner.RunAsync(csharpCode);

    /// <summary>Runs the snippet natively against the real System.Text.Json, returning its output.</summary>
    protected string RunNative(string csharpCode) => NativeJsonRunner.CompileAndRun(csharpCode);
}
