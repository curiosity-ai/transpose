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

        // Library members: [External] types (native JS mappings like System.String)
        // camelCase every member; other library types camelCase methods but preserve
        // property/field/event names.
        if (symbol.Kind == SymbolKind.Method || HasExternalAttribute(symbol.ContainingType))
        {
            return CamelCase(symbol.Name);
        }
        return symbol.Name;
    }

    public static string CamelCase(string s)
    {
        if (string.IsNullOrEmpty(s) || char.IsLower(s[0])) return s;
        if (s.Length == 1) return s.ToLowerInvariant();
        // Leave ALLCAPS runs mostly alone but lower the first char (H5 behavior is
        // first-letter lowercase for typical PascalCase identifiers).
        return char.ToLowerInvariant(s[0]) + s.Substring(1);
    }
}
