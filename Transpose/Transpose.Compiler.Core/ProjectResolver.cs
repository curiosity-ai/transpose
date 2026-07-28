using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace Transpose.Compiler;

/// <summary>
/// Minimal resolver for an Transpose project: gathers the C# sources (the SDK's default glob, explicit
/// <c>&lt;Compile&gt;</c> items, and anything the project <c>&lt;Import&gt;</c>s — see
/// <see cref="ProjectXml"/>), and resolves the package references — transitively — to their assemblies
/// in the NuGet global-packages cache. Transpose projects reference Transpose.dll as their whole BCL plus a
/// few Transpose.* packages (Transpose.Core, Transpose.Newtonsoft.Json), all of which live in the cache
/// once restored.
/// </summary>
internal sealed class ResolvedProject
{
    public required string CsprojPath { get; init; }
    public required string ProjectDir { get; init; }
    public required string AssemblyName { get; init; }

    /// <summary>The project's target framework (e.g. <c>netstandard2.0</c>), used to compute the
    /// build-output directory (<c>bin/&lt;config&gt;/&lt;tfm&gt;</c>) so tps writes the emitted assembly
    /// where the SDK/<c>dotnet pack</c> expects it. Defaults to <c>netstandard2.0</c> — the framework
    /// the Transpose packages ship (and the one the Transpose.Build.Target SDK forces).</summary>
    public required string TargetFramework { get; init; }
    public required List<(string path, string text)> Sources { get; init; }
    public required List<string> ReferencePaths { get; init; }
    public required List<string> DefineConstants { get; init; }
    public required LanguageVersion LanguageVersion { get; init; }

    /// <summary>The csproj <c>&lt;MinifyLocalVariables&gt;</c> property — when true, the minified
    /// bundle crunches local variable names (smaller output). Defaults to false, mirroring the
    /// legacy compiler's safe minifier profile that keeps local names.</summary>
    public bool MinifyLocalVariables { get; init; }

    /// <summary>
    /// The csproj <c>&lt;TransposeMetadataOnlyAssembly&gt;</c> property: emit the project's .NET assembly
    /// as metadata only — full type/member metadata including private members, but <c>throw null</c>
    /// method bodies — instead of compiling real IL. Null when the project says nothing, in which case
    /// the configuration decides (see <see cref="MetadataOnlyAssemblyDefault"/>).
    ///
    /// Skipping the IL codegen removes the second full bind of every method body from the build:
    /// measured ~18% off a clean Tesserae build (8.3 s → 6.8 s), roughly halves the DLL, and produces
    /// byte-identical JavaScript. Implies no debug information (Roslyn rejects an embedded PDB when
    /// there are no bodies to describe).
    ///
    /// It is sound because a Transpose-compiled assembly can never execute: it binds against
    /// <c>Transpose.dll</c>, a stand-in BCL with no implementations, so no .NET host can load it. Its
    /// only jobs are to be *bound against* by another Transpose project and to carry the compiled JS
    /// as embedded resources — both of which need metadata alone.
    /// </summary>
    public bool? MetadataOnlyAssembly { get; init; }

    /// <summary>
    /// Whether to emit a metadata-only assembly for <paramref name="configuration"/> when the project
    /// expresses no preference: yes for Debug, no for Release.
    ///
    /// Debug is the inner-loop configuration — its output is consumed by the developer's own build and
    /// by `dotnet serve`, never shipped — so it takes the faster path. Release is what
    /// <c>dotnet pack</c> turns into a NuGet package, so it keeps real IL, and the Transpose SDK
    /// additionally refuses to package a Debug build at all (see Sdk.targets) so a metadata-only
    /// assembly cannot reach a feed by accident.
    /// </summary>
    public static bool MetadataOnlyAssemblyDefault(string configuration)
        => string.Equals(configuration, "Debug", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the emitted .NET assembly carries debug information, from the csproj's
    /// <c>&lt;DebugType&gt;</c> (<c>None</c> — or <c>DebugSymbols=false</c> — turns it off). Producing an
    /// embedded PDB is real work and adds ~12% to the assembly's size, and a Transpose project's DLL
    /// exists to be *bound against* by other Transpose projects — nobody steps through its IL — so a
    /// project that says it wants no symbols should not pay for them. Defaults to true, so a project
    /// that says nothing keeps the debug information it has always got.</summary>
    public bool EmitDebugInformation { get; init; } = true;

    /// <summary>Directories of every project in the closure — the root first, then the
    /// referenced projects it pulls in (each may contribute tps.json resources).</summary>
    public required List<string> ProjectDirs { get; init; }

    /// <summary>In separate-assembly mode, the built output DLLs of referenced projects — the
    /// consumer extracts their embedded JS/resources instead of recompiling their source.</summary>
    public List<string> ReferencedProjectDlls { get; init; } = new();
}

internal static class ProjectResolver
{
    public static ResolvedProject Resolve(string csprojPath, string configuration = "Debug", bool separateAssemblies = false)
    {
        csprojPath = Path.GetFullPath(csprojPath);
        var projectDir = Path.GetDirectoryName(csprojPath)!;
        // Loads the csproj *and everything it <Import>s* — a shared project's .projitems is where its
        // <Compile> items live, so ignoring imports silently dropped that source. See ProjectXml.
        var doc = ProjectXml.Load(csprojPath);

        var assemblyName = doc.Property("AssemblyName") ?? Path.GetFileNameWithoutExtension(csprojPath);

        var targetFramework = EffectiveTargetFramework(doc);

        var defines = (doc.Property("DefineConstants") ?? "")
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
        if (!defines.Contains("TRANSPOSE")) defines.Add("TRANSPOSE"); // Transpose projects always compile the TRANSPOSE branch (#if TRANSPOSE)

        // Configuration-driven symbols the .NET SDK defines implicitly: TRACE in every configuration,
        // DEBUG in the Debug configuration. Without these, `#if DEBUG` never compiles in a `-c Debug`
        // build (it silently took the #else branch — e.g. loading the minified bundle instead of the
        // dev one). Additive with the project's own <DefineConstants>; add only if not already present.
        if (!defines.Contains("TRACE")) defines.Add("TRACE");
        if (string.Equals(configuration, "Debug", StringComparison.OrdinalIgnoreCase) && !defines.Contains("DEBUG"))
            defines.Add("DEBUG");

        // Framework-derived symbols the .NET SDK defines implicitly from the target framework moniker
        // (e.g. netstandard2.0 → NETSTANDARD, NETSTANDARD2_0 and the NETSTANDARD*_OR_GREATER chain).
        // Without these, `#if NETSTANDARD2_0` never compiles under `tps`. Additive with the project's
        // own <DefineConstants>.
        foreach (var fx in FrameworkDefines(targetFramework))
            if (!defines.Contains(fx)) defines.Add(fx);

        var lang = ParseLangVersion(doc.Property("LangVersion"));

        // Default (bundle) mode: translate the whole closure of source projects into one JS
        // output. Separate-assembly mode: compile only this project's own sources and reference
        // each project dependency as its built DLL (its JS is embedded and extracted, not recompiled).
        var sources = new List<(string, string)>();
        var references = new Dictionary<string, (string path, string version)>(StringComparer.OrdinalIgnoreCase);
        var visitedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectDirs = new List<string>();
        var projectDlls = new List<string>();
        var roots = NuGetRoots().Where(Directory.Exists).ToList();
        CollectProject(csprojPath, sources, references, visitedProjects, projectDirs, roots,
            separateAssemblies, projectDlls, configuration, isRoot: true);

        return new ResolvedProject
        {
            CsprojPath = csprojPath,
            ProjectDir = projectDir,
            AssemblyName = assemblyName,
            TargetFramework = targetFramework,
            Sources = sources,
            ReferencePaths = references.Values.Select(v => v.path).ToList(),
            DefineConstants = defines,
            LanguageVersion = lang,
            MinifyLocalVariables = string.Equals(doc.Property("MinifyLocalVariables")?.Trim(), "true", StringComparison.OrdinalIgnoreCase),
            MetadataOnlyAssembly = ParseBool(doc.Property("TransposeMetadataOnlyAssembly")),
            EmitDebugInformation = !string.Equals(doc.Property("DebugType")?.Trim(), "none", StringComparison.OrdinalIgnoreCase)
                                   && !string.Equals(doc.Property("DebugSymbols")?.Trim(), "false", StringComparison.OrdinalIgnoreCase),
            ProjectDirs = projectDirs,
            ReferencedProjectDlls = projectDlls,
        };
    }

    /// <summary>
    /// Turns a project's <c>&lt;AssemblyAttribute Include="Transpose.ExternalAttribute" /&gt;</c> items into a
    /// synthesized <c>[assembly: ...]</c> source file. MSBuild's GenerateAssemblyInfo does this when
    /// building with the SDK; the tps CLI reads the csproj directly, so it must synthesize them itself.
    /// This is what makes binding libraries such as Transpose.Core (which mark the whole assembly
    /// <c>[assembly: External]</c>) compile: every type is external and its extern members are JS bindings.
    /// </summary>
    private static string? SynthesizeAssemblyAttributes(ProjectXml doc)
    {
        var lines = new List<string>();
        foreach (var (e, _) in doc.Elements("AssemblyAttribute"))
        {
            var name = e.Attribute("Include")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            // InternalsVisibleTo is a .NET assembly-visibility concept with no meaning in transpiled
            // JavaScript (there is no "internal" access enforcement at runtime). MSBuild's
            // WriteCodeFragment would emit it, but the transpiler does not need it — and a project
            // that lists several friend assemblies as one item with multiple <_ParameterN> children
            // would otherwise synthesize a call that does not match the single-string ctor. Skip it.
            var simple = name!.Split('.')[^1];
            if (simple is "InternalsVisibleTo" or "InternalsVisibleToAttribute") continue;

            // Positional constructor arguments come from ordered <_Parameter1>, <_Parameter2>, …
            // children (MSBuild's convention). Emit them as C# string literals — the shape used by
            // every assembly attribute that carries data (they take string arguments).
            var args = e.Elements()
                .Where(c => c.Name.LocalName.StartsWith("_Parameter", StringComparison.Ordinal))
                .OrderBy(c => c.Name.LocalName)
                .Select(c => "\"" + c.Value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")
                .ToList();
            var argList = args.Count > 0 ? "(" + string.Join(", ", args) + ")" : "";
            lines.Add($"[assembly: global::{name}{argList}]");
        }
        if (lines.Count == 0) return null;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("// <auto-generated> Synthesized from csproj <AssemblyAttribute> items by the tps compiler.");
        foreach (var l in lines) sb.AppendLine(l);
        return sb.ToString();
    }

    /// <summary>Adds a project's sources and package references, then handles its ProjectReferences —
    /// recursing into their source (bundle mode) or referencing their built DLL (separate mode).</summary>
    private static void CollectProject(
        string csprojPath,
        List<(string, string)> sources,
        Dictionary<string, (string path, string version)> references,
        HashSet<string> visited,
        List<string> projectDirs,
        List<string> roots,
        bool separate,
        List<string> projectDlls,
        string configuration,
        bool isRoot)
    {
        csprojPath = Path.GetFullPath(csprojPath);
        if (!visited.Add(csprojPath) || !File.Exists(csprojPath)) return;

        var doc = ProjectXml.Load(csprojPath);
        var projectDir = Path.GetDirectoryName(csprojPath)!;

        // In separate mode only the root contributes source + tps.json resources; a referenced
        // project contributes its DLL (below) and its own package references for binding.
        if (!separate || isRoot)
        {
            projectDirs.Add(projectDir);
            sources.AddRange(ResolveSources(doc, projectDir));
            var asmAttrs = SynthesizeAssemblyAttributes(doc);
            if (asmAttrs is not null) sources.Add(("__TransposeAssemblyAttributes.g.cs", asmAttrs));
        }
        foreach (var (name, path, version) in ResolvePackageReferenceDlls(doc, roots))
            MergePackageReference(references, name, path, version);

        foreach (var (pr, _) in doc.Elements("ProjectReference"))
        {
            var include = pr.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include)) continue;
            var refPath = Path.GetFullPath(Path.Combine(projectDir, include!.Replace('\\', '/')));

            if (separate)
            {
                // Reference the dependency's built DLL (and, recursively, everything *it* references)
                // so types it exposes bind, but not its source or resources.
                CollectReferencedProjectPackages(refPath, references, visited, projectDlls, roots, configuration);
            }
            else
            {
                CollectProject(refPath, sources, references, visited, projectDirs, roots,
                    separate, projectDlls, configuration, isRoot: false);
            }
        }
    }

    /// <summary>
    /// Separate-assembly mode's ProjectReference walk: references a dependency's built DLL, merges its
    /// package references into the shared <paramref name="references"/> map, and recurses into *its*
    /// own ProjectReferences — so a package version required transitively (project B → project A →
    /// package L 1.2) is reconciled against a version B might declare on L itself, exactly as within a
    /// single project's own <see cref="ResolvePackageReferenceDlls"/> walk. Resolving only one
    /// ProjectReference level (as a non-recursive version of this once did) silently dropped a deeper
    /// dependency's package references, and merging with first-write-wins (<c>Dictionary.TryAdd</c>)
    /// let a project's own lower declared version of a package beat a higher version required by a
    /// project it also references — see <see cref="MergePackageReference"/>.
    /// </summary>
    private static void CollectReferencedProjectPackages(
        string refPath,
        Dictionary<string, (string path, string version)> references,
        HashSet<string> visited,
        List<string> projectDlls,
        List<string> roots,
        string configuration)
    {
        refPath = Path.GetFullPath(refPath);
        var dll = ProjectOutputDll(refPath, configuration);
        if (dll is not null)
        {
            references[Path.GetFileNameWithoutExtension(dll)] = (dll, string.Empty);
            if (!projectDlls.Contains(dll)) projectDlls.Add(dll);
        }

        if (!visited.Add(refPath) || ProjectXml.TryLoad(refPath) is not { } refDoc) return;

        foreach (var (name, path, version) in ResolvePackageReferenceDlls(refDoc, roots))
            MergePackageReference(references, name, path, version);

        var refDir = Path.GetDirectoryName(refPath)!;
        foreach (var (nested, _) in refDoc.Elements("ProjectReference"))
        {
            var ninc = nested.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(ninc)) continue;
            CollectReferencedProjectPackages(Path.GetFullPath(Path.Combine(refDir, ninc!.Replace('\\', '/'))),
                references, visited, projectDlls, roots, configuration);
        }
    }

    /// <summary>
    /// Merges one more resolved package assembly into the build-wide reference map, keeping the higher
    /// version when the same assembly is reached from two different projects in a ProjectReference
    /// closure — e.g. project B directly declares <c>PackageReference L 1.1</c> while also referencing
    /// project A, which declares <c>L 1.2</c>: B's build must resolve <c>L 1.2</c> too, matching how
    /// NuGet reconciles a solution-wide package graph (the higher floor version wins). Each project's
    /// *own* <see cref="ResolvePackageReferenceDlls"/> call already picks the right version among its
    /// own direct and transitive (nuspec) dependencies; this only reconciles across separate calls.
    /// </summary>
    private static void MergePackageReference(
        Dictionary<string, (string path, string version)> references, string name, string path, string version)
    {
        if (!references.TryGetValue(name, out var current) || CompareVersions(version, current.version) > 0)
            references[name] = (path, version);
    }

    /// <summary>
    /// The referenced projects of <paramref name="rootCsproj"/> in build order — every transitive
    /// ProjectReference, dependencies before dependents (post-order DFS), excluding the root. So a
    /// caller can build each one (as a package) before the project that consumes it.
    /// </summary>
    public static List<string> ReferencedProjectsInBuildOrder(string rootCsproj)
    {
        rootCsproj = Path.GetFullPath(rootCsproj);
        var order = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string csproj, bool isRoot)
        {
            csproj = Path.GetFullPath(csproj);
            if (!visited.Add(csproj) || !File.Exists(csproj)) return;
            var dir = Path.GetDirectoryName(csproj)!;
            var doc = ProjectXml.TryLoad(csproj);
            if (doc is null) return;
            foreach (var (pr, _) in doc.Elements("ProjectReference"))
            {
                var include = pr.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include)) continue;
                Visit(Path.GetFullPath(Path.Combine(dir, include!.Replace('\\', '/'))), isRoot: false);
            }
            if (!isRoot) order.Add(csproj);   // post-order → dependencies precede this project
        }

        Visit(rootCsproj, isRoot: true);
        return order;
    }

    /// <summary>The built output DLL path of a project, or null when the .csproj is missing.</summary>
    public static string? OutputDll(string csprojPath, string configuration) => ProjectOutputDll(csprojPath, configuration);

    /// <summary>
    /// True when the project produces a NuGet package (<c>GeneratePackageOnBuild</c> or
    /// <c>IsPackable</c> set to true). Such a project must emit the package assembly that
    /// <c>dotnet pack</c> wraps — even when it also carries a tps.json (which only configures its JS
    /// layout / embedded resources), so it must not be mistaken for a runnable site app.
    /// </summary>
    public static bool IsPackable(string csprojPath)
    {
        if (ProjectXml.TryLoad(csprojPath) is not { } doc) return false;
        static bool IsTrue(string? v) => string.Equals(v?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        return IsTrue(doc.Property("GeneratePackageOnBuild")) || IsTrue(doc.Property("IsPackable"));
    }

    /// <summary>
    /// True if <paramref name="csprojPath"/>'s package DLL is present, carries embedded Transpose resources
    /// (i.e. was built by the translator, not a plain csc build), and is newer than the project's
    /// .csproj, all its source files, and every referenced project's DLL — the same incremental
    /// check the MSBuild-driven compiler relies on. When false, the project must be rebuilt.
    /// </summary>
    public static bool IsPackageUpToDate(string csprojPath, string configuration)
    {
        csprojPath = Path.GetFullPath(csprojPath);
        var dll = ProjectOutputDll(csprojPath, configuration);
        if (dll is null || !File.Exists(dll)) return false;
        if (!ResourceEmbedder.HasManifest(dll)) return false;   // a plain build with no embedded JS

        var dllTime = File.GetLastWriteTimeUtc(dll);
        var dir = Path.GetDirectoryName(csprojPath)!;

        if (File.GetLastWriteTimeUtc(csprojPath) > dllTime) return false;

        foreach (var src in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            if (IsUnderBuildOutput(src, dir)) continue;
            if (File.GetLastWriteTimeUtc(src) > dllTime) return false;
        }

        if (ProjectXml.TryLoad(csprojPath) is not { } doc) return false;

        // Imported files (a shared project's .projitems) and the sources they contribute live outside
        // this project's directory, so the glob above never sees them. Without this, editing shared
        // source left the package "up to date" and the change simply did not reach the output.
        foreach (var imported in doc.ImportedFiles)
            if (File.GetLastWriteTimeUtc(imported) > dllTime) return false;
        foreach (var (path, _) in ResolveSources(doc, dir))
            if (File.Exists(path) && File.GetLastWriteTimeUtc(path) > dllTime) return false;

        // A dependency rebuilt more recently invalidates this project too.
        foreach (var (pr, _) in doc.Elements("ProjectReference"))
        {
            var include = pr.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include)) continue;
            var depDll = ProjectOutputDll(Path.GetFullPath(Path.Combine(dir, include!.Replace('\\', '/'))), configuration);
            if (depDll is null || !File.Exists(depDll) || File.GetLastWriteTimeUtc(depDll) > dllTime) return false;
        }

        return true;
    }

    /// <summary>Parses an MSBuild boolean property, returning null when it is absent or empty so a
    /// caller can tell "the project did not say" from "the project said false".</summary>
    private static bool? ParseBool(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    /// <summary>The built output DLL path of a referenced project: bin/&lt;config&gt;/&lt;tfm&gt;/&lt;asm&gt;.dll.</summary>
    private static string? ProjectOutputDll(string csprojPath, string configuration)
    {
        if (ProjectXml.TryLoad(csprojPath) is not { } doc) return null;
        var dir = Path.GetDirectoryName(csprojPath)!;
        var asm = doc.Property("AssemblyName") ?? Path.GetFileNameWithoutExtension(csprojPath);
        var tfm = EffectiveTargetFramework(doc);
        var binBase = Path.Combine(dir, "bin", configuration, tfm);
        var dll = Path.Combine(binBase, asm + ".dll");
        return dll;
    }

    private static List<(string path, string text)> ResolveSources(ProjectXml doc, string projectDir)
    {
        // The set of compiled files, in a deterministic order (glob first, then explicit includes),
        // deduplicated by full path (case-insensitive).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();
        void AddFile(string full)
        {
            full = Path.GetFullPath(full).Replace('\\', '/');
            if (seen.Add(full)) files.Add(full);
        }

        // SDK default glob: every .cs under the project directory, minus the build output — unless the
        // project opts out with <EnableDefaultCompileItems>false</EnableDefaultCompileItems>.
        if (!IsFalse(doc.Property("EnableDefaultCompileItems")))
        {
            foreach (var f in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
                if (!IsUnderBuildOutput(f, projectDir)) AddFile(f);
        }

        // Honour explicit <Compile Include="..."/>: single files, globs (*.cs / **/*.cs), and files
        // *outside* the project directory (e.g. shared source linked in via ..\..\Shared\*.cs). MSBuild
        // resolves these; tps reads the csproj raw, so it must expand them itself. Link/LinkBase only
        // affect IDE/output layout, not which file is compiled, so they are ignored here.
        //
        // Items from an <Import>ed file (a shared project's .projitems) come through here too, which is
        // the whole point of the flattened view: such a file writes its includes as
        // $(MSBuildThisFileDirectory)Foo.cs, so each item is expanded against the directory of the file
        // that *declared* it, while a plain relative path still resolves against the project directory
        // exactly as MSBuild does.
        foreach (var (element, declaringDir) in doc.Elements("Compile"))
        {
            var inc = element.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(inc)) continue;
            foreach (var f in ExpandInclude(projectDir, declaringDir, inc!))
                if (!IsUnderBuildOutput(f, projectDir)) AddFile(f);
        }

        // Honour explicit <Compile Remove="..."/> (glob or exact), with the same two-directory rule.
        var removed = new List<string>();
        foreach (var (element, declaringDir) in doc.Elements("Compile"))
        {
            var rem = element.Attribute("Remove")?.Value;
            if (string.IsNullOrWhiteSpace(rem)) continue;
            if (SubstituteThisFileDirectory(rem!, declaringDir) is { } pattern)
                removed.Add(NormalizeGlob(projectDir, pattern));
        }

        var result = new List<(string, string)>();
        foreach (var full in files)
        {
            if (removed.Any(r => MatchesGlob(full, r))) continue;
            result.Add((full, File.ReadAllText(full)));
        }
        return result;
    }

    /// <summary>Expands a <c>&lt;Compile Include&gt;</c> pattern into concrete .cs files. Handles a
    /// single file, a same-directory glob (<c>*.cs</c>), and a recursive glob (<c>**\*.cs</c>), and
    /// resolves patterns that reach outside the project directory (shared source linked in via
    /// <c>..\..\Shared\...</c>). <paramref name="declaringDir"/> is the directory of the file the item
    /// was written in, which is what <c>$(MSBuildThisFileDirectory)</c> means; everything relative is
    /// still resolved against <paramref name="projectDir"/>, as MSBuild does. A pattern containing a
    /// property this resolver cannot expand yields nothing rather than a guess.</summary>
    private static IEnumerable<string> ExpandInclude(string projectDir, string declaringDir, string rawPattern)
    {
        if (SubstituteThisFileDirectory(rawPattern, declaringDir) is not { } pattern) yield break;
        if (!pattern.Contains('*'))
        {
            var single = Path.GetFullPath(Path.Combine(projectDir, pattern));
            if (File.Exists(single)) yield return single;
            yield break;
        }

        // Split into the fixed base directory (up to the first wildcard segment) and the wildcard tail.
        var segments = pattern.Split('/');
        var firstWild = System.Array.FindIndex(segments, s => s.Contains('*'));
        var baseParts = segments.Take(firstWild);
        var baseDir = Path.GetFullPath(Path.Combine(projectDir, string.Join("/", baseParts)));
        if (!Directory.Exists(baseDir)) yield break;

        var tail = string.Join("/", segments.Skip(firstWild));
        // Recursive when the wildcard tail contains "**"; otherwise only the immediate directory.
        var recurse = tail.Contains("**") ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var fileGlob = segments[^1]; // e.g. "*.cs"
        var fullPattern = NormalizeGlob(projectDir, pattern);
        foreach (var f in Directory.EnumerateFiles(baseDir, fileGlob, recurse))
            if (MatchesGlob(Path.GetFullPath(f), fullPattern)) yield return f;
    }

    private static bool IsUnderBuildOutput(string file, string projectDir)
    {
        var rel = Path.GetRelativePath(projectDir, file).Replace('\\', '/');
        return rel.StartsWith("obj/") || rel.StartsWith("bin/")
            || rel.Contains("/obj/") || rel.Contains("/bin/");
    }

    /// <summary>Expands <c>$(MSBuildThisFileDirectory)</c> (and the project-directory equivalent) in an
    /// item specification, returning null when some other unexpandable <c>$(…)</c> remains — see
    /// <see cref="ProjectXml.ResolvePath"/> for why guessing is worse than skipping.</summary>
    private static string? SubstituteThisFileDirectory(string spec, string declaringDir)
    {
        var value = spec.Replace('\\', '/').Trim();
        var dirWithSlash = declaringDir.Replace('\\', '/').TrimEnd('/') + "/";
        value = value
            .Replace("$(MSBuildThisFileDirectory)", dirWithSlash, StringComparison.OrdinalIgnoreCase)
            .Replace("$(MSBuildProjectDirectory)/", dirWithSlash, StringComparison.OrdinalIgnoreCase)
            .Replace("$(MSBuildProjectDirectory)", dirWithSlash, StringComparison.OrdinalIgnoreCase);
        return value.Contains("$(", StringComparison.Ordinal) ? null : value;
    }

    private static string NormalizeGlob(string projectDir, string pattern)
        => Path.GetFullPath(Path.Combine(projectDir, pattern.Replace('\\', '/'))).Replace('\\', '/');

    private static bool MatchesGlob(string fullPath, string pattern)
    {
        fullPath = fullPath.Replace('\\', '/');
        if (!pattern.Contains('*')) return string.Equals(fullPath, pattern, StringComparison.OrdinalIgnoreCase);
        // Support the common **/*.cs / *.cs shapes with a simple regex translation.
        var rx = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace(@"\*\*/", "(.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(fullPath, rx, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    // ---- reference resolution (NuGet global-packages cache) -----------------

    /// <summary>
    /// The assemblies a project's <c>&lt;PackageReference&gt;</c> items contribute, resolved from the
    /// NuGet global-packages cache as <c>assembly simple name → dll path</c>.
    ///
    /// Precedence follows NuGet's own rules rather than the order the items happen to appear in: the
    /// version the project declares itself wins over one reached through another package's
    /// dependencies (NuGet's "direct dependency wins"), and between two transitive candidates the
    /// higher version wins. Resolving in document order instead silently downgraded a package — a
    /// csproj listing <c>Tesserae.GraphKit</c> above <c>Tesserae</c> compiled against the older
    /// Tesserae that GraphKit's nuspec declares, ignoring the version written right there in the
    /// csproj.
    /// </summary>
    private static IEnumerable<(string name, string path, string version)> ResolvePackageReferenceDlls(ProjectXml doc, List<string> roots)
    {
        // pkgId → the version the project declares (highest, if it declares one id twice).
        var declared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pr, _) in doc.Elements("PackageReference"))
        {
            var id = pr.Attribute("Include")?.Value ?? pr.Attribute("Update")?.Value;
            var version = pr.Attribute("Version")?.Value ?? pr.Element(pr.Name.Namespace + "Version")?.Value;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version)) continue;
            if (!declared.TryGetValue(id!, out var already) || CompareVersions(version!, already) > 0)
                declared[id!] = version!;
        }

        return ResolveDeclaredPackages(declared, roots);
    }

    /// <summary>
    /// Resolves a single package (and its transitive dependencies) from the NuGet global-packages
    /// cache, exactly as one <c>&lt;PackageReference&gt;</c> in a csproj would — for a caller that has
    /// a package id and version but no project file (<c>Transpose.Compiler.Service</c>'s
    /// <c>CompilationRequest.WithPackageReference</c>).
    /// </summary>
    public static IEnumerable<(string name, string path, string version)> ResolvePackage(string packageId, string version)
    {
        var roots = NuGetRoots().Where(Directory.Exists).ToList();
        var declared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [packageId] = version };
        return ResolveDeclaredPackages(declared, roots);
    }

    /// <summary>
    /// The shared resolution core behind <see cref="ResolvePackageReferenceDlls"/> and
    /// <see cref="ResolvePackage"/>: given the packages a caller declares directly (highest version
    /// per id), walks their transitive nuspec dependencies and resolves every assembly they
    /// contribute, applying NuGet's own precedence — a declared version always wins over one reached
    /// transitively, and between two transitive candidates the higher version wins.
    /// </summary>
    private static IEnumerable<(string name, string path, string version)> ResolveDeclaredPackages(
        Dictionary<string, string> declared, List<string> roots)
    {
        var resolved = new Dictionary<string, (string path, string version, bool declared)>(StringComparer.OrdinalIgnoreCase);
        var visited  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);                     // pkgId@version
        var queue    = new Queue<(string id, string version, bool isDeclared)>();

        // Breadth-first from the declared set, so every declared package is resolved before any
        // transitive one can offer the same assembly.
        foreach (var (id, version) in declared) queue.Enqueue((id, version, true));

        while (queue.Count > 0)
        {
            var (id, version, isDeclared) = queue.Dequeue();

            // A transitive dependency on a package the project declares itself is ignored outright:
            // the declared version supersedes it, so its cache folder is never even read.
            if (!isDeclared && declared.ContainsKey(id)) continue;
            if (!visited.Add(id + "@" + version)) continue;

            var pkgDir = roots
                .Select(r => Path.Combine(r, id.ToLowerInvariant(), version))
                .FirstOrDefault(Directory.Exists);
            if (pkgDir is null) continue;

            var libDir = BestLibDir(pkgDir);
            if (libDir is not null)
            {
                foreach (var dll in Directory.GetFiles(libDir, "*.dll"))
                {
                    var name = Path.GetFileNameWithoutExtension(dll);
                    if (Wins(name, version, isDeclared)) resolved[name] = (dll, version, isDeclared);
                }
            }

            // Follow the package's declared dependencies so transitive BCL/interop types resolve.
            var nuspec = Path.Combine(pkgDir, id.ToLowerInvariant() + ".nuspec");
            if (!File.Exists(nuspec)) continue;
            try
            {
                var ndoc = XDocument.Load(nuspec);
                foreach (var dep in ndoc.Descendants().Where(e => e.Name.LocalName == "dependency"))
                {
                    var depId = dep.Attribute("id")?.Value;
                    var depVer = dep.Attribute("version")?.Value?.Trim('[', ']', '(', ')').Split(',')[0];
                    if (!string.IsNullOrWhiteSpace(depId) && !string.IsNullOrWhiteSpace(depVer))
                        queue.Enqueue((depId!, depVer!, false));
                }
            }
            catch { /* best-effort transitive resolution */ }
        }

        return resolved.Select(kv => (kv.Key, kv.Value.path, kv.Value.version));

        bool Wins(string name, string version, bool isDeclared)
        {
            if (!resolved.TryGetValue(name, out var current)) return true;
            if (current.declared) return false;                        // a declared version is never displaced
            return isDeclared || CompareVersions(version, current.version) > 0;
        }
    }

    /// <summary>Compares two NuGet version strings by their numeric segments, ignoring any
    /// prerelease/metadata suffix — enough to pick between two versions of one package in the cache
    /// (a full NuGet.Versioning dependency would buy nothing here).</summary>
    private static int CompareVersions(string a, string b)
    {
        var left  = Segments(a);
        var right = Segments(b);

        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var x = i < left.Length ? left[i] : 0;
            var y = i < right.Length ? right[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;

        static int[] Segments(string v) => v.Trim().Split('-', '+')[0].Split('.')
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .ToArray();
    }

    private static string? BestLibDir(string pkgDir)
    {
        var lib = Path.Combine(pkgDir, "lib");
        if (!Directory.Exists(lib)) return null;
        var tfms = Directory.GetDirectories(lib);
        // Prefer netstandard2.0 (what the tps packages ship), else any netstandard, else the first.
        return tfms.FirstOrDefault(d => Path.GetFileName(d).Equals("netstandard2.0", StringComparison.OrdinalIgnoreCase))
            ?? tfms.FirstOrDefault(d => Path.GetFileName(d).StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
            ?? tfms.FirstOrDefault();
    }

    private static IEnumerable<string> NuGetRoots()
    {
        var env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(env)) yield return env;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".nuget", "packages");
        yield return "/root/.nuget/packages";
    }

    /// <summary>The project's <c>&lt;AssemblyVersion&gt;</c> (falling back to <c>&lt;Version&gt;</c>),
    /// emitted into the bundle as <c>Transpose.assemblyVersion(...)</c>. Null when neither is set.</summary>
    public static string? ReadAssemblyVersion(string csprojPath)
    {
        try { var doc = ProjectXml.Load(csprojPath); return doc.Property("AssemblyVersion") ?? doc.Property("Version"); }
        catch { return null; }
    }

    private static bool IsFalse(string? v) => string.Equals(v?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The target framework the build actually produces its assembly under — the path
    /// <c>dotnet pack</c> then looks for it at (<c>bin/&lt;config&gt;/&lt;tfm&gt;/&lt;Assembly&gt;.dll</c>).
    /// The Transpose.Build.Target SDK forcibly overrides <c>&lt;TargetFramework&gt;</c> to
    /// <c>netstandard2.0</c> in its Sdk.targets (regardless of what the csproj declares), so a
    /// binding library that declares e.g. <c>netstandard2.1</c> is still built and packed as
    /// <c>netstandard2.0</c>. Honour that here, otherwise tps writes the DLL under the declared tfm
    /// and <c>dotnet pack</c> fails with NU5026 (<c>&lt;Assembly&gt;.dll</c> not found).
    /// </summary>
    private static string EffectiveTargetFramework(ProjectXml doc)
    {
        var sdk = doc.SdkName ?? "";
        if (sdk.StartsWith("Transpose.Build.Target", StringComparison.OrdinalIgnoreCase))
            return "netstandard2.0";
        return doc.Property("TargetFramework")
               ?? doc.Property("TargetFrameworks")?.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
               ?? "netstandard2.0";
    }

    // Version ladders per framework family, in ascending order — used to generate the SDK's
    // "<MONIKER>x_y_OR_GREATER" chains (every version up to and including the target's).
    private static readonly string[] NetStandardVersions =
        { "1.0", "1.1", "1.2", "1.3", "1.4", "1.5", "1.6", "2.0", "2.1" };
    private static readonly string[] NetCoreAppLegacyVersions =
        { "1.0", "1.1", "2.0", "2.1", "2.2", "3.0", "3.1" };
    private static readonly string[] NetVersions =
        { "5.0", "6.0", "7.0", "8.0", "9.0", "10.0" };

    /// <summary>
    /// The preprocessor symbols the .NET SDK defines implicitly from a target framework moniker,
    /// mirroring the SDK's implicit framework defines:
    /// <list type="bullet">
    /// <item><c>netstandardX.Y</c> → <c>NETSTANDARD</c>, <c>NETSTANDARDX_Y</c>, and
    /// <c>NETSTANDARDa_b_OR_GREATER</c> for every version up to X.Y.</item>
    /// <item><c>netcoreappX.Y</c> (≤ 3.1) → <c>NETCOREAPP</c>, <c>NETCOREAPPX_Y</c>, and the
    /// <c>NETCOREAPPa_b_OR_GREATER</c> chain.</item>
    /// <item><c>netX.Y</c> (5.0+) → <c>NET</c>, <c>NETCOREAPP</c>, <c>NETX_Y</c>, the full legacy
    /// <c>NETCOREAPP*_OR_GREATER</c> chain, and <c>NETa_b_OR_GREATER</c> for every 5.0+ version up to X.Y.</item>
    /// <item><c>net4x</c> (.NET Framework) → <c>NETFRAMEWORK</c>, <c>NET4x</c>.</item>
    /// </list>
    /// </summary>
    internal static IEnumerable<string> FrameworkDefines(string tfm)
    {
        tfm = (tfm ?? "").Trim().ToLowerInvariant();
        // Drop an OS platform suffix (net8.0-windows → net8.0).
        var dash = tfm.IndexOf('-');
        if (dash > 0) tfm = tfm.Substring(0, dash);

        static string Sym(string prefix, string ver) => prefix + ver.Replace('.', '_');
        var result = new List<string>();

        if (tfm.StartsWith("netstandard", StringComparison.Ordinal))
        {
            var ver = tfm.Substring("netstandard".Length);
            result.Add("NETSTANDARD");
            result.Add(Sym("NETSTANDARD", ver));
            foreach (var v in NetStandardVersions)
            {
                result.Add(Sym("NETSTANDARD", v) + "_OR_GREATER");
                if (v == ver) break;
            }
        }
        else if (tfm.StartsWith("netcoreapp", StringComparison.Ordinal))
        {
            var ver = tfm.Substring("netcoreapp".Length);
            result.Add("NETCOREAPP");
            result.Add(Sym("NETCOREAPP", ver));
            foreach (var v in NetCoreAppLegacyVersions)
            {
                result.Add(Sym("NETCOREAPP", v) + "_OR_GREATER");
                if (v == ver) break;
            }
        }
        else if (tfm.StartsWith("net", StringComparison.Ordinal) && tfm.Length > 3 && tfm.Contains('.'))
        {
            // net5.0+ — the continuation of netcoreapp.
            var ver = tfm.Substring("net".Length);
            result.Add("NET");
            result.Add("NETCOREAPP");
            result.Add(Sym("NET", ver));
            // The whole legacy netcoreapp chain is implied.
            foreach (var v in NetCoreAppLegacyVersions)
                result.Add(Sym("NETCOREAPP", v) + "_OR_GREATER");
            foreach (var v in NetVersions)
            {
                result.Add(Sym("NET", v) + "_OR_GREATER");
                if (v == ver) break;
            }
        }
        else if (tfm.StartsWith("net", StringComparison.Ordinal) && tfm.Length > 3)
        {
            // .NET Framework (net46, net472, net48, …): the digits are the version (net472 → 4.7.2).
            var digits = tfm.Substring("net".Length);
            result.Add("NETFRAMEWORK");
            result.Add("NET" + digits);
        }

        return result;
    }

    private static LanguageVersion ParseLangVersion(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return LanguageVersion.Latest;
        return v.Trim().ToLowerInvariant() switch
        {
            "latest" or "latestmajor" or "preview" or "default" => LanguageVersion.Latest,
            _ => LanguageVersionFacts.TryParse(v, out var parsed) ? parsed : LanguageVersion.Latest,
        };
    }
}
