namespace Transpose.Compiler;

/// <summary>
/// Which variants of the emitted JavaScript a build produces. This is derived <em>entirely</em> from
/// what is being built and in which configuration — there is no <c>tps.json</c> setting for it, and
/// deliberately so: the old <c>outputFormatting</c> key let a project ship a Debug-only site or a
/// package with no minified half, and every consumer then inherited that choice instead of making
/// its own.
///
/// <list type="bullet">
/// <item><b>Debug</b> — a site build in the Debug configuration. Formatted JavaScript, and
/// <b>module chunking is off</b> however the project's tps.json is written: one readable bundle is
/// what makes a debugger and a stack trace usable, and hunting a symbol across sixty on-demand
/// chunks is what makes them not.</item>
/// <item><b>Release</b> — a site build in any other configuration. Module chunks when the project
/// asked for <c>outputBy: "Module"</c> (emitted formatted — they carry <c>import</c> syntax the
/// minifier does not handle), otherwise one minified bundle.</item>
/// <item><b>Package</b> — <c>--emit-package</c>, i.e. a library. It cannot know how it will be
/// consumed, so it ships <b>all three</b>: the formatted bundle, the minified bundle, and — when its
/// tps.json asks for it — the module entry plus its chunks. A consuming site build then takes the
/// variant matching <em>its own</em> profile, which is what lets an application be debugged as one
/// readable bundle and shipped as chunks without the library being rebuilt in between.</item>
/// </list>
/// </summary>
internal enum JsOutputProfile
{
    Debug,
    Release,
    Package,
}

internal static class JsOutputProfiles
{
    /// <summary>The profile a build of these options runs under. <paramref name="emitPackage"/> wins:
    /// a library is packaged the same way in every configuration, because the choice belongs to
    /// whoever consumes it.</summary>
    public static JsOutputProfile For(bool emitPackage, string? configuration)
        => emitPackage ? JsOutputProfile.Package
         : IsDebug(configuration) ? JsOutputProfile.Debug
         : JsOutputProfile.Release;

    /// <summary>Debug is the named configuration and nothing else — an unnamed or custom
    /// configuration behaves like Release, so a site never ships unminified by accident.</summary>
    public static bool IsDebug(string? configuration)
        => string.Equals(configuration, "Debug", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this build emits chunked ES modules. Debug never does — see the type
    /// remarks — and neither does a project whose tps.json did not ask for it.</summary>
    public static bool WantsModules(this JsOutputProfile profile, bool outputByModule)
        => outputByModule && profile != JsOutputProfile.Debug;

    /// <summary>Whether the formatted variant of a classic (non-module) bundle is produced/consumed.</summary>
    public static bool WantsFormatted(this JsOutputProfile profile)
        => profile != JsOutputProfile.Release;

    /// <summary>Whether the minified variant of a classic (non-module) bundle is produced/consumed.</summary>
    public static bool WantsMinified(this JsOutputProfile profile)
        => profile != JsOutputProfile.Debug;
}

/// <summary>
/// The role a piece of JavaScript embedded in a package plays, recorded in the package's
/// <c>Transpose.Resources.json</c> so a consuming site build can pick by intent rather than by
/// guessing from file names.
///
/// A package emits every variant (see <see cref="JsOutputProfile.Package"/>) and the consumer keeps
/// exactly one set: the formatted bundle in Debug, the minified bundle in Release, or the module
/// entry and its chunks when the consumer is itself chunked and the package offers them.
///
/// An entry with <b>no</b> variant is an authored resource — Monaco's <c>editor.main.js</c>, a
/// vendored <c>d3.min.js</c>, a hand-declared bundle — which belongs to no such set and is copied
/// through in every configuration under the name it was authored with.
/// </summary>
internal enum JsVariant
{
    /// <summary>The compiled single bundle (or its reflection metadata), beautified.</summary>
    Formatted,
    /// <summary>The same bundle, minified.</summary>
    Minified,
    /// <summary>The ES-module entry: the eager imports, the reflection metadata and the manifest of
    /// what was deferred. Scripted as <c>&lt;script type="module"&gt;</c>.</summary>
    ModuleEntry,
    /// <summary>One on-demand chunk file. Copied to the site but never scripted — the entry imports
    /// the ones it needs and <c>Transpose.Modules</c> fetches the rest.</summary>
    ModuleChunk,
}

internal static class JsVariants
{
    public static string ToJson(this JsVariant variant) => variant switch
    {
        JsVariant.Formatted   => "Formatted",
        JsVariant.Minified    => "Minified",
        JsVariant.ModuleEntry => "ModuleEntry",
        _                     => "ModuleChunk",
    };

    /// <summary>Parses a manifest <c>Variant</c>. Null for an absent or unrecognised value, which is
    /// also what a package built before variants existed produces — such a package is routed by the
    /// older file-name pairing instead, so it keeps working unchanged.</summary>
    public static JsVariant? Parse(string? s) => s switch
    {
        "Formatted"   => JsVariant.Formatted,
        "Minified"    => JsVariant.Minified,
        "ModuleEntry" => JsVariant.ModuleEntry,
        "ModuleChunk" => JsVariant.ModuleChunk,
        _             => null,
    };
}
