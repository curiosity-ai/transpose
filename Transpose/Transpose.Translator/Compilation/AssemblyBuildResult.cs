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
    public RuntimePackageResult(byte[]? assemblyBytes, Emitter.ClassPathOutput? classPath,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        AssemblyBytes = assemblyBytes;
        ClassPath = classPath;
        Diagnostics = diagnostics;
    }

    public byte[]? AssemblyBytes { get; }
    public Emitter.ClassPathOutput? ClassPath { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public IEnumerable<Diagnostic> Errors => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
    public bool Success => AssemblyBytes is not null && ClassPath is not null && !Errors.Any();
}
