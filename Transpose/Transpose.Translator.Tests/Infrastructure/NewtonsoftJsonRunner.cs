using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Transpose.Translator.Tests;

/// <summary>
/// Compiles and runs a C# program that uses the <c>Transpose.Newtonsoft.Json</c> binding library as
/// translated JavaScript on Node, so the package's runtime (JsonConvert.js) is exercised end-to-end.
///
/// The package is a JavaScript binding library ([assembly: External]): its C# is a set of external
/// type/attribute declarations, and its real behaviour lives in the hand-written
/// <c>Resources/Manual/JsonConvert.js</c>. To run a program against it we
/// <list type="number">
///   <item>compile the package's own C# once into a reference assembly (+ its emitted glue JS), so
///     the test program can bind to <c>Newtonsoft.Json.*</c>;</item>
///   <item>compile the test program against that reference (reflection on, so types are
///     reflectable — TypeNameHandling and the contract walker need metadata);</item>
///   <item>prepend the Transpose runtime, the package glue and <c>JsonConvert.js</c> to the program
///     JS and run it on Node.</item>
/// </list>
/// There is no native (Roslyn) comparison: the package types are external stubs with no managed
/// behaviour, so tests assert the expected console output directly.
/// </summary>
public static class NewtonsoftJsonRunner
{
    private const string PackageAssemblyName = "Transpose.Newtonsoft.Json";

    private static readonly Lazy<PackageArtifacts> _package = new(BuildPackage, LazyThreadSafetyMode.ExecutionAndPublication);

    private sealed record PackageArtifacts(string ReferenceDllPath, string GlueJavascript, string JsonConvertJavascript);

    /// <summary>Repo root, derived from this source file's compile-time path.</summary>
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        // .../Transpose/Transpose.Translator.Tests/Infrastructure/NewtonsoftJsonRunner.cs → repo root
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));

    private static string PackageDir => Path.Combine(RepoRoot(), "Packages", "Transpose.Newtonsoft.Json");

    private static PackageArtifacts BuildPackage()
    {
        var packageDir = PackageDir;

        // The package's C# declarations (external stubs + attributes + enums). Exclude the JS
        // resources (they aren't C#) and any build output.
        var sources = Directory.EnumerateFiles(packageDir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Replace('\\', '/').Contains("/bin/") && !p.Replace('\\', '/').Contains("/obj/"))
            .Select(p => (path: p, text: File.ReadAllText(p)))
            .ToList();

        var result = new RoslynTranslator().BuildAssembly(
            sources,
            PackageAssemblyName,
            extraReferencePaths: null,
            preprocessorSymbols: new[] { "Transpose", "TRACE" },
            emitAssembly: true);

        if (result.AssemblyBytes is null || result.Javascript is null)
        {
            var errors = string.Join("\n", result.Diagnostics.Select(d => d.GetMessage()));
            throw new InvalidOperationException($"Failed to build the Transpose.Newtonsoft.Json package:\n{errors}");
        }

        var dllPath = Path.Combine(Path.GetTempPath(), $"Transpose.Newtonsoft.Json.{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(dllPath, result.AssemblyBytes);

        var jsonConvert = File.ReadAllText(Path.Combine(packageDir, "Resources", "Manual", "JsonConvert.js"));

        return new PackageArtifacts(dllPath, result.Javascript, jsonConvert);
    }

    /// <summary>
    /// Translates <paramref name="csharpCode"/> (which may use <c>Newtonsoft.Json.*</c>), runs it on
    /// Node with the package runtime loaded, and returns the trimmed console output.
    /// </summary>
    public static async Task<string> RunAsync(string csharpCode)
    {
        var package = _package.Value;

        var result = new RoslynTranslator().Translate(
            new[] { ("App.cs", csharpCode) },
            CompilationBuilder.DefaultAssemblyName,
            extraReferencePaths: new[] { package.ReferenceDllPath },
            preprocessorSymbols: new[] { "DEBUG", "TRACE" });

        if (!result.Success)
        {
            var errors = string.Join("\n", result.Errors.Select(d => d.GetMessage()));
            throw new InvalidOperationException($"Translation failed:\n{errors}");
        }

        // Runtime + package glue (the External type/enum/attribute defines) + the hand-written
        // JsonConvert.js behaviour + the program. tps.js auto-runs the entry point.
        // The package's tps.json stitches JsonConvert.js between AssemblyBegin.js/AssemblyEnd.js;
        // the only runtime state those add is the type cache, so initialise it here.
        var full =
            RoslynTranslator.LoadRuntime() + "\n" +
            package.GlueJavascript + "\n" +
            package.JsonConvertJavascript + "\n" +
            "Newtonsoft.Json.$cache = Newtonsoft.Json.$cache || [];\n" +
            result.Javascript;

        if (Environment.GetEnvironmentVariable("TPS_DUMP_JS") is { Length: > 0 } dump)
            File.WriteAllText(dump, full);

        var output = await NodeJsRunner.RunAsync(full);
        return Normalize(output);
    }

    private static string Normalize(string output)
    {
        if (string.IsNullOrEmpty(output)) return "";
        return string.Join("\n", output.Trim()
            .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
            .Select(s => s.TrimEnd()));
    }
}
