using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

public sealed partial class Emitter
{
    /// <summary>Record positional properties, exposed as fields named after the parameter.</summary>
    private IEnumerable<IPropertySymbol> RecordPositionalProps(INamedTypeSymbol type)
        => type.IsRecord
            ? type.GetMembers().OfType<IPropertySymbol>().Where(IsRecordPositionalProperty)
            : Enumerable.Empty<IPropertySymbol>();

    /// <summary>
    /// The property a record synthesizes for one primary-constructor parameter. It is NOT
    /// <see cref="ISymbol.IsImplicitlyDeclared"/> — Roslyn points its declaring syntax at the
    /// parameter, which also hides it from <see cref="IsAutoProperty"/> (that looks for a
    /// PropertyDeclarationSyntax). Testing IsImplicitlyDeclared therefore matched nothing, so a
    /// record's positional members got no field slot (`default(RS).X` was `undefined`, not 0) and
    /// its synthesized <c>Deconstruct</c> was never emitted (`var (x, y) = rs` threw
    /// "Deconstruct is not a function").
    /// </summary>
    internal static bool IsRecordPositionalProperty(IPropertySymbol p)
        => p.ContainingType is { IsRecord: true }
           && !p.IsStatic && !p.IsIndexer
           && p.Name != "EqualityContract"
           && (p.IsImplicitlyDeclared
               || p.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is ParameterSyntax));

    /// <summary>The record inheritance chain ending at <paramref name="type"/>, base record first.
    /// C#'s synthesized PrintMembers / Equals / GetHashCode chain to the base record before handling
    /// the derived members, so a derived record that only listed its own members would drop the
    /// inherited ones from ToString/equality.</summary>
    private static List<INamedTypeSymbol> RecordChain(INamedTypeSymbol type)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var t = type; t is not null && t.IsRecord; t = t.BaseType)
            chain.Add(t);
        chain.Reverse(); // base → derived
        return chain;
    }

    /// <summary>
    /// The members one record's synthesized <c>PrintMembers</c> prints: its own non-static
    /// <b>public</b> fields and <b>public readable</b> properties, in declaration order. Note this is
    /// a different set from the one equality uses (<see cref="RecordEqualitySlots"/>): ToString prints
    /// members — including computed properties, which have no storage — while Equals compares fields,
    /// including private ones. A non-public member is printed by neither, and <c>EqualityContract</c>
    /// (protected) is excluded by the accessibility test.
    /// </summary>
    /// <remarks>The printed LABEL is the C# member name (that is what .NET writes), while the value is
    /// read through the member's JS name — the two differ for a <c>[Name]</c>-renamed member or one that
    /// took a hiding slot.</remarks>
    private static List<(string label, string js, ITypeSymbol type)> RecordPrintableMembers(INamedTypeSymbol type)
    {
        var members = new List<(string, string, ITypeSymbol)>();
        foreach (var m in type.GetMembers())
        {
            if (m.IsStatic || m.DeclaredAccessibility != Accessibility.Public) continue;
            // An OVERRIDE is not a member of this record's own set: the base record's PrintMembers
            // already prints it, and virtual dispatch reads the override's value there — printing it
            // again here gave `OverrideProp2 { A = 1, V = 2, V = 2 }`. A `new` member is different: C#
            // prints both the hidden and the hiding one, which the base/derived split does naturally.
            if (m.IsOverride) continue;
            switch (m)
            {
                // A backing field (AssociatedSymbol) is private, so it never reaches here; the
                // property it backs is printed instead.
                case IFieldSymbol { IsConst: false, AssociatedSymbol: null } f when f.CanBeReferencedByName:
                    members.Add((f.Name, TransposeNaming.MemberJsName(f), f.Type));
                    break;
                case IPropertySymbol { IsIndexer: false, IsAbstract: false } p when p.GetMethod is not null:
                    members.Add((p.Name, TransposeNaming.MemberJsName(p), p.Type));
                    break;
            }
        }
        return members;
    }

    /// <summary>
    /// The instance slots a record's synthesized <c>Equals</c>/<c>GetHashCode</c> compare, base record
    /// first. C# compares a record's <b>fields</b> — every instance field, public or private, including
    /// the compiler-generated backing field of each auto-property — which is exactly the set of slots
    /// the emitter lays down. Comparing the <i>properties</i> instead diverged twice over: a public
    /// field of a record body was ignored, and a computed (storage-less) property was compared, so
    /// <c>record Node(int V) { public int[] Cache => new[] { V }; }</c> reported two equal values as
    /// unequal because each read allocated a fresh array.
    /// </summary>
    private List<(string name, ITypeSymbol? type)> RecordEqualitySlots(INamedTypeSymbol type)
    {
        var slots = new List<(string, ITypeSymbol?)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in RecordChain(type))
            foreach (var (name, _, symbol) in InstanceFieldSlots(t))
                if (seen.Add(name))
                    slots.Add((name, symbol switch
                    {
                        IFieldSymbol f => f.Type,
                        IPropertySymbol p => p.Type,
                        IEventSymbol e => e.Type,
                        _ => null,
                    }));
        return slots;
    }

    /// <summary>The record member C# synthesizes for this signature, or null when the record declares
    /// its own (which the ordinary method walk emits instead, so nothing must be synthesized for it).
    /// Recognised by <see cref="ISymbol.IsImplicitlyDeclared"/> — true for every member the record
    /// machinery adds, and false as soon as the user writes one.</summary>
    private static IMethodSymbol? SynthesizedRecordMethod(
        INamedTypeSymbol type, string name, Func<IMethodSymbol, bool> signature)
        => type.GetMembers(name).OfType<IMethodSymbol>()
            .FirstOrDefault(m => !m.IsStatic && m.IsImplicitlyDeclared && signature(m));

    /// <summary>The record's <c>PrintMembers(StringBuilder)</c> signature — not just any one-parameter
    /// method of that name, which an unrelated overload could also be.</summary>
    private static bool IsRecordPrintMembers(IMethodSymbol m)
        => m.Parameters is [{ Type: { Name: "StringBuilder" } t }]
           && t.ContainingNamespace?.ToDisplayString() == "System.Text";

    /// <summary>The declared type behind each emitted instance slot, keyed by its JS name.</summary>
    private static Dictionary<string, ITypeSymbol> SlotTypesByName(
        IEnumerable<(string name, string def, ISymbol symbol)> slots)
    {
        var map = new Dictionary<string, ITypeSymbol>(StringComparer.Ordinal);
        foreach (var (name, _, symbol) in slots)
        {
            ITypeSymbol? t = symbol switch
            {
                IFieldSymbol f    => f.Type,
                IPropertySymbol p => p.Type,
                IEventSymbol e    => e.Type,
                _                 => null,
            };
            if (t is not null) map[name] = t;
        }
        return map;
    }

    /// <summary>How one slot must be carried across a struct's value copy.</summary>
    private enum ValueCopy
    {
        /// <summary>Copy the JS value as-is (a primitive, or a reference the struct genuinely shares).</summary>
        Reference,
        /// <summary>The slot is itself a struct: clone it, or the copy would share the same object.</summary>
        Struct,
        /// <summary>Statically a type parameter — only the runtime value can say whether it is a struct.</summary>
        Dynamic,
    }

    /// <summary>
    /// Whether copying a struct must also copy this slot. A struct assignment (and every implicit
    /// copy: passing/returning by value, an array fill, boxing, storing into a collection, `with`)
    /// is a VALUE copy in C#, so a slot that is itself a struct must be cloned — sharing the one JS
    /// object made `var b = a; b.Inner.V = 9;` write through to `a.Inner.V`. Primitives, enums and
    /// reference types are copied as-is: that IS their C# semantics. A <c>Nullable&lt;T&gt;</c>
    /// follows T (a nullable struct is emitted as the struct object or null).
    /// </summary>
    private static ValueCopy ValueCopyKind(ITypeSymbol type)
    {
        // The runtime value decides for a type parameter: `struct Wrap<T> { public T Value; }` needs
        // a clone for Wrap<SomeStruct> and must NOT clone for Wrap<SomeClass>.
        if (type is ITypeParameterSymbol) return ValueCopy.Dynamic;
        if (type.TypeKind != TypeKind.Struct) return ValueCopy.Reference;
        // Emitted as a JS number/boolean (or an immutable Long/Decimal object) — nothing to alias.
        if (IsPrimitiveNumericOrBool(type)) return ValueCopy.Reference;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1 } n)
            return ValueCopyKind(n.TypeArguments[0]);
        return ValueCopy.Struct;
    }

    /// <summary>The right-hand side that copies one slot in a struct's <c>$clone</c>.</summary>
    private static string CloneSlotExpression(string name, Dictionary<string, ITypeSymbol> slotTypes)
    {
        var source = $"this.{name}";
        // A record's inherited/computed value property has no slot of its own; copy it verbatim.
        if (!slotTypes.TryGetValue(name, out var type)) return source;
        return ValueCopyKind(type) switch
        {
            ValueCopy.Struct  => $"TransposeR.clone({source})",
            ValueCopy.Dynamic => $"TransposeR.cloneValue({source})",
            _                 => source,
        };
    }

    /// <summary>True if the type itself declares an instance method with this name and arity — i.e. a
    /// hand-written Equals/GetHashCode that must win over the synthesized value-wise one.</summary>
    private static bool DeclaresOwn(INamedTypeSymbol type, string name, int parameterCount)
        => type.GetMembers(name).OfType<IMethodSymbol>()
            .Any(m => !m.IsStatic && !m.IsImplicitlyDeclared && m.Parameters.Length == parameterCount);

    /// <summary>Appends synthesized value-type methods (struct $clone/equals, record members).</summary>
    private void AddValueTypeMethodEntries(INamedTypeSymbol type, List<Action> entries)
    {
        // Everything a value-copy must carry: the type's real slots (fields, auto-properties and a
        // record's positional properties, which InstanceFieldSlots already covers). Only slots — a
        // get-only computed property is not storage, and assigning one in $clone threw "Cannot set
        // property … which has only a getter" for `readonly record struct RS(int X) { int D => X*2; }`.
        var slots = InstanceFieldSlots(type).ToList();
        var fields = slots.Select(f => f.name).ToList();

        if (type.TypeKind == TypeKind.Struct)
        {
            var slotTypes = SlotTypesByName(slots);
            entries.Add(() =>
            {
                _w.Write("$clone: function (to) ");
                _w.Block(() =>
                {
                    // A generic instantiation like Entry(TKey, TValue) is a factory call and must
                    // be parenthesized before `new` (new (Entry(TKey,TValue))(), not new Entry(…)()).
                    var typeRef = TypeRef(type);
                    var newTarget = typeRef.Contains('(') ? $"({typeRef})" : typeRef;
                    _w.WriteLine($"var s = to || new {newTarget}();");
                    foreach (var f in fields) _w.WriteLine($"s.{f} = {CloneSlotExpression(f, slotTypes)};");
                    _w.WriteLine("return s;");
                });
            });
        }

        // A plain (non-record) struct: .NET gives every value type field-wise Equals/GetHashCode via
        // ValueType, so two structs with equal fields are equal and hash alike — that is what makes a
        // struct usable as a Dictionary/HashSet key. Without these the JS object fell back to
        // reference identity, so `d[new Key { A = 1 }]` threw KeyNotFoundException for a key that was
        // just added. Records already synthesize their own (over properties) below; a struct that
        // declares either member keeps it.
        if (type is { TypeKind: TypeKind.Struct, IsRecord: false })
        {
            if (!DeclaresOwn(type, "Equals", 1))
            {
                entries.Add(() =>
                {
                    _w.Write("equals: function (o) ");
                    _w.Block(() =>
                    {
                        _w.WriteLine("if (o == null || o.constructor !== this.constructor) { return false; }");
                        _w.Write("return ");
                        _w.Write(fields.Count == 0 ? "true"
                            : string.Join(" && ", fields.Select(f => $"TransposeR.equals(this.{f}, o.{f})")));
                        _w.WriteLine(";");
                    });
                });
            }
            if (!DeclaresOwn(type, "GetHashCode", 0))
            {
                entries.Add(() =>
                {
                    _w.Write("getHashCode: function () ");
                    _w.Block(() =>
                    {
                        _w.WriteLine("var h = 17;");
                        foreach (var f in fields) _w.WriteLine($"h = (h * 31 + TransposeR.hash(this.{f})) | 0;");
                        _w.WriteLine("return h;");
                    });
                });
            }
        }

        if (type.IsRecord) AddRecordMethodEntries(type, entries);
    }

    /// <summary>
    /// Appends the members C# synthesizes for a record: <c>PrintMembers</c>/<c>ToString</c>,
    /// <c>Equals(object)</c>/<c>Equals(T)</c>/<c>GetHashCode</c> and <c>Deconstruct</c>.
    ///
    /// Each one is emitted only when the record does not declare it itself — a hand-written
    /// <c>ToString</c>, <c>PrintMembers</c>, <c>Equals</c> or <c>Deconstruct</c> is emitted by the
    /// ordinary member walk, and synthesizing a second entry under the same key made JavaScript keep
    /// whichever came last (so the user's override silently never ran). Every key comes from
    /// <see cref="TransposeNaming.MemberJsName"/> on the synthesized symbol itself, which also keeps a
    /// synthesized member and a same-named user overload on distinct, correctly-numbered slots (a
    /// record with its own two-out-parameter <c>Deconstruct</c> used to collide with the synthesized
    /// one-parameter form).
    /// </summary>
    private void AddRecordMethodEntries(INamedTypeSymbol type, List<Action> entries)
    {
        // ToString() is `"Name { " + PrintMembers(sb) + " }"`, routed through the real PrintMembers so
        // a hand-written or inherited override participates — matching C#, where ToString calls the
        // virtual PrintMembers and a derived record's PrintMembers chains to its base.
        var printMembers = SynthesizedRecordMethod(type, "PrintMembers", IsRecordPrintMembers);
        if (printMembers is not null)
        {
            var printName = TransposeNaming.MemberJsName(printMembers);
            var printable = RecordPrintableMembers(type);
            // A derived record prints the base record's members first, via the base's PrintMembers.
            var basePrint = type.BaseType is { IsRecord: true } br ? $"{TypeRef(br)}.prototype.{printName}" : null;
            entries.Add(() =>
            {
                _w.Write($"{NameMangler.JsPropertyKey(printName)}: function (builder) ");
                _w.Block(() =>
                {
                    if (basePrint is not null)
                    {
                        // The base returns whether it printed anything; only then is a separator due.
                        _w.WriteLine($"var $printed = {basePrint}.call(this, builder);");
                        if (printable.Count == 0) { _w.WriteLine("return $printed;"); return; }
                        _w.WriteLine("if ($printed) { builder.append(\", \"); }");
                    }
                    else if (printable.Count == 0)
                    {
                        _w.WriteLine("return false;");
                        return;
                    }
                    for (var i = 0; i < printable.Count; i++)
                    {
                        if (i > 0) _w.WriteLine("builder.append(\", \");");
                        var (label, js, memberType) = printable[i];
                        _w.WriteLine($"builder.append(\"{label} = \");");
                        _w.WriteLine($"builder.append({ToStringJs($"this.{js}", memberType)});");
                    }
                    _w.WriteLine("return true;");
                });
            });
        }

        var toString = SynthesizedRecordMethod(type, "ToString", m => m.Parameters.Length == 0);
        if (toString is not null)
        {
            // The PrintMembers ToString calls — the synthesized one above, or the record's own.
            var printName = TransposeNaming.MemberJsName(
                type.GetMembers("PrintMembers").OfType<IMethodSymbol>().First(m => !m.IsStatic && IsRecordPrintMembers(m)));
            entries.Add(() =>
            {
                _w.Write($"{NameMangler.JsPropertyKey(TransposeNaming.MemberJsName(toString))}: function () ");
                _w.Block(() =>
                {
                    _w.WriteLine("var $sb = new System.Text.StringBuilder();");
                    _w.WriteLine($"$sb.append(\"{type.Name}\");");
                    _w.WriteLine("$sb.append(\" { \");");
                    _w.WriteLine($"if (this.{printName}($sb)) {{ $sb.append(\" \"); }}");
                    _w.WriteLine("$sb.append(\"}\");");
                    _w.WriteLine("return $sb.toString();");
                });
            });
        }

        // The value-equality body, shared by the object override `equals(obj)` and the strongly-typed
        // IEquatable<T> `equalsT(other)` a record synthesizes. Both are needed: `a.Equals(b)` binds to
        // IEquatable<T>.Equals → `equalsT`, while ==/collections go through `equals`. Without
        // `equalsT` a direct `.Equals(record)` call threw "equalsT is not a function".
        var slots = RecordEqualitySlots(type);
        void EmitRecordEqualsBody(string param)
        {
            _w.WriteLine($"if ({param} == null || {param}.constructor !== this.constructor) {{ return false; }}");
            _w.Write("return ");
            _w.Write(slots.Count == 0
                ? "true"
                : string.Join(" && ", slots.Select(s => $"TransposeR.equals(this.{s.name}, {param}.{s.name})")));
            _w.WriteLine(";");
        }

        // The strongly-typed Equals a record gets from IEquatable<T> — synthesized, or hand-written when
        // the record defines its own value equality. A derived record also carries a sealed override of
        // the base record's Equals(Base); both land on the same JS slot, so prefer the one whose
        // parameter is this very type to keep the choice deterministic.
        var typedEquals = type.GetMembers("Equals").OfType<IMethodSymbol>()
            .Where(m => m is { IsStatic: false, Parameters.Length: 1 }
                        && m.Parameters[0].Type.SpecialType != SpecialType.System_Object)
            .OrderByDescending(m => SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, type))
            .FirstOrDefault();

        var equalsObj = SynthesizedRecordMethod(type, "Equals",
            m => m.Parameters is [{ Type.SpecialType: SpecialType.System_Object }]);
        if (equalsObj is not null)
            entries.Add(() =>
            {
                _w.Write($"{NameMangler.JsPropertyKey(TransposeNaming.MemberJsName(equalsObj))}: function (o) ");
                _w.Block(() =>
                {
                    // C# synthesizes `Equals(object obj) => Equals(obj as T)`, so the object override
                    // must DELEGATE to the typed one rather than repeat the field-wise comparison: a
                    // record that writes its own Equals(T) governs ==, HashSet/Dictionary lookups AND
                    // `Equals((object)x)` alike, and a duplicated body ignored it for the last of those.
                    if (typedEquals is null) { EmitRecordEqualsBody("o"); return; }
                    _w.WriteLine("if (o == null || o.constructor !== this.constructor) { return false; }");
                    _w.WriteLine($"return this.{TransposeNaming.MemberJsName(typedEquals)}(o);");
                });
            });

        if (typedEquals is { IsImplicitlyDeclared: true })
            entries.Add(() =>
            {
                _w.Write($"{NameMangler.JsPropertyKey(TransposeNaming.MemberJsName(typedEquals))}: function (other) ");
                _w.Block(() => EmitRecordEqualsBody("other"));
            });

        var getHashCode = SynthesizedRecordMethod(type, "GetHashCode", m => m.Parameters.Length == 0);
        if (getHashCode is not null)
            entries.Add(() =>
            {
                _w.Write($"{NameMangler.JsPropertyKey(TransposeNaming.MemberJsName(getHashCode))}: function () ");
                _w.Block(() =>
                {
                    _w.WriteLine("var h = 17;");
                    foreach (var s in slots) _w.WriteLine($"h = (h * 31 + TransposeR.hash(this.{s.name})) | 0;");
                    _w.WriteLine("return h;");
                });
            });

        var positional = RecordPositionalProps(type).Select(p => TransposeNaming.MemberJsName(p)).ToList();
        var deconstruct = SynthesizedRecordMethod(type, "Deconstruct", m => m.Parameters.Length == positional.Count);
        if (positional.Count > 0 && deconstruct is not null)
        {
            entries.Add(() =>
            {
                var holders = positional.Select((_, i) => "$p" + i).ToList();
                _w.Write($"{NameMangler.JsPropertyKey(TransposeNaming.MemberJsName(deconstruct))}: function ({string.Join(", ", holders)}) ");
                _w.Block(() =>
                {
                    for (var i = 0; i < positional.Count; i++)
                        _w.WriteLine($"{holders[i]}.v = this.{positional[i]};");
                });
            });
        }
    }

    /// <summary>Emits a record's synthesized primary constructor (sets positional fields).</summary>
    private bool TryEmitRecordCtors(INamedTypeSymbol type)
    {
        if (!type.IsRecord) return false;
        var recordDecl = type.DeclaringSyntaxReferences.Select(r => r.GetSyntax())
            .OfType<RecordDeclarationSyntax>().FirstOrDefault(r => r.ParameterList is not null)
            ?? type.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<RecordDeclarationSyntax>().FirstOrDefault();
        var positional = recordDecl?.ParameterList?.Parameters.Select(p => NameMangler.JsIdentifier(p.Identifier.Text)).ToList() ?? new List<string>();

        // The primary constructor's symbol, for the parameter defaults: a positional parameter may be
        // optional (`record Defaults(int X = 5)`), and a JS call that omits it passes undefined, so the
        // default has to be applied in the body exactly as it is for an ordinary constructor. Without
        // this `new Defaults()` left every omitted member undefined.
        var primaryCtor = type.InstanceConstructors
            .FirstOrDefault(c => c.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is RecordDeclarationSyntax);

        // Only the parameters the record actually synthesized a property for are stored. When the body
        // declares its own member of that name C# suppresses the synthesized property and the parameter
        // becomes an ordinary constructor parameter (referenced by initializers, nothing more);
        // assigning it anyway clobbered the declared member, so
        // `record R(int X) { public int X { get; init; } = X * 2; }` yielded 3 instead of 6.
        // The store targets the property's SLOT, which is not always the parameter's own name — a
        // [property: Name("jsX")] positional member lives at `jsX`, and writing `this.X` left the slot
        // (and so ToString, equality and Deconstruct, which all read the slot) at its default.
        var stored = RecordPositionalProps(type).ToDictionary(p => p.Name, TransposeNaming.MemberJsName, StringComparer.Ordinal);
        var storedPositional = recordDecl?.ParameterList?.Parameters
            .Where(p => stored.ContainsKey(p.Identifier.Text))
            .Select(p => (slot: stored[p.Identifier.Text], param: NameMangler.JsIdentifier(p.Identifier.Text)))
            .ToList() ?? new List<(string slot, string param)>();

        // Only user-written constructors (with real ConstructorDeclarationSyntax); the
        // primary constructor lives on the record header and is emitted as "ctor" above.
        var explicitCtors = type.InstanceConstructors
            .Where(c => c.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is ConstructorDeclarationSyntax)
            .ToList();

        // A `record struct` also has the implicit parameterless struct constructor that `new S()`,
        // `default(S)` and `: this()` reach. The positional primary owns the plain "ctor" slot, so this
        // one is named $ctorN — and it has no syntax of its own, so neither branch above emitted it and
        // `new S()` on a `record struct S(int X)` threw "S.$ctor1 is not a constructor". It zeroes the
        // value rather than running the declared field initializers, matching C#.
        var structDefaultCtor = type.TypeKind == TypeKind.Struct
            ? type.InstanceConstructors.FirstOrDefault(c => c is { Parameters.Length: 0, IsImplicitlyDeclared: true })
            : null;
        // A record struct with no positional parameters already emits that very constructor as "ctor".
        if (structDefaultCtor is not null && CtorName(structDefaultCtor) == "ctor") structDefaultCtor = null;

        _w.Block(() =>
        {
            // primary ctor
            _w.Write($"ctor: function ({string.Join(", ", positional)}) ");
            _w.Block(() =>
            {
                if (primaryCtor is not null) EmitOptionalDefaults(primaryCtor);
                _w.WriteLine("this.$initialize();");
                // A record body may declare ordinary fields/auto-properties, with or without an
                // initializer, and the positional constructor is the only one that runs — so it has
                // to do what every other constructor does: run the initializers and zero-init the
                // struct-typed slots. Without this a `record R(int X) { public int K = 7; }` left K
                // at 0, a `public Nested N = new Nested();` stayed null, and a struct-typed field was
                // null instead of default(T) (so `r.I.V = 1` threw "Cannot set properties of null").
                // Emitted BEFORE the base call, matching C#: a derived record's initializers run
                // first, then the base constructor (verified against native .NET).
                EmitInstanceFieldInitializers(type);
                var baseType = type.BaseType;
                if (baseType is { } bt && bt.SpecialType != SpecialType.System_Object && bt.TypeKind != TypeKind.Error && bt.IsRecord)
                {
                    // A derived record forwards to the base record's positional ctor with the args
                    // from `: Base(a, b)` on the record header — without them the base's positional
                    // members stay at their defaults (e.g. `record D(...) : B(A, B)` dropped A/B).
                    var primaryBase = recordDecl?.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>().FirstOrDefault();
                    if (primaryBase is not null && _model.GetSymbolInfo(primaryBase).Symbol is IMethodSymbol baseCtor)
                    {
                        _w.Write($"{TypeRef(bt)}.{ExternalAwareCtorName(baseCtor)}.call(this");
                        if (primaryBase.ArgumentList.Arguments.Count > 0) { _w.Write(", "); EmitArguments(primaryBase.ArgumentList, baseCtor); }
                        _w.WriteLine(");");
                    }
                    else
                    {
                        _w.WriteLine($"{TypeRef(bt)}.ctor.call(this);");
                    }
                }
                foreach (var (slot, param) in storedPositional) _w.WriteLine($"this.{slot} = {param};");
            });
            _w.WriteLine(explicitCtors.Count > 0 || structDefaultCtor is not null ? "," : "");

            if (structDefaultCtor is not null)
            {
                _w.Write($"{CtorName(structDefaultCtor)}: function () ");
                _w.Block(() =>
                {
                    _w.WriteLine("this.$initialize();");
                    EmitInstanceFieldInitializers(type, runInitializers: false);
                });
                _w.WriteLine(explicitCtors.Count > 0 ? "," : "");
            }

            // explicit ctors kept as $ctorN. C# requires each to chain `: this(...)` back to the
            // positional primary; emit that delegation (EmitConstructorChain) then the body — else
            // the delegated-to primary never runs and the record's members stay unset.
            for (var i = 0; i < explicitCtors.Count; i++)
            {
                var decl = (ConstructorDeclarationSyntax)explicitCtors[i].DeclaringSyntaxReferences[0].GetSyntax();
                _w.Write($"{CtorName(explicitCtors[i])}: function (");
                EmitParameterList(explicitCtors[i]);
                _w.Write(") ");
                _w.Block(() =>
                {
                    _w.WriteLine("this.$initialize();");
                    EmitConstructorChain(explicitCtors[i], decl, type);
                    if (decl.Body is not null) EmitStatements(decl.Body.Statements);
                    else if (decl.ExpressionBody is not null) EmitExpressionStatement(decl.ExpressionBody.Expression);
                });
                _w.WriteLine(i < explicitCtors.Count - 1 ? "," : "");
            }
        });
        return true;
    }
}
