using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace H5.Translator.Roslyn;

/// <summary>
/// Result of translating one or more C# source files to JavaScript.
/// </summary>
public sealed class TranslationResult
{
    public TranslationResult(string? javascript, IReadOnlyList<Diagnostic> diagnostics)
    {
        Javascript = javascript;
        Diagnostics = diagnostics;
    }

    /// <summary>The emitted JavaScript, or <c>null</c> if translation failed.</summary>
    public string? Javascript { get; }

    /// <summary>All diagnostics produced by Roslyn and by the translator itself.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public IEnumerable<Diagnostic> Errors =>
        Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);

    public bool Success => Javascript is not null && !Errors.Any();
}
