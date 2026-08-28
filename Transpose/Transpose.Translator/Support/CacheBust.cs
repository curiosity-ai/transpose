using System;

namespace Transpose.Translator;

/// <summary>
/// The cache-busting token a build stamps into its JavaScript, so that a page rebuilt and
/// redeployed never serves a stale copy of a file <c>Transpose.Require</c> fetches at run time.
///
/// <para>
/// A vendored bundle, a stylesheet or a lazily-loaded library is fetched by a URL the compiler does
/// not version — <c>assets/js/graph-kit.min.js</c> is the same URL after the file behind it changed —
/// so a browser or a CDN that cached the previous copy has no reason to ask for it again. Every build
/// therefore mints one token and every URL <c>Require</c> injects carries it as a query
/// (<c>graph-kit.min.js?<em>token</em></c>): unchanged after a build that changed nothing, different
/// after one that did.
/// </para>
///
/// <para>
/// The token is <b>monotonic</b> — a fixed-width base-36 millisecond stamp followed by three random
/// characters — because a page can carry several assemblies, each stamped when *it* was built, and the
/// runtime keeps the greatest one it is given (see <c>Resources/Require.js</c>). That makes the newest
/// build on the page win whatever order the bundles happen to load in, which a plain random value
/// could not do.
/// </para>
/// </summary>
public static class CacheBust
{
    /// <summary>
    /// Pins the token (any value) or turns cache-busting off entirely (empty / <c>0</c> / <c>none</c>).
    /// A build whose output has to be byte-identical to another one — the reproducibility gate the
    /// performance work leans on, a diff against a baseline compiler — sets this.
    /// </summary>
    public const string EnvVar = "TRANSPOSE_CACHE_BUST";

    /// <summary>Base-36 milliseconds are counted from here, so eight digits carry the stamp until
    /// well past 2100 and every token this compiler mints is the same width (which is what makes
    /// "greater string" mean "newer build").</summary>
    private static readonly DateTime Epoch = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";

    /// <summary>
    /// The token for one build: the environment override when there is one, else a fresh
    /// <c>&lt;8 base-36 digits of the build time&gt;&lt;3 random&gt;</c> id. Empty means "do not bust".
    /// </summary>
    public static string NewToken()
    {
        var pinned = Environment.GetEnvironmentVariable(EnvVar);
        if (pinned is not null)
        {
            pinned = pinned.Trim();
            if (pinned.Length == 0 || pinned == "0" || string.Equals(pinned, "none", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pinned, "false", StringComparison.OrdinalIgnoreCase))
                return "";
            return Sanitize(pinned);
        }

        var ms = (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;
        if (ms < 0) ms = 0;
        return Base36(ms, 8) + Base36(Random.Shared.NextInt64(0, 36 * 36 * 36), 3);
    }

    private static string Base36(long value, int width)
    {
        var buffer = new char[width];
        for (var i = width - 1; i >= 0; i--)
        {
            buffer[i] = Digits[(int)(value % 36)];
            value /= 36;
        }
        return new string(buffer);
    }

    /// <summary>A pinned token goes into a URL's query and into a JS string literal, so keep it to
    /// characters that need no escaping in either.</summary>
    private static string Sanitize(string token)
    {
        var buffer = new char[token.Length];
        var n = 0;
        foreach (var c in token)
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_') buffer[n++] = c;
        return new string(buffer, 0, n);
    }
}
