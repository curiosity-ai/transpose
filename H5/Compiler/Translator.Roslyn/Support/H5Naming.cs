using System.Linq;
using Microsoft.CodeAnalysis;

namespace H5.Translator.Roslyn;

/// <summary>
/// Reads the H5 code-generation attributes ([Template], [Name], [External], [Script])
/// from symbols in the referenced H5 assembly, and derives JavaScript names using
/// H5's conventions. This is what lets emitted code interoperate with the h5.js runtime.
/// </summary>
internal static class H5Naming
{
    public const string TemplateAttr = "H5.TemplateAttribute";
    public const string NameAttr = "H5.NameAttribute";
    public const string ExternalAttr = "H5.ExternalAttribute";
    public const string ScriptAttr = "H5.ScriptAttribute";

    /// <summary>The [Template] JS string for a member, or null.</summary>
    public static string? GetTemplate(ISymbol symbol)
        => GetStringAttr(symbol, TemplateAttr);

    /// <summary>The explicit [Name] for a member/type, or null.</summary>
    public static string? GetName(ISymbol symbol)
        => GetStringAttr(symbol, NameAttr);

    /// <summary>
    /// Accessor-method name for an indexer element access. An indexer with a [Name]
    /// (e.g. StringBuilder's [Name("Char")]) maps to get&lt;Name&gt;/set&lt;Name&gt;
    /// (getChar/setChar); the default is getItem/setItem.
    /// </summary>
    public static string IndexerAccessorName(IPropertySymbol indexer, bool isGet)
    {
        var name = GetName(indexer);
        var suffix = name ?? "Item";
        return (isGet ? "get" : "set") + suffix;
    }

    /// <summary>True if the type (or an enclosing type) carries the [External] attribute.</summary>
    public static bool HasExternalAttribute(ITypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.ContainingType)
        {
            if (t.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == ExternalAttr))
                return true;
        }
        return false;
    }

    public static bool IsExternal(ISymbol symbol)
    {
        for (var s = symbol; s is not null; s = s.ContainingType)
        {
            if (s.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == ExternalAttr))
                return true;
        }
        // Types defined in the H5 assembly (not in user source) are external BCL.
        return !symbol.Locations.Any(l => l.IsInSource);
    }

    private static string? GetStringAttr(ISymbol symbol, string attrName)
    {
        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == attrName);
        if (attr is null || attr.ConstructorArguments.Length == 0) return null;
        return attr.ConstructorArguments[0].Value as string;
    }

    /// <summary>
    /// JavaScript member name for a member with no [Template], honouring [Name]
    /// and H5's convention (camelCase methods for library members; source members
    /// keep their C# name, with the entry point mapped to "main").
    /// </summary>
    public static string MemberJsName(ISymbol symbol)
    {
        if (symbol is IMethodSymbol m) return MethodJsName(m);

        var name = GetName(symbol);
        if (name is not null) return name;

        if (symbol.Locations.Any(l => l.IsInSource)) return symbol.Name;

        // Property / field / event: camelCase under an [External] type or a
        // [Convention] covering that member kind; otherwise preserve.
        var kindFlag = symbol.Kind switch
        {
            SymbolKind.Property => ConvProperty,
            SymbolKind.Field => ConvField,
            SymbolKind.Event => ConvEvent,
            _ => ConvAll,
        };
        var notation = MemberConventionNotation(symbol)
                       ?? ResolveNotation(symbol.ContainingType, kindFlag)
                       ?? (HasExternalAttribute(symbol.ContainingType) ? Notation.CamelCase : Notation.None);
        return Apply(notation, symbol.Name);
    }

    private static readonly System.Collections.Generic.Dictionary<ISymbol, string> _methodCache = new(SymbolEqualityComparer.Default);

    private static string MethodJsName(IMethodSymbol method)
    {
        method = method.OriginalDefinition;
        if (_methodCache.TryGetValue(method, out var cached)) return cached;

        var baseName = JsBaseName(method);

        // A canonical name (object-override, explicit/inherited [Name], or an implemented
        // interface member's name) is used verbatim — no overload suffix (H5 behaviour).
        // External non-interface types also skip suffixes.
        var declType = method.ContainingType;
        var external = declType is not null && HasExternalAttribute(declType) && declType.TypeKind != TypeKind.Interface;
        if (external || HasCanonicalName(method)) return Cache(method, baseName);

        // Overload index via H5's overload-collection ordering (override → base member),
        // grouped by the FINAL JS base name so differently-named overloads don't collide.
        var resolved = ResolveOverrideBase(method);
        var group = OverloadGroup(resolved);
        var index = group.FindIndex(x => SymbolEqualityComparer.Default.Equals(x, resolved));

        for (var i = 0; i < group.Count; i++)
        {
            if (_methodCache.ContainsKey(group[i])) continue;
            var gBase = JsBaseName(group[i]);
            _methodCache[group[i]] = i == 0 ? gBase : $"{gBase}${i}";
        }

        var result = index < 0 ? baseName : (index == 0 ? baseName : $"{baseName}${index}");
        return Cache(method, result);
    }

    // The object-method override signatures (H5 gives these canonical runtime names).
    private static bool IsObjectToString(IMethodSymbol m) => m is { Name: "ToString", Parameters.Length: 0 };
    private static bool IsObjectGetHashCode(IMethodSymbol m) => m is { Name: "GetHashCode", Parameters.Length: 0 };
    private static bool IsObjectEquals(IMethodSymbol m)
        => m is { Name: "Equals", Parameters.Length: 1 } && m.Parameters[0].Type.SpecialType == SpecialType.System_Object;

    /// <summary>
    /// The un-suffixed JS name for a method, following H5's resolution order:
    /// object-override runtime names, an explicit [Name], an inherited [Name] (from an
    /// overridden base or implemented interface member), the implemented interface member's
    /// own name, then the convention-derived name.
    /// </summary>
    private static string JsBaseName(IMethodSymbol method)
    {
        if (IsObjectToString(method)) return "toString";
        if (IsObjectGetHashCode(method)) return "getHashCode";
        if (IsObjectEquals(method)) return "equals";
        if (GetName(method) is { } explicitName) return explicitName;
        if (InheritedName(method) is { } inherited) return inherited;
        if (ImplementedInterfaceMember(method) is { } im) return JsBaseName(im);
        return Apply(MethodNotation(method), method.Name);
    }

    /// <summary>A resolved name that must be used verbatim (never overload-suffixed).</summary>
    private static bool HasCanonicalName(IMethodSymbol m)
        => IsObjectToString(m) || IsObjectGetHashCode(m) || IsObjectEquals(m)
           || GetName(m) is not null || InheritedName(m) is not null || ImplementedInterfaceMember(m) is not null;

    /// <summary>A [Name] inherited from an overridden base method or an implemented interface member.</summary>
    private static string? InheritedName(IMethodSymbol method)
    {
        for (var b = method.OverriddenMethod; b is not null; b = b.OverriddenMethod)
            if (GetName(b.OriginalDefinition) is { } n) return n;
        if (ImplementedInterfaceMember(method) is { } im && GetName(im) is { } inm) return inm;
        return null;
    }

    /// <summary>The interface member this method implements (implicitly), or null.</summary>
    private static IMethodSymbol? ImplementedInterfaceMember(IMethodSymbol method)
    {
        var type = method.ContainingType;
        if (type is null) return null;
        foreach (var iface in type.AllInterfaces)
        {
            foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
            {
                if (type.FindImplementationForInterfaceMember(member) is IMethodSymbol impl
                    && SymbolEqualityComparer.Default.Equals(impl.OriginalDefinition, method.OriginalDefinition))
                    return member.OriginalDefinition;
            }
        }
        return null;
    }

    private static string Cache(IMethodSymbol m, string name) { _methodCache[m.OriginalDefinition] = name; return name; }

    /// <summary>
    /// JS name of a constructor using H5's OverloadsCollection ordering: the first
    /// constructor is "ctor", the rest "$ctor1", "$ctor2"… This matches the names h5.js
    /// was generated with, so BCL constructions (e.g. new Guid(string) → $ctor4) resolve.
    /// </summary>
    public static string ConstructorName(IMethodSymbol ctor)
    {
        ctor = ctor.OriginalDefinition;
        var group = OverloadGroup(ctor);
        var index = group.FindIndex(x => SymbolEqualityComparer.Default.Equals(x, ctor));
        return index <= 0 ? "ctor" : "$ctor" + index;
    }

    private static IMethodSymbol ResolveOverrideBase(IMethodSymbol method)
    {
        var m = method;
        while (m.IsOverride && m.OverriddenMethod is not null && GetTemplate(m) is null)
            m = m.OverriddenMethod.OriginalDefinition;
        return m;
    }

    /// <summary>Notation for a library method: convention, else interface-inherited camelCase, else external, else preserve.</summary>
    private static Notation MethodNotation(IMethodSymbol method)
    {
        if (method.Locations.Any(l => l.IsInSource)) return Notation.None;
        var conv = ResolveNotation(method.ContainingType, ConvMethod);
        if (conv is { } c) return c;
        if (ImplementsInterfaceMember(method)) return Notation.CamelCase;
        if (HasExternalAttribute(method.ContainingType)) return Notation.CamelCase;
        return Notation.None;
    }

    private static bool ImplementsInterfaceMember(IMethodSymbol method)
    {
        var type = method.ContainingType;
        if (type is null) return false;
        foreach (var iface in type.AllInterfaces)
        {
            foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
            {
                if (type.FindImplementationForInterfaceMember(member) is IMethodSymbol im
                    && SymbolEqualityComparer.Default.Equals(im.OriginalDefinition, method.OriginalDefinition))
                    return true;
            }
        }
        return false;
    }

    // ---- overload collection (ported from H5's OverloadsCollection) --------

    private static System.Collections.Generic.List<IMethodSymbol> OverloadGroup(IMethodSymbol method)
    {
        var jsBase = JsBaseName(method);
        var isStatic = method.IsStatic;
        var kind = method.MethodKind;

        // Constructors are not inherited, so their overload set is the declaring type's own
        // ctors; ordinary methods fold in base-type members (for override-based numbering).
        var members = new System.Collections.Generic.List<IMethodSymbol>();
        var seen = new System.Collections.Generic.HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        for (var t = method.ContainingType; t is not null; t = t.BaseType)
        {
            foreach (var candidate in t.GetMembers().OfType<IMethodSymbol>())
            {
                var c = candidate.OriginalDefinition;
                if (c.MethodKind != kind || c.IsStatic != isStatic) continue;
                if (c.ExplicitInterfaceImplementations.Length > 0) continue;
                if (GetTemplate(c) is not null) continue;              // inline methods are excluded
                if (JsBaseName(c) != jsBase) continue;                 // group by final JS name
                if (c.IsOverride) continue;                            // overrides fold into their base
                if (seen.Add(c)) members.Add(c);
            }
            if (kind == MethodKind.Constructor) break;                 // ctors aren't inherited
        }

        members.Sort(CompareOverload);
        return members;
    }

    private static int CompareOverload(IMethodSymbol m1, IMethodSymbol m2)
    {
        if (!SymbolEqualityComparer.Default.Equals(m1.ContainingType, m2.ContainingType))
            return IsDerivedFrom(m1.ContainingType, m2.ContainingType) ? 1 : -1;

        var i1 = ImplementsInterfaceMember(m1);
        var i2 = ImplementsInterfaceMember(m2);
        if (i1 && !i2) return -1;
        if (i2 && !i1) return 1;

        var c1 = m1.MethodKind == MethodKind.Constructor;
        var c2 = m2.MethodKind == MethodKind.Constructor;
        if (c1 && !c2) return -1;
        if (c2 && !c1) return 1;
        // Two constructors compare by signature directly (H5 returns here, before accessibility).
        if (c1 && c2) return string.Compare(MethodToString(m1), MethodToString(m2), System.StringComparison.CurrentCulture);

        var a1 = AccessibilityWeight(m1.DeclaredAccessibility);
        var a2 = AccessibilityWeight(m2.DeclaredAccessibility);
        if (a1 != a2) return a1.CompareTo(a2);

        return string.Compare(MethodToString(m1), MethodToString(m2), System.StringComparison.CurrentCulture);
    }

    private static bool IsDerivedFrom(ITypeSymbol? derived, ITypeSymbol? base_)
    {
        for (var t = derived?.BaseType; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t, base_)) return true;
        return false;
    }

    private static int AccessibilityWeight(Accessibility a) => a switch
    {
        Accessibility.Public => 1,
        Accessibility.Internal or Accessibility.ProtectedOrInternal => 2,
        Accessibility.Protected or Accessibility.ProtectedAndInternal => 3,
        _ => 4,
    };

    private static readonly SymbolDisplayFormat FullName = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    private static string MethodToString(IMethodSymbol m)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(m.ReturnType.ToDisplayString(FullName)).Append(' ');
        sb.Append(m.Name).Append(' ');
        sb.Append(m.TypeParameters.Length).Append(' ');
        foreach (var p in m.Parameters) sb.Append(p.Type.ToDisplayString(FullName)).Append(' ');
        return sb.ToString();
    }

    // ---- [Convention] resolution ------------------------------------------

    private enum Notation { None = 0, LowerCase = 1, UpperCase = 2, CamelCase = 3, PascalCase = 4 }
    private const int ConvAll = 0, ConvMethod = 0x1, ConvProperty = 0x2, ConvField = 0x4, ConvEvent = 0x8;

    /// <summary>A [Convention] applied directly to a member (e.g. KeyValuePair.Key).</summary>
    private static Notation? MemberConventionNotation(ISymbol symbol)
    {
        var a = symbol.GetAttributes().FirstOrDefault(x => x.AttributeClass?.ToDisplayString() == ConventionAttr);
        if (a is null) return null;
        var notation = a.ConstructorArguments.Length > 0 && a.ConstructorArguments[0].Value is int cn
            ? cn
            : NamedInt(a, "Notation", (int)Notation.None);
        return (Notation)notation;
    }

    private static Notation? ResolveNotation(ITypeSymbol? type, int memberKindFlag)
    {
        if (type is null) return null;
        AttributeData? best = null;
        var bestPriority = int.MinValue;
        foreach (var a in type.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() == ConventionAttr))
        {
            var member = NamedInt(a, "Member", ConvAll);
            if (member != ConvAll && (member & memberKindFlag) == 0) continue;
            var priority = NamedInt(a, "Priority", 0);
            if (best is null || priority >= bestPriority) { best = a; bestPriority = priority; }
        }
        if (best is null) return null;
        var notation = best.ConstructorArguments.Length > 0 && best.ConstructorArguments[0].Value is int cn
            ? cn
            : NamedInt(best, "Notation", (int)Notation.None);
        return (Notation)notation;
    }

    private static int NamedInt(AttributeData a, string name, int dflt)
    {
        foreach (var na in a.NamedArguments)
            if (na.Key == name && na.Value.Value is int v) return v;
        return dflt;
    }

    private static string Apply(Notation notation, string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return notation switch
        {
            Notation.CamelCase => char.ToLowerInvariant(name[0]) + name.Substring(1),
            Notation.PascalCase => char.ToUpperInvariant(name[0]) + name.Substring(1),
            Notation.LowerCase => name.ToLowerInvariant(),
            Notation.UpperCase => name.ToUpperInvariant(),
            _ => name,
        };
    }

    public const string ConventionAttr = "H5.ConventionAttribute";

    public static string CamelCase(string s)
    {
        if (string.IsNullOrEmpty(s) || char.IsLower(s[0])) return s;
        if (s.Length == 1) return s.ToLowerInvariant();
        // Leave ALLCAPS runs mostly alone but lower the first char (H5 behavior is
        // first-letter lowercase for typical PascalCase identifiers).
        return char.ToLowerInvariant(s[0]) + s.Substring(1);
    }
}
