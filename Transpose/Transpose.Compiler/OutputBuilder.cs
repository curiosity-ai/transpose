using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Transpose.Compiler;

/// <summary>
/// Assembles a runnable Transpose site from a translation: writes the compiled bundle, extracts the
/// JavaScript each referenced package embeds (tps.js, newtonsoft.json.js, … — listed in the
/// assembly's <c>Transpose.Resources.json</c>), copies the tps.json resource files (CSS/images), and
/// generates index.html — mirroring what the existing tps compiler produces.
///
/// When <c>outputFormatting</c> is <c>Minified</c> or <c>Both</c>, every compiler-produced JS output
/// (the extracted runtime, the compiled bundle and its reflection metadata, and referenced library
/// code) is minified with NUglify into a <c>.min.js</c> sibling. The generated HTML then follows the
/// legacy compiler's rule: index.html links the formatted variants, index.min.html links the
/// minified variants, and the active build configuration collapses the pair to a single index.html
/// (Release keeps the minified one, Debug the formatted one). tps.json resource files are taken as
/// authored (never re-minified) — a project ships both a <c>foo.js</c> and a <c>foo.min.js</c> group
/// when it wants both variants, exactly as before.
/// </summary>
internal static class OutputBuilder
{
    // Matches the legacy compiler's HtmlGenerator template.
    private const string HtmlTemplate =
@"<!doctype html>
<html lang=en>
<head>
    <meta charset=""utf-8"" />
    {META}
    <title>{TITLE}</title>
    {CSS}
    {SCRIPT}
    {HEAD}
</head>
<body>
{BODY}
</body>
</html>";

    /// <summary>A JavaScript output as it should appear in the generated HTML — a direct port of the
    /// legacy compiler's <c>TranslatorOutputItem</c> routing. <see cref="Path"/> is the formatted
    /// path; <see cref="MinifiedPath"/> is its minified sibling when both were produced (Both mode).
    /// <see cref="IsEmpty"/> marks a formatted variant that was not written (Minified mode) — the
    /// formatted HTML then falls back to the minified path, exactly like <c>GetOutputPath</c>.
    /// <see cref="IsMinified"/> marks a standalone authored <c>.min.js</c> resource, which appears
    /// only in the minified HTML.</summary>
    private sealed class JsOut
    {
        public string Path = "";
        public string? MinifiedPath;
        public bool IsEmpty;
        public bool IsMinified;
    }

    public static string Build(ResolvedProject project, TransposeJson config, string javascript, string outputDir, string configuration, string? metadataJavascript = null)
    {
        Directory.CreateDirectory(outputDir);

        var fmt = config.OutputFormatting;
        var wantFormatted = fmt != JsOutputFormatting.Minified;   // Formatted or Both
        var wantMinified  = fmt != JsOutputFormatting.Formatted;  // Minified or Both
        var minifyLocals  = project.MinifyLocalVariables;

        var jsOuts = new List<JsOut>();    // JS in load order (runtime → libs → resources → app)
        var cssLinks = new List<string>();

        var utf8 = new UTF8Encoding(false);

        void WriteText(string rel, string content)
        {
            var dest = Path.Combine(outputDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest, content, utf8);
        }

        // A per-project compiler output (the app bundle, its reflection metadata, the shim): there
        // is no pre-built variant to reuse, so minify at compile time per outputFormatting.
        void EmitCompilerJs(string rel, string content)
        {
            var o = new JsOut { Path = rel };
            if (wantFormatted) WriteText(rel, content); else o.IsEmpty = true;
            if (wantMinified)
            {
                var minRel = ToMinName(rel);
                WriteText(minRel, JsMinifier.Minify(content, Path.GetFileName(rel), minifyLocals));
                o.MinifiedPath = minRel;
            }
            jsOuts.Add(o);
        }

        // Routes a package's already-loaded JS resources (runtime bundles, library code) into the
        // output. A package ships both a formatted and a pre-minified variant of each file, so we
        // reuse them as-is: the formatted variant links from index.html, the pre-minified from
        // index.min.html — no re-minification. Only when a package predates the pre-minified variant
        // (no .min.js sibling) do we minify the formatted one as a fallback. Files are written to disk
        // on demand so a Formatted build never materialises the .min.js (and vice-versa).
        void RoutePackageJs(IReadOnlyList<EmbeddedJs> jsFiles)
        {
            var present = jsFiles.Select(f => f.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var byName = jsFiles.ToDictionary(f => f.FileName, f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var f in jsFiles)
            {
                if (IsMinifiedName(f.FileName))
                {
                    // A .min.js whose formatted sibling is also in the package is written by that
                    // sibling below; a standalone .min.js links only from the minified HTML.
                    if (present.Contains(CounterpartName(f.FileName))) continue;
                    if (wantMinified) { WriteText(f.Rel, f.Text); jsOuts.Add(new JsOut { Path = f.Rel, IsMinified = true }); }
                    continue;
                }

                var o = new JsOut { Path = f.Rel };
                if (wantFormatted) WriteText(f.Rel, f.Text); else o.IsEmpty = true;
                if (wantMinified)
                {
                    var minName = CounterpartName(f.FileName);
                    if (byName.TryGetValue(minName, out var pre))
                    {
                        WriteText(pre.Rel, pre.Text);       // pre-built .min.js shipped in the package
                        o.MinifiedPath = pre.Rel;
                    }
                    else
                    {
                        var minRel = ToMinName(f.Rel);      // fallback: minify at compile time
                        WriteText(minRel, JsMinifier.Minify(f.Text, f.FileName, minifyLocals));
                        o.MinifiedPath = minRel;
                    }
                }
                jsOuts.Add(o);
            }
        }

        // In separate-assembly mode, referenced *projects* are consumed as DLLs (their JS is
        // extracted, not recompiled) — exclude them from the runtime-package JS loop below.
        var projectDlls = new HashSet<string>(project.ReferencedProjectDlls, StringComparer.OrdinalIgnoreCase);

        // 1. Runtime JS embedded in referenced packages, in dependency order (Transpose core first).
        var runtimeJs = new List<EmbeddedJs>();
        foreach (var dll in OrderRuntimeAssemblies(project.ReferencePaths.Where(p => !projectDlls.Contains(p))))
            foreach (var (fileName, text) in ExtractEmbeddedJs(dll))
                runtimeJs.Add(new EmbeddedJs(fileName, fileName, text));
        RoutePackageJs(runtimeJs);

        // The TransposeR shim (the translator's language-level helpers over tps.js) loads right after
        // the Transpose runtime and before any generated code that calls into it.
        EmitCompilerJs("tps.shim.js", RoslynTranslator.RuntimeShim);

        // 1b. Referenced project assemblies: extract their embedded JS/CSS/resources (deepest
        //     dependency first) so a library loads before the app that uses it.
        foreach (var dll in Enumerable.Reverse(project.ReferencedProjectDlls))
            RoutePackageJs(ExtractProjectDllResources(dll, outputDir, cssLinks, utf8));

        // 2. tps.json resource files from every project in the closure — referenced projects
        //    first (a library's JS deps load before the app that uses them). A resource group
        //    whose name is a .js/.css file concatenates its files into that one bundle; other
        //    groups (globbed images, etc.) copy each file through. Resource JS is taken as authored:
        //    a .js group routes to index.html, a .min.js group to index.min.html.
        foreach (var projectDir in Enumerable.Reverse(project.ProjectDirs))
        {
            var cfg = projectDir == project.ProjectDir ? config : TransposeJson.TryLoad(projectDir, configuration);
            if (cfg is null) continue;
            foreach (var group in cfg.Resources)
                ProcessResourceGroup(projectDir, outputDir, group, jsOuts, cssLinks);
        }

        // 3. The compiled bundle — loads last, after runtime + library deps are in place.
        EmitCompilerJs(config.FileName, javascript);

        // 3b. Reflection metadata as a separate file (reflection.target: "file") — loads right
        //     after the bundle whose types it describes, matching the existing compiler.
        if (metadataJavascript is not null)
        {
            var metaName = Path.GetFileNameWithoutExtension(config.FileName) + ".meta.js";
            EmitCompilerJs(metaName, metadataJavascript);
        }

        // 4. index.html (and index.min.html when both variants exist).
        if (!config.HtmlDisabled)
            WriteHtml(project, config, outputDir, jsOuts, cssLinks, configuration, utf8);

        return outputDir;
    }

    /// <summary>
    /// The resources a library assembly embeds so a referencing project can extract them: the
    /// compiled JS (and its .meta.js) plus every tps.json resource group (bundled or copied),
    /// each tagged with its output subdirectory. The compiled JS is embedded in both a formatted
    /// and a pre-minified (<c>.min.js</c>) variant, so a referencing build never has to minify
    /// library code itself — it just extracts the variant the build configuration calls for.
    /// </summary>
    public static List<EmbeddedItem> CollectEmbeddableItems(
        string projectDir, TransposeJson config, string mainJsName, string javascript, string? metadataJavascript,
        bool minifyLocalVariables = false)
    {
        var items = new List<EmbeddedItem>();
        var utf8 = new UTF8Encoding(false);

        // The compiled bundle + its reflection metadata, each in a formatted and a pre-minified
        // variant. Shipping the .min.js in the package is deliberate: minifying library code is
        // work the consumer would otherwise repeat on every build (see BuildRuntime for the same
        // for the runtime bundles).
        items.Add(new EmbeddedItem(mainJsName, utf8.GetBytes(javascript), null));

        if (config.OutputFormatting != JsOutputFormatting.Formatted)
        {
            items.Add(new EmbeddedItem(ToMinName(mainJsName), utf8.GetBytes(JsMinifier.Minify(javascript, mainJsName, minifyLocalVariables)), null));
        }

        if (metadataJavascript is not null)
        {
            var metaName = Path.GetFileNameWithoutExtension(mainJsName) + ".meta.js";
            items.Add(new EmbeddedItem(metaName, utf8.GetBytes(metadataJavascript), null));

            if (config.OutputFormatting != JsOutputFormatting.Formatted)
            {
                items.Add(new EmbeddedItem(ToMinName(metaName), utf8.GetBytes(JsMinifier.Minify(metadataJavascript, metaName, minifyLocalVariables)), null));
            }
        }

        foreach (var group in config.Resources)
        {
            var name = group.Name ?? "";
            var destSub = string.IsNullOrEmpty(group.Output) ? null : group.Output!.Replace('\\', '/');
            var files = group.Files.SelectMany(p => ExpandGlob(projectDir, p)).ToList();
            if (files.Count == 0) continue;

            var isBundle = name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                           || name.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
            if (isBundle)
            {
                var bytes = utf8.GetBytes(string.Join("\n", files.Select(File.ReadAllText)));
                items.Add(new EmbeddedItem(name, bytes, destSub));
            }
            else
            {
                // Copy-through group (images, fonts): embed each file under its own name.
                foreach (var src in files)
                    items.Add(new EmbeddedItem(Path.GetFileName(src), File.ReadAllBytes(src), destSub));
            }
        }
        return items;
    }

    /// <summary>A JavaScript resource loaded from a package (a runtime bundle or embedded library
    /// code), with its file name (for pairing a formatted variant with its <c>.min.js</c>), the
    /// output-relative path it extracts to, and its text.</summary>
    private readonly record struct EmbeddedJs(string FileName, string Rel, string Text);

    /// <summary>
    /// Extracts a referenced project assembly's embedded resources (listed in its
    /// Transpose.Resources.json manifest) into the output folder: CSS is written and linked here,
    /// and the JS resources are returned for <c>RoutePackageJs</c> to place (which picks the
    /// formatted vs pre-minified variant per build). Non-JS/CSS resources (images, fonts) are
    /// copied through under their output subdirectory.
    /// </summary>
    private static IReadOnlyList<EmbeddedJs> ExtractProjectDllResources(
        string dllPath, string outputDir, List<string> cssLinks, UTF8Encoding utf8)
    {
        var jsFiles = new List<EmbeddedJs>();
        if (!File.Exists(dllPath)) return jsFiles;
        Assembly asm;
        try { asm = Assembly.LoadFrom(dllPath); } catch { return jsFiles; }

        var names = asm.GetManifestResourceNames();
        var manifestName = names.FirstOrDefault(n => n.EndsWith("Transpose.Resources.json", StringComparison.OrdinalIgnoreCase));
        if (manifestName is null) return jsFiles;

        List<(string fileName, string? path)> entries;
        using (var ms = asm.GetManifestResourceStream(manifestName)!)
        using (var sr = new StreamReader(ms))
        using (var doc = JsonDocument.Parse(sr.ReadToEnd(), new JsonDocumentOptions { AllowTrailingCommas = true }))
        {
            entries = doc.RootElement.EnumerateArray()
                .Select(e => (
                    fileName: e.TryGetProperty("FileName", out var f) ? f.GetString() : null,
                    path: e.TryGetProperty("Path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null))
                .Where(x => !string.IsNullOrEmpty(x.fileName))
                .Select(x => (x.fileName!, x.path))
                .ToList();
        }

        string Rel(string fileName, string? path) => string.IsNullOrEmpty(path) ? fileName : path!.Replace('\\', '/') + "/" + fileName;

        foreach (var (fileName, path) in entries)
        {
            var resName = names.FirstOrDefault(n => n.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                          ?? names.FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
            if (resName is null) continue;

            var rel = Rel(fileName, path);

            if (rel.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            {
                using var s = asm.GetManifestResourceStream(resName)!;
                using var reader = new StreamReader(s);
                jsFiles.Add(new EmbeddedJs(fileName, rel, reader.ReadToEnd()));
            }
            else
            {
                // CSS and copy-through resources (images, fonts): write to disk now.
                var dest = Path.Combine(outputDir, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using (var s = asm.GetManifestResourceStream(resName)!)
                using (var fs = File.Create(dest))
                    s.CopyTo(fs);
                if (rel.EndsWith(".css", StringComparison.OrdinalIgnoreCase)) cssLinks.Add(rel);
            }
        }
        return jsFiles;
    }

    /// <summary>A file name for a minified bundle (ends in <c>.min.js</c>/<c>.min.css</c>).</summary>
    private static bool IsMinifiedName(string name)
        => name.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase)
           || name.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase);

    /// <summary>The paired variant of a bundle name: <c>x.js</c> ↔ <c>x.min.js</c> (and .css).</summary>
    private static string CounterpartName(string name)
    {
        foreach (var ext in new[] { ".js", ".css" })
        {
            if (IsMinifiedName(name) && name.EndsWith(".min" + ext, StringComparison.OrdinalIgnoreCase))
                return name.Substring(0, name.Length - (".min" + ext).Length) + ext;
            if (!IsMinifiedName(name) && name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return name.Substring(0, name.Length - ext.Length) + ".min" + ext;
        }
        return name;
    }

    /// <summary>The minified sibling of a JS path: <c>x.js</c> → <c>x.min.js</c> (idempotent).</summary>
    private static string ToMinName(string rel)
    {
        if (IsMinifiedName(rel)) return rel;
        if (rel.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            return rel.Substring(0, rel.Length - ".js".Length) + ".min.js";
        return rel;
    }

    private static void ProcessResourceGroup(
        string projectDir, string outputDir, TransposeJson.ResourceGroup group,
        List<JsOut> jsOuts, List<string> cssLinks)
    {
        var destSub = (group.Output ?? "").Replace('\\', '/');
        var files = group.Files.SelectMany(p => ExpandGlob(projectDir, p)).ToList();
        if (files.Count == 0) return;

        var name = group.Name ?? "";
        var isBundle = name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                       || name.EndsWith(".css", StringComparison.OrdinalIgnoreCase);

        void RouteJs(string rel)
        {
            // Resource JS is taken as authored (never re-minified): a .min.js links only from
            // index.min.html; a plain .js links only from index.html — matching the legacy compiler.
            if (IsMinifiedName(rel)) jsOuts.Add(new JsOut { Path = rel, IsMinified = true });
            else jsOuts.Add(new JsOut { Path = rel });
        }

        if (isBundle)
        {
            // Concatenate the group's files into a single named output (e.g. tss-dep.js).
            var rel = string.IsNullOrEmpty(destSub) ? name : destSub + "/" + name;
            var dest = Path.Combine(outputDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest, string.Join("\n", files.Select(File.ReadAllText)));
            if (name.EndsWith(".css", StringComparison.OrdinalIgnoreCase)) cssLinks.Add(rel);
            else RouteJs(rel);
        }
        else
        {
            // Copy each file through (images, or JS/CSS referenced by their own file name).
            foreach (var src in files)
            {
                var rel = string.IsNullOrEmpty(destSub) ? Path.GetFileName(src) : destSub + "/" + Path.GetFileName(src);
                var dest = Path.Combine(outputDir, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(src, dest, overwrite: true);
                if (rel.EndsWith(".css", StringComparison.OrdinalIgnoreCase)) cssLinks.Add(rel);
                else if (rel.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) RouteJs(rel);
            }
        }
    }

    /// <summary>Orders reference assemblies so the Transpose runtime core loads first.</summary>
    private static IEnumerable<string> OrderRuntimeAssemblies(IEnumerable<string> dlls)
        => dlls.OrderBy(d => Path.GetFileName(d).Equals("Transpose.dll", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
               .ThenBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

    /// <summary>The JS files a package embeds, in the order its Transpose.Resources.json lists them.</summary>
    private static IEnumerable<(string fileName, string text)> ExtractEmbeddedJs(string dllPath)
    {
        Assembly asm;
        try { asm = Assembly.LoadFrom(dllPath); }
        catch { yield break; }

        var resourceNames = asm.GetManifestResourceNames();
        var manifest = resourceNames.FirstOrDefault(n => n.EndsWith("Transpose.Resources.json", StringComparison.OrdinalIgnoreCase));
        List<string> order;
        if (manifest is not null)
        {
            using var ms = asm.GetManifestResourceStream(manifest)!;
            using var sr = new StreamReader(ms);
            using var doc = JsonDocument.Parse(sr.ReadToEnd(), new JsonDocumentOptions { AllowTrailingCommas = true });
            order = doc.RootElement.EnumerateArray()
                .Select(e => e.TryGetProperty("FileName", out var f) ? f.GetString() : null)
                .Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList();
        }
        else
        {
            order = resourceNames.Where(n => n.EndsWith(".js", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        foreach (var fileName in order)
        {
            var resName = resourceNames.FirstOrDefault(n => n.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                          ?? resourceNames.FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
            if (resName is null) continue;
            using var s = asm.GetManifestResourceStream(resName)!;
            using var reader = new StreamReader(s);
            yield return (Path.GetFileName(fileName), reader.ReadToEnd());
        }
    }

    private static IEnumerable<string> ExpandGlob(string baseDir, string pattern)
    {
        pattern = pattern.Replace('\\', '/');
        var dir = Path.GetDirectoryName(pattern) ?? "";
        var file = Path.GetFileName(pattern);
        var searchDir = Path.Combine(baseDir, dir);
        if (!Directory.Exists(searchDir)) return Enumerable.Empty<string>();
        return file.Contains('*')
            ? Directory.EnumerateFiles(searchDir, file, SearchOption.TopDirectoryOnly)
            : File.Exists(Path.Combine(searchDir, file)) ? new[] { Path.Combine(searchDir, file) } : Enumerable.Empty<string>();
    }

    /// <summary>
    /// Writes index.html (formatted scripts) and/or index.min.html (minified scripts), then collapses
    /// the pair to a single index.html for the active build configuration — a port of the legacy
    /// HtmlGenerator: Release keeps the minified variant as index.html, Debug keeps the formatted one,
    /// and an empty configuration keeps both (index.html + index.min.html).
    /// </summary>
    private static void WriteHtml(
        ResolvedProject project, TransposeJson config, string outputDir,
        List<JsOut> jsOuts, List<string> cssLinks, string configuration, UTF8Encoding utf8)
    {
        var css = new StringBuilder();
        foreach (var link in cssLinks)
            css.Append("\n    ").Append($"<link rel=\"stylesheet\" href=\"{link}\">");

        var js = new StringBuilder();
        var jsMin = new StringBuilder();
        var minSeen = new HashSet<string>();
        foreach (var o in jsOuts)
        {
            if (o.IsMinified)
            {
                // A standalone minified resource — minified HTML only.
                if (minSeen.Add(o.Path))
                    jsMin.Append("\n    ").Append($"<script src=\"{o.Path}\" defer></script>");
                continue;
            }

            // Formatted HTML links the formatted variant, falling back to the minified path when the
            // formatted one was not written (Minified mode) — mirrors the legacy GetOutputPath().
            var formattedPath = o.IsEmpty ? o.MinifiedPath : o.Path;
            if (formattedPath is not null)
                js.Append("\n    ").Append($"<script src=\"{formattedPath}\" defer></script>");

            // Minified HTML links the minified sibling, falling back to the formatted path when no
            // minified variant exists. (The legacy compiler dropped such files from the minified
            // HTML entirely — so a resource declared once, with no .min sibling, silently failed to
            // load in a Release build. Transpose keeps it: a missing .min just loads the plain file.)
            var minifiedPath = o.MinifiedPath ?? (o.IsEmpty ? null : o.Path);
            if (minifiedPath is not null && minSeen.Add(minifiedPath))
                jsMin.Append("\n    ").Append($"<script src=\"{minifiedPath}\" defer></script>");
        }

        string? htmlName = (js.Length > 0 || css.Length > 0) ? "index.html" : null;
        string? htmlMinName = jsMin.Length > 0 ? (htmlName is null ? "index.html" : "index.min.html") : null;

        // Collapse to a single index.html for the requested configuration (legacy behaviour).
        if (string.Equals(configuration, "Release", StringComparison.OrdinalIgnoreCase))
        {
            if (htmlMinName is not null) { htmlName = null; htmlMinName = "index.html"; }
        }
        else if (string.Equals(configuration, "Debug", StringComparison.OrdinalIgnoreCase))
        {
            if (htmlMinName is not null && htmlName is not null) htmlMinName = null;
        }

        string Render(string scripts) => HtmlTemplate
            .Replace("{META}", config.HtmlMeta)
            .Replace("{TITLE}", config.HtmlTitle ?? project.AssemblyName)
            .Replace("{CSS}", css.ToString().TrimStart())
            .Replace("{SCRIPT}", scripts.TrimStart())
            .Replace("{HEAD}", config.HtmlHead)
            .Replace("{BODY}", config.HtmlBody);

        if (htmlName is not null)
            File.WriteAllText(Path.Combine(outputDir, htmlName), Render(js.ToString()), utf8);
        if (htmlMinName is not null)
            File.WriteAllText(Path.Combine(outputDir, htmlMinName), Render(jsMin.ToString()), utf8);
    }
}
