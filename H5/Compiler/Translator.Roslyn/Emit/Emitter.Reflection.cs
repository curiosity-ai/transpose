using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace H5.Translator.Roslyn;

public sealed partial class Emitter
{
    /// <summary>
    /// Emits H5 reflection metadata (<c>H5.setMetadata</c>) carrying each source type's
    /// custom attributes, so runtime reflection — C# <c>GetCustomAttributes</c> /
    /// <c>H5.Reflection.getAttributes</c> — returns them. Only attributes whose class is
    /// itself an emitted source type are included (so the <c>new</c> reference is guaranteed
    /// to resolve); a minimal <c>{ at: [...] }</c> payload is enough for attribute lookup.
    /// </summary>
    private void EmitReflectionMetadata(IReadOnlyList<INamedTypeSymbol> types)
    {
        var emitted = new HashSet<INamedTypeSymbol>(types, SymbolEqualityComparer.Default);
        foreach (var type in types)
        {
            var attrs = type.GetAttributes()
                .Where(a => a.AttributeClass is { } ac
                            && emitted.Contains((INamedTypeSymbol)ac.OriginalDefinition))
                .ToList();
            if (attrs.Count == 0) continue;

            var instances = string.Join(", ", attrs.Select(EmitAttributeInstance));
            _w.WriteLine(
                $"H5.setMetadata(\"{_names.TypeFullName(type)}\", " +
                $"function () {{ return {{ at: [{instances}] }}; }}, []);");
        }
    }

    /// <summary>
    /// A single custom-attribute instance: <c>new T(ctorArgs)</c>, wrapped in
    /// <c>H5.apply(..., { Named: value })</c> when the attribute sets named properties/fields.
    /// </summary>
    private string EmitAttributeInstance(AttributeData attr)
    {
        var ctorArgs = string.Join(", ", attr.ConstructorArguments.Select(TypedConstantJs));
        var ctor = $"new {TypeRef(attr.AttributeClass!)}({ctorArgs})";
        if (attr.NamedArguments.Length == 0) return ctor;

        var named = string.Join(", ", attr.NamedArguments.Select(
            kv => $"{NameMangler.JsPropertyKey(kv.Key)}: {TypedConstantJs(kv.Value)}"));
        return $"H5.apply({ctor}, {{ {named} }})";
    }

    /// <summary>An attribute argument value as JS: enums fold to their numeric value, typeof to
    /// a type reference, arrays to a JS array literal, and everything else to a plain literal.</summary>
    private string TypedConstantJs(TypedConstant c)
    {
        if (c.IsNull) return "null";
        return c.Kind switch
        {
            TypedConstantKind.Array => "[" + string.Join(", ", c.Values.Select(TypedConstantJs)) + "]",
            TypedConstantKind.Type => c.Value is ITypeSymbol t ? TypeRef(t) : "null",
            _ => ConstantLiteral(c.Value, c.Type!),
        };
    }
}
