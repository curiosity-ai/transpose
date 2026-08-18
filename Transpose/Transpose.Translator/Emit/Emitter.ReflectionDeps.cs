using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Transpose.Translator;

/// <summary>
/// The dependency a reflection-driven activator creates, which nothing in the emitted code shows.
///
/// The module chunker records an edge exactly when a type reference is <em>emitted</em>, which is
/// what makes it sound: a type stays loadable because some code names it. A reflection-driven
/// deserializer defeats that. <c>JsonConvert.DeserializeObject&lt;Order&gt;(json)</c> emits nothing
/// about <c>Order</c> beyond its <c>Type</c> object — and a deferred type's stub answers a
/// <c>Type</c> question perfectly well — then walks that metadata and constructs <c>Order</c> and
/// every member type below it. Constructing a stub throws, and its module can only be fetched
/// asynchronously, so by then it is too late.
///
/// <c>[ConstructsTypeArguments]</c> puts the edge back at the one place the compiler can see it: the
/// call site, where the type argument is written down. Each call records its type arguments — and,
/// transitively, every type reachable from them through the fields and properties reflection
/// describes — as real dependencies of the type being emitted. So the chunk that deserializes an
/// <c>Order</c> imports the chunk that defines it, and the DTO stays deferred for every screen that
/// does not.
///
/// This is the same shape as <c>[SkipTypeClustering]</c> (Emitter.SkipClustering.cs): both move a
/// dependency to the call site, because that is where the code actually runs. The difference is the
/// direction — that one takes edges away from a facade, this one adds edges an activator hides.
///
/// <c>[NeverDefer]</c> is the fallback for what a call site cannot show: a <c>Type</c> value rather
/// than a static type argument, or a reflective lookup inside a library that cannot be annotated. It
/// joins the eager roots (Emitter.Modules.cs), like the attribute classes the metadata constructs.
///
/// The attribute is read in three places, so an activator can be marked by whoever knows about it:
/// on the method, on its containing type, or — for a library that cannot be edited or has not been
/// re-released with the annotation — from the calling assembly, with
/// <c>[assembly: ConstructsTypeArguments(typeof(JsonConvert))]</c>.
/// </summary>
public sealed partial class Emitter
{
    /// <summary>Memoizes the walk for the type currently being emitted — Clone() gives each emitted
    /// type its own Emitter, so this is never shared across the parallel emit.</summary>
    private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> _activatedGraphs =
        new(SymbolEqualityComparer.Default);

    /// <summary>
    /// At a call to a <c>[ConstructsTypeArguments]</c> method, records the call's type arguments and
    /// the graph reachable from them as dependencies of the type being emitted. A no-op outside
    /// module mode, and for every other call.
    /// </summary>
    private void RecordActivatedTypeArguments(ISymbol? symbol)
    {
        if (_recordedRefs is null && _recordedExternalRefs is null) return;      // not module mode
        if (symbol is not IMethodSymbol { IsGenericMethod: true } method) return;
        if (!ConstructsTypeArguments(method)) return;

        foreach (var argument in method.TypeArguments)
            foreach (var reached in ActivatedGraph(argument))
                RecordRef(reached);
    }

    /// <summary>
    /// Whether calling this method activates its type arguments. Looked up on the method, on its
    /// containing type — so a binding library can mark one overload or a whole activator class — and
    /// in this compilation's own assembly attributes, which is how an application marks an activator
    /// it does not own (<c>[assembly: ConstructsTypeArguments(typeof(JsonConvert))]</c>).
    /// </summary>
    private bool ConstructsTypeArguments(IMethodSymbol method) =>
        TransposeNaming.HasAttr(method.OriginalDefinition, TransposeNaming.ConstructsTypeArgumentsAttr)
        || TransposeNaming.HasAttr(method.ContainingType, TransposeNaming.ConstructsTypeArgumentsAttr)
        || (method.ContainingType is { } owner
            && DeclaredActivators.Contains((INamedTypeSymbol)owner.OriginalDefinition));

    /// <summary>The types this assembly declares as activators, from its own
    /// <c>[assembly: ConstructsTypeArguments(typeof(X))]</c> attributes. Scanned once and carried
    /// across Clone(), so the parallel emit reads it rather than rebuilding it per type; empty for
    /// almost every project, so the lookup above costs nothing when nobody uses the escape hatch.</summary>
    private HashSet<INamedTypeSymbol> DeclaredActivators =>
        _declaredActivators ??= CollectDeclaredActivators();

    private HashSet<INamedTypeSymbol>? _declaredActivators;

    private HashSet<INamedTypeSymbol> CollectDeclaredActivators()
    {
        var found = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var attribute in _compilation.Assembly.GetAttributes())
        {
            if (!TransposeNaming.AttrIs(attribute, TransposeNaming.ConstructsTypeArgumentsAttr)) continue;
            foreach (var argument in attribute.ConstructorArguments)
                if (argument.Value is INamedTypeSymbol named)
                    found.Add((INamedTypeSymbol)named.OriginalDefinition);
        }
        return found;
    }

    /// <summary>
    /// Every type an activator can reach from <paramref name="root"/>: the type itself, its bases,
    /// and — recursively — the types of the instance fields and properties reflection describes,
    /// looking through arrays and generic arguments (a <c>List&lt;Order&gt;</c> member activates
    /// <c>Order</c>).
    ///
    /// Deliberately an over-approximation, for the same reason <c>BuildSkipClusterDeps</c> is one:
    /// it only has to be a superset of what the deserializer will touch, and over-approximating
    /// costs an import that was not needed, never a missing one that throws. It stops at anything
    /// with no module behind it — an external (native JS) type and the always-loaded BCL — and at
    /// the visited set, which is what terminates a recursive model.
    /// </summary>
    private List<INamedTypeSymbol> ActivatedGraph(ITypeSymbol root)
    {
        if (Unwrap(root) is not { } start) return new List<INamedTypeSymbol>();
        if (_activatedGraphs.TryGetValue(start, out var cached)) return cached;

        var reached = new List<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var queue = new Queue<INamedTypeSymbol>();
        seen.Add(start);
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            reached.Add(type);

            void Follow(ITypeSymbol? next)
            {
                if (Unwrap(next) is { } named && seen.Add(named)) queue.Enqueue(named);
                // A constructed generic reaches its arguments too — the member is declared as
                // List<Order>, and Order is what gets built.
                if (next is INamedTypeSymbol { IsGenericType: true } generic)
                    foreach (var argument in generic.TypeArguments)
                        if (Unwrap(argument) is { } arg && seen.Add(arg)) queue.Enqueue(arg);
                if (next is IArrayTypeSymbol array) Follow(array.ElementType);
            }

            if (type.BaseType is { SpecialType: not SpecialType.System_Object } baseType) Follow(baseType);
            foreach (var member in type.GetMembers())
                switch (member)
                {
                    // What a serializer round-trips: instance state. A compiler-generated backing
                    // field repeats its property, which the visited set absorbs.
                    case IFieldSymbol { IsStatic: false, IsConst: false } field: Follow(field.Type); break;
                    case IPropertySymbol { IsStatic: false, Parameters.IsEmpty: true } property: Follow(property.Type); break;
                }
        }

        _activatedGraphs[start] = reached;
        return reached;
    }

    /// <summary>
    /// The other direction: a call whose type arguments must NOT become dependencies.
    ///
    /// A generic call emits its type arguments — as a <c>{T}</c> template token, or as the leading
    /// arguments a generic method is threaded — and every emitted type reference is an edge, so
    /// <c>Activator.CreateInstanceAsync&lt;HomeView&gt;()</c> pinned <c>HomeView</c>'s chunk into the
    /// caller's. That is precisely what the asynchronous activator exists to avoid: it fetches the
    /// module itself, and a static edge to it leaves code that looks lazy and is not.
    ///
    /// <c>[LoadsTypeArguments]</c> says the method does that fetching, so the argument is emitted as
    /// a <em>soft</em> reference (<c>_softRefDepth</c>, the same mechanism <c>typeof</c> uses): the
    /// name is still written down, because the runtime needs it to know what to load, and a stub
    /// answers for it until the module arrives.
    /// </summary>
    private bool LoadsTypeArguments(IMethodSymbol? method) =>
        method is not null
        && (TransposeNaming.HasAttr(method.OriginalDefinition, TransposeNaming.LoadsTypeArgumentsAttr)
            || TransposeNaming.HasAttr(method.ContainingType, TransposeNaming.LoadsTypeArgumentsAttr));

    /// <summary>Opens a soft-reference scope around a call's type arguments when the callee loads
    /// them itself. Returns whether one was opened, to be handed back to <see cref="EndSoftTypeArgs"/>
    /// in a <c>finally</c>.</summary>
    private bool BeginSoftTypeArgs(IMethodSymbol? method)
    {
        if (!LoadsTypeArguments(method)) return false;
        _softRefDepth++;
        return true;
    }

    /// <summary>Closes what <see cref="BeginSoftTypeArgs"/> opened.</summary>
    private void EndSoftTypeArgs(bool opened)
    {
        if (opened) _softRefDepth--;
    }

    /// <summary>The named type behind a reference, or null when there is no module to load for it:
    /// a type parameter (nothing is written down at the call site), an external type (native JS), or
    /// the BCL (always loaded, and walking <c>string</c>'s members would be a waste).</summary>
    private static INamedTypeSymbol? Unwrap(ITypeSymbol? type) => type switch
    {
        IArrayTypeSymbol array => Unwrap(array.ElementType),
        INamedTypeSymbol named when !TransposeNaming.IsExternalType(named)
                                    && (named.Locations.Any(l => l.IsInSource)
                                        || TransposeNaming.IsTransposeCompiledSource(named))
            => (INamedTypeSymbol)named.OriginalDefinition,
        _ => null,
    };
}
