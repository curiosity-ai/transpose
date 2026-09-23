using System.Xml.Linq;

namespace Transpose.Compiler;

/// <summary>One place packages can be fetched from: a folder of <c>.nupkg</c> files, or a V3 feed.</summary>
internal sealed record PackageSource(string Name, string Value)
{
    /// <summary>A source is a feed when it is written as an http(s) URL; anything else is a folder on
    /// disk. That is NuGet's own rule, and it is what makes a flat folder of <c>.nupkg</c> files a
    /// source with nothing else to declare.</summary>
    public bool IsHttp => Value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                       || Value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Name.Length > 0 ? $"{Name} ({Value})" : Value;
}

/// <summary>
/// The <c>nuget.config</c> chain a project sits under, reduced to the two things a restore needs: the
/// sources to fetch from and the global-packages folder to install into.
///
/// <para>NuGet reads its configuration from the machine-wide file, then the user-level one, then every
/// <c>nuget.config</c> from the filesystem root down to the project's own directory — nearer files
/// winning, and a <c>&lt;clear /&gt;</c> discarding everything accumulated so far. That ordering is
/// what lets a folder of packages be declared for a whole checkout by a config written *beside* it, in
/// a file no project in the checkout owns, which is exactly how a workspace hands its shipped packages
/// to a front-end it did not write.</para>
///
/// <para>Deliberately not read: credentials, package-source mapping and client certificates. A feed
/// that needs authenticating is out of scope for <c>tps restore</c> — it restores what the compiler
/// itself binds against, which is public packages and whatever folder the caller points it at — and
/// silently ignoring a credential would be worse than saying so.</para>
/// </summary>
internal sealed class NuGetSettings
{
    private NuGetSettings(List<PackageSource> sources, string? globalPackagesFolder, List<string> files, bool declaresSources)
    {
        Sources              = sources;
        GlobalPackagesFolder = globalPackagesFolder;
        Files                = files;
        DeclaresSources      = declaresSources;
    }

    /// <summary>The enabled sources, in the order they should be consulted.</summary>
    public IReadOnlyList<PackageSource> Sources { get; }

    /// <summary>The <c>globalPackagesFolder</c> the configuration names, or null when none does.</summary>
    public string? GlobalPackagesFolder { get; }

    /// <summary>The config files that were actually read, nearest last — reported so a restore that
    /// fetched from an unexpected place can be traced back to the file that said so.</summary>
    public IReadOnlyList<string> Files { get; }

    /// <summary>
    /// Whether any config said anything at all about sources. It is what tells "nobody has configured
    /// NuGet on this machine" — where falling back to nuget.org is the only useful thing to do, and
    /// the only reason <c>dotnet restore</c> works out of the box, its machine-level config declaring
    /// it — from "the configuration cleared or disabled every source", which is a decision to respect
    /// rather than a gap to fill.
    /// </summary>
    public bool DeclaresSources { get; }

    /// <summary>nuget.org, the source NuGet itself falls back to when nothing is configured.</summary>
    public const string DefaultSourceUrl = "https://api.nuget.org/v3/index.json";

    public static NuGetSettings Load(string startDirectory)
    {
        var sources  = new List<PackageSource>();
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files    = new List<string>();
        var declared = false;

        string? globalPackages = null;

        foreach (var file in ConfigFiles(startDirectory))
        {
            XDocument doc;
            try { doc = XDocument.Load(file); }
            catch { continue; } // a malformed config is not this tool's to report; NuGet's own reader owns that message

            files.Add(file);

            var dir  = Path.GetDirectoryName(Path.GetFullPath(file))!;
            var root = doc.Root;
            if (root is null) continue;

            foreach (var section in root.Elements())
            {
                switch (section.Name.LocalName)
                {
                    case "packageSources":
                        declared = true;
                        foreach (var e in section.Elements())
                        {
                            if (e.Name.LocalName == "clear") { sources.Clear(); continue; }
                            if (e.Name.LocalName is not ("add" or "remove")) continue;

                            var key   = e.Attribute("key")?.Value?.Trim();
                            var value = e.Attribute("value")?.Value?.Trim();
                            if (string.IsNullOrEmpty(key)) continue;

                            sources.RemoveAll(s => string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase));
                            if (e.Name.LocalName == "remove" || string.IsNullOrEmpty(value)) continue;

                            sources.Add(new PackageSource(key!, Absolute(value!, dir)));
                        }
                        break;

                    case "disabledPackageSources":
                        foreach (var e in section.Elements())
                        {
                            if (e.Name.LocalName == "clear") { disabled.Clear(); continue; }
                            var key = e.Attribute("key")?.Value?.Trim();
                            if (string.IsNullOrEmpty(key)) continue;

                            // NuGet reads the value as a bool, so a later config re-enables a source
                            // its parent disabled by writing the same key with "false".
                            if (string.Equals(e.Attribute("value")?.Value?.Trim(), "false", StringComparison.OrdinalIgnoreCase))
                                disabled.Remove(key!);
                            else
                                disabled.Add(key!);
                        }
                        break;

                    case "config":
                        foreach (var e in section.Elements("add"))
                            if (string.Equals(e.Attribute("key")?.Value?.Trim(), "globalPackagesFolder", StringComparison.OrdinalIgnoreCase))
                            {
                                var value = e.Attribute("value")?.Value?.Trim();
                                if (!string.IsNullOrEmpty(value)) globalPackages = Absolute(value!, dir);
                            }
                        break;
                }
            }
        }

        sources.RemoveAll(s => disabled.Contains(s.Name));

        return new NuGetSettings(sources, globalPackages, files, declared);

        // A source written as a relative path means "relative to the config that declared it" — which
        // is the whole point of declaring one in a config beside a checkout rather than inside it.
        static string Absolute(string value, string configDir)
        {
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
             || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return value;

            return Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(configDir, value));
        }
    }

    /// <summary>
    /// The config files that apply to <paramref name="startDirectory"/>, in the order NuGet applies
    /// them: the user-level file first, then every <c>nuget.config</c> from the filesystem root down to
    /// the directory itself, so the nearest file has the last word.
    /// </summary>
    private static IEnumerable<string> ConfigFiles(string startDirectory)
    {
        foreach (var user in UserLevelConfigs())
            if (File.Exists(user)) yield return user;

        var chain = new List<string>();

        for (var dir = new DirectoryInfo(Path.GetFullPath(startDirectory)); dir is not null; dir = dir.Parent)
        {
            // A case-insensitive match because the file is spelled NuGet.Config on Windows, nuget.config
            // in most repositories, and a Linux filesystem keeps the two apart.
            var found = SafeEnumerate(dir).FirstOrDefault(f =>
                string.Equals(Path.GetFileName(f), "nuget.config", StringComparison.OrdinalIgnoreCase));

            if (found is not null) chain.Add(found);
        }

        chain.Reverse(); // root first, the project's own directory last
        foreach (var file in chain) yield return file;

        static IEnumerable<string> SafeEnumerate(DirectoryInfo dir)
        {
            try { return dir.Exists ? dir.GetFiles("*.config").Select(f => f.FullName) : Enumerable.Empty<string>(); }
            catch { return Enumerable.Empty<string>(); }
        }
    }

    private static IEnumerable<string> UserLevelConfigs()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (appData.Length > 0) yield return Path.Combine(appData, "NuGet", "NuGet.Config");
            yield break;
        }

        // NuGet on Unix looks under $XDG_CONFIG_HOME (default ~/.config) — and, for caches written by
        // older clients, under ~/.nuget/NuGet.
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrEmpty(xdg)) yield return Path.Combine(xdg!, "NuGet", "NuGet.Config");
        else if (home.Length > 0)       yield return Path.Combine(home, ".config", "NuGet", "NuGet.Config");

        if (home.Length > 0) yield return Path.Combine(home, ".nuget", "NuGet", "NuGet.Config");
    }
}
