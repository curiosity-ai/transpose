using System;
using System.Threading.Tasks;

namespace Transpose
{
    /// <summary>How <see cref="Require.RequireAsync(RequireKind, string[])"/> loads a URL.</summary>
    [Enum(Emit.Value)]
    public enum RequireKind
    {
        /// <summary>Picked from the URL: <c>.css</c> is a stylesheet, <c>.mjs</c> a module, anything
        /// else a classic script. This is what nearly every call wants.</summary>
        Auto = 0,

        /// <summary>A classic <c>&lt;script&gt;</c>.</summary>
        Script = 1,

        /// <summary>A <c>&lt;script type="module"&gt;</c>. Needed for a module whose name does not say
        /// so — a Transpose module entry keeps the bundle's <c>.js</c> name, because that is the name
        /// the application fetches it by.</summary>
        Module = 2,

        /// <summary>A <c>&lt;link rel="stylesheet"&gt;</c>.</summary>
        Style = 3,
    }

    /// <summary>
    /// Fetches a script, an ES module or a stylesheet at run time — the one loader for everything a
    /// page pulls in after it has started.
    ///
    /// <para>
    /// It picks the element from the URL (see <see cref="RequireKind"/>), resolves the URL against
    /// the document's base first — so <c>assets/x.js</c>, <c>./assets/x.js</c> and the absolute form
    /// are one entry, and a file <c>index.html</c> already carries is waited on rather than fetched
    /// again — and shares one fetch between every caller that asks for the same file. A load that
    /// failed is forgotten, so a later caller retries instead of inheriting the failure.
    /// </para>
    ///
    /// <para>
    /// It also falls back between the <c>.js</c> / <c>.min.js</c> (and <c>.css</c> / <c>.min.css</c>)
    /// spellings of the same file: a site keeps whichever variant its own build called for, and a
    /// library published once cannot know which build is consuming it, so asking for either name
    /// works. Only if both spellings fail does the task fault, reporting the URL that was asked for.
    /// </para>
    ///
    /// <para>
    /// Several URLs load <em>in order</em>: a plugin that extends a library has to arrive after it
    /// (<c>d3.js</c> then <c>d3.lasso.js</c>), which is the common case. Ask in separate calls, and
    /// await them together, when they are genuinely independent.
    /// </para>
    ///
    /// <para>
    /// Every URL it fetches carries the build's cache-busting token as a query —
    /// <c>my-library.min.js?1q9v3k2abc</c> — so a page that has been rebuilt and redeployed never
    /// serves the copy a browser or a CDN kept of a file whose <em>name</em> did not change. The
    /// compiler stamps one token per build into each assembly it emits and the newest one on the page
    /// wins, so it moves when a build moves and stands still when nothing was rebuilt. Set
    /// <c>TRANSPOSE_CACHE_BUST</c> to pin the token, or to empty to switch this off, when a build's
    /// output has to be byte-identical to another's.
    /// </para>
    ///
    /// <para>
    /// The token is added where the element is created and nowhere else, so it changes nothing about
    /// a URL's identity: three spellings of one file are still one fetch,
    /// <see cref="IsLoaded(string)"/> answers about the URL as it was asked for, and a file
    /// <c>index.html</c> already carries — written there without a token — is still recognised rather
    /// than fetched a second time.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// await Require.RequireAsync("assets/js/graph-kit.min.js");           // .js is tried if it 404s
    /// await Require.RequireAsync("assets/css/fullcalendar.css", "assets/js/fullcalendar.js");
    /// await Require.RequireAsync(RequireKind.Module, "./pdf.mjs");
    /// </code>
    /// </example>
    [External]
    [Name("Transpose.Require")]
    public static class Require
    {
        /// <summary>
        /// Loads <paramref name="urls"/> in order, each as its extension says, and completes when the
        /// last one has run.
        /// </summary>
        [Template("System.Threading.Tasks.Task.fromPromise(Transpose.Require.loadAsync({urls:array}, 0), 0, System.Exception.create)")]
        public static extern Task RequireAsync(params string[] urls);

        /// <summary>
        /// Loads <paramref name="urls"/> in order as <paramref name="kind"/>, for the file whose
        /// extension does not say what it is — a module entry named <c>.js</c>, a stylesheet served
        /// from an extensionless endpoint.
        /// </summary>
        [Template("System.Threading.Tasks.Task.fromPromise(Transpose.Require.loadAsync({urls:array}, {kind}), 0, System.Exception.create)")]
        public static extern Task RequireAsync(RequireKind kind, params string[] urls);

        /// <summary>
        /// True once <paramref name="url"/> is on the page — loaded through here, or scripted by
        /// <c>index.html</c> before anything asked for it. Awaiting <see cref="RequireAsync(string[])"/>
        /// again is free, so this is for a caller that wants to branch rather than wait.
        /// </summary>
        [Template("Transpose.Require.isLoaded({url})")]
        public static extern bool IsLoaded(string url);
    }
}
