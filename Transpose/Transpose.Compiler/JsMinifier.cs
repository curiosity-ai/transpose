using NUglify;
using NUglify.JavaScript;

namespace Transpose.Compiler;

/// <summary>
/// Minifies emitted JavaScript with NUglify, mirroring the legacy compiler's settings so the
/// <c>outputFormatting</c> <c>Minified</c>/<c>Both</c> variants match what tps has always produced.
///
/// The runtime core files (tps.js / tps.collections.js) get a distinct settings profile from
/// user/project code, and everything defaults to a "safe" profile that keeps local variable
/// names (<see cref="LocalRenaming.KeepAll"/>) unless local-variable crunching is requested.
/// These map directly onto the legacy <c>MinifierCodeSettings*</c> definitions.
/// </summary>
internal static class JsMinifier
{
    // The runtime core files, matched by name — they get the "internal" settings profile.
    private static readonly HashSet<string> InternalFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "tps.js", "tps.min.js", "tps.collections.js", "tps.collections.min.js",
    };

    // Safe profile: never rename locals, terminate statements with semicolons, escape non-ASCII.
    private static CodeSettings Safe() => new()
    {
        EvalTreatment        = EvalTreatment.MakeAllSafe,
        LocalRenaming        = LocalRenaming.KeepAll,
        TermSemicolons       = true,
        StrictMode           = false,
        RemoveUnneededCode   = false,
        AlwaysEscapeNonAscii = true,
    };

    // Safe profile but crunch local variable names (smaller output; opted into per project).
    private static CodeSettings SafeCrunchLocal() => new()
    {
        EvalTreatment        = EvalTreatment.MakeAllSafe,
        LocalRenaming        = LocalRenaming.CrunchAll,
        TermSemicolons       = true,
        StrictMode           = false,
        RemoveUnneededCode   = false,
        AlwaysEscapeNonAscii = true,
    };

    // Runtime-core profile (tps.js, …): as safe but without the eval/local-renaming overrides.
    private static CodeSettings Internal() => new()
    {
        TermSemicolons       = true,
        StrictMode           = false,
        RemoveUnneededCode   = false,
        AlwaysEscapeNonAscii = true,
    };

    /// <summary>
    /// Minifies <paramref name="source"/>. The settings are chosen from <paramref name="fileName"/>:
    /// the runtime core files use the internal profile; everything else uses the safe profile
    /// (crunching locals only when <paramref name="minifyLocalVariables"/> is set).
    /// </summary>
    public static string Minify(string source, string fileName, bool minifyLocalVariables = false, bool noStrictMode = false)
    {
        if (string.IsNullOrEmpty(source)) return source;

        CodeSettings settings;
        if (InternalFileNames.Contains(Path.GetFileName(fileName)))
        {
            settings = Internal();
        }
        else
        {
            settings = minifyLocalVariables ? SafeCrunchLocal() : Safe();
            if (noStrictMode) settings.StrictMode = false;
        }

        UglifyResult result;
        try
        {
            result = Uglify.Js(source, settings);
        }
        catch (Exception ex)
        {
            // NUglify's JavaScript parser can throw (e.g. a NullReferenceException) rather than
            // returning a diagnostic when handed input it cannot parse — most notably non-JavaScript
            // content routed here by mistake. Surface an actionable error naming the file instead of
            // letting an opaque NRE bubble out of the compiler.
            throw new InvalidOperationException(
                $"Minification of {fileName} failed: the NUglify JavaScript parser threw " +
                $"{ex.GetType().Name} ({source.Length} bytes). This usually means the input is not " +
                $"JavaScript (e.g. CSS routed to the JS minifier).", ex);
        }

        // NUglify raises non-fatal analysis diagnostics (e.g. JS1300 "assignment to undefined
        // variable" for the runtime's intentional implicit globals) but still emits valid code — the
        // legacy compiler takes result.Code regardless of HasErrors, so we do the same. Only a null
        // Code signals a genuine failure to minify.
        if (result.Code is null)
        {
            var first = result.Errors.Count > 0 ? result.Errors[0].ToString() : "produced no output";
            throw new InvalidOperationException($"Minification of {fileName} failed: {first}");
        }
        return result.Code;
    }
}
