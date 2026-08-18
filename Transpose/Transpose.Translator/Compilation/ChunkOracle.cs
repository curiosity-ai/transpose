using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Transpose.Translator;

/// <summary>
/// <c>tps.chunks.json</c> — measured evidence of which types are used together, fed back into the
/// chunker.
///
/// The chunker knows the reference graph, which tells it what <em>can</em> be reached, and it
/// approximates what is <em>actually</em> fetched together with the chunk's load signature: the set
/// of roots that reach it. That approximation is as good as static analysis gets, and it is still
/// only an approximation — a screen that shows a chart and a table pulls two subtrees a reference
/// graph has no reason to associate, while a facade's reachable set spans screens nobody visits in
/// one session.
///
/// A run of the real application knows the answer exactly: it fetched these chunks, in this order,
/// on this screen. This file is how that observation comes back. Each group names the types seen
/// together at one step of a capture (a route, a view, boot), and the chunker treats membership as
/// an extra dimension of the load signature — so two chunks the application always fetches together
/// end up in one chunk, and two it never does stay apart even when the graph cannot tell them apart.
///
/// <code>
/// {
///   "version": 1,
///   "groups": [
///     { "name": "boot",      "eager": true, "types": [ "tss.UI", "tss.Stack", … ] },
///     { "name": "#/search",                 "types": [ "Mosaik.Components.SearchView", … ] }
///   ]
/// }
/// </code>
///
/// <b>Parsing never fails.</b> This file is generated from a running application and checked in
/// beside a codebase that keeps moving: a type gets renamed, a group is written by an older capture,
/// someone hand-edits it and drops a comma. None of that may break a build — the file is an
/// optimization hint, and a build that cannot read it must produce a correct site, just a
/// less-well-packed one. So a malformed file yields <see cref="Empty"/>, and a name that matches no
/// emitted type is skipped silently.
/// </summary>
public sealed class ChunkOracle
{
    /// <summary>One captured step: the emitted type names observed together.</summary>
    /// <param name="Name">Human-readable label — the route or view the capture was on. Diagnostic
    /// only; nothing keys off it.</param>
    /// <param name="Eager">The types are needed before the application is usable at all, so a
    /// <em>package</em> build puts them in its initial payload. A library otherwise has no way to
    /// know which of its chunks a consumer needs at start-up, which is the single biggest source of
    /// over-fetch in a chunked package.</param>
    /// <param name="Types">Emitted define names (<c>tss.UI</c>, <c>Mosaik.Components.HomeView</c>) —
    /// the names the runtime and the chunk map use, which is what a capture can observe.</param>
    public readonly record struct Group(string Name, bool Eager, IReadOnlyList<string> Types);

    public static readonly ChunkOracle Empty = new(Array.Empty<Group>());

    private ChunkOracle(IReadOnlyList<Group> groups) => Groups = groups;

    public IReadOnlyList<Group> Groups { get; }

    public bool IsEmpty => Groups.Count == 0;

    public const string FileName = "tps.chunks.json";

    /// <summary>Reads <c>tps.chunks.json</c> from <paramref name="projectDir"/>. Returns
    /// <see cref="Empty"/> when the file is absent, unreadable, or not in a shape this understands —
    /// see the type remarks for why that is never an error.</summary>
    public static ChunkOracle TryLoad(string projectDir)
    {
        try
        {
            var path = Path.Combine(projectDir, FileName);
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
        }
        catch { return Empty; }
    }

    /// <summary>Parses the file's text. Every step is defensive on purpose: an entry of the wrong
    /// JSON kind is skipped rather than rejected, so one stale group cannot cost a build the rest of
    /// the file.</summary>
    public static ChunkOracle Parse(string json)
    {
        try
        {
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            using var doc = JsonDocument.Parse(json, options);
            var root = doc.RootElement;

            // Both shapes are accepted: the documented object with a "groups" array, and a bare
            // array of groups — a generator that writes the simpler one should not be a build break.
            var array = root.ValueKind switch
            {
                JsonValueKind.Array => root,
                JsonValueKind.Object when root.TryGetProperty("groups", out var g) && g.ValueKind == JsonValueKind.Array => g,
                _ => default,
            };
            if (array.ValueKind != JsonValueKind.Array) return Empty;

            var groups = new List<Group>();
            var index = 0;
            foreach (var element in array.EnumerateArray())
            {
                index++;
                var types = new List<string>();
                var name = $"group{index}";
                var eager = false;

                if (element.ValueKind == JsonValueKind.Array)
                {
                    // A bare array of type names is a group with no metadata.
                    types.AddRange(Names(element));
                }
                else if (element.ValueKind == JsonValueKind.Object)
                {
                    if (element.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                        name = n.GetString() ?? name;
                    if (element.TryGetProperty("eager", out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        eager = e.ValueKind == JsonValueKind.True;
                    if (element.TryGetProperty("types", out var t) && t.ValueKind == JsonValueKind.Array)
                        types.AddRange(Names(t));
                }

                if (types.Count > 0) groups.Add(new Group(name, eager, types));
            }
            return groups.Count == 0 ? Empty : new ChunkOracle(groups);
        }
        catch { return Empty; }
    }

    private static IEnumerable<string> Names(JsonElement array)
        => array.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal);
}
