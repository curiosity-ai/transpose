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

    /// <summary>
    /// Per source file, a hash of everything in it except method/accessor bodies — the input the next
    /// build's incremental check compares against to decide whether an edit touched declarations. Null
    /// unless the build was given an <see cref="IncrementalPlan"/> (i.e. caching is on).
    /// </summary>
    public IReadOnlyDictionary<string, string>? DeclarationHashes { get; init; }

    /// <summary>
    /// The ES-module form of the same output, when the build asked for it (<c>outputBy: Module</c>).
    /// <see cref="Javascript"/> then holds the entry module rather than a single bundle, and this
    /// carries the chunk files alongside it. Null for an ordinary single-bundle build.
    /// </summary>
    public Emitter.ModuleOutput? Modules { get; init; }

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
