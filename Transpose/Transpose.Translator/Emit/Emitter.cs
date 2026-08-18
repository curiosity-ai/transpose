using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

/// <summary>
/// Walks the Roslyn syntax tree (guided by the semantic model) and emits JavaScript
/// in the Transpose runtime format (Transpose.assembly + Transpose.define), so the output runs against
/// the real tps.js / tps.core runtime.
/// </summary>
public sealed partial class Emitter
{
    private readonly CSharpCompilation _compilation;
    private JsWriter _w = new();
    private readonly NameMangler _names = new();
    private readonly TreeModel _model;
    private readonly string _assemblyName;

    /// <summary>While emitting a primary constructor's own body, its parameters are the JS
    /// function parameters (raw names); elsewhere captured params read from the instance.</summary>
    private bool _inPrimaryCtorBody;

    /// <summary>How many loop bodies (for/foreach/while/do) currently enclose the emission point.
    /// A local declared inside a loop is emitted with `let` (a fresh per-iteration binding) so a
    /// closure created in the loop captures that iteration's value — C# block scoping. Outside a
    /// loop (loop depth 0) locals stay `var` (function scope), which tolerates the same-name
    /// redeclarations across flattened scopes that some code relies on.</summary>
    private int _loopDepth;

    /// <summary>The type whose define body is currently being emitted. Its own type parameters are
    /// the ones actually bound as JS function parameters of the define, so <c>default(T)</c> may
    /// safely reference them via <c>Transpose.getDefaultValue(T)</c>; a type parameter from an enclosing
    /// type (accessible in C# but not emitted as a parameter here) is not in scope.</summary>
    private INamedTypeSymbol? _currentEmitType;

    /// <summary>JS identifiers the type being emitted binds locally, which would otherwise shadow a
    /// same-named type reference. See <c>ShadowingIdentifiers</c> / <c>UnshadowedTypeRef</c>.</summary>
    private HashSet<string>? _shadowingNames;

    /// <summary>
    /// Active goto label-dispatch contexts. When non-empty a statement body is being lowered
    /// into a `for(;;) switch($state)` machine: `goto L` sets the state and continues the loop.
    /// The top entry maps each label name to its case index and names the dispatch loop.
    /// </summary>
    private readonly Stack<(System.Collections.Generic.Dictionary<string, int> labels, string loopLabel, string stateVar)> _gotoContexts = new();

    /// <summary>Whether reflection metadata is emitted at all (tps.json reflection.disabled).</summary>
    public bool ReflectionEnabled { get; set; } = true;

    /// <summary>Where reflection metadata goes — inline in the assembly, or a separate file.</summary>
    public MetadataTarget MetadataTarget { get; set; } = MetadataTarget.Inline;

    /// <summary>Assembly version string emitted into a separate metadata file's header.</summary>
    public string AssemblyVersion { get; set; } = "1.0.0.0";

    /// <summary>When <see cref="MetadataTarget"/> is File/Assembly, the standalone metadata
    /// script (a full Transpose.assembly wrapper) produced by the last <see cref="Emit"/>; else null.</summary>
    public string? MetadataScript { get; private set; }

    /// <summary>What this build may reuse from the previous one (null = emit everything). Only the
    /// top-level emitter consults it; the per-type clones never see it.</summary>
    private IncrementalPlan? _plan;

    /// <summary>When non-null, every source type <see cref="TypeRef"/> emits is recorded here — the
    /// dependency set of the type currently being emitted, which module mode chunks on.</summary>
    private HashSet<INamedTypeSymbol>? _recordedRefs;

    /// <summary>Alongside <see cref="_recordedRefs"/>: the define names of types from *referenced*
    /// Transpose-compiled assemblies, so a chunk can import the referenced assembly's chunk that
    /// holds them.</summary>
    private HashSet<string>? _recordedExternalRefs;

    /// <summary>Nesting depth of a reference position that only needs a <em>Type object</em> rather
    /// than the type's code — a <c>typeof</c> operand. A stub satisfies those (see Modules.js), so
    /// they are not recorded as dependencies and do not fuse two chunks together.</summary>
    private int _softRefDepth;

    /// <summary>Emitting an <c>outputBy: Module</c> build, where reflection metadata must tolerate a
    /// type whose module has not been fetched (Emitter.Reflection.cs, MetaTypeName).</summary>
    private bool _moduleMetadata;

    /// <param name="models">A semantic-model cache to reuse. Passing the one the
    /// unsupported-feature scan already populated means every member that scan bound is bound
    /// once for the whole build instead of twice.</param>
    internal Emitter(CSharpCompilation compilation, string assemblyName, TreeModel? models, IncrementalPlan? plan = null)
    {
        _compilation = compilation;
        _assemblyName = assemblyName;
        _model = models ?? new TreeModel(compilation);
        _plan = plan;
    }

    public Emitter(CSharpCompilation compilation, string assemblyName = CompilationBuilder.DefaultAssemblyName)
        : this(compilation, assemblyName, null)
    {
    }

    private Emitter(CSharpCompilation compilation, string assemblyName, NameMangler names, TreeModel model)
    {
        _compilation = compilation;
        _assemblyName = assemblyName;
        _names = names;
        // Share the parent's semantic-model cache rather than building a fresh one per type: see
        // TreeModel for why that is the single biggest lever on JS-emit time.
        _model = model;
        _w.Indent();
    }

    public string Emit()
    {
        _w.WriteLine("/**");
        _w.WriteLine(" * Transpose.Translator generated output.");
        _w.WriteLine(" */");
        var types = PhaseTimings.Measure("  ├ collect + order types", CollectTypes);

        // Reflection metadata: either woven into this assembly function (inline target) or
        // collected into a standalone metadata script (file target), never both.
        var inlineMeta = ReflectionEnabled && MetadataTarget is MetadataTarget.Inline or MetadataTarget.Type;
        var fileMeta = ReflectionEnabled && MetadataTarget is MetadataTarget.File or MetadataTarget.Assembly;

        // Record the assembly's version with the runtime (matching the legacy compiler's
        // `H5.assemblyVersion(...)`), so reflection/diagnostics can report it. Emitted just before the
        // assembly body.
        if (!string.IsNullOrEmpty(AssemblyVersion))
            _w.WriteLine($"Transpose.assemblyVersion(\"{_assemblyName}\", \"{AssemblyVersion}\");");

        _w.Write($"Transpose.assembly(\"{_assemblyName}\", function ($asm, globals) ");
        _w.Block(() =>
        {
            _w.WriteLine("\"use strict\";");
            _w.WriteLine();

            long done = 0;

#pragma warning disable RS1024 // Symbols should be compared for equality - we don't the symbol comparer here as we want the actual object to define the order, not the "kind" of symbol
            var results = new ConcurrentDictionary<INamedTypeSymbol, string>();
#pragma warning restore RS1024 // Symbols should be compared for equality

            // Parallel.ForEach aggregates any thrown exception into an AggregateException. The
            // callers (RoslynTranslator) catch TranslationException to turn an unsupported construct
            // into a clean diagnostic, so unwrap a single TranslationException and rethrow it as
            // itself — otherwise an unsupported feature would surface as an unhandled AggregateException
            // (a crash) instead of a reported error.
            try
            {
                PhaseTimings.Measure("  ├ emit type bodies (parallel)", () =>
                    Parallel.ForEach(types, type =>
                    {
                        // An incremental build splices in the previous build's JavaScript for every
                        // type none of whose declaring files changed. The text is reused verbatim, so
                        // the bundle is byte-identical to the one a full build would have produced.
                        if (_plan?.TryReuse(type) is { } cached)
                        {
                            results[type] = cached;
                            _plan.RecordReused();
                        }
                        else
                        {
                            results[type] = EmitOnlyType(this, type).ToString();
                            _plan?.RecordReemitted();
                        }
                        var count = Interlocked.Increment(ref done);
                        CompileProgress.ReportStep("emitting JavaScript", count, types.Count);
                    }));
            }
            catch (AggregateException ex) when (ex.Flatten().InnerExceptions.OfType<TranslationException>().FirstOrDefault() is { } te)
            {
                throw te;
            }

            PhaseTimings.Measure("  ├ concatenate type bodies", () =>
            {
                foreach (var type in types)
                {
                    var js = results[type];
                    _w.WriteRaw(js);
                    _w.WriteLine();
                    if (_plan is not null)
                    {
                        var key = IncrementalPlan.TypeKey(type);
                        _plan.FinalTypeJs[key] = js;
                        _plan.FinalOrder.Add(key);
                    }
                }
            });

            // [Transpose.Ready] static methods: schedule each via Transpose.ready so it runs on
            // page load (or immediately when the assembly is loaded on demand, e.g. a lazily
            // fetched package). Emitted after all defines so the referenced types are registered.
            EmitReadyRegistrations(types);

            //foreach (var type in types)
            //{
            //    EmitType(type);
            //    _w.WriteLine();
            //    CompileProgress.ReportStep("emitting JavaScript", ++done, types.Count);
            //}

            // The reflection metadata describes declarations only — types, members, signatures and
            // attributes — so an incremental build over a body-only edit reuses it wholesale. That is
            // worth having: on a large project building the metadata block is ~12% of a build.
            if (inlineMeta)
            {
                if (_plan?.InlineMetadata is { } cachedInline)
                {
                    _w.WriteRaw(cachedInline);
                    if (_plan is not null) _plan.FinalInlineMetadata = cachedInline;
                }
                else
                {
                    var block = PhaseTimings.Measure("  ├ reflection metadata (inline)",
                        () => CaptureAtCurrentIndent(() => EmitReflectionMetadata(types)));
                    _w.WriteRaw(block);
                    if (_plan is not null) _plan.FinalInlineMetadata = block;
                }
            }
        });
        _w.WriteLine(");");

        MetadataScript = fileMeta
            ? _plan?.MetadataScript ?? PhaseTimings.Measure("  └ reflection metadata (file)", () => BuildMetadataFile(types))
            : null;
        return _w.ToString();
    }

    public static JsWriter EmitOnlyType(Emitter emitter, INamedTypeSymbol type)
        => EmitOnlyType(emitter, type, null);

    /// <param name="refs">When supplied, receives every source type the emitted body references.</param>
    public static JsWriter EmitOnlyType(Emitter emitter, INamedTypeSymbol type, HashSet<INamedTypeSymbol>? refs)
        => EmitOnlyType(emitter, type, refs, null);

    /// <param name="externalRefs">When supplied, receives the define names of referenced-assembly
    /// types the emitted body reaches into.</param>
    public static JsWriter EmitOnlyType(Emitter emitter, INamedTypeSymbol type,
        HashSet<INamedTypeSymbol>? refs, HashSet<string>? externalRefs)
    {
        var clonedEmitter = emitter.Clone();
        clonedEmitter._recordedRefs = refs;
        clonedEmitter._recordedExternalRefs = externalRefs;
        clonedEmitter.EmitType(type);
        return clonedEmitter._w;
    }

    private Emitter Clone()
    {
        return new Emitter(_compilation, _assemblyName, _names, _model)
        {
            // Built once before the parallel emit and read-only during it (see Emitter.SkipClustering.cs).
            _skipClusterDeps = _skipClusterDeps,
            _skipClusterEagerDeps = _skipClusterEagerDeps,
            _externalSkipClusterDeps = _externalSkipClusterDeps,
            // Same: the assembly's own [assembly: ConstructsTypeArguments(typeof(X))] list, computed
            // on the parent here so it is scanned once rather than once per emitted type.
            _declaredActivators = DeclaredActivators,
        };
    }

    /// <summary>The result of an <c>outputBy: ClassPath</c> emission: one bare
    /// <c>Transpose.define(...)</c> per non-external type at a namespace/containing-type path,
    /// plus the shared reflection metadata block (bare <c>$m/$n</c> calls) for the assembly.</summary>
    public sealed class ClassPathOutput
    {
        public List<(string relPath, string js)> Files { get; } = new();
        public List<(string type, string reason)> Skipped { get; } = new();
        public string? MetaBlock { get; init; }
    }

    /// <summary>
    /// Emits the assembly with <c>outputBy: ClassPath</c>: each type goes to its own file
    /// (<c>&lt;ns&gt;/&lt;containing types&gt;/&lt;Type&gt;.js</c>) containing a bare
    /// <c>Transpose.define(...)</c>, and the reflection metadata is returned as one bare block.
    /// This is how the base runtime library (Transpose.BCL) is transpiled: the per-class files are
    /// stitched with the hand-written <c>Resources/*.js</c> primitives into <c>tps.js</c>.
    /// </summary>
    public ClassPathOutput EmitClassPath()
    {
        var types = CollectTypes();
        var meta = ReflectionEnabled ? BuildMetadataBlock(types) : null;
        var outp = new ClassPathOutput { MetaBlock = meta };
        foreach (var type in types)
        {
            string js;
            try { js = Capture(() => EmitType(type)).Trim(); }
            catch (TranslationException ex)
            {
                // Skip a type the emitter can't yet translate (e.g. an unsupported ref construct in
                // a rarely-used BCL member) rather than aborting the whole runtime build; record it.
                outp.Skipped.Add((type.ToDisplayString(), ex.Message));
                continue;
            }
            if (js.Length == 0) continue;
            outp.Files.Add((ClassPathRelPath(type), js + "\n"));
        }
        return outp;
    }

    /// <summary>The <c>&lt;ns segments&gt;/&lt;containing types&gt;/&lt;Type&gt;.js</c> path for a type,
    /// mirroring the legacy ClassPath layout (no generic-arity suffix in the file name).</summary>
    private static string ClassPathRelPath(INamedTypeSymbol type)
    {
        var parts = new List<string>();
        var nsParts = new List<string>();
        for (var ns = type.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
            nsParts.Insert(0, ns.Name);
        parts.AddRange(nsParts);
        var containing = new List<string>();
        for (var t = type.ContainingType; t is not null; t = t.ContainingType) containing.Insert(0, t.Name);
        parts.AddRange(containing);
        parts.Add(type.Name);
        return string.Join("/", parts) + ".js";
    }

    /// <summary>
    /// Runs <paramref name="emit"/> against a temporary writer that starts at the *current*
    /// indentation depth, and returns its text. Splicing the result back with
    /// <see cref="JsWriter.WriteRaw"/> then reproduces exactly what emitting in place would have
    /// written — which is what lets the block be cached and restored verbatim.
    /// </summary>
    private string CaptureAtCurrentIndent(Action emit)
    {
        var saved = _w;
        _w = new JsWriter(saved.IndentLevel);
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
        // Resolve each file's declared types in parallel — every tree is independent and a project
        // has hundreds of them — then merge in tree order so the emitted bundle's type order stays
        // exactly what a sequential walk produced.
        var trees = _compilation.SyntaxTrees as IList<SyntaxTree> ?? _compilation.SyntaxTrees.ToList();
        var perTree = new List<INamedTypeSymbol>[trees.Count];
        Parallel.For(0, trees.Count, i =>
        {
            var tree = trees[i];
            var model = _model.SemanticModelFor(tree);
            var found = new List<INamedTypeSymbol>();
            foreach (var node in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(node) is INamedTypeSymbol sym)
                {
                    // External types ([External] on the type/assembly, or [Scope]/[GlobalMethods]
                    // bindings) have no emitted body: they are native JS (DOM, browser globals) or
                    // are supplied by a hand-written runtime file ([Script]/embedded resource, e.g.
                    // Newtonsoft's JsonConvert). Emitting a Transpose.define for them collides with
                    // the real definition ("already defined") — so an [assembly: External] binding
                    // library such as Transpose.Core contributes no runtime defines, matching h5.core.
                    if (TransposeNaming.IsExternalType(sym)) continue;
                    found.Add(sym);
                }
            }
            perTree[i] = found;
        });

        // Dedupe across trees (a partial type is declared in several) preserving first-seen order.
        var declared = new List<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var found in perTree)
            foreach (var sym in found)
                if (seen.Add(sym)) declared.Add(sym);

        // Emit each type after every source type it depends on (base class + implemented/
        // extended interfaces), so the runtime's Transpose.define never sees an undefined reference
        // in `inherits`. Dependency depth gives such an order (the graph is acyclic).
        var depthCache = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);
        return declared
            .OrderBy(t => DependencyDepth(t, depthCache))
            .ThenBy(t => t.TypeKind == TypeKind.Interface ? 0 : 1)
            .ToList();
    }

    /// <summary>
    /// The longest chain of source-type dependencies (base class and implemented/extended
    /// interfaces) below <paramref name="type"/>. Types with a greater depth are emitted later,
    /// guaranteeing a type's dependencies are defined first. Non-source dependencies live in
    /// the runtime (loaded before the bundle) and contribute no ordering constraint.
    /// </summary>
    private int DependencyDepth(INamedTypeSymbol type, Dictionary<INamedTypeSymbol, int> cache)
    {
        type = (INamedTypeSymbol)type.OriginalDefinition;
        if (cache.TryGetValue(type, out var cached)) return cached;
        cache[type] = 0; // guard against unexpected cycles

        var depth = 0;
        foreach (var dep in Dependencies(type))
            depth = Math.Max(depth, DependencyDepth(dep, cache) + 1);

        cache[type] = depth;
        return depth;
    }

    private static IEnumerable<INamedTypeSymbol> Dependencies(INamedTypeSymbol type)
    {
        // A type's `inherits` names its base class and interfaces *including their generic
        // type arguments* (e.g. LayerHost : ComponentBase<Layer, …> references Layer), and all
        // of those source types must already be defined when the lazy inherits runs.
        if (type.BaseType is { } bt)
            foreach (var d in SourceTypesIn(bt)) yield return d;
        foreach (var iface in type.Interfaces)
            foreach (var d in SourceTypesIn(iface)) yield return d;
    }

    /// <summary>The source named types within a type reference — the type itself and, recursively,
    /// its generic type arguments.</summary>
    private static IEnumerable<INamedTypeSymbol> SourceTypesIn(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named) yield break;
        if (named.Locations.Any(l => l.IsInSource))
            yield return (INamedTypeSymbol)named.OriginalDefinition;
        foreach (var arg in named.TypeArguments)
            foreach (var d in SourceTypesIn(arg)) yield return d;
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
