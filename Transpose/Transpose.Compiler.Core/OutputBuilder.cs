using System.Text;
using System.Text.Json;

namespace Transpose.Compiler;

/// <summary>
/// Assembles a runnable Transpose site from a translation: writes the compiled bundle, extracts the
/// JavaScript each referenced package embeds (tps.js, newtonsoft.json.js, … — listed in the
/// assembly's <c>Transpose.Resources.json</c>), copies the tps.json resource files (CSS/images), and
/// generates index.html — mirroring what the existing tps compiler produces.
///
/// When <c>outputFormatting</c> is <c>Minified</c> or <c>Both</c>, the JS this compilation itself
/// produces — the compiled bundle, its reflection metadata and the TransposeR shim — is minified with
/// NUglify into a <c>.min.js</c> sibling. A referenced package's JS is never minified here: the
/// package already ships a pre-minified variant of its own compiled code, and that pair is simply
/// reused. The generated HTML then follows the legacy compiler's rule: index.html links the formatted
/// variants, index.min.html links the minified variants, and the active build configuration collapses
/// the pair to a single index.html (Release keeps the minified one, Debug the formatted one).
///
/// tps.json resource files are taken as authored — never minified, and never renamed — whether this
/// build reads them from disk or extracts them from a referenced package: a project ships both a
/// <c>foo.js</c> and a <c>foo.min.js</c> group when it wants both variants, exactly as before, and a
/// resource that exists in only one variant keeps that name in every build configuration.
///
/// Stylesheets are the one resource kind that is not copied through verbatim: every CSS file this
/// build writes — from a resource group, or extracted from a referenced package — has its comments
/// stripped by <see cref="CssProcessor"/>, as does every stylesheet embedded into a package DLL by
/// <see cref="CollectEmbeddableItems"/>. Nothing else about it changes; it is not minified.
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

    /// <summary>The outcome of assembling a site: the directory it was written to, and every stale
    /// file that <c>cleanOutputFolder</c> pruned (files from a previous build this one did not
    /// re-produce). <see cref="RemovedStaleFiles"/> is empty when cleaning is disabled or nothing was
    /// stale.</summary>
    public readonly record struct SiteBuildResult(string OutputDir, IReadOnlyList<string> RemovedStaleFiles);

    /// <summary>
    /// One stylesheet a site build produces <em>from files on disk</em> — a <c>tps.json</c> resource
    /// group of the root project or of any project in its closure — recorded as the output-relative
    /// path it is written to plus the source files that produce it. <see cref="Concatenated"/>
    /// distinguishes a named bundle group (every source file joined into the one output, e.g.
    /// Tesserae's <c>tss.css</c>) from a copy-through group (each source copied under its own name).
    ///
    /// This is what makes a CSS-only rebuild possible: watch mode can reproduce exactly these files,
    /// byte for byte, without recompiling anything (see <see cref="WriteCssResources"/>). CSS a
    /// referenced <em>package</em> embeds is deliberately not here — changing that means rebuilding
    /// the package, which is a real build.
    /// </summary>
    public readonly record struct CssResource(string OutputRelativePath, IReadOnlyList<string> SourceFiles, bool Concatenated);

    public static SiteBuildResult Build(ResolvedProject project, TransposeJson config, string javascript, string outputDir, string configuration, string? metadataJavascript = null, string? liveReloadScript = null)
    {
        Directory.CreateDirectory(outputDir);

        var fmt = config.OutputFormatting;
        var wantFormatted = fmt != JsOutputFormatting.Minified;   // Formatted or Both
        var wantMinified  = fmt != JsOutputFormatting.Formatted;  // Minified or Both
        var minifyLocals  = project.MinifyLocalVariables;

        var jsOuts = new List<JsOut>();    // JS in load order (runtime → libs → resources → app)
        var cssLinks = new List<string>();

        var utf8 = new UTF8Encoding(false);

        // Every file this build writes, by full path. After the site is assembled, cleanOutputFolder
        // diffs *this project's own* previous output (a persisted manifest) against this set and prunes
        // only what this project wrote last time but no longer writes — never a file some other project
        // or package placed in a shared output folder. All disk writes below funnel through WriteText or
        // the resource/extract helpers, each of which records here.
        var written = new HashSet<string>(PathComparer);

        void WriteText(string rel, string content)
        {
            var dest = Path.Combine(outputDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest, content, utf8);
            written.Add(Path.GetFullPath(dest));
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

        // Routes a package's embedded JS into the output — the runtime bundles and compiled library
        // code, and the authored JavaScript the library shipped through its own tps.json `resources`.
        //
        // Only a file that ships in BOTH variants — `x.js` next to `x.min.js` — takes part in the
        // formatted/minified switch: the formatted one is written and linked from index.html, the
        // pre-minified one from index.min.html, and neither is re-minified here. That pair is exactly
        // what a Transpose-compiled bundle looks like (CollectEmbeddableItems embeds both), and it is
        // also how a library declares an authored bundle it wants both variants of.
        //
        // A file that ships ALONE is an authored resource — Monaco's editor.main.js, a vendored
        // d3.min.js — and no other variant of it exists to switch to. It is copied through verbatim,
        // under the name it was authored with, in every build configuration. Renaming editor.main.js to
        // editor.main.min.js for a Minified build (or leaving d3.min.js out of a Formatted one) breaks
        // every consumer that fetches the file by path — a module loader, a `new Worker(...)`, an
        // import map — none of which this compiler rewrites. It matches what the same resource group
        // does when it is built from disk rather than extracted from a package (ProcessResourceGroup).
        void RoutePackageJs(IReadOnlyList<EmbeddedJs> jsFiles)
        {
            var present = jsFiles.Select(f => f.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Tolerate a duplicate file name in an older/hand-authored manifest (last wins) instead of
            // throwing — the embed side dedupes, but a package built before that fix could still carry one.
            var byName = new Dictionary<string, EmbeddedJs>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in jsFiles) byName[f.FileName] = f;

            foreach (var f in jsFiles)
            {
                if (!present.Contains(CounterpartName(f.FileName)))
                {
                    // An authored resource: one variant, copied through under its own name, always.
                    WriteText(f.Rel, f.Text);
                    // A standalone .min.js is still a Release-only script as far as index.html goes,
                    // matching how the same group routes from disk. A .dontload resource is on disk
                    // (above) but never injected into either HTML.
                    if (f.Load) jsOuts.Add(new JsOut { Path = f.Rel, IsMinified = IsMinifiedName(f.FileName) });
                    continue;
                }

                // The minified half of a pair is written below, by its formatted sibling.
                if (IsMinifiedName(f.FileName)) continue;

                var o = new JsOut { Path = f.Rel };
                if (wantFormatted) WriteText(f.Rel, f.Text); else o.IsEmpty = true;
                if (wantMinified)
                {
                    var pre = byName[CounterpartName(f.FileName)];   // pre-built .min.js from the package
                    WriteText(pre.Rel, pre.Text);
                    o.MinifiedPath = pre.Rel;
                }
                if (f.Load) jsOuts.Add(o);
            }
        }

        // In separate-assembly mode, referenced *projects* are consumed as DLLs (their JS is
        // extracted, not recompiled). Both package DLLs and project DLLs contribute embedded JS.
        var projectDlls = new HashSet<string>(project.ReferencedProjectDlls, StringComparer.OrdinalIgnoreCase);

        // 1. Every referenced assembly's embedded JS, in dependency order — a library must load
        //    before anything that depends on it (Transpose runtime → Transpose.Core → Tesserae →
        //    Tesserae.GraphKit → Curiosity.FrontEnd.Core → …API → …FrontEnd). TopologicalOrder does a
        //    post-order walk of the assembly reference graph (dependencies first), matching how the
        //    legacy compiler loaded assemblies. The TransposeR shim loads immediately after the
        //    Transpose runtime (tps.js) and before any generated code that calls into it.
        foreach (var dll in TopologicalOrder(project.ReferencePaths))
        {
            RoutePackageJs(projectDlls.Contains(dll)
                ? ExtractProjectDllResources(dll, outputDir, cssLinks, utf8, written)
                : ExtractEmbeddedJs(dll, outputDir, cssLinks, written));

            if (string.Equals(Path.GetFileNameWithoutExtension(dll), "Transpose", StringComparison.OrdinalIgnoreCase))
                EmitCompilerJs("tps.shim.js", RoslynTranslator.RuntimeShim);
        }

        // 2. tps.json resource files from every project in the closure — referenced projects
        //    first (a library's JS deps load before the app that uses them). A resource group
        //    whose name is a .js/.css file concatenates its files into that one bundle; other
        //    groups (globbed images, etc.) copy each file through. Resource JS is taken as authored:
        //    a .js group routes to index.html, a .min.js group to index.min.html. Resource CSS is
        //    taken as authored too, minus its comments (CssProcessor).
        foreach (var projectDir in Enumerable.Reverse(project.ProjectDirs))
        {
            var cfg = projectDir == project.ProjectDir ? config : TransposeJson.TryLoad(projectDir, configuration);
            if (cfg is null) continue;
            foreach (var group in cfg.Resources)
                ProcessResourceGroup(projectDir, outputDir, group, jsOuts, cssLinks, written);
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
            WriteHtml(project, config, outputDir, jsOuts, cssLinks, configuration, utf8, written, liveReloadScript);

        // 5. Prune only files THIS project authored in an earlier build and no longer writes — read
        //    from its own manifest — then persist the current file list as the next manifest. Files
        //    other projects/packages/tools placed in a shared output folder are never in this project's
        //    manifest, so they are never touched.
        var manifestPath = ManifestPath(outputDir, project.AssemblyName);
        var removed = config.CleanOutputFolder
            ? PruneStaleFiles(outputDir, written, ReadManifest(manifestPath, outputDir), config.CleanOutputFolderExclude)
            : Array.Empty<string>();

        WriteManifest(manifestPath, outputDir, written, utf8);

        return new SiteBuildResult(outputDir, removed);
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
        var metaName = metadataJavascript is not null
            ? Path.GetFileNameWithoutExtension(mainJsName) + ".meta.js"
            : null;

        // A tps.json `resources` group may re-declare the project's OWN compiled output — e.g.
        //   { "name": "<Assembly>.js.dontload", "files": [ "$(OutDir)tps/<Assembly>.js" ] } —
        // which isn't on disk during compilation. Such a self-reference maps to the in-memory bundle
        // (or its .meta.js), and its parsed name/load flag win: the `.dontload` variant is embedded so
        // consumers copy it but don't auto-load it (the module is lazy-loaded at runtime). When the
        // resources section re-declares a bundle this way, the default auto-embed of that same bundle
        // is suppressed — matching the legacy compiler, where a `resources` section opts out of the
        // default embed and must re-list exactly what it wants (min and/or formatted).
        bool mainDeclared = false, metaDeclared = false;

        // Maps a self-referenced compiled-output leaf name to its in-memory text, or null if the leaf
        // isn't one of this project's own outputs. Also records which bundle was referenced.
        string? SelfText(string leaf)
        {
            if (SameFile(leaf, mainJsName)) { mainDeclared = true; return javascript; }
            if (SameFile(leaf, ToMinName(mainJsName))) { mainDeclared = true; return JsMinifier.Minify(javascript, mainJsName, minifyLocalVariables); }
            if (metaName is not null)
            {
                if (SameFile(leaf, metaName)) { metaDeclared = true; return metadataJavascript!; }
                if (SameFile(leaf, ToMinName(metaName))) { metaDeclared = true; return JsMinifier.Minify(metadataJavascript!, metaName, minifyLocalVariables); }
            }
            return null;
        }

        foreach (var group in config.Resources)
        {
            // Resolve the "module#file" grouping and the load flag ("load": false / a ".dontload"
            // name) so a referencing project extracts the resource under its real name and knows
            // whether to inject it into index.html. The flag travels in the DLL's resource manifest,
            // which is how it survives packing.
            var (name, load) = ResolveResource(group);
            var destSub = string.IsNullOrEmpty(group.Output) ? null : group.Output!.Replace('\\', '/');

            var isBundle = name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                           || name.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
            // A stylesheet is comment-stripped before it is embedded, exactly as the site build strips
            // it before writing it to disk (WriteResource) — the two paths must produce the same bytes,
            // since a consuming project extracts what is embedded here.
            var css = IsCss(name);

            if (isBundle)
            {
                // Resolve each declared file to text, mapping a self-reference to the in-memory bundle.
                var texts = new List<string>();
                foreach (var raw in group.Files)
                {
                    var self = SelfText(Path.GetFileName(raw.Replace('\\', '/')));
                    if (self is not null) texts.Add(self);
                    else texts.AddRange(ExpandGlob(projectDir, raw).Select(f => ReadResourceText(f, css)));
                }
                if (texts.Count == 0) continue;
                items.Add(new EmbeddedItem(name, utf8.GetBytes(string.Join("\n", texts)), destSub, load));
            }
            else
            {
                // Copy-through group (images, fonts): embed each file under its own name.
                var files = group.Files.SelectMany(p => ExpandGlob(projectDir, p)).ToList();
                if (files.Count == 0) continue;
                foreach (var src in files)
                {
                    var content = File.ReadAllBytes(src);
                    if (IsCss(src)) content = CssProcessor.StripComments(content);
                    items.Add(new EmbeddedItem(Path.GetFileName(src), content, destSub, load));
                }
            }
        }

        // Default embed of the compiled bundle + reflection metadata (each in a formatted and a
        // pre-minified variant), prepended so the main bundle stays first — UNLESS the resources
        // section already re-declared it above. Shipping the .min.js is deliberate: minifying library
        // code is work the consumer would otherwise repeat on every build.
        var defaults = new List<EmbeddedItem>();
        if (!mainDeclared)
        {
            defaults.Add(new EmbeddedItem(mainJsName, utf8.GetBytes(javascript), null));
            if (config.OutputFormatting != JsOutputFormatting.Formatted)
                defaults.Add(new EmbeddedItem(ToMinName(mainJsName), utf8.GetBytes(JsMinifier.Minify(javascript, mainJsName, minifyLocalVariables)), null));
        }
        if (metaName is not null && !metaDeclared)
        {
            defaults.Add(new EmbeddedItem(metaName, utf8.GetBytes(metadataJavascript!), null));
            if (config.OutputFormatting != JsOutputFormatting.Formatted)
                defaults.Add(new EmbeddedItem(ToMinName(metaName), utf8.GetBytes(JsMinifier.Minify(metadataJavascript!, metaName, minifyLocalVariables)), null));
        }

        defaults.AddRange(items);

        // Dedupe by output name: a resource manifest is keyed by name, so two groups that resolve to
        // the same file (e.g. the base tps.json embeds Curiosity.FrontEnd.meta.js from .meta.js while
        // the tps.Release.json overlay re-declares it from .meta.min.js) must collapse to one. The
        // overlay is concatenated after the base, so last-wins gives it override precedence; we keep
        // each name at its first position to preserve load order.
        var seen = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        var deduped = new List<EmbeddedItem>(defaults.Count);
        foreach (var item in defaults)
        {
            if (seen.TryGetValue(item.Name, out var at)) deduped[at] = item;   // override in place
            else { seen[item.Name] = deduped.Count; deduped.Add(item); }
        }
        return deduped;
    }

    /// <summary>A JavaScript resource loaded from a package (a runtime bundle or embedded library
    /// code), with its file name (for pairing a formatted variant with its <c>.min.js</c>), the
    /// output-relative path it extracts to, and its text.</summary>
    private readonly record struct EmbeddedJs(string FileName, string Rel, string Text, bool Load = true);

    /// <summary>
    /// Read-only access to one assembly's embedded resources, opened and closed around the extraction —
    /// deliberately <em>not</em> via <c>Assembly.LoadFrom</c>.
    ///
    /// Loading an assembly to read its resources has two consequences a build cannot live with, both of
    /// which only show up in a long-running process (i.e. <c>tps --watch</c>, or a host embedding the
    /// compiler): the file stays locked for the lifetime of the process, so the *next* rebuild of that
    /// referenced project fails to write its DLL; and the runtime resolves by assembly identity, so a
    /// second read of a rebuilt DLL silently returns the copy loaded the first time. Reading the metadata
    /// with Mono.Cecil — already how <see cref="TopologicalOrder"/> inspects these same assemblies — has
    /// neither problem: nothing is loaded into the process, and the handle is released on Dispose.
    /// </summary>
    private sealed class AssemblyResources : IDisposable
    {
        private readonly Mono.Cecil.AssemblyDefinition _assembly;
        private readonly Dictionary<string, Mono.Cecil.EmbeddedResource> _byName;

        private AssemblyResources(Mono.Cecil.AssemblyDefinition assembly)
        {
            _assembly = assembly;
            _byName = new Dictionary<string, Mono.Cecil.EmbeddedResource>(StringComparer.Ordinal);
            var names = new List<string>();
            foreach (var resource in assembly.MainModule.Resources.OfType<Mono.Cecil.EmbeddedResource>())
            {
                if (_byName.TryAdd(resource.Name, resource)) names.Add(resource.Name);
            }
            Names = names;
        }

        /// <summary>Opens <paramref name="dllPath"/> for reading, or returns null if it is not a readable
        /// assembly — a missing or half-written DLL is a condition the site build reports by simply
        /// contributing nothing, exactly as it did before.</summary>
        public static AssemblyResources? TryOpen(string dllPath)
        {
            try { return new AssemblyResources(Mono.Cecil.AssemblyDefinition.ReadAssembly(dllPath)); }
            catch { return null; }
        }

        /// <summary>Every embedded resource's name, in metadata order.</summary>
        public IReadOnlyList<string> Names { get; }

        public Stream Open(string name) => _byName[name].GetResourceStream();

        public void Dispose() => _assembly.Dispose();
    }

    /// <summary>
    /// Extracts a referenced project assembly's embedded resources (listed in its
    /// Transpose.Resources.json manifest) into the output folder: CSS is written and linked here,
    /// and the JS resources are returned for <c>RoutePackageJs</c> to place (which picks the
    /// formatted vs pre-minified variant per build). Non-JS/CSS resources (images, fonts) are
    /// copied through under their output subdirectory.
    /// </summary>
    private static IReadOnlyList<EmbeddedJs> ExtractProjectDllResources(
        string dllPath, string outputDir, List<string> cssLinks, UTF8Encoding utf8, HashSet<string> written)
    {
        var jsFiles = new List<EmbeddedJs>();
        if (!File.Exists(dllPath)) return jsFiles;
        using var asm = AssemblyResources.TryOpen(dllPath);
        if (asm is null) return jsFiles;

        var names = asm.Names;
        var manifestName = names.FirstOrDefault(n => n.EndsWith("Transpose.Resources.json", StringComparison.OrdinalIgnoreCase));
        if (manifestName is null) return jsFiles;

        List<(string fileName, string? path, bool load)> entries;
        using (var ms = asm.Open(manifestName))
        using (var sr = new StreamReader(ms))
        using (var doc = JsonDocument.Parse(sr.ReadToEnd(), new JsonDocumentOptions { AllowTrailingCommas = true }))
        {
            entries = doc.RootElement.EnumerateArray()
                .Select(e => (
                    fileName: e.TryGetProperty("FileName", out var f) ? f.GetString() : null,
                    path: e.TryGetProperty("Path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
                    load: !e.TryGetProperty("Load", out var l) || l.ValueKind != JsonValueKind.False))   // absent → true
                .Where(x => !string.IsNullOrEmpty(x.fileName))
                .Select(x => (x.fileName!, x.path, x.load))
                .ToList();
        }

        string Rel(string fileName, string? path)
            => string.IsNullOrEmpty(path) ? fileName : path!.Replace('\\', '/').TrimEnd('/') + "/" + fileName;

        foreach (var (fileName, path, load) in entries)
        {
            var resName = names.FirstOrDefault(n => n.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                          ?? names.FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
            if (resName is null) continue;

            var rel = Rel(fileName, path);

            if (rel.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            {
                using var s = asm.Open(resName);
                using var reader = new StreamReader(s);
                jsFiles.Add(new EmbeddedJs(fileName, rel, reader.ReadToEnd(), load));
            }
            else
            {
                // CSS and copy-through resources (images, fonts): write to disk now.
                ExtractResourceFile(asm, resName, outputDir, rel, written);
                if (load && IsCss(rel)) cssLinks.Add(rel);
            }
        }
        return jsFiles;
    }

    /// <summary>
    /// Writes one non-JavaScript embedded resource to the site: the raw bytes, so a binary asset (a
    /// font, an image) stays intact, except for a stylesheet, which goes through
    /// <see cref="CssProcessor"/> on the way out. A package built by an older compiler still carries
    /// its comments, so the strip happens on extraction and not only on the embed side.
    /// </summary>
    private static void ExtractResourceFile(
        AssemblyResources asm, string resourceName, string outputDir, string rel, HashSet<string> written)
    {
        var dest = Path.Combine(outputDir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        using (var s = asm.Open(resourceName))
        {
            if (IsCss(rel))
            {
                using var buffer = new MemoryStream();
                s.CopyTo(buffer);
                File.WriteAllBytes(dest, CssProcessor.StripComments(buffer.ToArray()));
            }
            else
            {
                using var fs = File.Create(dest);
                s.CopyTo(fs);
            }
        }
        written.Add(Path.GetFullPath(dest));
    }

    /// <summary>
    /// What a <c>tps.json</c> resource group produces: the output file name, and whether the generated
    /// <c>index.html</c> references it (a <c>&lt;script&gt;</c> for JavaScript, a
    /// <c>&lt;link rel=stylesheet&gt;</c> for CSS — the only two resource kinds the HTML can load).
    /// Either way the file is written to the output and embedded into a package DLL; a
    /// non-loaded resource is simply left for the application to fetch itself.
    ///
    /// Two spellings say "don't load", and they are AND-ed so either one alone suppresses the
    /// injection: the declarative <c>"load": false</c> on the group, and the legacy <c>.dontload</c>
    /// suffix on its <c>name</c> (see <see cref="ParseResourceName"/>). The single place both are
    /// resolved, so the site build (<see cref="ProcessResourceGroup"/>) and the package embed
    /// (<see cref="CollectEmbeddableItems"/>) cannot disagree — which is what makes the flag survive
    /// packing: the resolved value is what lands in the DLL's resource manifest as <c>Load</c>, and a
    /// referencing project's site build honours it there.
    /// </summary>
    internal static (string fileName, bool load) ResolveResource(TransposeJson.ResourceGroup group)
    {
        var (name, loadFromName) = ParseResourceName(group.Name ?? "");
        return (name, loadFromName && group.Load);
    }

    /// <summary>
    /// Parses a tps.json resource group's <c>name</c> into the output file name and whether it should
    /// be injected into index.html, mirroring the legacy compiler's conventions:
    /// <list type="bullet">
    /// <item><c>module#file.js</c> — the part before <c>#</c> is a grouping label; the output name is
    /// the part after it (<c>file.js</c>).</item>
    /// <item><c>file.js.dontload</c> — the <c>.dontload</c> suffix marks a resource that is copied to
    /// the output but NOT referenced from index.html (loaded on demand); the output name drops the
    /// suffix (<c>file.js</c>).</item>
    /// </list>
    /// Callers that need the group's effective load flag go through <see cref="ResolveResource"/>,
    /// which folds in the group's own <c>load</c> property.
    /// </summary>
    internal static (string fileName, bool load) ParseResourceName(string rawName)
    {
        var name = rawName ?? "";
        var hash = name.IndexOf('#');
        if (hash >= 0) name = name.Substring(hash + 1);   // drop the "module#" grouping prefix
        var load = true;
        if (name.EndsWith(".dontload", StringComparison.OrdinalIgnoreCase))
        {
            load = false;
            name = name.Substring(0, name.Length - ".dontload".Length);
        }
        return (name, load);
    }

    /// <summary>A file name for a minified bundle (ends in <c>.min.js</c>/<c>.min.css</c>).</summary>
    private static bool IsMinifiedName(string name)
        => name.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase)
           || name.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a file name is a stylesheet (<c>.css</c>/<c>.min.css</c>).</summary>
    private static bool IsCss(string name) => name.EndsWith(".css", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>Case-insensitive file-name equality (used to spot a tps.json resource that
    /// self-references the project's own compiled output by leaf name).</summary>
    private static bool SameFile(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

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
        List<JsOut> jsOuts, List<string> cssLinks, HashSet<string> written)
    {
        // The output name (the group name minus its "module#" grouping prefix and ".dontload" suffix)
        // plus the effective load flag: a non-loaded resource is written to the output but never
        // referenced from index.html — neither as a <script> nor as a stylesheet <link>.
        var (name, load) = ResolveResource(group);
        var isBundle = IsBundleName(name);

        foreach (var (rel, sources) in ResourceGroupOutputs(projectDir, group))
        {
            WriteResource(outputDir, rel, sources, concatenate: isBundle, written);
            if (!load) continue;   // written to disk already; not injected into index.html

            if (IsCss(rel)) cssLinks.Add(rel);
            // Resource JS is taken as authored (never re-minified): a .min.js links only from
            // index.min.html; a plain .js links only from index.html — matching the legacy compiler.
            else if (rel.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                jsOuts.Add(new JsOut { Path = rel, IsMinified = IsMinifiedName(rel) });
        }
    }

    /// <summary>Whether a resource group's <c>name</c> makes it a <em>bundle</em> group — every file it
    /// lists is concatenated into that one named output — rather than a copy-through group, where each
    /// listed file is copied under its own file name.</summary>
    private static bool IsBundleName(string name)
        => name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
           || name.EndsWith(".css", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What one <c>tps.json</c> resource group resolves to on disk: the output-relative path of each
    /// file it produces, and the source file(s) behind it. A bundle group yields exactly one output
    /// (its declared name, fed by every file it lists); a copy-through group yields one output per
    /// file. Empty when the group's globs match nothing.
    ///
    /// The single place that maps a group to its outputs, so the site build
    /// (<see cref="ProcessResourceGroup"/>) and the CSS-only rebuild path
    /// (<see cref="CssResources"/>) can never disagree about where a file lands.
    /// </summary>
    private static List<(string rel, List<string> sources)> ResourceGroupOutputs(
        string projectDir, TransposeJson.ResourceGroup group)
    {
        var outputs = new List<(string, List<string>)>();
        var destSub = (group.Output ?? "").Replace('\\', '/').TrimEnd('/');
        var files = group.Files.SelectMany(p => ExpandGlob(projectDir, p)).ToList();
        if (files.Count == 0) return outputs;

        string Rel(string leaf) => string.IsNullOrEmpty(destSub) ? leaf : destSub + "/" + leaf;

        var (name, _) = ResolveResource(group);
        if (IsBundleName(name))
        {
            outputs.Add((Rel(name), files));
        }
        else
        {
            foreach (var src in files)
                outputs.Add((Rel(Path.GetFileName(src)), new List<string> { src }));
        }
        return outputs;
    }

    /// <summary>Writes one resource-group output: a bundle group's files joined with newlines, or a
    /// single file copied through byte for byte. A stylesheet goes through <see cref="CssProcessor"/>
    /// first, so no authoring comment reaches the site — bundled or copied through alike, and per
    /// source file so an unterminated comment in one cannot swallow the next.
    /// <paramref name="written"/> is null for a CSS-only rebuild, which rewrites files the last full
    /// build already recorded in its manifest.</summary>
    private static void WriteResource(
        string outputDir, string rel, IReadOnlyList<string> sources, bool concatenate, HashSet<string>? written)
    {
        var dest = Path.Combine(outputDir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var css = IsCss(rel);
        if (concatenate)
            File.WriteAllText(dest, string.Join("\n", sources.Select(s => ReadResourceText(s, css))));
        else if (css) File.WriteAllBytes(dest, CssProcessor.StripComments(File.ReadAllBytes(sources[0])));
        else File.Copy(sources[0], dest, overwrite: true);
        written?.Add(Path.GetFullPath(dest));
    }

    /// <summary>One bundle member's text, comment-stripped when it is a stylesheet.</summary>
    private static string ReadResourceText(string path, bool css)
    {
        var text = File.ReadAllText(path);
        return css ? CssProcessor.StripComments(text) : text;
    }

    /// <summary>
    /// Every stylesheet this project's site build produces from files on disk, across the whole project
    /// closure. Two sources contribute, and both resolve a group to the same output path:
    ///
    /// <list type="bullet">
    /// <item>the projects whose <c>tps.json</c> resources <see cref="Build"/> reads directly
    /// (<see cref="ResolvedProject.ProjectDirs"/> — in separate-assembly mode, just the root);</item>
    /// <item>every project the root <em>references</em>, whose stylesheets reach the site as resources
    /// embedded in its package DLL. The embedded copy is a byte-for-byte concatenation of the same files
    /// under the same group name and <c>output</c> directory, so re-copying from disk lands the same
    /// bytes at the same path that a full rebuild (recompiling that dependency, re-embedding, and
    /// re-extracting) would have.</item>
    /// </list>
    ///
    /// Watch mode captures this after each successful build so it can tell a change to a CSS source it
    /// already knows about from one that needs a real rebuild.
    /// </summary>
    public static List<CssResource> CssResources(ResolvedProject project, TransposeJson config, string configuration)
    {
        var css = new List<CssResource>();
        var seen = new HashSet<string>(PathComparer);

        void Scan(string projectDir)
        {
            if (!seen.Add(Path.GetFullPath(projectDir))) return;
            var cfg = projectDir == project.ProjectDir ? config : TransposeJson.TryLoad(projectDir, configuration);
            if (cfg is null) return;
            foreach (var group in cfg.Resources)
            {
                var isBundle = IsBundleName(ResolveResource(group).fileName);
                foreach (var (rel, sources) in ResourceGroupOutputs(projectDir, group))
                    if (IsCss(rel)) css.Add(new CssResource(rel, sources, isBundle));
            }
        }

        // Referenced projects first, then the project's own dirs with the root last — the precedence
        // Build applies (a package's extracted resources are written before the root's own, and the
        // root's tps.json wins a collision).
        foreach (var dep in ProjectResolver.ReferencedProjectsInBuildOrder(project.CsprojPath))
            if (Path.GetDirectoryName(dep) is { } dir) Scan(dir);
        foreach (var projectDir in Enumerable.Reverse(project.ProjectDirs))
            Scan(projectDir);

        return css;
    }

    /// <summary>Rewrites the given stylesheets into an already-assembled site, exactly as
    /// <see cref="Build"/> would have — same concatenation, same encoding, same destination — so a
    /// CSS-only update leaves the site byte-identical to what a full rebuild would have produced.
    /// Nothing else in the output is touched, and no manifest entry changes (these paths were already
    /// recorded by the build that produced them).</summary>
    public static void WriteCssResources(string outputDir, IEnumerable<CssResource> resources)
    {
        foreach (var resource in resources)
            WriteResource(outputDir, resource.OutputRelativePath, resource.SourceFiles, resource.Concatenated, written: null);
    }

    /// <summary>
    /// Orders reference assemblies so a dependency always loads before anything that depends on it —
    /// the load order the emitted JavaScript needs (e.g. Tesserae before Tesserae.GraphKit). Does a
    /// post-order depth-first walk of the assembly reference graph (each assembly's Cecil
    /// AssemblyReferences, restricted to the set given), so dependencies are yielded first. This
    /// mirrors the legacy compiler, which loaded referenced assemblies depth-first (deepest first).
    /// Ties (independent assemblies) keep the input order; unreadable assemblies fall back to name.
    /// </summary>
    private static List<string> TopologicalOrder(IReadOnlyList<string> dllPaths)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);      // asm name → path
        var deps   = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase); // asm name → referenced asm names
        var orderIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);      // stable tie-break

        var i = 0;
        foreach (var path in dllPaths)
        {
            string name;
            List<string> references;
            try
            {
                using var ad = Mono.Cecil.AssemblyDefinition.ReadAssembly(path);
                name = ad.Name.Name;
                references = ad.MainModule.AssemblyReferences.Select(r => r.Name).ToList();
            }
            catch { name = Path.GetFileNameWithoutExtension(path); references = new List<string>(); }

            if (!byName.ContainsKey(name)) orderIndex[name] = i++;
            byName[name] = path;
            deps[name] = references;
        }

        var ordered = new List<string>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string name)
        {
            if (!done.Add(name)) return;
            if (!visiting.Add(name)) return;          // guard against reference cycles
            if (deps.TryGetValue(name, out var refs))
                foreach (var r in refs.Where(byName.ContainsKey).OrderBy(r => orderIndex[r]))
                    Visit(r);
            visiting.Remove(name);
            if (byName.TryGetValue(name, out var path)) ordered.Add(path);
        }

        foreach (var name in byName.Keys.OrderBy(n => orderIndex[n]))
            Visit(name);
        return ordered;
    }

    /// <summary>
    /// Extracts a referenced package's embedded web resources, in the order its
    /// Transpose.Resources.json lists them. Only <b>JavaScript</b> is returned (as text) for
    /// <c>RoutePackageJs</c> to place and minify; CSS and copy-through resources (fonts, images, …)
    /// are written straight to <paramref name="outputDir"/> here — binary-safe, never decoded as text
    /// and never handed to the JS minifier — with stylesheets also added to <paramref name="cssLinks"/>.
    /// Each resource is placed under the output subdirectory (<c>Path</c>) its manifest entry declares
    /// — the <c>output</c> from the library's tps.json resource group, e.g. <c>assets/fonts</c> — so a
    /// package's folder layout is preserved on the consumer side (and CSS <c>url(...)</c> references
    /// into sibling folders keep resolving). A <c>Path</c>-less entry sits at the site root (runtime
    /// bundles like <c>tps.js</c>). When a package has no manifest, only its <c>.js</c>/<c>.css</c>
    /// resources are surfaced, at the site root (other resource types cannot be identified reliably,
    /// and no per-resource output subdirectory is recorded, without the manifest).
    /// </summary>
    private static IReadOnlyList<EmbeddedJs> ExtractEmbeddedJs(string dllPath, string outputDir, List<string> cssLinks, HashSet<string> written)
    {
        var jsFiles = new List<EmbeddedJs>();
        using var asm = AssemblyResources.TryOpen(dllPath);
        if (asm is null) return jsFiles;

        var resourceNames = asm.Names;
        var manifest = resourceNames.FirstOrDefault(n => n.EndsWith("Transpose.Resources.json", StringComparison.OrdinalIgnoreCase));

        // Each entry: the resource's FileName (manifest key), the output subdirectory it extracts to
        // (Path — null/empty means the site root), and whether it is injected into index.html (Load).
        // A package without a manifest (the base runtime) surfaces only its .js/.css resources at the
        // site root — other resource types can't be identified, nor their output path recovered, without it.
        List<(string fileName, string? path, bool load)> entries;
        if (manifest is not null)
        {
            using var ms = asm.Open(manifest);
            using var sr = new StreamReader(ms);
            using var doc = JsonDocument.Parse(sr.ReadToEnd(), new JsonDocumentOptions { AllowTrailingCommas = true });
            entries = doc.RootElement.EnumerateArray()
                .Select(e => (
                    fileName: e.TryGetProperty("FileName", out var f) ? f.GetString() : null,
                    path: e.TryGetProperty("Path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
                    load: !e.TryGetProperty("Load", out var l) || l.ValueKind != JsonValueKind.False))   // absent → true
                .Where(x => !string.IsNullOrEmpty(x.fileName))
                .Select(x => (x.fileName!, x.path, x.load))
                .ToList();
        }
        else
        {
            entries = resourceNames
                .Where(n => n.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                            || n.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                .Select(n => (n, (string?)null, true))
                .ToList();
        }

        foreach (var (fileName, path, load) in entries)
        {
            var resName = resourceNames.FirstOrDefault(n => n.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                          ?? resourceNames.FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
            if (resName is null) continue;

            // Place the resource under its declared output subdirectory (Path). The manifest FileName
            // may carry an assembly-qualified prefix, so use just its leaf; an empty Path (runtime
            // bundles, or a resource group without an `output`) leaves the resource at the site root.
            var leaf = Path.GetFileName(fileName);
            var rel = string.IsNullOrEmpty(path) ? leaf : path!.Replace('\\', '/').TrimEnd('/') + "/" + leaf;

            if (rel.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            {
                using var s = asm.Open(resName);
                using var reader = new StreamReader(s);
                jsFiles.Add(new EmbeddedJs(leaf, rel, reader.ReadToEnd(), load));   // JS: placed/minified by RoutePackageJs
            }
            else
            {
                // CSS and copy-through resources (fonts, images): copy the raw bytes to disk so binary
                // assets stay intact, and link stylesheets. These must never reach the JS minifier.
                ExtractResourceFile(asm, resName, outputDir, rel, written);
                if (load && IsCss(rel)) cssLinks.Add(rel);
            }
        }
        return jsFiles;
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
        List<JsOut> jsOuts, List<string> cssLinks, string configuration, UTF8Encoding utf8, HashSet<string> written,
        string? liveReloadScript = null)
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

        // Watch mode (--watch) passes a small inline script that opens a websocket back to the tps
        // dev server and reloads the page when a rebuild completes. Appended right before </body> so
        // it runs after everything else on the page, and left out entirely for a normal build (the
        // parameter is null), so ordinary output is unaffected.
        string Render(string scripts)
        {
            var html = HtmlTemplate
                .Replace("{META}", config.HtmlMeta)
                .Replace("{TITLE}", config.HtmlTitle ?? project.AssemblyName)
                .Replace("{CSS}", css.ToString().TrimStart())
                .Replace("{SCRIPT}", scripts.TrimStart())
                .Replace("{HEAD}", config.HtmlHead)
                .Replace("{BODY}", config.HtmlBody);
            return liveReloadScript is null ? html : html.Replace("</body>", liveReloadScript + "\n</body>");
        }

        if (htmlName is not null)
        {
            var dest = Path.Combine(outputDir, htmlName);
            File.WriteAllText(dest, Render(js.ToString()), utf8);
            written.Add(Path.GetFullPath(dest));
        }
        if (htmlMinName is not null)
        {
            var dest = Path.Combine(outputDir, htmlMinName);
            File.WriteAllText(dest, Render(jsMin.ToString()), utf8);
            written.Add(Path.GetFullPath(dest));
        }
    }

    /// <summary>
    /// Compares output paths for the stale-file diff. Linux file systems are case-sensitive; Windows
    /// and macOS are not — so match the host, otherwise a rebuilt <c>App.js</c> could be mistaken for
    /// a stale <c>app.js</c> (or a genuinely stale file could survive) on a case-insensitive volume.
    /// </summary>
    internal static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// The "clean output folder" step: prunes files THIS project authored in an <em>earlier</em> build
    /// (recorded in <paramref name="previouslyWritten"/>, read from its own manifest) that the current
    /// build no longer writes (not in <paramref name="written"/>) — a removed resource, a renamed
    /// bundle, a <c>.min</c> variant no longer produced, a stale <c>index.min.html</c>. Directories it
    /// empties are removed too. Files matching an <paramref name="excludeGlobs"/> pattern are kept even
    /// when stale.
    ///
    /// Crucially, the candidate set is <em>this project's own</em> previous output, not "every file in
    /// the folder": a site output directory is routinely shared — several entry apps compile into one
    /// folder, and MSBuild or other tools drop assets there — and diffing against the whole folder
    /// would delete files this project never authored. Diffing against the project's manifest confines
    /// the prune to files it is actually responsible for. (The first build after upgrading from a
    /// compiler with no manifest simply writes one and prunes nothing — strictly safe.)
    ///
    /// Unlike the legacy h5 <c>cleanOutputFolderBeforeBuild</c> — which deleted by glob before
    /// compiling, risking loss of output when a build later failed — this runs after a successful
    /// assembly and can only ever remove files the current build did not produce. A delete that fails
    /// (a locked or read-only file) is skipped rather than failing the build. Returns the files
    /// removed, for reporting.
    /// </summary>
    internal static IReadOnlyList<string> PruneStaleFiles(
        string outputDir, HashSet<string> written, IReadOnlyCollection<string> previouslyWritten, IReadOnlyList<string> excludeGlobs)
    {
        var removed = new List<string>();
        var fullOut = Path.GetFullPath(outputDir);
        if (!Directory.Exists(fullOut) || previouslyWritten.Count == 0) return removed;

        var excludes = excludeGlobs.Where(g => !string.IsNullOrWhiteSpace(g)).Select(GlobToRegex).ToList();

        foreach (var full in previouslyWritten)
        {
            if (written.Contains(full)) continue;   // (re)written by this build — not stale
            if (!File.Exists(full)) continue;        // already gone (manual delete, another build)

            if (excludes.Count > 0)
            {
                var rel = Path.GetRelativePath(fullOut, full).Replace('\\', '/');
                var leaf = Path.GetFileName(full);
                if (excludes.Any(r => r.IsMatch(rel) || r.IsMatch(leaf))) continue;   // protected by cleanOutputFolderExclude
            }

            try { File.Delete(full); removed.Add(full); }
            catch { /* a locked/read-only file must not fail the build; leave it in place */ }
        }

        RemoveEmptyDirectories(fullOut);
        return removed;
    }

    /// <summary>
    /// The per-project build manifest: a hidden file in the output directory listing every file the
    /// project's last build wrote, so the next build knows exactly which files it is responsible for
    /// pruning. Keyed by assembly name (sanitised to a safe leaf) so several projects compiling into
    /// the same output folder each keep — and prune against — their own manifest without clobbering
    /// one another's. The manifest is never itself recorded in <c>written</c>, so it is never a prune
    /// candidate and never listed in the generated HTML.
    /// </summary>
    private static string ManifestPath(string outputDir, string assemblyName)
    {
        var safe = new string(assemblyName.Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray());
        if (safe.Length == 0) safe = "project";
        return Path.Combine(Path.GetFullPath(outputDir), $".tps-manifest.{safe}.json");
    }

    /// <summary>Reads the paths a previous build of this project wrote (full, normalised paths), or an
    /// empty set when there is no manifest yet (first build, or upgrade from a manifest-less compiler)
    /// or it can't be read — in which case nothing is pruned.</summary>
    private static IReadOnlyCollection<string> ReadManifest(string manifestPath, string outputDir)
    {
        var result = new HashSet<string>(PathComparer);
        if (!File.Exists(manifestPath)) return result;

        var fullOut = Path.GetFullPath(outputDir);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var rel = e.GetString();
                if (string.IsNullOrEmpty(rel)) continue;
                result.Add(Path.GetFullPath(Path.Combine(fullOut, rel.Replace('/', Path.DirectorySeparatorChar))));
            }
        }
        catch { /* a corrupt/unreadable manifest simply means "prune nothing this run" */ }
        return result;
    }

    /// <summary>Persists the files this build wrote as the next run's manifest, as output-relative,
    /// forward-slashed paths (portable if the site folder moves). A write failure is non-fatal — the
    /// next build just falls back to pruning nothing.</summary>
    private static void WriteManifest(string manifestPath, string outputDir, HashSet<string> written, UTF8Encoding utf8)
    {
        var fullOut = Path.GetFullPath(outputDir);
        try
        {
            var rels = written
                .Select(f => Path.GetRelativePath(fullOut, f).Replace('\\', '/'))
                .OrderBy(r => r, StringComparer.Ordinal)
                .ToList();
            var json = JsonSerializer.Serialize(rels, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(manifestPath, json, utf8);
        }
        catch { /* non-fatal: without a manifest the next build prunes nothing, which is safe */ }
    }

    /// <summary>Removes directories left empty by the prune, deepest first, keeping the output root.</summary>
    private static void RemoveEmptyDirectories(string root)
    {
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                                     .OrderByDescending(d => d.Length))   // longest path first ⇒ children before parents
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch { /* ignore: a concurrently-recreated or locked directory is not fatal */ }
        }
    }

    /// <summary>
    /// Compiles a <c>cleanOutputFolderExclude</c> glob into an anchored regex: <c>*</c> matches any
    /// run of characters (path separators included, so <c>assets/*</c> spans subfolders), <c>?</c>
    /// matches one character, everything else is literal. Case-insensitive on Windows/macOS to match
    /// <see cref="PathComparer"/>.
    /// </summary>
    private static System.Text.RegularExpressions.Regex GlobToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        foreach (var c in glob)
        {
            if (c == '*') sb.Append(".*");
            else if (c == '?') sb.Append('.');
            else sb.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString()));
        }
        sb.Append('$');
        var opts = System.Text.RegularExpressions.RegexOptions.CultureInvariant;
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            opts |= System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        return new System.Text.RegularExpressions.Regex(sb.ToString(), opts);
    }
}
