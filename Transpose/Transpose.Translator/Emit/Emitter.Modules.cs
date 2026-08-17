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
    public ModuleOutput EmitModules(string chunkDirectory = "chunks")
    {
        var types = PhaseTimings.Measure("  ├ collect + order types", CollectTypes);
        var order = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);
        for (var i = 0; i < types.Count; i++) order[types[i]] = i;

        // Emit every type once, recording what it references as we go.
        var bodies = new ConcurrentDictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default);
        var refs = new ConcurrentDictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        try
        {
            PhaseTimings.Measure("  ├ emit type bodies (parallel)", () =>
                Parallel.ForEach(types, type =>
                {
                    var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                    bodies[type] = EmitOnlyType(this, type, seen).ToString();
                    seen.Remove(type);
                    refs[type] = seen;
                }));
        }
        catch (AggregateException ex) when (ex.Flatten().InnerExceptions.OfType<TranslationException>().FirstOrDefault() is { } te)
        {
            throw te;
        }

        var chunks = PhaseTimings.Measure("  ├ chunk (strongly-connected components)",
            () => Chunk(types, refs, order));

        // Eager set: the chunk holding the entry point, plus every chunk it transitively references.
        var entry = _compilation.GetEntryPoint(default)?.ContainingType?.OriginalDefinition as INamedTypeSymbol;
        var eager = new HashSet<int>();
        if (entry is not null && chunks.IndexOf.TryGetValue(entry, out var entryChunk))
        {
            var stack = new Stack<int>();
            eager.Add(entryChunk);
            stack.Push(entryChunk);
            while (stack.Count > 0)
                foreach (var d in chunks.Deps[stack.Pop()])
                    if (eager.Add(d)) stack.Push(d);
        }
        else
        {
            for (var i = 0; i < chunks.Members.Count; i++) eager.Add(i);
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

        return new ModuleOutput
        {
            EntryJs = BuildEntryModule(types, chunks, eager, lazyTypes, ChunkFile),
            EagerChunkCount = eager.Count,
            LazyChunkCount = chunks.Members.Count - eager.Count,
            LazyTypeCount = lazyTypes.Count,
        }.With(output);
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

    /// <summary>The entry module: it imports the eager chunks (so they are fully evaluated before
    /// its own body runs), then declares what was deferred, attaches the reflection metadata for
    /// <em>every</em> type, and starts the runtime.</summary>
    private string BuildEntryModule(
        List<INamedTypeSymbol> types, ChunkGraph chunks, HashSet<int> eager,
        List<INamedTypeSymbol> lazyTypes, Func<int, string> chunkFile)
    {
        var sb = new StringBuilder();
        sb.Append("/**\n * Transpose.Translator generated output (module entry).\n */\n");
        foreach (var i in eager.OrderBy(x => x))
            sb.Append("import './").Append(chunkFile(i)).Append("';\n");
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
                var bases = ManifestBaseNames(t);
                sb.Append("    \"").Append(MetaTypeDefName(t)).Append("\": { m: \"./")
                  .Append(chunkFile(chunks.IndexOf[t])).Append("\", k: \"").Append(ManifestKind(t))
                  .Append("\", a: \"").Append(_assemblyName).Append("\", i: [")
                  .Append(string.Join(", ", bases.Select(b => "\"" + b + "\""))).Append("] },\n");
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
    /// The base class and interfaces a stub reports, as dotted names the runtime resolves with
    /// <c>Transpose.unroll</c>. Names rather than expressions because a base may itself still be a
    /// stub when the manifest is registered — only names can be resolved after every stub exists.
    /// A constructed generic base contributes its definition name (<c>Foo$1</c>); that is all an
    /// unrolled lookup can express, and it is enough for the common interface-scan case.
    /// </summary>
    private List<string> ManifestBaseNames(INamedTypeSymbol type)
    {
        var names = new List<string>();
        void Add(INamedTypeSymbol t)
        {
            var n = TransposeNaming.IsTransposeCompiledSource(t)
                ? (t.Arity > 0 ? _names.TypeFullName(t) + "$" + t.Arity : _names.TypeFullName(t))
                : TransposeNaming.GetName(t);
            if (!string.IsNullOrEmpty(n) && !names.Contains(n!)) names.Add(n!);
        }
        if (type.BaseType is { } bt && bt.SpecialType != SpecialType.System_Object) Add(bt);
        foreach (var i in type.AllInterfaces.Where(TransposeNaming.IsInheritableInterface)) Add(i);
        return names;
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

    /// <summary>A relative specifier from one chunk file to another. Both live in the same folder in
    /// every layout emitted today, so this is just <c>./name</c>, but it is computed rather than
    /// assumed so a nested chunk directory keeps working.</summary>
    private static string RelativeImport(string from, string to)
    {
        var fromDir = from.Contains('/') ? from.Substring(0, from.LastIndexOf('/')) : "";
        var toDir = to.Contains('/') ? to.Substring(0, to.LastIndexOf('/')) : "";
        return fromDir == toDir ? "./" + to.Substring(toDir.Length == 0 ? 0 : toDir.Length + 1) : "./" + to;
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
