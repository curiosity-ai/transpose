using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

public sealed partial class Emitter
{
    private void EmitType(INamedTypeSymbol type)
    {
        switch (type.TypeKind)
        {
            case TypeKind.Enum:
                EmitEnum(type);
                break;
            case TypeKind.Interface:
                EmitInterface(type);
                break;
            case TypeKind.Class:
            case TypeKind.Struct:
                EmitClassLike(type);
                break;
            case TypeKind.Delegate:
                break; // delegates map onto plain functions
            case TypeKind.Extension:
                Unsupported(type.DeclaringSyntaxReferences[0].GetSyntax(),
                    "Extension members (C# 14 extension blocks) are not supported");
                break;
            default:
                Unsupported(type.DeclaringSyntaxReferences[0].GetSyntax(), $"type kind {type.TypeKind}");
                break;
        }
    }

    /// <summary>
    /// Emits <c>Transpose.ready(Type.Method, Type);</c> for every <c>[Transpose.Ready]</c> static
    /// method. Transpose.ready runs the callback on DOMContentLoaded, or immediately when the
    /// document is already loaded — the latter is what lets a lazily fetched package (e.g. the admin
    /// bundle) run its initializer the moment it is loaded. Static members are referenced fully
    /// qualified, so the <c>this</c>-scope argument is just belt-and-braces for parity with the
    /// [Ready] adapter's FormatScope.
    /// </summary>
    private void EmitReadyRegistrations(IReadOnlyList<INamedTypeSymbol> types)
    {
        foreach (var type in types)
        {
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            {
                if (!method.IsStatic) continue;
                if (!method.GetAttributes().Any(a => TransposeNaming.AttrIs(a, TransposeNaming.ReadyAttr))) continue;
                var typeRef = TypeRef(type);
                _w.WriteLine($"Transpose.ready({typeRef}.{TransposeNaming.MemberJsName(method)}, {typeRef});");
            }
        }
    }

    /// <summary>Full JS name a type is registered / referenced under.</summary>
    /// <summary>
    /// The JavaScript expression that names a type at runtime. A named type resolves through
    /// <see cref="UnshadowedTypeRef"/>, so a local binding of the same name cannot intercept it; a type
    /// PARAMETER never does, because it IS a local binding (the generic define's own parameter).
    /// </summary>
    private string TypeRef(ITypeSymbol type)
    {
        // Module mode records which source types this one's emitted body reaches into, so the
        // chunker can group mutually-referencing types (see Emitter.Modules.cs). Recording here
        // rather than in a separate analysis pass keeps the two in step by construction: an edge
        // exists exactly when a reference was emitted.
        if ((_recordedRefs is not null || _recordedExternalRefs is not null) && _softRefDepth == 0) RecordRef(type);
        return type is ITypeParameterSymbol ? TypeRefCore(type) : UnshadowedTypeRef(TypeRefCore(type));
    }

    /// <summary>
    /// The runtime type token an ARRAY is tagged with (<c>System.Array.init([…], token)</c>).
    ///
    /// Normally the element type's own <see cref="TypeRef"/>, but an [ObjectLiteral] type from an
    /// external binding library has no runtime type object to name: it is a WebIDL *dictionary*
    /// (dom.AnimationKeyFrame, dom.EventInit, …), so instances are plain JS objects, no
    /// Transpose.define is ever emitted for it, and the browser has no global of that name either —
    /// naming it emitted `System.Array.init([…], AnimationKeyFrame)`, which throws ReferenceError
    /// before the array is even built. Such an element IS a plain object, so it is tagged as one,
    /// exactly as `new object[]{…}` is. A NON-external [ObjectLiteral] type is unaffected: it still
    /// gets a define (carrying $literal), so its name resolves.
    /// </summary>
    private string ArrayElementTypeRef(ITypeSymbol elementType)
        => TransposeNaming.IsExternalType(elementType)
           && elementType.GetAttributes().Any(a => TransposeNaming.AttrIs(a, "Transpose.ObjectLiteralAttribute"))
            ? TypeRef(_compilation.GetSpecialType(SpecialType.System_Object))
            : TypeRef(elementType);

    /// <summary>Adds every source named type inside <paramref name="type"/> (the type itself and,
    /// recursively, its generic arguments and array element) to the current type's dependency set.
    /// A generic argument counts: <c>Foo$1(X)</c> builds a generic instance whose base class can be
    /// <c>X</c> itself, so X has to be defined before the application runs.</summary>
    private void RecordRef(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                RecordRef(array.ElementType);
                return;
            case INamedTypeSymbol named:
                if (TransposeNaming.IsExternalType(named)) { /* native JS - nothing to load */ }
                else if (named.Locations.Any(l => l.IsInSource))
                    _recordedRefs!.Add((INamedTypeSymbol)named.OriginalDefinition);
                else if (_recordedExternalRefs is not null && TransposeNaming.IsTransposeCompiledSource(named))
                    // A type from a *referenced* Transpose-compiled assembly. If that assembly was
                    // itself built as modules, the chunk holding this type has to be imported, or the
                    // reference would land on a stub. Recorded by emitted define name, which is the
                    // key the referenced assembly's chunk map uses.
                    _recordedExternalRefs.Add(DefineName(named));
                foreach (var arg in named.TypeArguments) RecordRef(arg);
                return;
        }
    }

    /// <summary>
    /// Records, as hard dependencies, every part of <paramref name="type"/> that <see cref="TypeRefCore"/>
    /// will emit as a CALL on a generic definition (<c>Foo$1(Bar)</c>). Used where the reference is
    /// otherwise soft — <c>typeof(X)</c> — because a soft reference is only satisfiable by a stub
    /// object, and a stub cannot be called: building a constructed generic needs the definition's
    /// real code. An unbound <c>typeof(Foo&lt;&gt;)</c> emits the definition object rather than a
    /// call and so stays soft, as does any non-generic type.
    /// </summary>
    private void RecordConstructedTypeRefs(ITypeSymbol type)
    {
        if (_recordedRefs is null && _recordedExternalRefs is null) return;   // not module mode
        switch (type)
        {
            case IArrayTypeSymbol array:
                // `typeof(Foo<Bar>[])` emits System.Array.type(Foo$1(Bar)) — the call is still there.
                RecordConstructedTypeRefs(array.ElementType);
                return;
            case INamedTypeSymbol named:
                if (!named.IsUnboundGenericType && EffectiveTypeArguments(named).Count > 0)
                {
                    // RecordRef records the definition and recurses through the arguments, which is
                    // the same set the emitted call touches.
                    RecordRef(named);
                    return;
                }
                foreach (var arg in named.TypeArguments) RecordConstructedTypeRefs(arg);
                return;
        }
    }

    /// <summary>The bare name a type's <c>Transpose.define</c> registers it under — no type
    /// arguments, arity suffixed. This is the key both the module manifest and the chunk map use.</summary>
    private string DefineName(INamedTypeSymbol named)
    {
        var t = (INamedTypeSymbol)named.OriginalDefinition;
        return t.Arity > 0 ? _names.TypeFullName(t) + "$" + t.Arity : _names.TypeFullName(t);
    }

    private string TypeRefCore(ITypeSymbol type)
    {
        // `dynamic` has no runtime type of its own — it is System.Object at runtime.
        if (type.TypeKind == TypeKind.Dynamic) return "System.Object";

        // An anonymous type has no runtime type of its own — instances are plain JS objects, so any
        // runtime type reference (e.g. a generic method's threaded type argument, as in
        // req.WithBody(new { ... })) resolves to System.Object. Without this the type ref emits empty
        // and produces a syntactically invalid call like `WithBody$1(, {...})`.
        if (type is INamedTypeSymbol { IsAnonymousType: true }) return "System.Object";

        if (type is INamedTypeSymbol named)
        {
            // An UNBOUND generic — `typeof(Foo<>)` — has no concrete type arguments; its
            // TypeArguments are the type PARAMETERS (T, TKey, …), which have no runtime value. Its
            // JS value is the generic type DEFINITION itself (what Type.GetGenericTypeDefinition()
            // returns), so emit the arity-suffixed name WITHOUT applying arguments — `Foo$1`, never
            // `Foo$1(T)` (which references an undefined `T`).
            var unbound = named.IsUnboundGenericType;

            // External (BCL / DOM) types are named by their runtime binding: [Name], a
            // [Scope]/[GlobalMethods] global (e.g. Transpose.Core.dom's HTMLElement), or the dotted
            // metadata name. [Name] applies ONLY here — an Transpose-compiled type ignores it.
            if (!TransposeNaming.IsTransposeCompiledSource(named))
            {
                var name = TransposeNaming.GetName(named);
                if (name is not null) return name;

                if (ScopedExternalName(named) is { } scoped) return scoped;

                // A nested BCL runtime type (e.g. System.Collections.Generic.List<T>.Enumerator,
                // reached here when self-building the runtime) is defined under its full nested JS
                // name with the enclosing types' arity carried in the name and the effective type
                // arguments appended once at the leaf — List$1.Enumerator(T), matching its define.
                if (named.ContainingType is not null)
                {
                    var nestedArgs = EffectiveTypeArguments(named);
                    var nestedName = named.Arity > 0 ? _names.TypeFullName(named) + "$" + named.Arity : _names.TypeFullName(named);
                    return nestedArgs.Count > 0 && !unbound
                        ? $"{nestedName}({string.Join(", ", nestedArgs.Select(TypeRef))})"
                        : nestedName;
                }

                var ns = named.ContainingNamespace?.ToDisplayString();
                // A type-level [Transpose.Namespace] overrides the emitted namespace: false/"" drops
                // it (so Transpose.Core's String/Number/… bind to the JS globals), a string replaces it.
                if (TransposeNaming.NamespaceOverride(named) is { } nsOverride)
                    ns = nsOverride.Length == 0 ? null : nsOverride;
                if (named.IsGenericType && named.TypeArguments.Length > 0)
                {
                    var baseName = (string.IsNullOrEmpty(ns) ? "" : ns + ".") + StripArity(named.Name) + "$" + named.Arity;
                    if (unbound) return baseName;
                    var args = string.Join(", ", named.TypeArguments.Select(TypeRef));
                    return $"{baseName}({args})";
                }
                return string.IsNullOrEmpty(ns) ? named.Name : ns + "." + named.Name;
            }

            // A type this compiler emits — either from source, or from a referenced Transpose-compiled
            // assembly (a package built with --emit-package). Both are defined via Transpose.define under
            // their full nested JS name, so a reference must use the same name (nested-aware).
            // The name carries only the type's OWN arity suffix; the type arguments passed are the
            // EFFECTIVE ones (enclosing + own), so a type nested in a generic (e.g.
            // IconToggle<int>.Item) resolves to tss.IconToggle.Item(System.Int32).
            var effArgs = EffectiveTypeArguments(named);
            var defName = named.Arity > 0 ? _names.TypeFullName(named) + "$" + named.Arity : _names.TypeFullName(named);
            if (effArgs.Count > 0 && !unbound)
                return $"{defName}({string.Join(", ", effArgs.Select(TypeRef))})";
            return defName;
        }
        // An array type reifies to its concrete runtime type — System.Array.type(element, rank) —
        // NOT the bare System.Array base (which has no $elementType and fails Transpose.isArray, so a
        // reified `T[]` type argument, e.g. DeserializeObject<AdminSettingsItem[]>, would deserialize
        // into an object with numeric keys instead of an array). rank defaults to 1, so it is only
        // emitted for multidimensional arrays, matching System.Array.type's signature.
        if (type is IArrayTypeSymbol array)
            return array.Rank > 1
                ? $"System.Array.type({TypeRef(array.ElementType)}, {array.Rank})"
                : $"System.Array.type({TypeRef(array.ElementType)})";
        return type.Name;
    }

    private static string StripArity(string name)
    {
        var i = name.IndexOf('`');
        return i >= 0 ? name.Substring(0, i) : name;
    }

    /// <summary>
    /// The type parameters a type's Transpose.define is a function of: its enclosing types' parameters
    /// (outermost first) followed by its own. A type nested in a generic type can reference the
    /// enclosing type parameters in C#, so — like the legacy compiler — its define is emitted as
    /// <c>function (TOuter…) { return {…}; }</c> even when the nested type has no parameters of its
    /// own (e.g. <c>IconToggle&lt;T&gt;.Item</c> → <c>Transpose.define("tss.IconToggle.Item", function (T){…})</c>).
    /// </summary>
    private static List<ITypeParameterSymbol> EffectiveTypeParameters(INamedTypeSymbol type)
    {
        var result = new List<ITypeParameterSymbol>();
        for (INamedTypeSymbol? t = type; t is not null; t = t.ContainingType)
            result.InsertRange(0, t.TypeParameters);
        return result;
    }

    /// <summary>
    /// JS parameter names for a generic type's define function, made unique. A nested generic type
    /// may reuse an enclosing type parameter's name (legal C# — CS0693 — where the inner shadows the
    /// outer, e.g. <c>ReturnType&lt;T&gt;.ReturnTypeFnAlias&lt;T&gt;</c>), which would emit an illegal
    /// <c>function (T, T)</c>. C# resolves an unqualified <c>T</c> in the body to the innermost, so we
    /// keep the LAST occurrence's original name and suffix earlier duplicates.
    /// </summary>
    private static List<string> UniqueTypeParamNames(List<ITypeParameterSymbol> typeParams)
    {
        var names = new List<string>(typeParams.Count);
        for (var i = 0; i < typeParams.Count; i++)
        {
            var name = typeParams[i].Name;
            var shadowedLater = false;
            for (var j = i + 1; j < typeParams.Count; j++)
                if (typeParams[j].Name == name) { shadowedLater = true; break; }
            names.Add(shadowedLater ? name + "$" + i : name);
        }
        return names;
    }

    /// <summary>The type arguments to pass when referencing a type: its enclosing types' arguments
    /// (outermost first) then its own — the mirror of <see cref="EffectiveTypeParameters"/>.</summary>
    private static List<ITypeSymbol> EffectiveTypeArguments(INamedTypeSymbol type)
    {
        var result = new List<ITypeSymbol>();
        for (INamedTypeSymbol? t = type; t is not null; t = t.ContainingType)
            result.InsertRange(0, t.TypeArguments);
        return result;
    }

    /// <summary>
    /// The JS name of an external type nested under a <c>[Scope]</c>/<c>[GlobalMethods]</c>
    /// binding: the scope prefix (empty for a global scope) plus the type names between the
    /// scope and this type — so <c>Transpose.Core.dom.HTMLElement</c> becomes <c>HTMLElement</c>.
    /// Null when no enclosing scope applies.
    /// </summary>
    /// <summary>
    /// A static member reference: for a <c>[Scope]</c>/<c>[GlobalMethods]</c> binding it is the
    /// bare (or scope-prefixed) member — <c>dom.window</c> → <c>window</c>, <c>dom.alert(…)</c>
    /// → <c>alert(…)</c> — otherwise the qualified <c>Type.member</c>.
    /// </summary>
    private string StaticMemberAccess(ISymbol member)
    {
        var name = TransposeNaming.MemberJsName(member);
        var prefix = TransposeNaming.ScopePrefix(member.ContainingType);
        if (prefix is null) return $"{TypeRef(member.ContainingType)}.{name}";
        return prefix.Length == 0 ? name : $"{prefix}.{name}";
    }

    private string? ScopedExternalName(INamedTypeSymbol named)
    {
        var names = new List<string>();
        for (INamedTypeSymbol? t = named; t is not null; t = t.ContainingType)
        {
            if (TransposeNaming.ScopePrefix(t) is { } prefix)
            {
                if (names.Count == 0) return null; // referencing the scope type itself — not a member
                var path = string.Join(".", names);
                return string.IsNullOrEmpty(prefix) ? path : prefix + "." + path;
            }
            names.Insert(0, TransposeNaming.GetName(t) ?? StripArity(t.Name));
        }
        return null;
    }

    /// <summary>The JS literal for <c>default(enum)</c>. A string-backed enum
    /// (<c>[Enum(Emit.StringName*)]</c>) defaults to the string of its zero-valued member;
    /// every other mode is a numeric enum at runtime and defaults to 0.</summary>
    private string EnumDefaultLiteral(INamedTypeSymbol enumType)
    {
        var mode = TransposeNaming.EnumEmitMode(enumType);
        if (mode is 3 or 4 or 5 or 6)
        {
            var zero = enumType.GetMembers().OfType<IFieldSymbol>()
                .FirstOrDefault(f => f.HasConstantValue && EnumOrdinalText(f.ConstantValue) == "0");
            return zero is not null ? JsString(TransposeNaming.EnumStringName(zero, mode)) : "null";
        }
        return "0";
    }

    /// <summary>
    /// An enum member's underlying constant as an invariant decimal string, used both to emit the
    /// ordinal and to match members by value. Deliberately not <c>Convert.ToInt64</c>: a
    /// <c>ulong</c>-backed enum can hold values above <c>long.MaxValue</c>, and
    /// <c>ulong.MaxValue</c> made the translator itself throw "Value was either too large or too
    /// small for an Int64".
    /// </summary>
    internal static string EnumOrdinalText(object? constantValue) => constantValue switch
    {
        null => "0",
        ulong u => u.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => Convert.ToInt64(constantValue).ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Below this many members, an enum keeps the member-per-name object literal. The compact form
    /// (see <see cref="TryDenseEnumNames"/>) trades a parsed property per member for one string and
    /// a <c>split</c> at define time, which is only worth doing at scale — and keeping every small
    /// enum on the path it has always used means the emitted output of almost every type is
    /// unchanged, which is what makes this measurable rather than merely different.
    /// </summary>
    private const int DenseEnumMinimumMembers = 64;

    /// <summary>
    /// The member names of a <em>dense</em> enum as one delimited string, or null when the enum is
    /// not one.
    ///
    /// Dense means its members are exactly the ordinals <c>0…n-1</c> in declaration order — no gaps,
    /// no aliases, no <c>[Flags]</c> bit pattern. Then the names carry all the information the
    /// runtime needs, because each one's value <em>is</em> its index, and the whole
    /// <c>{ "name": 0, "name": 1, … }</c> block collapses to <c>"name,name,…"</c>.
    ///
    /// This is worth a special case because generated enums are enormous and are exactly the ones an
    /// application cannot avoid loading. Tesserae's <c>UIcons</c> has 5,372 members: 212 KB as an
    /// object literal against 100 KB as a string, 40 KB against 25 KB compressed. The parse side
    /// matters as much as the bytes — the object form makes the JS parser build 5,372 properties and
    /// the runtime sort 5,372 pairs of them before anything renders, where the string form is one
    /// literal and one split.
    ///
    /// Returns null if any member's emitted name would not survive the round trip — a name holding
    /// the delimiter, a quote, a backslash or a line break — so the encoding can never lose a member
    /// silently. Those cannot occur in a C# identifier but <c>[Name("…")]</c> can say anything.
    /// </summary>
    private static string? TryDenseEnumNames(IReadOnlyList<IFieldSymbol> fields)
    {
        if (fields.Count < DenseEnumMinimumMembers) return null;

        var names = new List<string>(fields.Count);
        for (var i = 0; i < fields.Count; i++)
        {
            if (EnumOrdinalText(fields[i].ConstantValue) != i.ToString(System.Globalization.CultureInfo.InvariantCulture))
                return null;

            var name = TransposeNaming.MemberJsName(fields[i]);
            if (name.Length == 0 || name.AsSpan().IndexOfAny(",\"\\\n\r") >= 0) return null;
            names.Add(name);
        }
        return string.Join(",", names);
    }

    private void EmitEnum(INamedTypeSymbol type)
    {
        _w.Write($"Transpose.define(\"{_names.TypeFullName(type)}\", ");
        var isFlags = type.GetAttributes().Any(a => TransposeNaming.AttrIs(a, "System.FlagsAttribute"));
        var mode = TransposeNaming.EnumEmitMode(type);
        // Emit.StringName* modes back the enum with strings (its [Name] on each member); every
        // other mode keeps the numeric ordinals. A string-backed enum also declares
        // $utype: System.String so the runtime treats its members as strings (this is what makes
        // `x === "top"`-style comparisons against enum members work).
        var stringMode = mode is 3 or 4 or 5 or 6;
        var enumNested = type.ContainingType is not null ? "nested " : "";
        _w.Block(() =>
        {
            _w.WriteLine($"$kind: \"{enumNested}enum\",");
            if (isFlags) _w.WriteLine("$flags: true,");
            _w.Write("statics: ");
            _w.Block(() =>
            {
                var fields = type.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue).ToList();
                // A large dense enum ships its names as one string; the runtime expands it back into
                // members (Class.js). A string-backed enum is excluded — its members' values are the
                // names themselves, not their positions, so nothing is implied by order.
                var dense = stringMode ? null : TryDenseEnumNames(fields);
                if (dense is not null)
                {
                    _w.WriteLine($"$denseNames: {JsString(dense)}");
                    return;
                }
                _w.Write("fields: ");
                _w.Block(() =>
                {
                    for (var i = 0; i < fields.Count; i++)
                    {
                        var value = stringMode
                            ? JsString(TransposeNaming.EnumStringName(fields[i], mode))
                            : EnumOrdinalText(fields[i].ConstantValue);
                        _w.Write($"{NameMangler.JsPropertyKey(TransposeNaming.MemberJsName(fields[i]))}: {value}");
                        _w.WriteLine(i < fields.Count - 1 ? "," : "");
                    }
                });
                _w.WriteLine();
            });
            _w.WriteLine(stringMode ? "," : "");
            if (stringMode) _w.WriteLine("$utype: System.String");
        });
        _w.WriteLine(");");
    }

    private void EmitInterface(INamedTypeSymbol type)
    {
        // A generic interface is a function of its type parameters (like generic classes),
        // so references such as IContainer$1(T) resolve at runtime. Effective parameters include
        // an enclosing generic type's, matching the class treatment.
        var typeParams = EffectiveTypeParameters(type);
        var isGeneric = typeParams.Count > 0;
        var fullName = type.Arity > 0 ? _names.TypeFullName(type) + "$" + type.Arity : _names.TypeFullName(type);

        // $variance records each OWN type parameter's variance so the runtime can model
        // covariant/contravariant interface assignability: 1 = covariant (out), 2 = contravariant
        // (in), 0 = invariant. Only emitted when at least one parameter is variant (as Transpose does).
        var variances = type.TypeParameters.Select(p => p.Variance switch
        {
            VarianceKind.Out => 1,
            VarianceKind.In => 2,
            _ => 0,
        }).ToList();
        var hasVariance = variances.Any(v => v != 0);

        // A variant generic interface is registered with Transpose.definei (the runtime needs the
        // variance model to resolve assignability); all other interfaces use Transpose.define.
        _w.Write($"Transpose.{(hasVariance ? "definei" : "define")}(\"{fullName}\", ");
        if (isGeneric) _w.Write($"function ({string.Join(", ", UniqueTypeParamNames(typeParams))}) {{ return ");

        _w.Block(() =>
        {
            _w.Write($"$kind: \"{(type.ContainingType is not null ? "nested " : "")}interface\"");
            var bases = type.Interfaces.Where(i => TransposeNaming.IsInheritableInterface(i)).ToList();
            if (bases.Count > 0)
            {
                _w.WriteLine(",");
                _w.Write($"inherits: function () {{ return [{string.Join(", ", bases.Select(TypeRef))}]; }}");
            }
            if (hasVariance)
            {
                _w.WriteLine(",");
                _w.Write($"$variance: [{string.Join(", ", variances)}]");
            }
            _w.WriteLine();
        });
        if (isGeneric) _w.Write("; }");
        _w.WriteLine(");");
    }

    private void EmitClassLike(INamedTypeSymbol type)
    {
        var prevEmitType = _currentEmitType;
        var prevShadowing = _shadowingNames;
        _currentEmitType = type;
        _shadowingNames = ShadowingIdentifiers(type);
        try { EmitClassLikeCore(type); }
        finally { _currentEmitType = prevEmitType; _shadowingNames = prevShadowing; }
    }

    /// <summary>
    /// Every JS identifier this type's code introduces as a local binding — parameters (of methods,
    /// constructors, lambdas, local functions and a record header), locals, <c>out var</c>/pattern
    /// designations, <c>foreach</c> and <c>catch</c> variables, and query range variables.
    ///
    /// A type is referenced by its bare emitted name, so any of these shadows a same-named type for
    /// the whole of the function it is declared in: <c>record RD(int A, int B) : RB(A)</c> whose base
    /// is named <c>B</c> emitted <c>B.ctor.call(this, A)</c> inside <c>function (A, B)</c> and read
    /// the int. <see cref="TypeRef"/> consults this set and routes such a reference through
    /// <c>Transpose.global</c>, which nothing can shadow.
    ///
    /// Deliberately collected per TYPE rather than per function: a name that only shadows inside a
    /// sibling function is then qualified as well, which is merely redundant, whereas tracking exact
    /// scopes would have to be threaded through every body-emitting path to stay correct.
    /// </summary>
    private static HashSet<string> ShadowingIdentifiers(INamedTypeSymbol type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            var declaration = reference.GetSyntax();
            foreach (var node in declaration.DescendantNodesAndSelf())
            {
                switch (node)
                {
                    case ParameterSyntax p:
                        Add(p.Identifier.Text);
                        break;
                    // A field's declarator is `this.X`, never a bare identifier — only locals shadow.
                    case VariableDeclaratorSyntax v when v.Parent?.Parent is LocalDeclarationStatementSyntax
                                                             or UsingStatementSyntax or ForStatementSyntax
                                                             or FixedStatementSyntax:
                        Add(v.Identifier.Text);
                        break;
                    case SingleVariableDesignationSyntax d:
                        Add(d.Identifier.Text);
                        break;
                    case ForEachStatementSyntax f:
                        Add(f.Identifier.Text);
                        break;
                    case CatchDeclarationSyntax c:
                        Add(c.Identifier.Text);
                        break;
                    case FromClauseSyntax from:
                        Add(from.Identifier.Text);
                        break;
                    case LetClauseSyntax let:
                        Add(let.Identifier.Text);
                        break;
                    case JoinClauseSyntax join:
                        Add(join.Identifier.Text);
                        break;
                    case JoinIntoClauseSyntax into:
                        Add(into.Identifier.Text);
                        break;
                    case QueryContinuationSyntax cont:
                        Add(cont.Identifier.Text);
                        break;
                    case LocalFunctionStatementSyntax fn:
                        Add(fn.Identifier.Text);
                        break;
                }
            }
        }
        return names;

        void Add(string name)
        {
            if (name.Length > 0) names.Add(NameMangler.JsIdentifier(name));
        }
    }

    /// <summary>Wraps a type reference so a local binding of the same name cannot shadow it. The
    /// runtime registers every global-scope type on <c>Transpose.global</c> (the real global object),
    /// so this reaches the same value by a path no local can intercept.</summary>
    private string UnshadowedTypeRef(string reference)
    {
        if (_shadowingNames is null || _shadowingNames.Count == 0) return reference;
        var dot = reference.IndexOfAny(new[] { '.', '(' });
        var head = dot < 0 ? reference : reference.Substring(0, dot);
        return _shadowingNames.Contains(head) ? "Transpose.global." + reference : reference;
    }

    private void EmitClassLikeCore(INamedTypeSymbol type)
    {
        var entryPoint = _compilation.GetEntryPoint(System.Threading.CancellationToken.None);

        // A generic type is defined as a function of its type parameters, returning the
        // config object (Transpose.define("Name$N", function (T) { return { … }; })); the type
        // parameters are then in scope at runtime for new T()/default(T)/typeof(T). A type nested
        // in a generic type is a function of the ENCLOSING parameters too (its own arity may be 0),
        // so the define name carries only its own arity but the function takes every effective one.
        var typeParams = EffectiveTypeParameters(type);
        var isGeneric = typeParams.Count > 0;
        var fullName = type.Arity > 0 ? _names.TypeFullName(type) + "$" + type.Arity : _names.TypeFullName(type);

        _w.Write($"Transpose.define(\"{fullName}\", ");
        if (isGeneric) _w.Write($"function ({string.Join(", ", UniqueTypeParamNames(typeParams))}) {{ return ");
        _w.Block(() =>
        {
            var sections = new List<Action>();

            // $kind for structs, and for any nested type (a nested class needs "nested class";
            // a top-level class needs no $kind, since class is the runtime default).
            var nested = type.ContainingType is not null;
            if (type.TypeKind == TypeKind.Struct)
                sections.Add(() => _w.Write($"$kind: \"{(nested ? "nested " : "")}struct\""));
            else if (nested)
                sections.Add(() => _w.Write("$kind: \"nested class\""));

            // $literal marks an [ObjectLiteral] type: instances are plain JS objects (construction
            // emits {} + initializer), and the runtime treats the type as a literal for is/as/typeof
            // rather than a real class. Matches the legacy compiler's $literal:true flag.
            if (type.GetAttributes().Any(a => TransposeNaming.AttrIs(a, "Transpose.ObjectLiteralAttribute")))
            {
                sections.Add(() => _w.Write("$literal: true"));
            }

            // inherits: base class + implemented interfaces the runtime tracks (source or a
            // referenced Transpose-compiled library — so `x is IFoo`/`as IFoo` against a library
            // interface resolves; external BCL interfaces are omitted, matching Transpose).
            var inherits = new List<string>();
            if (type.BaseType is { } bt && bt.SpecialType != SpecialType.System_Object
                && bt.TypeKind != TypeKind.Error && !IsValueTypeBase(bt))
            {
                inherits.Add(TypeRef(bt));
            }
            inherits.AddRange(type.Interfaces.Where(i => TransposeNaming.IsInheritableInterface(i)).Select(TypeRef));
            if (inherits.Count > 0)
            {
                // Lazy inherits (a function, as the legacy compiler emits): the config object
                // is built before Transpose.define runs, so evaluating an eager array would resolve a
                // self/forward reference (e.g. class C : IFoo<C>) before the type is registered.
                sections.Add(() => _w.Write($"inherits: function () {{ return [{string.Join(", ", inherits)}]; }}"));
            }

            // alias: maps each implicitly-implemented interface member's plain slot to the
            // mangled interface slot, so access through the interface type resolves.
            var aliases = TransposeNaming.InterfaceAliasPairs(type);
            if (aliases.Count > 0)
            {
                sections.Add(() => _w.Write(
                    $"alias: [{string.Join(", ", aliases.SelectMany(a => new[] { JsString(a.plain), JsString(a.mangled) }))}]"));
            }

            // main: entry point.
            if (entryPoint is not null && SymbolEqualityComparer.Default.Equals(entryPoint.ContainingType, type))
            {
                sections.Add(() => EmitEntryPoint(entryPoint));
            }

            // statics { fields, ctors.init/ctor, methods, properties }
            var staticsBody = Capture(() => EmitStatics(type, fullName));
            if (staticsBody.Trim().Length > 0)
            {
                sections.Add(() => { _w.Write("statics: "); _w.Write(staticsBody); });
            }

            // instance fields
            var fieldsBody = Capture(() => EmitInstanceFields(type));
            if (fieldsBody.Trim().Length > 0)
            {
                sections.Add(() => { _w.Write("fields: "); _w.Write(fieldsBody); });
            }

            // instance ctors
            var ctorsBody = Capture(() => { if (!TryEmitRecordCtors(type)) EmitInstanceCtors(type); });
            if (ctorsBody.Trim().Length > 0)
            {
                sections.Add(() => { _w.Write("ctors: "); _w.Write(ctorsBody); });
            }

            // instance properties (with logic)
            var propsBody = Capture(() => EmitInstanceProperties(type));
            if (propsBody.Trim().Length > 0)
            {
                sections.Add(() => { _w.Write("props: "); _w.Write(propsBody); });
            }

            // instance methods
            var methodsBody = Capture(() => EmitInstanceMethods(type, entryPoint));
            if (methodsBody.Trim().Length > 0)
            {
                sections.Add(() => { _w.Write("methods: "); _w.Write(methodsBody); });
            }

            for (var i = 0; i < sections.Count; i++)
            {
                sections[i]();
                _w.WriteLine(i < sections.Count - 1 ? "," : "");
            }
        });
        if (isGeneric) _w.Write("; }");
        _w.WriteLine(");");
    }

    private static bool IsValueTypeBase(INamedTypeSymbol baseType)
        => baseType.SpecialType is SpecialType.System_ValueType or SpecialType.System_Enum;

    /// <summary>Auto-properties are stored as plain fields; only these + real fields appear here.</summary>
    private void EmitInstanceFields(INamedTypeSymbol type)
    {
        var entries = InstanceFieldSlots(type).ToList();
        if (entries.Count == 0) return;
        _w.Block(() =>
        {
            for (var i = 0; i < entries.Count; i++)
            {
                _w.Write($"{NameMangler.JsPropertyKey(entries[i].name)}: {entries[i].def}");
                _w.WriteLine(i < entries.Count - 1 ? "," : "");
            }
        });
    }

    private IEnumerable<(string name, string def, ISymbol symbol)> InstanceFieldSlots(INamedTypeSymbol type)
    {
        foreach (var m in type.GetMembers())
        {
            if (m.IsStatic) continue;
            if (m is IFieldSymbol f && !f.IsConst && f.AssociatedSymbol is null && f.CanBeReferencedByName)
                yield return (TransposeNaming.MemberJsName(f), FieldDefaultLiteral(f.Type), f);
            // Checked before the plain auto-property slot below: a virtual/overriding auto-property is
            // field-backed (it needs real accessors so it dispatches, over storage of its own), so it
            // must take the backing slot rather than the property's own name.
            else if (m is IPropertySymbol fbp && IsFieldBackedProperty(fbp))
                yield return (PropertyBackingName(fbp), FieldDefaultLiteral(fbp.Type), fbp);
            else if (m is IPropertySymbol p && !p.IsAbstract && !p.IsIndexer
                     && (IsAutoProperty(p) || IsRecordPositionalProperty(p)))
                yield return (TransposeNaming.MemberJsName(p), FieldDefaultLiteral(p.Type), p);
            else if (m is IEventSymbol ev && IsFieldLikeEvent(ev))
                yield return (TransposeNaming.MemberJsName(ev), "null", ev);
        }
    }

    /// <summary>A field-like event (no explicit add/remove) — backed by a null delegate field.</summary>
    internal static bool IsFieldLikeEvent(IEventSymbol ev)
        => ev.AddMethod is null or { IsImplicitlyDeclared: true };

    // ---- shared helpers ----------------------------------------------------

    /// <summary>
    /// An <c>extern</c> property — its accessors are bodyless, but the implementation lives outside
    /// the compilation (a <c>[Template]</c>/<c>[Script]</c> applied at the call site), so C# gives it
    /// no backing field and there is no accessor body to emit.
    /// </summary>
    internal static bool IsExternProperty(IPropertySymbol prop)
        => prop.IsExtern || prop.GetMethod is { IsExtern: true } || prop.SetMethod is { IsExtern: true };

    internal static bool IsAutoProperty(IPropertySymbol prop)
    {
        // Not an auto-property: treating one as such invented a phantom backing field that then leaked
        // into the struct's default value, $clone, equals and getHashCode (e.g. LanguageDTO, whose
        // runtime value IS its JS string).
        if (IsExternProperty(prop)) return false;

        foreach (var reference in prop.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is PropertyDeclarationSyntax decl)
            {
                if (decl.ExpressionBody is not null) return false;
                if (decl.AccessorList is null) return false;
                return decl.AccessorList.Accessors.All(a => a.Body is null && a.ExpressionBody is null);
            }
        }
        return false;
    }

    private ExpressionSyntax? FieldInitializerSyntax(IFieldSymbol field)
    {
        foreach (var reference in field.DeclaringSyntaxReferences)
            if (reference.GetSyntax() is VariableDeclaratorSyntax { Initializer: { } init })
                return init.Value;
        return null;
    }

    private ExpressionSyntax? AutoPropertyInitializerSyntax(IPropertySymbol prop)
    {
        foreach (var reference in prop.DeclaringSyntaxReferences)
            if (reference.GetSyntax() is PropertyDeclarationSyntax { Initializer: { } init })
                return init.Value;
        return null;
    }

    /// <summary>Default value for a field/auto-property SLOT in a define's <c>fields:</c> block:
    /// like <see cref="DefaultValueLiteral"/>, but a struct-typed slot is <c>null</c> (not
    /// <c>Struct.getDefaultValue()</c>) — the zeroed struct is assigned in the constructor's
    /// $initialize, so the slot literal stays order-independent (matching the reference runtime).</summary>
    private string FieldDefaultLiteral(ITypeSymbol type)
    {
        // A struct-typed slot other than a primitive numeric/bool (DateTime, Guid, Nullable<T>, a
        // user struct) defaults to null in the slot; the zeroed struct is assigned in $initialize.
        // long/ulong/decimal go the same way: they are JS *objects* at runtime, not numbers.
        if (type.TypeKind == TypeKind.Struct && (!IsPrimitiveNumericOrBool(type) || IsRuntimeObjectNumeric(type)))
            return "null";
        return DefaultValueLiteral(type);
    }

    private static bool IsPrimitiveNumericOrBool(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_Boolean or SpecialType.System_Char or SpecialType.System_SByte
            or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
            or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64
            or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double
            or SpecialType.System_Decimal => true,
        _ => false,
    };

    private string DefaultValueLiteral(ITypeSymbol type)
    {
        // default(T) for an unconstrained/struct type parameter must defer to the runtime, which
        // picks 0 / false / null based on the *actual* T at construction — exactly what Transpose emits
        // (Transpose.getDefaultValue(T)). Emitting a bare null here would wrongly seed value-type T
        // (int/bool/enum/struct) with null instead of its zeroed default. Only safe when T is a
        // type parameter of the type currently being emitted (hence bound as a JS function
        // parameter of the define); a T inherited from an enclosing generic type is not in scope
        // here, so fall through to null (that nested type is emitted non-generically).
        if (type is ITypeParameterSymbol tp)
        {
            // A method type parameter is threaded as a leading JS parameter of the (generic) method
            // (`Def: function (T) {…}`, called `Foo.Def(System.Int32)`), so getDefaultValue(T) is in
            // scope wherever default(T) appears in the body — emit it so `default(int)` is 0, not null.
            if (tp.TypeParameterKind == TypeParameterKind.Method)
                return $"Transpose.getDefaultValue({TypeRef(type)})";
            // A type's own (or enclosing generic type's) parameter is a define JS function parameter
            // and thus in scope; a parameter from a different scope falls back to null.
            var inScope = _currentEmitType is not null
                && EffectiveTypeParameters(_currentEmitType).Any(p => SymbolEqualityComparer.Default.Equals(p, tp));
            return inScope ? $"Transpose.getDefaultValue({TypeRef(type)})" : "null";
        }
        if (type.TypeKind == TypeKind.Enum) return EnumDefaultLiteral((INamedTypeSymbol)type);
        // Primitives resolve to a literal default FIRST — even during the BCL self-build where
        // System.Int32 etc. are in-source structs — so a field default is `0`/`false` (order-free)
        // rather than System.Int32.getDefaultValue() (which would run before System.Int32 is defined).
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean: return "false";
            case SpecialType.System_Char:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
                return "0";
        }
        // long, ulong and decimal are deliberately NOT in that list: tps.js models them as runtime
        // OBJECTS (System.Int64/UInt64/Decimal), so their default is that type's zero instance, not
        // the literal 0 — which is a plain JS number and therefore a System.Int32 to everything that
        // asks. They fall through to the struct path below, i.e. Transpose.getDefaultValue(…), which
        // keeps a field slot the same shape as every other struct (null in the slot, the real zero
        // assigned in the constructor) and so stays order-independent during the BCL's self-build.
        // default(T?) is null whatever T is — the null state IS the default, and the runtime's
        // Nullable$1(T).getDefaultValue() returns null for every T, so this is the same value by a
        // shorter route. Routing it through getDefaultValue(Nullable$1(T)) names T for a value that
        // never depends on it, and in module mode a named type is a dependency edge: every
        // `UIcons? icon = null` optional parameter in Tesserae recorded one against its
        // five-thousand-member icon table, ten of them across the library.
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
            return "null";
        // A non-primitive struct (DateTime, Guid, a user struct) gets its zeroed value via the
        // runtime's Transpose.getDefaultValue, which dispatches to the struct's getDefaultValue()
        // (and special-cases BCL structs like System.DateTime). It works for a referenced BCL struct
        // too — the old in-source-only check returned null for `default(DateTime)` in user projects
        // (→ "reading getTime of null") — and returns null gracefully for a struct with no default.
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Struct } st)
            return $"Transpose.getDefaultValue({TypeRef(st)})";
        return "null";
    }
}
