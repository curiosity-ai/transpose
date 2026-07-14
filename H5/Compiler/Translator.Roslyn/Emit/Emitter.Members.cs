using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace H5.Translator.Roslyn;

public sealed partial class Emitter
{
    // ---- statics -----------------------------------------------------------

    private void EmitStatics(INamedTypeSymbol type, string fullName)
    {
        var staticFields = type.GetMembers().Where(m => m.IsStatic).Select(m => m switch
        {
            IFieldSymbol f when !f.IsConst && f.AssociatedSymbol is null => ((string name, string def)?)(H5Naming.MemberJsName(f), DefaultValueLiteral(f.Type)),
            IPropertySymbol p when IsAutoProperty(p) => (H5Naming.MemberJsName(p), DefaultValueLiteral(p.Type)),
            _ => null,
        }).Where(x => x is not null).Select(x => x!.Value).ToList();

        var staticInitAssignments = StaticInitializers(type).ToList();
        var staticCtor = type.StaticConstructors.FirstOrDefault(c => c.DeclaringSyntaxReferences.Length > 0);
        var staticMethods = type.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.IsStatic && !m.IsImplicitlyDeclared && IsEmittableMethod(m) && !IsEntryPoint(m))
            .ToList();
        var staticProps = type.GetMembers().OfType<IPropertySymbol>()
            .Where(p => p.IsStatic && !p.IsAbstract && !IsAutoProperty(p) && !p.IsIndexer)
            .ToList();

        var sections = new List<Action>();

        if (staticFields.Count > 0)
        {
            sections.Add(() =>
            {
                _w.Write("fields: ");
                _w.Block(() =>
                {
                    for (var i = 0; i < staticFields.Count; i++)
                    {
                        _w.Write($"{staticFields[i].name}: {staticFields[i].def}");
                        _w.WriteLine(i < staticFields.Count - 1 ? "," : "");
                    }
                });
            });
        }

        if (staticInitAssignments.Count > 0 || staticCtor is not null)
        {
            sections.Add(() =>
            {
                _w.Write("ctors: ");
                _w.Block(() =>
                {
                    _w.Write("init: function () ");
                    _w.Block(() =>
                    {
                        foreach (var (target, init) in staticInitAssignments)
                        {
                            _w.Write($"{fullName}.{target} = ");
                            EmitExpression(init);
                            _w.WriteLine(";");
                        }
                        if (staticCtor?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is ConstructorDeclarationSyntax { Body: { } body })
                        {
                            foreach (var s in body.Statements) EmitStatement(s);
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

    private IEnumerable<(string target, ExpressionSyntax init)> StaticInitializers(INamedTypeSymbol type)
    {
        foreach (var m in type.GetMembers().Where(m => m.IsStatic))
        {
            if (m is IFieldSymbol f && !f.IsConst && f.AssociatedSymbol is null && FieldInitializerSyntax(f) is { } fi)
                yield return (H5Naming.MemberJsName(f), fi);
            else if (m is IPropertySymbol p && IsAutoProperty(p) && AutoPropertyInitializerSyntax(p) is { } pi)
                yield return (H5Naming.MemberJsName(p), pi);
        }
    }

    // ---- instance constructors ---------------------------------------------

    private readonly Dictionary<ISymbol, string> _ctorNames = new(SymbolEqualityComparer.Default);

    private string CtorName(IMethodSymbol ctor)
    {
        ctor = ctor.OriginalDefinition;
        if (_ctorNames.TryGetValue(ctor, out var cached)) return cached;

        var ctors = ctor.ContainingType.InstanceConstructors
            .OrderBy(c => c.Parameters.Length)
            .ThenBy(c => string.Join(",", c.Parameters.Select(p => p.Type.ToDisplayString())), StringComparer.Ordinal)
            .ToList();

        if (ctors.Count == 1)
        {
            _ctorNames[ctors[0].OriginalDefinition] = "ctor";
        }
        else
        {
            var primary = ctors.FirstOrDefault(c => c.Parameters.Length == 0) ?? ctors[0];
            var n = 1;
            foreach (var c in ctors)
            {
                _ctorNames[c.OriginalDefinition] = ReferenceEquals(c, primary) ? "ctor" : "$ctor" + n++;
            }
        }
        return _ctorNames.TryGetValue(ctor, out var name) ? name : "ctor";
    }

    /// <summary>Ctor name honouring that external (h5) types expose only "ctor".</summary>
    private string ExternalAwareCtorName(IMethodSymbol ctor)
        => ctor.ContainingType.Locations.Any(l => l.IsInSource) ? CtorName(ctor) : "ctor";

    private void EmitInstanceCtors(INamedTypeSymbol type)
    {
        var ctors = type.InstanceConstructors.Where(c => !c.IsImplicitlyDeclared && c.DeclaringSyntaxReferences.Length > 0).ToList();
        var hasExplicit = ctors.Count > 0;

        _w.Block(() =>
        {
            if (!hasExplicit)
            {
                // Synthesized default constructor.
                _w.Write("ctor: function () ");
                _w.Block(() =>
                {
                    _w.WriteLine("this.$initialize();");
                    EmitImplicitBaseCall(type);
                    EmitInstanceFieldInitializers(type);
                });
                _w.WriteLine();
                return;
            }

            var all = type.InstanceConstructors.Where(c => c.DeclaringSyntaxReferences.Length > 0).ToList();
            for (var i = 0; i < all.Count; i++)
            {
                var ctor = all[i];
                var decl = ctor.DeclaringSyntaxReferences[0].GetSyntax() as ConstructorDeclarationSyntax;
                _w.Write($"{CtorName(ctor)}: function (");
                EmitParameterList(ctor);
                _w.Write(") ");
                _w.Block(() =>
                {
                    EmitOptionalDefaults(ctor);
                    _w.WriteLine("this.$initialize();");
                    EmitConstructorChain(ctor, decl!, type);
                    if (decl?.Body is not null)
                        foreach (var s in decl.Body.Statements) EmitStatement(s);
                    else if (decl?.ExpressionBody is not null)
                        EmitExpressionStatement(decl.ExpressionBody.Expression);
                });
                _w.WriteLine(i < all.Count - 1 ? "," : "");
            }
        });
    }

    private void EmitConstructorChain(IMethodSymbol ctor, ConstructorDeclarationSyntax decl, INamedTypeSymbol type)
    {
        var initializer = decl?.Initializer;
        if (initializer is { RawKind: (int)SyntaxKind.ThisConstructorInitializer }
            && _model.GetSymbolInfo(initializer).Symbol is IMethodSymbol thisCtor)
        {
            _w.Write($"this.{CtorName(thisCtor)}(");
            EmitArguments(initializer.ArgumentList, thisCtor);
            _w.WriteLine(");");
            EmitInstanceFieldInitializers(type);
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

    private void EmitInstanceFieldInitializers(INamedTypeSymbol type)
    {
        foreach (var m in type.GetMembers())
        {
            if (m.IsStatic) continue;
            ExpressionSyntax? init = m switch
            {
                IFieldSymbol f when !f.IsConst && f.AssociatedSymbol is null => FieldInitializerSyntax(f),
                IPropertySymbol p when IsAutoProperty(p) => AutoPropertyInitializerSyntax(p),
                _ => null,
            };
            if (init is null) continue;
            _w.Write($"this.{H5Naming.MemberJsName(m)} = ");
            EmitExpression(init);
            _w.WriteLine(";");
        }
    }

    // ---- methods -----------------------------------------------------------

    private bool IsEmittableMethod(IMethodSymbol m)
        => m.MethodKind is MethodKind.Ordinary or MethodKind.UserDefinedOperator or MethodKind.Conversion
           && !m.IsAbstract
           && m.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is BaseMethodDeclarationSyntax d
           && (d.Body is not null || d.ExpressionBody is not null);

    private bool IsEntryPoint(IMethodSymbol m)
        => SymbolEqualityComparer.Default.Equals(m, _compilation.GetEntryPoint(System.Threading.CancellationToken.None));

    private void EmitInstanceMethods(INamedTypeSymbol type, IMethodSymbol? entryPoint)
    {
        var methods = type.GetMembers().OfType<IMethodSymbol>()
            .Where(m => !m.IsStatic && IsEmittableMethod(m))
            .ToList();
        var indexers = type.GetMembers().OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && p.IsIndexer && !p.IsAbstract)
            .ToList();
        if (methods.Count == 0 && indexers.Count == 0) return;

        _w.Block(() =>
        {
            for (var i = 0; i < methods.Count; i++)
            {
                EmitMethodEntry(methods[i]);
                _w.WriteLine(i < methods.Count - 1 || indexers.Count > 0 ? "," : "");
            }
            for (var k = 0; k < indexers.Count; k++)
            {
                EmitIndexerEntries(indexers[k], last: k == indexers.Count - 1);
            }
        });
    }

    private void EmitIndexerEntries(IPropertySymbol indexer, bool last)
    {
        var accessors = new List<(string name, IMethodSymbol accessor, bool getter)>();
        if (indexer.GetMethod is not null) accessors.Add(("getItem", indexer.GetMethod, true));
        if (indexer.SetMethod is not null) accessors.Add(("setItem", indexer.SetMethod, false));

        for (var i = 0; i < accessors.Count; i++)
        {
            var (name, accessor, getter) = accessors[i];
            _w.Write($"{name}: function (");
            for (var p = 0; p < accessor.Parameters.Length; p++)
            {
                if (p > 0) _w.Write(", ");
                _w.Write(NameMangler.JsIdentifier(accessor.Parameters[p].Name));
            }
            _w.Write(") ");
            EmitAccessorBody(accessor, getter);
            var isLastEntry = last && i == accessors.Count - 1;
            _w.WriteLine(isLastEntry ? "" : ",");
        }
    }

    private void EmitMethodEntry(IMethodSymbol m)
    {
        var decl = (BaseMethodDeclarationSyntax)m.DeclaringSyntaxReferences[0].GetSyntax();
        var asyncKw = m.IsAsync ? "async " : "";
        _w.Write($"{H5Naming.MemberJsName(m)}: {asyncKw}function (");
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
                var asyncKw = m.IsAsync ? "async " : "";
                _w.Write($"{H5Naming.MemberJsName(m)}: {asyncKw}function (");
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
            _w.Write("return H5R.iter(function* () ");
            _w.Block(() => { foreach (var s in body.Statements) EmitStatement(s); });
            _w.WriteLine(");");
        });
    }

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
        var asyncKw = entry.IsAsync || IsTaskType(entry.ReturnType) ? "async " : "";
        _w.Write($"main: {asyncKw}function Main () ");
        EmitMethodBody(decl.Body, decl.ExpressionBody, entry.ReturnsVoid, entry);
    }

    private void EmitParameterList(IMethodSymbol method)
    {
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            if (i > 0) _w.Write(", ");
            _w.Write(NameMangler.JsIdentifier(method.Parameters[i].Name));
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
                if (returnsVoid) EmitExpressionStatement(arrow.Expression);
                else { _w.Write("return "); EmitExpressionConverted(arrow.Expression, method.ReturnType); _w.WriteLine(";"); }
            }
        });
    }

    // ---- properties (with logic) -------------------------------------------

    private void EmitInstanceProperties(INamedTypeSymbol type)
    {
        var props = type.GetMembers().OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && !p.IsAbstract && !p.IsIndexer && !IsAutoProperty(p)
                        && p.DeclaringSyntaxReferences.Length > 0)
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
                _w.Write($"{H5Naming.MemberJsName(p)}: ");
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

    private void EmitAccessorBody(IMethodSymbol accessor, bool isGetter)
    {
        var syntax = accessor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        switch (syntax)
        {
            case AccessorDeclarationSyntax { Body: { } body }:
                _w.Block(() => { foreach (var s in body.Statements) EmitStatement(s); });
                break;
            case AccessorDeclarationSyntax { ExpressionBody: { } arrow }:
            case ArrowExpressionClauseSyntax arrow2 when (arrow2 = (ArrowExpressionClauseSyntax)syntax) != null:
                var arrowExpr = (syntax as AccessorDeclarationSyntax)?.ExpressionBody?.Expression
                                ?? ((ArrowExpressionClauseSyntax)syntax).Expression;
                _w.Block(() =>
                {
                    if (isGetter) { _w.Write("return "); EmitExpression(arrowExpr); _w.WriteLine(";"); }
                    else EmitExpressionStatement(arrowExpr);
                });
                break;
            case PropertyDeclarationSyntax { ExpressionBody: { } arrow3 }:
                _w.Block(() => { _w.Write("return "); EmitExpression(arrow3.Expression); _w.WriteLine(";"); });
                break;
            default:
                _w.Block(() => { });
                break;
        }
    }
}
