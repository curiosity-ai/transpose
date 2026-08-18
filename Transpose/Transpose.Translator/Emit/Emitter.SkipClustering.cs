using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

/// <summary>
/// <c>[SkipTypeClustering]</c> — keeping a static facade out of the chunker's reference graph.
///
/// A chunk is a strongly-connected component of the graph <c>TypeRef</c> records, so a type whose
/// members construct half the library fuses that half into one chunk: the facade reaches every
/// component and every component reaches the facade, and the cycle makes them a single unit. That is
/// exactly what Tesserae's <c>UI</c> class did — 300 static factories, and every component calling
/// back into it for <c>Div</c>/<c>VStack</c>.
///
/// The observation this exploits: a static method body only runs when someone CALLS it. The facade's
/// own definition needs none of the types its members mention — only <c>inherits</c> and eager static
/// state have to exist when a type is defined. So the edges out of the facade can be moved to the
/// call sites, where the dependency is real:
///
///   before   Caller -> UI,  UI -> {Card, TextBlock, …}      (one SCC)
///   after    Caller -> UI,  Caller -> deps(UI.Card)          (a DAG)
///
/// The facade still becomes a chunk and its callers still import it; only the direction of the
/// component edges changes.
/// </summary>
public sealed partial class Emitter
{
    /// <summary>Per-member dependency sets for every <c>[SkipTypeClustering]</c> type in this
    /// compilation, built once before the parallel emit and only read during it.</summary>
    private IReadOnlyDictionary<ISymbol, HashSet<INamedTypeSymbol>>? _skipClusterDeps;

    internal static bool IsSkipClustered(INamedTypeSymbol? type) =>
        type is not null && TransposeNaming.HasAttr(type, TransposeNaming.SkipTypeClusteringAttr);

    /// <summary>
    /// Maps each member of each <c>[SkipTypeClustering]</c> type to the source types its body
    /// reaches. Deliberately a syntax walk over the member rather than a trial emission: it only has
    /// to be a superset of what the body will touch, and over-approximating costs a slightly larger
    /// import list, never a missing one.
    /// </summary>
    private Dictionary<ISymbol, HashSet<INamedTypeSymbol>> BuildSkipClusterDeps(
        IReadOnlyList<INamedTypeSymbol> types)
    {
        var result = new Dictionary<ISymbol, HashSet<INamedTypeSymbol>>(SymbolEqualityComparer.Default);

        foreach (var type in types)
        {
            if (!IsSkipClustered(type)) continue;

            foreach (var member in type.GetMembers())
            {
                if (member is not (IMethodSymbol or IPropertySymbol or IFieldSymbol)) continue;

                var deps = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                foreach (var syntaxRef in member.DeclaringSyntaxReferences)
                {
                    var node = syntaxRef.GetSyntax();
                    foreach (var descendant in node.DescendantNodesAndSelf())
                    {
                        Add(deps, _model.GetTypeInfo(descendant).Type);
                        var symbol = _model.GetSymbolInfo(descendant).Symbol;
                        // The *declaring* type of whatever is called or read: `new Card(...)`,
                        // `Card.Something`, an extension method's own class.
                        Add(deps, symbol?.ContainingType);
                        if (symbol is IMethodSymbol m) Add(deps, m.ReturnType);
                    }
                }
                deps.Remove(type);
                if (deps.Count > 0) result[member] = deps;
            }
        }
        return result;
    }

    private static void Add(HashSet<INamedTypeSymbol> into, ITypeSymbol? type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                Add(into, array.ElementType);
                return;
            case INamedTypeSymbol named:
                // Same filter TypeRef's RecordRef applies: only types this compilation emits.
                if (!TransposeNaming.IsExternalType(named) && TransposeNaming.AnyInSource(named.Locations))
                    into.Add((INamedTypeSymbol)named.OriginalDefinition);
                foreach (var arg in named.TypeArguments) Add(into, arg);
                return;
        }
    }

    /// <summary>
    /// At a call into a <c>[SkipTypeClustering]</c> type, records that member's dependencies against
    /// the type being emitted — the caller is where they are needed, and where the import has to go.
    /// A no-op outside module mode and for every other call.
    /// </summary>
    private void RecordSkipClusterCall(ISymbol? member)
    {
        if (member is null || !IsSkipClustered(member.ContainingType)) return;
        var key = member is IMethodSymbol { OriginalDefinition: { } original } ? original : member;

        // The facade is in this compilation: its member deps were computed from source.
        if (_recordedRefs is not null && _skipClusterDeps is not null
            && _skipClusterDeps.TryGetValue(key, out var deps))
        {
            foreach (var dep in deps) _recordedRefs.Add(dep);
            return;
        }

        // The facade came from a referenced package: it published the same sets keyed by
        // documentation-comment id, which is the one name both compilations agree on.
        if (_recordedExternalRefs is null || _externalSkipClusterDeps is null) return;
        var id = key.GetDocumentationCommentId();
        if (id is null || !_externalSkipClusterDeps.TryGetValue(id, out var names)) return;
        foreach (var name in names) _recordedExternalRefs.Add(name);
    }

    /// <summary>Documentation-comment id → emitted define names, merged from every referenced
    /// module-mode package that has a <c>[SkipTypeClustering]</c> facade.</summary>
    private IReadOnlyDictionary<string, List<string>>? _externalSkipClusterDeps;

    /// <summary>The reference set a type contributes to the graph. A skipped type contributes
    /// nothing: its members' dependencies were attributed to the callers instead, and keeping them
    /// here as well would re-form the very cycle the attribute exists to break.</summary>
    private static HashSet<INamedTypeSymbol> ClusterRefsFor(
        INamedTypeSymbol type, HashSet<INamedTypeSymbol> recorded) =>
        IsSkipClustered(type) ? new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) : recorded;
}

public sealed partial class Emitter
{
    /// <summary>
    /// The per-member dependency sets in the form a *consuming* build can use: documentation-comment
    /// id → emitted define names. A consumer has the facade only as metadata, so it can neither walk
    /// the member's body nor name its dependencies — but it can compute the same doc id for the
    /// symbol it is calling, and it already knows how to turn a define name into an import through
    /// the merged chunk map.
    /// </summary>
    private Dictionary<string, List<string>> PublishedSkipClusterDeps()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (_skipClusterDeps is null) return result;

        foreach (var (member, deps) in _skipClusterDeps)
        {
            var id = member.GetDocumentationCommentId();
            if (string.IsNullOrEmpty(id)) continue;
            var names = deps.Select(DefineName).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal)
                            .OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (names.Count > 0) result[id!] = names;
        }
        return result;
    }
}
