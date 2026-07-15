using System;
using Microsoft.CodeAnalysis;

namespace Transpose.Translator;

/// <summary>
/// Thrown when the translator encounters a construct it cannot emit, including
/// language features that do not make sense in a browser environment.
/// </summary>
public sealed class TranslationException : Exception
{
    public TranslationException(string message, Location? location = null) : base(message)
    {
        Location = location;
    }

    public Location? Location { get; }
}

/// <summary>
/// Diagnostic descriptors emitted by the Roslyn translator (prefix TransposeR).
/// </summary>
internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor Unsupported = new(
        id: "TransposeR0001",
        title: "Unsupported feature",
        messageFormat: "{0}",
        category: "Transpose.Translator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotImplemented = new(
        id: "TransposeR0002",
        title: "Not implemented",
        messageFormat: "Translation of this construct is not implemented yet: {0}",
        category: "Transpose.Translator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static Diagnostic Create(DiagnosticDescriptor descriptor, Location? location, params object[] args) =>
        Diagnostic.Create(descriptor, location ?? Location.None, args);
}
