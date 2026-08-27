using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

public sealed partial class Emitter
{
    // ---- statics -----------------------------------------------------------

    private void EmitStatics(INamedTypeSymbol type, string fullName)
    {
        var staticFields = type.GetMembers().Where(m => m.IsStatic).Select(m => m switch
        {
            IFieldSymbol f when !f.IsConst && f.AssociatedSymbol is null => ((string name, string def)?)(TransposeNaming.MemberJsName(f), FieldDefaultLiteral(f.Type)),
            // A const is inlined at every use site, but it is also emitted as a real static slot
            // holding its value: the member then exists for reflection, for a debugger, and for
            // hand-written JS reaching into the type — matching the reference runtime, which
            // exposes consts as static fields. An enum's members are consts too, but the enum type
            // has its own emit path, so they must not be duplicated here.
            IFieldSymbol { IsConst: true, ContainingType.TypeKind: not TypeKind.Enum } c when c.CanBeReferencedByName
                => (TransposeNaming.MemberJsName(c), ConstantLiteral(c.ConstantValue, c.Type)),
            IPropertySymbol p when IsAutoProperty(p) => (TransposeNaming.MemberJsName(p), FieldDefaultLiteral(p.Type)),
            _ => null,
        }).Where(x => x is not null).Select(x => x!.Value).ToList();

        var staticInitAssignments = StaticInitializers(type).ToList();
        // Static non-primitive-struct fields/auto-props with no initializer default to the zeroed
        // struct (like the instance path), not the `null` their slot holds — otherwise a member
        // access on an uninitialized static DateTime/Guid/… throws a JS TypeError.
        var staticStructDefaults = StaticStructDefaults(type).ToList();
        var staticCtor = type.StaticConstructors.FirstOrDefault(c => c.DeclaringSyntaxReferences.Length > 0);
        var staticMethods = type.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.IsStatic && !m.IsImplicitlyDeclared && IsEmittableMethod(m) && !IsEntryPoint(m))
            .ToList();
        var staticProps = type.GetMembers().OfType<IPropertySymbol>()
            .Where(p => p.IsStatic && !p.IsAbstract && !IsAutoProperty(p) && !IsExternProperty(p) && !p.IsIndexer)
            .ToList();

        var sections = new List<Action>();

        // Structs expose a getDefaultValue() static returning a zero-initialized value, sharing
        // the single `methods:` block with the struct's own static methods.
        if (type.TypeKind == TypeKind.Struct)
        {
            var slots = InstanceFieldSlots(type).ToList();
            // Bind the static methods in a dedicated local: the section runs LATER, and
            // `staticMethods` is cleared below so the general `methods:` section (further down)
            // doesn't emit a duplicate key. Capturing the variable directly would see the cleared
            // list at execution time and silently drop every struct static method.
            var structStaticMethods = staticMethods;
            sections.Add(() =>
            {
                _w.Write("methods: ");
                _w.Block(() =>
                {
                    _w.Write("getDefaultValue: function () ");
                    _w.Block(() =>
                    {
                        _w.WriteLine("var $ = Object.create(this.prototype);");
                        foreach (var (name, def, sym) in slots)
                        {
                            // A non-primitive struct field (DateTime, Guid, a nested struct, …)
                            // defaults to the ZEROED struct, not null — this factory produces the
                            // real default(T), so recurse via getDefaultValue instead of the `null`
                            // slot literal (which is only the order-independent define-time placeholder).
                            // Otherwise e.g. default(DateTimeOffset).m_dateTime is null and .UtcDateTime
                            // throws "reading getTime of null".
                            var slotType = sym switch
                            {
                                IFieldSymbol f    => f.Type,
                                IPropertySymbol p => p.Type,
                                _                 => null,
                            };
                            var value = slotType is not null && NeedsStructDefaultInit(slotType)
                                ? $"Transpose.getDefaultValue({TypeRef(slotType)})"
                                : def;
                            _w.WriteLine($"$.{name} = {value};");
                        }
                        _w.WriteLine("return $;");
                    });
                    if (structStaticMethods.Count > 0)
                    {
                        _w.WriteLine(",");
                        for (var i = 0; i < structStaticMethods.Count; i++)
                        {
                            EmitMethodEntry(structStaticMethods[i]);
                            _w.WriteLine(i < structStaticMethods.Count - 1 ? "," : "");
                        }
                    }
                    else { _w.WriteLine(); }
                });
            });
            staticMethods = new List<IMethodSymbol>(); // consumed above
        }

        if (staticFields.Count > 0)
        {
            sections.Add(() =>
            {
                _w.Write("fields: ");
                _w.Block(() =>
                {
                    for (var i = 0; i < staticFields.Count; i++)
                    {
                        _w.Write($"{NameMangler.JsPropertyKey(staticFields[i].name)}: {staticFields[i].def}");
                        _w.WriteLine(i < staticFields.Count - 1 ? "," : "");
                    }
                });
            });
        }

        if (staticInitAssignments.Count > 0 || staticStructDefaults.Count > 0 || staticCtor is not null)
        {
            sections.Add(() =>
            {
                _w.Write("ctors: ");
                _w.Block(() =>
                {
                    _w.Write("init: function () ");
                    _w.Block(() =>
                    {
                        // For a generic type, the static init runs per closed instantiation with
                        // `this` bound to that closed type — where its statics live and where
                        // instances read them (Name$arity(args).Field). Assigning through the
                        // open generic-definition name (fullName) would set a property nothing
                        // reads. A non-generic type's static init also runs with `this` = the type,
                        // so `this` is correct for both.
                        var staticRef = EffectiveTypeParameters(type).Count > 0 ? "this" : fullName;
                        // Zero-init struct statics first (order-independent), before the explicit
                        // initializers which run in declaration order and may reference them.
                        foreach (var (target, slotType) in staticStructDefaults)
                            _w.WriteLine($"{staticRef}.{target} = Transpose.getDefaultValue({TypeRef(slotType)});");
                        foreach (var (target, init, slotType) in staticInitAssignments)
                        {
                            _w.Write($"{staticRef}.{target} = ");
                            EmitExpressionConverted(init, slotType);
                            _w.WriteLine(";");
                        }
                        if (staticCtor?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is ConstructorDeclarationSyntax { Body: { } body })
                        {
                            EmitStatements(body.Statements);
                        }
                    });
                    _w.WriteLine();
                });
            });
        }

        if (staticMethods.Count > 0)
        {
            sections.Add(() =>
            {
                _w.Write("methods: ");
                EmitMethodMap(staticMethods, fullName);
            });
        }

        if (staticProps.Count > 0)
        {
            sections.Add(() =>
            {
                _w.Write("properties: ");
                EmitPropertyMap(staticProps);
            });
        }

        if (sections.Count == 0) return;

        _w.Block(() =>
        {
            for (var i = 0; i < sections.Count; i++)
            {
                sections[i]();
                _w.WriteLine(i < sections.Count - 1 ? "," : "");
            }
        });
    }

    private IEnumerable<(string target, ExpressionSyntax init, ITypeSymbol slotType)> StaticInitializers(INamedTypeSymbol type)
    {
        foreach (var m in type.GetMembers().Where(m => m.IsStatic))
        {
            if (m is IFieldSymbol f && !f.IsConst && f.AssociatedSymbol is null && FieldInitializerSyntax(f) is { } fi)
                yield return (TransposeNaming.MemberJsName(f), fi, f.Type);
            else if (m is IPropertySymbol p && IsAutoProperty(p) && AutoPropertyInitializerSyntax(p) is { } pi)
                yield return (TransposeNaming.MemberJsName(p), pi, p.Type);
        }
    }

    /// <summary>Static non-primitive-struct fields/auto-props with no explicit initializer — their
    /// slot holds <c>null</c>, so the zeroed struct default is assigned in the static <c>init</c>
    /// (mirrors the instance-field path). Excludes primitives, enums and Nullable&lt;T&gt;.</summary>
    private IEnumerable<(string target, ITypeSymbol type)> StaticStructDefaults(INamedTypeSymbol type)
    {
        foreach (var m in type.GetMembers().Where(m => m.IsStatic))
        {
            if (m is IFieldSymbol f && !f.IsConst && f.AssociatedSymbol is null
                && FieldInitializerSyntax(f) is null && NeedsStructDefaultInit(f.Type))
                yield return (TransposeNaming.MemberJsName(f), f.Type);
            else if (m is IPropertySymbol p && IsAutoProperty(p) && !p.IsAbstract && !p.IsIndexer
                && AutoPropertyInitializerSyntax(p) is null && NeedsStructDefaultInit(p.Type))
                yield return (TransposeNaming.MemberJsName(p), p.Type);
        }
    }

    // ---- instance constructors ---------------------------------------------

    private readonly Dictionary<ISymbol, string> _ctorNames = new(SymbolEqualityComparer.Default);

    private string CtorName(IMethodSymbol ctor)
    {
        ctor = ctor.OriginalDefinition;
        // External BCL types were baked into tps.js with Transpose's OverloadsCollection ctor numbering;
        // match it so e.g. new Guid(string) resolves to $ctor4. A referenced Transpose-compiled package
        // (non-source but non-external) was emitted by THIS compiler's own numbering below, so it
        // must be numbered the same way here — over its full ctor set (private ones included,
        // surfaced via MetadataImportOptions.All) — for call sites to resolve to the same $ctorN.
        if (!TransposeNaming.IsTransposeCompiledSource(ctor.ContainingType))
            return TransposeNaming.ConstructorName(ctor);
        if (_ctorNames.TryGetValue(ctor, out var cached)) return cached;

        var ctors = ctor.ContainingType.InstanceConstructors
            .Where(c => !IsRecordCopyCtor(c))
            .OrderBy(c => c.Parameters.Length)
            .ThenBy(c => string.Join(",", c.Parameters.Select(p => p.Type.ToDisplayString())), StringComparer.Ordinal)
            .ToList();

        if (ctors.Count == 1)
        {
            _ctorNames[ctors[0].OriginalDefinition] = "ctor";
        }
        else
        {
            // A record's positional primary constructor (declared on the record header) is the
            // "ctor"; otherwise the parameterless constructor is. (For a `record struct` the
            // implicit parameterless struct ctor must NOT usurp the positional primary.)
            var primary = ctors.FirstOrDefault(c => c.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is RecordDeclarationSyntax)
                          ?? ctors.FirstOrDefault(c => c.Parameters.Length == 0)
                          ?? ctors[0];
            var n = 1;
            foreach (var c in ctors)
            {
                _ctorNames[c.OriginalDefinition] = ReferenceEquals(c, primary) ? "ctor" : "$ctor" + n++;
            }
        }
        return _ctorNames.TryGetValue(ctor, out var name) ? name : "ctor";
    }

    private static bool IsRecordCopyCtor(IMethodSymbol c)
        => c.ContainingType.IsRecord && c.Parameters.Length == 1
           && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, c.ContainingType);

    /// <summary>Ctor name honouring that genuinely external (native-JS) types expose only "ctor".
    /// A transpiled BCL runtime type (e.g. System.SystemException) is NOT external — its base call
    /// must use the real overload name ($ctorN) so, e.g., `: base(message)` reaches the message
    /// ctor rather than the parameterless one. Only [External]/scoped types collapse to "ctor".</summary>
    private string ExternalAwareCtorName(IMethodSymbol ctor)
        => TransposeNaming.IsExternalType(ctor.ContainingType) ? "ctor" : CtorName(ctor);

    private static bool IsPrimaryCtorSyntax(IMethodSymbol ctor)
        => ctor.MethodKind == MethodKind.Constructor
           && ctor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is TypeDeclarationSyntax { ParameterList: not null };

    /// <summary>The primary constructor of a non-record class/struct, or null.</summary>
    private static IMethodSymbol? PrimaryConstructor(INamedTypeSymbol type)
    {
        if (type.IsRecord) return null;
        return type.InstanceConstructors.FirstOrDefault(c =>
            c.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is TypeDeclarationSyntax { ParameterList: not null });
    }

    /// <summary>True for a primary-constructor parameter captured into instance state.</summary>
    private bool IsCapturedPrimaryCtorParam(IParameterSymbol param)
        => param.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor } ctor
           && ctor.ContainingType is { IsRecord: false } type
           && ctor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is TypeDeclarationSyntax { ParameterList: not null }
           && CapturedPrimaryParamNames(type).Contains(param.Name);

    private readonly Dictionary<INamedTypeSymbol, HashSet<string>> _capturedParamCache = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// Names of primary-ctor parameters used outside the constructor (in a method or
    /// property body) — these must be stored on the instance. Parameters used only in
    /// field initializers / base-args are consumed within the constructor.
    /// </summary>
    private HashSet<string> CapturedPrimaryParamNames(INamedTypeSymbol type)
    {
        if (_capturedParamCache.TryGetValue(type, out var cached)) return cached;
        var captured = new HashSet<string>();
        var primary = PrimaryConstructor(type);
        if (primary is null) { _capturedParamCache[type] = captured; return captured; }

        var paramNames = primary.Parameters.Select(p => p.Name).ToHashSet();
        foreach (var declRef in type.DeclaringSyntaxReferences)
        {
            if (declRef.GetSyntax() is not TypeDeclarationSyntax typeDecl) continue;
            foreach (var member in typeDecl.Members)
            {
                // Method / property / accessor / indexer bodies capture; field initializers do not.
                foreach (var id in member.DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    if (!paramNames.Contains(id.Identifier.Text)) continue;
                    if (_model.GetSymbolInfo(id).Symbol is IParameterSymbol p
                        && SymbolEqualityComparer.Default.Equals(p.ContainingSymbol, primary))
                        captured.Add(p.Name);
                }
            }
        }
        _capturedParamCache[type] = captured;
        return captured;
    }

    private void EmitInstanceCtors(INamedTypeSymbol type)
    {
        var ctors = type.InstanceConstructors
            .Where(c => (!c.IsImplicitlyDeclared || IsPrimaryCtorSyntax(c)) && c.DeclaringSyntaxReferences.Length > 0)
            .ToList();
        var hasExplicit = ctors.Count > 0;

        _w.Block(() =>
        {
            if (!hasExplicit)
            {
                // Synthesized default constructor. The field initializers run BEFORE the base
                // constructor, which is C#'s order for every derived type (and what a declared
                // constructor already does, via EmitConstructorChain) — running them after meant a
                // base constructor observed this type's slots at their defaults, and any side effect
                // in an initializer was sequenced after the base's.
                _w.Write("ctor: function () ");
                _w.Block(() =>
                {
                    _w.WriteLine("this.$initialize();");
                    EmitInstanceFieldInitializers(type);
                    EmitImplicitBaseCall(type);
                });
                _w.WriteLine();
                return;
            }

            var all = type.InstanceConstructors.Where(c => c.DeclaringSyntaxReferences.Length > 0).ToList();

            // A struct always has a parameterless constructor, even when every declared one takes
            // arguments — `new S()` and `: this()` both reach it, and CtorName already reserves the
            // plain "ctor" name for it. It has no syntax of its own, so it is absent from `all`;
            // without an entry here those call sites fell through to the runtime's synthesized
            // default, which does not zero the struct-typed slots (`new S().Inner` was null, so
            // reading through it threw "Cannot read properties of null").
            if (NeedsSynthesizedStructDefaultCtor(type, all))
            {
                _w.Write("ctor: function () ");
                _w.Block(() =>
                {
                    _w.WriteLine("this.$initialize();");
                    EmitInstanceFieldInitializers(type, runInitializers: false);
                });
                _w.WriteLine(all.Count > 0 ? "," : "");
            }

            for (var i = 0; i < all.Count; i++)
            {
                var ctor = all[i];
                var syntax = ctor.DeclaringSyntaxReferences[0].GetSyntax();
                var decl = syntax as ConstructorDeclarationSyntax;
                var isPrimary = syntax is TypeDeclarationSyntax { ParameterList: not null };
                _w.Write($"{CtorName(ctor)}: function (");
                EmitParameterList(ctor);
                _w.Write(") ");
                _w.Block(() =>
                {
                    EmitOptionalDefaults(ctor);
                    _w.WriteLine("this.$initialize();");
                    if (isPrimary)
                    {
                        // Primary constructor: run the field initializers, chain to base, then store
                        // the captured params — the same order a record's primary constructor uses,
                        // and C#'s (the derived initializers precede the base constructor). Inside
                        // this body, param refs use the raw JS parameter name.
                        _inPrimaryCtorBody = true;
                        EmitInstanceFieldInitializers(type);
                        EmitPrimaryBaseCall(type, syntax as TypeDeclarationSyntax);
                        var captured = CapturedPrimaryParamNames(type);
                        foreach (var p in ctor.Parameters.Where(p => captured.Contains(p.Name)))
                            _w.WriteLine($"this.{NameMangler.JsIdentifier(p.Name)} = {NameMangler.JsIdentifier(p.Name)};");
                        _inPrimaryCtorBody = false;
                    }
                    else
                    {
                        EmitConstructorChain(ctor, decl!, type);
                        if (decl?.Body is not null)
                            EmitStatements(decl.Body.Statements);
                        else if (decl?.ExpressionBody is not null)
                            EmitExpressionStatement(decl.ExpressionBody.Expression);
                    }
                });
                _w.WriteLine(i < all.Count - 1 ? "," : "");
            }
        });
    }

    /// <summary>True when a struct's implicit parameterless constructor has to be emitted: it is
    /// reachable (`new S()`, `: this()`, a `$clone` target) but, being implicitly declared, carries no
    /// syntax and so never appears among the constructors walked from the declaration.</summary>
    private static bool NeedsSynthesizedStructDefaultCtor(INamedTypeSymbol type, List<IMethodSymbol> emitted)
        => type.TypeKind == TypeKind.Struct
           && !type.IsRecord
           && emitted.Count > 0
           && emitted.All(c => c.Parameters.Length > 0);

    private void EmitConstructorChain(IMethodSymbol ctor, ConstructorDeclarationSyntax decl, INamedTypeSymbol type)
    {
        var initializer = decl?.Initializer;
        if (initializer is { RawKind: (int)SyntaxKind.ThisConstructorInitializer }
            && _model.GetSymbolInfo(initializer).Symbol is IMethodSymbol thisCtor)
        {
            // A constructor that chains to `: this(...)` must NOT run the instance field
            // initializers — the constructor it delegates to (the one that ultimately chains to
            // base) runs them. Emitting them here re-ran them AFTER the delegated ctor had already
            // initialized the object, overwriting its work with the field defaults (e.g.
            // `Random() : this(seed)` reset SeedArray back to all-zeros, so Random/Guid.NewGuid
            // produced a constant value).
            //
            // Delegate type-qualified (`ThisType.ctor.call(this, …)`), NOT via `this.ctor(…)`: a
            // dynamic member dispatch resolves to the MOST-DERIVED override, so in a subclass
            // instance `: this()` would re-enter the subclass constructor forever. `this(...)`
            // always targets a sibling ctor of the SAME declaring type, so bind it there.
            _w.Write($"{TypeRef(thisCtor.ContainingType)}.{CtorName(thisCtor)}.call(this");
            if (initializer.ArgumentList.Arguments.Count > 0) { _w.Write(", "); EmitArguments(initializer.ArgumentList, thisCtor); }
            _w.WriteLine(");");
            return;
        }

        EmitInstanceFieldInitializers(type);

        if (initializer is { RawKind: (int)SyntaxKind.BaseConstructorInitializer }
            && _model.GetSymbolInfo(initializer).Symbol is IMethodSymbol baseCtor)
        {
            _w.Write($"{TypeRef(baseCtor.ContainingType)}.{ExternalAwareCtorName(baseCtor)}.call(this");
            if (initializer.ArgumentList.Arguments.Count > 0) { _w.Write(", "); EmitArguments(initializer.ArgumentList, baseCtor); }
            _w.WriteLine(");");
        }
        else
        {
            EmitImplicitBaseCall(type);
        }
    }

    /// <summary>Base-constructor call for a primary constructor (honours `: Base(args)`).</summary>
    private void EmitPrimaryBaseCall(INamedTypeSymbol type, TypeDeclarationSyntax? decl)
    {
        var primaryBase = decl?.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>().FirstOrDefault();
        if (primaryBase is not null && _model.GetSymbolInfo(primaryBase).Symbol is IMethodSymbol baseCtor)
        {
            _w.Write($"{TypeRef(baseCtor.ContainingType)}.{ExternalAwareCtorName(baseCtor)}.call(this");
            if (primaryBase.ArgumentList.Arguments.Count > 0) { _w.Write(", "); EmitArguments(primaryBase.ArgumentList, baseCtor); }
            _w.WriteLine(");");
            return;
        }
        EmitImplicitBaseCall(type);
    }

    private void EmitImplicitBaseCall(INamedTypeSymbol type)
    {
        var baseType = type.BaseType;
        if (baseType is not null && baseType.SpecialType != SpecialType.System_Object
            && !IsValueTypeBase(baseType) && baseType.TypeKind != TypeKind.Error)
        {
            var baseCtor = baseType.InstanceConstructors.FirstOrDefault(c => c.Parameters.Length == 0);
            var name = baseCtor is not null ? ExternalAwareCtorName(baseCtor) : "ctor";
            _w.WriteLine($"{TypeRef(baseType)}.{name}.call(this);");
        }
    }

    /// <param name="runInitializers">
    /// False for a struct's <i>implicit</i> parameterless constructor, which zeroes the value rather
    /// than running the declared <c>= value</c> initializers: C# only runs those from a constructor the
    /// type declares, so <c>new S().Y</c> is 0 for <c>struct S { public int Y = 1; … }</c>. Running them
    /// here reported 1 instead. The struct-typed slots are still defaulted either way — the field slot
    /// is emitted as <c>null</c> for order-independence, so <c>default(T)</c> has to be assigned here.
    /// </param>
    private void EmitInstanceFieldInitializers(INamedTypeSymbol type, bool runInitializers = true)
    {
        foreach (var m in type.GetMembers())
        {
            if (m.IsStatic) continue;
            // This selection must stay in lockstep with InstanceFieldSlots: a member with no slot
            // has nothing to initialize, and assigning to it anyway writes through whatever the
            // runtime DID install at that name. An ABSTRACT auto-property is the case that bit —
            // `public abstract long Length { get; }` (System.IO.Stream) becomes a real getter, so
            // `this.Length = …` throws "Cannot set property Length of #<ctor> which has only a
            // getter". An indexer, and a field with no referenceable name, are the same shape.
            ITypeSymbol? slotType = m switch
            {
                IFieldSymbol f when !f.IsConst && f.AssociatedSymbol is null && f.CanBeReferencedByName => f.Type,
                IPropertySymbol p when (IsAutoProperty(p) && !p.IsAbstract && !p.IsIndexer)
                                       || IsFieldBackedProperty(p) => p.Type,
                _ => null,
            };
            if (slotType is null) continue;

            ExpressionSyntax? init = m switch
            {
                IFieldSymbol f => FieldInitializerSyntax(f),
                IPropertySymbol p => AutoPropertyInitializerSyntax(p),
                _ => null,
            };

            // A field-backed property's initializer writes its BACKING slot, never `this.P` — going
            // through the setter would dispatch to a derived override and initialize the wrong storage.
            var slot = m is IPropertySymbol fbp && IsFieldBackedProperty(fbp)
                ? PropertyBackingName(fbp)
                : TransposeNaming.MemberJsName(m);

            // An [ObjectLiteral] type's slots live in a plain JS object, so a long/ulong one holds a
            // plain number rather than a System.Int64 instance (Emitter.Foreign64.cs).
            var foreignSlot = IsForeignJsSlot(m) && Is64BitInteger(UnwrapNullable(slotType));

            if (init is not null && runInitializers)
            {
                _w.Write($"this.{slot} = ");
                EmitExpressionConverted(init, slotType, foreignSlot);
                _w.WriteLine(";");
            }
            else if (foreignSlot && !IsNullableValueType(slotType))
            {
                _w.WriteLine($"this.{slot} = 0;");
            }
            else if (NeedsStructDefaultInit(slotType))
            {
                // A non-nullable struct field/auto-property with no initializer defaults to the
                // zeroed struct (C# default(T)), not null. The field slot is emitted as `null`
                // (order-independence at define time), so assign the real default here in the ctor,
                // when the struct type is defined — otherwise e.g. an uninitialized DateTime is null
                // and `.Equals`/`.UtcDateTime` throws (reading getTime of null).
                _w.WriteLine($"this.{slot} = Transpose.getDefaultValue({TypeRef(slotType)});");
            }
        }
    }

    /// <summary>A slot whose C# <c>default(T)</c> is a runtime OBJECT rather than null or a JS
    /// number: DateTime, Guid, a user struct, ValueTuple — and long/ulong/decimal, whose zero is a
    /// System.Int64/UInt64/Decimal instance. Excludes the primitives that really are a JS number or
    /// boolean (already a literal slot default), enums (numeric slot default), Nullable&lt;T&gt;
    /// (null is correct), and type parameters (their default defers to the runtime at
    /// construction).</summary>
    private static bool NeedsStructDefaultInit(ITypeSymbol type)
        => type.TypeKind == TypeKind.Struct
           && (!IsPrimitiveNumericOrBool(type) || IsRuntimeObjectNumeric(type))
           && type is not INamedTypeSymbol { ConstructedFrom.SpecialType: SpecialType.System_Nullable_T };

    // ---- methods -----------------------------------------------------------

    // Static so DuplicateJsNameScanner can ask the same question the emitter does — the scanner must
    // flag exactly the members that end up as keys of one object literal, no more.
    internal static bool IsEmittableMethod(IMethodSymbol m)
        => m.MethodKind is MethodKind.Ordinary or MethodKind.UserDefinedOperator or MethodKind.Conversion
               or MethodKind.ExplicitInterfaceImplementation
           && !m.IsAbstract
           && m.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is BaseMethodDeclarationSyntax d
           // A body, an expression body, or a [Script] that supplies a hand-written JS body — the
           // last lets an `extern` (body-less) method be emitted with raw JavaScript.
           && (d.Body is not null || d.ExpressionBody is not null || TransposeNaming.GetScriptBody(m) is not null);

    private bool IsEntryPoint(IMethodSymbol m)
        => SymbolEqualityComparer.Default.Equals(m, _compilation.GetEntryPoint(System.Threading.CancellationToken.None));

    private void EmitInstanceMethods(INamedTypeSymbol type, IMethodSymbol? entryPoint)
    {
        var entries = new List<Action>();

        foreach (var m in type.GetMembers().OfType<IMethodSymbol>().Where(m => !m.IsStatic && IsEmittableMethod(m)))
        {
            var method = m;
            entries.Add(() => EmitMethodEntry(method));
        }
        foreach (var indexer in type.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsStatic && p.IsIndexer && !p.IsAbstract))
        {
            var idx = indexer;
            if (idx.GetMethod is not null) entries.Add(() => EmitAccessorEntry(TransposeNaming.IndexerAccessorName(idx, isGet: true), idx.GetMethod!, true));
            if (idx.SetMethod is not null) entries.Add(() => EmitAccessorEntry(TransposeNaming.IndexerAccessorName(idx, isGet: false), idx.SetMethod!, false));
        }

        AddValueTypeMethodEntries(type, entries);

        if (entries.Count == 0) return;

        _w.Block(() =>
        {
            for (var i = 0; i < entries.Count; i++)
            {
                entries[i]();
                _w.WriteLine(i < entries.Count - 1 ? "," : "");
            }
        });
    }

    private void EmitAccessorEntry(string name, IMethodSymbol accessor, bool getter)
    {
        _w.Write($"{NameMangler.JsPropertyKey(name)}: function (");
        for (var p = 0; p < accessor.Parameters.Length; p++)
        {
            if (p > 0) _w.Write(", ");
            _w.Write(NameMangler.JsIdentifier(accessor.Parameters[p].Name));
        }
        _w.Write(") ");
        EmitAccessorBody(accessor, getter);
    }

    private void EmitMethodEntry(IMethodSymbol m)
    {
        var decl = (BaseMethodDeclarationSyntax)m.DeclaringSyntaxReferences[0].GetSyntax();
        _w.Write($"{NameMangler.JsPropertyKey(TransposeNaming.MemberJsName(m))}: function (");
        EmitParameterList(m);
        _w.Write(") ");
        if (decl.Body is not null && IsIteratorBody(decl.Body))
            EmitIteratorBody(decl.Body, m);
        else
            EmitMethodBody(decl.Body, decl.ExpressionBody, m.ReturnsVoid, m);
    }

    private void EmitMethodMap(List<IMethodSymbol> methods, string ownerRef)
    {
        _w.Block(() =>
        {
            for (var i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                var decl = (BaseMethodDeclarationSyntax)m.DeclaringSyntaxReferences[0].GetSyntax();
                _w.Write($"{NameMangler.JsPropertyKey(TransposeNaming.MemberJsName(m))}: function (");
                EmitParameterList(m);
                _w.Write(") ");
                if (decl.Body is not null && IsIteratorBody(decl.Body))
                    EmitIteratorBody(decl.Body, m);
                else
                    EmitMethodBody(decl.Body, decl.ExpressionBody, m.ReturnsVoid, m);
                _w.WriteLine(i < methods.Count - 1 ? "," : "");
            }
        });
    }

    private void EmitIteratorBody(BlockSyntax body, IMethodSymbol method)
    {
        _w.Block(() =>
        {
            EmitOptionalDefaults(method);
            EmitIteratorGenerator(body.Statements, method.ReturnType);
        });
    }

    /// <summary>
    /// Emits the generator an iterator body compiles to. C# lets an iterator be declared as either the
    /// sequence (<c>IEnumerable</c>/<c>IEnumerable&lt;T&gt;</c>) or the cursor over it
    /// (<c>IEnumerator</c>/<c>IEnumerator&lt;T&gt;</c>), and those are two different JavaScript objects:
    /// the first answers <c>GetEnumerator()</c>, the second answers <c>moveNext()</c>/<c>current</c>.
    /// Emitting the enumerable for both is what made <c>foreach</c> over a collection whose
    /// <c>GetEnumerator()</c> is itself an iterator method fail with "e.MoveNext is not a function" —
    /// the caller received a sequence where the language contract promised a cursor.
    /// </summary>
    private void EmitIteratorGenerator(SyntaxList<StatementSyntax> statements, ITypeSymbol? returnType)
    {
        var helper = IsEnumeratorType(returnType) ? "TransposeR.iterEnumerator" : "TransposeR.iter";
        // A generator function can't be an arrow, so it rebinds `this`; bind it to the
        // enclosing instance so an iterator body that reads `this.field` still works.
        _w.Write($"return {helper}((function* () ");
        _w.Block(() => EmitStatements(statements));
        _w.WriteLine(").bind(this));");
    }

    /// <summary>True for <c>System.Collections.IEnumerator</c> and <c>System.Collections.Generic.IEnumerator&lt;T&gt;</c>.</summary>
    private static bool IsEnumeratorType(ITypeSymbol? type) =>
        type is INamedTypeSymbol named
        && named.OriginalDefinition.MetadataName is "IEnumerator" or "IEnumerator`1"
        && named.ContainingNamespace?.ToDisplayString() is "System.Collections" or "System.Collections.Generic";

    private static bool IsIteratorBody(SyntaxNode body)
    {
        foreach (var node in body.DescendantNodes(descendIntoChildren: n =>
                     n is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax))
        {
            if (node is YieldStatementSyntax) return true;
        }
        return false;
    }

    private void EmitEntryPoint(IMethodSymbol entry)
    {
        var decl = (BaseMethodDeclarationSyntax)entry.DeclaringSyntaxReferences[0].GetSyntax();
        var isAsync = entry.IsAsync || IsTaskType(entry.ReturnType);
        _w.Write("main: function Main () ");

        var moduleInits = ModuleInitializerMethods();
        if (moduleInits.Count == 0)
        {
            EmitMethodBody(decl.Body, decl.ExpressionBody, entry.ReturnsVoid, entry);
            return;
        }

        // Run [ModuleInitializer] methods before the entry point body (they execute at
        // module load in .NET; here we sequence them just ahead of Main).
        _w.Block(() =>
        {
            foreach (var mi in moduleInits)
                _w.WriteLine($"{TypeRef(mi.ContainingType)}.{TransposeNaming.MemberJsName(mi)}();");
            EmitOptionalDefaults(entry);
            EmitMaybeAsyncBody(isAsync, () =>
            {
                if (decl.Body is not null) EmitStatements(decl.Body.Statements);
                else if (decl.ExpressionBody is not null)
                {
                    if (entry.ReturnsVoid) EmitExpressionStatement(decl.ExpressionBody.Expression);
                    else { _w.Write("return "); EmitExpressionConverted(decl.ExpressionBody.Expression, entry.ReturnType); _w.WriteLine(";"); }
                }
            });
        });
    }

    private List<IMethodSymbol> ModuleInitializerMethods()
        => CollectTypes()
            .SelectMany(t => t.GetMembers().OfType<IMethodSymbol>())
            .Where(m => m.IsStatic && m.GetAttributes().Any(a =>
                TransposeNaming.AttrIs(a, "System.Runtime.CompilerServices.ModuleInitializerAttribute")))
            .ToList();

    private void EmitParameterList(IMethodSymbol method)
    {
        var first = true;
        // A generic method that threads its type arguments receives them as leading
        // parameters (T, args…), so the body can use typeof(T)/default(T)/new T().
        if (ThreadsTypeArgs(method))
        {
            foreach (var tp in method.TypeParameters)
            {
                if (!first) _w.Write(", ");
                _w.Write(tp.Name);
                first = false;
            }
        }
        foreach (var p in method.Parameters)
        {
            if (!first) _w.Write(", ");
            _w.Write(NameMangler.JsIdentifier(p.Name));
            first = false;
        }
    }

    /// <summary>
    /// True if a generic method threads its type arguments at runtime: a source-defined
    /// generic method not marked [IgnoreGeneric]. Its definition takes the type parameters
    /// as leading arguments and every call site passes the concrete type arguments, so
    /// runtime uses of the type parameter (typeof(T), default(T), new T()) resolve.
    /// </summary>
    private static bool ThreadsTypeArgs(IMethodSymbol method)
    {
        // A generic method threads its type arguments as leading JS parameters when its Transpose-emitted
        // definition takes them — so the call site passes exactly what the definition expects.
        if (!method.IsGenericMethod) return false;
        var def = method.OriginalDefinition;
        if (def.GetAttributes().Any(a => TransposeNaming.AttrIs(a, "Transpose.IgnoreGenericAttribute"))) return false;
        // A templated method's call shape is the template itself — no separate leading type args.
        if (TransposeNaming.GetTemplate(def) is not null) return false;
        // Source / referenced-library generic methods always thread.
        if (TransposeNaming.IsTransposeCompiledSource(method.ContainingType)) return true;
        // Otherwise the containing type is a Transpose runtime/binding assembly (classified so by name).
        // Its EXTERNAL types — Transpose.Core's [assembly:External] DOM bindings, whose members like
        // Node.appendChild<T> are native JS and use their native call form — must NOT thread. A
        // non-external type that carries a real method body threads its type args as leading JS
        // parameters: the base "Transpose" BCL's generic methods (e.g. CollectionExtensions.TryAdd) and
        // a Transpose.*-named library with genuine implementation (e.g. Transpose.Plotly's
        // Bindings.flatten2DArrayIf1D<T>, which IsTransposeCompiledSource treats as runtime purely by its
        // Transpose.* assembly name). A body-less extern (hand-written JS) uses its native form.
        if (TransposeNaming.IsExternalType(method.ContainingType)) return false;
        return !TransposeNaming.HasNoBody(def);
    }

    private void EmitOptionalDefaults(IMethodSymbol method)
    {
        foreach (var p in method.Parameters)
        {
            var name = NameMangler.JsIdentifier(p.Name);
            if (p.HasExplicitDefaultValue)
            {
                _w.Write($"if ({name} === undefined) {{ {name} = ");
                _w.Write(ConstantLiteral(p.ExplicitDefaultValue, p.Type));
                _w.WriteLine("; }");
            }
            else if (p.IsParams)
            {
                // A params array invoked with no trailing arguments arrives as undefined at the JS
                // boundary (e.g. a reflection/JS caller, or an ExpandParams spread with none); default
                // it to an empty array so the body's enumeration/indexing behaves, matching Transpose.
                _w.WriteLine($"if ({name} === undefined) {{ {name} = []; }}");
            }
        }
    }

    private void EmitMethodBody(BlockSyntax? block, ArrowExpressionClauseSyntax? arrow, bool returnsVoid, IMethodSymbol method)
    {
        // [Script(...)] supplies a hand-written JS body that replaces the C# body entirely.
        if (TransposeNaming.GetScriptBody(method) is { } scriptLines)
        {
            _w.Block(() => { foreach (var line in scriptLines) _w.WriteLine(line); });
            return;
        }

        _w.Block(() =>
        {
            EmitOptionalDefaults(method);
            EmitMaybeAsyncBody(method.IsAsync, () =>
            {
                if (block is not null)
                {
                    EmitStatements(block.Statements);
                }
                else if (arrow is not null)
                {
                    // Hoist out-var / is-pattern variables the expression introduces (e.g.
                    // `=> TryParse(s, out var n) && n > 0`) so their write-backs and later reads
                    // resolve — an expression body has no statement to predeclare them otherwise.
                    PredeclareInlineVars(arrow.Expression);
                    if (returnsVoid) EmitExpressionStatement(arrow.Expression);
                    else { _w.Write("return "); EmitExpressionConverted(arrow.Expression, method.ReturnType); _w.WriteLine(";"); }
                }
            });
        });
    }

    /// <summary>
    /// Emits statements directly, or — for an async member — inside a native `async` IIFE
    /// whose promise is adapted to an tps.js Task via TransposeR.fromPromise. This gives async
    /// methods the same contract as tps.js's own state-machine output: they return a Task
    /// that composes with Task.Run/WhenAll/ContinueWith and carries faults through the Task.
    /// </summary>
    private void EmitMaybeAsyncBody(bool isAsync, Action emitStatements)
    {
        if (!isAsync) { emitStatements(); return; }
        _w.Write("return TransposeR.fromPromise((async () => ");
        _w.Block(emitStatements);
        _w.WriteLine(")());");
    }

    // ---- properties (with logic) -------------------------------------------

    private void EmitInstanceProperties(INamedTypeSymbol type)
    {
        var props = type.GetMembers().OfType<IPropertySymbol>()
            // A field-backed property needs real accessors even when its accessors are auto — that is
            // what makes a virtual auto-property dispatch to the most-derived declaration instead of
            // every level sharing one slot.
            .Where(p => !p.IsStatic && !p.IsAbstract && !p.IsIndexer
                        && (!IsAutoProperty(p) || IsFieldBackedProperty(p))
                        && !IsExternProperty(p)
                        && !p.IsImplicitlyDeclared
                        && p.DeclaringSyntaxReferences.Length > 0
                        && p.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is PropertyDeclarationSyntax)
            .ToList();
        // Indexers → get_Item/set_Item under methods handled separately (skip for now).
        if (props.Count == 0) return;
        EmitPropertyMap(props);
    }

    private void EmitPropertyMap(List<IPropertySymbol> props)
    {
        _w.Block(() =>
        {
            for (var i = 0; i < props.Count; i++)
            {
                var p = props[i];
                _w.Write($"{NameMangler.JsPropertyKey(TransposeNaming.MemberJsName(p))}: ");
                _w.Block(() =>
                {
                    if (p.GetMethod is not null)
                    {
                        _w.Write("get: function () ");
                        EmitAccessorBody(p.GetMethod, isGetter: true);
                        _w.WriteLine(p.SetMethod is not null ? "," : "");
                    }
                    if (p.SetMethod is not null)
                    {
                        _w.Write("set: function (value) ");
                        EmitAccessorBody(p.SetMethod, isGetter: false);
                        _w.WriteLine();
                    }
                });
                _w.WriteLine(i < props.Count - 1 ? "," : "");
            }
        });
    }

    /// <summary>
    /// A property that needs a compiler-synthesized backing field rather than being emitted AS its
    /// slot: it mixes an auto accessor with a bodied one, an accessor uses the C# 14 `field` keyword,
    /// or it is a <b>virtual or overriding</b> auto-property.
    ///
    /// The last case is what .NET does: each declaration of a virtual auto-property has its own
    /// backing field, and the base's is reachable only through <c>base.P</c> — every other read goes
    /// through the virtual getter. Emitting both as one plain slot named after the property collapsed
    /// them into a single storage location, so with an initializer at each level the base
    /// constructor's write landed on the derived value (`new D().P` read 1, not 2). Real accessors over
    /// per-declaration slots restore both the storage and the dispatch.
    /// </summary>
    internal static bool IsFieldBackedProperty(IPropertySymbol p)
    {
        if (p.IsStatic || p.IsIndexer || p.IsAbstract) return false;
        if (p.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not PropertyDeclarationSyntax { AccessorList: { } accessors })
            return false;
        var anyAuto = accessors.Accessors.Any(a => a.Body is null && a.ExpressionBody is null);
        var anyBodied = accessors.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null);
        if (anyAuto && anyBodied) return true;
        if (anyAuto && !anyBodied && (p.IsVirtual || p.IsOverride)) return true;
        return accessors.Accessors.Any(a => a.DescendantNodes().Any(n => n.IsKind(SyntaxKind.FieldExpression)));
    }

    /// <summary>The slot a field-backed property stores into. An OVERRIDE carries its declaring type,
    /// because .NET gives it storage of its own: sharing the base's slot is exactly what made the base
    /// constructor's initializer overwrite the override's value.</summary>
    internal static string PropertyBackingName(IPropertySymbol p)
        => "$" + TransposeNaming.MemberJsName(p)
           + (p.IsOverride ? "$" + TransposeNaming.MangledTypeName(p.ContainingType) : "");

    private void EmitAccessorBody(IMethodSymbol accessor, bool isGetter)
    {
        // [Script(...)] on the accessor (or its property) supplies a raw JS body.
        var accessorScript = TransposeNaming.GetScriptBody(accessor)
            ?? (accessor.AssociatedSymbol is { } assoc ? TransposeNaming.GetScriptBody(assoc) : null);
        if (accessorScript is { } scriptLines)
        {
            _w.Block(() => { foreach (var line in scriptLines) _w.WriteLine(line); });
            return;
        }

        // Field-backed property with an auto accessor → read/write the backing field.
        if (accessor.AssociatedSymbol is IPropertySymbol prop && IsFieldBackedProperty(prop))
        {
            var syn = accessor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            var isAuto = syn is AccessorDeclarationSyntax { Body: null, ExpressionBody: null };
            if (isAuto)
            {
                var backing = PropertyBackingName(prop);
                _w.Block(() => _w.WriteLine(isGetter ? $"return this.{backing};" : $"this.{backing} = value;"));
                return;
            }
        }

        var syntax = accessor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        switch (syntax)
        {
            case AccessorDeclarationSyntax { Body: { } body }:
                if (IsIteratorBody(body)) EmitIteratorBody(body, accessor);
                else _w.Block(() => EmitStatements(body.Statements));
                break;
            case AccessorDeclarationSyntax { ExpressionBody: { } arrow }:
            case ArrowExpressionClauseSyntax arrow2 when (arrow2 = (ArrowExpressionClauseSyntax)syntax) != null:
                var arrowExpr = (syntax as AccessorDeclarationSyntax)?.ExpressionBody?.Expression
                                ?? ((ArrowExpressionClauseSyntax)syntax).Expression;
                _w.Block(() =>
                {
                    // Hoist out-var / is-pattern variables the expression introduces (e.g.
                    // `=> _w is WebSocket ws && ws.readyState == ...`) so their write-backs and
                    // later reads resolve — an expression body has no statement to predeclare them.
                    PredeclareInlineVars(arrowExpr);
                    if (isGetter) { _w.Write("return "); EmitExpression(arrowExpr); _w.WriteLine(";"); }
                    else EmitExpressionStatement(arrowExpr);
                });
                break;
            case PropertyDeclarationSyntax { ExpressionBody: { } arrow3 }:
                _w.Block(() => { PredeclareInlineVars(arrow3.Expression); _w.Write("return "); EmitExpression(arrow3.Expression); _w.WriteLine(";"); });
                break;
            default:
                _w.Block(() => { });
                break;
        }
    }
}
