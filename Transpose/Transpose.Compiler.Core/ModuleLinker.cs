using System.Security.Cryptography;
using System.Text;
using Transpose.Translator;

namespace Transpose.Compiler;

/// <summary>One module file on its way into a site: where it lands, and its JavaScript.</summary>
internal readonly record struct ModuleFile(string Rel, string Text, bool IsChunk);

/// <summary>
/// Turns the type placeholders an emitted module carries (<c>import "tps-type:tss.UI";</c>) into real
/// relative imports of the chunks that define those types <em>in the site being assembled</em>.
///
/// <para><b>Why the indirection exists.</b> A chunk's file name is the hash of its own text, so every
/// rebuild of a library renames the chunks whose JavaScript changed. A package that had written its
/// dependency's file names into its own chunks would therefore go stale the moment that dependency
/// shipped a new version: <c>Tesserae.GraphKit</c> compiled against Tesserae 1.0 keeps importing
/// <c>../Tesserae/c0f1….mjs</c>, and Tesserae 1.1 no longer has a file by that name. Nothing about
/// GraphKit changed, nothing warns, and the application 404s on the first screen that needs it. So a
/// package names the <em>type</em> it needs — which does not move — and the site build, which can see
/// both sides, resolves it here.</para>
///
/// <para><b>Renaming.</b> Resolving a placeholder changes a file's text, so the file is renamed to the
/// hash of what it now contains. That keeps the property the naming exists for — a chunk's URL
/// identifies its bytes, so it can be served immutably — which resolving-without-renaming would
/// quietly break: two deployments of the same application against different Tesserae versions would
/// otherwise serve different bytes under one name, and a browser holding the first would import a
/// chunk that is no longer there. A rename cascades (an importer's text changes too), which is why
/// chunks are linked in dependency order; a file whose text is unchanged keeps its name.</para>
///
/// <para>Assemblies are linked in dependency order — the order the site writes them in — so by the
/// time a library's placeholders are resolved, every library it depends on has its final names.</para>
/// </summary>
internal sealed class ModuleLinker
{
    // Define name → the site-relative chunk file that defines it, AFTER linking. Accumulated across
    // assemblies, which is why the caller must link in dependency order.
    private readonly Dictionary<string, string> _typeToChunk = new(StringComparer.Ordinal);

    /// <summary>Every type the site can import so far, by emitted define name. A placeholder naming
    /// anything else is dropped rather than resolved — see <see cref="LinkAssembly"/>.</summary>
    public IReadOnlyDictionary<string, string> TypeToChunk => _typeToChunk;

    /// <summary>
    /// Links one assembly's module files. <paramref name="ownTypeToChunk"/> is that assembly's own
    /// published map (<c>Transpose.Modules.json</c>, or the map the current compilation produced);
    /// it is re-keyed through the renames below and folded into <see cref="TypeToChunk"/> so later
    /// assemblies — and this site's own chunks — can import into it.
    ///
    /// Returns the files in the order they were given, each with its final path and text.
    /// </summary>
    public List<ModuleFile> LinkAssembly(
        IReadOnlyDictionary<string, string>? ownTypeToChunk, IReadOnlyList<ModuleFile> files)
    {
        var chunks = files.Where(f => f.IsChunk).ToList();
        var linked = new Dictionary<string, ModuleFile>(StringComparer.Ordinal);   // original rel → linked file
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);      // original leaf → new leaf
        var used = new HashSet<string>(chunks.Select(c => Leaf(c.Rel)), StringComparer.Ordinal);

        // Dependency order: a chunk is linked only once every chunk it imports has its final name, so
        // the rename it may itself trigger is already visible. The emitter guarantees the internal
        // import graph is acyclic (a chunk is an SCC of the reference graph, and the condensation of
        // an SCC graph is a DAG), so a depth-first walk terminates; the visited set guards a package
        // built by some other tool anyway.
        foreach (var chunk in InDependencyOrder(chunks))
        {
            var text = Link(chunk.Rel, chunk.Text, renamed);
            var rel = chunk.Rel;
            if (!string.Equals(text, chunk.Text, StringComparison.Ordinal))
            {
                var leaf = ChunkLeafName(text, used);
                var dir = chunk.Rel.LastIndexOf('/');
                rel = dir < 0 ? leaf : chunk.Rel.Substring(0, dir + 1) + leaf;
                renamed[Leaf(chunk.Rel)] = leaf;
            }
            linked[chunk.Rel] = new ModuleFile(rel, text, IsChunk: true);
        }

        // The entry module last: it imports the eager chunks and names every deferred one in the
        // manifest it registers, so it has to see the finished set of names.
        foreach (var file in files)
        {
            if (file.IsChunk) continue;
            linked[file.Rel] = file with { Text = Link(file.Rel, file.Text, renamed) };
        }

        // First wins, matching the order the site places these assemblies in: two assemblies claiming
        // one emitted type name is a collision the compiler cannot resolve, and the dependency-order
        // reference is the one whose JavaScript loads first.
        if (ownTypeToChunk is not null)
            foreach (var (type, chunkRel) in ownTypeToChunk)
                _typeToChunk.TryAdd(type, linked.TryGetValue(chunkRel, out var f) ? f.Rel : chunkRel);

        return files.Select(f => linked[f.Rel]).ToList();
    }

    /// <summary>The linked form of one module file: placeholders resolved against everything linked so
    /// far, then every reference to a chunk this assembly renamed updated — in the import block and in
    /// the <c>Transpose.Modules.register</c> manifest alike, since both name chunk files.</summary>
    private string Link(string rel, string js, IReadOnlyDictionary<string, string> renamed)
    {
        var linked = ModuleSpecifier.RewriteLeadingImports(js, spec =>
        {
            var type = ModuleSpecifier.TypeOf(spec);
            // Not a placeholder: an import of a sibling chunk, or a real path an older package wrote
            // before placeholders existed. Either way it is already correct; renames are applied below.
            if (type is null) return spec;
            // Nothing in this site defines the type. That is the ordinary outcome for a library the
            // site took as a single bundle — its code is already on the page and there is nothing to
            // import — so the placeholder simply goes away.
            return _typeToChunk.TryGetValue(type, out var file) ? ModuleSpecifier.Relative(rel, file) : null;
        });

        return renamed.Count == 0 ? linked : ApplyRenames(linked, renamed);
    }

    /// <summary>Rewrites every chunk file name this assembly renamed, wherever it appears. Done as one
    /// pass over the text rather than a sequence of replacements, so a chunk renamed <em>to</em> a name
    /// another chunk was renamed <em>from</em> cannot be rewritten twice.</summary>
    private static string ApplyRenames(string js, IReadOnlyDictionary<string, string> renamed)
        => System.Text.RegularExpressions.Regex.Replace(js, @"c[0-9a-f]{16}(?:-\d+)?\.mjs",
            m => renamed.TryGetValue(m.Value, out var to) ? to : m.Value);

    /// <summary>The chunks, ordered so that each comes after every chunk of this assembly it imports.</summary>
    private static List<ModuleFile> InDependencyOrder(List<ModuleFile> chunks)
    {
        var byRel = chunks.ToDictionary(c => c.Rel, StringComparer.Ordinal);
        var ordered = new List<ModuleFile>(chunks.Count);
        var state = new Dictionary<string, int>(StringComparer.Ordinal);   // 1 = visiting, 2 = done

        void Visit(ModuleFile chunk)
        {
            if (state.TryGetValue(chunk.Rel, out var s) && s != 0) return;
            state[chunk.Rel] = 1;
            foreach (var import in ModuleSpecifier.ReadLeading(chunk.Text))
            {
                if (ModuleSpecifier.Resolve(chunk.Rel, import.Specifier) is not { } target) continue;
                if (byRel.TryGetValue(target, out var dep) && state.GetValueOrDefault(dep.Rel) == 0) Visit(dep);
            }
            state[chunk.Rel] = 2;
            ordered.Add(chunk);
        }

        foreach (var chunk in chunks) Visit(chunk);
        return ordered;
    }

    private static string Leaf(string rel)
    {
        var slash = rel.LastIndexOf('/');
        return slash < 0 ? rel : rel.Substring(slash + 1);
    }

    /// <summary>The same content-addressed name the emitter gives a chunk (see
    /// <c>Emitter.ChunkLeafName</c>): <c>c</c> plus the first 16 hex digits of the SHA-256 of the file.
    /// A linked chunk is renamed with it so its URL still identifies its bytes.</summary>
    private static string ChunkLeafName(string js, HashSet<string> used)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(js)).AsSpan(0, 8));
        var name = "c" + hash + ".mjs";
        for (var n = 2; !used.Add(name); n++) name = "c" + hash + "-" + n + ".mjs";
        return name;
    }
}
