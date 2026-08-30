using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Transpose.Translator;

/// <summary>
/// The vocabulary of an emitted ES module's import block — shared by the emitter that writes one and
/// the linker that finalises it when a site is assembled (<c>ModuleLinker</c> in Transpose.Compiler.Core).
///
/// <para>A chunk's imports come in two shapes. An import of a chunk of the <em>same</em> assembly is a
/// real relative path, decided and final at emit time. A reference into <em>another</em> assembly is a
/// <b>type placeholder</b> — <c>import "tps-type:tss.UI";</c> — naming the type rather than the file
/// that happens to hold it today.</para>
///
/// <para>The placeholder exists because a chunk file name is the hash of its own text, so it moves
/// whenever the library is rebuilt. A package that wrote its dependency's file names into its own
/// chunks would go stale the moment that dependency shipped a new version — the imports would point at
/// chunks that no longer exist — even though nothing about the package itself changed. Naming the type
/// is version-independent: the site build knows which chunk of which build of the library defines it
/// (every module-mode package publishes that map as <c>Transpose.Modules.json</c>) and rewrites the
/// placeholder into a path on the way into the site.</para>
/// </summary>
public static class ModuleSpecifier
{
    /// <summary>Marks an import specifier as a reference to a TYPE in another assembly rather than a
    /// path. Never reaches a browser: the site build rewrites every one of these into a relative path,
    /// or drops it when nothing in the site defines the type (the library was consumed as a single
    /// bundle, so its code is already there).</summary>
    public const string TypePrefix = "tps-type:";

    /// <summary>The placeholder specifier for a type, by its emitted define name — the same key a
    /// package's chunk map is keyed by.</summary>
    public static string ForType(string defineName) => TypePrefix + defineName;

    /// <summary>The define name behind a placeholder specifier, or null when it is an ordinary path.</summary>
    public static string? TypeOf(string specifier)
        => specifier.StartsWith(TypePrefix, StringComparison.Ordinal) ? specifier.Substring(TypePrefix.Length) : null;

    /// <summary>
    /// An ES module specifier from one site-relative file to another. Both may sit in different
    /// per-assembly chunk folders (a consumer importing a library's chunk), so this walks up with
    /// <c>../</c> as needed. Always explicitly relative — a bare name would be a bare specifier,
    /// which the browser resolves through the import map rather than as a path.
    /// </summary>
    public static string Relative(string from, string to)
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

    /// <summary>The site-relative file a relative specifier written in <paramref name="from"/> points
    /// at — the inverse of <see cref="Relative"/>. Returns null for a specifier that is not a path
    /// (a placeholder, a bare specifier) or that climbs above the site root.</summary>
    public static string? Resolve(string from, string specifier)
    {
        if (specifier.Length == 0 || specifier[0] != '.') return null;
        var parts = new List<string>(from.Split('/'));
        parts.RemoveAt(parts.Count - 1);                       // the importing file itself
        foreach (var seg in specifier.Split('/'))
        {
            if (seg is "." or "") continue;
            if (seg == "..")
            {
                if (parts.Count == 0) return null;
                parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(seg);
        }
        return string.Join("/", parts);
    }

    /// <summary>One side-effect <c>import</c> at the top of an emitted module, with the spans needed to
    /// rewrite its specifier in place or to cut the whole statement out.</summary>
    public readonly record struct Import(string Specifier, int Start, int End, int SpecifierStart, int SpecifierLength);

    /// <summary>
    /// The leading side-effect imports of an emitted module, in order.
    ///
    /// Deliberately reads only the <em>leading</em> block and stops at the first statement that is not
    /// one: everything the emitter writes puts its imports first, and stopping there means no string
    /// literal in the body can ever be mistaken for an import. Whitespace and comments (the entry
    /// module opens with a banner) are skipped. Works on minified text as well as formatted, which
    /// matters because a package's chunks are embedded already minified.
    /// </summary>
    public static List<Import> ReadLeading(string js)
    {
        var found = new List<Import>();
        var i = 0;
        while (true)
        {
            i = SkipTrivia(js, i);
            if (i + 6 > js.Length || string.CompareOrdinal(js, i, "import", 0, 6) != 0) return found;

            var start = i;
            var j = SkipTrivia(js, i + 6);
            if (j >= js.Length || (js[j] != '"' && js[j] != '\'')) return found;    // `import x from …` — not ours
            var quote = js[j];
            var specStart = j + 1;
            var specEnd = js.IndexOf(quote, specStart);
            if (specEnd < 0) return found;

            var end = SkipTrivia(js, specEnd + 1);
            if (end < js.Length && js[end] == ';') end++;

            found.Add(new Import(js.Substring(specStart, specEnd - specStart), start, end, specStart, specEnd - specStart));
            i = end;
        }
    }

    private static int SkipTrivia(string js, int i)
    {
        while (i < js.Length)
        {
            if (char.IsWhiteSpace(js[i])) { i++; continue; }
            if (js[i] == '/' && i + 1 < js.Length && js[i + 1] == '*')
            {
                var close = js.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (close < 0) return js.Length;
                i = close + 2;
                continue;
            }
            if (js[i] == '/' && i + 1 < js.Length && js[i + 1] == '/')
            {
                var nl = js.IndexOf('\n', i);
                if (nl < 0) return js.Length;
                i = nl + 1;
                continue;
            }
            return i;
        }
        return i;
    }

    /// <summary>
    /// Rewrites the leading import block: <paramref name="resolve"/> maps each specifier to the one it
    /// should carry in the site, or to null for an import that has to go away entirely (nothing in the
    /// site defines the type it names). A specifier that resolves to one already emitted is dropped as
    /// a duplicate — several types of one library commonly land in one chunk.
    ///
    /// Every specifier that keeps its text leaves the file byte-identical, which is what lets an
    /// unlinked module (an older package with real paths already in it) pass through untouched.
    /// </summary>
    public static string RewriteLeadingImports(string js, Func<string, string?> resolve)
    {
        var imports = ReadLeading(js);
        if (imports.Count == 0) return js;

        var sb = new StringBuilder(js.Length);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var pos = 0;
        foreach (var import in imports)
        {
            var to = resolve(import.Specifier);
            if (to is not null && emitted.Add(to))
            {
                sb.Append(js, pos, import.SpecifierStart - pos).Append(to);
                pos = import.SpecifierStart + import.SpecifierLength;
                continue;
            }
            // Cut the statement, and the newline after it, so a formatted file does not grow a blank line.
            sb.Append(js, pos, import.Start - pos);
            pos = import.End;
            if (pos < js.Length && js[pos] == '\r') pos++;
            if (pos < js.Length && js[pos] == '\n') pos++;
        }
        sb.Append(js, pos, js.Length - pos);
        return sb.ToString();
    }
}
