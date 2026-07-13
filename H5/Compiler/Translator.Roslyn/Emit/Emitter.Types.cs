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
                // Interfaces are structural at runtime; nothing to emit for now.
                break;
            case TypeKind.Class:
            case TypeKind.Struct:
                EmitClass(type);
                break;
            case TypeKind.Delegate:
                // Delegates map onto plain JS functions; no type object needed.
                break;
            default:
                Unsupported(type.DeclaringSyntaxReferences[0].GetSyntax(), $"type kind {type.TypeKind}");
                break;
        }
    }

    private void EmitEnum(INamedTypeSymbol type)
    {
        var fullName = _names.TypeFullName(type);
        _w.Write($"H5R.define(\"{fullName}\", function () ");
        _w.Block(() =>
        {
            var simpleName = fullName.Split('.').Last();
            _w.WriteLine($"var {simpleName} = {{}};");
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue))
            {
                _w.WriteLine($"{simpleName}.{NameMangler.JsIdentifier(field.Name)} = {Convert.ToInt64(field.ConstantValue)};");
            }
            // Reverse map for ToString support.
            _w.WriteLine($"{simpleName}.$names = {{}};");
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue))
            {
                _w.WriteLine($"{simpleName}.$names[{Convert.ToInt64(field.ConstantValue)}] = \"{field.Name}\";");
            }
            _w.WriteLine($"return {simpleName};");
        });
        _w.WriteLine(");");
    }

    private void EmitClass(INamedTypeSymbol type)
    {
        var fullName = _names.TypeFullName(type);
        var simpleName = fullName.Split('.').Last();

        _w.Write($"H5R.define(\"{fullName}\", function () ");
        _w.Block(() =>
        {
            _w.WriteLine($"function {simpleName}() {{}}");

            var baseType = type.BaseType;
            if (baseType is not null && baseType.SpecialType != SpecialType.System_Object
                && baseType.TypeKind != TypeKind.Error && !IsValueTypeBase(baseType))
            {
                _w.WriteLine($"H5R.inherit({simpleName}, {_names.TypeFullName(baseType)});");
            }

            EmitInstanceFieldInit(type, simpleName);
            EmitStaticInit(type, simpleName);
            EmitMembers(type, simpleName);
            if (type.IsRecord) EmitRecordMembers(type, simpleName);

            _w.WriteLine($"return {simpleName};");
        });
        _w.WriteLine(");");
    }

    private static bool IsValueTypeBase(INamedTypeSymbol baseType)
        => baseType.SpecialType is SpecialType.System_ValueType or SpecialType.System_Enum;

    /// <summary>
    /// Emits the $ctorInit method which runs this type's instance field / auto-property
    /// initializers (mirroring C# field-initializer execution order).
    /// </summary>
    private void EmitInstanceFieldInit(INamedTypeSymbol type, string simpleName)
    {
        var fields = InstanceFieldInitializers(type).ToList();
        _w.Write($"{simpleName}.prototype.$ctorInit = function () ");
        _w.Block(() =>
        {
            foreach (var (target, initializer, defaultLiteral) in fields)
            {
                _w.Write($"this.{target} = ");
                if (initializer is not null)
                {
                    EmitExpression(initializer);
                }
                else
                {
                    _w.Write(defaultLiteral);
                }
                _w.WriteLine(";");
            }
        });
        _w.WriteLine(";");
    }

    private IEnumerable<(string target, ExpressionSyntax? initializer, string defaultLiteral)> InstanceFieldInitializers(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers())
        {
            if (member.IsStatic) continue;

            if (member is IFieldSymbol field && !field.IsConst && field.AssociatedSymbol is null)
            {
                yield return (_names.FieldName(field), FieldInitializerSyntax(field), DefaultValueLiteral(field.Type));
            }
            else if (member is IPropertySymbol { IsAbstract: false } prop && IsAutoProperty(prop))
            {
                yield return (_names.BackingFieldName(prop), AutoPropertyInitializerSyntax(prop), DefaultValueLiteral(prop.Type));
            }
        }
    }

    private void EmitStaticInit(INamedTypeSymbol type, string simpleName)
    {
        var staticFields = type.GetMembers()
            .Where(m => m.IsStatic)
            .Select(m => m switch
            {
                IFieldSymbol f when !f.IsConst && f.AssociatedSymbol is null => ((string target, ExpressionSyntax? init, string def)?)(_names.FieldName(f), FieldInitializerSyntax(f), DefaultValueLiteral(f.Type)),
                IPropertySymbol p when IsAutoProperty(p) => (_names.BackingFieldName(p), AutoPropertyInitializerSyntax(p), DefaultValueLiteral(p.Type)),
                _ => null,
            })
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .ToList();

        var staticCtor = type.StaticConstructors.FirstOrDefault();

        if (staticFields.Count == 0 && staticCtor is null) return;

        _w.Write($"{simpleName}.$cctor = function () ");
        _w.Block(() =>
        {
            foreach (var (target, init, def) in staticFields)
            {
                _w.Write($"{simpleName}.{target} = ");
                if (init is not null) EmitExpression(init); else _w.Write(def);
                _w.WriteLine(";");
            }

            if (staticCtor is { DeclaringSyntaxReferences.Length: > 0 }
                && staticCtor.DeclaringSyntaxReferences[0].GetSyntax() is ConstructorDeclarationSyntax { Body: { } body })
            {
                foreach (var stmt in body.Statements) EmitStatement(stmt);
            }
        });
        _w.WriteLine(";");
        _w.WriteLine($"{simpleName}.$cctor();");
    }

    // ---- syntax lookups ----------------------------------------------------

    private ExpressionSyntax? FieldInitializerSyntax(IFieldSymbol field)
    {
        foreach (var reference in field.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is VariableDeclaratorSyntax { Initializer: { } init })
            {
                return init.Value;
            }
        }
        return null;
    }

    private ExpressionSyntax? AutoPropertyInitializerSyntax(IPropertySymbol prop)
    {
        foreach (var reference in prop.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is PropertyDeclarationSyntax { Initializer: { } init })
            {
                return init.Value;
            }
        }
        return null;
    }

    private static bool IsAutoProperty(IPropertySymbol prop)
    {
        foreach (var reference in prop.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is PropertyDeclarationSyntax decl)
            {
                if (decl.ExpressionBody is not null) return false; // expression-bodied
                if (decl.AccessorList is null) return false;
                // auto-property: all accessors have no body
                return decl.AccessorList.Accessors.All(a => a.Body is null && a.ExpressionBody is null);
            }
        }
        return false;
    }

    /// <summary>Returns null; caller emits the JS default for the type.</summary>
    private ExpressionSyntax? DefaultInitializer(ITypeSymbol type) => null;

    private string DefaultValueLiteral(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum) return "0";
        // default(struct) is a zero-initialized instance, not null.
        if (IsSourceStruct(type)) return $"H5R.createDefault({_names.TypeReference(type)})";
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean: return "false";
            case SpecialType.System_Char: return "0";
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
