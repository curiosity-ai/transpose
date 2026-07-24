using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Transpose.Translator;

/// <summary>
/// Reads the Transpose code-generation attributes ([Template], [Name], [External], [Script])
/// from symbols in the referenced Transpose assembly, and derives JavaScript names using
/// Transpose's conventions. This is what lets emitted code interoperate with the tps.js runtime.
/// </summary>
internal static class TransposeNaming
{
    public const string TemplateAttr = "Transpose.TemplateAttribute";
    public const string NameAttr = "Transpose.NameAttribute";
    public const string ExternalAttr = "Transpose.ExternalAttribute";
    public const string ScriptAttr = "Transpose.ScriptAttribute";
    public const string EnumAttr = "Transpose.EnumAttribute";
    public const string ScopeAttr = "Transpose.ScopeAttribute";
    public const string GlobalMethodsAttr = "Transpose.GlobalMethodsAttribute";
    public const string ReadyAttr = "Transpose.ReadyAttribute";

    /// <summary>Allocation-free equivalent of <c>a.AttributeClass?.ToDisplayString() == fullName</c>.
    /// Attribute matching runs for every symbol reference during emit; <c>ToDisplayString</c> builds a
    /// fresh fully-qualified string on each call, so the naming layer used to allocate one string per
    /// attribute per lookup. <see cref="IsFullyQualified"/> compares the symbol's name and containing
    /// namespaces against the constant by offset instead, allocating nothing on the (dominant)
    /// non-matching path.</summary>
    public static bool AttrIs(AttributeData a, string fullName) => IsFullyQualified(a.AttributeClass, fullName);

    /// <summary>
    /// The first attribute on <paramref name="symbol"/> matching <paramref name="fullName"/>, or null.
    ///
    /// Deliberately a plain loop rather than <c>GetAttributes().FirstOrDefault(a =&gt; AttrIs(a, name))</c>:
    /// that form allocates a closure (it captures <paramref name="fullName"/>) and boxes the
    /// <see cref="ImmutableArray{T}"/>'s enumerator on *every* call, and these lookups run for
    /// essentially every symbol the emitter touches — they were the single largest allocation site in
    /// a build (~42 MB on a 69k-line project) before this became allocation-free.
    /// </summary>
    internal static AttributeData? FindAttr(ISymbol? symbol, string fullName)
    {
        if (symbol is null) return null;
        foreach (var a in symbol.GetAttributes())
            if (AttrIs(a, fullName)) return a;
        return null;
    }

    /// <summary>Whether <paramref name="symbol"/> carries <paramref name="fullName"/>. Allocation-free,
    /// for the same reason as <see cref="FindAttr"/>.</summary>
    internal static bool HasAttr(ISymbol? symbol, string fullName) => FindAttr(symbol, fullName) is not null;

    /// <summary>Whether any of <paramref name="locations"/> is in source. Allocation-free replacement
    /// for <c>Locations.Any(l =&gt; l.IsInSource)</c>, which boxes the ImmutableArray enumerator on a
    /// path the emitter takes for every symbol it names.</summary>
    internal static bool AnyInSource(ImmutableArray<Location> locations)
    {
        foreach (var l in locations)
            if (l.IsInSource) return true;
        return false;
    }

    /// <summary>True if <paramref name="cls"/>'s fully-qualified name equals <paramref name="dotted"/>
    /// (namespaces + type name, e.g. "Transpose.NameAttribute"). Walks name then containing namespaces
    /// right-to-left, comparing each dot-delimited segment against the constant by offset — no
    /// allocation. Handles only namespace-qualified top-level types (all Transpose codegen attributes
    /// are such), not nested types.</summary>
    internal static bool IsFullyQualified(INamedTypeSymbol? cls, string dotted)
    {
        if (cls is null) return false;
        var end = dotted.Length;                       // exclusive end of the current segment
        if (!SegmentEquals(dotted, ref end, cls.Name)) return false;
        for (var ns = cls.ContainingNamespace; ns is not null && !ns.IsGlobalNamespace; ns = ns.ContainingNamespace)
        {
            if (end == 0) return false;                // constant exhausted but the namespace continues
            if (!SegmentEquals(dotted, ref end, ns.Name)) return false;
        }
        return end == 0;                               // whole constant consumed, nothing left over
    }

    /// <summary>Matches the rightmost dot-delimited segment of <paramref name="dotted"/> ending at
    /// <paramref name="end"/> against <paramref name="seg"/>; on success advances <paramref name="end"/>
    /// past the preceding '.' (or to 0 at the start).</summary>
    private static bool SegmentEquals(string dotted, ref int end, string seg)
    {
        var start = end - seg.Length;
        if (start < 0) return false;
        if (start > 0 && dotted[start - 1] != '.') return false;   // must be dot-delimited
        if (string.CompareOrdinal(dotted, start, seg, 0, seg.Length) != 0) return false;
        end = start > 0 ? start - 1 : 0;               // skip the '.' for the next (outer) segment
        return true;
    }

    /// <summary>
    /// The JS scope prefix for a type marked <c>[Scope]</c>/<c>[GlobalMethods]</c> — the Transpose
    /// bindings (e.g. <c>Transpose.Core.dom</c>) that project onto ambient JS globals. Returns the
    /// scope's name argument, <c>""</c> for the global scope (no argument), or null when the
    /// type is not scoped. A scoped type's static members and nested types drop the C#
    /// type/namespace path and live under this prefix (so <c>dom.window</c> → <c>window</c>).
    /// </summary>
    public static string? ScopePrefix(ITypeSymbol? type)
    {
        if (type is null) return null;
        var scope = FindAttr(type, ScopeAttr);
        var global = HasAttr(type, GlobalMethodsAttr);
        if (scope is null && !global) return null;
        return (scope?.ConstructorArguments.FirstOrDefault().Value as string) ?? "";
    }

    /// <summary>
    /// The <c>[Enum(Emit.X)]</c> mode of an enum type (Transpose's <c>Emit</c> values:
    /// 1 Name, 2 Value, 3 StringName, 4 StringNamePreserveCase, 5 StringNameLowerCase,
    /// 6 StringNameUpperCase, 7 NamePreserveCase, 8 NameLowerCase, 9 NameUpperCase).
    /// Defaults to 7 (NamePreserveCase) when the attribute is absent, matching Transpose.
    /// </summary>
    public static int EnumEmitMode(ITypeSymbol enumType)
    {
        var a = FindAttr(enumType, EnumAttr);
        if (a is null || a.ConstructorArguments.Length == 0) return 7;
        return a.ConstructorArguments[0].Value is int m ? m : 7;
    }

    /// <summary>
    /// The string an enum member emits under a StringName mode (3–6): the member name
    /// with Transpose's per-mode casing (3 camelCases the first letter, 5 lowercases, 6 uppercases,
    /// 4 preserves), unless an explicit <c>[Name]</c> overrides it.
    /// </summary>
    public static string EnumStringName(IFieldSymbol member, int mode)
    {
        if (GetName(member) is { } named) return named;
        var name = member.Name;
        return mode switch
        {
            3 => char.ToLowerInvariant(name[0]) + name.Substring(1),
            5 => name.ToLowerInvariant(),
            6 => name.ToUpperInvariant(),
            _ => name,
        };
    }

    /// <summary>The [Template] JS string for a member, or null.</summary>
    public static string? GetTemplate(ISymbol symbol)
        => GetStringAttr(symbol, TemplateAttr);

    /// <summary>
    /// The <c>Fn</c> named argument of a member's <c>[Template]</c> — the delegate/method-group form of
    /// the template, used when the method is referenced as a method group rather than invoked (e.g.
    /// <c>bool.ToString</c> as a <c>Func&lt;string&gt;</c> must resolve to <c>System.Boolean.toString</c>,
    /// not the native <c>.toString</c>). Null when absent.
    /// </summary>
    public static string? GetTemplateFn(ISymbol? symbol)
    {
        if (symbol is null) return null;
        var attr = FindAttr(symbol, TemplateAttr);
        if (attr is null) return null;
        foreach (var na in attr.NamedArguments)
            if (na.Key == "Fn") return na.Value.Value as string;
        return null;
    }

    /// <summary>
    /// The second positional argument of a 2-arg <c>[Template(format, nonExpandedFormat)]</c> — the
    /// template to use when the method's trailing <c>params</c> argument is supplied NON-expanded (a
    /// single array passed directly) rather than as individual elements. E.g. MethodInfo.Invoke uses
    /// <c>midel(this,obj).apply(null, {arguments:array})</c> instead of <c>midel(this,obj)({*arguments})</c>.
    /// Null when the attribute has fewer than two positional string arguments.
    /// </summary>
    public static string? GetTemplateNonExpanded(ISymbol? symbol)
    {
        if (symbol is null) return null;
        var attr = FindAttr(symbol, TemplateAttr);
        if (attr is null || attr.ConstructorArguments.Length < 2) return null;
        return attr.ConstructorArguments[1].Value as string;
    }

    /// <summary>The explicit [Name] for a member/type, or null.</summary>
    public static string? GetName(ISymbol symbol)
        => GetStringAttr(symbol, NameAttr);

    /// <summary>
    /// The explicit [Name] that renames a property/event's JS slot: the member's own [Name] if
    /// present, otherwise the [Name] on one of its accessors (property get/set, event add/remove).
    /// h5 allows <c>[Name]</c> on an accessor to rename the whole member — e.g. Tesserae's
    /// <c>ReadOnlyArray&lt;T&gt;.Length</c> whose getter is <c>[Name("length")]</c> so the access hits
    /// the native JS array <c>.length</c>. Roslyn attaches an accessor-level attribute to the accessor
    /// method, not the property/event, so a member-only lookup (which happened to work for source
    /// [External] types via the camelCase convention) silently emitted <c>.Length</c> for a referenced
    /// non-external type and broke <c>.Where(g => g.Values.Length > 0)</c>.
    /// </summary>
    public static string? PropertyEffectiveName(ISymbol symbol)
    {
        if (GetName(symbol) is { } own) return own;
        if (symbol is IPropertySymbol { IsIndexer: false } p)
            return (p.GetMethod is { } g ? GetName(g) : null) ?? (p.SetMethod is { } s ? GetName(s) : null);
        if (symbol is IEventSymbol e)
            return (e.AddMethod is { } a ? GetName(a) : null) ?? (e.RemoveMethod is { } r ? GetName(r) : null);
        return null;
    }

    /// <summary>
    /// The raw-JavaScript body lines of a member's <c>[Transpose.Script(...)]</c>, or null when
    /// absent. When present, these lines become the member's body verbatim and the C# body (if any)
    /// is discarded — the mechanism for hand-writing a method/accessor/operator implementation.
    /// </summary>
    public static string[]? GetScriptBody(ISymbol symbol)
    {
        var a = FindAttr(symbol, ScriptAttr);
        if (a is null || a.ConstructorArguments.Length == 0) return null;
        var arg = a.ConstructorArguments[0];
        if (arg.Kind == TypedConstantKind.Array)
            return arg.Values.Select(v => v.Value as string ?? "").ToArray();
        return arg.Value is string s ? new[] { s } : null;
    }

    public const string NamespaceAttr = "Transpose.NamespaceAttribute";

    /// <summary>
    /// The effect of a type-level <c>[Transpose.Namespace]</c> on the emitted namespace prefix:
    /// <list type="bullet">
    /// <item><c>null</c> — no attribute; use the C# namespace.</item>
    /// <item><c>""</c> (empty) — suppress the namespace entirely, so the type emits under its bare
    /// entity name (<c>[Namespace(false)]</c> or <c>[Namespace("")]</c>). This is how the
    /// Transpose.Core primitive bindings (String, Number, Object, …) map onto the JS globals.</item>
    /// <item><c>"x.y"</c> — replace the C# namespace with this custom one (<c>[Namespace("x.y")]</c>).</item>
    /// </list>
    /// The attribute is read from the outermost enclosing type (a namespace belongs to the top-level
    /// type), so a nested type inherits its container's namespace treatment.
    /// </summary>
    public static string? NamespaceOverride(ITypeSymbol? type)
    {
        if (type is null) return null;
        var outer = type;
        while (outer.ContainingType is { } ct) outer = ct;
        var a = FindAttr(outer, NamespaceAttr);
        if (a is null || a.ConstructorArguments.Length == 0) return null;
        var arg = a.ConstructorArguments[0].Value;
        // [Namespace(false)] suppresses; [Namespace(true)] is the default (no override).
        if (arg is bool b) return b ? null : "";
        // [Namespace("x")] sets a custom namespace; [Namespace("")] also suppresses.
        return arg as string;
    }

    public const string ReflectableAttr = "Transpose.ReflectableAttribute";

    /// <summary>
    /// An explicit <c>[Transpose.Reflectable]</c> override on a type or member: <c>true</c> forces
    /// reflection metadata to be emitted, <c>false</c> suppresses it, <c>null</c> means no attribute
    /// (fall back to the default policy). The boolean constructor argument decides; the argument-less
    /// <c>[Reflectable]</c> — and the advanced filter/accessibility forms — mean <c>true</c>.
    /// </summary>
    public static bool? ReflectableOverride(ISymbol symbol)
    {
        var a = FindAttr(symbol, ReflectableAttr);
        if (a is null) return null;
        if (a.ConstructorArguments.Length > 0 && a.ConstructorArguments[0].Value is bool b) return b;
        return true;
    }

    public const string AccessorsIndexerAttr = "Transpose.AccessorsIndexerAttribute";

    /// <summary>
    /// True if an indexer maps to native JS bracket access (<c>obj[key]</c>) rather than
    /// getItem/setItem accessors — the case for an [External] type's plain indexer (e.g. a DOM
    /// element's <c>this[string]</c>), unless it opts into accessors via [AccessorsIndexer]/[Name]
    /// or a [Template].
    /// </summary>
    public static bool IsNativeIndexer(IPropertySymbol indexer)
    {
        // Native bracket access (obj[key]) is used by an [External] non-interface type — the
        // native JS objects like a DOM element's this[string]. The [External] BCL *collection
        // interfaces* (IReadOnlyList/IReadOnlyDictionary) still route through getItem/setItem.
        if (!indexer.IsIndexer) return false;
        // External (or scope-bound, e.g. a DOM NodeList under Transpose.Core.dom's [Scope]) non-interface
        // types are native JS objects, so their indexer is bracket access. Real Transpose runtime
        // collection classes (List<T>, Dictionary<,>, …) are not external and keep getItem/setItem.
        if (indexer.ContainingType is not { TypeKind: TypeKind.Class } ct || !IsExternalType(ct)) return false;
        if (HasAttr(indexer.ContainingType, AccessorsIndexerAttr)) return false;
        if (GetName(indexer) is not null) return false;
        if (GetTemplate(indexer.GetMethod?.OriginalDefinition) is not null) return false;
        if (GetTemplate(indexer.SetMethod?.OriginalDefinition) is not null) return false;
        return true;
    }

    /// <summary>
    /// Accessor-method name for an indexer element access. An indexer with a [Name]
    /// (e.g. StringBuilder's [Name("Char")]) maps to get&lt;Name&gt;/set&lt;Name&gt;
    /// (getChar/setChar); the default is getItem/setItem.
    /// </summary>
    public static string IndexerAccessorName(IPropertySymbol indexer, bool isGet)
    {
        var name = GetName(indexer);
        var suffix = name ?? "Item";
        var accessor = (isGet ? "get" : "set") + suffix;

        // An explicit interface indexer (e.g. `object IDictionary.this[object]`) takes the
        // interface-qualified mangled slot (Namespace$IFace$getItem/$setItem) so it does not
        // collide with the public indexer's getItem/setItem — otherwise the public and explicit
        // setters share one JS name and the explicit body's `this[k] = v` recurses into itself.
        if (ExplicitlyImplementedMember(indexer)?.ContainingType is { } eiface)
            return MangledTypeName(eiface) + "$" + accessor;

        // Access through a *source* interface's indexer resolves to the mangled slot every
        // implementer aliases; BCL interfaces keep the plain accessor (their implementers expose
        // it directly), mirroring MemberJsName.
        if (indexer.ContainingType is { TypeKind: TypeKind.Interface } iface && IsSourceInterface(iface))
            return MangledTypeName(iface) + "$" + accessor;

        return accessor;
    }

    /// <summary>
    /// True if the type behaves like an external JS type for naming (no overload suffixes,
    /// camelCase members): it carries [External], or it is projected onto ambient JS globals
    /// via a [Scope]/[GlobalMethods] binding (e.g. the DOM types under Transpose.Core.dom).
    /// </summary>
    public static bool IsExternalType(ITypeSymbol? type)
    {
        if (HasExternalAttribute(type)) return true;
        for (var t = type; t is not null; t = t.ContainingType)
            if (ScopePrefix(t) is not null) return true;
        return false;
    }

    /// <summary>
    /// True if a type is emitted by an Transpose compiler with source naming conventions — either it is
    /// in this compilation's source, or it lives in a referenced *user library* assembly (one
    /// compiled with --emit-package). It is false for external/DOM types and for the Transpose runtime
    /// assemblies (Transpose, Transpose.Core, …) whose BCL types are baked into tps.js with fixed names. This is
    /// the discriminator for names that must agree between a library and the projects that
    /// reference it — preserving existing source/BCL behaviour while treating a referenced library
    /// the same as source.
    /// </summary>
    public static bool IsTransposeCompiledSource(ITypeSymbol? type)
    {
        if (type is null) return false;
        // Runtime/BCL types (assembly "Transpose" / "Transpose.*") always use the fixed library
        // naming conventions (camelCase interface members, etc.) — whether they are referenced OR
        // in-source (when self-building the base runtime). This keeps a self-built tps.js consistent
        // with the hand-written primitives and with how user code calls the referenced BCL.
        if (IsTransposeRuntimeAssembly(type.ContainingAssembly)) return false;
        if (IsExternalType(type)) return false;
        if (AnyInSource(type.Locations)) return true;
        return true; // referenced user library (compiled with --emit-package)
    }

    /// <summary>An Transpose runtime/BCL package (Transpose.dll, Transpose.Core.dll, Transpose.Newtonsoft.Json.dll, …) whose
    /// types are provided pre-compiled by the runtime, as opposed to a user library.</summary>
    private static bool IsTransposeRuntimeAssembly(IAssemblySymbol? asm)
    {
        var n = asm?.Name;
        return n == "Transpose" || (n is not null && n.StartsWith("Transpose.", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// True if an interface should be listed in a type's <c>inherits</c> so the runtime tracks it
    /// for <c>is</c>/<c>as</c> and interface dispatch. This is every implemented interface that the
    /// runtime registers: source/referenced-library interfaces AND the Transpose BCL interfaces
    /// (System.Collections.Generic.IList/ICollection/IEnumerable, IComparable, …) which are real
    /// Transpose.define'd types. Only ambient DOM/scoped interfaces are excluded (they are native JS, not
    /// Transpose-registered). Omitting the BCL collection interfaces breaks e.g. LINQ over a user
    /// collection: <c>Enumerable.from(x)</c> tests <c>x is IEnumerable</c> before enumerating it.
    /// </summary>
    public static bool IsInheritableInterface(ITypeSymbol? i)
    {
        if (i is not { TypeKind: TypeKind.Interface }) return false;
        if (IsScopedType(i)) return false;                       // DOM / ambient JS — not registered
        if (IsTransposeCompiledSource(i)) return true;                  // source or referenced user library
        return IsTransposeRuntimeAssembly(i.ContainingAssembly);        // Transpose BCL interface (IList, IEnumerable, …)
    }

    /// <summary>True if the type (or an enclosing type) is a [Scope]/[GlobalMethods] binding
    /// projected onto ambient JS (e.g. the DOM types under Transpose.Core.dom).</summary>
    public static bool IsScopedType(ITypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.ContainingType)
            if (ScopePrefix(t) is not null) return true;
        return false;
    }

    /// <summary>True if the type (or an enclosing type) carries the [External] attribute.</summary>
    public static bool HasExternalAttribute(ITypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.ContainingType)
        {
            if (HasAttr(t, ExternalAttr))
                return true;
        }
        // Assembly-level [assembly: External] (how binding libraries such as Transpose.Core mark
        // every type external) — synthesized from the csproj's <AssemblyAttribute> items.
        return AssemblyHasExternalAttribute(type?.ContainingAssembly);
    }

    /// <summary>True if the assembly carries <c>[assembly: Transpose.External]</c>.</summary>
    public static bool AssemblyHasExternalAttribute(IAssemblySymbol? asm)
        => HasAttr(asm, ExternalAttr);

    public static bool IsExternal(ISymbol symbol)
    {
        for (var s = symbol; s is not null; s = s.ContainingType)
        {
            if (HasAttr(s, ExternalAttr))
                return true;
        }
        // Types defined in the Transpose assembly (not in user source) are external BCL.
        return !AnyInSource(symbol.Locations);
    }

    private static string? GetStringAttr(ISymbol? symbol, string attrName)
    {
        var attr = FindAttr(symbol, attrName);
        if (attr is null || attr.ConstructorArguments.Length == 0) return null;
        return attr.ConstructorArguments[0].Value as string;
    }

    /// <summary>
    /// JavaScript member name for a member with no [Template], honouring [Name]
    /// and Transpose's convention (camelCase methods for library members; source members
    /// keep their C# name, with the entry point mapped to "main").
    /// </summary>
    public static string MemberJsName(ISymbol symbol)
        => _memberCache.TryGetValue(symbol, out var cached) ? cached : _memberCache[symbol] = MemberJsNameCore(symbol);

    /// <summary>
    /// Resolved JS member names, cached per symbol.
    ///
    /// <see cref="MemberJsNameCore"/> is a pure function of the symbol but a genuinely expensive one:
    /// for a property/field it walks <c>AllInterfaces</c> and every interface's members to decide
    /// whether the member must yield its plain slot, and for a method it can build and sort the whole
    /// overload group. The emitter asks for the same member's name once per *reference* in the source,
    /// so on a real project this recomputation dominated allocation (~164 MB on a 69k-line project).
    /// Methods keep their own cache inside <see cref="MethodJsName"/> (keyed on the original
    /// definition, and populated a group at a time); this one covers every symbol kind.
    ///
    /// Keyed on the symbol exactly as passed, not on its original definition: a constructed generic's
    /// member is a distinct symbol and caching it separately cannot change an answer, whereas
    /// normalising here would.
    /// </summary>
    private static readonly ConcurrentDictionary<ISymbol, string> _memberCache = new(SymbolEqualityComparer.Default);

    private static string MemberJsNameCore(ISymbol symbol)
    {
        // An explicit interface implementation is named by the interface-qualified mangled
        // name Transpose uses (e.g. IMyInterface.Method → Namespace$IMyInterface$method), so it
        // stays a valid JS identifier and matches the runtime's interface-member slot.
        if (ExplicitInterfaceMangledName(symbol) is { } explicitName) return explicitName;

        // A member accessed through a *source* interface type resolves to the interface's own
        // member; Transpose stores it under a mangled slot (Namespace$IFace$member) that every
        // implementer aliases (see InterfaceAliasPairs), so route the access there — this is
        // what reaches explicit implementations. BCL interfaces keep the plain name: their
        // implementers (tps.js types) expose it directly, so plain access already resolves.
        if (symbol.ContainingType is { TypeKind: TypeKind.Interface } iface && IsSourceInterface(iface))
            return MangledTypeName(iface) + "$" + LeafJsName(symbol);

        return EscapeStaticReserved(symbol, LeafJsName(symbol));
    }

    // Function own-properties: a static member lives on the type's constructor FUNCTION, so a static
    // member named one of these would clash with the (read-only) function property (e.g. Class.name).
    private static readonly HashSet<string> _functionReserved = new(System.StringComparer.Ordinal)
        { "name", "length", "caller", "arguments", "prototype", "constructor" };

    /// <summary>Prefixes a static member whose JS name collides with a Function own-property
    /// (name/length/…) with <c>$</c> — matching the reference runtime (enum member `name` → `$name`).</summary>
    private static string EscapeStaticReserved(ISymbol symbol, string name)
        => symbol.IsStatic && _functionReserved.Contains(name) ? "$" + name : name;

    /// <summary>The un-mangled JS name of a member (ignoring interface qualification).</summary>
    private static string LeafJsName(ISymbol symbol)
    {
        if (symbol is IMethodSymbol m) return MethodJsName(m);

        var raw = RawMemberName(symbol);

        // A property/field/event that hides a same-named base member (C# `new`) takes its own
        // slot via a $N suffix (as Transpose does), so the hiding member and the base member don't
        // collide — and base access (base.X, emitted as this.X) still reaches the base slot.
        // Only Transpose-compiled members (source or referenced library) get a slot suffix: an
        // external/native member (e.g. a DOM NodeListOf<T>.length hiding NodeList.length) maps to
        // a single fixed JS property, so suffixing it would reference a nonexistent property.
        if (PropertyEffectiveName(symbol) is null
            && symbol is IPropertySymbol { IsIndexer: false } or IFieldSymbol or IEventSymbol)
        {
            // Hiding suffix is only meaningful for Transpose-compiled members (external/native
            // members map to a single fixed JS property).
            var idx = IsTransposeCompiledSource(symbol.ContainingType) ? HidingIndex(symbol, raw) : 0;

            // A member (typically a private backing field like Dictionary's `keys`/`values`) whose
            // plain slot collides with the plain-access name of a BCL interface member the type
            // implements at a *different* slot must yield that slot: BCL interface dispatch reads the
            // plain name (e.g. `d.values` for IReadOnlyDictionary<,>.Values), which must reach the
            // interface getter (installed as an alias), not the shadowing field. This also applies to
            // runtime/BCL members (Dictionary lives in the runtime assembly), so it is not gated on
            // IsTransposeCompiledSource.
            if (idx == 0 && YieldsToBclInterfaceSlot(symbol, raw)) idx = 1;

            if (idx > 0) return raw + "$" + idx;
        }
        return raw;
    }

    /// <summary>
    /// True when <paramref name="raw"/> (this member's plain JS slot) is the plain-access name of a
    /// non-templated BCL interface member the containing type implements via a *different* member —
    /// so this member must move off the slot to leave it for interface dispatch (see the alias in
    /// <see cref="InterfaceAliasPairs"/>). Skips source interfaces (they dispatch via mangled slots,
    /// so no plain-name collision) and members that themselves implement the interface member.
    /// </summary>
    private static bool YieldsToBclInterfaceSlot(ISymbol symbol, string raw)
    {
        if (symbol.ContainingType is not { } type) return false;
        if (type.TypeKind == TypeKind.Interface) return false; // interface members never yield

        foreach (var iface in type.AllInterfaces)
        {
            if (IsSourceInterface(iface)) continue;         // mangled dispatch — no plain collision
            if (!IsInheritableInterface(iface)) continue;   // only Transpose-registered BCL interfaces

            foreach (var member in iface.GetMembers())
            {
                if (member.IsStatic) continue;
                if (member is IMethodSymbol { MethodKind: not MethodKind.Ordinary }) continue; // accessors
                if (member is not (IPropertySymbol { IsIndexer: false } or IMethodSymbol or IEventSymbol)) continue;
                if (GetTemplate(member) is not null) continue;   // templated access — no object slot
                if (RawMemberName(member) != raw) continue;      // plain-access name differs

                // If this very member implements the interface member, it legitimately owns the slot.
                var impl = type.FindImplementationForInterfaceMember(member);
                if (impl is not null && SymbolEqualityComparer.Default.Equals(impl, symbol)) continue;
                if (impl is null) continue;                      // unimplemented (shouldn't happen) — leave as-is

                return true;
            }
        }
        return false;
    }

    /// <summary>The base JS name of a property/field/event (before any hiding suffix).</summary>
    private static string RawMemberName(ISymbol symbol)
    {
        if (PropertyEffectiveName(symbol) is { } name) return name;

        // Enum members honour the [Enum(Emit.Name*)] casing modes on their own JS name: NameLowerCase
        // (8) lowercases, NameUpperCase (9) uppercases; Name (1) / NamePreserveCase (7) and every
        // other mode preserve. (The StringName* modes 3–6 cast the emitted string VALUE — see
        // EnumStringName — not the member's JS name, which stays verbatim.)
        if (symbol is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } enumMember)
        {
            return EnumEmitMode(enumMember.ContainingType) switch
            {
                8 => enumMember.Name.ToLowerInvariant(),
                9 => enumMember.Name.ToUpperInvariant(),
                _ => enumMember.Name,
            };
        }

        // A compiled type's member keeps its verbatim C# name. An [External] type's member does
        // NOT short-circuit here even when in source (self-building the BCL): its members bind to
        // native JS names via [Convention]/casing — e.g. String.Length ([External] +
        // [Convention(CamelCase)]) must emit `length` to hit the native JS string property.
        if (AnyInSource(symbol.Locations) && !IsExternalType(symbol.ContainingType))
            return symbol.Name;

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
                       ?? (IsExternalType(symbol.ContainingType) ? Notation.CamelCase : Notation.None);
        return Apply(notation, symbol.Name);
    }

    /// <summary>How many base-type members this member hides (share its slot name). Zero for an
    /// override (which shares the base slot) or a member that introduces a fresh name.</summary>
    private static int HidingIndex(ISymbol symbol, string raw)
    {
        if (symbol is IPropertySymbol { IsOverride: true } or IEventSymbol { IsOverride: true }) return 0;
        var count = 0;
        for (var t = symbol.ContainingType?.BaseType; t is not null; t = t.BaseType)
        {
            foreach (var bm in t.GetMembers())
            {
                if (bm.IsStatic || bm.Kind != symbol.Kind) continue;
                if (bm is IPropertySymbol { IsIndexer: true }) continue;
                if (RawMemberName(bm) == raw) { count++; break; }
            }
        }
        return count;
    }

    /// <summary>
    /// The mangled JS name of an explicit interface implementation
    /// (<c>Namespace$IFace$member</c>), or null when the member isn't an explicit impl.
    /// </summary>
    private static string? ExplicitInterfaceMangledName(ISymbol symbol)
    {
        var im = ExplicitlyImplementedMember(symbol);
        if (im?.ContainingType is not { } iface) return null;
        return MangledTypeName(iface) + "$" + LeafJsName(im.OriginalDefinition);
    }

    /// <summary>The interface member a symbol explicitly implements, or null.</summary>
    private static ISymbol? ExplicitlyImplementedMember(ISymbol symbol) => symbol switch
    {
        IMethodSymbol m when m.ExplicitInterfaceImplementations.Length > 0 => m.ExplicitInterfaceImplementations[0],
        IPropertySymbol p when p.ExplicitInterfaceImplementations.Length > 0 => p.ExplicitInterfaceImplementations[0],
        IEventSymbol e when e.ExplicitInterfaceImplementations.Length > 0 => e.ExplicitInterfaceImplementations[0],
        _ => null,
    };

    /// <summary>The mangled interface-member slot name (<c>Namespace$IFace$member</c>).</summary>
    public static string InterfaceMemberName(ISymbol interfaceMember)
        => MangledTypeName(interfaceMember.ContainingType) + "$" + LeafJsName(interfaceMember.OriginalDefinition);

    /// <summary>The un-mangled JS name of a member (its slot on the declaring class).</summary>
    public static string LeafMemberName(ISymbol symbol) => LeafJsName(symbol);

    /// <summary>
    /// The (name, aliasSlot) aliases a type must publish so that a member accessed through one of
    /// its interfaces resolves to the implementing member. Two shapes:
    /// <list type="bullet">
    /// <item><b>Source interfaces</b> — access uses the mangled interface slot (see MemberJsName),
    /// so an *implicit* implementation (which lives under its plain slot) publishes
    /// <c>(plain, mangled)</c> to expose the mangled slot. Explicit impls already emit under the
    /// mangled slot; inherited impls are aliased by the base type.</item>
    /// <item><b>BCL/runtime interfaces</b> — access uses the interface member's plain camelCase name
    /// (see MemberJsName). A member whose implementing slot differs from that plain name publishes
    /// <c>(implSlot, plainName)</c> to expose it. This covers an *explicit* impl (which lives only
    /// under the mangled slot, e.g. <c>IReadOnlyDictionary&lt;K,V&gt;.Values</c> on Dictionary) and
    /// an *implicit* property/event impl whose own slot keeps its PascalCase C# name (e.g.
    /// <c>SortedList.Keys</c> at slot <c>Keys</c>, reached as <c>keys</c> through IDictionary — only
    /// methods inherit the interface's camelCase name, so property/event access needs the alias).</item>
    /// </list>
    /// Transpose's <c>alias</c> config installs these on the prototype (it looks up the descriptor by
    /// the first element and defines the second as an alias of it).
    /// </summary>
    public static System.Collections.Generic.List<(string plain, string mangled)> InterfaceAliasPairs(INamedTypeSymbol type)
    {
        var pairs = new System.Collections.Generic.List<(string, string)>();
        var seen = new System.Collections.Generic.HashSet<string>();
        // Plain BCL-interface alias names already claimed, so multiple interfaces that share a
        // camelCase name (e.g. IDictionary<K,V>.Values and IReadOnlyDictionary<K,V>.Values both →
        // "values") publish it once — they resolve to equivalent members anyway.
        var bclAliasNames = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);

        foreach (var iface in type.AllInterfaces)
        {
            var sourceIface = IsSourceInterface(iface);
            // A runtime BCL interface (IList, IReadOnlyDictionary, …) dispatches through plain names.
            var bclIface = !sourceIface && IsInheritableInterface(iface);
            if (!sourceIface && !bclIface) continue; // ambient/DOM interface — not Transpose-dispatched

            foreach (var member in iface.GetMembers())
            {
                if (member.IsStatic) continue;
                if (member is IMethodSymbol { MethodKind: not MethodKind.Ordinary }) continue; // skip accessors
                if (member is not (IMethodSymbol or IPropertySymbol or IEventSymbol)) continue;
                if (GetTemplate(member) is not null) continue;   // templated members apply at the call site

                if (type.FindImplementationForInterfaceMember(member) is not { } impl) continue;
                if (!SymbolEqualityComparer.Default.Equals(impl.ContainingType, type)) continue; // declared here

                var isExplicit = ExplicitlyImplementedMember(impl) is not null;

                if (sourceIface)
                {
                    if (isExplicit) continue;                    // already mangled

                    var plain = LeafJsName(impl);
                    var mangled = InterfaceMemberName(member);
                    if (plain == mangled) continue;
                    if (seen.Add(plain + "\0" + mangled)) pairs.Add((plain, mangled));
                }
                else
                {
                    // BCL interface: the call site uses the member's plain camelCase name. Alias that
                    // plain name onto the implementing slot whenever they differ — for an explicit
                    // impl (mangled slot) or an implicit property/event impl that kept its PascalCase
                    // C# name. An implicit method impl already inherits the interface's camelCase name
                    // (plainAccess == implSlot), so it needs no alias.
                    var plainAccess = MemberJsName(member);                         // name emitted at call sites
                    var implSlot = isExplicit ? InterfaceMemberName(member)         // mangled explicit slot
                                              : LeafJsName(impl);                   // implicit impl's own slot
                    if (plainAccess == implSlot) continue;
                    if (!bclAliasNames.Add(plainAccess)) continue; // one alias per plain name
                    if (seen.Add(implSlot + "\0" + plainAccess)) pairs.Add((implSlot, plainAccess));
                }
            }
        }
        return pairs;
    }

    /// <summary>A user-defined (source) interface — its members are dispatched via mangled
    /// interface slots; BCL interfaces resolve through their implementers' plain names.</summary>
    private static bool IsSourceInterface(INamedTypeSymbol iface)
        // A user-defined interface — whether from this compilation's source or a referenced
        // Transpose-compiled assembly (both mangle their members the same way). Only truly external
        // (BCL/DOM) interfaces resolve through their implementers' plain names.
        => iface.TypeKind == TypeKind.Interface && IsTransposeCompiledSource(iface);

    /// <summary>Full type name as a single JS identifier: dotted segments joined by <c>$</c>,
    /// with a generic arity suffix (e.g. <c>System$Collections$Generic$IComparer$1</c>). Honours a
    /// type-level <c>[Transpose.Name]</c> (its dotted value becomes the mangled prefix, e.g.
    /// <c>[Name("tss.IC")]</c> → <c>tss$IC</c>), so interface-member slots stay consistent with the
    /// type's registered name.</summary>
    private static string MangledTypeName(INamedTypeSymbol type)
    {
        if (MangledSelf(type) is { } named) return named;
        var parts = new System.Collections.Generic.List<string>();
        for (INamedTypeSymbol? t = type; t is not null; t = t.ContainingType)
        {
            if (!SymbolEqualityComparer.Default.Equals(t, type) && MangledSelf(t) is { } enclosing)
            {
                var tail = string.Join("$", parts);
                return tail.Length == 0 ? enclosing : enclosing + "$" + tail;
            }
            parts.Insert(0, t.Arity > 0 ? t.Name + "$" + t.Arity : t.Name);
        }
        for (var ns = type.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
            parts.Insert(0, ns.Name);
        return string.Join("$", parts);
    }

    /// <summary>The mangled form of a type's own <c>[Name]</c> (dotted → <c>$</c>, arity appended),
    /// or null when the type has no <c>[Name]</c>.</summary>
    private static string? MangledSelf(INamedTypeSymbol type)
    {
        if (GetName(type) is not { } n) return null;
        var mangled = n.Replace('.', '$');
        return type.Arity > 0 ? mangled + "$" + type.Arity : mangled;
    }

    private static readonly ConcurrentDictionary<ISymbol, string> _methodCache = new(SymbolEqualityComparer.Default);

    private static string MethodJsName(IMethodSymbol method)
    {
        method = method.OriginalDefinition;
        if (_methodCache.TryGetValue(method, out var cached)) return cached;

        var baseName = JsBaseName(method);

        // A canonical name (object-override, explicit/inherited [Name], or an implemented
        // interface member's name) is used verbatim — no overload suffix (Transpose behaviour).
        // External non-interface types also skip suffixes.
        var declType = method.ContainingType;
        var external = declType is not null && IsExternalType(declType) && declType.TypeKind != TypeKind.Interface;

        // Extern (no IL body) methods on a non-external type are hand-written-JS backed:
        // Transpose excludes them from the overload set, so they carry no suffix.
        if (external || HasCanonicalName(method) || HasNoBody(method)) return Cache(method, baseName);

        // Overload index via Transpose's overload-collection ordering (override → base member),
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

    // The object-method override signatures (Transpose gives these canonical runtime names).
    private static bool IsObjectToString(IMethodSymbol m) => m is { Name: "ToString", Parameters.Length: 0 };
    private static bool IsObjectGetHashCode(IMethodSymbol m) => m is { Name: "GetHashCode", Parameters.Length: 0 };
    private static bool IsObjectEquals(IMethodSymbol m)
        => m is { Name: "Equals", Parameters.Length: 1 } && m.Parameters[0].Type.SpecialType == SpecialType.System_Object;

    /// <summary>
    /// The un-suffixed JS name for a method, following Transpose's resolution order:
    /// object-override runtime names, an explicit [Name], an inherited [Name] (from an
    /// overridden base or implemented interface member), the implemented interface member's
    /// own name, then the convention-derived name.
    ///
    /// Cached per method: <see cref="OverloadGroup"/> calls this for every candidate method in every
    /// base type in order to group overloads by their final JS name, and it recurses into interface
    /// members, so the same method's base name was being derived many times over.
    /// </summary>
    private static readonly ConcurrentDictionary<IMethodSymbol, string> _baseNameCache = new(SymbolEqualityComparer.Default);

    private static string JsBaseName(IMethodSymbol method)
        => _baseNameCache.TryGetValue(method, out var cached) ? cached : _baseNameCache[method] = JsBaseNameCore(method);

    /// <inheritdoc cref="JsBaseName"/>
    private static string JsBaseNameCore(IMethodSymbol method)
    {
        if (IsObjectToString(method)) return "toString";
        if (IsObjectGetHashCode(method)) return "getHashCode";
        if (IsObjectEquals(method)) return "equals";
        if (GetName(method) is { } explicitName) return explicitName;
        if (InheritedName(method) is { } inherited) return inherited;
        if (ImplementedInterfaceMember(method) is { } im)
        {
            var imName = JsBaseName(im);
            // If the interface member carries no naming rule of its own (its JS name is just the raw
            // C# name, e.g. IDisposable.Dispose) and is not templated, but the IMPLEMENTING type (or
            // the method) declares an explicit [Convention], the implementer's runtime slot follows
            // that convention — CancellationTokenSource's CamelCase makes Dispose -> "dispose", the
            // slot the hand-written runtime (and the legacy compiler) use, so `cts.Dispose()` resolves.
            // When the interface member DOES have a rule — a [Convention] (IEnumerator.MoveNext ->
            // "moveNext"), a [Name], or a [Template] (IEnumerable.GetEnumerator) — inherit it so
            // interface dispatch and the runtime lookup stay aligned.
            if (GetTemplate(im) is null && imName == im.Name
                && (MemberConventionNotation(method) ?? ResolveNotation(method.ContainingType, ConvMethod)) is { } conv)
                return Apply(conv, method.Name);
            return imName;
        }
        return Apply(MethodNotation(method), method.Name);
    }

    /// <summary>A resolved name that must be used verbatim (never overload-suffixed).</summary>
    private static bool HasCanonicalName(IMethodSymbol m)
        => IsObjectToString(m) || IsObjectGetHashCode(m) || IsObjectEquals(m)
           || GetName(m) is not null || InheritedName(m) is not null;
    // Note: a plain interface implementation is NOT canonical — Transpose still numbers it through the
    // overload collection (so a type implementing e.g. both IDictionary.Add(k,v) and
    // ICollection.Add(KeyValuePair) gets add / add$1, not two colliding `add` keys). A lone
    // interface implementation still lands at index 0 and keeps its bare name.

    /// <summary>
    /// True if this is a library (Transpose.dll) method with no IL body and not abstract — a C#
    /// <c>extern</c> member backed by hand-written runtime JS (e.g. <c>Regex.Replace</c>).
    /// Transpose leaves such members out of a non-external type's overload set, so they take the
    /// bare convention name with no <c>$N</c> suffix.
    /// </summary>
    public static bool HasNoBody(IMethodSymbol method)
    {
        if (AnyInSource(method.Locations))
        {
            // Self-building the BCL (--build-runtime): the runtime types are in source, but their
            // `extern` members are hand-written-JS backed exactly as when referenced from metadata,
            // so exclude them from overload numbering too. Without this the extern overloads inflate
            // the group and shift the emittable overloads' $N suffixes off the call-site numbering
            // (e.g. Console.WriteLine(object) → WriteLine$5 instead of the [Name]-fixed WriteLine).
            return method.ContainingAssembly?.Name == "Transpose" && method.IsExtern;
        }
        if (method.ContainingAssembly?.Name != "Transpose") return false;
        return TransposeAssemblies.NoBodyMethodTokens.Contains(method.OriginalDefinition.MetadataToken);
    }

    /// <summary>A [Name] inherited from an overridden base method or an implemented interface member.</summary>
    private static string? InheritedName(IMethodSymbol method)
    {
        for (var b = method.OverriddenMethod; b is not null; b = b.OverriddenMethod)
            if (GetName(b.OriginalDefinition) is { } n) return n;
        if (ImplementedInterfaceMember(method) is { } im && GetName(im) is { } inm) return inm;
        return null;
    }

    /// <summary>
    /// Resolved implicit interface members, cached per method. <see cref="ImplementedInterfaceMemberCore"/>
    /// is O(interfaces x their members) with a <c>FindImplementationForInterfaceMember</c> call for each,
    /// walked once per level of the override chain — and it is reached from <see cref="JsBaseName"/>,
    /// which <see cref="OverloadGroup"/> calls for every candidate in every base type. Caching it turns
    /// a repeatedly-recomputed graph walk into one lookup. A null result is cached too (as the absent
    /// value of the nullable), since "implements nothing" is the common answer.
    /// </summary>
    private static readonly ConcurrentDictionary<IMethodSymbol, IMethodSymbol?> _implementedIfaceCache = new(SymbolEqualityComparer.Default);

    private static IMethodSymbol? ImplementedInterfaceMember(IMethodSymbol method)
        => _implementedIfaceCache.TryGetValue(method, out var cached)
            ? cached
            : _implementedIfaceCache[method] = ImplementedInterfaceMemberCore(method);

    /// <summary>The interface member this method implements (implicitly), or null.</summary>
    private static IMethodSymbol? ImplementedInterfaceMemberCore(IMethodSymbol method)
    {
        // Walk the override chain: an override implements whatever interface member its overridden
        // base declared as the implementation. Roslyn's FindImplementationForInterfaceMember resolves
        // an interface member to the type that *declares* the implementing member (an abstract base),
        // not a derived override — so an override must inherit its base's interface-member JS name to
        // land in the same runtime slot. Without this, e.g. OrdinalComparer.Compare (overriding the
        // abstract StringComparer.Compare that implements IComparer<T>.Compare) misses the interface
        // member's camelCase [Convention] and emits PascalCase "Compare" while every caller uses
        // camelCase "compare".
        for (var m = method; m is not null; m = m.OverriddenMethod?.OriginalDefinition)
        {
            var type = m.ContainingType;
            if (type is null) continue;
            foreach (var iface in type.AllInterfaces)
            {
                foreach (var ifaceMember in iface.GetMembers())
                {
                    if (ifaceMember is not IMethodSymbol member) continue;
                    if (type.FindImplementationForInterfaceMember(member) is IMethodSymbol impl
                        && SymbolEqualityComparer.Default.Equals(impl.OriginalDefinition, m.OriginalDefinition))
                        return member.OriginalDefinition;
                }
            }
        }
        return null;
    }

    private static string Cache(IMethodSymbol m, string name) { _methodCache[m.OriginalDefinition] = name; return name; }

    /// <summary>
    /// JS name of a constructor using Transpose's OverloadsCollection ordering: the first
    /// constructor is "ctor", the rest "$ctor1", "$ctor2"… This matches the names tps.js
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

    /// <summary>Notation for a library method: member [Convention], else type [Convention], else interface-inherited camelCase, else external, else preserve.</summary>
    private static Notation MethodNotation(IMethodSymbol method)
    {
        // Transpose-compiled methods (source or referenced library) keep their verbatim name; only the
        // external-type conventions below apply to BCL/DOM methods.
        if (IsTransposeCompiledSource(method.ContainingType)) return Notation.None;
        // A [Convention] applied directly to the method (e.g. IComparer<T>.Compare) wins.
        if (MemberConventionNotation(method) is { } mc) return mc;
        var conv = ResolveNotation(method.ContainingType, ConvMethod);
        if (conv is { } c) return c;
        // A templated member with no [Convention] keeps its raw name — Transpose does not camelCase it.
        // The [Template] drives every call site, so the name is used only for an implementer's
        // method slot: e.g. IEnumerable.GetEnumerator (templated, no [Convention]) stays
        // "GetEnumerator", the PascalCase name tps.js's Transpose.getEnumerator looks up. (Collection
        // interfaces whose members SHOULD camelCase — ICollection.Add etc. — carry an explicit
        // type-level [Convention], handled above before this point.)
        if (GetTemplate(method) is not null) return Notation.None;
        // An implementer of an interface member does NOT blanket-camelCase; it inherits the
        // interface member's own JS name (see JsBaseName -> ImplementedInterfaceMember). So a method
        // implementing IEnumerator.MoveNext (whose [Convention] camelCases it) becomes "moveNext",
        // while one implementing IDisposable.Dispose (no [Convention], though IDisposable is
        // [External]) stays "Dispose". External members are preserved too: their bindings are written
        // in the target JS casing already (e.g. dom.addEventListener) or mapped via [Name]/[Template].
        return Notation.None;
    }

    private static bool ImplementsInterfaceMember(IMethodSymbol method)
    {
        var type = method.ContainingType;
        if (type is null) return false;
        foreach (var iface in type.AllInterfaces)
        {
            foreach (var ifaceMember in iface.GetMembers())
            {
                if (ifaceMember is not IMethodSymbol member) continue;
                if (type.FindImplementationForInterfaceMember(member) is IMethodSymbol im
                    && SymbolEqualityComparer.Default.Equals(im.OriginalDefinition, method.OriginalDefinition))
                    return true;
            }
        }
        return false;
    }

    // ---- overload collection (ported from Transpose's OverloadsCollection) --------

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
            foreach (var typeMember in t.GetMembers())
            {
                if (typeMember is not IMethodSymbol candidate) continue;
                var c = candidate.OriginalDefinition;
                if (c.MethodKind != kind || c.IsStatic != isStatic) continue;
                if (c.ExplicitInterfaceImplementations.Length > 0) continue;
                if (JsBaseName(c) != jsBase) continue;                 // group by final JS name
                if (c.IsOverride) continue;                            // overrides fold into their base

                // The parameterless object ToString occupies a slot (so a same-named
                // overload like Version.ToString(int) numbers from $1), even though it is
                // inline/body-less — Transpose keeps only ToString here, not Equals/GetHashCode.
                var isToStringSlot = IsObjectToString(c);
                if (!isToStringSlot && GetTemplate(c) is not null) continue;   // inline methods are excluded
                if (!isToStringSlot && HasNoBody(c)) continue;                 // extern (hand-written JS) methods aren't numbered
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
        // Two constructors compare by signature directly (Transpose returns here, before accessibility).
        if (c1 && c2) return string.Compare(MethodToString(m1), MethodToString(m2), System.StringComparison.CurrentCulture);

        var a1 = AccessibilityWeight(m1.DeclaredAccessibility);
        var a2 = AccessibilityWeight(m2.DeclaredAccessibility);
        if (a1 != a2) return a1.CompareTo(a2);

        return string.Compare(MethodToString(m1), MethodToString(m2), System.StringComparison.CurrentCulture);
    }

    private static bool IsDerivedFrom(ITypeSymbol? derived, ITypeSymbol? base_)
    {
        // Compare on original definitions: an overload group mixes a derived type's own methods
        // with a *generic* base's methods, where the base appears constructed on the derived
        // (e.g. Card : ComponentBase<Card,…>) but as its open definition in the group. Comparing
        // the raw symbols would miss that relationship and mis-order the overloads — which made a
        // derived new overload collide with the base's slot (duplicate JS keys).
        var target = base_?.OriginalDefinition ?? base_;
        for (var t = derived?.BaseType; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, target)) return true;
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
        var a = FindAttr(symbol, ConventionAttr);
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
        var bestSpecific = -1;
        foreach (var a in type.GetAttributes())
        {
            if (!AttrIs(a, ConventionAttr)) continue;
            var member = NamedInt(a, "Member", ConvAll);
            if (member != ConvAll && (member & memberKindFlag) == 0) continue;
            var priority = NamedInt(a, "Priority", 0);
            // A convention that explicitly targets this member kind (Member != All) is more
            // specific than a catch-all one and wins at equal priority, regardless of declaration
            // order — e.g. Console carries both [Convention(PascalCase)] (All) and
            // [Convention(Member = Field | Method, CamelCase)]; its methods must be camelCase.
            var specific = member != ConvAll ? 1 : 0;
            if (best is null || priority > bestPriority
                || (priority == bestPriority && specific >= bestSpecific))
            {
                best = a; bestPriority = priority; bestSpecific = specific;
            }
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

    public const string ConventionAttr = "Transpose.ConventionAttribute";

    /// <summary>
    /// The <c>[GlobalTarget(name)]</c> value for a method, or null. Such a method is a typed
    /// window onto the JS global scope: <c>Transpose.Script.ToDynamic()</c> returns the global
    /// root, so <c>ToDynamic().Transpose.global.console</c> resolves to <c>Transpose.global.console</c>.
    /// </summary>
    public static string? GlobalTargetName(IMethodSymbol? method)
    {
        if (method is null) return null;
        var a = FindAttr(method.OriginalDefinition, "Transpose.GlobalTargetAttribute");
        return a?.ConstructorArguments.Length > 0 ? a.ConstructorArguments[0].Value as string : null;
    }

    /// <summary>True if a method is a dynamic-cast identity (<c>ToDynamic()</c>) — the call is
    /// elided and its receiver used directly (e.g. <c>view.ToDynamic().setInt16(…)</c> →
    /// <c>view.setInt16(…)</c>).</summary>
    public static bool IsDynamicCast(IMethodSymbol? method)
        => method is { Name: "ToDynamic", Parameters.Length: 0 };

    public static string CamelCase(string s)
    {
        if (string.IsNullOrEmpty(s) || char.IsLower(s[0])) return s;
        if (s.Length == 1) return s.ToLowerInvariant();
        // Leave ALLCAPS runs mostly alone but lower the first char (Transpose behavior is
        // first-letter lowercase for typical PascalCase identifiers).
        return char.ToLowerInvariant(s[0]) + s.Substring(1);
    }
}
