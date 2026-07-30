using Microsoft.CodeAnalysis;

namespace Transpose.Compiler;

/// <summary>
/// Formats every error and warning <c>tps</c> prints in the canonical MSBuild / Visual Studio
/// diagnostic format, so a build that shells out to <c>tps</c> gets first-class errors and warnings
/// instead of anonymous console text.
///
/// MSBuild scans a tool's stdout and stderr line by line and promotes anything matching this
/// five-part shape to a real build error or warning (see
/// <see href="https://learn.microsoft.com/visualstudio/msbuild/msbuild-diagnostic-format-for-tasks"/>):
///
/// <code>
///   Origin : Subcategory Category Code : Text
///
///   /src/App/Main.cs(17,20): error CS0103: The name 'x' does not exist in the current context
///   tps : error TPS0002: No .csproj found at '/src/Nope'.
/// </code>
///
/// The parts that matter here:
///
///   * <b>Origin</b> — the source file, as an **absolute** path plus <c>(line,column)</c> (1-based),
///     which is what lets a double-click in the IDE jump to the offending line. Relative paths are
///     legal but are resolved against the caller's working directory, so an absolute one is the only
///     spelling that always lands in the right file. A diagnostic with no source location uses the
///     tool name instead — locale-neutral, as the format requires.
///   * <b>Category</b> — literally <c>error</c> or <c>warning</c>. This is the part that was missing
///     before: <c>Main.cs(17,20): CS0103: …</c> looks like a diagnostic to a human but matches
///     nothing, so every compile error was invisible to MSBuild and the IDE.
///   * <b>Code</b> — the diagnostic id. Must contain no spaces.
///   * <b>Text</b> — one line. MSBuild matches per line, so a message that wraps would have its tail
///     dropped (or worse, parsed as another diagnostic); <see cref="SingleLine"/> flattens it.
///
/// Anything else <c>tps</c> prints — progress, phase timings, the "N error(s)" summary — must *not*
/// look like a diagnostic, or MSBuild invents build errors out of the compiler's own chatter. The rule
/// to keep in mind when adding output: a line is at risk when the word <c>error</c> or <c>warning</c>
/// is preceded only by a colon-free run ending in a space (or starts the line) *and* is followed by a
/// colon — <c>"warning: could not write …"</c> matches, whereas <c>"3 error(s), by id: CS0103×2"</c>
/// does not, because the parser needs the colon right after the category and its optional code.
/// MsBuildDiagnosticFormatTests guards both directions.
/// </summary>
internal static class MsBuildDiagnostic
{
    /// <summary>Origin for a diagnostic that has no source location. Locale-neutral by contract.</summary>
    public const string ToolName = "tps";

    // Codes for the compiler's own diagnostics — the ones that are not Roslyn's and so have no id of
    // their own. TPS0001-0099 are errors, TPS0100+ warnings. They are part of the tool's contract
    // once shipped (a build can suppress or escalate by code), so retire a code rather than reuse it.
    public const string CodeInvalidCommandLine     = "TPS0001";
    public const string CodeProjectNotFound        = "TPS0002";
    public const string CodeProjectResolveFailed   = "TPS0003";
    public const string CodeInternalError          = "TPS0004";
    public const string CodeDependencyBuildFailed  = "TPS0005";
    public const string CodeAssemblyEmitFailed     = "TPS0006";
    public const string CodeWatchRequiresSiteBuild = "TPS0007";
    public const string CodeCompilerTooOld         = "TPS0008";
    public const string CodeReferenceNotFound      = "TPS0100";
    public const string CodeMissingRuntimeBundle   = "TPS0101";
    public const string CodeTimingJsonNotWritten   = "TPS0102";
    public const string CodeCacheNotWritten        = "TPS0103";
    public const string CodeWatchServerFailed      = "TPS0104";

    /// <summary>Formats a Roslyn diagnostic, pointing at its source location when it has one.</summary>
    public static string Format(Diagnostic d)
    {
        var span = d.Location.GetLineSpan();
        var category = d.Severity == DiagnosticSeverity.Error ? "error" : "warning";
        return string.IsNullOrEmpty(span.Path)
            ? Format(category, d.Id, d.GetMessage())
            : Format(category, d.Id, d.GetMessage(), span.Path,
                     span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);
    }

    /// <summary>
    /// Formats one diagnostic line. Pass <paramref name="filePath"/> to point at a file (with
    /// <paramref name="line"/>/<paramref name="column"/> 1-based, or 0 to omit the position);
    /// omit it for a tool-level diagnostic, which is attributed to <see cref="ToolName"/>.
    /// </summary>
    public static string Format(string category, string code, string text,
                               string? filePath = null, int line = 0, int column = 0)
    {
        // A file origin is written tight against the colon (`Main.cs(17,20): error CS0103: …`), the way
        // csc writes it; the tool origin keeps the spaced form the documentation uses for a tool name
        // (`cl : Command line warning D4024 : …`). Both parse identically.
        var origin = $"{ToolName} ";
        if (!string.IsNullOrEmpty(filePath))
        {
            origin = AbsolutePath(filePath!);
            if (line > 0) origin += column > 0 ? $"({line},{column})" : $"({line})";
        }
        // A diagnostic with no text at all parses but tells the user nothing, so say something.
        var message = SingleLine(text);
        return $"{origin}: {category} {code}: {(message.Length == 0 ? "(no message)" : message)}";
    }

    /// <summary>
    /// Where a diagnostic line is written: the line, and whether it is an error. Null — the default —
    /// means the console, which is what <c>tps</c> wants (errors on stderr, warnings on stdout; MSBuild
    /// parses both streams alike). <c>Transpose.Compiler.Library</c> redirects this for the duration of
    /// one build so a hosting application can collect the diagnostics instead of finding them on its own
    /// console. Process-wide mutable state, exactly like <c>Transpose.Translator.CompileProgress.Sink</c>
    /// — which is why the library serializes its builds.
    /// </summary>
    public static Action<string, bool>? Sink;

    /// <summary>Writes an error for a failure that is not tied to a source file.</summary>
    public static void WriteError(string code, string text)
        => Write(Format("error", code, text), isError: true);

    /// <summary>Writes a warning for a condition that is not tied to a source file.</summary>
    public static void WriteWarning(string code, string text)
        => Write(Format("warning", code, text), isError: false);

    /// <summary>Writes an already-formatted diagnostic line to <see cref="Sink"/> (or the console).</summary>
    public static void Write(string line, bool isError)
    {
        var sink = Sink;
        if (sink is not null) sink(line, isError);
        else if (isError) Console.Error.WriteLine(line);
        else Console.WriteLine(line);
    }

    /// <summary>
    /// Collapses a message to a single line: MSBuild parses line by line, so a newline in the middle
    /// of a message would truncate it (an exception's stack trace is the usual culprit).
    /// </summary>
    public static string SingleLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new System.Text.StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) { pendingSpace = sb.Length > 0; continue; }
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>The absolute form of a source path, which is what makes a diagnostic navigable. Falls
    /// back to the path as given if it cannot be rooted (an in-memory tree name, say).</summary>
    private static string AbsolutePath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }
}
