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

    public static readonly DiagnosticDescriptor DuplicateJsName = new(
        id: "TransposeR0003",
        title: "Duplicate JavaScript member name",
        messageFormat: "Two members of '{1}' are emitted as '{0}', which JavaScript cannot represent - "
                     + "only the last would exist at runtime. Give one of them a different [Name], or remove the overload: {2}",
        category: "Transpose.Translator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BadSharedWorkerEntry = new(
        id: "TransposeR0004",
        title: "Invalid shared-worker entry point",
        messageFormat: "'{0}' carries [SharedWorkerEntry] but {1}. A shared worker's entry point runs "
                     + "once, with no caller and no page to answer to, so it has to be a static void "
                     + "method taking no parameters.",
        category: "Transpose.Translator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateSharedWorkerName = new(
        id: "TransposeR0005",
        title: "Duplicate shared-worker name",
        messageFormat: "Two [SharedWorkerEntry] methods are both named '{0}', so each would emit the "
                     + "same worker script over the other. A shared worker is identified by its name, "
                     + "so give one of them a different one: {1}",
        category: "Transpose.Translator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static Diagnostic Create(DiagnosticDescriptor descriptor, Location? location, params object[] args) =>
        Diagnostic.Create(descriptor, location ?? Location.None, args);
}
