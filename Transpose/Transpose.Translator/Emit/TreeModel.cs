using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

/// <summary>
/// A semantic-model facade that always queries the correct per-syntax-tree
/// <see cref="SemanticModel"/> for the node being asked about. A Roslyn semantic model is
/// bound to a single syntax tree, but a <c>partial</c> type's members span several files
/// (trees); routing each query by the node's own tree keeps multi-file projects working.
///
/// One <see cref="TreeModel"/> is shared by every <see cref="Emitter"/> clone, i.e. across the whole
/// parallel per-type emit, and that sharing is the point: a Roslyn <see cref="SemanticModel"/>
/// caches the bound form of each member it is asked about, so all the types declared in one file —
/// and every later query about the same member — reuse a single bind instead of paying for a fresh
/// one per type. A <see cref="SemanticModel"/> is safe for concurrent readers (Roslyn's IDE layer
/// relies on that), so the only thing needing care here is the tree→model map itself, hence the
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>.
///
/// The trade is memory: bound bodies stay reachable for the whole emit rather than being collected
/// per type. For the projects this compiler targets that is a few hundred MB at most, and it buys
/// back far more time than it costs.
/// </summary>
internal sealed class TreeModel
{
    private readonly Compilation _compilation;
    private readonly ConcurrentDictionary<SyntaxTree, SemanticModel> _cache = new();

    public TreeModel(Compilation compilation) => _compilation = compilation;

    private SemanticModel For(SyntaxNode node) => SemanticModelFor(node.SyntaxTree);

    /// <summary>The cached model for a whole tree — for callers that already know the tree (e.g. the
    /// per-file type collection) and so can skip going through a node.</summary>
    public SemanticModel SemanticModelFor(SyntaxTree tree)
        => _cache.GetOrAdd(tree, static (t, compilation) => compilation.GetSemanticModel(t), _compilation);

    public TypeInfo GetTypeInfo(SyntaxNode node) => For(node).GetTypeInfo(node);
    public SymbolInfo GetSymbolInfo(SyntaxNode node) => For(node).GetSymbolInfo(node);
    public ISymbol? GetDeclaredSymbol(SyntaxNode node) => For(node).GetDeclaredSymbol(node);
    public Optional<object?> GetConstantValue(SyntaxNode node) => For(node).GetConstantValue(node);

    /// <summary>The conversion C# applies to an expression at its usage site — notably whether it is
    /// a boxing conversion, which <see cref="GetTypeInfo"/> only reports as "converted to object".</summary>
    public Conversion GetConversion(ExpressionSyntax node) => For(node).GetConversion(node);

    public ForEachStatementInfo GetForEachStatementInfo(CommonForEachStatementSyntax node)
        => For(node).GetForEachStatementInfo(node);

    public SymbolInfo GetCollectionInitializerSymbolInfo(ExpressionSyntax node)
        => For(node).GetCollectionInitializerSymbolInfo(node);

    /// <summary>The awaiter C# resolved for an <c>await</c> — which is how a [Template]'d GetAwaiter
    /// (notably the extension that adapts an IPromise) is found at the await site.</summary>
    public AwaitExpressionInfo GetAwaitExpressionInfo(AwaitExpressionSyntax node)
        => For(node).GetAwaitExpressionInfo(node);

    /// <summary>The enclosing symbol at a node's position (the node identifies the tree).</summary>
    public ISymbol? GetEnclosingSymbol(SyntaxNode node)
        => For(node).GetEnclosingSymbol(node.SpanStart);
}
