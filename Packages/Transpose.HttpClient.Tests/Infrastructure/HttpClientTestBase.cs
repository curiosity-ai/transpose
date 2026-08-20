using System;
using System.Linq;
using System.Threading.Tasks;

namespace Transpose.HttpClient.Tests;

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
}

/// <summary>
/// Base class for the <c>Transpose.HttpClient</c> test suite.
///
/// The package re-implements the <c>System.Net.Http</c> surface a browser application uses, over
/// <c>XMLHttpRequest</c>. That splits its behaviour in two, and so does this class:
/// <list type="bullet">
///   <item><b>Everything off the wire</b> — <c>HttpMethod</c>, <c>HttpStatusCode</c>, the header
///     collections, message state, <c>EnsureSuccessStatusCode</c>, <c>HttpRequestOptions</c> — has a
///     real oracle: the same snippet compiles against the <b>real</b> System.Net.Http in the shared
///     framework. Use <see cref="RunAndCompare"/> there, so the package is held to what .NET does
///     rather than to what it happens to do today.</item>
///   <item><b>Everything on the wire</b> runs against the fake <c>XMLHttpRequest</c> in
///     <c>xhr-stub.js</c>, driven from C# through the <c>Xhr</c> harness class. There is no native
///     oracle for that (the transport differs by construction), so use <see cref="RunJs"/> and assert
///     the output directly — including the request that went out, which the stub records.</item>
/// </list>
///
/// <see cref="RunJs(string,string,string)"/> also takes what native .NET prints, for a snippet the
/// package deliberately or accidentally diverges on. That keeps a documented divergence honest: the
/// note is asserted on both sides, so it fails when either changes.
/// </summary>
public abstract class HttpClientTestBase
{
    /// <summary>
    /// Runs <paramref name="csharpCode"/> both natively (real System.Net.Http) and as translated
    /// JavaScript (this package), asserting the console output matches. Returns the JavaScript output.
    /// </summary>
    /// <param name="nativeCode">
    /// An equivalent snippet to run natively, where the two surfaces are not source-compatible — the
    /// package's <c>HttpResponseMessage</c> constructors take the underlying <c>XMLHttpRequest</c>, so
    /// a snippet that builds a response by hand cannot compile against both. Only the *shape* of the
    /// snippet may differ; what it prints is still required to match.
    /// </param>
    protected async Task<string> RunAndCompare(string csharpCode, string? nativeCode = null)
    {
        var jsOutput = await TranslatedHttpClientRunner.RunAsync(csharpCode);
        var nativeOutput = NativeHttpClientRunner.CompileAndRun(nativeCode ?? csharpCode);

        if (!string.Equals(nativeOutput, jsOutput, StringComparison.Ordinal))
        {
            Assert.Fail(
                "Output mismatch between the real System.Net.Http and the Transpose package.\n" +
                $"\n--- expected (native System.Net.Http) ---\n{nativeOutput}\n" +
                $"\n--- actual (Transpose / JS) ---\n{jsOutput}\n");
        }

        return jsOutput;
    }

    /// <summary>
    /// Runs <paramref name="csharpCode"/> as translated JavaScript only and asserts its console output
    /// verbatim. Pass <paramref name="nativePrints"/> for a documented divergence — what the real
    /// System.Net.Http prints for the same snippet is then asserted too, so the note cannot rot.
    /// </summary>
    /// <param name="nativeCode">
    /// As on <see cref="RunAndCompare"/>: the equivalent snippet to run natively when the two
    /// surfaces are not source-compatible. Only meaningful together with
    /// <paramref name="nativePrints"/>.
    /// </param>
    protected async Task<string> RunJs(string csharpCode, string expected, string? nativePrints = null, string? nativeCode = null)
    {
        var jsOutput = await TranslatedHttpClientRunner.RunAsync(csharpCode);

        if (!string.Equals(TestOutput.Normalize(expected), jsOutput, StringComparison.Ordinal))
        {
            Assert.Fail(
                "Translated JavaScript output changed.\n" +
                $"\n--- expected ---\n{TestOutput.Normalize(expected)}\n" +
                $"\n--- actual ---\n{jsOutput}\n");
        }

        if (nativePrints is not null)
        {
            var nativeOutput = NativeHttpClientRunner.CompileAndRun(nativeCode ?? csharpCode);
            if (!string.Equals(TestOutput.Normalize(nativePrints), nativeOutput, StringComparison.Ordinal))
            {
                Assert.Fail(
                    "The recorded native System.Net.Http output for this documented divergence is stale.\n" +
                    $"\n--- recorded ---\n{TestOutput.Normalize(nativePrints)}\n" +
                    $"\n--- actual (native System.Net.Http) ---\n{nativeOutput}\n");
            }
        }

        return jsOutput;
    }

    /// <summary>Runs the snippet as translated JavaScript only, returning its output (no assertion).</summary>
    protected Task<string> RunJs(string csharpCode) => TranslatedHttpClientRunner.RunAsync(csharpCode);

    /// <summary>Runs the snippet natively against the real System.Net.Http, returning its output.</summary>
    protected string RunNative(string csharpCode) => NativeHttpClientRunner.CompileAndRun(csharpCode);
}
