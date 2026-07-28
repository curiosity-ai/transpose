using Microsoft.CodeAnalysis;

namespace Transpose.Compiler.Library;

/// <summary>
/// The outcome of a <see cref="CompilationRequest"/>: on success, the translated JavaScript (and
/// optionally the .NET assembly bytes); on failure, the diagnostics that explain why. Mirrors
/// <c>Transpose.Translator.AssemblyBuildResult</c>, adapted for a caller with no MSBuild diagnostic
/// pipeline to hand raw <see cref="Diagnostic"/>s to — <see cref="Errors"/>/<see cref="Warnings"/> are
/// pre-formatted, one line per diagnostic, in the same
/// <c>file(line,column): error CS0000: message</c> shape <c>tps</c> itself prints.
/// </summary>
public sealed class CompilationResult
{
    private CompilationResult(
        string? javascript, string? metadataJavascript, byte[]? assemblyBytes, byte[]? packageAssemblyBytes,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        Javascript = javascript;
        MetadataJavascript = metadataJavascript;
        AssemblyBytes = assemblyBytes;
        PackageAssemblyBytes = packageAssemblyBytes;
        Diagnostics = diagnostics;
        Errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(MsBuildDiagnostic.Format).ToList();
        Warnings = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).Select(MsBuildDiagnostic.Format).ToList();
    }

    /// <summary>True when the compilation produced JavaScript with no errors (there may still be
    /// warnings — see <see cref="Warnings"/>).</summary>
    public bool Success => Javascript is not null;

    /// <summary>The compiled JavaScript bundle. Null when <see cref="Success"/> is false. Includes the
    /// <c>tps.js</c> runtime + <c>TransposeR</c> shim prelude when the request set
    /// <see cref="CompilationRequest.WithRuntime"/>.</summary>
    public string? Javascript { get; }

    /// <summary>The separate reflection-metadata script, when the request's
    /// <see cref="CompilationRequest.MetadataTarget"/> is <see cref="Transpose.Translator.MetadataTarget.File"/>
    /// and reflection is enabled. Null otherwise (with <c>Inline</c>, the default, the metadata is
    /// already part of <see cref="Javascript"/>).</summary>
    public string? MetadataJavascript { get; }

    /// <summary>The emitted .NET assembly's raw bytes, when <see cref="CompilationRequest.EmitPackageAssembly"/>
    /// was set. This is the plain assembly with no JavaScript embedded yet — see
    /// <see cref="PackageAssemblyBytes"/> for the distributable form.</summary>
    public byte[]? AssemblyBytes { get; }

    /// <summary>The distributable Transpose package assembly — <see cref="AssemblyBytes"/> with the
    /// compiled JavaScript (and its metadata, if separate) embedded as a manifest resource, exactly
    /// like the <c>tps</c> CLI's <c>--emit-package</c>. Another <see cref="CompilationRequest"/> can
    /// reference it via <see cref="CompilationRequest.WithReferenceAssembly"/> once written to disk.
    /// Null unless <see cref="CompilationRequest.EmitPackageAssembly"/> was set and the build
    /// succeeded.</summary>
    public byte[]? PackageAssemblyBytes { get; }

    /// <summary>Every diagnostic Roslyn (or the unsupported-feature scan) reported, errors and
    /// warnings alike.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>The compile errors, formatted one per line — empty when <see cref="Success"/> is true.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>The compile warnings, formatted one per line. Populated whether or not the build
    /// succeeded.</summary>
    public IReadOnlyList<string> Warnings { get; }

    internal static CompilationResult Failed(IReadOnlyList<Diagnostic> diagnostics)
        => new(null, null, null, null, diagnostics);

    internal static CompilationResult Succeeded(
        string javascript, string? metadataJavascript, byte[]? assemblyBytes, byte[]? packageAssemblyBytes,
        IReadOnlyList<Diagnostic> diagnostics)
        => new(javascript, metadataJavascript, assemblyBytes, packageAssemblyBytes, diagnostics);
}
