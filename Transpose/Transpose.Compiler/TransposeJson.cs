using System.Text.Json;

namespace Transpose.Compiler;

/// <summary>
/// Which variants of the emitted JavaScript the build produces — mirrors the legacy compiler's
/// <c>JavaScriptOutputType</c> (the <c>outputFormatting</c> tps.json setting).
/// </summary>
internal enum JsOutputFormatting
{
    /// <summary>Only the formatted (beautified) JS. Good for debugging.</summary>
    Formatted = 1,
    /// <summary>Only the minified JS. Good for production.</summary>
    Minified = 2,
    /// <summary>Both variants — a referencing build (or index.html generation) then picks one.</summary>
    Both = 3,
}

/// <summary>
/// The subset of a project's <c>tps.json</c> the CLI acts on: where output goes, the bundle
/// file name, HTML generation, and the resource files (CSS/images) copied into the output
/// and linked from index.html. JSONC (comments + trailing commas) is tolerated.
///
/// A configuration-specific overlay (<c>tps.&lt;Configuration&gt;.json</c>, e.g. <c>tps.Release.json</c>)
/// is merged on top of the base <c>tps.json</c> when present, matching the legacy compiler's
/// <c>ConfigHelper.ReadConfig</c>: scalar settings from the overlay win, and resource arrays are
/// concatenated (base first, then overlay). This is how the front-end projects flip
/// <c>outputFormatting</c> to <c>Both</c> for Release while keeping <c>Formatted</c> for Debug.
/// </summary>
internal sealed class TransposeJson
{
    public string? Output { get; init; }
    public string FileName { get; init; } = "app.js";

    /// <summary>The tps.json <c>fileName</c> exactly as written (null when unset). A library with
    /// no explicit fileName outputs &lt;AssemblyName&gt;.js.</summary>
    public string? ExplicitFileName { get; init; }
    public bool HtmlDisabled { get; init; }
    public string? HtmlTitle { get; init; }
    public string HtmlBody { get; init; } = "";
    public string HtmlHead { get; init; } = "";
    public string HtmlMeta { get; init; } = "";
    public List<ResourceGroup> Resources { get; init; } = new();

    /// <summary>
    /// <c>outputFormatting</c> — whether to emit the formatted JS, the minified JS, or both.
    /// Defaults to <see cref="JsOutputFormatting.Both"/>, matching the legacy compiler.
    /// </summary>
    public JsOutputFormatting OutputFormatting { get; init; } = JsOutputFormatting.Both;

    /// <summary>
    /// <c>outputBy</c> — the file-layout mode (Class/ClassPath/Namespace/…). The base runtime
    /// library (Transpose.BCL) uses <c>ClassPath</c>: this is the marker that the project *defines*
    /// the BCL and must be compiled self-contained (no Transpose.dll reference) into the tps.js
    /// runtime bundle, rather than transpiled against Transpose.dll like a normal project.
    /// </summary>
    public string? OutputBy { get; init; }

    /// <summary>
    /// <c>cleanOutputFolder</c> — prune stale files from the site output folder after a build:
    /// any file left over from a previous build that <em>this</em> build did not (re)write is
    /// deleted, and directories it empties are removed. This is the improved take on the legacy h5
    /// <c>cleanOutputFolderBeforeBuild</c>, which deleted by glob <em>before</em> compiling: instead
    /// of a pattern applied up-front, the folder is diffed against exactly what the build produced,
    /// so a current output is never removed, a failed build leaves the previous output untouched, and
    /// no pattern needs configuring. Defaults to <c>true</c>. Set <c>false</c> to accumulate files.
    /// </summary>
    public bool CleanOutputFolder { get; init; } = true;

    /// <summary>
    /// <c>cleanOutputFolderExclude</c> — glob patterns (matched against each file's output-relative
    /// path and its file name, with <c>*</c>/<c>?</c> wildcards) that <see cref="CleanOutputFolder"/>
    /// never prunes even when stale. The escape hatch for hand-placed files that live alongside the
    /// generated site (e.g. <c>favicon.ico</c>, <c>.htaccess</c>, <c>assets/**</c>) — the equivalent
    /// of the legacy h5 <c>!</c> skip patterns, but only consulted for files the build didn't write.
    /// </summary>
    public List<string> CleanOutputFolderExclude { get; init; } = new();

    /// <summary>reflection.disabled — when true, no reflection metadata is emitted.</summary>
    public bool ReflectionDisabled { get; init; }

    /// <summary>reflection.target — "inline" or "file" (default). Others map to the closest of the two.</summary>
    public string ReflectionTarget { get; init; } = "file";

    internal sealed class ResourceGroup
    {
        public string? Name { get; init; }
        public List<string> Files { get; init; } = new();
        public string? Output { get; init; }
    }

    /// <summary>
    /// Loads <c>tps.json</c> from <paramref name="projectDir"/>, merging a
    /// <c>tps.&lt;configuration&gt;.json</c> overlay on top when one exists.
    /// </summary>
    public static TransposeJson? TryLoad(string projectDir, string? configuration = null)
    {
        var basePath = Path.Combine(projectDir, "tps.json");
        var baseDraft = File.Exists(basePath) ? ReadDraft(basePath) : null;

        Draft? overlayDraft = null;
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            var overlayPath = Path.Combine(projectDir, $"tps.{configuration}.json");
            if (File.Exists(overlayPath)) overlayDraft = ReadDraft(overlayPath);
        }

        if (baseDraft is null && overlayDraft is null) return null;

        // Merge base + overlay: overlay scalars win; resource arrays concatenate (base first).
        var merged = Draft.Merge(baseDraft ?? new Draft(), overlayDraft);

        return new TransposeJson
        {
            Output = merged.Output,
            OutputBy = merged.OutputBy,
            FileName = merged.FileName ?? "app.js",
            ExplicitFileName = merged.FileName,
            OutputFormatting = merged.OutputFormatting ?? JsOutputFormatting.Both,
            HtmlDisabled = merged.HtmlDisabled ?? false,
            HtmlTitle = merged.HtmlTitle,
            HtmlBody = merged.HtmlBody ?? "",
            HtmlHead = merged.HtmlHead ?? "",
            HtmlMeta = merged.HtmlMeta ?? "",
            Resources = merged.Resources,
            CleanOutputFolder = merged.CleanOutputFolder ?? true,
            CleanOutputFolderExclude = merged.CleanOutputFolderExclude,
            ReflectionDisabled = merged.ReflectionDisabled ?? false,
            ReflectionTarget = merged.ReflectionTarget ?? "file",
        };
    }

    /// <summary>A single tps.json file parsed into nullable fields (null = the key was absent),
    /// so a configuration overlay can be merged field-by-field.</summary>
    private sealed class Draft
    {
        public string? Output;
        public string? OutputBy;
        public string? FileName;
        public JsOutputFormatting? OutputFormatting;
        public bool? HtmlDisabled;
        public string? HtmlTitle;
        public string? HtmlBody;
        public string? HtmlHead;
        public string? HtmlMeta;
        public List<ResourceGroup> Resources = new();
        public bool? CleanOutputFolder;
        public List<string> CleanOutputFolderExclude = new();
        public bool? ReflectionDisabled;
        public string? ReflectionTarget;

        public static Draft Merge(Draft b, Draft? o)
        {
            if (o is null) return b;
            return new Draft
            {
                Output           = o.Output           ?? b.Output,
                OutputBy         = o.OutputBy          ?? b.OutputBy,
                FileName         = o.FileName          ?? b.FileName,
                OutputFormatting = o.OutputFormatting  ?? b.OutputFormatting,
                HtmlDisabled     = o.HtmlDisabled      ?? b.HtmlDisabled,
                HtmlTitle        = o.HtmlTitle         ?? b.HtmlTitle,
                HtmlBody         = o.HtmlBody          ?? b.HtmlBody,
                HtmlHead         = o.HtmlHead          ?? b.HtmlHead,
                Resources        = b.Resources.Concat(o.Resources).ToList(),
                CleanOutputFolder = o.CleanOutputFolder ?? b.CleanOutputFolder,
                CleanOutputFolderExclude = b.CleanOutputFolderExclude.Concat(o.CleanOutputFolderExclude).ToList(),
                ReflectionDisabled = o.ReflectionDisabled ?? b.ReflectionDisabled,
                ReflectionTarget = o.ReflectionTarget  ?? b.ReflectionTarget,
            };
        }
    }

    private static Draft ReadDraft(string path)
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var doc = JsonDocument.Parse(File.ReadAllText(path), options);
        var root = doc.RootElement;

        string? Str(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        bool? Bool(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? v.ValueKind == JsonValueKind.True
                : (bool?)null;

        var html = root.TryGetProperty("html", out var h) && h.ValueKind == JsonValueKind.Object ? h : default;
        var reflection = root.TryGetProperty("reflection", out var rfl) && rfl.ValueKind == JsonValueKind.Object ? rfl : default;

        var cleanExclude = new List<string>();
        if (root.TryGetProperty("cleanOutputFolderExclude", out var ce) && ce.ValueKind == JsonValueKind.Array)
            cleanExclude.AddRange(ce.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!));

        var resources = new List<ResourceGroup>();
        if (root.TryGetProperty("resources", out var res) && res.ValueKind == JsonValueKind.Array)
        {
            foreach (var g in res.EnumerateArray())
            {
                if (g.ValueKind != JsonValueKind.Object) continue;
                var files = new List<string>();
                if (g.TryGetProperty("files", out var fs) && fs.ValueKind == JsonValueKind.Array)
                    files.AddRange(fs.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!));
                resources.Add(new ResourceGroup { Name = Str(g, "name"), Files = files, Output = Str(g, "output") });
            }
        }

        return new Draft
        {
            Output = Str(root, "output"),
            OutputBy = Str(root, "outputBy"),
            FileName = Str(root, "fileName"),
            OutputFormatting = ParseFormatting(Str(root, "outputFormatting")),
            HtmlDisabled = html.ValueKind == JsonValueKind.Object && html.TryGetProperty("disabled", out var d)
                ? d.ValueKind == JsonValueKind.True
                : (bool?)null,
            HtmlTitle = html.ValueKind == JsonValueKind.Object ? Str(html, "title") : null,
            HtmlBody = html.ValueKind == JsonValueKind.Object ? Str(html, "body") : null,
            HtmlHead = html.ValueKind == JsonValueKind.Object ? Str(html, "head") : null,
            HtmlMeta = html.ValueKind == JsonValueKind.Object ? Str(html, "meta") : null,
            Resources = resources,
            CleanOutputFolder = Bool(root, "cleanOutputFolder"),
            CleanOutputFolderExclude = cleanExclude,
            ReflectionDisabled = reflection.ValueKind == JsonValueKind.Object && reflection.TryGetProperty("disabled", out var rd)
                ? rd.ValueKind == JsonValueKind.True
                : (bool?)null,
            ReflectionTarget = reflection.ValueKind == JsonValueKind.Object ? Str(reflection, "target") : null,
        };
    }

    /// <summary>Parses the <c>outputFormatting</c> string (case-insensitive) into the enum; null/unknown → absent.</summary>
    private static JsOutputFormatting? ParseFormatting(string? s)
        => s?.Trim().ToLowerInvariant() switch
        {
            "formatted" => JsOutputFormatting.Formatted,
            "minified" => JsOutputFormatting.Minified,
            "both" => JsOutputFormatting.Both,
            _ => null,
        };
}
