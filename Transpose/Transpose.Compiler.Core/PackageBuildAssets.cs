using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Transpose.Compiler;

/// <summary>
/// Puts into a site the files a referenced package's MSBuild targets would have copied there.
///
/// <para>A package can ship assets two ways. Embedding them as <c>tps.json</c> resources is the way
/// every build path honours, because the compiler itself extracts them. Several packages instead
/// ship a folder in the package root and an <c>AfterTargets="Build"</c> target in
/// <c>buildTransitive/</c> that copies it to <c>$(OutDir)/tps/assets/…</c> — Tesserae.Monaco,
/// Tesserae.Lauschwerk and Tesserae.ThreeD do, because their bundles must be loaded on demand under
/// the names they were authored with. That works under <c>dotnet build</c> and silently ships nothing
/// to anything that compiles without MSBuild: the in-process library behind a workspace's own
/// front-end build, which then answered a 404 for <c>assets/js/monaco/monaco.js</c> on every page that
/// opens a code editor.</para>
///
/// <para>This is not an MSBuild engine. It recognises exactly the shape those targets share — a target
/// run after <c>Build</c> whose <c>ItemGroup</c> globs a folder and whose <c>Copy</c> maps each item to
/// <c>&lt;dir&gt;%(RecursiveDir)%(Filename)%(Extension)</c> under the site's <c>tps</c> folder — and
/// evaluates the handful of property functions, conditions and globs that shape uses. A target that
/// uses anything else is skipped whole and reported, never half-applied: copying a guess into a site is
/// worse than copying nothing. Targets that write anywhere other than the site (installing a skill
/// into <c>.claude/</c>, say) are skipped for the same reason.</para>
/// </summary>
internal static class PackageBuildAssets
{
    /// <summary>What was copied for one package target, and what was left alone.</summary>
    public readonly record struct Outcome(IReadOnlyList<string> Copied, IReadOnlyList<string> Skipped);

    /// <summary>
    /// A project directory that does not exist, substituted for <c>$(MSBuildProjectDirectory)</c> so the
    /// destination a target computes can be told apart from any real path and turned back into a
    /// site-relative one.
    /// </summary>
    private static readonly string ProjectSentinel =
        Path.Combine(Path.GetTempPath(), "transpose-msbuild-project-sentinel") + Path.DirectorySeparatorChar;

    private static readonly Regex DestinationShape = new(
        @"^@\((?<item>[\w.]+)\s*->\s*'(?<dir>.*)%\(RecursiveDir\)%\(Filename\)%\(Extension\)'\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Runs every applicable package target for the packages the referenced DLLs belong to, writing
    /// into <paramref name="outputDir"/> and recording each file in <paramref name="written"/>.
    /// </summary>
    public static Outcome CopyInto(IEnumerable<string> referencePaths, string outputDir, ISet<string> written)
    {
        var copied  = new List<string>();
        var skipped = new List<string>();

        foreach (var packageDir in PackageRoots(referencePaths))
        {
            foreach (var targets in TargetsFiles(packageDir))
            {
                foreach (var plan in Plan(packageDir, targets, skipped))
                    Apply(plan, outputDir, written, copied);
            }
        }

        return new Outcome(copied, skipped);
    }

    // ------------------------------------------------------------------ finding the packages

    /// <summary>
    /// The package folder (the one holding the <c>.nuspec</c>) of every reference that lives in one.
    /// A reference that is a project's own output has no nuspec above it and is not a package.
    /// </summary>
    internal static IReadOnlyList<string> PackageRoots(IEnumerable<string> referencePaths)
    {
        var roots = new List<string>();
        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dll in referencePaths)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(dll));

            // lib/<tfm>/x.dll — a nuspec sits at most a few levels up, and a deeper search could walk
            // out of the package into an unrelated folder that happens to hold one.
            for (var depth = 0; depth < 4 && !string.IsNullOrEmpty(dir); depth++, dir = Path.GetDirectoryName(dir))
            {
                if (!Directory.EnumerateFiles(dir, "*.nuspec", SearchOption.TopDirectoryOnly).Any()) continue;

                if (seen.Add(dir)) roots.Add(dir);
                break;
            }
        }

        return roots;
    }

    private static IEnumerable<string> TargetsFiles(string packageDir)
    {
        foreach (var folder in new[] { "build", "buildTransitive" })
        {
            var dir = Path.Combine(packageDir, folder);
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.targets", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.Ordinal))
                yield return file;
        }
    }

    // ------------------------------------------------------------------ reading a targets file

    private sealed record CopyPlan(string Package, string Target, string SourceDir, string SiteRelativeDir, IReadOnlyList<(string full, string recursiveDir)> Files);

    private static IEnumerable<CopyPlan> Plan(string packageDir, string targetsFile, List<string> skipped)
    {
        XDocument doc;

        try { doc = XDocument.Load(targetsFile); }
        catch (Exception ex)
        {
            skipped.Add($"{Describe(packageDir, targetsFile)}: not readable as XML ({ex.Message})");
            return Array.Empty<CopyPlan>();
        }

        var root = doc.Root;
        if (root is null) return Array.Empty<CopyPlan>();

        var plans = new List<CopyPlan>();
        var eval  = new Evaluator();

        eval.Set("MSBuildThisFileDirectory", Path.GetDirectoryName(Path.GetFullPath(targetsFile))! + Path.DirectorySeparatorChar);
        eval.Set("MSBuildProjectDirectory",  ProjectSentinel.TrimEnd(Path.DirectorySeparatorChar));
        eval.Set("OutDir",                   "");
        eval.Set("DesignTimeBuild",          "");

        // Top-level properties are what a target's own paths are built from.
        foreach (var group in root.Elements().Where(e => e.Name.LocalName == "PropertyGroup"))
        {
            try { eval.ApplyPropertyGroup(group); }
            catch (Evaluator.UnsupportedException ex) { skipped.Add($"{Describe(packageDir, targetsFile)}: {ex.Message}"); return plans; }
        }

        foreach (var target in root.Elements().Where(e => e.Name.LocalName == "Target"))
        {
            var name = (string?)target.Attribute("Name") ?? "(unnamed)";

            // Only what MSBuild would run as part of a normal build. A target the project has to ask
            // for by name is not part of "the package's assets reach the output".
            var after = ((string?)target.Attribute("AfterTargets") ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!after.Contains("Build", StringComparer.OrdinalIgnoreCase)) continue;
            if (!target.Descendants().Any(e => e.Name.LocalName == "Copy")) continue;

            try
            {
                var plan = PlanTarget(packageDir, targetsFile, target, name, eval.Clone());
                if (plan is not null) plans.AddRange(plan);
            }
            catch (Evaluator.UnsupportedException ex)
            {
                skipped.Add($"{Describe(packageDir, targetsFile)}, target {name}: {ex.Message}");
            }
        }

        return plans;
    }

    private static IEnumerable<CopyPlan>? PlanTarget(string packageDir, string targetsFile, XElement target, string name, Evaluator eval)
    {
        if (!eval.Condition((string?)target.Attribute("Condition"))) return null;

        var items = new Dictionary<string, List<(string full, string recursiveDir)>>(StringComparer.OrdinalIgnoreCase);
        var plans = new List<CopyPlan>();

        foreach (var step in target.Elements())
        {
            switch (step.Name.LocalName)
            {
                case "PropertyGroup":
                    eval.ApplyPropertyGroup(step);
                    break;

                case "ItemGroup":
                    if (!eval.Condition((string?)step.Attribute("Condition"))) break;

                    foreach (var item in step.Elements())
                    {
                        if (!eval.Condition((string?)item.Attribute("Condition"))) continue;

                        var list = items.TryGetValue(item.Name.LocalName, out var existing) ? existing : items[item.Name.LocalName] = new();
                        list.AddRange(Glob.Expand(eval, (string?)item.Attribute("Include"), (string?)item.Attribute("Exclude")));
                    }
                    break;

                case "Error":
                    // The package's own "my payload is not where I expect it" guard. In MSBuild it fails
                    // the build; here the sensible reading is that there is nothing to copy.
                    if (eval.Condition((string?)step.Attribute("Condition"))) return null;
                    break;

                case "Copy":
                    if (!eval.Condition((string?)step.Attribute("Condition"))) break;
                    plans.Add(PlanCopy(packageDir, name, step, eval, items));
                    break;

                // Message, ReadLinesFromFile, MakeDir… change nothing a site is made of.
            }
        }

        return plans;
    }

    private static CopyPlan PlanCopy(string packageDir, string target, XElement copy, Evaluator eval, Dictionary<string, List<(string full, string recursiveDir)>> items)
    {
        var source = ((string?)copy.Attribute("SourceFiles"))?.Trim() ?? "";
        var dest   = ((string?)copy.Attribute("DestinationFiles"))?.Trim() ?? "";

        var shape = DestinationShape.Match(dest);
        if (!shape.Success)
            throw new Evaluator.UnsupportedException($"Copy destination '{dest}' is not <dir>%(RecursiveDir)%(Filename)%(Extension)");

        var item = shape.Groups["item"].Value;
        if (!string.Equals(source, "@(" + item + ")", StringComparison.OrdinalIgnoreCase))
            throw new Evaluator.UnsupportedException($"Copy source '{source}' is not the item its destination maps");

        var dir = eval.Expand(shape.Groups["dir"].Value);
        var site = Path.GetFullPath(Path.Combine(ProjectSentinel, "tps")) + Path.DirectorySeparatorChar;

        // Anything that does not land in the site's tps folder is not an asset of the site (it is a
        // skill install, a tool restore…) and is none of this compiler's business.
        if (!Path.GetFullPath(dir).StartsWith(site, StringComparison.Ordinal))
            throw new Evaluator.UnsupportedException($"Copy writes to '{dir}', which is not inside the site's tps folder");

        var files = items.TryGetValue(item, out var list) ? list : new();
        var relative = Path.GetRelativePath(site, Path.GetFullPath(dir)).Replace('\\', '/').TrimEnd('/');

        return new CopyPlan(PackageName(packageDir), target, packageDir, relative == "." ? "" : relative, files);
    }

    /// <summary>The package's id: NuGet lays a package out as <c>&lt;id&gt;/&lt;version&gt;</c>.</summary>
    private static string PackageName(string packageDir)
        => Path.GetFileName(Path.GetDirectoryName(packageDir.TrimEnd(Path.DirectorySeparatorChar))) is { Length: > 0 } id ? id : Path.GetFileName(packageDir);

    private static string Describe(string packageDir, string targetsFile)
        => $"{PackageName(packageDir)} ({Path.GetFileName(targetsFile)})";

    // ------------------------------------------------------------------ doing the copy

    private static void Apply(CopyPlan plan, string outputDir, ISet<string> written, List<string> copied)
    {
        var root = Path.GetFullPath(outputDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var count = 0;

        foreach (var (full, recursiveDir) in plan.Files)
        {
            var dest = Path.GetFullPath(Path.Combine(root, plan.SiteRelativeDir, recursiveDir, Path.GetFileName(full)));

            // A glob can only produce paths below its base, but a recursive directory is data and this
            // writes files: keep every one of them inside the site.
            if (!dest.StartsWith(root, StringComparison.Ordinal)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            // SkipUnchangedFiles, as the target asked for: a bundle that is already there and no older
            // than the package's is left alone (these are tens of megabytes, copied on every build).
            var source = new FileInfo(full);
            var target = new FileInfo(dest);
            if (!(target.Exists && target.Length == source.Length && target.LastWriteTimeUtc >= source.LastWriteTimeUtc))
                File.Copy(full, dest, overwrite: true);

            written.Add(dest);
            count++;
        }

        if (count > 0)
            copied.Add($"{plan.Package}: {count} file(s) to {(plan.SiteRelativeDir.Length == 0 ? "/" : plan.SiteRelativeDir + "/")}");
    }

    // ------------------------------------------------------------------ the MSBuild subset

    /// <summary>
    /// Properties, conditions and <c>$(…)</c> / <c>$([MSBuild]::NormalizeDirectory(…))</c> expansion —
    /// the part of MSBuild's expression language the package targets this supports are written in.
    /// Anything outside it throws <see cref="UnsupportedException"/>, which skips the target.
    /// </summary>
    private sealed class Evaluator
    {
        public sealed class UnsupportedException(string message) : Exception(message);

        private Dictionary<string, string> _props = new(StringComparer.OrdinalIgnoreCase);

        public void Set(string name, string value) => _props[name] = value;

        public Evaluator Clone() => new() { _props = new Dictionary<string, string>(_props, StringComparer.OrdinalIgnoreCase) };

        public void ApplyPropertyGroup(XElement group)
        {
            if (!Condition((string?)group.Attribute("Condition"))) return;

            foreach (var prop in group.Elements())
            {
                if (!Condition((string?)prop.Attribute("Condition"))) continue;
                _props[prop.Name.LocalName] = Expand(prop.Value);
            }
        }

        public bool Condition(string? condition)
        {
            if (string.IsNullOrWhiteSpace(condition)) return true;

            if (Regex.IsMatch(condition, @"\sor\s", RegexOptions.IgnoreCase))
                throw new UnsupportedException($"condition '{condition}' is outside what is evaluated");

            foreach (var term in Regex.Split(condition, @"\sand\s", RegexOptions.IgnoreCase))
            {
                if (!Term(term.Trim())) return false;
            }

            return true;
        }

        private bool Term(string term)
        {
            var negate = false;

            if (term.StartsWith('!')) { negate = true; term = term[1..].TrimStart(); }

            bool result;

            var exists = Regex.Match(term, @"^Exists\('(?<p>.*)'\)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (exists.Success)
            {
                var path = Expand(exists.Groups["p"].Value);
                result = Directory.Exists(path) || File.Exists(path);
            }
            else
            {
                var compare = Regex.Match(term, @"^'(?<l>.*)'\s*(?<op>==|!=)\s*'(?<r>.*)'$", RegexOptions.Singleline);
                if (!compare.Success) throw new UnsupportedException($"condition '{term}' is outside what is evaluated");

                var equal = string.Equals(Expand(compare.Groups["l"].Value), Expand(compare.Groups["r"].Value), StringComparison.OrdinalIgnoreCase);
                result = compare.Groups["op"].Value == "==" ? equal : !equal;
            }

            return negate ? !result : result;
        }

        public string Expand(string text)
        {
            var sb = new StringBuilder();

            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '$' || i + 1 >= text.Length || text[i + 1] != '(') { sb.Append(text[i]); continue; }

                var end = MatchingParen(text, i + 1);
                var body = text.Substring(i + 2, end - i - 2);

                if (body.StartsWith("[MSBuild]::NormalizeDirectory(", StringComparison.Ordinal))
                {
                    var args = SplitArguments(body["[MSBuild]::NormalizeDirectory(".Length..^1]).Select(a => Expand(Unquote(a))).ToArray();
                    if (args.Length == 0) throw new UnsupportedException("NormalizeDirectory with no arguments");

                    sb.Append(Path.GetFullPath(Path.Combine(args.Select(a => a.Replace('\\', '/')).ToArray())).TrimEnd(Path.DirectorySeparatorChar))
                      .Append(Path.DirectorySeparatorChar);
                }
                else if (body.StartsWith('['))
                {
                    throw new UnsupportedException($"property function '$({body})' is outside what is evaluated");
                }
                else
                {
                    sb.Append(_props.TryGetValue(body, out var value) ? value : "");
                }

                i = end;
            }

            return sb.ToString();
        }

        private static int MatchingParen(string text, int open)
        {
            var depth = 0;
            var quoted = false;

            for (var i = open; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\'') quoted = !quoted;
                if (quoted) continue;
                if (c == '(') depth++;
                else if (c == ')' && --depth == 0) return i;
            }

            throw new UnsupportedException($"unbalanced expression '{text}'");
        }

        private static IEnumerable<string> SplitArguments(string args)
        {
            var depth = 0;
            var quoted = false;
            var start = 0;

            for (var i = 0; i < args.Length; i++)
            {
                var c = args[i];
                if (c == '\'') quoted = !quoted;
                if (quoted) continue;
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0) { yield return args[start..i]; start = i + 1; }
            }

            yield return args[start..];
        }

        private static string Unquote(string argument)
        {
            var a = argument.Trim();
            return a.Length >= 2 && a[0] == '\'' && a[^1] == '\'' ? a[1..^1] : a;
        }
    }

    // ------------------------------------------------------------------ globbing

    private static class Glob
    {
        /// <summary>
        /// The files an item's <c>Include</c> (minus its <c>Exclude</c>) names, each with the directory
        /// the wildcard matched below its base — MSBuild's <c>%(RecursiveDir)</c>.
        /// </summary>
        public static IEnumerable<(string full, string recursiveDir)> Expand(Evaluator eval, string? include, string? exclude)
        {
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pattern in Patterns(eval, exclude))
                foreach (var (full, _) in Match(pattern)) excluded.Add(full);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pattern in Patterns(eval, include))
                foreach (var file in Match(pattern))
                    if (!excluded.Contains(file.full) && seen.Add(file.full)) yield return file;
        }

        private static IEnumerable<string> Patterns(Evaluator eval, string? list)
            => string.IsNullOrWhiteSpace(list)
                ? Array.Empty<string>()
                : list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => eval.Expand(p).Replace('\\', '/'));

        private static IEnumerable<(string full, string recursiveDir)> Match(string pattern)
        {
            var segments = pattern.Split('/');
            var firstWild = Array.FindIndex(segments, s => s.Contains('*') || s.Contains('?'));

            if (firstWild < 0)
            {
                if (File.Exists(pattern)) yield return (Path.GetFullPath(pattern), "");
                yield break;
            }

            var baseDir = string.Join('/', segments.Take(firstWild));
            if (!Directory.Exists(baseDir)) yield break;

            var rest = "^" + Regex.Escape(string.Join('/', segments.Skip(firstWild)))
                .Replace(@"\*\*/", "(?:.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]") + "$";
            var rx = new Regex(rest, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            foreach (var file in Directory.EnumerateFiles(baseDir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                var rel = Path.GetRelativePath(baseDir, file).Replace('\\', '/');
                if (!rx.IsMatch(rel)) continue;

                var slash = rel.LastIndexOf('/');
                yield return (Path.GetFullPath(file), slash < 0 ? "" : rel[..slash]);
            }
        }
    }
}
