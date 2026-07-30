using Microsoft.CodeAnalysis;

namespace Transpose.Compiler.Library;

/// <summary>
/// The outcome of a <see cref="ProjectBuildRequest"/>. <see cref="SiteDirectory"/> is set only when the
/// build assembled a runnable site (the shape a dev server can serve); a package build succeeds with it
/// null. <see cref="Errors"/>/<see cref="Warnings"/> are pre-formatted in the canonical
/// <c>file(line,column): error CS0000: message</c> shape <c>tps</c> prints, so a host can surface them
/// without knowing anything about Roslyn.
/// </summary>
public sealed class ProjectBuildResult
{
    private readonly IReadOnlyList<OutputBuilder.CssResource> _cssResources;

    internal ProjectBuildResult(BuildOutcome outcome, IReadOnlyList<string> output)
    {
        ExitCode = outcome.ExitCode;
        SiteDirectory = outcome.OutDir;
        HtmlDisabled = outcome.HtmlDisabled;
        Diagnostics = outcome.Diagnostics;
        _cssResources = outcome.CssResources;
        Output = output;
        Errors = outcome.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(MsBuildDiagnostic.Format).ToList();
        Warnings = outcome.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).Select(MsBuildDiagnostic.Format).ToList();
    }

    /// <summary>True when the build completed without errors.</summary>
    public bool Success => ExitCode == 0;

    /// <summary>The process exit code <c>tps</c> would have returned for this build: 0 on success, 1 for a
    /// build failure, 2 for a crash while translating or writing the output.</summary>
    public int ExitCode { get; }

    /// <summary>The assembled site's directory, for a successful site build; null for a failure or for a
    /// project whose build shape is a package DLL.</summary>
    public string? SiteDirectory { get; }

    /// <summary>Whether the project's <c>tps.json</c> disables index.html generation — a host that wants
    /// to inject into the page needs to know there is no generated page to inject into.</summary>
    public bool HtmlDisabled { get; }

    /// <summary>Every diagnostic the build reported, errors and warnings alike.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>The build errors, formatted one per line — empty when <see cref="Success"/> is true.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>The build warnings, formatted one per line. Populated whether or not the build succeeded.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Every line the build printed — its progress output plus its formatted diagnostics, in the
    /// order <c>tps</c> would have written them. Collected regardless of
    /// <see cref="ProjectBuildRequest.OnProgress"/>, so a failure can be reported in full after the fact.</summary>
    public IReadOnlyList<string> Output { get; }

    /// <summary>The stylesheets this site build copied from disk (its own <c>tps.json</c> resources and
    /// those of every project it references). Opaque to callers — it is what
    /// <see cref="TransposeWatcher"/> uses to update CSS without recompiling.</summary>
    internal IReadOnlyList<OutputBuilder.CssResource> CssResources => _cssResources;
}
