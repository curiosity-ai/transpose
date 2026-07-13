using System;
using System.Linq;
using System.Threading.Tasks;

namespace H5.Translator.Roslyn.Tests;

/// <summary>
/// Base class for the Roslyn translator integration tests. Mirrors the behavior
/// of the existing H5 integration tests: it runs the same C# both natively (Roslyn)
/// and as translated JavaScript (Node), then diffs the normalized console output.
/// </summary>
public abstract class TranslatorTestBase
{
    /// <summary>Compiles + runs the C# natively and as JS, asserting the outputs match.</summary>
    protected async Task<string> RunTest(string csharpCode, bool skipNative = false)
    {
        var translator = new RoslynTranslator();
        var result = translator.Translate(csharpCode);

        if (!result.Success)
        {
            var errors = string.Join("\n", result.Errors.Select(d => d.GetMessage()));
            Assert.Fail($"H5.Translator.Roslyn translation failed:\n{errors}");
        }

        string jsOutput;
        try
        {
            jsOutput = await NodeJsRunner.RunAsync(result.Javascript!);
        }
        catch (Exception ex)
        {
            Assert.Fail($"JavaScript execution failed:\n{ex}\n\n--- Generated JS ---\n{result.Javascript}");
            throw;
        }

        jsOutput = Normalize(jsOutput);

        if (!skipNative)
        {
            var nativeOutput = Normalize(RoslynNativeRunner.CompileAndRun(csharpCode));
            Assert.AreEqual(nativeOutput, jsOutput,
                $"Output mismatch.\n\nExpected (native Roslyn):\n----\n{nativeOutput}\n----\n\nActual (H5.Translator.Roslyn / JS):\n----\n{jsOutput}\n----\n\n--- Generated JS ---\n{result.Javascript}");
        }

        return jsOutput;
    }

    /// <summary>Asserts that translation reports the given unsupported-feature error.</summary>
    protected void RunTestExpectingError(string csharpCode, string expectedErrorSubstring)
    {
        var translator = new RoslynTranslator();
        var result = translator.Translate(csharpCode);

        if (result.Success)
        {
            Assert.Fail("Translation should have failed but succeeded.");
        }

        var combined = string.Join("\n", result.Diagnostics.Select(d => d.GetMessage()));
        Assert.IsTrue(combined.Contains(expectedErrorSubstring, StringComparison.OrdinalIgnoreCase),
            $"Expected error containing '{expectedErrorSubstring}' but got:\n{combined}");
    }

    private static string Normalize(string output)
    {
        if (string.IsNullOrEmpty(output)) return "";
        return string.Join("\n", output.Trim()
            .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
            .Select(s => s.TrimEnd()));
    }
}
