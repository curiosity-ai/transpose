using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using Transpose.Translator;

namespace Transpose.Compiler;

/// <summary>
/// The on-disk build cache behind <c>--incremental</c>: what the previous build of this project
/// produced, and the decision about how much of it the current build may reuse.
///
/// `tps` is a plain CLI — a fresh process per build, no compilation server (see CLAUDE.md) — so
/// nothing of Roslyn's bound state survives between builds. What *can* survive is the output of the
/// phases whose inputs are unchanged, and this class decides which those are. There are exactly three
/// verdicts:
///
/// <list type="bullet">
/// <item><b>UpToDate</b> — every input hashes the same as last time and every output file is still on
/// disk unchanged. Nothing is compiled at all.</item>
/// <item><b>BodyOnlyChange</b> — some files changed, but only inside method/accessor bodies: no
/// declaration was added, removed, renamed or re-signed anywhere. The unchanged files' types keep
/// their cached JavaScript, only the changed files are scanned and diagnosed, the reflection metadata
/// is reused, and (in a metadata-only configuration) so is the .NET assembly.</item>
/// <item><b>FullBuild</b> — anything else: a declaration moved, a file was added or removed, a
/// reference or a build setting changed, or there is no usable cache. The build runs from scratch and
/// writes a fresh cache.</item>
/// </list>
///
/// Why that middle tier is sound is argued in <see cref="IncrementalPlan"/>; this class's job is to
/// establish its precondition, conservatively. Every input that could change an output is in the
/// settings key or one of the per-file hashes, and anything unrecognised falls back to FullBuild.
///
/// The cache lives in the project's <c>obj/</c> (so <c>dotnet clean</c>, a `rm -rf obj`, and
/// <c>tps-bench</c>'s clean-slate scenarios all drop it), or wherever <c>--cache-dir</c> /
/// <c>TRANSPOSE_CACHE_DIR</c> points — a temp directory works fine, since a missing or stale cache
/// only ever costs a full build.
/// </summary>
internal sealed class BuildCache
{
    /// <summary>Bumped whenever the cache layout or the reuse rules change, so an older cache written
    /// by a different compiler is ignored rather than misread.</summary>
    private const int FormatVersion = 1;

    private const string ManifestFile = "manifest.json";
    private const string TypesFile = "types.js";
    private const string MetaFile = "meta.js";
    private const string InlineMetaFile = "inline-meta.js";
    private const string AssemblyFile = "assembly.bin";
    private const string DeniedNamesFile = "denied-names.txt";
    private const string BundleFile = "bundle.js";

    internal enum Verdict
    {
        /// <summary>Compile everything and write a fresh cache.</summary>
        FullBuild,

        /// <summary>Only method/accessor bodies changed — reuse everything declaration-derived.</summary>
        BodyOnlyChange,

        /// <summary>Nothing changed and the outputs are intact — do nothing.</summary>
        UpToDate,
    }

    private readonly string _dir;
    private readonly string _settingsKey;
    private readonly string _contentKey;
    private readonly Dictionary<string, string> _textHashes;
    private readonly ResolvedProject _project;
    private Manifest? _previous;

    private BuildCache(string dir, string settingsKey, string contentKey,
        Dictionary<string, string> textHashes, ResolvedProject project)
    {
        _dir = dir;
        _settingsKey = settingsKey;
        _contentKey = contentKey;
        _textHashes = textHashes;
        _project = project;
    }

    /// <summary>Where this project's cache lives.</summary>
    public string Directory => _dir;

    /// <summary>
    /// Opens (without validating) the cache for a project.
    ///
    /// There are two keys, because two different questions have to be answered. <paramref
    /// name="settings"/> is everything that can change *what this project compiles to* — the
    /// configuration, defines, language version, output mode, tps.json, reflection settings, and the
    /// declaration-level identity of every reference. A change there invalidates the whole cache.
    /// <paramref name="contentExtras"/> is what can change only *what this project has to write out* —
    /// most importantly the full byte content of a referenced Transpose package, whose embedded
    /// JavaScript is copied into the site verbatim. A change there cannot alter a single byte this
    /// project emits, but it does mean the outputs are stale, so the build must run and rewrite them
    /// while still reusing everything it emitted last time.
    /// </summary>
    /// <param name="mode">The output shape this build produces (<c>package</c>, <c>site</c>,
    /// <c>bundle</c>). It is part of the cache's *path*, not just its key: a project built both ways
    /// would otherwise have the two builds take turns invalidating and overwriting one cache, and
    /// neither would ever hit.</param>
    public static BuildCache Open(ResolvedProject project, string configuration, string mode,
        IEnumerable<string> settings, IEnumerable<string> contentExtras, string? cacheDirOverride)
    {
        var settingsKey = HashStrings(settings.Prepend($"format={FormatVersion}").Prepend($"compiler={CompilerId()}"));
        var contentKey = HashStrings(contentExtras.Prepend(settingsKey));

        // A cache is per project, per configuration and per output mode: Debug and Release are
        // structurally different builds (metadata-only vs full IL, formatted vs minified bundles).
        var explicitRoot = cacheDirOverride ?? Environment.GetEnvironmentVariable("TRANSPOSE_CACHE_DIR");
        var dir = explicitRoot is null
            ? Path.Combine(project.ProjectDir, "obj", "tps-cache", configuration, mode)
            // An explicit shared cache root (e.g. a temp folder) has to keep projects apart itself.
            : Path.Combine(explicitRoot, ProjectSlug(project.CsprojPath), configuration, mode);

        var textHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, text) in project.Sources) textHashes[path] = HashString(text);

        return new BuildCache(dir, settingsKey, contentKey, textHashes, project);
    }

    /// <summary>
    /// How much of the cache this build may reuse, and the files it must recompile. Returns the changed
    /// source paths for a <see cref="Verdict.BodyOnlyChange"/>; empty otherwise.
    /// </summary>
    public (Verdict verdict, List<string> changedSources, string? reason) Decide()
    {
        _previous = LoadManifest();
        if (_previous is null) return (Verdict.FullBuild, new List<string>(), "no cache");
        if (_previous.FormatVersion != FormatVersion) return (Verdict.FullBuild, new List<string>(), "cache format changed");
        if (_previous.SettingsKey != _settingsKey) return (Verdict.FullBuild, new List<string>(), "build settings or references changed");

        // The *set* of files has to match: a new or deleted file changes which types exist and in what
        // order the bundle emits them, which is a declaration-level change by definition.
        if (_previous.Sources.Count != _textHashes.Count)
            return (Verdict.FullBuild, new List<string>(), "source file added or removed");

        var changed = new List<string>();
        foreach (var entry in _previous.Sources)
        {
            if (!_textHashes.TryGetValue(entry.Path, out var now))
                return (Verdict.FullBuild, new List<string>(), $"source file removed ({Path.GetFileName(entry.Path)})");
            if (now != entry.Text) changed.Add(entry.Path);
        }

        // A referenced package's content changed without its declarations changing — e.g. a dependency
        // project was rebuilt after a body-only edit of its own. Nothing this project emits can differ,
        // but its outputs embed or copy that content, so they have to be written again.
        var contentStale = _previous.ContentKey != _contentKey;

        if (changed.Count == 0 && !contentStale)
            return OutputsIntact()
                ? (Verdict.UpToDate, new List<string>(), null)
                : (Verdict.FullBuild, new List<string>(), "an output file was modified or deleted");

        // The one question that separates a body-only edit from everything else. Only the changed files
        // need re-parsing to answer it, which is why this is cheap even on a large project.
        var previousDecl = _previous.Sources.ToDictionary(s => s.Path, s => s.Decl, StringComparer.OrdinalIgnoreCase);
        var texts = _project.Sources.ToDictionary(s => s.path, s => s.text, StringComparer.OrdinalIgnoreCase);
        foreach (var path in changed)
        {
            var tree = CompilationBuilder.ParseOne(path, texts[path], _project.LanguageVersion, _project.DefineConstants);
            if (IncrementalPlan.DeclarationHash(tree) != previousDecl[path])
                return (Verdict.FullBuild, new List<string>(), $"declarations changed in {Path.GetFileName(path)}");
        }

        if (!_previous.HasTypes) return (Verdict.FullBuild, new List<string>(), "cached JavaScript missing");

        return (Verdict.BodyOnlyChange, changed,
            contentStale && changed.Count == 0 ? "a referenced package was rebuilt, its declarations unchanged" : null);
    }

    /// <summary>
    /// The plan to hand the translator. For a full build it carries nothing reusable but still collects
    /// what this build produces, so the cache can be written afterwards.
    /// </summary>
    public IncrementalPlan CreatePlan(Verdict verdict, List<string> changedSources, bool canReuseAssembly)
    {
        if (verdict != Verdict.BodyOnlyChange)
            return new IncrementalPlan
            {
                // Nothing is reusable: every file counts as changed, so every type is re-emitted and
                // every file is scanned and diagnosed, exactly as a non-incremental build would.
                ChangedSources = _textHashes.Keys.ToList(),
                TypeJs = new Dictionary<string, string>(),
            };

        var types = ReadTypes();
        return new IncrementalPlan
        {
            ChangedSources = changedSources,
            TypeJs = types,
            PreviousDeclarationHashes = _previous!.Sources.ToDictionary(s => s.Path, s => s.Decl, StringComparer.Ordinal),
            MetadataScript = _previous!.HasMetadataScript ? File.ReadAllText(Path.Combine(_dir, MetaFile)) : null,
            InlineMetadata = _previous.HasInlineMetadata ? File.ReadAllText(Path.Combine(_dir, InlineMetaFile)) : null,
            DeniedSimpleNames = _previous.HasDeniedNames && File.Exists(Path.Combine(_dir, DeniedNamesFile))
                ? File.ReadAllLines(Path.Combine(_dir, DeniedNamesFile))
                : null,
            AssemblyBytes = canReuseAssembly && _previous.HasAssembly && File.Exists(Path.Combine(_dir, AssemblyFile))
                ? File.ReadAllBytes(Path.Combine(_dir, AssemblyFile))
                : null,
        };
    }

    /// <summary>
    /// The previous build's finished output, when this build's own inputs are all unchanged and only a
    /// referenced package was rebuilt. Nothing this project emits can differ — so rather than reuse the
    /// cache *within* a compilation, skip the compilation entirely and go straight to writing the
    /// outputs (which do have to change: they carry the reference's JavaScript).
    ///
    /// Returns null when the cache cannot supply a complete result, in which case the caller compiles
    /// normally. Requires the cached assembly, so it only applies to a metadata-only configuration.
    /// </summary>
    public AssemblyBuildResult? TryReplayCompilation(bool canReuseAssembly)
    {
        if (_previous is null || !canReuseAssembly || !_previous.HasAssembly) return null;
        var bundle = Path.Combine(_dir, BundleFile);
        var assembly = Path.Combine(_dir, AssemblyFile);
        if (!File.Exists(bundle) || !File.Exists(assembly)) return null;
        try
        {
            return new AssemblyBuildResult(
                File.ReadAllText(bundle),
                _previous.HasMetadataScript ? File.ReadAllText(Path.Combine(_dir, MetaFile)) : null,
                File.ReadAllBytes(assembly),
                Array.Empty<Microsoft.CodeAnalysis.Diagnostic>());
        }
        catch { return null; }
    }

    /// <summary>
    /// Records a new set of output files against the existing cache entry, for a build that reused the
    /// previous compilation wholesale (see <see cref="TryReplayCompilation"/>). Everything else in the
    /// manifest — the source hashes, the per-type JavaScript — is still current and is left alone.
    /// </summary>
    public void SaveOutputsOnly(IEnumerable<string> outputs)
    {
        if (_previous is null) return;
        try
        {
            _previous.ContentKey = _contentKey;
            _previous.Outputs = outputs.Where(File.Exists).Select(Describe).ToList();
            File.WriteAllText(Path.Combine(_dir, ManifestFile), JsonSerializer.Serialize(_previous, ManifestJson));
        }
        catch (Exception ex)
        {
            MsBuildDiagnostic.WriteWarning(MsBuildDiagnostic.CodeCacheNotWritten,
                $"could not update the incremental build cache in '{_dir}': {ex.Message}");
        }
    }

    /// <summary>
    /// Writes the cache for the next build: the per-type JavaScript in bundle order, the reflection
    /// metadata, the emitted assembly, the per-file hashes, and the outputs this build produced (so a
    /// later run can tell whether they are still the ones it wrote).
    ///
    /// A failure here is never fatal — the cache is an optimisation, and a build that produced correct
    /// output must not fail because a temp directory was not writable.
    /// </summary>
    public void Save(IncrementalPlan plan, AssemblyBuildResult result, IEnumerable<string> outputs)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_dir);

            WriteTypes(plan);

            // The finished bundle, so a later build whose own inputs are all unchanged can skip the
            // compilation outright rather than reassembling the bundle from the per-type pieces.
            if (result.Javascript is { } bundle) File.WriteAllText(Path.Combine(_dir, BundleFile), bundle);
            else File.Delete(Path.Combine(_dir, BundleFile));

            if (result.MetadataJavascript is { } meta) File.WriteAllText(Path.Combine(_dir, MetaFile), meta);
            else File.Delete(Path.Combine(_dir, MetaFile));

            if (plan.FinalInlineMetadata is { } inline) File.WriteAllText(Path.Combine(_dir, InlineMetaFile), inline);
            else File.Delete(Path.Combine(_dir, InlineMetaFile));

            if (result.AssemblyBytes is { } asm) File.WriteAllBytes(Path.Combine(_dir, AssemblyFile), asm);
            else File.Delete(Path.Combine(_dir, AssemblyFile));

            if (plan.FinalDeniedSimpleNames is { } denied)
                File.WriteAllLines(Path.Combine(_dir, DeniedNamesFile), denied);
            else File.Delete(Path.Combine(_dir, DeniedNamesFile));

            var declHashes = result.DeclarationHashes ?? new Dictionary<string, string>();
            var manifest = new Manifest
            {
                FormatVersion = FormatVersion,
                SettingsKey = _settingsKey,
                ContentKey = _contentKey,
                HasTypes = plan.FinalOrder.Count > 0,
                HasMetadataScript = result.MetadataJavascript is not null,
                HasInlineMetadata = plan.FinalInlineMetadata is not null,
                HasAssembly = result.AssemblyBytes is not null,
                HasDeniedNames = plan.FinalDeniedSimpleNames is not null,
                Sources = _project.Sources
                    .Select(s => new SourceEntry
                    {
                        Path = s.path,
                        Text = _textHashes[s.path],
                        Decl = declHashes.TryGetValue(s.path, out var d) ? d : "",
                    })
                    .ToList(),
                Outputs = outputs.Where(File.Exists).Select(Describe).ToList(),
            };
            File.WriteAllText(Path.Combine(_dir, ManifestFile),
                JsonSerializer.Serialize(manifest, ManifestJson));
        }
        catch (Exception ex)
        {
            MsBuildDiagnostic.WriteWarning(MsBuildDiagnostic.CodeCacheNotWritten,
                $"could not write the incremental build cache in '{_dir}': {ex.Message}");
        }
    }

    /// <summary>Deletes the cache — used when a build fails, so a broken state is never reused.</summary>
    public void Invalidate()
    {
        try { if (System.IO.Directory.Exists(_dir)) System.IO.Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    /// <summary>The output files the previous build recorded, so a caller can report them.</summary>
    public IReadOnlyList<string> PreviousOutputs => _previous?.Outputs.Select(o => o.Path).ToList() ?? new List<string>();

    // ---- outputs ---------------------------------------------------------------------------------

    /// <summary>
    /// Whether every file the previous build wrote is still there, the same length, with the same
    /// write time. This is what makes "nothing changed" mean "nothing needs doing": without it, a
    /// deleted site folder or a hand-edited bundle would be silently left broken.
    /// </summary>
    private bool OutputsIntact()
    {
        if (_previous!.Outputs.Count == 0) return false;
        foreach (var recorded in _previous.Outputs)
        {
            if (!File.Exists(recorded.Path)) return false;
            var now = Describe(recorded.Path);
            if (now.Length != recorded.Length || now.Ticks != recorded.Ticks) return false;
        }
        return true;
    }

    private static OutputEntry Describe(string path)
    {
        var info = new FileInfo(path);
        return new OutputEntry { Path = path, Length = info.Length, Ticks = info.LastWriteTimeUtc.Ticks };
    }

    // ---- per-type JavaScript store ---------------------------------------------------------------

    // One file rather than 500: a project has hundreds of types, and a single framed blob is one read
    // and one write instead of hundreds of directory operations. Frame = "<key>\t<utf8 length>\n<js>".

    private void WriteTypes(IncrementalPlan plan)
    {
        var path = Path.Combine(_dir, TypesFile);
        if (plan.FinalOrder.Count == 0) { File.Delete(path); return; }

        using var stream = File.Create(path);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        foreach (var key in plan.FinalOrder)
        {
            var js = plan.FinalTypeJs[key];
            writer.Write(key);
            writer.Write('\t');
            writer.Write(Encoding.UTF8.GetByteCount(js));
            writer.Write('\n');
            writer.Write(js);
        }
    }

    private Dictionary<string, string> ReadTypes()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = Path.Combine(_dir, TypesFile);
        if (!File.Exists(path)) return result;

        var bytes = File.ReadAllBytes(path);
        var at = 0;
        while (at < bytes.Length)
        {
            var tab = Array.IndexOf(bytes, (byte)'\t', at);
            if (tab < 0) break;
            var nl = Array.IndexOf(bytes, (byte)'\n', tab);
            if (nl < 0) break;
            var key = Encoding.UTF8.GetString(bytes, at, tab - at);
            if (!int.TryParse(Encoding.UTF8.GetString(bytes, tab + 1, nl - tab - 1), out var length)) break;
            if (nl + 1 + length > bytes.Length) break;
            result[key] = Encoding.UTF8.GetString(bytes, nl + 1, length);
            at = nl + 1 + length;
        }
        return result;
    }

    // ---- manifest --------------------------------------------------------------------------------

    private static readonly JsonSerializerOptions ManifestJson = new() { WriteIndented = false };

    private Manifest? LoadManifest()
    {
        var path = Path.Combine(_dir, ManifestFile);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), ManifestJson); }
        catch { return null; } // an unreadable cache is simply no cache
    }

    private sealed class Manifest
    {
        public int FormatVersion { get; set; }
        public string SettingsKey { get; set; } = "";
        public string ContentKey { get; set; } = "";
        public bool HasTypes { get; set; }
        public bool HasMetadataScript { get; set; }
        public bool HasInlineMetadata { get; set; }
        public bool HasAssembly { get; set; }
        public bool HasDeniedNames { get; set; }
        public List<SourceEntry> Sources { get; set; } = new();
        public List<OutputEntry> Outputs { get; set; } = new();
    }

    private sealed class SourceEntry
    {
        public string Path { get; set; } = "";

        /// <summary>Hash of the whole file — decides whether it has to be looked at at all.</summary>
        public string Text { get; set; } = "";

        /// <summary>Hash of the file minus method/accessor bodies — decides whether the change is
        /// confined to bodies. See <see cref="IncrementalPlan.DeclarationHash"/>.</summary>
        public string Decl { get; set; } = "";
    }

    private sealed class OutputEntry
    {
        public string Path { get; set; } = "";
        public long Length { get; set; }
        public long Ticks { get; set; }
    }

    // ---- fingerprints ----------------------------------------------------------------------------

    /// <summary>
    /// A referenced assembly's fingerprint. Package DLLs in the NuGet cache are immutable and a
    /// sibling project's DLL is rewritten wholesale by its own build, so path + length + write time
    /// identifies the content without reading megabytes of metadata on every build.
    /// </summary>
    public static string ReferenceFingerprint(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch { return $"{path}|missing"; }
    }

    /// <summary>
    /// The *declaration-level* fingerprint of a referenced assembly: what a consumer's emitted
    /// JavaScript actually depends on. Everything a consumer reads from a reference — member names,
    /// overload numbering over the full member set, <c>[Template]</c>/<c>[Name]</c> attributes,
    /// constants — comes from its metadata; the JavaScript embedded alongside it is copied through
    /// untouched, never consulted.
    ///
    /// So a Transpose project writes <see cref="MetadataSidecarSuffix"/> next to its DLL recording the
    /// hash of the assembly Roslyn emitted, before the resources were embedded, and a consumer prefers
    /// that over the DLL's bytes. That is what lets a body-only edit in a referenced library leave its
    /// consumers' compilation cached: the library's metadata is identical (in a metadata-only
    /// configuration it *is* the same bytes), even though its DLL was rewritten — Mono.Cecil stamps a
    /// fresh MVID and timestamp on every embed, so the DLL never compares equal twice.
    /// </summary>
    public static string ReferenceMetadataFingerprint(string path)
    {
        var sidecar = path + MetadataSidecarSuffix;
        try
        {
            if (File.Exists(sidecar)) return $"{path}|meta:{File.ReadAllText(sidecar).Trim()}";
        }
        catch { /* fall through to the byte fingerprint */ }
        return ReferenceFingerprint(path);
    }

    /// <summary>Written next to a project's package DLL; see <see cref="ReferenceMetadataFingerprint"/>.</summary>
    public const string MetadataSidecarSuffix = ".tpsmeta";

    /// <summary>Records the hash of a project's emitted assembly metadata next to its DLL, for
    /// consumers to fingerprint. Best effort: without it a consumer simply rebuilds more often.</summary>
    public static void WriteMetadataSidecar(string dllPath, byte[]? assemblyBytes)
    {
        if (assemblyBytes is null) return;
        try { File.WriteAllText(dllPath + MetadataSidecarSuffix, Convert.ToHexString(SHA256.HashData(assemblyBytes))); }
        catch { /* best effort */ }
    }

    /// <summary>Fingerprint of a file's contents, or a marker when it does not exist.</summary>
    public static string FileContentFingerprint(string path)
    {
        try { return File.Exists(path) ? HashString(File.ReadAllText(path)) : "absent"; }
        catch { return "unreadable"; }
    }

    /// <summary>The identity of this compiler build. A different compiler emits different JavaScript,
    /// so its cache must not be reused: the informational version covers a released tool, and the
    /// assembly's own write time covers a developer rebuilding the compiler in place.</summary>
    private static string CompilerId()
    {
        var asm = typeof(BuildCache).Assembly;
        var version = asm.GetName().Version?.ToString() ?? "0";
        var informational = asm
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "";
        var stamp = "";
        try
        {
            var location = asm.Location;
            if (!string.IsNullOrEmpty(location) && File.Exists(location))
                stamp = File.GetLastWriteTimeUtc(location).Ticks.ToString();
        }
        catch { /* single-file or trimmed host: the versions alone have to do */ }
        // The translator is a separate assembly and is where nearly all emit changes land.
        var translator = "";
        try
        {
            var tloc = typeof(RoslynTranslator).Assembly.Location;
            if (!string.IsNullOrEmpty(tloc) && File.Exists(tloc))
                translator = File.GetLastWriteTimeUtc(tloc).Ticks.ToString();
        }
        catch { /* ditto */ }
        return $"{version}|{informational}|{stamp}|{translator}";
    }

    private static string ProjectSlug(string csprojPath)
        => Path.GetFileNameWithoutExtension(csprojPath) + "-" + HashString(Path.GetFullPath(csprojPath)).Substring(0, 12);

    private static string HashString(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string HashStrings(IEnumerable<string> parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var part in parts)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(part));
            hash.AppendData(new byte[] { 0 });
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
