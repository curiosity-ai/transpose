using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Transpose.Translator;

public sealed partial class Emitter
{
    /// <summary>
    /// The result of an <c>outputBy: Module</c> emission: one ES module per chunk, plus the entry
    /// module that imports the eagerly-needed ones and registers the rest.
    /// </summary>
    public sealed class ModuleOutput
    {
        /// <summary>Chunk files, output-relative path → JavaScript.</summary>
        public List<(string relPath, string js)> Chunks { get; } = new();

        /// <summary>The entry module: eager imports, the reflection metadata, the manifest of what
        /// was deferred, and the runtime init. This is the only file index.html scripts.</summary>
        public string EntryJs { get; init; } = "";

        public int EagerChunkCount { get; init; }
        public int LazyChunkCount { get; init; }
        public int LazyTypeCount { get; init; }

        /// <summary>Every emitted type's define name → the site-relative chunk file holding it. A
        /// package embeds this so a consuming build can import the chunk behind a type it uses;
        /// without it the consumer's reference would land on a stub it cannot resolve synchronously.
        /// </summary>
        public Dictionary<string, string> TypeToChunk { get; } = new(StringComparer.Ordinal);

        /// <summary>For a <c>[SkipTypeClustering]</c> facade: each member's documentation-comment id
        /// → the emitted define names its body reaches. A consuming build cannot see the facade's
        /// source, so without this it would import the facade's chunk and none of the chunks the
        /// member it calls actually needs. See Emitter.SkipClustering.cs.</summary>
        public Dictionary<string, List<string>> SkipClusterDeps { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Emits the assembly as ES modules instead of one bundle.
    ///
    /// A chunk is a <b>strongly-connected component of the reference graph</b> — the graph
    /// <see cref="TypeRef"/> recorded while emitting each type's body. That is the smallest sound
    /// unit: <c>Transpose.define</c> resolves <c>inherits</c> eagerly (Class.js), so a type's bases
    /// must already be defined when its define runs, and a per-class split cannot guarantee that in
    /// the presence of a reference cycle. The condensation of an SCC graph is a DAG, so a chunk can
    /// pull in every chunk it references with a side-effect <c>import</c> and the evaluation order
    /// is always correct — no name rewriting, because every emitted type reference is already a
    /// dotted global.
    ///
    /// Chunks reachable from the entry point are imported by the entry module; the rest are declared
    /// to <c>Transpose.Modules</c> so reflection still sees their types while their code stays
    /// unfetched, and are loaded on demand. A project with no entry point (a library) keeps
    /// everything eager — there is nothing to be lazy relative to.
    /// </summary>
    /// <param name="chunkDirectory">Site-relative folder the chunk files go in. Per assembly, so two
    /// module-mode assemblies in one site cannot collide.</param>
    /// <param name="externalChunks">Define name → site-relative chunk file, merged from every
    /// referenced assembly that was itself built as modules.</param>
    /// <param name="packageMode">A library: nothing is eager beyond what its own [Ready] handlers
    /// need, because there is no entry point to be lazy relative to — the consumer's chunks import
    /// what they use, and everything else stays a stub until something asks for it.</param>
    /// <param name="minChunkBytes">The second pass's target band: an SCC chunk smaller than this is
    /// merged with whatever loads alongside it (see Emitter.ModuleChunks.cs). 0 emits one chunk per
    /// SCC, which is what the first pass produces on its own.</param>
    /// <param name="maxChunkBytes">The ceiling a merged chunk is kept under.</param>
    public ModuleOutput EmitModules(
        string chunkDirectory = "chunks",
        IReadOnlyDictionary<string, string>? externalChunks = null,
        bool packageMode = false,
        IReadOnlyDictionary<string, List<string>>? externalSkipClusterDeps = null,
        int minChunkBytes = DefaultMinChunkBytes,
        int maxChunkBytes = DefaultMaxChunkBytes)
    {
        _externalSkipClusterDeps = externalSkipClusterDeps;
        // Reflection metadata is emitted once for the whole assembly, outside the per-type walk, so
        // its type references never take part in chunking — see MetaTypeName.
        _moduleMetadata = true;
        var types = PhaseTimings.Measure("  ├ collect + order types", CollectTypes);
        var order = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);
        for (var i = 0; i < types.Count; i++) order[types[i]] = i;

        // [SkipTypeClustering]: a facade's member dependencies belong to its callers, so they have to
        // be known before any caller is emitted.
        _skipClusterDeps = PhaseTimings.Measure("  ├ skip-clustering member deps",
            () => BuildSkipClusterDeps(types));

        // Emit every type once, recording what it references as we go.
        var bodies = new ConcurrentDictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default);
        var refs = new ConcurrentDictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        var extRefs = new ConcurrentDictionary<INamedTypeSymbol, HashSet<string>>(SymbolEqualityComparer.Default);
        try
        {
            PhaseTimings.Measure("  ├ emit type bodies (parallel)", () =>
                Parallel.ForEach(types, type =>
                {
                    var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                    var ext = externalChunks is null ? null : new HashSet<string>(StringComparer.Ordinal);
                    bodies[type] = EmitOnlyType(this, type, seen, ext).ToString();
                    seen.Remove(type);
                    refs[type] = ClusterRefsFor(type, seen);
                    if (ext is not null) extRefs[type] = ext;
                }));
        }
        catch (AggregateException ex) when (ex.Flatten().InnerExceptions.OfType<TranslationException>().FirstOrDefault() is { } te)
        {
            throw te;
        }

        var skipDeps = PublishedSkipClusterDeps();

        var chunks = PhaseTimings.Measure("  ├ chunk (strongly-connected components)",
            () => Chunk(types, refs, order));

        // Eager set: the chunks the entry module has to import, closed over chunk dependencies.
        //
        //  - an application: the chunk holding Main;
        //  - a library (packageMode): only the chunks holding [Ready] handlers, which run on load and
        //    so cannot be deferred. Everything else waits to be imported by a consumer's chunk or
        //    fetched on demand — a library has no entry point to be lazy relative to, and making it
        //    all eager would defeat the split entirely;
        //  - neither (a site build with no Main): everything, since nothing can be deferred safely.
        var roots = new List<INamedTypeSymbol>();
        var canDefer = packageMode;
        if (packageMode)
        {
            roots.AddRange(types.Where(t => t.GetMembers().OfType<IMethodSymbol>()
                .Any(m => m.IsStatic && m.GetAttributes().Any(a => TransposeNaming.AttrIs(a, TransposeNaming.ReadyAttr)))));
        }
        else if (_compilation.GetEntryPoint(default)?.ContainingType?.OriginalDefinition is INamedTypeSymbol main)
        {
            roots.Add(main);
            canDefer = true;
        }

        // The reflection metadata covers every type and *constructs* the attributes it records —
        // `new SomeAttribute(...)`, the first time a type's metadata is materialized (getMembers,
        // GetCustomAttributes, a reflection-driven deserializer). That construction is synchronous,
        // so an attribute class whose chunk has not been fetched throws where a stub answers every
        // other reflection question. The classes are few and small, and nothing imports them (the
        // metadata is emitted outside the per-type walk), so they join the roots of the eager set.
        if (canDefer && ReflectionEnabled) roots.AddRange(MetadataAttributeClasses(types));

        // [NeverDefer]: something reaches this type purely through reflection — a DTO an activator
        // builds from a `Type` value, a class resolved by name — so no emitted reference records the
        // edge and the chunker would leave it a stub that throws when constructed. Prefer
        // [ConstructsTypeArguments] on the generic method that does the activating, which records the
        // dependency at the call site instead of making the type eager for everyone; this is the
        // fallback for what a call site cannot show. See Emitter.ReflectionDeps.cs.
        if (canDefer)
            roots.AddRange(types.Where(t => TransposeNaming.HasAttr(t, TransposeNaming.NeverDeferAttr)));

        var eager = new HashSet<int>();
        if (canDefer)
        {
            var stack = new Stack<int>();
            foreach (var r in roots)
                if (chunks.IndexOf.TryGetValue(r, out var c) && eager.Add(c)) stack.Push(c);
            while (stack.Count > 0)
                foreach (var d in chunks.Deps[stack.Pop()])
                    if (eager.Add(d)) stack.Push(d);
        }
        else
        {
            for (var i = 0; i < chunks.Members.Count; i++) eager.Add(i);
        }

        // Second pass: merge the SCCs into chunks worth fetching. An SCC is the smallest sound
        // unit, not a useful one — a real library comes out with a median chunk of a couple of KB,
        // so a screen that needs twenty types pays twenty requests. Coalesce groups them by what is
        // loaded together and cuts the result into the target size band. Sizes are exact: the type
        // bodies are already emitted at this point (chunking needs the graph the emit records), so
        // nothing is estimated and nothing is emitted twice. See Emitter.ModuleChunks.cs.
        if (minChunkBytes > 0 && chunks.Members.Count > 1)
        {
            var sizes = new int[chunks.Members.Count];
            for (var i = 0; i < chunks.Members.Count; i++)
                foreach (var t in chunks.Members[i]) sizes[i] += bodies[t].Length + 1;

            chunks = PhaseTimings.Measure("  ├ coalesce chunks (co-load signature)", () =>
            {
                var merged = Coalesce(chunks, sizes, eager, order, minChunkBytes, maxChunkBytes, out var mergedEager);
                eager = mergedEager;
                return merged;
            });
        }

        var dir = string.IsNullOrEmpty(chunkDirectory) ? "" : chunkDirectory.TrimEnd('/') + "/";
        string ChunkFile(int i) => $"{dir}c{i}.mjs";

        var output = new List<(string, string)>();
        for (var i = 0; i < chunks.Members.Count; i++)
        {
            var w = new StringBuilder();
            // Import order is the chunk index, which is the topological order Chunk() assigned, so
            // two builds of the same sources produce byte-identical files.
            foreach (var d in chunks.Deps[i].OrderBy(x => x))
                w.Append("import '").Append(RelativeImport(ChunkFile(i), ChunkFile(d))).Append("';\n");
            // Cross-assembly: a referenced module-mode assembly's chunk holding a type this one uses.
            // Without it the reference would resolve to that assembly's stub, and a stub cannot be
            // resolved synchronously.
            if (externalChunks is not null)
            {
                var wanted = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var t in chunks.Members[i])
                    if (extRefs.TryGetValue(t, out var names))
                        foreach (var n in names)
                            if (externalChunks.TryGetValue(n, out var file)) wanted.Add(file);
                foreach (var file in wanted)
                    w.Append("import '").Append(RelativeImport(ChunkFile(i), file)).Append("';\n");
            }
            // A bare Transpose.define outside the Transpose.assembly(...) wrapper has no ambient
            // assembly, so each chunk names its own before defining anything.
            w.Append("Transpose.$useAssembly(\"").Append(_assemblyName).Append("\");\n");
            foreach (var t in chunks.Members[i])
            {
                w.Append(Dedent(bodies[t]));
                w.Append('\n');
            }
            output.Add((ChunkFile(i), w.ToString()));
        }

        var lazyTypes = new List<INamedTypeSymbol>();
        for (var i = 0; i < chunks.Members.Count; i++)
            if (!eager.Contains(i)) lazyTypes.AddRange(chunks.Members[i]);

        var result = new ModuleOutput
        {
            EntryJs = BuildEntryModule(types, chunks, eager, lazyTypes, ChunkFile, externalChunks),
            EagerChunkCount = eager.Count,
            LazyChunkCount = chunks.Members.Count - eager.Count,
            LazyTypeCount = lazyTypes.Count,
        }.With(output);
        foreach (var t in types) result.TypeToChunk[MetaTypeDefName(t)] = ChunkFile(chunks.IndexOf[t]);
        foreach (var kv in skipDeps) result.SkipClusterDeps[kv.Key] = kv.Value;
        return result;
    }

    private sealed record ChunkGraph(
        List<List<INamedTypeSymbol>> Members,
        List<HashSet<int>> Deps,
        Dictionary<INamedTypeSymbol, int> IndexOf);

    /// <summary>
    /// Groups the types into strongly-connected components of the reference graph and returns the
    /// condensation, indexed so that a chunk's dependencies always have a lower index than itself
    /// (a topological order) — which makes both the import lists and the file names deterministic.
    /// </summary>
    private static ChunkGraph Chunk(
        List<INamedTypeSymbol> types,
        ConcurrentDictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>> refs,
        Dictionary<INamedTypeSymbol, int> order)
    {
        // Iterative Tarjan — the graph is a whole project's type graph, deep enough to blow a
        // recursive walk's stack. Tarjan emits each component only after every component it points
        // at, so the emission order is already a reverse-topological order of the condensation.
        var index = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);
        var low = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);
        var onStack = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var stack = new Stack<INamedTypeSymbol>();
        var components = new List<List<INamedTypeSymbol>>();
        var next = 0;

        // Deterministic successor order, so the component numbering never depends on hash order.
        List<INamedTypeSymbol> Successors(INamedTypeSymbol t) =>
            refs.TryGetValue(t, out var s)
                ? s.Where(order.ContainsKey).OrderBy(x => order[x]).ToList()
                : new List<INamedTypeSymbol>();

        foreach (var root in types)
        {
            if (index.ContainsKey(root)) continue;
            var work = new Stack<(INamedTypeSymbol node, List<INamedTypeSymbol> succ, int i)>();
            index[root] = low[root] = next++;
            stack.Push(root); onStack.Add(root);
            work.Push((root, Successors(root), 0));

            while (work.Count > 0)
            {
                var (node, succ, i) = work.Pop();
                var advanced = false;
                while (i < succ.Count)
                {
                    var w = succ[i++];
                    if (!index.ContainsKey(w))
                    {
                        work.Push((node, succ, i));
                        index[w] = low[w] = next++;
                        stack.Push(w); onStack.Add(w);
                        work.Push((w, Successors(w), 0));
                        advanced = true;
                        break;
                    }
                    if (onStack.Contains(w)) low[node] = Math.Min(low[node], index[w]);
                }
                if (advanced) continue;

                if (low[node] == index[node])
                {
                    var comp = new List<INamedTypeSymbol>();
                    INamedTypeSymbol popped;
                    do
                    {
                        popped = stack.Pop();
                        onStack.Remove(popped);
                        comp.Add(popped);
                    } while (!SymbolEqualityComparer.Default.Equals(popped, node));
                    // Inside a chunk, keep the emitter's own dependency-depth ordering: it already
                    // guarantees a type's bases are defined before it.
                    comp.Sort((a, b) => order[a].CompareTo(order[b]));
                    components.Add(comp);
                }
                if (work.Count > 0)
                {
                    var parent = work.Pop();
                    low[parent.node] = Math.Min(low[parent.node], low[node]);
                    work.Push(parent);
                }
            }
        }

        var indexOf = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);
        for (var i = 0; i < components.Count; i++)
            foreach (var t in components[i]) indexOf[t] = i;

        var deps = new List<HashSet<int>>(components.Count);
        for (var i = 0; i < components.Count; i++) deps.Add(new HashSet<int>());
        foreach (var t in types)
        {
            if (!refs.TryGetValue(t, out var s)) continue;
            var from = indexOf[t];
            foreach (var r in s)
                if (indexOf.TryGetValue(r, out var to) && to != from) deps[from].Add(to);
        }

        return new ChunkGraph(components, deps, indexOf);
    }

    /// <summary>The chunk files of the attribute classes the metadata records that come from a
    /// referenced module-mode assembly, sorted so the entry module is byte-identical run to run.
    /// Empty when reflection is off or no reference was built as modules.</summary>
    private IEnumerable<string> ExternalMetadataAttributeChunks(
        List<INamedTypeSymbol> types, IReadOnlyDictionary<string, string>? externalChunks)
    {
        if (!ReflectionEnabled || externalChunks is null) return Array.Empty<string>();

        var mine = new HashSet<INamedTypeSymbol>(types, SymbolEqualityComparer.Default);
        var files = new SortedSet<string>(StringComparer.Ordinal);

        void Collect(IEnumerable<AttributeData> attrs)
        {
            foreach (var a in ReflectableAttributes(attrs))
                if (a.AttributeClass?.OriginalDefinition is INamedTypeSymbol ac && !mine.Contains(ac)
                    && externalChunks.TryGetValue(DefineName(ac), out var file))
                    files.Add(file);
        }

        foreach (var t in types)
        {
            Collect(t.GetAttributes());
            foreach (var m in t.GetMembers()) Collect(m.GetAttributes());
        }
        return files;
    }

    /// <summary>Every attribute class this compilation emits that the reflection metadata records —
    /// on a type, or on one of its members — deduplicated. Cross-assembly attribute classes are not
    /// here: they are imported by the entry module instead (BuildEntryModule).</summary>
    private List<INamedTypeSymbol> MetadataAttributeClasses(List<INamedTypeSymbol> types)
    {
        var found = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var byName = new HashSet<INamedTypeSymbol>(types, SymbolEqualityComparer.Default);

        void Collect(IEnumerable<AttributeData> attrs)
        {
            foreach (var a in ReflectableAttributes(attrs))
                if (a.AttributeClass?.OriginalDefinition is INamedTypeSymbol ac && byName.Contains(ac))
                    found.Add(ac);
        }

        foreach (var t in types)
        {
            Collect(t.GetAttributes());
            foreach (var m in t.GetMembers()) Collect(m.GetAttributes());
        }
        return found.ToList();
    }

    /// <summary>The entry module: it imports the eager chunks (so they are fully evaluated before
    /// its own body runs), then declares what was deferred, attaches the reflection metadata for
    /// <em>every</em> type, and starts the runtime.</summary>
    private string BuildEntryModule(
        List<INamedTypeSymbol> types, ChunkGraph chunks, HashSet<int> eager,
        List<INamedTypeSymbol> lazyTypes, Func<int, string> chunkFile,
        IReadOnlyDictionary<string, string>? externalChunks)
    {
        var sb = new StringBuilder();
        sb.Append("/**\n * Transpose.Translator generated output (module entry).\n */\n");
        foreach (var i in eager.OrderBy(x => x))
            sb.Append("import './").Append(chunkFile(i)).Append("';\n");
        // An attribute class the metadata constructs can live in a referenced module-mode assembly,
        // where it is no more constructible from a stub than a local one is - and no chunk of this
        // assembly imports it, since the metadata takes no part in chunking.
        foreach (var file in ExternalMetadataAttributeChunks(types, externalChunks))
            sb.Append("import './").Append(file).Append("';\n");
        sb.Append('\n');

        if (!string.IsNullOrEmpty(AssemblyVersion))
            sb.Append("Transpose.assemblyVersion(\"").Append(_assemblyName).Append("\", \"").Append(AssemblyVersion).Append("\");\n");
        sb.Append("Transpose.$useAssembly(\"").Append(_assemblyName).Append("\");\n\n");

        // Reflection metadata describes declarations only, so it is always eager and always covers
        // every type. It is emitted BEFORE the manifest on purpose: Modules.register() ends with a
        // Transpose.init(), and init runs the entry point — so anything emitted after the register
        // call would not exist yet when Main runs. An entry whose type is still missing is deferred
        // by setMetadata and flushed by that same init, at the top of it, before $main is scheduled.
        if (ReflectionEnabled && BuildMetadataBlock(types) is { } meta)
        {
            foreach (var line in meta.Split('\n')) sb.Append(line).Append('\n');
            sb.Append('\n');
        }

        // What was deferred. Registering stubs makes every one of these types visible to
        // reflection — Assembly.GetTypes(), IsAssignableFrom, the attributes above — while its
        // chunk stays unfetched.
        if (lazyTypes.Count > 0)
        {
            sb.Append("Transpose.Modules.register({\n");
            foreach (var t in lazyTypes)
            {
                var bases = ManifestBaseSpecs(t);
                sb.Append("    \"").Append(MetaTypeDefName(t)).Append("\": { m: \"./")
                  .Append(chunkFile(chunks.IndexOf[t])).Append("\", k: \"").Append(ManifestKind(t))
                  .Append("\", a: \"").Append(_assemblyName).Append("\", i: [")
                  .Append(string.Join(", ", bases)).Append("] },\n");
            }
            sb.Append("});\n\n");
        }

        var ready = CaptureAtCurrentIndent(() => EmitReadyRegistrations(types));
        if (ready.Trim().Length > 0) sb.Append(ready.TrimEnd()).Append('\n');

        sb.Append("Transpose.init();\n");
        return sb.ToString();
    }

    private static string ManifestKind(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Interface => "interface",
        TypeKind.Struct => "struct",
        TypeKind.Enum => "enum",
        _ => "class",
    };

    /// <summary>
    /// The base class and interfaces a stub reports, as JavaScript literals the runtime resolves
    /// (see <c>Modules.$resolveType</c>). Specs rather than expressions because a base may itself
    /// still be a stub when the manifest is registered — a spec can be resolved later, an
    /// expression is evaluated on the spot.
    ///
    /// Two forms. A plain type is its dotted name, resolved with <c>Transpose.unroll</c>:
    /// <c>"tss.IComponent"</c>. A CONSTRUCTED generic is an array of the definition name followed
    /// by its arguments (each a spec in turn, so <c>IFoo&lt;IBar&lt;int&gt;&gt;</c> nests):
    /// <c>["tss.CB$2", "tss.Avatar", "HTMLElement"]</c>. The runtime builds it by applying the
    /// definition, which is what makes <c>IsAssignableFrom</c> against a constructed generic answer
    /// from a stub — <c>varianceAssignable</c> matches on <c>$genericTypeDefinition</c> plus
    /// <c>$typeArguments</c>, neither of which a bare definition object carries.
    ///
    /// A base whose arguments cannot be named — an open <c>class Deferred&lt;T&gt; : IFoo&lt;T&gt;</c>
    /// (there is no T to write down until the definition is applied), an array argument, a type
    /// nested in a generic — falls back to the definition name alone, which is what this emitted
    /// before specs existed: the definition-level relationship is still reported, a question about
    /// one specific instantiation still cannot be answered without loading the module.
    /// </summary>
    private List<string> ManifestBaseSpecs(INamedTypeSymbol type)
    {
        var specs = new List<string>();
        void Add(INamedTypeSymbol t)
        {
            var name = ManifestTypeName(t);
            if (string.IsNullOrEmpty(name)) return;
            var spec = ManifestConstructedSpec(t) ?? "\"" + name + "\"";
            if (!specs.Contains(spec)) specs.Add(spec);
        }
        if (type.BaseType is { } bt && bt.SpecialType != SpecialType.System_Object) Add(bt);
        foreach (var i in type.AllInterfaces.Where(TransposeNaming.IsInheritableInterface)) Add(i);
        return specs;
    }

    /// <summary>The name a manifest spec resolves through <c>Transpose.unroll</c> — the same
    /// arity-suffixed define name a reference uses, or the runtime binding of an external type.</summary>
    private string? ManifestTypeName(INamedTypeSymbol t) =>
        TransposeNaming.IsTransposeCompiledSource(t)
            ? (t.Arity > 0 ? _names.TypeFullName(t) + "$" + t.Arity : _names.TypeFullName(t))
            // The same two-step TypeRefCore uses for an external type: its [Name], else the name it
            // takes from an enclosing [Scope]/[GlobalMethods] binding (HTMLElement and friends).
            : TransposeNaming.GetName(t) ?? ScopedExternalName(t);

    /// <summary>
    /// The array-form spec for a constructed generic, or null when it is not one or cannot be
    /// expressed (see <see cref="ManifestBaseSpecs"/>). Deliberately narrow: only a type whose own
    /// arity accounts for every effective type argument, so a type nested in a generic — whose
    /// define takes the enclosing arguments too — is left to the definition-name fallback rather
    /// than guessed at.
    /// </summary>
    private string? ManifestConstructedSpec(INamedTypeSymbol t)
    {
        if (t.Arity == 0 || t.IsUnboundGenericType) return null;
        if (t.TypeArguments.Length != t.Arity || EffectiveTypeArguments(t).Count != t.Arity) return null;
        var def = ManifestTypeName(t);
        if (string.IsNullOrEmpty(def)) return null;

        var parts = new List<string> { "\"" + def + "\"" };
        foreach (var arg in t.TypeArguments)
        {
            if (arg is not INamedTypeSymbol named) return null;      // a type PARAMETER or an array
            if (ManifestConstructedSpec(named) is { } nested) { parts.Add(nested); continue; }
            var argName = ManifestTypeName(named);
            if (string.IsNullOrEmpty(argName) || named.Arity > 0) return null;
            parts.Add("\"" + argName + "\"");
        }
        return "[" + string.Join(", ", parts) + "]";
    }

    /// <summary>Removes the four-space indent a type body carries from being emitted inside a
    /// <c>Transpose.assembly(...)</c> wrapper — a chunk has no wrapper.</summary>
    private static string Dedent(string js)
    {
        var lines = js.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            if (lines[i].StartsWith("    ", StringComparison.Ordinal)) lines[i] = lines[i].Substring(4);
        return string.Join("\n", lines);
    }

    /// <summary>
    /// An ES module specifier from one site-relative file to another. Both may sit in different
    /// per-assembly chunk folders (a consumer importing a library's chunk), so this walks up with
    /// <c>../</c> as needed. Always explicitly relative — a bare name would be a bare specifier,
    /// which the browser resolves through the import map rather than as a path.
    /// </summary>
    internal static string RelativeImport(string from, string to)
    {
        var fromParts = from.Split('/');
        var toParts = to.Split('/');
        var common = 0;
        while (common < fromParts.Length - 1 && common < toParts.Length - 1
               && string.Equals(fromParts[common], toParts[common], StringComparison.Ordinal)) common++;
        var up = fromParts.Length - 1 - common;
        var prefix = up == 0 ? "./" : string.Concat(Enumerable.Repeat("../", up));
        return prefix + string.Join("/", toParts.Skip(common));
    }
}

internal static class ModuleOutputExtensions
{
    public static Emitter.ModuleOutput With(this Emitter.ModuleOutput output, List<(string, string)> chunks)
    {
        foreach (var c in chunks) output.Chunks.Add(c);
        return output;
    }
}
