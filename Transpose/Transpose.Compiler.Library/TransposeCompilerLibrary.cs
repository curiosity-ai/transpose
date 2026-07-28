using System.Text;

namespace Transpose.Compiler.Library;

/// <summary>
/// Compiles C# source held in memory to JavaScript, in process — the library form of the <c>tps</c>
/// CLI, for a .NET application that wants to translate code without shelling out to a separate
/// process or touching disk. Build a <see cref="CompilationRequest"/> and pass it to
/// <see cref="Compile"/> or <see cref="CompileAsync"/>.
///
/// <code>
/// var result = TransposeCompilerLibrary.Compile(
///     new CompilationRequest("App")
///         .WithSourceFile("Program.cs", "System.Console.WriteLine(\"Hello!\");"));
///
/// if (result.Success) Console.WriteLine(result.Javascript);
/// else foreach (var error in result.Errors) Console.Error.WriteLine(error);
/// </code>
///
/// Compilations run one at a time, process-wide: <c>Transpose.Translator.CompileProgress.Sink</c> and
/// <c>Transpose.Translator.PhaseTimings</c> are process-wide mutable state the translator reports
/// progress/timings through (by design — <c>tps</c> is a fresh, single-build-per-process CLI, so
/// nothing about them needed to be reentrant). Two compilations running at once in the same process
/// would attribute each other's timings and progress to the wrong build, so this service serializes
/// them instead. A single compilation is still fully synchronous/CPU-bound — concurrent *callers*
/// just queue behind whichever one runs first.
/// </summary>
public static class TransposeCompilerLibrary
{
    private static readonly object Gate = new();

    /// <summary>Runs <paramref name="request"/> synchronously, blocking until any other compilation
    /// already in progress (in this process) finishes.</summary>
    public static CompilationResult Compile(CompilationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (Gate)
        {
            return CompileCore(request);
        }
    }

    /// <summary>Runs <paramref name="request"/> on a thread-pool thread, still serialized against any
    /// other compilation in this process (see the type-level remarks). Cancelling before this
    /// request's turn comes up skips it entirely; cancelling once it has started does not interrupt
    /// the (CPU-bound) compile in progress.</summary>
    public static Task<CompilationResult> CompileAsync(CompilationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => Compile(request), cancellationToken);
    }

    private static CompilationResult CompileCore(CompilationRequest request)
    {
        var translator = new RoslynTranslator();
        var result = translator.BuildAssembly(
            request.Sources,
            request.AssemblyName,
            request.ReferencePaths,
            request.DefineConstants,
            request.LanguageVersion,
            request.ReflectionEnabled,
            request.MetadataTarget,
            emitAssembly: request.EmitPackageAssembly,
            assemblyVersion: request.AssemblyVersion);

        if (!result.Success) return CompilationResult.Failed(result.Diagnostics);

        byte[]? packageAssemblyBytes = null;
        if (request.EmitPackageAssembly && result.AssemblyBytes is not null)
            packageAssemblyBytes = EmbedPackageResources(request, result);

        var js = request.IncludeRuntime
            ? RoslynTranslator.LoadRuntime() + "\n" + result.Javascript
            : result.Javascript!;

        return CompilationResult.Succeeded(js, result.MetadataJavascript, result.AssemblyBytes, packageAssemblyBytes, result.Diagnostics);
    }

    /// <summary>Embeds the compiled JS (and its separate metadata script, if any) into the emitted
    /// .NET assembly as manifest resources, fully in memory — the same shape
    /// <c>ResourceEmbedder</c>/<c>OutputBuilder</c> produce for a <c>tps --emit-package</c> build, so a
    /// referencing project can extract them exactly as it would from a package built by the CLI.</summary>
    private static byte[] EmbedPackageResources(CompilationRequest request, AssemblyBuildResult result)
    {
        var utf8 = new UTF8Encoding(false);
        var items = new List<EmbeddedItem> { new(request.AssemblyName + ".js", utf8.GetBytes(result.Javascript!), null) };
        if (result.MetadataJavascript is not null)
            items.Add(new EmbeddedItem(request.AssemblyName + ".meta.js", utf8.GetBytes(result.MetadataJavascript), null));

        using var output = new MemoryStream();
        ResourceEmbedder.Embed(output, result.AssemblyBytes!, items, request.ReferencePaths);
        return output.ToArray();
    }
}
