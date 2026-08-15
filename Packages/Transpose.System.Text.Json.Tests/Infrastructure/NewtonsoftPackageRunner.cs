using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Transpose.SystemTextJson.Tests;

/// <summary>
/// The same translate-and-run-on-Node pipeline as <see cref="TranslatedJsonRunner"/>, but against the
/// sibling <c>Transpose.Newtonsoft.Json</c> package.
///
/// It exists for one reason: the Curiosity front-end is migrating off that package onto this one, and
/// the question that decides how risky each call site is — "does the payload on the wire change?" —
/// is only answerable by running the same program through both. See <c>CrossPackageTests</c>.
/// </summary>
public static class NewtonsoftPackageRunner
{
    private const string PackageAssemblyName = "Transpose.Newtonsoft.Json";

    private static readonly Lazy<PackageArtifacts> _package = new(BuildPackage, LazyThreadSafetyMode.ExecutionAndPublication);

    private sealed record PackageArtifacts(string ReferenceDllPath, string GlueJavascript, string JsonConvertJavascript);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));

    public static string PackageDir => Path.Combine(RepoRoot(), "Packages", "Transpose.Newtonsoft.Json");

    private static PackageArtifacts BuildPackage()
    {
        var packageDir = PackageDir;

        var sources = Directory.EnumerateFiles(packageDir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Replace('\\', '/').Contains("/bin/") && !p.Replace('\\', '/').Contains("/obj/"))
            .Select(p => (path: p, text: File.ReadAllText(p)))
            .ToList();

        var result = new RoslynTranslator().BuildAssembly(
            sources,
            PackageAssemblyName,
            extraReferencePaths: null,
            preprocessorSymbols: new[] { "TRANSPOSE", "TRACE" },
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
    /// Node against that package's runtime, and returns the trimmed console output.
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

        var full =
            RoslynTranslator.LoadRuntime() + "\n" +
            package.GlueJavascript + "\n" +
            package.JsonConvertJavascript + "\n" +
            "Newtonsoft.Json.$cache = Newtonsoft.Json.$cache || [];\n" +
            result.Javascript;

        if (Environment.GetEnvironmentVariable("TPS_DUMP_JS_NEWTONSOFT") is { Length: > 0 } dump)
            File.WriteAllText(dump, full);

        return TestOutput.Normalize(await NodeJsRunner.RunAsync(full));
    }
}
