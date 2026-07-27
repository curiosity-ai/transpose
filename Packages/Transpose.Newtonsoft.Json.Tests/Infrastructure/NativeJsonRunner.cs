using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Transpose.Newtonsoft.Json.Tests;

/// <summary>
/// Compiles and runs a test snippet natively (Roslyn → in-memory assembly → invoke entry point)
/// against the <b>real</b> Json.NET, capturing Console output. This is the oracle the translated
/// JavaScript is diffed against: the binding library exists to behave like Json.NET, so "what does
/// Json.NET print" is the definition of correct.
///
/// The reference set is the test host's own trusted-platform assembly list, which contains the
/// <c>Newtonsoft.Json</c> package this project references — that is how a snippet's
/// <c>using Newtonsoft.Json;</c> binds.
/// </summary>
public static class NativeJsonRunner
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
            "NativeJsonRun_" + Guid.NewGuid().ToString("N"),
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
        var context = new AssemblyLoadContext("NativeJsonRun", isCollectible: true);
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
                var invokeResult = entry.Invoke(null, args);
                if (invokeResult is System.Threading.Tasks.Task task)
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
