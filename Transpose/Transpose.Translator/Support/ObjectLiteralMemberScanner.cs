using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Transpose.Translator;

/// <summary>
/// Reports a field or property of an <c>[ObjectLiteral]</c> type whose type has no plain JavaScript
/// representation.
///
/// An <c>[ObjectLiteral]</c> instance IS a plain JS object — <c>new Options { A = 1 }</c> emits
/// <c>{A: 1}</c>, with no <c>Transpose.define</c>d class behind it — and the whole point of the
/// attribute is that such an object crosses into JSON and into hand-written JavaScript. So every slot
/// it declares has to hold something JavaScript can represent on its own: a primitive, an enum
/// (a number), an array of those, a function, or another object literal.
///
/// Everything else in Transpose is a *runtime* object built by tps.js — a <c>System.Int64</c> is a
/// <c>{low, high}</c> pair, a <c>DateTime</c> a runtime instance, a <c>List&lt;T&gt;</c> a class with a
/// prototype. Putting one in a literal produces an object that looks right in C#, serializes to
/// something nobody wrote (<c>{"Id":{"low":7,"high":0}}</c>), and cannot be read by the JavaScript the
/// literal exists to talk to. 64-bit integers are the sharpest case: they are unwrapped to plain
/// numbers in a literal (see <c>Emitter.Foreign64.cs</c>), which is representable but silently lossy
/// above 2^53 — so declaring one is rejected rather than quietly rounded.
///
/// The check is on the DECLARATION, not the construction site, so it fires once per bad member
/// wherever the type is used. It applies only to a type Transpose itself materialises: an
/// <c>[External]</c> or <c>[Scope]</c>-projected literal (Howler's option bags, the DOM's dictionary
/// types) describes an object that already exists in JavaScript, and its author — not this compiler —
/// decides what its slots hold.
///
/// Driven from <see cref="UnsupportedFeatureScanner"/>'s walk, like
/// <see cref="DuplicateJsNameScanner"/>: it already visits every type declaration with a semantic
/// model in hand, so this adds no pass of its own.
/// </summary>
internal static class ObjectLiteralMemberScanner
{
    public static void Report(INamedTypeSymbol type, List<Diagnostic> diagnostics)
    {
        if (!Emitter.IsObjectLiteralType(type)) return;
        // An external literal's slots are declared by whoever wrote the JavaScript behind them.
        if (TransposeNaming.IsExternalType(type)) return;

        foreach (var member in LiteralSlots(type))
        {
            var memberType = member switch
            {
                IFieldSymbol f => f.Type,
                IPropertySymbol p => p.Type,
                _ => null,
            };
            if (memberType is null || IsPlainJsValue(memberType)) continue;

            diagnostics.Add(Diagnostics.Create(
                Diagnostics.ObjectLiteralMember,
                member.Locations.FirstOrDefault(),
                member.Name,
                type.ToDisplayString(),
                memberType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                Remedy(memberType)));
        }
    }

    /// <summary>
    /// The members that become slots of the plain object: instance fields, and the instance
    /// properties that carry storage rather than compute a value (auto, field-backed, a record's
    /// positional members, and the bodyless <c>extern</c> form a binding uses to name a slot).
    /// A computed property is a prototype method on the literal's type and holds nothing, so it is
    /// not part of the object's shape; nor is an indexer, a constant, a static, or the compiler's own
    /// bookkeeping (a record's backing fields and its <c>EqualityContract</c>).
    /// </summary>
    private static IEnumerable<ISymbol> LiteralSlots(INamedTypeSymbol type)
    {
        foreach (var m in type.GetMembers())
        {
            if (m.IsStatic) continue;
            if (m is IFieldSymbol f && !f.IsConst && f.AssociatedSymbol is null && f.CanBeReferencedByName)
                yield return f;
            else if (m is IPropertySymbol p && !p.IsIndexer && !p.IsWriteOnly
                     && (Emitter.IsAutoProperty(p) || Emitter.IsRecordPositionalProperty(p)
                         || Emitter.IsFieldBackedProperty(p) || Emitter.IsExternProperty(p)))
                yield return p;
        }
    }

    /// <summary>
    /// True if a value of this type is something JavaScript can hold on its own, with no tps.js
    /// runtime object behind it.
    /// </summary>
    private static bool IsPlainJsValue(ITypeSymbol? type)
    {
        if (type is null) return true;

        // Nothing to say about a type the compilation could not resolve (Roslyn already reported it),
        // about `dynamic` (which is `object`), or about a type parameter — a generic literal's slot is
        // whatever it is substituted with, which this declaration does not know. That leaves
        // `Box<List<int>>` unreported; narrowing it would mean checking every construction of every
        // generic literal, which is a different (and much noisier) check than this one.
        if (type.TypeKind is TypeKind.Error or TypeKind.Dynamic or TypeKind.TypeParameter) return true;

        // `int?` is the int slot plus null, which JavaScript expresses directly.
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && type is INamedTypeSymbol { TypeArguments.Length: 1 } nullable)
            return IsPlainJsValue(nullable.TypeArguments[0]);

        switch (type.SpecialType)
        {
            // A JS string, boolean, or number (char included — Transpose emits one as its code unit),
            // and `object`, which by definition says nothing about the shape.
            case SpecialType.System_Boolean:
            case SpecialType.System_Char:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_String:
            case SpecialType.System_Object:
                return true;

            // long/ulong are unwrapped to plain numbers in a literal and round above 2^53; decimal and
            // the native-sized integers are tps.js runtime objects. Neither survives the round trip a
            // literal exists for.
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Decimal:
            case SpecialType.System_IntPtr:
            case SpecialType.System_UIntPtr:
                return false;
        }

        // An enum member is emitted as its numeric value. A 64-bit underlying type is already rejected
        // outright by UnsupportedFeatureScanner, so this only re-states it for a referenced enum.
        if (type.TypeKind == TypeKind.Enum)
            return (type as INamedTypeSymbol)?.EnumUnderlyingType?.SpecialType
                is not (SpecialType.System_Int64 or SpecialType.System_UInt64);

        // A C# array is a JS Array (it carries a $type stamp, which hand-written JS and JSON ignore).
        if (type is IArrayTypeSymbol array) return IsPlainJsValue(array.ElementType);

        // A delegate is a JS function — the callback slot every option bag has.
        if (type.TypeKind == TypeKind.Delegate) return true;

        // And a literal may hold another literal, which is how a nested shape is declared.
        return Emitter.IsObjectLiteralType(type);
    }

    /// <summary>The fix, on the same line as the error — MSBuild matches diagnostics per line.</summary>
    private static string Remedy(ITypeSymbol type)
    {
        // `long?` is rejected for exactly the reason `long` is, so it gets the same advice.
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && type is INamedTypeSymbol { TypeArguments.Length: 1 } nullable)
            type = nullable.TypeArguments[0];

        return type.SpecialType switch
        {
            SpecialType.System_Int64 or SpecialType.System_UInt64 =>
                "A 64-bit integer is a runtime object in JavaScript, and a plain JS number loses precision above 2^53; use int, double or string.",
            SpecialType.System_Decimal =>
                "decimal is a runtime object in JavaScript; use double or string.",
            _ =>
                "Use bool, char, string, object, a numeric type up to 32 bits (or float/double), an enum, a delegate, an array of those, or another [ObjectLiteral] type.",
        };
    }
}
