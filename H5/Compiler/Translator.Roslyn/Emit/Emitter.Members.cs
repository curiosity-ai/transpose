using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace H5.Translator.Roslyn;

public sealed partial class Emitter
{
    private void EmitMembers(INamedTypeSymbol type, string simpleName)
    {
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            switch (method.MethodKind)
            {
                case MethodKind.Ordinary:
                    EmitMethod(method, simpleName);
                    break;
                case MethodKind.Constructor:
                    EmitConstructor(method, simpleName);
                    break;
                // property/event accessors handled with their property; static ctor handled in $cctor
            }
        }

        var instanceCtors = type.InstanceConstructors
            .Where(c => c.DeclaringSyntaxReferences.Length > 0)
            .ToList();

        if (instanceCtors.Count == 0)
        {
            EmitDefaultConstructor(type, simpleName);
        }

        foreach (var prop in type.GetMembers().OfType<IPropertySymbol>())
        {
            EmitProperty(prop, simpleName);
        }
    }

    // ---- methods -----------------------------------------------------------

    private void EmitMethod(IMethodSymbol method, string simpleName)
    {
        if (method.IsAbstract) return; // no body
        var decl = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
        if (decl is null) return;
        if (decl.Body is null && decl.ExpressionBody is null) return; // partial/extern without body

        var name = _names.MethodName(method);
        var target = method.IsStatic ? $"{simpleName}.{name}" : $"{simpleName}.prototype.{name}";
        var asyncKw = method.IsAsync || IsTaskType(method.ReturnType) && decl.Modifiers.Any(SyntaxKind.AsyncKeyword) ? "async " : "";

        _w.Write($"{target} = {asyncKw}function (");
        EmitParameterList(method);
        _w.Write(") ");
        EmitMethodBody(decl.Body, decl.ExpressionBody, method.ReturnsVoid, method);
        _w.WriteLine(";");
    }

    private void EmitParameterList(IMethodSymbol method)
    {
        var ps = method.Parameters;
        for (var i = 0; i < ps.Length; i++)
        {
            if (i > 0) _w.Write(", ");
            _w.Write(NameMangler.JsIdentifier(ps[i].Name));
        }
    }

    private void EmitOptionalDefaults(IMethodSymbol method)
    {
        foreach (var p in method.Parameters.Where(p => p.HasExplicitDefaultValue))
        {
            var name = NameMangler.JsIdentifier(p.Name);
            _w.Write($"if ({name} === undefined) {{ {name} = ");
            _w.Write(ConstantLiteral(p.ExplicitDefaultValue, p.Type));
            _w.WriteLine("; }");
        }
    }

    private void EmitMethodBody(BlockSyntax? block, ArrowExpressionClauseSyntax? arrow, bool returnsVoid, IMethodSymbol method)
    {
        _w.Block(() =>
        {
            EmitOptionalDefaults(method);

            if (block is not null)
            {
                foreach (var stmt in block.Statements) EmitStatement(stmt);
            }
            else if (arrow is not null)
            {
                if (returnsVoid)
                {
                    EmitExpressionStatement(arrow.Expression);
                }
                else
                {
                    _w.Write("return ");
                    EmitExpression(arrow.Expression);
                    _w.WriteLine(";");
                }
            }
        });
    }

    // ---- constructors ------------------------------------------------------

    private void EmitConstructor(IMethodSymbol ctor, string simpleName)
    {
        var decl = ctor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as ConstructorDeclarationSyntax;
        if (decl is null) return;

        var name = _names.MethodName(ctor);
        _w.Write($"{simpleName}.prototype.{name} = function (");
        EmitParameterList(ctor);
        _w.Write(") ");
        _w.Block(() =>
        {
            EmitOptionalDefaults(ctor);
            EmitConstructorChain(ctor, decl, simpleName);

            if (decl.Body is not null)
            {
                foreach (var stmt in decl.Body.Statements) EmitStatement(stmt);
            }
            else if (decl.ExpressionBody is not null)
            {
                EmitExpressionStatement(decl.ExpressionBody.Expression);
            }
        });
        _w.WriteLine(";");
    }

    private void EmitConstructorChain(IMethodSymbol ctor, ConstructorDeclarationSyntax decl, string simpleName)
    {
        var initializer = decl.Initializer;

        if (initializer is { RawKind: (int)SyntaxKind.ThisConstructorInitializer })
        {
            // : this(...) — delegate to sibling ctor (which runs field init).
            if (_model.GetSymbolInfo(initializer).Symbol is IMethodSymbol target)
            {
                _w.Write($"this.{_names.MethodName(target)}(");
                EmitArgumentList(initializer.ArgumentList);
                _w.WriteLine(");");
            }
            return;
        }

        // Runs this type's field initializers first.
        _w.WriteLine("this.$ctorInit();");

        var baseType = ctor.ContainingType.BaseType;
        if (initializer is { RawKind: (int)SyntaxKind.BaseConstructorInitializer })
        {
            if (_model.GetSymbolInfo(initializer).Symbol is IMethodSymbol baseCtor)
            {
                _w.Write($"{_names.TypeFullName(baseCtor.ContainingType)}.prototype.{_names.MethodName(baseCtor)}.call(this");
                if (initializer.ArgumentList.Arguments.Count > 0)
                {
                    _w.Write(", ");
                    EmitArgumentList(initializer.ArgumentList);
                }
                _w.WriteLine(");");
            }
        }
        else if (baseType is not null && baseType.SpecialType != SpecialType.System_Object
                 && !IsValueTypeBase(baseType) && baseType.TypeKind != TypeKind.Error)
        {
            // Implicit base() — call base parameterless ctor.
            _w.WriteLine($"{_names.TypeFullName(baseType)}.prototype.$ctor.call(this);");
        }
    }

    private void EmitDefaultConstructor(INamedTypeSymbol type, string simpleName)
    {
        _w.Write($"{simpleName}.prototype.$ctor = function () ");
        _w.Block(() =>
        {
            _w.WriteLine("this.$ctorInit();");
            var baseType = type.BaseType;
            if (baseType is not null && baseType.SpecialType != SpecialType.System_Object
                && !IsValueTypeBase(baseType) && baseType.TypeKind != TypeKind.Error)
            {
                _w.WriteLine($"{_names.TypeFullName(baseType)}.prototype.$ctor.call(this);");
            }
        });
        _w.WriteLine(";");
    }

    // ---- properties --------------------------------------------------------

    private void EmitProperty(IPropertySymbol prop, string simpleName)
    {
        if (prop.IsAbstract) return;
        if (prop.IsIndexer) { EmitIndexer(prop, simpleName); return; }

        var target = prop.IsStatic ? simpleName : $"{simpleName}.prototype";
        var propName = _names.PropertyName(prop);
        var isAuto = IsAutoProperty(prop);
        var backing = prop.IsStatic ? $"{simpleName}.{_names.BackingFieldName(prop)}" : $"this.{_names.BackingFieldName(prop)}";

        _w.Write($"Object.defineProperty({target}, \"{propName}\", ");
        _w.Block(() =>
        {
            // getter
            if (prop.GetMethod is not null)
            {
                _w.Write("get: function () ");
                if (isAuto)
                {
                    _w.Block(() => _w.WriteLine($"return {backing};"));
                }
                else
                {
                    EmitAccessorBody(prop.GetMethod, isGetter: true);
                }
                _w.WriteLine(",");
            }
            // setter
            if (prop.SetMethod is not null)
            {
                _w.Write("set: function (value) ");
                if (isAuto)
                {
                    _w.Block(() => _w.WriteLine($"{backing} = value;"));
                }
                else
                {
                    EmitAccessorBody(prop.SetMethod, isGetter: false);
                }
                _w.WriteLine(",");
            }
            _w.WriteLine("enumerable: true, configurable: true");
        });
        _w.WriteLine(");");
    }

    private void EmitAccessorBody(IMethodSymbol accessor, bool isGetter)
    {
        var syntax = accessor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        switch (syntax)
        {
            case AccessorDeclarationSyntax { Body: { } body }:
                _w.Block(() => { foreach (var s in body.Statements) EmitStatement(s); });
                break;
            case AccessorDeclarationSyntax { ExpressionBody: { } arrow }:
                _w.Block(() =>
                {
                    if (isGetter) { _w.Write("return "); EmitExpression(arrow.Expression); _w.WriteLine(";"); }
                    else EmitExpressionStatement(arrow.Expression);
                });
                break;
            case PropertyDeclarationSyntax { ExpressionBody: { } arrow }:
                _w.Block(() => { _w.Write("return "); EmitExpression(arrow.Expression); _w.WriteLine(";"); });
                break;
            case ArrowExpressionClauseSyntax arrow:
                // Expression-bodied property/accessor: `Prop => expr;`
                _w.Block(() =>
                {
                    if (isGetter) { _w.Write("return "); EmitExpression(arrow.Expression); _w.WriteLine(";"); }
                    else EmitExpressionStatement(arrow.Expression);
                });
                break;
            default:
                _w.Block(() => { });
                break;
        }
    }

    private void EmitIndexer(IPropertySymbol prop, string simpleName)
    {
        // Indexers map to get_Item/set_Item methods.
        if (prop.GetMethod is not null)
        {
            _w.Write($"{simpleName}.prototype.get_Item = function (");
            EmitParameterList(prop.GetMethod);
            _w.Write(") ");
            EmitAccessorBody(prop.GetMethod, isGetter: true);
            _w.WriteLine(";");
        }
        if (prop.SetMethod is not null)
        {
            _w.Write($"{simpleName}.prototype.set_Item = function (");
            var ps = prop.SetMethod.Parameters;
            for (var i = 0; i < ps.Length; i++)
            {
                if (i > 0) _w.Write(", ");
                _w.Write(NameMangler.JsIdentifier(ps[i].Name));
            }
            _w.Write(") ");
            EmitAccessorBody(prop.SetMethod, isGetter: false);
            _w.WriteLine(";");
        }
    }
}
