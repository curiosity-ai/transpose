using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Transpose.Translator;

/// <summary>
/// The second chunking pass: coalescing the strongly-connected components into chunks that are
/// worth fetching.
///
/// The first pass (<c>Emitter.Modules.cs</c>) produces the <em>smallest sound</em> unit — one chunk
/// per SCC of the reference graph. On a real library that is far too fine: Tesserae came out at 682
/// chunks with a median of 2.2 KB, and half of them under 1 KB. Every one of those is a separate
/// HTTP request, a separate module record, and a separate compression context, so a sample that
/// needs twenty small types pays twenty round trips to fetch 30 KB.
///
/// This pass merges them back up to a target size band (50–100 KB by default) <b>without</b> making
/// anything load that would not have loaded anyway, where it can. The rule it merges by is the one
/// the request asks for — <em>what is used together</em>:
///
/// <list type="number">
/// <item><b>Load signature.</b> A chunk with no importer is a <em>root</em>: nothing pulls it in, so
/// it is only ever fetched because the application asked for it (<c>Modules.LoadAsync</c>). Every
/// other chunk is fetched exactly when one of the roots that reaches it is. So the set of roots that
/// reach a chunk <em>is</em> its load condition, and two chunks with the same set are always
/// fetched together — merging them costs nothing at all.</item>
/// <item><b>Ordering.</b> Classes of equal signature are emitted in a reverse-topological order of
/// the class graph, choosing at each step the ready class most similar (Jaccard over the root sets)
/// to the one just emitted, so classes that <em>nearly</em> always load together end up adjacent.</item>
/// <item><b>Bucketing.</b> The resulting sequence is cut into contiguous buckets of
/// <c>min…max</c> bytes. A bucket only spans a class boundary while it is still under the minimum,
/// which is where the over-fetch is: below the minimum a chunk is not worth a request of its own,
/// and its neighbour in the order above is the least-bad thing to pay for.</item>
/// </list>
///
/// <b>Sizes are exact, not estimated.</b> The type bodies are emitted before chunking (chunking
/// needs the reference graph the emit records), so the byte count of every chunk is already known —
/// no complexity heuristic and no emit-measure-regroup round trip.
///
/// <b>Why the result is still a DAG.</b> Chunk evaluation order has to be sound —
/// <c>Transpose.define</c> resolves <c>inherits</c> eagerly, so a cycle between two chunks is
/// exactly the failure the SCC pass exists to prevent, and merging is the one operation that could
/// reintroduce one (merging A and C when A → B → C makes the merged node and B import each other).
/// Two properties rule that out by construction, so no cycle check is needed:
/// <list type="bullet">
/// <item>If chunk <c>i</c> depends on chunk <c>d</c>, then every root that reaches <c>i</c> reaches
/// <c>d</c>: <c>sig(d) ⊇ sig(i)</c>. So a cycle among signature classes would force all of them
/// equal — i.e. one class — and the class graph is acyclic. Emitting classes in reverse-topological
/// order therefore places every dependency before its dependent.</item>
/// <item>Within a class, and within the eager group, members are emitted in the first pass's own
/// index order, which is already topological (a dependency always has a lower index). A contiguous
/// run of a topological order can never contain an edge pointing forward.</item>
/// </list>
/// Both together mean every edge of the merged graph points at a bucket with a lower-or-equal index,
/// which is the invariant the emitter already relies on for deterministic file names. It is
/// re-checked before the merged graph is returned, and a violation falls back to the unmerged one
/// rather than emitting a site that cannot evaluate.
///
/// <b>The eager group is never mixed with the lazy one.</b> The eager set is closed under
/// dependencies, so merging an eager chunk with a lazy one would move the lazy code into the initial
/// payload — the opposite of the point. Eager chunks are bucketed first, on their own, purely by
/// size: they all load anyway, so there is no over-fetch to trade against and packing them as tightly
/// as the maximum allows is strictly better.
/// </summary>
public sealed partial class Emitter
{
    /// <summary>Below this, a chunk is not worth a request of its own and is merged with its
    /// neighbour in the load order. 0 disables the pass entirely (one chunk per SCC).</summary>
    public const int DefaultMinChunkBytes = 50 * 1024;

    /// <summary>A bucket stops accepting chunks once adding one would take it past this. A single
    /// SCC larger than this is not split — an SCC is atomic.</summary>
    public const int DefaultMaxChunkBytes = 100 * 1024;

    /// <summary>
    /// Merges the SCC chunks into buckets of <paramref name="minBytes"/>–<paramref name="maxBytes"/>,
    /// grouping by what is loaded together. See the type remarks for the algorithm and why the
    /// result is still a DAG. Returns the input unchanged when the pass is disabled, when there is
    /// nothing to merge, or when the safety re-check fails.
    /// </summary>
    /// <param name="sizes">Exact emitted bytes per chunk.</param>
    /// <param name="eager">Chunks the entry module imports. Never merged with the rest.</param>
    /// <param name="order">The emitter's dependency-depth ordering, which decides the order of types
    /// inside a merged chunk — it already guarantees a type's bases are defined before it.</param>
    /// <param name="oracleBits">Per chunk, the <c>tps.chunks.json</c> groups it belongs to, already
    /// propagated down the dependency edges. Null when there is no oracle. See
    /// <see cref="OracleBits"/> for what it means and why it is safe to fold into the signature.</param>
    private static ChunkGraph Coalesce(
        ChunkGraph chunks,
        IReadOnlyList<int> sizes,
        HashSet<int> eager,
        Dictionary<INamedTypeSymbol, int> order,
        int minBytes,
        int maxBytes,
        out HashSet<int> coalescedEager,
        ulong[][]? oracleBits = null)
    {
        coalescedEager = eager;
        var n = chunks.Members.Count;
        if (minBytes <= 0 || n <= 1) return chunks;
        if (maxBytes < minBytes) maxBytes = minBytes;

        // Everything below reads the first pass's index order as a topological order. It is one by
        // construction (Tarjan emits a component only after every component it points at), but the
        // whole pass is unsound if it ever stops being one, so it is checked rather than assumed.
        for (var i = 0; i < n; i++)
            foreach (var d in chunks.Deps[i])
                if (d >= i) return chunks;

        // --- 1. roots and load signatures ------------------------------------------------------
        // A root is a lazy chunk nothing imports: it is fetched only when the application asks for
        // it. (A lazy chunk can only be imported by another lazy chunk — if an eager one imported
        // it, the eager closure would have made it eager too.)
        var indegree = new int[n];
        for (var i = 0; i < n; i++)
            foreach (var d in chunks.Deps[i]) indegree[d]++;

        var rootId = new int[n];
        Array.Fill(rootId, -1);
        var rootCount = 0;
        for (var i = 0; i < n; i++)
            if (!eager.Contains(i) && indegree[i] == 0) rootId[i] = rootCount++;

        // sig[i] = the roots that reach chunk i. Dependencies have lower indices, so one descending
        // sweep is a complete propagation: when i is read, every chunk that could contribute to it
        // has already been read.
        var words = Math.Max(1, (rootCount + 63) / 64);
        // A measured oracle contributes extra bits of the same kind: "the running application
        // fetched this chunk on screen X" is a load condition exactly like "root R reaches it", and
        // OracleBits has already propagated it down the dependency edges so the sig(d) ⊇ sig(i)
        // property the whole pass rests on still holds. Appending them therefore only *refines* the
        // partition — it can split a signature class, never merge two — so the acyclicity argument in
        // the type remarks carries over unchanged.
        var oracleWords = oracleBits is null ? 0 : oracleBits[0].Length;
        var total = words + oracleWords;
        var sig = new ulong[n][];
        for (var i = 0; i < n; i++)
        {
            sig[i] = new ulong[total];
            if (rootId[i] >= 0) sig[i][rootId[i] >> 6] |= 1UL << (rootId[i] & 63);
            if (oracleBits is not null)
                Array.Copy(oracleBits[i], 0, sig[i], words, oracleWords);
        }
        for (var i = n - 1; i >= 0; i--)
            foreach (var d in chunks.Deps[i])
                for (var w = 0; w < words; w++) sig[d][w] |= sig[i][w];

        // --- 2. one class per distinct signature (lazy chunks only) ----------------------------
        var classOf = new int[n];
        Array.Fill(classOf, -1);
        var classSig = new List<ulong[]>();
        var classMembers = new List<List<int>>();
        var byKey = new Dictionary<ulong[], int>(BitsComparer.Instance);
        for (var i = 0; i < n; i++)
        {
            if (eager.Contains(i)) continue;
            if (!byKey.TryGetValue(sig[i], out var c))
            {
                c = classSig.Count;
                byKey[sig[i]] = c;
                classSig.Add(sig[i]);
                classMembers.Add(new List<int>());
            }
            classOf[i] = c;
            classMembers[c].Add(i);      // ascending, because i ascends
        }

        // A class whose signature carries oracle bits is one the running application was *measured*
        // fetching together. That is a stronger statement than anything the reference graph can make,
        // so it outranks the size band: see Take.
        var classMeasured = new bool[classSig.Count];
        if (oracleWords > 0)
        {
            for (var c = 0; c < classSig.Count; c++)
                for (var w = words; w < total; w++)
                    if (classSig[c][w] != 0) { classMeasured[c] = true; break; }
        }

        // --- 3. order the classes ---------------------------------------------------------------
        var classOrder = OrderClasses(chunks, classOf, classSig, classMembers);
        if (classOrder is null) return chunks;

        // --- 4. cut the sequence into buckets ---------------------------------------------------
        var buckets = new List<List<int>>();
        var current = new List<int>();
        var currentBytes = 0;
        var currentClass = int.MinValue;

        void Flush()
        {
            if (current.Count == 0) return;
            buckets.Add(current);
            current = new List<int>();
            currentBytes = 0;
        }

        void Take(int chunk, int cls)
        {
            // A measured group is never cut. maxChunkSize is a guess about what makes a request worth
            // sending; a tps.chunks.json group is an observation that the application fetched these
            // together, so splitting one to respect the band hands back exactly the extra requests the
            // measurement existed to remove. The group's own size is the answer, whatever it is.
            var keepTogether = cls >= 0 && cls == currentClass && classMeasured[cls];

            // Otherwise: start a new bucket when this one is full, or when the load condition changes
            // and the bucket already earns its request. Crossing a class boundary under the minimum is
            // the deliberate over-fetch: it is what buys the size band.
            if (current.Count > 0 && ((currentBytes + sizes[chunk] > maxBytes && !keepTogether)
                                      || (cls != currentClass && currentBytes >= minBytes)))
                Flush();
            current.Add(chunk);
            currentBytes += sizes[chunk];
            currentClass = cls;
        }

        for (var i = 0; i < n; i++)
            if (eager.Contains(i)) Take(i, -1);
        Flush();
        var eagerBuckets = buckets.Count;

        foreach (var c in classOrder)
            foreach (var i in classMembers[c]) Take(i, c);
        Flush();

        // --- 5. rebuild the graph ----------------------------------------------------------------
        var bucketOf = new int[n];
        Array.Fill(bucketOf, -1);
        var members = new List<List<INamedTypeSymbol>>(buckets.Count);
        var indexOf = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);
        for (var b = 0; b < buckets.Count; b++)
        {
            var list = new List<INamedTypeSymbol>();
            foreach (var i in buckets[b])
            {
                bucketOf[i] = b;
                list.AddRange(chunks.Members[i]);
            }
            // The emitter's dependency-depth ordering is global, so re-sorting the union of several
            // components by it keeps every type's bases ahead of it, exactly as within one SCC.
            list.Sort((a, z) => order[a].CompareTo(order[z]));
            members.Add(list);
            foreach (var t in list) indexOf[t] = b;
        }

        // Every chunk is either eager (bucketed in the first sweep) or a member of exactly one
        // signature class (the second), so this cannot miss one — but a missed chunk would silently
        // vanish from the output rather than fail, which is worth one pass to rule out.
        for (var i = 0; i < n; i++)
            if (bucketOf[i] < 0) return chunks;

        var deps = new List<HashSet<int>>(buckets.Count);
        for (var b = 0; b < buckets.Count; b++) deps.Add(new HashSet<int>());
        for (var i = 0; i < n; i++)
            foreach (var d in chunks.Deps[i])
                if (bucketOf[d] != bucketOf[i]) deps[bucketOf[i]].Add(bucketOf[d]);

        // The invariant the whole emitter rests on (and the reason no cycle check was needed above):
        // every import points at a lower index. If the reasoning above is ever wrong, fall back to
        // the unmerged graph instead of emitting a site whose modules cannot evaluate.
        for (var b = 0; b < deps.Count; b++)
            foreach (var d in deps[b])
                if (d >= b) return chunks;

        coalescedEager = new HashSet<int>(Enumerable.Range(0, eagerBuckets));
        return new ChunkGraph(members, deps, indexOf);
    }

    /// <summary>
    /// Turns a <c>tps.chunks.json</c> oracle into one bit per group per chunk: bit <c>g</c> is set on
    /// chunk <c>i</c> when the application, at capture step <c>g</c>, had a type of <c>i</c> loaded.
    ///
    /// The bits are then propagated <b>down the dependency edges</b> — if step <c>g</c> loaded chunk
    /// <c>i</c>, it necessarily also loaded everything <c>i</c> imports — which is both true of the
    /// run being described and the property <see cref="Coalesce"/> needs: its acyclicity argument
    /// rests on <c>sig(d) ⊇ sig(i)</c> for every edge <c>i → d</c>, and a bit set on <c>i</c> but not
    /// on its dependency would break exactly that.
    ///
    /// Names that match no emitted type are skipped: a checked-in capture goes stale as the code
    /// moves, and a renamed type must cost the oracle one hint, not the build.
    /// Null when there is no oracle to apply, so nothing is allocated on the normal path.
    /// </summary>
    private ulong[][]? OracleBits(ChunkGraph chunks, ChunkOracle? oracle)
    {
        if (oracle is null || oracle.IsEmpty) return null;

        var n = chunks.Members.Count;
        var groups = oracle.Groups;
        var words = (groups.Count + 63) / 64;

        // Emitted define name → chunk. The name is what a capture can see (it is what the chunk map
        // and the runtime registry are keyed by), so it is what the file records.
        var chunkOfName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < n; i++)
            foreach (var t in chunks.Members[i])
            {
                var name = DefineName(t);
                if (!string.IsNullOrEmpty(name)) chunkOfName[name] = i;
            }

        var bits = new ulong[n][];
        for (var i = 0; i < n; i++) bits[i] = new ulong[words];

        var matched = 0;
        for (var g = 0; g < groups.Count; g++)
            foreach (var name in groups[g].Types)
                if (chunkOfName.TryGetValue(name, out var i))
                {
                    bits[i][g >> 6] |= 1UL << (g & 63);
                    matched++;
                }

        if (matched == 0) return null;

        // Deps always have a lower index, so one descending sweep is a complete propagation.
        for (var i = n - 1; i >= 0; i--)
            foreach (var d in chunks.Deps[i])
                for (var w = 0; w < words; w++) bits[d][w] |= bits[i][w];

        return bits;
    }

    /// <summary>
    /// The order the signature classes are laid out in: reverse-topological over the class graph
    /// (so a class is emitted only after everything it depends on), and among the classes that are
    /// ready at each step, the one whose root set most resembles the one just emitted. That second
    /// half is what puts <em>nearly</em>-co-loaded classes next to each other, so that when the
    /// bucketer has to cross a class boundary it crosses into the closest neighbour rather than an
    /// arbitrary one. Null if the class graph is not a DAG, which cannot happen (see the type
    /// remarks) but is not worth being wrong about.
    /// </summary>
    private static List<int>? OrderClasses(
        ChunkGraph chunks, int[] classOf, List<ulong[]> classSig, List<List<int>> classMembers)
    {
        var k = classMembers.Count;
        if (k == 0) return new List<int>();

        var dependsOn = new HashSet<int>[k];
        var dependents = new List<int>[k];
        for (var c = 0; c < k; c++) { dependsOn[c] = new HashSet<int>(); dependents[c] = new List<int>(); }

        for (var i = 0; i < classOf.Length; i++)
        {
            var c = classOf[i];
            if (c < 0) continue;                       // eager: bucketed before any of this
            foreach (var d in chunks.Deps[i])
            {
                var e = classOf[d];
                if (e < 0 || e == c) continue;
                if (dependsOn[c].Add(e)) dependents[e].Add(c);
            }
        }

        // The greedy pick below is quadratic in the class count. That is nothing at the scale this
        // runs at (a few hundred classes), but a pathologically wide project should not pay for it:
        // above the cap, fall back to ordering by root-set size descending, which satisfies the same
        // topological constraint on its own — a dependency's root set is a strict superset of its
        // dependent's, so it is strictly larger.
        if (k > 4096)
            return Enumerable.Range(0, k)
                .OrderByDescending(c => PopCount(classSig[c]))
                .ThenBy(c => classMembers[c][0])
                .ToList();

        var remaining = new int[k];
        var ready = new List<int>();
        for (var c = 0; c < k; c++)
        {
            remaining[c] = dependsOn[c].Count;
            if (remaining[c] == 0) ready.Add(c);
        }

        var result = new List<int>(k);
        ulong[]? previous = null;
        while (ready.Count > 0)
        {
            var pick = -1;
            var best = -1d;
            foreach (var c in ready)
            {
                var score = previous is null ? 0d : Jaccard(previous, classSig[c]);
                if (score > best || (score == best && c < pick)) { best = score; pick = c; }
            }
            ready.Remove(pick);
            result.Add(pick);
            previous = classSig[pick];
            foreach (var p in dependents[pick])
                if (--remaining[p] == 0) ready.Add(p);
        }
        return result.Count == k ? result : null;
    }

    /// <summary>How much two load conditions overlap: |A ∩ B| / |A ∪ B|. 1 means the two classes are
    /// always fetched together and merging them is free; 0 means they never are.</summary>
    private static double Jaccard(ulong[] a, ulong[] b)
    {
        var both = 0; var either = 0;
        for (var w = 0; w < a.Length; w++)
        {
            both += System.Numerics.BitOperations.PopCount(a[w] & b[w]);
            either += System.Numerics.BitOperations.PopCount(a[w] | b[w]);
        }
        return either == 0 ? 0d : (double)both / either;
    }

    private static int PopCount(ulong[] a)
    {
        var total = 0;
        foreach (var w in a) total += System.Numerics.BitOperations.PopCount(w);
        return total;
    }

    /// <summary>Value equality for the root bitsets, so identical load conditions land in one class.</summary>
    private sealed class BitsComparer : IEqualityComparer<ulong[]>
    {
        public static readonly BitsComparer Instance = new();

        public bool Equals(ulong[]? x, ulong[]? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null || x.Length != y.Length) return false;
            for (var i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
            return true;
        }

        public int GetHashCode(ulong[] obj)
        {
            var hash = new HashCode();
            foreach (var w in obj) hash.Add(w);
            return hash.ToHashCode();
        }
    }
}
