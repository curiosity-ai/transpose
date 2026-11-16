using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Covers which <c>Transpose.dll</c> a project build binds against when the NuGet cache holds more
/// than one <c>Transpose.BCL</c> version (<see cref="TransposeAssemblies.UseBaseLibraryOf"/>).
///
/// A project build used to bind the newest base library in the cache whatever the project pinned,
/// while its site shipped the <c>tps.js</c> of the pinned version. Tesserae pinned 26.9.5840 and built
/// on a machine that had only that version; on a machine where any other project had restored
/// 26.11.7972, the same commit failed with CS0411, because <c>Enumerable.Range</c> returns
/// <c>IEnumerable&lt;int&gt;</c> there and <c>EnumerableInstance&lt;int&gt;</c> in 26.9.5840.
/// </summary>
[TestClass]
public sealed class ProjectBaseLibraryVersionTests
{
    private string  _root  = "";
    private string  _cache = "";
    private string  _realBaseLibrary = "";
    private string? _previousCacheEnv;
    private string? _previousDllEnv;

    [TestInitialize]
    public void Setup()
    {
        // Read before the environment changes: this is the base library every other test binds, and
        // it is put back afterwards, since the choice is process-wide.
        _realBaseLibrary = TransposeAssemblies.TransposeDllPath;

        _root  = Path.Combine(Path.GetTempPath(), "tps-bclpin-" + Guid.NewGuid().ToString("N"));
        _cache = Path.Combine(_root, "packages");
        Directory.CreateDirectory(_cache);

        _previousCacheEnv = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        _previousDllEnv   = Environment.GetEnvironmentVariable("TRANSPOSE_DLL_PATH");
        Environment.SetEnvironmentVariable("NUGET_PACKAGES", _cache);
        Environment.SetEnvironmentVariable("TRANSPOSE_DLL_PATH", null);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("NUGET_PACKAGES", _previousCacheEnv);
        Environment.SetEnvironmentVariable("TRANSPOSE_DLL_PATH", _previousDllEnv);
        TransposeAssemblies.TransposeDllPath = _realBaseLibrary;
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>Extracts a <c>Transpose.BCL</c> version into the fake cache the way NuGet lays it out,
    /// holding a copy of the real base library (the choice is made by assembly name, so it has to be
    /// a real assembly called <c>Transpose</c>).</summary>
    private string BaseLibraryPackage(string version)
    {
        var lib = Path.Combine(_cache, "transpose.bcl", version, "lib", "netstandard2.0");
        Directory.CreateDirectory(lib);
        var dll = Path.Combine(lib, "Transpose.dll");
        File.Copy(_realBaseLibrary, dll);
        File.WriteAllText(Path.Combine(_cache, "transpose.bcl", version, "transpose.bcl.nuspec"), $@"<?xml version=""1.0""?>
<package><metadata><id>Transpose.BCL</id><version>{version}</version></metadata></package>");
        return dll;
    }

    private string Csproj(string? baseLibraryVersion)
    {
        var path = Path.Combine(_root, "app", "app.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var reference = baseLibraryVersion is null
            ? ""
            : $@"<PackageReference Include=""Transpose.BCL"" Version=""{baseLibraryVersion}"" />";
        File.WriteAllText(path, $@"<Project>
  <PropertyGroup><TargetFramework>netstandard2.0</TargetFramework></PropertyGroup>
  <ItemGroup>{reference}</ItemGroup>
</Project>");
        return path;
    }

    private static string Full(string path) => Path.GetFullPath(path);

    // Discovery also searches the user's real NuGet cache, so the fake versions are above any real one.
    private const string Older = "99.9.5840";
    private const string Newer = "99.11.7972";

    [TestMethod]
    public void TheVersionTheProjectPinsWinsOverANewerOneInTheCache()
    {
        var pinned = BaseLibraryPackage(Older);
        var newer  = BaseLibraryPackage(Newer);

        // Sanity: with no project to ask, the newest in the cache is still the answer.
        TransposeAssemblies.UseBaseLibraryOf(Array.Empty<string>());
        Assert.AreEqual(Full(newer), Full(TransposeAssemblies.TransposeDllPath));

        var project = ProjectResolver.Resolve(Csproj(Older));
        var chosen  = TransposeAssemblies.UseBaseLibraryOf(project.ReferencePaths);

        Assert.AreEqual(Full(pinned), Full(chosen!));
        Assert.AreEqual(Full(pinned), Full(TransposeAssemblies.TransposeDllPath),
            "a project build must bind the Transpose.BCL version its csproj pins, not the newest in the cache");
    }

    [TestMethod]
    public void AProjectThatReferencesNoBaseLibraryGetsTheNewestInTheCache()
    {
        BaseLibraryPackage(Older);
        var newer = BaseLibraryPackage(Newer);

        // A previous build in the same process chose the older one…
        TransposeAssemblies.UseBaseLibraryOf(ProjectResolver.Resolve(Csproj(Older)).ReferencePaths);

        // …and must not leak into a project that pins nothing.
        var chosen = TransposeAssemblies.UseBaseLibraryOf(ProjectResolver.Resolve(Csproj(null)).ReferencePaths);

        Assert.IsNull(chosen, "nothing among the references is the base library");
        Assert.AreEqual(Full(newer), Full(TransposeAssemblies.TransposeDllPath));
    }

    [TestMethod]
    public void TransposeDllPathStillWinsOverThePinnedVersion()
    {
        BaseLibraryPackage(Older);
        var local = Path.Combine(_root, "local", "Transpose.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        File.Copy(_realBaseLibrary, local);
        Environment.SetEnvironmentVariable("TRANSPOSE_DLL_PATH", local);

        var chosen = TransposeAssemblies.UseBaseLibraryOf(ProjectResolver.Resolve(Csproj(Older)).ReferencePaths);

        Assert.AreEqual(Full(local), Full(chosen!),
            "a dev tree compiling against a locally built runtime must keep doing so");
        Assert.AreEqual(Full(local), Full(TransposeAssemblies.TransposeDllPath));
    }

    [TestMethod]
    public void ChoosingAnotherBaseLibraryDropsWhatWasReadFromThePreviousOne()
    {
        var older = BaseLibraryPackage(Older);
        var newer = BaseLibraryPackage(Newer);

        TransposeAssemblies.TransposeDllPath = older;
        var fromOlder = TransposeAssemblies.NoBodyMethodTokens;

        TransposeAssemblies.TransposeDllPath = older;
        Assert.AreSame(fromOlder, TransposeAssemblies.NoBodyMethodTokens,
            "choosing the same file again keeps what was read from it");

        // The tokens are metadata tokens of ONE file; read against another build they name unrelated
        // methods (see DuplicateBaseLibraryReferenceTests), so they must be read again.
        TransposeAssemblies.TransposeDllPath = newer;
        Assert.AreNotSame(fromOlder, TransposeAssemblies.NoBodyMethodTokens);
    }
}
