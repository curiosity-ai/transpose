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

    public static readonly DiagnosticDescriptor ObjectLiteralMember = new(
        id: "TransposeR0004",
        title: "Unsupported [ObjectLiteral] member type",
        messageFormat: "'{0}' is a '{2}', which an [ObjectLiteral] type cannot hold: '{1}' is emitted as a plain JavaScript object, "
                     + "so every field and property of it must hold a value JavaScript can represent on its own. {3}",
        category: "Transpose.Translator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ObjectLiteralTypeTest = new(
        id: "TransposeR0005",
        title: "Undecidable [ObjectLiteral] type test",
        messageFormat: "'{0}' cannot be tested at run time: '{2}' is an [ObjectLiteral] type, so its instances are plain "
                     + "JavaScript objects carrying no type identity, and {1}, whatever it really is. "
                     + "Cast instead - '({0})value', 'Script.Write<{0}>' and '.As<{0}>()' assert a type rather than asking about one.",
        category: "Transpose.Translator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static Diagnostic Create(DiagnosticDescriptor descriptor, Location? location, params object[] args) =>
        Diagnostic.Create(descriptor, location ?? Location.None, args);
}
