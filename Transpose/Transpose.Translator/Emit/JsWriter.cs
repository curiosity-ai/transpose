using System.Text;

namespace Transpose.Translator;

/// <summary>
/// Indentation-aware writer for emitting JavaScript.
/// </summary>
public sealed class JsWriter
{
    private readonly StringBuilder _sb = new();
    private int _indent;
    private bool _atLineStart = true;

    public const string IndentUnit = "    ";

    public JsWriter Indent()
    {
        _indent++;
        return this;
    }

    public JsWriter Outdent()
    {
        if (_indent > 0) _indent--;
        return this;
    }

    private void WriteIndentIfNeeded()
    {
        if (_atLineStart)
        {
            for (var i = 0; i < _indent; i++) _sb.Append(IndentUnit);
            _atLineStart = false;
        }
    }

    internal JsWriter Write(JsWriter jsWriter)
    {
        if (jsWriter._sb.Length == 0) return this;
        _sb.Append(jsWriter._sb);
        return this;
    }

    /// <summary>
    /// Appends already-formatted JavaScript verbatim — no indentation is inserted and the
    /// line-start state is left alone, exactly as appending another writer's buffer does. This is how
    /// a block that was written by a nested writer (a per-type emit, or one restored from the
    /// incremental cache) is spliced in without disturbing its own indentation.
    /// </summary>
    internal JsWriter WriteRaw(string text)
    {
        if (string.IsNullOrEmpty(text)) return this;
        _sb.Append(text);
        return this;
    }

    /// <summary>The current indentation depth, so a capture buffer can start at the same level and
    /// produce text that splices back in unchanged.</summary>
    internal int IndentLevel => _indent;

    public JsWriter() { }

    internal JsWriter(int indent) => _indent = indent;


    public JsWriter Write(string text)
    {
        if (string.IsNullOrEmpty(text)) return this;
        WriteIndentIfNeeded();
        _sb.Append(text);
        return this;
    }

    public JsWriter Write(char c)
    {
        WriteIndentIfNeeded();
        _sb.Append(c);
        return this;
    }

    public JsWriter WriteLine(string text = "")
    {
        if (text.Length > 0)
        {
            WriteIndentIfNeeded();
            _sb.Append(text);
        }
        _sb.Append('\n');
        _atLineStart = true;
        return this;
    }

    /// <summary>Opens a "{" block, indents, runs body, outdents and closes.</summary>
    public JsWriter Block(System.Action body, string open = "{", string close = "}")
    {
        WriteLine(open);
        Indent();
        body();
        Outdent();
        Write(close);
        return this;
    }

    public override string ToString() => _sb.ToString();
}
