using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace Transpose.Compiler;

/// <summary>
/// Minimal resolver for an Transpose project: gathers the C# sources (the SDK's default glob),
/// and resolves the package references — transitively — to their assemblies in the NuGet
/// global-packages cache. Transpose projects reference Transpose.dll as their whole BCL plus a few Transpose.*
/// packages (Transpose.Core, Transpose.Newtonsoft.Json), all of which live in the cache once restored.
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
        var doc = XDocument.Load(csprojPath);

        var assemblyName = Property(doc, "AssemblyName") ?? Path.GetFileNameWithoutExtension(csprojPath);

        var targetFramework = EffectiveTargetFramework(doc);

        var defines = (Property(doc, "DefineConstants") ?? "")
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
        if (!defines.Contains("Transpose")) defines.Add("Transpose"); // Transpose projects always compile the Transpose branch

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

        var lang = ParseLangVersion(Property(doc, "LangVersion"));

        // Default (bundle) mode: translate the whole closure of source projects into one JS
        // output. Separate-assembly mode: compile only this project's own sources and reference
        // each project dependency as its built DLL (its JS is embedded and extracted, not recompiled).
        var sources = new List<(string, string)>();
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            ReferencePaths = references.Values.ToList(),
            DefineConstants = defines,
            LanguageVersion = lang,
            MinifyLocalVariables = string.Equals(Property(doc, "MinifyLocalVariables")?.Trim(), "true", StringComparison.OrdinalIgnoreCase),
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
    private static string? SynthesizeAssemblyAttributes(XDocument doc)
    {
        var lines = new List<string>();
        foreach (var e in doc.Descendants().Where(e => e.Name.LocalName == "AssemblyAttribute"))
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
        Dictionary<string, string> references,
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

        var doc = XDocument.Load(csprojPath);
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
        foreach (var (name, path) in ResolvePackageReferenceDlls(doc, roots))
            references.TryAdd(name, path);

        foreach (var pr in doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
        {
            var include = pr.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include)) continue;
            var refPath = Path.GetFullPath(Path.Combine(projectDir, include!.Replace('\\', '/')));

            if (separate)
            {
                // Reference the dependency's built DLL; still gather its package refs (and its own
                // project-ref DLLs) so types it exposes bind, but not its source or resources.
                var dll = ProjectOutputDll(refPath, configuration);
                if (dll is not null)
                {
                    references[Path.GetFileNameWithoutExtension(dll)] = dll;
                    if (!projectDlls.Contains(dll)) projectDlls.Add(dll);
                }
                if (visited.Add(refPath) && File.Exists(refPath))
                {
                    var refDoc = XDocument.Load(refPath);
                    var refDir = Path.GetDirectoryName(refPath)!;
                    foreach (var (name, path) in ResolvePackageReferenceDlls(refDoc, roots))
                        references.TryAdd(name, path);
                    foreach (var nested in refDoc.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
                    {
                        var ninc = nested.Attribute("Include")?.Value;
                        if (string.IsNullOrWhiteSpace(ninc)) continue;
                        var ndll = ProjectOutputDll(Path.GetFullPath(Path.Combine(refDir, ninc!.Replace('\\', '/'))), configuration);
                        if (ndll is not null)
                        {
                            references[Path.GetFileNameWithoutExtension(ndll)] = ndll;
                            if (!projectDlls.Contains(ndll)) projectDlls.Add(ndll);
                        }
                    }
                }
            }
            else
            {
                CollectProject(refPath, sources, references, visited, projectDirs, roots,
                    separate, projectDlls, configuration, isRoot: false);
            }
        }
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
            var doc = XDocument.Load(csproj);
            foreach (var pr in doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
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
        if (!File.Exists(csprojPath)) return false;
        var doc = XDocument.Load(csprojPath);
        static bool IsTrue(string? v) => string.Equals(v?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        return IsTrue(Property(doc, "GeneratePackageOnBuild")) || IsTrue(Property(doc, "IsPackable"));
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

        // A dependency rebuilt more recently invalidates this project too.
        var doc = XDocument.Load(csprojPath);
        foreach (var pr in doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
        {
            var include = pr.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include)) continue;
            var depDll = ProjectOutputDll(Path.GetFullPath(Path.Combine(dir, include!.Replace('\\', '/'))), configuration);
            if (depDll is null || !File.Exists(depDll) || File.GetLastWriteTimeUtc(depDll) > dllTime) return false;
        }

        return true;
    }

    /// <summary>The built output DLL path of a referenced project: bin/&lt;config&gt;/&lt;tfm&gt;/&lt;asm&gt;.dll.</summary>
    private static string? ProjectOutputDll(string csprojPath, string configuration)
    {
        if (!File.Exists(csprojPath)) return null;
        var doc = XDocument.Load(csprojPath);
        var dir = Path.GetDirectoryName(csprojPath)!;
        var asm = Property(doc, "AssemblyName") ?? Path.GetFileNameWithoutExtension(csprojPath);
        var tfm = EffectiveTargetFramework(doc);
        var binBase = Path.Combine(dir, "bin", configuration, tfm);
        var dll = Path.Combine(binBase, asm + ".dll");
        return dll;
    }

    private static List<(string path, string text)> ResolveSources(XDocument doc, string projectDir)
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
        if (!IsFalse(Property(doc, "EnableDefaultCompileItems")))
        {
            foreach (var f in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
                if (!IsUnderBuildOutput(f, projectDir)) AddFile(f);
        }

        // Honour explicit <Compile Include="..."/>: single files, globs (*.cs / **/*.cs), and files
        // *outside* the project directory (e.g. shared source linked in via ..\..\Shared\*.cs). MSBuild
        // resolves these; tps reads the csproj raw, so it must expand them itself. Link/LinkBase only
        // affect IDE/output layout, not which file is compiled, so they are ignored here.
        foreach (var inc in doc.Descendants().Where(e => e.Name.LocalName == "Compile")
                     .Select(e => e.Attribute("Include")?.Value)
                     .Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            foreach (var f in ExpandInclude(projectDir, inc!))
                if (!IsUnderBuildOutput(f, projectDir)) AddFile(f);
        }

        // Honour explicit <Compile Remove="..."/> (glob or exact, relative to the project dir).
        var removed = doc.Descendants().Where(e => e.Name.LocalName == "Compile")
            .Select(e => e.Attribute("Remove")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => NormalizeGlob(projectDir, v!))
            .ToList();

        var result = new List<(string, string)>();
        foreach (var full in files)
        {
            if (removed.Any(r => MatchesGlob(full, r))) continue;
            result.Add((full, File.ReadAllText(full)));
        }
        return result;
    }

    /// <summary>Expands a <c>&lt;Compile Include&gt;</c> pattern (relative to the project directory)
    /// into concrete .cs files. Handles a single file, a same-directory glob (<c>*.cs</c>), and a
    /// recursive glob (<c>**\*.cs</c>), and resolves patterns that reach outside the project directory
    /// (shared source linked in via <c>..\..\Shared\...</c>).</summary>
    private static IEnumerable<string> ExpandInclude(string projectDir, string pattern)
    {
        pattern = pattern.Replace('\\', '/');
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

    private static IEnumerable<(string name, string path)> ResolvePackageReferenceDlls(XDocument doc, List<string> roots)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // asmName → dll path
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);             // pkgId@version

        foreach (var pr in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
        {
            var id = pr.Attribute("Include")?.Value ?? pr.Attribute("Update")?.Value;
            var version = pr.Attribute("Version")?.Value ?? pr.Element(pr.Name.Namespace + "Version")?.Value;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version)) continue;
            ResolvePackage(roots, id!, version!, resolved, visited);
        }

        return resolved.Select(kv => (kv.Key, kv.Value));
    }

    private static void ResolvePackage(
        List<string> roots, string id, string version,
        Dictionary<string, string> resolved, HashSet<string> visited)
    {
        var key = id + "@" + version;
        if (!visited.Add(key)) return;

        var pkgDir = roots
            .Select(r => Path.Combine(r, id.ToLowerInvariant(), version))
            .FirstOrDefault(Directory.Exists);
        if (pkgDir is null) return;

        var libDir = BestLibDir(pkgDir);
        if (libDir is not null)
        {
            foreach (var dll in Directory.GetFiles(libDir, "*.dll"))
            {
                var name = Path.GetFileNameWithoutExtension(dll);
                if (!resolved.ContainsKey(name)) resolved[name] = dll;
            }
        }

        // Follow the package's declared dependencies so transitive BCL/interop types resolve.
        var nuspec = Path.Combine(pkgDir, id.ToLowerInvariant() + ".nuspec");
        if (File.Exists(nuspec))
        {
            try
            {
                var ndoc = XDocument.Load(nuspec);
                foreach (var dep in ndoc.Descendants().Where(e => e.Name.LocalName == "dependency"))
                {
                    var depId = dep.Attribute("id")?.Value;
                    var depVer = dep.Attribute("version")?.Value?.Trim('[', ']', '(', ')').Split(',')[0];
                    if (!string.IsNullOrWhiteSpace(depId) && !string.IsNullOrWhiteSpace(depVer))
                        ResolvePackage(roots, depId!, depVer!, resolved, visited);
                }
            }
            catch { /* best-effort transitive resolution */ }
        }
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

    private static string? Property(XDocument doc, string name)
        => doc.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    /// <summary>The project's <c>&lt;AssemblyVersion&gt;</c> (falling back to <c>&lt;Version&gt;</c>),
    /// emitted into the bundle as <c>Transpose.assemblyVersion(...)</c>. Null when neither is set.</summary>
    public static string? ReadAssemblyVersion(string csprojPath)
    {
        try { var doc = XDocument.Load(csprojPath); return Property(doc, "AssemblyVersion") ?? Property(doc, "Version"); }
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
    private static string EffectiveTargetFramework(XDocument doc)
    {
        var sdk = doc.Root?.Attribute("Sdk")?.Value
                  ?? doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Sdk")?.Attribute("Name")?.Value
                  ?? "";
        if (sdk.StartsWith("Transpose.Build.Target", StringComparison.OrdinalIgnoreCase))
            return "netstandard2.0";
        return Property(doc, "TargetFramework")
               ?? Property(doc, "TargetFrameworks")?.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
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
