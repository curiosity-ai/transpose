using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Transpose.Translator;

/// <summary>
/// Reports a runtime type test — <c>is</c>, <c>as</c>, or a type pattern — whose target is an
/// <c>[ObjectLiteral]</c> type.
///
/// An <c>[ObjectLiteral]</c> instance IS a plain JavaScript object: <c>new Options { A = 1 }</c>
/// emits <c>{A: 1}</c>, with no <c>Transpose.define</c>d class behind it and no <c>$type</c> on it.
/// That is the whole point of the attribute — such an object crosses into JSON and into hand-written
/// JavaScript — and it is also the reason nothing at run time can tell one literal type from another.
/// <c>Transpose.is(value, Options)</c> has only the object in front of it to go on, so it answers
/// <b>true</b> for every object it is handed: a literal of a completely unrelated type, a
/// <c>JSON.parse</c> result, an option bag a binding produced. The test compiles, reads like a
/// C# type test, and is right by accident:
///
/// <code>
/// object o = new Lit { X = 1 };
/// o is Other      // true  (.NET: false)
/// o as Other      // non-null (.NET: null)
/// o is Derived    // true  (.NET: false — Lit is not a Derived)
/// </code>
///
/// There is no structural rule that would fix this, which is why the test is rejected rather than
/// made smarter. A literal's members are ordinary JavaScript slots: a <c>{}</c> deserialized into a
/// literal whose only member is a <c>bool Flag</c> is a perfectly legitimate instance with
/// <c>Flag == false</c>, so "has the right properties" cannot separate it from any other object.
/// Nor does it help to ask where the value came from: the example above starts from a literal
/// Transpose itself built, and is still undecidable. The information simply is not there.
///
/// <b>A cast is not a test and is not reported.</b> <c>(Lit)value</c>, <c>Script.Write&lt;Lit&gt;</c>
/// and <c>.As&lt;Lit&gt;()</c> all *assert* a type rather than asking about one, which is exactly the
/// right thing to say about a value whose shape you know and the runtime cannot check — reading a
/// literal back out of JSON, or off a binding. That is the fix at every site this reports.
///
/// <b>The one sound test is kept.</b> When the value's static type already converts to the target —
/// <c>Lit x; x is Lit</c>, or a literal derived from the target — the test can only be asking whether
/// the value is null, and <c>Transpose.is(null, …)</c> answers false, so it means what it says.
/// Only a *downcast* or a test against an unrelated literal is reported.
///
/// Driven from <see cref="UnsupportedFeatureScanner"/>'s walk, like
/// <see cref="ObjectLiteralMemberScanner"/>: it already visits every syntax node with a semantic
/// model in hand, so this adds no pass of its own.
/// </summary>
internal static class ObjectLiteralTypeTestScanner
{
    /// <summary>The diagnostic for testing <paramref name="targetSyntax"/> against
    /// <paramref name="inputType"/>, or null when the test is sound (or not about a literal at all).
    /// <paramref name="inputType"/> is the static type of the value being tested — null when it is
    /// unknown, which is treated as "not convertible" and therefore reported.</summary>
    public static Diagnostic? Check(SemanticModel model, TypeSyntax? targetSyntax, ITypeSymbol? inputType, string form)
    {
        if (targetSyntax is null) return null;

        // A type used in *pattern* position is not an expression, and GetTypeInfo answers for some of
        // those shapes and not others (a bare `is not Lit` among the nots) — GetSymbolInfo always
        // resolves the name, so it is the fallback rather than the other way round.
        var target = model.GetTypeInfo(targetSyntax).Type ?? model.GetSymbolInfo(targetSyntax).Symbol as ITypeSymbol;
        if (!MentionsObjectLiteral(target)) return null;

        // `Lit x; x is Lit` and `Derived d; d is Lit` are null checks, and a null check is exactly what
        // the emitted `Transpose.is` performs correctly. Only a downcast asks a question the runtime
        // cannot answer. An array target is never sound this way: `System.Array.matchesUntyped` reads
        // the element type off the elements, and a literal element answers true for anything.
        if (target is not IArrayTypeSymbol && inputType is not null)
        {
            var conversion = ((CSharpCompilation)model.Compilation).ClassifyConversion(inputType, target!);
            if (conversion.IsIdentity || (conversion.IsImplicit && conversion.IsReference)) return null;
        }

        return Diagnostics.Create(
            Diagnostics.ObjectLiteralTypeTest,
            targetSyntax.GetLocation(),
            target!.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            form,
            LiteralIn(target!)!.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }

    /// <summary>The input type of a pattern, straight from the bound form — which is where Roslyn has
    /// already worked out what a <c>switch</c> arm or a nested sub-pattern is being matched against,
    /// and saves this from re-deriving it from the syntax.</summary>
    public static ITypeSymbol? PatternInputType(SemanticModel model, PatternSyntax pattern)
        => (model.GetOperation(pattern) as IPatternOperation)?.InputType;

    private static bool MentionsObjectLiteral(ITypeSymbol? type) => LiteralIn(type) is not null;

    /// <summary>The literal type a test against <paramref name="type"/> ends up asking about: the type
    /// itself, or — for <c>is Lit[]</c>, <c>is Lit[][]</c> — its element type, since testing an array
    /// tests every element with the same undecidable question.</summary>
    private static ITypeSymbol? LiteralIn(ITypeSymbol? type)
    {
        while (type is IArrayTypeSymbol array) type = array.ElementType;
        return Emitter.IsObjectLiteralType(type) ? type : null;
    }
}
