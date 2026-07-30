using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Transpose.Translator;

/// <summary>
/// Maps C# symbol names onto safe, collision-free JavaScript names.
///
/// Conventions:
///  - Types are addressed by their fully-qualified dotted name (namespace + nested chain).
///  - Fields keep their source name (prefixed backing fields for auto-props use "__").
///  - Properties are emitted as native JS accessor properties, so member access uses the
///    source name directly.
///  - Methods keep their source name; overloaded methods get a deterministic "$n" suffix.
/// </summary>
public sealed class NameMangler
{
    // reserved words / globals that must not be used bare as identifiers
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "break","case","catch","class","const","continue","debugger","default","delete","do",
        "else","export","extends","finally","for","function","if","import","in","instanceof",
        "new","return","super","switch","this","throw","try","typeof","var","void","while",
        "with","yield","let","static","enum","await","implements","package","protected",
        "interface","private","public","null","true","false","arguments","eval",
    };

    private readonly ConcurrentDictionary<ISymbol, string> _methodNameCache = new(SymbolEqualityComparer.Default);

    public static string JsIdentifier(string name)
    {
        // Strip a C# verbatim-identifier prefix (@event → event) so it never reaches JS.
        if (name.Length > 0 && name[0] == '@') name = name.Substring(1);
        return Reserved.Contains(name) ? "$" + name : name;
    }

    /// <summary>
    /// An object-literal property key: bare when it is a valid JS identifier (e.g. from a
    /// [Name]-free enum member), quoted when it contains characters JS identifiers can't
    /// (e.g. a [Name("shift-away-subtle")] enum member with hyphens).
    /// </summary>
    public static string JsPropertyKey(string name)
    {
        var ok = name.Length > 0 && (char.IsLetter(name[0]) || name[0] is '_' or '$');
        for (var i = 1; ok && i < name.Length; i++)
            ok = char.IsLetterOrDigit(name[i]) || name[i] is '_' or '$';
        return ok ? name : "\"" + name.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>
    /// Reads a member back off its declaring object: <c>Recv.name</c> when the name is a valid JS
    /// identifier, <c>Recv["name"]</c> when it is not. The counterpart to <see cref="JsPropertyKey"/>
    /// — a key that had to be quoted in the object literal cannot be read with a dot, so a
    /// <c>[Name("fi-rr-bug")]</c> enum member emitted <c>UIcons.fi-rr-bug</c>, which JS parses as
    /// subtraction and fails on at runtime ("rr is not defined").
    /// </summary>
    public static string JsMemberAccess(string receiver, string name)
    {
        var key = JsPropertyKey(name);
        return key == name ? receiver + "." + name : receiver + "[" + key + "]";
    }

    /// <summary>Fully-qualified JS name for a type, e.g. <c>App.Foo.Bar</c>.</summary>
    public string TypeFullName(INamedTypeSymbol type)
    {
        // A type-level [Transpose.Name("...")] supplies the fully-qualified JS name (namespace + entity),
        // overriding the inferred dotted name — this is how a source type maps onto a short runtime
        // name (e.g. [Name("tss.S")] class Stack → tss.S).
        if (TransposeNaming.GetName(type) is { } self) return self;

        var parts = new List<string>();

        for (INamedTypeSymbol? t = type; t is not null; t = t.ContainingType)
        {
            // An enclosing type's [Name] fixes the fully-qualified prefix; the nested leaf names
            // collected so far append under it (so [Name("tss.NodeView")] + Graph → tss.NodeView.Graph).
            if (!SymbolEqualityComparer.Default.Equals(t, type) && TransposeNaming.GetName(t) is { } enclosing)
            {
                var tail = parts.Select(JsIdentifier);
                return string.Join(".", new[] { enclosing }.Concat(tail));
            }
            // Enclosing generic types keep their arity suffix in the nested name
            // (Dictionary<K,V>.Enumerator → Dictionary$2.Enumerator); the leaf type's own arity is
            // appended by the caller.
            parts.Insert(0, SymbolEqualityComparer.Default.Equals(t, type) || t.Arity == 0
                ? t.Name
                : t.Name + "$" + t.Arity);
        }

        // A type-level [Transpose.Namespace] overrides the C# namespace: false/"" suppresses it,
        // "x.y" replaces it. Otherwise fall back to the containing C# namespace.
        if (TransposeNaming.NamespaceOverride(type) is { } nsOverride)
        {
            if (nsOverride.Length > 0)
                foreach (var seg in nsOverride.Split('.').Reverse())
                    parts.Insert(0, seg);
        }
        else
        {
            var ns = type.ContainingNamespace;
            while (ns is { IsGlobalNamespace: false })
            {
                parts.Insert(0, ns.Name);
                ns = ns.ContainingNamespace;
            }
        }

        return string.Join(".", parts.Select(JsIdentifier));
    }

    /// <summary>
    /// A JS expression that references a type object at runtime (for `new`, `is`,
    /// static member access). User types resolve to their dotted global name; BCL
    /// types resolve under the runtime's namespace tree (e.g. System.Exception).
    /// </summary>
    /// <summary>BCL types that map onto runtime-provided constructors.</summary>
    private static readonly ConcurrentDictionary<string, string> BclTypeMap = new(StringComparer.Ordinal)
    {
        ["System.Collections.Generic.List`1"] = "TransposeR.List",
        ["System.Collections.Generic.IList`1"] = "TransposeR.List",
        ["System.Collections.Generic.Dictionary`2"] = "TransposeR.Dictionary",
        ["System.Collections.Generic.HashSet`1"] = "TransposeR.HashSet",
        ["System.Collections.Generic.Queue`1"] = "TransposeR.List",
        ["System.Collections.Generic.Stack`1"] = "TransposeR.List",
        ["System.Text.StringBuilder"] = "TransposeR.StringBuilder",
    };

    public string TypeReference(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named)
        {
            if (named.Locations.Any(l => l.IsInSource))
            {
                return TypeFullName(named);
            }

            var ns = named.ContainingNamespace?.ToDisplayString();
            var metadataKey = string.IsNullOrEmpty(ns) ? named.MetadataName : ns + "." + named.MetadataName;
            if (BclTypeMap.TryGetValue(metadataKey, out var mapped))
            {
                return mapped;
            }

            // BCL/library type — reference by dotted metadata namespace + name.
            return string.IsNullOrEmpty(ns) ? named.Name : ns + "." + named.Name;
        }
        return type.Name;
    }

    public string FieldName(IFieldSymbol field) => JsIdentifier(field.Name);

    public string PropertyName(IPropertySymbol prop) => JsIdentifier(prop.Name);

    /// <summary>Backing field name for an auto-property.</summary>
    public string BackingFieldName(IPropertySymbol prop) => "__" + prop.Name;

    /// <summary>
    /// JS name for a method, disambiguating overloads with a stable suffix.
    /// </summary>
    public string MethodName(IMethodSymbol method)
    {
        method = method.OriginalDefinition;

        if (_methodNameCache.TryGetValue(method, out var cached))
        {
            return cached;
        }

        var containing = method.ContainingType;
        var baseName = method.MethodKind switch
        {
            MethodKind.Constructor => "$ctor",
            MethodKind.StaticConstructor => "$cctor",
            _ => JsIdentifier(method.Name),
        };

        // External/BCL methods map onto runtime functions by their plain name
        // (the runtime exposes a single variadic function per name), so we never
        // append overload-disambiguation suffixes for them.
        if (!method.Locations.Any(l => l.IsInSource))
        {
            _methodNameCache[method] = baseName;
            return baseName;
        }

        // Gather same-named overloads in declaration-stable order.
        var overloads = containing?.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.Name == method.Name && m.MethodKind == method.MethodKind)
            .OrderBy(m => m.Parameters.Length)
            .ThenBy(m => string.Join(",", m.Parameters.Select(p => p.Type.ToDisplayString())), StringComparer.Ordinal)
            .ToList() ?? new List<IMethodSymbol>();

        if (overloads.Count <= 1)
        {
            _methodNameCache[method] = baseName;
            return baseName;
        }

        for (var i = 0; i < overloads.Count; i++)
        {
            var name = i == 0 ? baseName : $"{baseName}${i}";
            _methodNameCache[overloads[i]] = name;
        }

        return _methodNameCache[method];
    }
}
