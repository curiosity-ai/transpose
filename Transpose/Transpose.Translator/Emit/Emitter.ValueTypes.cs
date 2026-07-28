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

    /// <summary>All instance properties that participate in a record's value equality / ToString,
    /// ordered base-record members first — matching C#'s synthesized PrintMembers / Equals /
    /// GetHashCode, which chain to the base record before the derived members. A derived record
    /// that only listed its own members would drop the inherited ones from ToString/equality.</summary>
    private List<string> RecordValuePropNames(INamedTypeSymbol type)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var t = type; t is not null && t.IsRecord; t = t.BaseType)
            chain.Add(t);
        chain.Reverse(); // base → derived

        var names = new List<string>();
        foreach (var t in chain)
            foreach (var p in t.GetMembers().OfType<IPropertySymbol>()
                         .Where(p => !p.IsStatic && p.GetMethod is not null && !p.IsIndexer && p.Name != "EqualityContract"))
            {
                var n = TransposeNaming.MemberJsName(p);
                if (!names.Contains(n)) names.Add(n);
            }
        return names;
    }

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
        // Everything a value-copy must carry: backing fields/auto-props plus (for record
        // structs) the positional value properties, which are synthesized without slots.
        var slots = InstanceFieldSlots(type).ToList();
        var fields = slots.Select(f => f.name).ToList();
        if (type.IsRecord)
            foreach (var p in RecordValuePropNames(type))
                if (!fields.Contains(p)) fields.Add(p);

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

        if (type.IsRecord)
        {
            var props = RecordValuePropNames(type);
            entries.Add(() =>
            {
                _w.Write("toString: function () ");
                _w.Block(() =>
                {
                    if (props.Count == 0) { _w.WriteLine($"return \"{type.Name} {{ }}\";"); return; }
                    var parts = string.Join(" + \", \" + ", props.Select(p => $"\"{p} = \" + TransposeR.toStr(this.{p})"));
                    _w.WriteLine($"return \"{type.Name} {{ \" + {parts} + \" }}\";");
                });
            });
            // The value-equality body, shared by the object override `equals(obj)` and the
            // strongly-typed IEquatable<T> `equalsT(other)` a record synthesizes. Both are needed:
            // `a.Equals(b)` binds to IEquatable<T>.Equals → `equalsT`, while ==/collections go
            // through `equals`. Without `equalsT` a direct `.Equals(record)` call threw
            // "equalsT is not a function".
            void EmitRecordEqualsBody(string param)
            {
                _w.WriteLine($"if ({param} == null || {param}.constructor !== this.constructor) {{ return false; }}");
                _w.Write("return ");
                _w.Write(props.Count == 0 ? "true" : string.Join(" && ", props.Select(p => $"TransposeR.equals(this.{p}, {param}.{p})")));
                _w.WriteLine(";");
            }
            entries.Add(() =>
            {
                _w.Write("equals: function (o) ");
                _w.Block(() => EmitRecordEqualsBody("o"));
            });
            entries.Add(() =>
            {
                _w.Write("equalsT: function (other) ");
                _w.Block(() => EmitRecordEqualsBody("other"));
            });
            entries.Add(() =>
            {
                _w.Write("getHashCode: function () ");
                _w.Block(() =>
                {
                    _w.WriteLine("var h = 17;");
                    foreach (var p in props) _w.WriteLine($"h = (h * 31 + TransposeR.hash(this.{p})) | 0;");
                    _w.WriteLine("return h;");
                });
            });

            var positional = RecordPositionalProps(type).Select(p => TransposeNaming.MemberJsName(p)).ToList();
            if (positional.Count > 0)
            {
                entries.Add(() =>
                {
                    var holders = positional.Select((_, i) => "$p" + i).ToList();
                    _w.Write($"Deconstruct: function ({string.Join(", ", holders)}) ");
                    _w.Block(() =>
                    {
                        for (var i = 0; i < positional.Count; i++)
                            _w.WriteLine($"{holders[i]}.v = this.{positional[i]};");
                    });
                });
            }
        }
    }

    /// <summary>Emits a record's synthesized primary constructor (sets positional fields).</summary>
    private bool TryEmitRecordCtors(INamedTypeSymbol type)
    {
        if (!type.IsRecord) return false;
        var recordDecl = type.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<RecordDeclarationSyntax>().FirstOrDefault();
        var positional = recordDecl?.ParameterList?.Parameters.Select(p => NameMangler.JsIdentifier(p.Identifier.Text)).ToList() ?? new List<string>();

        // Only user-written constructors (with real ConstructorDeclarationSyntax); the
        // primary constructor lives on the record header and is emitted as "ctor" above.
        var explicitCtors = type.InstanceConstructors
            .Where(c => c.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is ConstructorDeclarationSyntax)
            .ToList();

        _w.Block(() =>
        {
            // primary ctor
            _w.Write($"ctor: function ({string.Join(", ", positional)}) ");
            _w.Block(() =>
            {
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
                foreach (var p in positional) _w.WriteLine($"this.{p} = {p};");
            });
            _w.WriteLine(explicitCtors.Count > 0 ? "," : "");
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
