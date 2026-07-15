using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace H5.Translator.Roslyn;

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
            default:
                Unsupported(type.DeclaringSyntaxReferences[0].GetSyntax(), $"type kind {type.TypeKind}");
                break;
        }
    }

    /// <summary>Full JS name a type is registered / referenced under.</summary>
    private string TypeRef(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named)
        {
            var name = H5Naming.GetName(named);
            if (name is not null && !named.Locations.Any(l => l.IsInSource)) return name;

            if (named.Locations.Any(l => l.IsInSource))
            {
                // Constructed source generic → Name$arity(typeArgs); the type args are the
                // runtime values passed to the generic type's defining function.
                if (named.IsGenericType && named.TypeArguments.Length > 0)
                {
                    var gBase = _names.TypeFullName(named) + "$" + named.Arity;
                    var gArgs = string.Join(", ", named.TypeArguments.Select(TypeRef));
                    return $"{gBase}({gArgs})";
                }
                return _names.TypeFullName(named);
            }

            // External type living under a [Scope]/[GlobalMethods] binding (e.g. H5.Core.dom's
            // HTMLElement) → the ambient JS global, with the C# type/namespace path dropped.
            if (ScopedExternalName(named) is { } scoped) return scoped;

            // External BCL type — dotted metadata name; generics as Name$arity(typeArgs).
            var ns = named.ContainingNamespace?.ToDisplayString();
            var simple = named.MetadataName; // includes `arity
            var full = string.IsNullOrEmpty(ns) ? named.Name : ns + "." + named.Name;
            if (named.IsGenericType && named.TypeArguments.Length > 0)
            {
                var baseName = (string.IsNullOrEmpty(ns) ? "" : ns + ".") + StripArity(named.Name) + "$" + named.Arity;
                var args = string.Join(", ", named.TypeArguments.Select(TypeRef));
                return $"{baseName}({args})";
            }
            return full;
        }
        if (type is IArrayTypeSymbol) return "System.Array";
        return type.Name;
    }

    private static string StripArity(string name)
    {
        var i = name.IndexOf('`');
        return i >= 0 ? name.Substring(0, i) : name;
    }

    /// <summary>
    /// The JS name of an external type nested under a <c>[Scope]</c>/<c>[GlobalMethods]</c>
    /// binding: the scope prefix (empty for a global scope) plus the type names between the
    /// scope and this type — so <c>H5.Core.dom.HTMLElement</c> becomes <c>HTMLElement</c>.
    /// Null when no enclosing scope applies.
    /// </summary>
    /// <summary>
    /// A static member reference: for a <c>[Scope]</c>/<c>[GlobalMethods]</c> binding it is the
    /// bare (or scope-prefixed) member — <c>dom.window</c> → <c>window</c>, <c>dom.alert(…)</c>
    /// → <c>alert(…)</c> — otherwise the qualified <c>Type.member</c>.
    /// </summary>
    private string StaticMemberAccess(ISymbol member)
    {
        var name = H5Naming.MemberJsName(member);
        var prefix = H5Naming.ScopePrefix(member.ContainingType);
        if (prefix is null) return $"{TypeRef(member.ContainingType)}.{name}";
        return prefix.Length == 0 ? name : $"{prefix}.{name}";
    }

    private string? ScopedExternalName(INamedTypeSymbol named)
    {
        var names = new List<string>();
        for (INamedTypeSymbol? t = named; t is not null; t = t.ContainingType)
        {
            if (H5Naming.ScopePrefix(t) is { } prefix)
            {
                if (names.Count == 0) return null; // referencing the scope type itself — not a member
                var path = string.Join(".", names);
                return string.IsNullOrEmpty(prefix) ? path : prefix + "." + path;
            }
            names.Insert(0, H5Naming.GetName(t) ?? StripArity(t.Name));
        }
        return null;
    }

    private void EmitEnum(INamedTypeSymbol type)
    {
        _w.Write($"H5.define(\"{_names.TypeFullName(type)}\", ");
        var isFlags = type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.FlagsAttribute");
        _w.Block(() =>
        {
            _w.WriteLine("$kind: \"enum\",");
            if (isFlags) _w.WriteLine("$flags: true,");
            _w.Write("statics: ");
            _w.Block(() =>
            {
                _w.Write("fields: ");
                _w.Block(() =>
                {
                    var fields = type.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue).ToList();
                    for (var i = 0; i < fields.Count; i++)
                    {
                        _w.Write($"{NameMangler.JsPropertyKey(H5Naming.MemberJsName(fields[i]))}: {Convert.ToInt64(fields[i].ConstantValue)}");
                        _w.WriteLine(i < fields.Count - 1 ? "," : "");
                    }
                });
                _w.WriteLine();
            });
            _w.WriteLine();
        });
        _w.WriteLine(");");
    }

    private void EmitInterface(INamedTypeSymbol type)
    {
        // A generic interface is a function of its type parameters (like generic classes),
        // so references such as IContainer$1(T) resolve at runtime.
        var typeParams = type.TypeParameters;
        var isGeneric = typeParams.Length > 0;
        var fullName = isGeneric ? _names.TypeFullName(type) + "$" + type.Arity : _names.TypeFullName(type);

        _w.Write($"H5.define(\"{fullName}\", ");
        if (isGeneric) _w.Write($"function ({string.Join(", ", typeParams.Select(p => p.Name))}) {{ return ");
        _w.Block(() =>
        {
            _w.Write("$kind: \"interface\"");
            var bases = type.Interfaces.Where(i => i.Locations.Any(l => l.IsInSource)).ToList();
            if (bases.Count > 0)
            {
                _w.WriteLine(",");
                _w.WriteLine($"inherits: function () {{ return [{string.Join(", ", bases.Select(TypeRef))}]; }}");
            }
            else
            {
                _w.WriteLine();
            }
        });
        if (isGeneric) _w.Write("; }");
        _w.WriteLine(");");
    }

    private void EmitClassLike(INamedTypeSymbol type)
    {
        var entryPoint = _compilation.GetEntryPoint(System.Threading.CancellationToken.None);

        // A generic type is defined as a function of its type parameters, returning the
        // config object (H5.define("Name$N", function (T) { return { … }; })); the type
        // parameters are then in scope at runtime for new T()/default(T)/typeof(T).
        var typeParams = type.TypeParameters;
        var isGeneric = typeParams.Length > 0;
        var fullName = isGeneric ? _names.TypeFullName(type) + "$" + type.Arity : _names.TypeFullName(type);

        _w.Write($"H5.define(\"{fullName}\", ");
        if (isGeneric) _w.Write($"function ({string.Join(", ", typeParams.Select(p => p.Name))}) {{ return ");
        _w.Block(() =>
        {
            var sections = new List<Action>();

            // $kind for structs.
            if (type.TypeKind == TypeKind.Struct)
            {
                sections.Add(() => _w.Write("$kind: \"struct\""));
            }

            // inherits: base class + implemented (source) interfaces.
            var inherits = new List<string>();
            if (type.BaseType is { } bt && bt.SpecialType != SpecialType.System_Object
                && bt.TypeKind != TypeKind.Error && !IsValueTypeBase(bt))
            {
                inherits.Add(TypeRef(bt));
            }
            inherits.AddRange(type.Interfaces.Where(i => i.Locations.Any(l => l.IsInSource)).Select(TypeRef));
            if (inherits.Count > 0)
            {
                // Lazy inherits (a function, as the legacy compiler emits): the config object
                // is built before H5.define runs, so evaluating an eager array would resolve a
                // self/forward reference (e.g. class C : IFoo<C>) before the type is registered.
                sections.Add(() => _w.Write($"inherits: function () {{ return [{string.Join(", ", inherits)}]; }}"));
            }

            // alias: maps each implicitly-implemented interface member's plain slot to the
            // mangled interface slot, so access through the interface type resolves.
            var aliases = H5Naming.InterfaceAliasPairs(type);
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
                _w.Write($"{entries[i].name}: {entries[i].def}");
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
                yield return (H5Naming.MemberJsName(f), DefaultValueLiteral(f.Type), f);
            else if (m is IPropertySymbol p && !p.IsAbstract && !p.IsIndexer
                     && (IsAutoProperty(p) || (type.IsRecord && p.IsImplicitlyDeclared && p.Name != "EqualityContract")))
                yield return (H5Naming.MemberJsName(p), DefaultValueLiteral(p.Type), p);
            else if (m is IEventSymbol ev && IsFieldLikeEvent(ev))
                yield return (H5Naming.MemberJsName(ev), "null", ev);
            else if (m is IPropertySymbol fbp && IsFieldBackedProperty(fbp))
                yield return (PropertyBackingName(fbp), DefaultValueLiteral(fbp.Type), fbp);
        }
    }

    /// <summary>A field-like event (no explicit add/remove) — backed by a null delegate field.</summary>
    internal static bool IsFieldLikeEvent(IEventSymbol ev)
        => ev.AddMethod is null or { IsImplicitlyDeclared: true };

    // ---- shared helpers ----------------------------------------------------

    internal static bool IsAutoProperty(IPropertySymbol prop)
    {
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

    private string DefaultValueLiteral(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum) return "0";
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Struct } st && st.Locations.Any(l => l.IsInSource))
            return $"{TypeRef(st)}.getDefaultValue()";
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
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
                return "0";
            default:
                return "null";
        }
    }
}
