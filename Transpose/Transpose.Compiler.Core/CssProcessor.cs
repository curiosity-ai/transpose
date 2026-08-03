using System.Text;

namespace Transpose.Compiler;

/// <summary>
/// Strips <c>/* … */</c> comments from stylesheets. Every CSS file the compiler produces goes through
/// here — the ones a <c>tps.json</c> resource group writes into the site, the ones embedded into a
/// package DLL, and the ones extracted back out of a referenced package — so a shipped site never
/// carries the authoring comments of a stylesheet or of the framework stylesheets it bundles.
///
/// This is a comment pass, not a minifier: whitespace, casing and declaration order are left exactly
/// as authored, and only the comments go. CSS has no line comments, so <c>/* … */</c> is the whole
/// surface. Three rules keep the result equivalent to the input:
///
/// <list type="bullet">
/// <item>A <c>/*</c> inside a string (<c>content: "/* not a comment */"</c>) or inside an unquoted
/// <c>url(…)</c> token is not a comment and is copied through.</item>
/// <item>A comment between two non-whitespace tokens becomes a single space, so <c>a/**/b</c> cannot
/// collapse into <c>ab</c>.</item>
/// <item>A comment that ends its line takes the whitespace around it with it — and the whole line when
/// nothing else was on it — rather than leaving a blank line or a trailing space behind.</item>
/// </list>
///
/// An unterminated comment is consumed to the end of the file, which is what a browser does with it.
/// The one behaviour deliberately given up is the ancient IE5/6 comment hacks
/// (<c>/*\*/…/**/</c>), whose meaning *is* the comment.
/// </summary>
internal static class CssProcessor
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly UTF8Encoding Utf8Strict = new(false, throwOnInvalidBytes: true);
    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    /// <summary>The stylesheet with every comment removed. Returns the very same string when there is
    /// nothing to strip, so a caller can detect "unchanged" by reference.</summary>
    public static string StripComments(string css)
    {
        if (string.IsNullOrEmpty(css) || css.IndexOf("/*", StringComparison.Ordinal) < 0) return css;

        var sb = new StringBuilder(css.Length);
        var i = 0;
        while (i < css.Length)
        {
            var c = css[i];
            if (c == '"' || c == '\'') { i = CopyString(css, i, sb); continue; }
            if ((c == 'u' || c == 'U') && IsUrlToken(css, i)) { i = CopyUrl(css, i, sb); continue; }
            if (c == '/' && i + 1 < css.Length && css[i + 1] == '*') { i = SkipComment(css, i, sb); continue; }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// The same pass over a stylesheet's raw bytes — how a CSS resource reaches the site or a package
    /// DLL. A UTF-8 BOM is preserved, and anything that is not valid UTF-8 (a UTF-16 stylesheet, a
    /// legacy single-byte encoding) is returned untouched rather than re-encoded: a byte-for-byte copy
    /// with its comments intact is always better than a mangled one. The original array is returned
    /// unchanged when there was nothing to strip.
    /// </summary>
    public static byte[] StripComments(byte[] bytes)
    {
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var offset = hasBom ? 3 : 0;

        // A comment-free stylesheet is never decoded at all — UTF-8 is ASCII-transparent, so a `/*`
        // can only be the two bytes themselves, and the common case costs one scan and no allocation.
        if (!ContainsCommentStart(bytes, offset)) return bytes;

        string text;
        try { text = Utf8Strict.GetString(bytes, offset, bytes.Length - offset); }
        catch (DecoderFallbackException) { return bytes; }

        var stripped = StripComments(text);
        if (ReferenceEquals(stripped, text)) return bytes;

        var encoded = Utf8NoBom.GetBytes(stripped);
        if (!hasBom) return encoded;

        var result = new byte[Utf8Bom.Length + encoded.Length];
        Utf8Bom.CopyTo(result, 0);
        encoded.CopyTo(result, Utf8Bom.Length);
        return result;
    }

    /// <summary>Whether the bytes hold a <c>/*</c> anywhere past <paramref name="offset"/>.</summary>
    private static bool ContainsCommentStart(byte[] bytes, int offset)
    {
        for (var i = offset; i + 1 < bytes.Length; i++)
            if (bytes[i] == (byte)'/' && bytes[i + 1] == (byte)'*') return true;
        return false;
    }

    /// <summary>Copies a quoted string through verbatim, honouring backslash escapes. An unterminated
    /// string ends at the newline, as the CSS tokenizer says it does — so a stray quote cannot swallow
    /// the rest of the file.</summary>
    private static int CopyString(string css, int i, StringBuilder sb)
    {
        var quote = css[i];
        sb.Append(quote);
        i++;
        while (i < css.Length)
        {
            var c = css[i];
            sb.Append(c);
            i++;
            if (c == '\\' && i < css.Length) { sb.Append(css[i]); i++; continue; }
            if (c == quote || c == '\n') break;
        }
        return i;
    }

    /// <summary>Whether position <paramref name="i"/> starts a <c>url(</c> token — not the tail of a
    /// longer identifier such as <c>--my-url(</c>.</summary>
    private static bool IsUrlToken(string css, int i)
    {
        if (i + 4 > css.Length) return false;
        if (!(css[i + 1] is 'r' or 'R') || !(css[i + 2] is 'l' or 'L') || css[i + 3] != '(') return false;
        if (i > 0 && (char.IsLetterOrDigit(css[i - 1]) || css[i - 1] is '-' or '_' or '\\')) return false;
        return true;
    }

    /// <summary>Copies an unquoted <c>url(…)</c> token through verbatim (its contents are not CSS, so a
    /// <c>/*</c> in a path or data URI is not a comment). A quoted url is left to the string rule.</summary>
    private static int CopyUrl(string css, int i, StringBuilder sb)
    {
        sb.Append(css, i, 4);   // "url("
        i += 4;

        var j = i;
        while (j < css.Length && char.IsWhiteSpace(css[j])) j++;
        if (j < css.Length && (css[j] == '"' || css[j] == '\'')) return i;

        while (i < css.Length)
        {
            var c = css[i];
            sb.Append(c);
            i++;
            if (c == ')') break;
        }
        return i;
    }

    /// <summary>Consumes the comment starting at <paramref name="i"/>, appending whatever must take its
    /// place: nothing at all when it stood on its own line (the line goes with it), a single space when
    /// it separated two tokens, nothing otherwise.</summary>
    private static int SkipComment(string css, int i, StringBuilder sb)
    {
        var end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
        var after = end < 0 ? css.Length : end + 2;   // unterminated: consumed to EOF, as a browser does

        // Nothing but whitespace follows it on its line: the comment ends the line, so take the
        // whitespace on both sides of it with it — and the line itself when nothing else was on it.
        var j = after;
        while (j < css.Length && (css[j] == ' ' || css[j] == '\t')) j++;
        if (j >= css.Length || css[j] == '\n' || css[j] == '\r')
        {
            while (sb.Length > 0 && (sb[sb.Length - 1] == ' ' || sb[sb.Length - 1] == '\t')) sb.Length--;
            if (sb.Length == 0 || sb[sb.Length - 1] == '\n')      // the line held only the comment
            {
                if (j < css.Length && css[j] == '\r') j++;
                if (j < css.Length && css[j] == '\n') j++;
            }
            return j;
        }

        // Otherwise keep the tokens on either side apart: `a/**/b` must not become `ab`.
        if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]) && after < css.Length && !char.IsWhiteSpace(css[after]))
            sb.Append(' ');
        return after;
    }
}
