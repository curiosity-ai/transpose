using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace Transpose.Compiler;

/// <summary>What one restore is asked to do. Mirrors the <c>tps restore</c> command line one-to-one.</summary>
internal sealed record RestoreOptions
{
    /// <summary>The project to restore, with every project it transitively references.</summary>
    public required string CsprojPath { get; init; }

    /// <summary>Where packages are installed. Null takes <c>NUGET_PACKAGES</c>, then the configured
    /// <c>globalPackagesFolder</c>, then <c>~/.nuget/packages</c> — NuGet's own order.</summary>
    public string? PackagesFolder { get; init; }

    /// <summary>Sources to consult *before* the configured ones (<c>--source</c>). A folder of
    /// <c>.nupkg</c> files or a V3 feed URL.</summary>
    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();

    /// <summary>Consult only <see cref="Sources"/>, ignoring the nuget.config chain
    /// (<c>--no-configured-sources</c>). What an offline build wants: a source it cannot reach is a
    /// timeout per package rather than an error.</summary>
    public bool IgnoreConfiguredSources { get; init; }

    /// <summary>Re-download and re-extract a package that is already installed.</summary>
    public bool Force { get; init; }

    /// <summary>How many packages are fetched at once.</summary>
    public int MaxParallelDownloads { get; init; } = 8;

    /// <summary>How long one package download may take.</summary>
    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>One package the restore accounted for.</summary>
internal sealed record RestoredPackage(string Id, string Version, string Folder, bool Downloaded, string? Source);

internal sealed class RestoreOutcome
{
    public bool Success => Errors.Count == 0;
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<RestoredPackage> Packages { get; } = new();
    public string PackagesFolder { get; set; } = "";
    public int Downloaded => Packages.Count(p => p.Downloaded);
    public int ExitCode => Success ? 0 : 1;
}

/// <summary>
/// Installs the packages a project binds against into the NuGet global-packages folder — what
/// <c>dotnet restore</c> does, for the part of it <c>tps</c> actually consumes.
///
/// <para><see cref="ProjectResolver"/> resolves a <c>&lt;PackageReference&gt;</c> by reading the
/// package's folder out of that cache, so a project whose packages were never restored does not fail
/// with "restore first" — it compiles against nothing and reports the missing types as ordinary C#
/// errors. Until now the fix was to run <c>dotnet restore</c> first, which makes the .NET SDK a
/// prerequisite of compiling with a tool that otherwise needs nothing but itself: a container that
/// ships <c>tps</c> (or hosts <c>Transpose.Compiler.Library</c>) had to ship the SDK as well, for one
/// command.</para>
///
/// <para><b>This is not a general-purpose NuGet client.</b> It resolves the graph exactly the way
/// <see cref="ProjectResolver"/> reads it back — a version the project declares wins over one reached
/// through a dependency, and a dependency range takes its lowest satisfying version — downloads each
/// package from a folder source or a V3 feed, and lays it out in the cache the way NuGet lays it out,
/// so the folder it produces is one a later <c>dotnet restore</c> accepts as already installed.
/// Lock files, package-source mapping, authenticated feeds, floating versions, signature verification
/// and <c>project.assets.json</c> are all out of scope — nothing in the Transpose toolchain reads
/// them, and a restore that only has to feed a compiler does not need to model what MSBuild's does.
/// A project whose SDK (<c>Sdk="Transpose.Build.Target/…"</c>) is not already installed still needs
/// <c>dotnet</c> to build it *with MSBuild*; it does not need one to be compiled by <c>tps</c>.</para>
///
/// <para>Measured against a <c>dotnet restore</c> of the same starter front-end (eight direct
/// references, thirteen packages): the same thirteen, at the same versions, laid out the same way,
/// plus the SDK package only MSBuild reads. Including the four versions of one package the graph
/// reaches through different dependencies — which look redundant and are not: <see cref="ProjectResolver"/>
/// walks every version it is pointed at and follows each one's nuspec, so a version left uninstalled
/// is a dependency chain that stops being followed.</para>
/// </summary>
internal static class PackageRestore
{
    /// <summary>Marks a package folder as fully installed — the same file NuGet writes, and the same
    /// one it checks for, so the two agree about what is already there.</summary>
    private const string InstalledMarker = ".nupkg.metadata";

    public static async Task<RestoreOutcome> RunAsync(RestoreOptions options, BuildLog log, CancellationToken cancellationToken = default)
    {
        var outcome = new RestoreOutcome();

        var csproj     = Path.GetFullPath(options.CsprojPath);
        var projectDir = Path.GetDirectoryName(csproj)!;

        var settings = options.IgnoreConfiguredSources ? null : NuGetSettings.Load(projectDir);
        var packages = PackagesFolder(options, settings);

        outcome.PackagesFolder = packages;
        Directory.CreateDirectory(packages);

        var sources = Sources(options, settings);
        if (sources.Count == 0)
        {
            outcome.Errors.Add("No NuGet source to restore from: the configuration declares none and none was passed.");
            MsBuildDiagnostic.WriteError(MsBuildDiagnostic.CodeRestoreNoSource, outcome.Errors[^1]);
            return outcome;
        }

        log.Info($"Restoring into {packages}");
        log.Info($"  sources: {string.Join(", ", sources.Select(s => s.Value))}");

        using var http = HttpClientFor(options);
        var installer  = new Installer(packages, sources, http, options, outcome, log);

        // One walk per project in the closure, each with its own declared set — the same shape
        // ProjectResolver's reference resolution has, and the reason it matters is the interaction
        // between the two rules: a package a project declares itself suppresses every transitive
        // demand for it *within that project*, but a sibling project that does not declare it still
        // resolves the transitive version, and the build binds the higher of the two. Restoring from
        // one union of the declared sets would leave that second version uninstalled.
        foreach (var project in ProjectClosure(csproj))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ProjectXml.TryLoad(project) is not { } doc) continue;

            var declared        = DeclaredPackages(doc, outcome, project);
            var targetFramework = TargetFrameworkOf(doc);

            await installer.WalkAsync(declared, targetFramework, cancellationToken).ConfigureAwait(false);
        }

        foreach (var warning in outcome.Warnings) MsBuildDiagnostic.WriteWarning(MsBuildDiagnostic.CodeRestoreIncomplete, warning);
        foreach (var error in outcome.Errors) MsBuildDiagnostic.WriteError(MsBuildDiagnostic.CodeRestoreFailed, error);

        if (outcome.Success)
            log.Info($"Restored {outcome.Packages.Count} package(s) ({outcome.Downloaded} downloaded)");

        return outcome;
    }

    // ---- the graph walk -----------------------------------------------------

    /// <summary>
    /// Walks one project's package graph, installing what it reaches. Holds the cross-project memo of
    /// what is already on disk, so a package reached from two projects is inspected once.
    /// </summary>
    private sealed class Installer
    {
        private readonly string                  _packages;
        private readonly IReadOnlyList<PackageSource> _sources;
        private readonly HttpClient              _http;
        private readonly RestoreOptions          _options;
        private readonly RestoreOutcome          _outcome;
        private readonly BuildLog                _log;
        private readonly ConcurrentDictionary<string, Task<string?>> _installs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PackageBaseAddress>      _feeds    = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _feedGate = new(1, 1);

        public Installer(string packages, IReadOnlyList<PackageSource> sources, HttpClient http,
                         RestoreOptions options, RestoreOutcome outcome, BuildLog log)
        {
            _packages = packages;
            _sources  = sources;
            _http     = http;
            _options  = options;
            _outcome  = outcome;
            _log      = log;
        }

        public async Task WalkAsync(Dictionary<string, string> declared, string targetFramework, CancellationToken cancellationToken)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var wave    = declared.Select(kv => (Id: kv.Key, Version: kv.Value, Declared: true)).ToList();

            // Breadth-first, a wave at a time: a package's dependencies are only known once its nuspec
            // is on disk, but everything within one wave can be fetched at once — which is most of the
            // wall-clock time when the source is a feed rather than a folder.
            while (wave.Count > 0)
            {
                var pending = new List<(string Id, string Version, bool Declared)>();

                foreach (var item in wave)
                {
                    // A transitive demand for a package the project declares itself is dropped
                    // outright: the declared version supersedes it, and installing the other one would
                    // put a version in the cache that nothing ever binds.
                    if (!item.Declared && declared.ContainsKey(item.Id)) continue;
                    if (!visited.Add(item.Id + "@" + item.Version)) continue;

                    pending.Add(item);
                }

                if (pending.Count == 0) break;

                var folders = await Task.WhenAll(pending.Select(p => EnsureAsync(p.Id, p.Version, p.Declared, cancellationToken)))
                                        .ConfigureAwait(false);

                var next = new List<(string, string, bool)>();

                for (var i = 0; i < pending.Count; i++)
                {
                    if (folders[i] is not { } folder) continue;

                    foreach (var (id, version) in Dependencies(folder, pending[i].Id, targetFramework))
                        next.Add((id, version, false));
                }

                wave = next;
            }
        }

        /// <summary>Installs one package if it is not there already, answering with its folder.</summary>
        private Task<string?> EnsureAsync(string id, string version, bool declared, CancellationToken cancellationToken)
        {
            var normalized = NuGetVersions.Normalize(version);

            // Keyed by the *installed* identity rather than the requested spelling, so "1.0" and
            // "1.0.0" are one install rather than two racing for the same folder.
            return _installs.GetOrAdd(id + "/" + normalized, _ => InstallAsync(id, version, normalized, declared, cancellationToken));
        }

        private async Task<string?> InstallAsync(string id, string requested, string normalized, bool declared, CancellationToken cancellationToken)
        {
            if (NuGetVersions.IsFloating(requested))
            {
                // Nothing downstream resolves one: ProjectResolver looks a package up by the exact
                // version the project writes down, so installing *some* version would leave the build
                // unable to find it — a failure far from its cause.
                Report(declared, $"{id} {requested}: a floating version cannot be restored — write the exact version the project should bind against.");
                return null;
            }

            var folder = Path.Combine(_packages, id.ToLowerInvariant(), normalized);

            if (!_options.Force && IsInstalled(folder))
            {
                lock (_outcome) _outcome.Packages.Add(new RestoredPackage(id, normalized, folder, false, null));
                return folder;
            }

            // The spelling the project used may differ from the normalized one an install writes, and
            // a cache filled by an older client can carry either.
            var asWritten = Path.Combine(_packages, id.ToLowerInvariant(), requested);
            if (!_options.Force && !string.Equals(asWritten, folder, StringComparison.Ordinal) && IsInstalled(asWritten))
            {
                lock (_outcome) _outcome.Packages.Add(new RestoredPackage(id, requested, asWritten, false, null));
                return asWritten;
            }

            var (bytes, source) = await FetchAsync(id, normalized, requested, cancellationToken).ConfigureAwait(false);

            if (bytes is null)
            {
                Report(declared, $"{id} {requested} was not found in any source ({string.Join(", ", _sources.Select(s => s.Value))}).");
                return null;
            }

            try
            {
                Extract(bytes, id, normalized, folder, source);
            }
            catch (Exception ex)
            {
                Report(declared, $"{id} {requested} could not be installed into {folder}: {ex.Message}");
                return null;
            }

            _log.Info($"  installed {id} {normalized}");
            lock (_outcome) _outcome.Packages.Add(new RestoredPackage(id, normalized, folder, true, source));

            return folder;
        }

        /// <summary>
        /// A package the project declares is an error — the build cannot bind what it names. One
        /// reached through a dependency is a warning: this walk chooses a dependency group the way
        /// NuGet does but has no lock file to check itself against, and a package that is genuinely
        /// needed and absent surfaces as an ordinary C# error naming the type, which says more.
        /// </summary>
        private void Report(bool declared, string message)
        {
            lock (_outcome)
            {
                if (declared) _outcome.Errors.Add(message);
                else _outcome.Warnings.Add(message);
            }
        }

        // ---- fetching -------------------------------------------------------

        private async Task<(byte[]? bytes, string? source)> FetchAsync(string id, string normalized, string requested, CancellationToken cancellationToken)
        {
            foreach (var source in _sources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var bytes = source.IsHttp
                        ? await FromFeedAsync(source, id, normalized, cancellationToken).ConfigureAwait(false)
                        : FromFolder(source, id, normalized, requested);

                    if (bytes is not null) return (bytes, source.Value);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // One unreachable source must not fail a restore the next one can serve — which is
                    // the offline case exactly: nuget.org times out, the shipped folder answers.
                    lock (_outcome) _outcome.Warnings.Add($"{source}: {ex.Message}");
                }
            }

            return (null, null);
        }

        /// <summary>
        /// A folder source, read the two ways a folder can be laid out: flat (every <c>.nupkg</c> in
        /// one directory, which is what a <c>dotnet pack -o</c> or an offline bundle produces) and
        /// versioned (<c>&lt;id&gt;/&lt;version&gt;/&lt;id&gt;.&lt;version&gt;.nupkg</c>).
        /// </summary>
        private static byte[]? FromFolder(PackageSource source, string id, string normalized, string requested)
        {
            if (!Directory.Exists(source.Value)) return null;

            foreach (var version in Versions(normalized, requested))
            {
                var flat = Path.Combine(source.Value, $"{id}.{version}.nupkg");
                if (File.Exists(flat)) return File.ReadAllBytes(flat);

                var nested = Path.Combine(source.Value, id, version, $"{id}.{version}.nupkg");
                if (File.Exists(nested)) return File.ReadAllBytes(nested);
            }

            // A file system that tells case apart (every Linux container this runs in) will have missed
            // a package written with the casing of its id rather than the lower-cased one, so the
            // folder is searched once more by name.
            foreach (var version in Versions(normalized, requested))
            {
                var wanted = $"{id}.{version}.nupkg";
                var match  = Directory.EnumerateFiles(source.Value, "*.nupkg", SearchOption.TopDirectoryOnly)
                                      .FirstOrDefault(f => string.Equals(Path.GetFileName(f), wanted, StringComparison.OrdinalIgnoreCase));

                if (match is not null) return File.ReadAllBytes(match);
            }

            return null;

            static IEnumerable<string> Versions(string normalized, string requested)
            {
                yield return normalized;
                if (!string.Equals(normalized, requested, StringComparison.OrdinalIgnoreCase)) yield return requested;
            }
        }

        /// <summary>
        /// A V3 feed: the service index names a <c>PackageBaseAddress/3.0.0</c> resource, under which
        /// every package is at a path derived from its id and version — no search API, no
        /// registration, nothing to page through.
        /// </summary>
        private async Task<byte[]?> FromFeedAsync(PackageSource source, string id, string normalized, CancellationToken cancellationToken)
        {
            var feed = await BaseAddressAsync(source, cancellationToken).ConfigureAwait(false);
            if (feed.Url is null) return null;

            var lower = id.ToLowerInvariant();
            var url   = $"{feed.Url}{lower}/{normalized}/{lower}.{normalized}.nupkg";

            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<PackageBaseAddress> BaseAddressAsync(PackageSource source, CancellationToken cancellationToken)
        {
            await _feedGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_feeds.TryGetValue(source.Value, out var cached)) return cached;

                var resolved = new PackageBaseAddress(await ReadServiceIndexAsync(source, cancellationToken).ConfigureAwait(false));
                _feeds[source.Value] = resolved;
                return resolved;
            }
            finally
            {
                _feedGate.Release();
            }
        }

        private async Task<string?> ReadServiceIndexAsync(PackageSource source, CancellationToken cancellationToken)
        {
            // A V3 source is written as its index.json. A V2 feed (…/api/v2) has a different protocol
            // that nothing in this toolchain needs, so it is named as unsupported rather than guessed at.
            if (!source.Value.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("only V3 sources (an index.json URL) and folders are supported");

            using var stream = await _http.GetStreamAsync(source.Value, cancellationToken).ConfigureAwait(false);
            using var index  = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!index.RootElement.TryGetProperty("resources", out var resources)) return null;

            foreach (var resource in resources.EnumerateArray())
            {
                var type = resource.TryGetProperty("@type", out var t) ? t.GetString() : null;
                if (type is null || !type.StartsWith("PackageBaseAddress/3.0.0", StringComparison.OrdinalIgnoreCase)) continue;

                var url = resource.TryGetProperty("@id", out var at) ? at.GetString() : null;
                if (string.IsNullOrEmpty(url)) continue;

                return url!.EndsWith('/') ? url : url + "/";
            }

            return null;
        }

        private readonly record struct PackageBaseAddress(string? Url);
    }

    // ---- installing ---------------------------------------------------------

    private static bool IsInstalled(string folder)
        => Directory.Exists(folder)
        && (File.Exists(Path.Combine(folder, InstalledMarker)) || Directory.EnumerateFiles(folder, "*.nuspec").Any());

    /// <summary>
    /// Lays a package out the way NuGet lays it out: the contents extracted, the nuspec and the
    /// <c>.nupkg</c> under their lower-cased names, the content hash beside them and the
    /// <c>.nupkg.metadata</c> marker last. That layout is not decoration — <see cref="ProjectResolver"/>
    /// reads <c>&lt;id&gt;.nuspec</c> by its lower-cased name, and a <c>dotnet restore</c> run later
    /// over the same folder skips a package only when the marker is there.
    /// </summary>
    /// <remarks>
    /// One value differs from what NuGet writes: the marker's <c>contentHash</c>. NuGet's is the
    /// *signed content* hash — the package's contents hashed past its signature — while the
    /// <c>.nupkg.sha512</c> beside it (which this writes byte-identically) is the whole file's. The
    /// content hash is read back only to validate a <c>packages.lock.json</c>, which nothing in this
    /// toolchain writes; computing it would mean implementing package-signature verification for a
    /// field no restore here consults.
    /// </remarks>
    private static void Extract(byte[] nupkg, string id, string version, string folder, string? source)
    {
        var lower = id.ToLowerInvariant();
        var hash  = Convert.ToBase64String(SHA512.HashData(nupkg));

        // Extracted somewhere else and moved into place, so a folder that exists is a folder that is
        // complete — a build racing this restore (or a cancelled one) must never see half a package.
        var staging = Path.Combine(Path.GetDirectoryName(folder)!, "." + Path.GetFileName(folder) + "." + Guid.NewGuid().ToString("N")[..8]);

        Directory.CreateDirectory(staging);

        try
        {
            using (var archive = new ZipArchive(new MemoryStream(nupkg), ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith('/')) continue;                 // a directory entry carries nothing

                    var name = Uri.UnescapeDataString(entry.FullName).Replace('\\', '/');
                    if (IsPackagingArtefact(name)) continue;

                    // A nuspec at the root is written under the id's lower-cased name, which is where
                    // every reader of this cache looks for it.
                    if (!name.Contains('/') && name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                        name = lower + ".nuspec";

                    var target = SafeCombine(staging, name);
                    if (target is null) continue;                               // an entry escaping the folder is not extracted

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
            }

            File.WriteAllBytes(Path.Combine(staging, $"{lower}.{version}.nupkg"), nupkg);
            File.WriteAllText(Path.Combine(staging, $"{lower}.{version}.nupkg.sha512"), hash);
            File.WriteAllText(Path.Combine(staging, InstalledMarker),
                JsonSerializer.Serialize(new { version = 2, contentHash = hash, source },
                                         new JsonSerializerOptions { WriteIndented = true }));

            Directory.CreateDirectory(Path.GetDirectoryName(folder)!);

            try
            {
                Directory.Move(staging, folder);
            }
            catch (IOException) when (Directory.Exists(folder))
            {
                // Something else installed it while this was extracting. Its copy is as good as this one.
            }
        }
        finally
        {
            if (Directory.Exists(staging)) TryDelete(staging);
        }
    }

    /// <summary>The OPC bookkeeping a <c>.nupkg</c> carries as a zip package rather than as content —
    /// NuGet leaves all of it out of an installed folder, and a <c>_rels</c> directory appearing in a
    /// package's own layout would be visible to anything that globs it.</summary>
    private static bool IsPackagingArtefact(string name)
        => name.StartsWith("_rels/", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("package/", StringComparison.OrdinalIgnoreCase)
        || name.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolves an archive entry under <paramref name="root"/>, or null when it would land
    /// outside it — a zip is untrusted input, and an entry named <c>../../…</c> writes wherever it
    /// likes otherwise.</summary>
    private static string? SafeCombine(string root, string entry)
    {
        var full = Path.GetFullPath(Path.Combine(root, entry));
        var bound = Path.GetFullPath(root) + Path.DirectorySeparatorChar;

        return full.StartsWith(bound, StringComparison.Ordinal) ? full : null;
    }

    private static void TryDelete(string directory)
    {
        try { Directory.Delete(directory, recursive: true); } catch { /* a leftover staging folder is not worth failing a restore */ }
    }

    // ---- reading the project ------------------------------------------------

    /// <summary>The root project and every project it transitively references, each once.</summary>
    private static IEnumerable<string> ProjectClosure(string rootCsproj)
    {
        var order   = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Visit(Path.GetFullPath(rootCsproj));

        return order;

        void Visit(string csproj)
        {
            if (!visited.Add(csproj) || !File.Exists(csproj)) return;

            order.Add(csproj);

            if (ProjectXml.TryLoad(csproj) is not { } doc) return;

            var dir = Path.GetDirectoryName(csproj)!;

            foreach (var (element, _) in doc.Elements("ProjectReference"))
            {
                var include = element.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include)) continue;

                Visit(Path.GetFullPath(Path.Combine(dir, include!.Replace('\\', '/'))));
            }
        }
    }

    /// <summary>
    /// The packages a project declares itself, highest version per id — the same reading
    /// <see cref="ProjectResolver"/> gives a project's <c>&lt;PackageReference&gt;</c> items.
    /// </summary>
    private static Dictionary<string, string> DeclaredPackages(ProjectXml doc, RestoreOutcome outcome, string project)
    {
        var declared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (element, _) in doc.Elements("PackageReference"))
        {
            var id      = element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value;
            var version = element.Attribute("Version")?.Value ?? element.Element(element.Name.Namespace + "Version")?.Value;

            if (string.IsNullOrWhiteSpace(id)) continue;

            if (string.IsNullOrWhiteSpace(version))
            {
                // Central package management puts the version in Directory.Packages.props, which this
                // resolver does not evaluate — and the reference resolver cannot bind it either, so it
                // is said here rather than surfacing as a missing type.
                outcome.Warnings.Add($"{Path.GetFileName(project)}: PackageReference '{id}' declares no Version, so it cannot be restored.");
                continue;
            }

            if (!declared.TryGetValue(id!, out var already) || CompareVersions(version!, already) > 0)
                declared[id!] = version!.Trim();
        }

        return declared;
    }

    private static string TargetFrameworkOf(ProjectXml doc)
    {
        // The Transpose SDK overrides a project's own TargetFramework to netstandard2.0, so that is
        // what the build really binds against whatever the csproj says.
        var sdk = doc.SdkName ?? "";
        if (sdk.StartsWith("Transpose.Build.Target", StringComparison.OrdinalIgnoreCase)) return "netstandard2.0";

        return doc.Property("TargetFramework")
            ?? doc.Property("TargetFrameworks")?.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
            ?? "netstandard2.0";
    }

    /// <summary>
    /// The dependencies an installed package declares for <paramref name="targetFramework"/>: the one
    /// <c>&lt;group&gt;</c> NuGet would pick, or the ungrouped list of a nuspec old enough not to have
    /// groups.
    /// </summary>
    private static IEnumerable<(string id, string version)> Dependencies(string folder, string id, string targetFramework)
    {
        var nuspec = Path.Combine(folder, id.ToLowerInvariant() + ".nuspec");

        if (!File.Exists(nuspec))
            nuspec = Directory.EnumerateFiles(folder, "*.nuspec", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? "";

        if (nuspec.Length == 0 || !File.Exists(nuspec)) yield break;

        XDocument doc;
        try { doc = XDocument.Load(nuspec); }
        catch { yield break; }

        var dependencies = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "dependencies");
        if (dependencies is null) yield break;

        var groups = dependencies.Elements().Where(e => e.Name.LocalName == "group").ToList();

        var chosen = groups.Count == 0
            ? dependencies.Elements().Where(e => e.Name.LocalName == "dependency")
            : PickGroup(groups, targetFramework);

        foreach (var dependency in chosen)
        {
            var depId      = dependency.Attribute("id")?.Value?.Trim();
            var depVersion = NuGetVersions.LowestSatisfying(dependency.Attribute("version")?.Value);

            if (!string.IsNullOrWhiteSpace(depId) && depVersion is not null) yield return (depId!, depVersion);
        }
    }

    private static IEnumerable<XElement> PickGroup(List<XElement> groups, string targetFramework)
    {
        var monikers = groups.Select(g => g.Attribute("targetFramework")?.Value).ToList();
        var best     = NuGetFrameworks.BestGroup(targetFramework, monikers);

        return best < 0
            ? Enumerable.Empty<XElement>()
            : groups[best].Elements().Where(e => e.Name.LocalName == "dependency");
    }

    // ---- configuration ------------------------------------------------------

    /// <summary>NuGet's own precedence for where packages are installed.</summary>
    private static string PackagesFolder(RestoreOptions options, NuGetSettings? settings)
    {
        if (!string.IsNullOrWhiteSpace(options.PackagesFolder)) return Path.GetFullPath(options.PackagesFolder!);

        var env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(env)) return Path.GetFullPath(env!);

        if (settings?.GlobalPackagesFolder is { Length: > 0 } configured) return configured;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
    }

    /// <summary>
    /// The sources to consult, in order: the ones the caller passed first, then the configured ones.
    /// The caller's come first on purpose — a workspace that ships the packages it was built from
    /// should answer out of that folder rather than reach nuget.org for a version it already has.
    /// </summary>
    private static List<PackageSource> Sources(RestoreOptions options, NuGetSettings? settings)
    {
        var sources = new List<PackageSource>();

        foreach (var source in options.Sources)
        {
            if (string.IsNullOrWhiteSpace(source)) continue;

            var value = source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                     || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? source
                : Path.GetFullPath(source);

            if (!sources.Any(s => string.Equals(s.Value, value, StringComparison.OrdinalIgnoreCase)))
                sources.Add(new PackageSource("", value));
        }

        if (settings is not null)
            foreach (var source in settings.Sources)
                if (!sources.Any(s => string.Equals(s.Value, source.Value, StringComparison.OrdinalIgnoreCase)))
                    sources.Add(source);

        // A machine where nobody has configured NuGet still has a meaning: its default source. A
        // configuration that cleared or disabled every source does not — that is a decision, and
        // answering it with nuget.org would be reaching the network precisely where someone said not to.
        if (sources.Count == 0 && settings is { DeclaresSources: false })
            sources.Add(new PackageSource("nuget.org", NuGetSettings.DefaultSourceUrl));

        return sources;
    }

    private static HttpClient HttpClientFor(RestoreOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            MaxConnectionsPerServer = Math.Max(1, options.MaxParallelDownloads),
        };

        var http = new HttpClient(handler) { Timeout = options.HttpTimeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("tps/" + CompilerVersion.Text);

        return http;
    }

    /// <summary>Compares two NuGet versions by their numeric segments — the same comparison
    /// <see cref="ProjectResolver"/> uses to pick between two versions of one package.</summary>
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
}
