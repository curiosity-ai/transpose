using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace H5.Translator.Roslyn.CLI;

/// <summary>
/// Minimal resolver for an H5 project: gathers the C# sources (the SDK's default glob),
/// and resolves the package references — transitively — to their assemblies in the NuGet
/// global-packages cache. H5 projects reference H5.dll as their whole BCL plus a few h5.*
/// packages (h5.core, h5.Newtonsoft.Json), all of which live in the cache once restored.
/// </summary>
internal sealed class ResolvedProject
{
    public required string CsprojPath { get; init; }
    public required string ProjectDir { get; init; }
    public required string AssemblyName { get; init; }
    public required List<(string path, string text)> Sources { get; init; }
    public required List<string> ReferencePaths { get; init; }
    public required List<string> DefineConstants { get; init; }
    public required LanguageVersion LanguageVersion { get; init; }
}

internal static class ProjectResolver
{
    public static ResolvedProject Resolve(string csprojPath)
    {
        csprojPath = Path.GetFullPath(csprojPath);
        var projectDir = Path.GetDirectoryName(csprojPath)!;
        var doc = XDocument.Load(csprojPath);

        var assemblyName = Property(doc, "AssemblyName") ?? Path.GetFileNameWithoutExtension(csprojPath);

        var defines = (Property(doc, "DefineConstants") ?? "")
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
        if (!defines.Contains("H5")) defines.Add("H5"); // H5 projects always compile the H5 branch

        var lang = ParseLangVersion(Property(doc, "LangVersion"));

        var sources = ResolveSources(doc, projectDir);
        var references = ResolveReferences(doc);

        return new ResolvedProject
        {
            CsprojPath = csprojPath,
            ProjectDir = projectDir,
            AssemblyName = assemblyName,
            Sources = sources,
            ReferencePaths = references,
            DefineConstants = defines,
            LanguageVersion = lang,
        };
    }

    private static List<(string path, string text)> ResolveSources(XDocument doc, string projectDir)
    {
        // SDK default glob: every .cs under the project directory, minus the build output.
        var all = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsUnderBuildOutput(f, projectDir))
            .ToList();

        // Honour explicit <Compile Remove="..."/> (glob or exact, relative to the project dir).
        var removed = doc.Descendants().Where(e => e.Name.LocalName == "Compile")
            .Select(e => e.Attribute("Remove")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => NormalizeGlob(projectDir, v!))
            .ToList();

        var result = new List<(string, string)>();
        foreach (var file in all)
        {
            var full = Path.GetFullPath(file);
            if (removed.Any(r => MatchesGlob(full, r))) continue;
            result.Add((full, File.ReadAllText(full)));
        }
        return result;
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

    private static List<string> ResolveReferences(XDocument doc)
    {
        var roots = NuGetRoots().Where(Directory.Exists).ToList();
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // asmName → dll path
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);             // pkgId@version

        foreach (var pr in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
        {
            var id = pr.Attribute("Include")?.Value ?? pr.Attribute("Update")?.Value;
            var version = pr.Attribute("Version")?.Value ?? pr.Element(pr.Name.Namespace + "Version")?.Value;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version)) continue;
            ResolvePackage(roots, id!, version!, resolved, visited);
        }

        return resolved.Values.ToList();
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
        // Prefer netstandard2.0 (what the h5 packages ship), else any netstandard, else the first.
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
