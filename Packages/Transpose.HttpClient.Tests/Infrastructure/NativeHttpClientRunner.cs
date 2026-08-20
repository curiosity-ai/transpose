using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Transpose.HttpClient.Tests;

/// <summary>
/// Compiles and runs a snippet natively (Roslyn → in-memory assembly → invoke entry point) against the
/// <b>real</b> <c>System.Net.Http</c>, capturing console output. That is the oracle the translated
/// JavaScript is diffed against for everything the package is meant to reproduce exactly.
///
/// The reference set is the test host's own trusted-platform assembly list, which is where
/// <c>System.Net.Http</c> lives — it ships in the shared framework, so a snippet's
/// <c>using System.Net.Http;</c> binds to it with no package reference involved.
///
/// The oracle only covers the part of the surface that does not touch the wire: <c>HttpMethod</c>,
/// <c>HttpStatusCode</c>, the header collections, <c>HttpRequestMessage</c>/<c>HttpResponseMessage</c>
/// state, <c>EnsureSuccessStatusCode</c>, <c>HttpRequestException</c>, <c>HttpRequestOptions</c>. A
/// snippet that sends a request cannot be compared this way — its transport is
/// <c>XMLHttpRequest</c> in one world and a socket in the other — so those tests assert the
/// translated output directly instead.
/// </summary>
public static class NativeHttpClientRunner
{
    public static string CompileAndRun(string source)
    {
        AppContext.SetSwitch("System.Globalization.Invariant", true);
        AppContext.SetSwitch("System.TimeZoneInfo.Invariant", true);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        // Define DEBUG/TRACE so native execution matches the translator's Debug-build parse options.
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.Latest).WithPreprocessorSymbols("DEBUG", "TRACE"));

        var refs = ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "NativeHttpRun_" + Guid.NewGuid().ToString("N"),
            new[] { tree },
            refs,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Debug));

        using var peStream = new MemoryStream();
        var result = compilation.Emit(peStream);
        if (!result.Success)
        {
            var errors = string.Join("\n", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            throw new InvalidOperationException("Native compilation failed:\n" + errors);
        }

        peStream.Seek(0, SeekOrigin.Begin);
        var context = new AssemblyLoadContext("NativeHttpRun", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(peStream);
            var entry = assembly.EntryPoint
                ?? throw new InvalidOperationException("No entry point found.");

            var sb = new StringBuilder();
            var writer = new StringWriter(sb);
            var previous = Console.Out;
            Console.SetOut(writer);
            try
            {
                var args = entry.GetParameters().Length == 0 ? null : new object[] { Array.Empty<string>() };
                if (entry.Invoke(null, args) is System.Threading.Tasks.Task task)
                {
                    task.GetAwaiter().GetResult();
                }
            }
            finally
            {
                Console.SetOut(previous);
                writer.Flush();
            }

            return TestOutput.Normalize(sb.ToString());
        }
        finally
        {
            context.Unload();
        }
    }
}
