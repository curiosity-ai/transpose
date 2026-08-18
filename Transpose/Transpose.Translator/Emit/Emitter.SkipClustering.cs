using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

/// <summary>
/// <c>[SkipTypeClustering]</c> — keeping a static facade out of the chunker's reference graph.
///
/// A chunk is a strongly-connected component of the reference graph, so a type whose
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
///
/// Two things the naive form of that move gets wrong, both of which fail at runtime with
/// "<c>… lives in module './chunks/…', which has not been loaded</c>" rather than at build time:
///
/// <list type="number">
/// <item><b>A call to a sibling on the same facade.</b> The sibling's containing type IS the facade,
/// which is exactly the edge being dropped — so member <c>A</c> calling <c>B</c> contributed only
/// <c>B</c>'s return type, never the types <c>B</c>'s own body constructs. Nothing then imported
/// them: the facade's chunk doesn't (its edges are dropped) and <c>A</c>'s callers didn't know to.
/// <see cref="BuildSkipClusterDeps"/> therefore takes a <b>transitive closure over the facade's own
/// members</b> before attributing anything to a call site, so <c>deps(A) ⊇ deps(B)</c>.</item>
/// <item><b>Eager static state.</b> A static field initializer or a static constructor runs when the
/// facade's own chunk evaluates, not when someone calls into it — there is no call site to move that
/// edge to. <see cref="ClusterRefsFor"/> therefore keeps exactly those members' dependencies on the
/// facade itself, and drops only the ones that genuinely wait for a call.</item>
/// </list>
/// </summary>
public sealed partial class Emitter
{
    /// <summary>Per-member dependency sets for every <c>[SkipTypeClustering]</c> type in this
    /// compilation, built once before the parallel emit and only read during it.</summary>
    private IReadOnlyDictionary<ISymbol, HashSet<INamedTypeSymbol>>? _skipClusterDeps;

    /// <summary>For each <c>[SkipTypeClustering]</c> facade, the dependencies that must exist when
    /// the facade's own chunk evaluates: everything reached from a static field/property initializer
    /// or a static constructor, closed over sibling calls. <see cref="ClusterRefsFor"/> hands these
    /// back to the graph instead of dropping them.</summary>
    private IReadOnlyDictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>>? _skipClusterEagerDeps;

    internal static bool IsSkipClustered(INamedTypeSymbol? type) =>
        type is not null && TransposeNaming.HasAttr(type, TransposeNaming.SkipTypeClusteringAttr);

    /// <summary>
    /// Maps each member of each <c>[SkipTypeClustering]</c> type to the source types its body
    /// reaches, <b>including everything reached through the siblings it calls</b>. Deliberately a
    /// syntax walk over the member rather than a trial emission: it only has to be a superset of what
    /// the body will touch, and over-approximating costs a slightly larger import list, never a
    /// missing one.
    ///
    /// The sibling closure is the load-bearing half. A facade is written as a facade — members call
    /// each other constantly (<c>UI.Card</c> building on <c>UI.Div</c>, a route table calling the
    /// method that constructs the view) — and a reference to a sibling names only the facade, the one
    /// type this pass is removing from every set. Without the closure, a caller of <c>A</c> imports
    /// the facade's chunk and nothing else, while the type <c>B</c> constructs is left to a chunk no
    /// module imports.
    /// </summary>
    private Dictionary<ISymbol, HashSet<INamedTypeSymbol>> BuildSkipClusterDeps(
        IReadOnlyList<INamedTypeSymbol> types)
    {
        var result = new Dictionary<ISymbol, HashSet<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        var eager = new Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>>(SymbolEqualityComparer.Default);

        foreach (var type in types)
        {
            if (!IsSkipClustered(type)) continue;

            // Pass 1 — each member's own syntax: the types it names directly, and the siblings on
            // this same facade it reaches (whose dependencies are transitively its own).
            var members = new List<ISymbol>();
            var siblingCalls = new Dictionary<ISymbol, HashSet<ISymbol>>(SymbolEqualityComparer.Default);
            var deps = new Dictionary<ISymbol, HashSet<INamedTypeSymbol>>(SymbolEqualityComparer.Default);

            foreach (var raw in type.GetMembers())
            {
                if (raw is not (IMethodSymbol or IPropertySymbol or IFieldSymbol)) continue;
                var member = MemberKey(raw);
                if (deps.ContainsKey(member)) continue;

                var own = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                var calls = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

                foreach (var syntaxRef in raw.DeclaringSyntaxReferences)
                {
                    var node = syntaxRef.GetSyntax();
                    foreach (var descendant in node.DescendantNodesAndSelf())
                    {
                        Add(own, _model.GetTypeInfo(descendant).Type);
                        var symbol = _model.GetSymbolInfo(descendant).Symbol;
                        // The *declaring* type of whatever is called or read: `new Card(...)`,
                        // `Card.Something`, an extension method's own class.
                        Add(own, symbol?.ContainingType);
                        if (symbol is IMethodSymbol m) Add(own, m.ReturnType);

                        // …unless that declaring type is this very facade, in which case the edge
                        // being followed is a sibling call and the dependency is the sibling's set.
                        if (symbol is (IMethodSymbol or IPropertySymbol or IFieldSymbol)
                            && SymbolEqualityComparer.Default.Equals(symbol.ContainingType?.OriginalDefinition, type))
                        {
                            var sibling = MemberKey(symbol);
                            if (!SymbolEqualityComparer.Default.Equals(sibling, member)) calls.Add(sibling);
                        }
                    }
                }

                own.Remove(type);
                members.Add(member);
                deps[member] = own;
                siblingCalls[member] = calls;
            }

            // Pass 2 — transitive closure over the sibling graph. A facade's members freely form
            // cycles (A calls B, B calls A), so this is a plain worklist fixpoint rather than a
            // topological walk: a member is re-queued whenever one of its callees grows, and the sets
            // only ever grow within a finite universe, so it terminates.
            var callers = new Dictionary<ISymbol, List<ISymbol>>(SymbolEqualityComparer.Default);
            foreach (var member in members)
            {
                foreach (var callee in siblingCalls[member])
                {
                    if (!deps.ContainsKey(callee)) continue;
                    if (!callers.TryGetValue(callee, out var waiting)) callers[callee] = waiting = new List<ISymbol>();
                    waiting.Add(member);
                }
            }

            var queue = new Queue<ISymbol>(members);
            var queued = new HashSet<ISymbol>(members, SymbolEqualityComparer.Default);
            while (queue.Count > 0)
            {
                var member = queue.Dequeue();
                queued.Remove(member);

                var set = deps[member];
                var grew = false;
                foreach (var callee in siblingCalls[member])
                    if (deps.TryGetValue(callee, out var calleeDeps))
                        foreach (var dep in calleeDeps) grew |= set.Add(dep);

                if (!grew || !callers.TryGetValue(member, out var waiting)) continue;
                foreach (var caller in waiting)
                    if (queued.Add(caller)) queue.Enqueue(caller);
            }

            foreach (var member in members)
                if (deps[member].Count > 0) result[member] = deps[member];

            // Whatever the facade's own definition needs before anyone calls into it. A static
            // initializer has no call site, so its edges stay where the graph can see them.
            var eagerDeps = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var member in members)
                if (RunsAtTypeDefinition(member)) eagerDeps.UnionWith(deps[member]);
            if (eagerDeps.Count > 0) eager[type] = eagerDeps;
        }

        _skipClusterEagerDeps = eager;
        return result;
    }

    /// <summary>The symbol a member is keyed by, matching what <see cref="RecordSkipClusterCall"/>
    /// looks up at a call site: the original (unconstructed) definition.</summary>
    private static ISymbol MemberKey(ISymbol member) => member.OriginalDefinition;

    /// <summary>
    /// Whether this member's dependencies are needed the moment the facade's type is defined, rather
    /// than when something calls it: a static constructor, or a static field/property with an
    /// initializer. Those run as part of the define, so there is no call site to move their edges to
    /// and <see cref="ClusterRefsFor"/> keeps them on the facade.
    /// </summary>
    private static bool RunsAtTypeDefinition(ISymbol member) => member switch
    {
        IMethodSymbol { MethodKind: MethodKind.StaticConstructor } => true,
        IFieldSymbol { IsStatic: true } f => f.DeclaringSyntaxReferences
            .Any(r => r.GetSyntax() is VariableDeclaratorSyntax { Initializer: not null }),
        IPropertySymbol { IsStatic: true } p => p.DeclaringSyntaxReferences
            .Any(r => r.GetSyntax() is PropertyDeclarationSyntax { Initializer: not null }),
        _ => false,
    };

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
        var key = MemberKey(member);

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

    /// <summary>The reference set a type contributes to the graph. A skipped type contributes only
    /// what its own definition needs — static initializers and a static constructor, which run
    /// without anyone calling in. Everything else was attributed to the callers instead, and keeping
    /// it here as well would re-form the very cycle the attribute exists to break.</summary>
    private HashSet<INamedTypeSymbol> ClusterRefsFor(
        INamedTypeSymbol type, HashSet<INamedTypeSymbol> recorded)
    {
        if (!IsSkipClustered(type)) return recorded;
        var kept = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        if (_skipClusterEagerDeps is not null && _skipClusterEagerDeps.TryGetValue(type, out var eager))
            kept.UnionWith(eager);
        return kept;
    }
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
