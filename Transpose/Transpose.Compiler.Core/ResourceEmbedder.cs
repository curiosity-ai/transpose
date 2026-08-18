using System.Text;
using System.Text.Json;
using Mono.Cecil;

namespace Transpose.Compiler;

/// <summary>One embedded Transpose resource: the file bytes plus its manifest entry. <see cref="Output"/>
/// is the output subdirectory a consumer extracts it to (null for a top-level script).
///
/// <see cref="Variant"/> says which of the package's interchangeable JavaScript variants this is (see
/// <see cref="JsVariant"/>) so a consuming site build keeps the set matching its own configuration
/// rather than pairing file names; null marks an authored resource, which belongs to no set and is
/// copied through in every configuration. <see cref="SiteName"/> is the name the consumer writes it
/// under when that differs from <see cref="Name"/> — only the module entry needs it, because the
/// three variants must have distinct names inside the DLL and the same name in the site.</summary>
internal sealed record EmbeddedItem(string Name, byte[] Content, string? Output, bool Load = true, bool Module = false,
    JsVariant? Variant = null, string? SiteName = null);

/// <summary>
/// Embeds the compiled JavaScript (and tps.json resource files) into a .NET assembly as private
/// manifest resources, alongside an <c>Transpose.Resources.json</c> manifest listing them — exactly the
/// shape the existing compiler produces and that <see cref="OutputBuilder"/> extracts when the
/// assembly is referenced. This is the "produce a distributable package" half of the protocol.
///
/// Every assembly written here also carries a <see cref="BuildStamp"/> (<c>Transpose.Build.json</c>):
/// the compiler that built it, and therefore the oldest compiler allowed to consume it. It is embedded
/// but *not* listed in the resource manifest — it is compiler metadata rather than a web resource, so
/// no consumer extracts it into a site.
/// </summary>
internal static class ResourceEmbedder
{
    private const string ManifestName = "Transpose.Resources.json";

    /// <summary>
    /// The chunk map a module-mode package publishes: emitted type name → the site-relative chunk
    /// file that defines it. A consuming build reads this so its own chunks can import the chunk
    /// behind a library type they use — without it the reference would land on the library's stub,
    /// and a stub cannot be resolved synchronously.
    ///
    /// Embedded but deliberately NOT listed in <see cref="ManifestName"/>: it is build metadata for
    /// the next compiler, not a web resource, so no consumer extracts it into a site.
    /// </summary>
    public const string ModuleMapName = "Transpose.Modules.json";

    /// <summary>Per-member dependency sets of a <c>[SkipTypeClustering]</c> facade (documentation
    /// comment id → emitted define names). Embedded but, like the chunk map and the build stamp,
    /// deliberately absent from Transpose.Resources.json — it is compiler metadata, not a web
    /// resource, so no site build ever extracts it.</summary>
    public const string SkipClusterMapName = "Transpose.SkipCluster.json";
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>True if the assembly already carries the embedded Transpose resource manifest — i.e. it
    /// was produced as an Transpose package (with its JS embedded), not a plain csc build. Used to decide
    /// whether a referenced project's DLL needs to be (re)built by the translator.</summary>
    public static bool HasManifest(string assemblyPath)
    {
        try
        {
            using var asm = AssemblyDefinition.ReadAssembly(assemblyPath);
            return asm.MainModule.Resources.Any(r => r.Name == ManifestName);
        }
        catch { return false; }
    }

    /// <summary>
    /// Writes <paramref name="assemblyBytes"/> to <paramref name="assemblyPath"/> with
    /// <paramref name="items"/> embedded as private manifest resources.
    ///
    /// The freshly-emitted assembly is handed over in memory rather than written first and re-read:
    /// with the JS, CSS and fonts embedded, a package DLL runs to tens of megabytes, and writing it
    /// once and then reading it straight back only to rewrite it was pure I/O.
    /// </summary>
    public static void Embed(string assemblyPath, byte[] assemblyBytes, IReadOnlyList<EmbeddedItem> items, IEnumerable<string>? referencePaths = null, IReadOnlyDictionary<string, string>? moduleMap = null,
        IReadOnlyDictionary<string, List<string>>? skipClusterMap = null)
    {
        using var output = File.Create(assemblyPath);
        Embed(output, assemblyBytes, items, referencePaths, contextDirectory: Path.GetDirectoryName(Path.GetFullPath(assemblyPath)), moduleMap: moduleMap, skipClusterMap: skipClusterMap);
    }

    /// <summary>
    /// Same as <see cref="Embed(string, byte[], IReadOnlyList{EmbeddedItem}, IEnumerable{string}?)"/>,
    /// but writes the resulting assembly to <paramref name="output"/> instead of a file on disk — used
    /// by <c>Transpose.Compiler.Library</c>, which compiles fully in memory and hands the caller the
    /// package assembly's bytes rather than writing them anywhere. <paramref name="contextDirectory"/>
    /// stands in for the assembly-file directory the file-based overload adds to the resolver's search
    /// path (a copy-local referenced assembly can sit there); pass null when there is no such directory.
    /// </summary>
    public static void Embed(Stream output, byte[] assemblyBytes, IReadOnlyList<EmbeddedItem> items,
        IEnumerable<string>? referencePaths = null, string? contextDirectory = null,
        IReadOnlyDictionary<string, string>? moduleMap = null,
        IReadOnlyDictionary<string, List<string>>? skipClusterMap = null)
    {
        // Cecil resolves referenced assemblies when it re-serializes metadata on Write() — e.g. to
        // determine the underlying type of a parameter's default value whose type is a referenced
        // enum. It must resolve to the very files the compilation bound to, hence the resolver below.
        using var resolver = new ReferencePathResolver(contextDirectory, referencePaths);

        using var source = new MemoryStream(assemblyBytes, writable: false);
        using var asm = AssemblyDefinition.ReadAssembly(source, new ReaderParameters { AssemblyResolver = resolver });
        var resources = asm.MainModule.Resources;

        void Replace(string name, byte[] bytes)
        {
            for (var i = resources.Count - 1; i >= 0; i--)
                if (resources[i].Name == name) resources.RemoveAt(i);
            resources.Add(new EmbeddedResource(name, ManifestResourceAttributes.Private, bytes));
        }

        foreach (var item in items)
            Replace(item.Name, item.Content);

        // The manifest lists { FileName, Name, Path } per resource (Parts omitted — we don't
        // split combined bundles). Serialized with the same field names the compiler reads.
        var manifest = items.Select(i => new
        {
            FileName = i.Name,
            // Module: the consumer scripts this file as <script type="module"> instead of a classic
            // deferred script. Only a module-mode package's entry file sets it; its chunk files are
            // Load=false and are reached through that entry's imports.
            Module = i.Module ? (bool?)true : null,
            Name = i.Name,
            Path = i.Output,
            Parts = (object?)null,
            Load = i.Load,   // false → copied to the site but not injected into index.html (.dontload)
            // Which interchangeable variant this is, and (module entry only) the name the consumer
            // writes it under. Both absent for an authored resource and for a package built before
            // variants existed, which is what makes the consumer fall back to file-name pairing.
            Variant = i.Variant?.ToJson(),
            SiteName = i.SiteName,
        }).ToArray();
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        Replace(ManifestName, Utf8NoBom.GetBytes(json));

        // The minimum-compiler-version stamp: embedded, but never part of the manifest above (see the
        // type-level remarks). Replace rather than add, so re-embedding an assembly that already
        // carries one restamps it instead of leaving two.
        Replace(BuildStamp.ResourceName, BuildStamp.ForCurrentCompiler().ToJsonBytes());

        if (moduleMap is { Count: > 0 })
            Replace(ModuleMapName, Utf8NoBom.GetBytes(JsonSerializer.Serialize(moduleMap)));

        if (skipClusterMap is { Count: > 0 })
            Replace(SkipClusterMapName, Utf8NoBom.GetBytes(JsonSerializer.Serialize(skipClusterMap)));

        asm.Write(output);
    }

    /// <summary>
    /// Resolves an assembly reference to the exact file the compilation bound to, keyed by the
    /// assembly's simple name.
    ///
    /// Cecil's own <see cref="DefaultAssemblyResolver"/> searches *directories* instead: it takes the
    /// first <c>&lt;name&gt;.dll</c> it finds, in the order the directories were added, and never looks at
    /// the assembly's version. The first candidate is the folder the DLL is being written to — where a
    /// copy-local copy of a referenced assembly from an earlier build routinely sits — plus Cecil's own
    /// implicit <c>.</c> and <c>bin</c> entries (relative to the process working directory). Binding to
    /// one of those instead of the reference Roslyn compiled against makes a type that exists in the
    /// real reference look missing, and re-serializing a constant of that type then fails:
    /// <c>Mono.Cecil.ResolutionException: Failed to resolve Tesserae.PixelAvatarDesign</c> — from a
    /// project whose C# compiled cleanly against a package that does define the enum.
    ///
    /// A name outside the reference set still falls back to the old directory search, so nothing that
    /// resolves today stops resolving.
    /// </summary>
    private sealed class ReferencePathResolver : IAssemblyResolver
    {
        private readonly Dictionary<string, string>              _byName;
        private readonly Dictionary<string, AssemblyDefinition>  _opened  = new(StringComparer.OrdinalIgnoreCase);
        private readonly DefaultAssemblyResolver                 _byDirectory = new();

        public ReferencePathResolver(string? contextDirectory, IEnumerable<string>? referencePaths)
        {
            _byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (contextDirectory is not null) _byDirectory.AddSearchDirectory(contextDirectory);

            foreach (var reference in referencePaths ?? Enumerable.Empty<string>())
            {
                var full = Path.GetFullPath(reference);
                if (!File.Exists(full)) continue;
                // First wins, matching how the resolved reference set itself is built.
                if (!_byName.ContainsKey(Path.GetFileNameWithoutExtension(full)))
                    _byName[Path.GetFileNameWithoutExtension(full)] = full;
                _byDirectory.AddSearchDirectory(Path.GetDirectoryName(full)!);
            }
        }

        public AssemblyDefinition Resolve(AssemblyNameReference name)
            => Resolve(name, new ReaderParameters());

        public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            if (_opened.TryGetValue(name.Name, out var already)) return already;
            if (!_byName.TryGetValue(name.Name, out var path)) return _byDirectory.Resolve(name, parameters);

            parameters.AssemblyResolver ??= this;
            var assembly = AssemblyDefinition.ReadAssembly(path, parameters);
            _opened[name.Name] = assembly;
            return assembly;
        }

        public void Dispose()
        {
            foreach (var assembly in _opened.Values) assembly.Dispose();
            _opened.Clear();
            _byDirectory.Dispose();
        }
    }
}
