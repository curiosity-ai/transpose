using System.Xml.Linq;

namespace Transpose.Compiler;

/// <summary>
/// A project file together with every file it pulls in via <c>&lt;Import Project="…"/&gt;</c>, flattened
/// into one queryable view.
///
/// <c>tps</c> reads the csproj as raw XML rather than evaluating it with MSBuild, so an
/// <c>&lt;Import&gt;</c> used to be invisible — and that is how **shared projects** work: a
/// <c>.shproj</c>'s companion <c>.projitems</c> holds the <c>&lt;Compile&gt;</c> items and every
/// consuming project imports it. Without following imports, all of that source silently vanished from
/// the compilation and the build failed with "type or namespace not found" for code that plainly
/// exists.
///
/// Each element is kept alongside the directory of the file that *declared* it, because the two
/// directories mean different things in MSBuild and a <c>.projitems</c> depends on both:
///
///   * <c>$(MSBuildThisFileDirectory)</c> is the declaring file's own directory (with a trailing
///     separator). A <c>.projitems</c> always writes its includes this way — that is what makes the
///     same file work from consumers in different folders.
///   * a plain relative <c>Include</c> is resolved against the *project* directory, wherever it was
///     written.
///
/// MSBuild conditions are not evaluated (nor are they anywhere else in this resolver), so an import is
/// followed whenever its path resolves to a file that exists. That is also what keeps SDK-internal
/// imports out: they are written in terms of properties like <c>$(MSBuildToolsPath)</c> that this
/// resolver cannot expand, so they simply do not resolve.
/// </summary>
internal sealed class ProjectXml
{
    /// <summary>Guards against a cycle that <see cref="_visited"/> would not catch (a file importing
    /// itself through different relative spellings) and against pathological nesting.</summary>
    private const int MaxImportDepth = 32;

    /// <summary>Every element of the project and its imports, paired with the directory of the file
    /// that declared it, in document order — the project's own elements first, then each import's,
    /// which is where an <c>&lt;Import&gt;</c> of a .projitems sits in a real project file and so the
    /// order MSBuild's last-write-wins evaluation sees (<see cref="Property"/>).</summary>
    private readonly List<(XElement element, string declaringDir)> _elements = new();

    private readonly HashSet<string> _visited = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The project file this view is rooted at.</summary>
    public string ProjectPath { get; }

    /// <summary>The project's directory — the base for relative item includes.</summary>
    public string ProjectDir { get; }

    /// <summary>Absolute paths of the imported files that were actually found and read. Callers that
    /// do up-to-date checks need these: a change to a <c>.projitems</c> (or to the files it lists) has
    /// to invalidate the build just as a change to the csproj does.</summary>
    public IReadOnlyList<string> ImportedFiles { get; }

    private ProjectXml(string projectPath)
    {
        ProjectPath = Path.GetFullPath(projectPath);
        ProjectDir = Path.GetDirectoryName(ProjectPath)!;
        var imported = new List<string>();
        Add(ProjectPath, depth: 0, imported);
        ImportedFiles = imported;
    }

    /// <summary>Loads <paramref name="projectPath"/> and its transitive imports. Returns null when the
    /// file does not exist or is not readable XML — callers already treat a missing project as
    /// "nothing to contribute".</summary>
    public static ProjectXml? TryLoad(string projectPath)
    {
        if (!File.Exists(projectPath)) return null;
        try { return new ProjectXml(projectPath); }
        catch { return null; }
    }

    /// <summary>Loads <paramref name="projectPath"/>, throwing if it cannot be read — for the root
    /// project, where an unreadable csproj is a hard error the user must see.</summary>
    public static ProjectXml Load(string projectPath) => new(projectPath);

    private void Add(string file, int depth, List<string> imported)
    {
        file = Path.GetFullPath(file);
        if (depth > MaxImportDepth || !_visited.Add(file) || !File.Exists(file)) return;

        XDocument doc;
        try { doc = XDocument.Load(file); }
        catch { return; }   // an unreadable import must not fail the whole resolve
        if (depth > 0) imported.Add(file);

        if (depth == 0)
            SdkName = doc.Root?.Attribute("Sdk")?.Value
                      ?? doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Sdk")?.Attribute("Name")?.Value;

        var dir = Path.GetDirectoryName(file)!;
        foreach (var element in doc.Descendants())
            _elements.Add((element, dir));

        // Depth-first, in document order, so imported elements land after the importing file's own.
        foreach (var import in doc.Descendants().Where(e => e.Name.LocalName == "Import"))
        {
            var spec = import.Attribute("Project")?.Value;
            if (string.IsNullOrWhiteSpace(spec)) continue;
            var resolved = ResolvePath(spec!, dir, dir);
            if (resolved is not null) Add(resolved, depth + 1, imported);
        }
    }

    /// <summary>Every element with the given local name, each with the directory of its declaring
    /// file.</summary>
    public IEnumerable<(XElement element, string declaringDir)> Elements(string localName)
        => _elements.Where(e => e.element.Name.LocalName == localName);

    /// <summary>The project's <c>Sdk</c> attribute (or a nested <c>&lt;Sdk Name="…"/&gt;</c>) — read from
    /// the project itself, never from an import, since the SDK is a property of the project.</summary>
    public string? SdkName { get; private set; }

    /// <summary>
    /// The value MSBuild would evaluate for a property: the <b>last</b> declaration wins, because that
    /// is what MSBuild does — a project that writes <c>&lt;LangVersion&gt;</c> in two
    /// <c>&lt;PropertyGroup&gt;</c>s compiles with the second one. Reading the first is how <c>tps</c>
    /// and an IDE came to disagree about the same csproj: the compiler took 7.2 from the top of the
    /// file while the editor took the 7 written further down, so the editor reported errors on code
    /// that built, in a language version nothing was actually compiling at.
    ///
    /// Conditions are not evaluated anywhere in this resolver, so a declaration guarded by one (on
    /// itself or on any group above it) is not a value that can be trusted: it is skipped while an
    /// unguarded declaration exists, and used only when every candidate is guarded.
    /// </summary>
    public string? Property(string name)
    {
        var candidates = _elements
            .Where(e => e.element.Name.LocalName == name)
            .Select(e => e.element)
            .ToList();
        if (candidates.Count == 0) return null;

        var unconditional = candidates.Where(e => !IsConditional(e)).ToList();
        var winning       = unconditional.Count > 0 ? unconditional : candidates;

        return winning[winning.Count - 1].Value;
    }

    /// <summary>Whether the element, or anything containing it, carries a <c>Condition</c>.</summary>
    private static bool IsConditional(XElement element)
    {
        for (var e = element; e is not null; e = e.Parent)
            if (e.Attribute("Condition") is not null) return true;

        return false;
    }

    /// <summary>
    /// Resolves an MSBuild path expression to an absolute path, or null when it cannot be resolved
    /// (an unexpandable property, or a file that is not there).
    ///
    /// <paramref name="declaringDir"/> is what <c>$(MSBuildThisFileDirectory)</c> expands to;
    /// <paramref name="baseDir"/> is what a relative path is resolved against. For an
    /// <c>&lt;Import&gt;</c> both are the declaring file's directory; for an item include the base is
    /// the project directory.
    /// </summary>
    public static string? ResolvePath(string spec, string declaringDir, string baseDir)
    {
        var expanded = ExpandThisFileDirectory(spec, declaringDir);
        if (expanded is null) return null;
        var full = Path.GetFullPath(Path.Combine(baseDir, expanded));
        return File.Exists(full) ? full : null;
    }

    /// <summary>
    /// Expands the two <c>$(MSBuildThisFile…)</c> forms a <c>.projitems</c> uses, and gives up (null)
    /// on any other <c>$(…)</c> property: guessing at a property this resolver never evaluated would
    /// silently compile the wrong set of files, whereas skipping is visible and safe.
    /// <c>$(MSBuildThisFileDirectory)</c> keeps MSBuild's trailing separator, so
    /// <c>$(MSBuildThisFileDirectory)Foo.cs</c> concatenates correctly.
    /// </summary>
    private static string? ExpandThisFileDirectory(string spec, string declaringDir)
    {
        var value = spec.Replace('\\', '/').Trim();
        var dirWithSlash = declaringDir.Replace('\\', '/').TrimEnd('/') + "/";

        value = value
            .Replace("$(MSBuildThisFileDirectory)", dirWithSlash, StringComparison.OrdinalIgnoreCase)
            .Replace("$(MSBuildProjectDirectory)/", dirWithSlash, StringComparison.OrdinalIgnoreCase)
            .Replace("$(MSBuildProjectDirectory)", dirWithSlash, StringComparison.OrdinalIgnoreCase);

        return value.Contains("$(", StringComparison.Ordinal) ? null : value;
    }
}
