using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Transpose.Translator;

/// <summary>
/// Result of building a project as a distributable assembly: the translated JavaScript, its
/// optional separate reflection-metadata script, and the emitted .NET assembly bytes (so the JS
/// can be embedded into it and the DLL referenced by another project).
/// </summary>
public sealed class AssemblyBuildResult
{
    public AssemblyBuildResult(string? javascript, string? metadataJavascript, byte[]? assemblyBytes,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        Javascript = javascript;
        MetadataJavascript = metadataJavascript;
        AssemblyBytes = assemblyBytes;
        Diagnostics = diagnostics;
    }

    public string? Javascript { get; }
    public string? MetadataJavascript { get; }
    public byte[]? AssemblyBytes { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public IEnumerable<Diagnostic> Errors => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
    public bool Success => Javascript is not null && !Errors.Any();
}

/// <summary>
/// Result of building the base runtime package (Transpose.BCL): the emitted .NET reference assembly
/// bytes plus the <c>outputBy: ClassPath</c> per-class JS files and reflection metadata block, which
/// the CLI stitches with the hand-written runtime primitives into <c>tps.js</c>.
/// </summary>
public sealed class RuntimePackageResult
{
    public RuntimePackageResult(
        System.Func<IReadOnlyList<(string name, byte[] bytes)>, byte[]>? emitAssembly,
        Emitter.ClassPathOutput? classPath,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        EmitAssembly = emitAssembly;
        ClassPath = classPath;
        Diagnostics = diagnostics;
    }

    /// <summary>
    /// Emits the final runtime assembly with the given JS bundles (tps.js, tps.meta.js, …) embedded
    /// as manifest resources, returning the assembly bytes. The embedding is done through Roslyn's
    /// emitter — not a post-processing pass — precisely so the result stays a clean core library:
    /// Mono.Cecil's assembly writer injects an <c>mscorlib</c> assembly reference, which stops
    /// Roslyn from recognising the runtime as the corlib when it is later used as the sole BCL
    /// reference (every downstream type would fail with CS0518 "predefined type … not defined").
    /// Deferred (rather than eagerly emitted) because the bundles are assembled by the CLI from the
    /// ClassPath output only after this method returns.
    /// </summary>
    public System.Func<IReadOnlyList<(string name, byte[] bytes)>, byte[]>? EmitAssembly { get; }
    public Emitter.ClassPathOutput? ClassPath { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public IEnumerable<Diagnostic> Errors => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
    public bool Success => EmitAssembly is not null && ClassPath is not null && !Errors.Any();
}
