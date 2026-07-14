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
        var name = GetName(symbol);
        if (name is not null) return name;

        var inSource = symbol.Locations.Any(l => l.IsInSource);
        if (inSource)
        {
            // Object-method overrides map to h5's lowercase runtime names so the
            // runtime's dispatch (H5.toString/H5.equals/H5.getHashCode) finds them.
            if (symbol is IMethodSymbol m)
            {
                if (m is { Name: "ToString", Parameters.Length: 0 }) return "toString";
                if (m is { Name: "GetHashCode", Parameters.Length: 0 }) return "getHashCode";
                if (m is { Name: "Equals", Parameters.Length: 1 }) return "equals";
            }
            return symbol.Name; // preserve C# name for user members
        }

        // Library members: names follow the containing type's H5 [Convention].
        if (symbol is IMethodSymbol lm)
        {
            return Apply(MethodNotation(lm), lm.Name);
        }

        // Property / field / event: camelCase only under an [External] type or a
        // [Convention] covering that member kind; otherwise preserve.
        var kindFlag = symbol.Kind switch
        {
            SymbolKind.Property => ConvProperty,
            SymbolKind.Field => ConvField,
            SymbolKind.Event => ConvEvent,
            _ => ConvAll,
        };
        var notation = ResolveNotation(symbol.ContainingType, kindFlag)
                       ?? (HasExternalAttribute(symbol.ContainingType) ? Notation.CamelCase : Notation.None);
        return Apply(notation, symbol.Name);
    }

    /// <summary>Notation for a library method: convention, else interface-inherited camelCase, else external, else preserve.</summary>
    private static Notation MethodNotation(IMethodSymbol method)
    {
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

    // ---- [Convention] resolution ------------------------------------------

    private enum Notation { None = 0, LowerCase = 1, UpperCase = 2, CamelCase = 3, PascalCase = 4 }
    private const int ConvAll = 0, ConvMethod = 0x1, ConvProperty = 0x2, ConvField = 0x4, ConvEvent = 0x8;

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
