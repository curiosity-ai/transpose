using Microsoft.CodeAnalysis.CSharp;

namespace Transpose.Compiler.Library;

/// <summary>
/// A compile-to-JavaScript request built up in memory: source files plus the reference assemblies
/// and settings they need, with no <c>.csproj</c> or on-disk project involved. Build one with the
/// fluent <c>With…</c> methods and hand it to <see cref="TransposeCompilerLibrary.Compile"/> (or
/// <see cref="TransposeCompilerLibrary.CompileAsync"/>).
///
/// <c>Transpose.dll</c> (the base library) is referenced automatically — every Transpose
/// compilation binds against it, exactly like a normal <c>tps</c> project (see
/// <c>Transpose.Translator.Compilation.CompilationBuilder</c>) — so it never needs to be added here.
/// Anything beyond the base library (Transpose.Core, Transpose.Newtonsoft.Json, a user's own
/// binding library, …) is added with <see cref="WithPackageReference"/> or
/// <see cref="WithReferenceAssembly"/>.
/// </summary>
public sealed class CompilationRequest
{
    private readonly List<(string path, string text)> _sources = new();
    private readonly List<string> _referencePaths = new();
    private readonly List<string> _defineConstants = new() { "TRANSPOSE", "TRACE" };
    private int _autoSourceCount;

    /// <summary>The assembly name the compilation is built under — the JS bundle's default file name
    /// and, when <see cref="EmitPackageAssembly"/> is set, the emitted .NET assembly's name too.</summary>
    public string AssemblyName { get; }

    public CompilationRequest(string assemblyName = "App")
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
            throw new ArgumentException("Assembly name must be provided.", nameof(assemblyName));
        AssemblyName = assemblyName;
    }

    internal IReadOnlyList<(string path, string text)> Sources => _sources;
    internal IReadOnlyList<string> ReferencePaths => _referencePaths;
    internal IReadOnlyList<string> DefineConstants => _defineConstants;

    public LanguageVersion LanguageVersion { get; private set; } = LanguageVersion.Latest;
    public bool ReflectionEnabled { get; private set; } = true;
    public MetadataTarget MetadataTarget { get; private set; } = MetadataTarget.Inline;
    public string? AssemblyVersion { get; private set; }

    /// <summary>Prepend the full <c>tps.js</c> runtime (plus the <c>TransposeR</c> shim) to the
    /// emitted JavaScript, so the result is a single, directly runnable script — equivalent to the
    /// <c>tps</c> CLI's <c>--with-runtime</c>.</summary>
    public bool IncludeRuntime { get; private set; }

    /// <summary>Also emit a .NET assembly with the compiled JS embedded as a manifest resource
    /// (<see cref="CompilationResult.PackageAssemblyBytes"/>) — equivalent to the <c>tps</c> CLI's
    /// <c>--emit-package</c>. Off by default: most callers just want the JavaScript.</summary>
    public bool EmitPackageAssembly { get; private set; }

    /// <summary>Adds one in-memory source file. <paramref name="fileName"/> only needs to be unique
    /// within the request — it never touches disk — and shows up in diagnostic locations.</summary>
    public CompilationRequest WithSourceFile(string fileName, string code)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name must be provided.", nameof(fileName));
        _sources.Add((fileName, code ?? ""));
        return this;
    }

    /// <summary>Adds one in-memory source file under an automatically generated name
    /// (<c>Source1.cs</c>, <c>Source2.cs</c>, …).</summary>
    public CompilationRequest WithSource(string code) => WithSourceFile($"Source{++_autoSourceCount}.cs", code);

    /// <summary>Adds a preprocessor symbol (<c>#if …</c>) for the compilation. <c>TRANSPOSE</c> and
    /// <c>TRACE</c> are always defined already, matching a normal <c>tps</c> build.</summary>
    public CompilationRequest WithDefine(string symbol)
    {
        if (!string.IsNullOrWhiteSpace(symbol) && !_defineConstants.Contains(symbol)) _defineConstants.Add(symbol);
        return this;
    }

    public CompilationRequest WithLanguageVersion(LanguageVersion version)
    {
        LanguageVersion = version;
        return this;
    }

    /// <summary>References an already-resolved assembly by path — a Transpose binding library
    /// (Transpose.Core, …) built elsewhere, a package DLL extracted by hand, or any other assembly
    /// the compilation should bind against. See <see cref="WithPackageReference"/> for resolving one
    /// straight from the local NuGet cache instead.</summary>
    public CompilationRequest WithReferenceAssembly(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path must be provided.", nameof(path));
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException("Reference assembly not found.", full);
        if (!_referencePaths.Contains(full, StringComparer.OrdinalIgnoreCase)) _referencePaths.Add(full);
        return this;
    }

    /// <summary>
    /// Resolves <paramref name="packageId"/> <paramref name="version"/> (and its transitive
    /// dependencies) from the local NuGet global-packages cache and references every assembly it
    /// contributes — exactly what a <c>&lt;PackageReference&gt;</c> in a csproj does. The package must
    /// already be restored (e.g. by a prior <c>dotnet restore</c>/<c>nuget install</c> that pulled it
    /// into <c>~/.nuget/packages</c> or the <c>NUGET_PACKAGES</c> directory) — this does not download
    /// anything.
    /// </summary>
    public CompilationRequest WithPackageReference(string packageId, string version)
    {
        if (string.IsNullOrWhiteSpace(packageId)) throw new ArgumentException("Package id must be provided.", nameof(packageId));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Package version must be provided.", nameof(version));

        var resolved = ProjectResolver.ResolvePackage(packageId, version).ToList();
        if (resolved.Count == 0)
            throw new InvalidOperationException(
                $"Could not resolve package '{packageId}' {version} from the local NuGet cache. " +
                "Restore it first (e.g. `dotnet restore`/`nuget install`) — this does not download packages.");

        foreach (var (_, path, _) in resolved)
            if (!_referencePaths.Contains(path, StringComparer.OrdinalIgnoreCase)) _referencePaths.Add(path);
        return this;
    }

    /// <summary>Disables reflection metadata emission entirely (equivalent to tps.json's
    /// <c>reflection.disabled</c>). Reflection is enabled by default.</summary>
    public CompilationRequest WithoutReflection()
    {
        ReflectionEnabled = false;
        return this;
    }

    /// <summary>Where the reflection metadata block is emitted (default: <see cref="MetadataTarget.Inline"/>,
    /// alongside the generated types — the natural choice with no separate metadata file to ship).</summary>
    public CompilationRequest WithMetadataTarget(MetadataTarget target)
    {
        MetadataTarget = target;
        return this;
    }

    public CompilationRequest WithRuntime()
    {
        IncludeRuntime = true;
        return this;
    }

    public CompilationRequest AsPackageAssembly()
    {
        EmitPackageAssembly = true;
        return this;
    }

    /// <summary>The <c>AssemblyVersion</c> baked into the bundle via <c>Transpose.assemblyVersion(...)</c>
    /// and, when <see cref="EmitPackageAssembly"/> is set, the emitted .NET assembly's version.</summary>
    public CompilationRequest WithAssemblyVersion(string version)
    {
        AssemblyVersion = version;
        return this;
    }
}
