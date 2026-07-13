using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace H5.Translator.Roslyn;

/// <summary>
/// Walks the Roslyn syntax tree (guided by the semantic model) and emits JavaScript.
/// </summary>
public sealed partial class Emitter
{
    private readonly CSharpCompilation _compilation;
    private readonly JsWriter _w = new();
    private readonly NameMangler _names = new();
    private SemanticModel _model = null!;

    public Emitter(CSharpCompilation compilation)
    {
        _compilation = compilation;
    }

    public string Emit()
    {
        _w.WriteLine("(function () {");
        _w.Indent();

        var types = CollectTypes();

        foreach (var type in types)
        {
            _model = _compilation.GetSemanticModel(type.DeclaringSyntaxReferences[0].SyntaxTree);
            EmitType(type);
            _w.WriteLine();
        }

        EmitBootstrap();

        _w.Outdent();
        _w.WriteLine("})();");
        return _w.ToString();
    }

    /// <summary>
    /// Collects all named types declared in source, ordered so that base types
    /// are emitted before their derived types.
    /// </summary>
    private List<INamedTypeSymbol> CollectTypes()
    {
        var declared = new List<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var tree in _compilation.SyntaxTrees)
        {
            var model = _compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(node) is INamedTypeSymbol sym && seen.Add(sym))
                {
                    declared.Add(sym);
                }
            }
        }

        // Interfaces first (classes reference them for $interfaces), then by
        // inheritance depth so base types precede derived types.
        return declared
            .OrderBy(t => t.TypeKind == TypeKind.Interface ? 0 : 1)
            .ThenBy(InheritanceDepth)
            .ToList();
    }

    private static int InheritanceDepth(INamedTypeSymbol type)
    {
        var depth = 0;
        for (var b = type.BaseType; b is not null; b = b.BaseType) depth++;
        return depth;
    }

    private void EmitBootstrap()
    {
        var entry = _compilation.GetEntryPoint(CancellationToken.None);
        if (entry is null) return;

        var typeName = _names.TypeFullName(entry.ContainingType);
        var methodName = _names.MethodName(entry);

        _w.WriteLine("// Entry point");
        var isAsync = entry.IsAsync || IsTaskType(entry.ReturnType);

        // Build the argument list Main expects (string[] args -> []).
        var arg = entry.Parameters.Length > 0 ? "[]" : "";

        if (isAsync)
        {
            _w.WriteLine($"Promise.resolve({typeName}.{methodName}({arg})).then(function () {{ H5R.flush(); }}).catch(function (e) {{ H5R.flush(); throw e; }});");
        }
        else
        {
            _w.WriteLine($"{typeName}.{methodName}({arg});");
            _w.WriteLine("H5R.flush();");
        }
    }

    // ---- helpers -----------------------------------------------------------

    private bool IsTaskType(ITypeSymbol type)
    {
        var name = type.OriginalDefinition.ToDisplayString();
        return name is "System.Threading.Tasks.Task" or "System.Threading.Tasks.Task<TResult>"
            or "System.Threading.Tasks.ValueTask" or "System.Threading.Tasks.ValueTask<TResult>";
    }

    private void Unsupported(SyntaxNode node, string what)
        => throw new TranslationException(
            $"Translation of this construct is not supported yet: {what}", node.GetLocation());
}
