using System.IO.Compression;
using System.Text;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// <c>tps restore</c>: reading the nuget.config chain, choosing versions and dependency groups, and
/// laying a package out in the global-packages folder.
///
/// <para>Everything here runs against a <b>folder source</b> of real <c>.nupkg</c> files built in a
/// temp directory — a folder is a NuGet source as it stands, so the whole restore path is exercised
/// with no feed to reach. What the tests are really pinning is that the restore installs exactly what
/// <see cref="ProjectResolver"/> goes looking for afterwards: the two are halves of one operation, and
/// a disagreement between them is silent — the package is on disk, the resolver looks for it under a
/// name nothing wrote, and the build reports missing types for a package that restored perfectly.</para>
/// </summary>
[TestClass]
public sealed class PackageRestoreTests
{
    private string _root     = "";
    private string _source   = "";
    private string _packages = "";

    [TestInitialize]
    public void Setup()
    {
        _root     = Path.Combine(Path.GetTempPath(), "tps-restore-" + Guid.NewGuid().ToString("N"));
        _source   = Path.Combine(_root, "source");
        _packages = Path.Combine(_root, "packages");

        Directory.CreateDirectory(_source);
        Directory.CreateDirectory(_packages);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    // ---- the fixture --------------------------------------------------------

    /// <summary>Writes a real <c>.nupkg</c> into the folder source: a nuspec, one assembly under
    /// <c>lib/netstandard2.0</c>, and the OPC parts a package carries as a zip.</summary>
    private void Package(string id, string version, params (string framework, (string id, string version)[] dependencies)[] groups)
    {
        var groupXml = groups.Length == 0
            ? ""
            : "<dependencies>" + string.Join("", groups.Select(g =>
                  $"<group targetFramework=\"{g.framework}\">"
                  + string.Join("", g.dependencies.Select(d => $"<dependency id=\"{d.id}\" version=\"{d.version}\" />"))
                  + "</group>")) + "</dependencies>";

        var nuspec = $"""
            <?xml version="1.0"?>
            <package><metadata>
              <id>{id}</id>
              <version>{version}</version>
              {groupXml}
            </metadata></package>
            """;

        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, id + ".nuspec", nuspec);
            Write(archive, $"lib/netstandard2.0/{id}.dll", "not really an assembly");
            Write(archive, "[Content_Types].xml", "<Types />");
            Write(archive, "_rels/.rels", "<Relationships />");
            Write(archive, "package/services/metadata/core-properties/x.psmdcp", "<coreProperties />");
        }

        File.WriteAllBytes(Path.Combine(_source, $"{id}.{version}.nupkg"), stream.ToArray());

        static void Write(ZipArchive archive, string name, string content)
        {
            using var entry = archive.CreateEntry(name).Open();
            entry.Write(Encoding.UTF8.GetBytes(content));
        }
    }

    private string Csproj(params string[] packageReferences)
        => CsprojAt("app", "app.csproj", packageReferences);

    private string CsprojAt(string directory, string fileName, params string[] items)
    {
        var path = Path.Combine(_root, directory, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        File.WriteAllText(path, $"""
            <Project>
              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
            {string.Join("\n", items.Select(i => "    " + i))}
              </ItemGroup>
            </Project>
            """);

        return path;
    }

    private static string Reference(string id, string version) => $"<PackageReference Include=\"{id}\" Version=\"{version}\" />";

    private async Task<RestoreOutcome> RestoreAsync(string csproj, bool fromConfiguredSources = false)
        => await PackageRestore.RunAsync(new RestoreOptions
        {
            CsprojPath              = csproj,
            PackagesFolder          = _packages,
            Sources                 = fromConfiguredSources ? Array.Empty<string>() : new[] { _source },
            IgnoreConfiguredSources = !fromConfiguredSources,
        }, BuildLog.Silent);

    private string Installed(string id, string version) => Path.Combine(_packages, id.ToLowerInvariant(), version);

    // ---- the layout ---------------------------------------------------------

    /// <summary>
    /// The one thing the rest of the compiler depends on: a restored package is laid out the way
    /// NuGet lays it out — lower-cased folder and nuspec, the <c>lib</c> tree as it was in the zip,
    /// the <c>.nupkg</c> and its hash beside them, and the marker that says it is complete. The
    /// lower-cased nuspec in particular is what <see cref="ProjectResolver"/> reads a package's
    /// dependencies out of, and a case-sensitive filesystem does not forgive the other spelling.
    /// </summary>
    [TestMethod]
    public async Task InstallsAPackageTheWayNuGetLaysItOutAsync()
    {
        Package("Alpha", "1.2.3");

        var outcome = await RestoreAsync(Csproj(Reference("Alpha", "1.2.3")));

        Assert.IsTrue(outcome.Success, string.Join("; ", outcome.Errors));

        var folder = Installed("Alpha", "1.2.3");

        Assert.IsTrue(File.Exists(Path.Combine(folder, "alpha.nuspec")), "the nuspec is written lower-cased");
        Assert.IsTrue(File.Exists(Path.Combine(folder, "lib", "netstandard2.0", "Alpha.dll")), "the lib tree is extracted");
        Assert.IsTrue(File.Exists(Path.Combine(folder, "alpha.1.2.3.nupkg")), "the package itself is kept");
        Assert.IsTrue(File.Exists(Path.Combine(folder, "alpha.1.2.3.nupkg.sha512")), "so is its hash");
        Assert.IsTrue(File.Exists(Path.Combine(folder, ".nupkg.metadata")), "and the marker that says the folder is complete");
    }

    /// <summary>The zip bookkeeping a <c>.nupkg</c> carries as an OPC package is not content, and NuGet
    /// leaves it out — a <c>_rels</c> folder appearing inside a package would be visible to anything
    /// that globs its files.</summary>
    [TestMethod]
    public async Task LeavesThePackagingArtefactsOutAsync()
    {
        Package("Alpha", "1.0.0");

        await RestoreAsync(Csproj(Reference("Alpha", "1.0.0")));

        var folder = Installed("Alpha", "1.0.0");

        Assert.IsFalse(Directory.Exists(Path.Combine(folder, "_rels")));
        Assert.IsFalse(Directory.Exists(Path.Combine(folder, "package")));
        Assert.IsFalse(File.Exists(Path.Combine(folder, "[Content_Types].xml")));
    }

    /// <summary>A package already installed is left alone — a restore before every build must not
    /// re-download a cache that is already correct.</summary>
    [TestMethod]
    public async Task DoesNotReinstallWhatIsAlreadyThereAsync()
    {
        Package("Alpha", "1.0.0");

        var first = await RestoreAsync(Csproj(Reference("Alpha", "1.0.0")));
        Assert.AreEqual(1, first.Downloaded);

        var second = await RestoreAsync(Csproj(Reference("Alpha", "1.0.0")));

        Assert.IsTrue(second.Success);
        Assert.AreEqual(0, second.Downloaded, "the second restore found the package already installed");
        Assert.AreEqual(1, second.Packages.Count);
    }

    /// <summary>A version the project writes as <c>1.0</c> installs — and has to be looked up — as
    /// <c>1.0.0</c>, NuGet's normalized spelling. The restore and the reference resolver agreeing on
    /// this is the whole point of <see cref="NuGetVersions.Normalize"/>.</summary>
    [TestMethod]
    public async Task NormalizesTheVersionTheWayTheCacheSpellsItAsync()
    {
        Package("Alpha", "1.0.0");

        var outcome = await RestoreAsync(Csproj(Reference("Alpha", "1.0")));

        Assert.IsTrue(outcome.Success, string.Join("; ", outcome.Errors));
        Assert.IsTrue(Directory.Exists(Installed("Alpha", "1.0.0")), "installed under the normalized version");

        var resolved = ResolveWithCache(Csproj(Reference("Alpha", "1.0")));

        Assert.IsTrue(resolved.ReferencePaths.Any(p => Path.GetFileName(p) == "Alpha.dll"),
            "and the reference resolver finds it under the version the project wrote");
    }

    // ---- the graph ----------------------------------------------------------

    /// <summary>A package's dependencies are installed too, transitively.</summary>
    [TestMethod]
    public async Task FollowsTheDependencyGraphAsync()
    {
        Package("Alpha", "1.0.0", (".NETStandard2.0", new[] { ("Beta", "2.0.0") }));
        Package("Beta",  "2.0.0", (".NETStandard2.0", new[] { ("Gamma", "3.0.0") }));
        Package("Gamma", "3.0.0");

        var outcome = await RestoreAsync(Csproj(Reference("Alpha", "1.0.0")));

        Assert.IsTrue(outcome.Success, string.Join("; ", outcome.Errors));
        CollectionAssert.AreEquivalent(
            new[] { "Alpha", "Beta", "Gamma" },
            outcome.Packages.Select(p => p.Id).ToArray());
    }

    /// <summary>
    /// The version a project declares supersedes one reached through a dependency, and the superseded
    /// one is never fetched — installing it would put a version in the cache that nothing ever binds.
    /// </summary>
    [TestMethod]
    public async Task ADeclaredVersionSupersedesATransitiveOneAsync()
    {
        Package("Alpha", "1.0.0", (".NETStandard2.0", new[] { ("Beta", "1.0.0") }));
        Package("Beta",  "1.0.0");
        Package("Beta",  "2.0.0");

        var outcome = await RestoreAsync(Csproj(Reference("Alpha", "1.0.0"), Reference("Beta", "2.0.0")));

        Assert.IsTrue(outcome.Success, string.Join("; ", outcome.Errors));
        Assert.IsTrue(Directory.Exists(Installed("Beta", "2.0.0")));
        Assert.IsFalse(Directory.Exists(Installed("Beta", "1.0.0")), "the version the project declares is the only one installed");
    }

    /// <summary>
    /// Every project in the ProjectReference closure is restored, each against its *own* declared set.
    /// That is not the same as restoring one union of them: the app declares Beta 1.0, which suppresses
    /// the transitive demand for Beta *within the app*, but the library it references does not declare
    /// Beta at all — so the build binds Beta 2.0 through the library, and a union-based restore would
    /// never have installed it.
    /// </summary>
    [TestMethod]
    public async Task RestoresEachProjectInTheClosureAgainstItsOwnDeclaredSetAsync()
    {
        Package("Alpha", "1.0.0", (".NETStandard2.0", new[] { ("Beta", "2.0.0") }));
        Package("Beta",  "1.0.0");
        Package("Beta",  "2.0.0");

        CsprojAt("lib", "lib.csproj", Reference("Alpha", "1.0.0"));

        var app = CsprojAt("app", "app.csproj",
            Reference("Beta", "1.0.0"),
            "<ProjectReference Include=\"../lib/lib.csproj\" />");

        var outcome = await RestoreAsync(app);

        Assert.IsTrue(outcome.Success, string.Join("; ", outcome.Errors));
        Assert.IsTrue(Directory.Exists(Installed("Beta", "1.0.0")), "the app's own declared version");
        Assert.IsTrue(Directory.Exists(Installed("Beta", "2.0.0")), "and the one the library reaches transitively");
    }

    /// <summary>A dependency range takes its lowest satisfying version, which for every shape a nuspec
    /// writes is the lower bound.</summary>
    [TestMethod]
    public async Task ADependencyRangeTakesItsLowestSatisfyingVersionAsync()
    {
        Package("Alpha", "1.0.0", (".NETStandard2.0", new[] { ("Beta", "[2.0.0, 3.0.0)") }));
        Package("Beta",  "2.0.0");
        Package("Beta",  "2.5.0");

        var outcome = await RestoreAsync(Csproj(Reference("Alpha", "1.0.0")));

        Assert.IsTrue(outcome.Success, string.Join("; ", outcome.Errors));
        Assert.IsTrue(Directory.Exists(Installed("Beta", "2.0.0")));
        Assert.IsFalse(Directory.Exists(Installed("Beta", "2.5.0")));
    }

    /// <summary>
    /// A package declares a different dependency set per framework, and a netstandard2.0 project takes
    /// the netstandard group — not the net8.0 one, whose dependencies it would never bind and whose
    /// download is what the framework matching exists to avoid.
    /// </summary>
    [TestMethod]
    public async Task TakesTheDependencyGroupForTheProjectsFrameworkAsync()
    {
        Package("Alpha", "1.0.0",
            (".NETStandard2.0", new[] { ("ForNetStandard", "1.0.0") }),
            ("net8.0",          new[] { ("ForNet8", "1.0.0") }));

        Package("ForNetStandard", "1.0.0");
        Package("ForNet8",        "1.0.0");

        var outcome = await RestoreAsync(Csproj(Reference("Alpha", "1.0.0")));

        Assert.IsTrue(outcome.Success, string.Join("; ", outcome.Errors));
        Assert.IsTrue(Directory.Exists(Installed("ForNetStandard", "1.0.0")));
        Assert.IsFalse(Directory.Exists(Installed("ForNet8", "1.0.0")));
    }

    // ---- what fails, and how loudly ----------------------------------------

    /// <summary>A package the project declares and no source has is an error: the build cannot bind
    /// what the project names.</summary>
    [TestMethod]
    public async Task ADeclaredPackageNoSourceHasIsAnErrorAsync()
    {
        var outcome = await RestoreAsync(Csproj(Reference("Missing", "1.0.0")));

        Assert.IsFalse(outcome.Success);
        Assert.IsTrue(outcome.Errors.Single().Contains("Missing 1.0.0"), outcome.Errors.Single());
    }

    /// <summary>One reached through a dependency is a warning. The group this walk chose is the one
    /// NuGet would choose, but there is no lock file to check that against — and a package that is
    /// genuinely needed and absent surfaces afterwards as a C# error naming the type, which says more
    /// than a restore can.</summary>
    [TestMethod]
    public async Task ATransitivePackageNoSourceHasIsAWarningAsync()
    {
        Package("Alpha", "1.0.0", (".NETStandard2.0", new[] { ("Missing", "1.0.0") }));

        var outcome = await RestoreAsync(Csproj(Reference("Alpha", "1.0.0")));

        Assert.IsTrue(outcome.Success, string.Join("; ", outcome.Errors));
        Assert.IsTrue(outcome.Warnings.Any(w => w.Contains("Missing 1.0.0")), string.Join("; ", outcome.Warnings));
    }

    /// <summary>Nothing downstream resolves a floating version — the reference resolver looks a package
    /// up by the exact version the project writes down — so installing *some* version would leave the
    /// build unable to find it, with nothing to say why.</summary>
    [TestMethod]
    public async Task AFloatingVersionIsRefusedRatherThanGuessedAtAsync()
    {
        Package("Alpha", "1.0.0");

        var outcome = await RestoreAsync(Csproj(Reference("Alpha", "1.0.*")));

        Assert.IsFalse(outcome.Success);
        Assert.IsTrue(outcome.Errors.Single().Contains("floating"), outcome.Errors.Single());
    }

    /// <summary>A <c>PackageReference</c> with no version at all (central package management, which
    /// nothing here evaluates) is said out loud rather than skipped silently — the reference resolver
    /// cannot bind it either.</summary>
    [TestMethod]
    public async Task APackageReferenceWithNoVersionIsReportedAsync()
    {
        var outcome = await RestoreAsync(Csproj("<PackageReference Include=\"Alpha\" />"));

        Assert.IsTrue(outcome.Success, "nothing to restore is not a failure");
        Assert.IsTrue(outcome.Warnings.Any(w => w.Contains("no Version")), string.Join("; ", outcome.Warnings));
    }

    // ---- the nuget.config chain --------------------------------------------

    /// <summary>
    /// A config written *beside* a checkout applies to every project in it — which is how a host hands
    /// a folder of packages to a front-end it did not write, without editing the front-end. A relative
    /// source in it resolves against the config's own directory.
    /// </summary>
    [TestMethod]
    public async Task ReadsASourceFromAConfigAboveTheProjectAsync()
    {
        Package("Alpha", "1.0.0");

        File.WriteAllText(Path.Combine(_root, "nuget.config"), """
            <?xml version="1.0"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="source" />
              </packageSources>
            </configuration>
            """);

        var outcome = await RestoreAsync(Csproj(Reference("Alpha", "1.0.0")), fromConfiguredSources: true);

        Assert.IsTrue(outcome.Success, string.Join("; ", outcome.Errors));
        Assert.AreEqual(_source, Path.GetFullPath(outcome.Packages.Single().Source!));
    }

    /// <summary>A source the configuration disables is not consulted — so the restore below finds
    /// nothing, rather than finding the package through a source someone switched off.</summary>
    [TestMethod]
    public async Task DoesNotConsultADisabledSourceAsync()
    {
        Package("Alpha", "1.0.0");

        File.WriteAllText(Path.Combine(_root, "nuget.config"), """
            <?xml version="1.0"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="source" />
              </packageSources>
              <disabledPackageSources>
                <add key="local" value="true" />
              </disabledPackageSources>
            </configuration>
            """);

        var outcome = await RestoreAsync(Csproj(Reference("Alpha", "1.0.0")), fromConfiguredSources: true);

        Assert.IsFalse(outcome.Success);
    }

    // ---- framework matching ------------------------------------------------

    /// <summary>
    /// The group chooser, on the monikers a real nuspec writes. A netstandard2.0 consumer takes the
    /// closest netstandard group it can use and never one above it; a .NET consumer prefers its own
    /// family and falls back to netstandard; an unusable set leaves nothing to choose.
    /// </summary>
    [TestMethod]
    public void PicksTheDependencyGroupNuGetWouldPick()
    {
        Assert.AreEqual(1, NuGetFrameworks.BestGroup("netstandard2.0", new[] { "netstandard1.6", ".NETStandard2.0", "netstandard2.1" }));
        Assert.AreEqual(0, NuGetFrameworks.BestGroup("netstandard2.0", new[] { "netstandard1.6", "net8.0" }));
        Assert.AreEqual(1, NuGetFrameworks.BestGroup("net10.0",        new[] { "netstandard2.0", "net8.0" }));
        Assert.AreEqual(0, NuGetFrameworks.BestGroup("net10.0",        new[] { "netstandard2.0", "net472" }));
        Assert.AreEqual(1, NuGetFrameworks.BestGroup("net472",         new[] { "netstandard2.0", "net462" }));
        Assert.AreEqual(0, NuGetFrameworks.BestGroup("netstandard2.0", new string?[] { null, "net8.0" }), "the catch-all group is usable");
        Assert.AreEqual(-1, NuGetFrameworks.BestGroup("netstandard2.0", new[] { "netstandard2.1", "net8.0" }), "and nothing else is");
    }

    /// <summary>The version spellings that reach the cache, and the ones that identify a range.</summary>
    [TestMethod]
    public void NormalizesVersionsAndReadsRanges()
    {
        Assert.AreEqual("1.0.0",       NuGetVersions.Normalize("1.0"));
        Assert.AreEqual("1.0.0",       NuGetVersions.Normalize("1.0.0.0"));
        Assert.AreEqual("1.0.0.4",     NuGetVersions.Normalize("1.0.0.4"));
        Assert.AreEqual("1.0.0-beta1", NuGetVersions.Normalize("1.0-Beta1"));
        Assert.AreEqual("1.0.0",       NuGetVersions.Normalize("1.0.0+build7"), "build metadata is not part of the identity");

        Assert.AreEqual("1.2.3", NuGetVersions.LowestSatisfying("1.2.3"));
        Assert.AreEqual("1.2.3", NuGetVersions.LowestSatisfying("[1.2.3]"));
        Assert.AreEqual("1.2.3", NuGetVersions.LowestSatisfying("[1.2.3, 2.0.0)"));
        Assert.IsNull(NuGetVersions.LowestSatisfying("(, 2.0.0)"));
        Assert.IsNull(NuGetVersions.LowestSatisfying(null));
    }

    private ResolvedProject ResolveWithCache(string csproj)
    {
        var previous = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        try
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", _packages);
            return ProjectResolver.Resolve(csproj);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", previous);
        }
    }
}
