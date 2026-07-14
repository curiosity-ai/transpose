using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace H5.Translator.Roslyn;

/// <summary>
/// Walks the Roslyn syntax tree (guided by the semantic model) and emits JavaScript
/// in the H5 runtime format (H5.assembly + H5.define), so the output runs against
/// the real h5.js / h5.core runtime.
/// </summary>
public sealed partial class Emitter
{
    private readonly CSharpCompilation _compilation;
    private JsWriter _w = new();
    private readonly NameMangler _names = new();
    private SemanticModel _model = null!;
    private readonly string _assemblyName;

    /// <summary>While emitting a primary constructor's own body, its parameters are the JS
    /// function parameters (raw names); elsewhere captured params read from the instance.</summary>
    private bool _inPrimaryCtorBody;

    /// <summary>
    /// Active goto label-dispatch contexts. When non-empty a statement body is being lowered
    /// into a `for(;;) switch($state)` machine: `goto L` sets the state and continues the loop.
    /// The top entry maps each label name to its case index and names the dispatch loop.
    /// </summary>
    private readonly Stack<(System.Collections.Generic.Dictionary<string, int> labels, string loopLabel, string stateVar)> _gotoContexts = new();

    public Emitter(CSharpCompilation compilation, string assemblyName = CompilationBuilder.DefaultAssemblyName)
    {
        _compilation = compilation;
        _assemblyName = assemblyName;
    }

    public string Emit()
    {
        _w.WriteLine("/**");
        _w.WriteLine(" * H5.Translator.Roslyn generated output.");
        _w.WriteLine(" */");
        _w.Write($"H5.assembly(\"{_assemblyName}\", function ($asm, globals) ");
        _w.Block(() =>
        {
            _w.WriteLine("\"use strict\";");
            _w.WriteLine();

            foreach (var type in CollectTypes())
            {
                _model = _compilation.GetSemanticModel(type.DeclaringSyntaxReferences[0].SyntaxTree);
                EmitType(type);
                _w.WriteLine();
            }
        });
        _w.WriteLine(");");
        return _w.ToString();
    }

    /// <summary>Runs <paramref name="emit"/> against a temporary writer and returns its text.</summary>
    private string Capture(Action emit)
    {
        var saved = _w;
        _w = new JsWriter();
        try
        {
            emit();
            return _w.ToString();
        }
        finally
        {
            _w = saved;
        }
    }

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
