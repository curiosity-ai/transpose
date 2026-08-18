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
/// <item><b>A call to another skipped member</b> — a sibling on the same facade, or a member of a
/// second facade. Either way the callee's containing type is one whose edges are being dropped, so
/// member <c>A</c> calling <c>B</c> contributed only <c>B</c>'s return type, never the types
/// <c>B</c>'s own body constructs. Nothing then imported them: <c>B</c>'s chunk doesn't (its edges
/// are dropped), and <c>A</c>'s callers didn't know to. <see cref="BuildSkipClusterDeps"/> therefore
/// takes a <b>transitive closure over every skipped member</b> before attributing anything to a call
/// site, so <c>deps(A) ⊇ deps(B)</c>. Both halves were found by running it: the sibling case killed
/// <c>#/home</c>, and the cross-facade case killed <c>App.Sidenav.Initialize</c> once <c>App</c> and
/// <c>AppSidenav</c> were both attributed.</item>
/// <item><b>Static state.</b> A static field initializer and a static constructor do not run with the
/// member that happens to be called, so following sibling calls alone misses them. They are not
/// needed when the facade's chunk <em>evaluates</em> either: the runtime hangs <c>$staticInit</c> off
/// the getter of the type's global slot (<c>Class.js</c>), so it fires the first time anything reads
/// the class — which is a call site like any other. So those dependencies are folded into
/// <b>every</b> member's set, and reach whoever touches the facade first. Keeping them on the facade
/// instead would be sound and would rebuild the very cycle the attribute exists to break: the app
/// shell's static tables alone re-fused 170 types into one chunk.</item>
/// </list>
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
        var facades = types.Where(IsSkipClustered).ToList();
        var result = new Dictionary<ISymbol, HashSet<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        if (facades.Count == 0) return result;

        // Pass 1 — each member's own syntax: the types it names directly, and the skipped members it
        // calls (whose dependencies are transitively its own). "Skipped member" spans every facade,
        // not just this one: a call from one facade into another loses the callee's dependencies the
        // same way a sibling call does, because both callees' edges are being dropped.
        var members = new List<ISymbol>();
        var owner = new Dictionary<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var skippedCalls = new Dictionary<ISymbol, HashSet<ISymbol>>(SymbolEqualityComparer.Default);
        var deps = new Dictionary<ISymbol, HashSet<INamedTypeSymbol>>(SymbolEqualityComparer.Default);

        foreach (var type in facades)
        {
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
                    var muted = MutedByLoadedTypeArguments(node);

                    foreach (var descendant in node.DescendantNodesAndSelf())
                    {
                        if (muted.Contains(descendant)) continue;

                        Add(own, _model.GetTypeInfo(descendant).Type);
                        var symbol = _model.GetSymbolInfo(descendant).Symbol;
                        // The *declaring* type of whatever is called or read: `new Card(...)`,
                        // `Card.Something`, an extension method's own class.
                        Add(own, symbol?.ContainingType);
                        if (symbol is IMethodSymbol m && !LoadsTypeArguments(m)) Add(own, m.ReturnType);

                        // …and when that declaring type is a facade, the call also inherits whatever
                        // the callee's body reaches — nothing else is going to import it.
                        if (symbol is (IMethodSymbol or IPropertySymbol or IFieldSymbol)
                            && IsSkipClustered(symbol.ContainingType?.OriginalDefinition))
                        {
                            var callee = MemberKey(symbol);
                            if (!SymbolEqualityComparer.Default.Equals(callee, member)) calls.Add(callee);
                        }
                    }
                }

                own.Remove(type);
                members.Add(member);
                owner[member] = type;
                deps[member] = own;
                skippedCalls[member] = calls;
            }
        }

        // Pass 2 — transitive closure over the call graph among skipped members. They freely form
        // cycles (A calls B, B calls A), so this is a plain worklist fixpoint rather than a
        // topological walk: a member is re-queued whenever one of its callees grows, and the sets
        // only ever grow within a finite universe, so it terminates.
        var callers = new Dictionary<ISymbol, List<ISymbol>>(SymbolEqualityComparer.Default);
        foreach (var member in members)
        {
            foreach (var callee in skippedCalls[member])
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
            foreach (var callee in skippedCalls[member])
                if (deps.TryGetValue(callee, out var calleeDeps))
                    foreach (var dep in calleeDeps) grew |= set.Add(dep);

            if (!grew || !callers.TryGetValue(member, out var waiting)) continue;
            foreach (var caller in waiting)
                if (queued.Add(caller)) queue.Enqueue(caller);
        }

        // Pass 3 — the static state, per facade. It runs on the first *read* of the type's global
        // slot rather than with any particular member, so it belongs to every way into that facade.
        var onFirstTouch = new Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        foreach (var member in members)
        {
            if (!RunsOnFirstTouch(member)) continue;
            var type = owner[member];
            if (!onFirstTouch.TryGetValue(type, out var set))
                onFirstTouch[type] = set = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            set.UnionWith(deps[member]);
        }

        foreach (var member in members)
        {
            var set = deps[member];
            if (onFirstTouch.TryGetValue(owner[member], out var touch)) set.UnionWith(touch);
            if (set.Count > 0) result[member] = set;
        }

        return result;
    }

    /// <summary>
    /// The parts of a facade member's syntax that must NOT contribute a dependency, because the call
    /// they belong to loads its type arguments itself (<c>[LoadsTypeArguments]</c>).
    ///
    /// This pass is a syntax walk and deliberately over-approximates — an extra import costs size,
    /// a missing one throws — but here the over-approximation defeats the point. A route table
    /// written as <c>await AppRouting.ActivateAsync&lt;ContactsView&gt;()</c> names the view three
    /// times over (the type argument, the <c>Task&lt;ContactsView&gt;</c> return type, the awaited
    /// expression's type), and recording any of them hands the view back to whoever calls the facade
    /// — which is exactly the eager edge the activation was written to remove. The emitter itself
    /// records none of them: it emits the name as a soft reference and nothing else.
    ///
    /// So the type-argument list, the call, and an <c>await</c> directly wrapping it are muted. A
    /// type the member reaches any *other* way is unaffected, because it is added from that other
    /// node.
    /// </summary>
    private HashSet<SyntaxNode> MutedByLoadedTypeArguments(SyntaxNode root)
    {
        var muted = new HashSet<SyntaxNode>();
        foreach (var node in root.DescendantNodesAndSelf())
        {
            if (node is not InvocationExpressionSyntax invocation) continue;
            if (_model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { IsGenericMethod: true } method) continue;
            if (!LoadsTypeArguments(method)) continue;

            muted.Add(invocation);
            foreach (var name in invocation.Expression.DescendantNodesAndSelf().OfType<GenericNameSyntax>())
                foreach (var inside in name.TypeArgumentList.DescendantNodesAndSelf())
                    muted.Add(inside);

            // `await Activate<X>()` has the awaited expression's type — X itself — on the await node.
            var wrapper = invocation.Parent;
            while (wrapper is ParenthesizedExpressionSyntax) wrapper = wrapper.Parent;
            if (wrapper is AwaitExpressionSyntax) muted.Add(wrapper);
        }
        return muted;
    }

    /// <summary>The symbol a member is keyed by, matching what <see cref="RecordSkipClusterCall"/>
    /// looks up at a call site: the original (unconstructed) definition.</summary>
    private static ISymbol MemberKey(ISymbol member) => member.OriginalDefinition;

    /// <summary>
    /// Whether this member runs on the first *touch* of the facade rather than with a call to it: a
    /// static constructor, or a static field/property with an initializer. The runtime hangs
    /// <c>$staticInit</c> off the getter of the type's global slot, so reading the class at all runs
    /// the lot — which means these dependencies belong to every member's set rather than to any one
    /// of them.
    /// </summary>
    private static bool RunsOnFirstTouch(ISymbol member) => member switch
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

    /// <summary>The reference set a type contributes to the graph. A skipped type contributes
    /// nothing: every one of its dependencies — including the ones its static state needs — was
    /// attributed to the callers instead, and keeping any of them here would re-form the very cycle
    /// the attribute exists to break.</summary>
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
