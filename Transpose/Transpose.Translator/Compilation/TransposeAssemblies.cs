using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Transpose.Translator;

/// <summary>
/// Locates the Transpose reference assembly (Transpose.dll) and extracts the embedded Transpose
/// JavaScript runtime (tps.js) so emitted code can run against the real Transpose runtime.
/// </summary>
public static class TransposeAssemblies
{
    private static string? _tpsDllPath;
    private static string? _runtimeJs;
    private static HashSet<int>? _noBodyTokens;

    /// <summary>
    /// Path to Transpose.dll: <c>TRANSPOSE_DLL_PATH</c> when set, otherwise the newest base library in
    /// the NuGet global-packages cache — unless a project build chose its own with
    /// <see cref="UseBaseLibraryOf"/>. Setting it to a different file drops everything read from the
    /// previous one (the runtime JS and the no-body method tokens), because both are only meaningful
    /// for the file they were read from.
    /// </summary>
    public static string TransposeDllPath
    {
        get => _tpsDllPath ??= Discover();
        set => Select(value);
    }

    /// <summary>
    /// Binds the next compilation to the base library its own references resolved to — the
    /// <c>Transpose.BCL</c> version the project (or a package it depends on) asked for, found among
    /// <paramref name="referencePaths"/> by assembly name. <c>TRANSPOSE_DLL_PATH</c> still wins, so a
    /// dev tree can compile against a locally built runtime. With neither, the choice falls back to
    /// the newest base library in the NuGet cache, as for a compilation with no project.
    ///
    /// Without this a project build bound the newest <c>Transpose.BCL</c> in the cache whatever it
    /// pinned, while its site shipped the <c>tps.js</c> of the version it pinned (the site build
    /// extracts the runtime from the project's own references). Whether a commit compiled then depended
    /// on which other projects had filled the machine's cache, and a build that did compile bound its
    /// C# against one BCL and ran on the runtime JavaScript of another.
    ///
    /// The choice is process-wide, like the rest of this class: a host that builds several projects
    /// calls this before each compilation (the library serializes compilations, so they cannot race).
    /// Returns the file chosen, or null when the choice is left to the lazy NuGet-cache discovery.
    /// </summary>
    public static string? UseBaseLibraryOf(IEnumerable<string> referencePaths)
    {
        var chosen = EnvironmentOverride()
                     ?? referencePaths.FirstOrDefault(p => File.Exists(p)
                                                           && AssemblySimpleName(p) == BaseLibraryAssemblyName);
        Select(chosen);
        return chosen;
    }

    /// <summary>Assembly name of the base library. Its package id is <c>Transpose.BCL</c>, but the
    /// assembly it ships stays <c>Transpose</c>.</summary>
    public const string BaseLibraryAssemblyName = "Transpose";

    /// <summary>Points everything at <paramref name="path"/> (null: discover lazily), dropping what was
    /// read from the previous file when it is a different one.</summary>
    private static void Select(string? path)
    {
        var full = path is null ? null : Path.GetFullPath(path);
        if (string.Equals(full, _tpsDllPath is null ? null : Path.GetFullPath(_tpsDllPath), StringComparison.Ordinal))
            return;
        _tpsDllPath = full;
        _runtimeJs = null;
        _noBodyTokens = null;
    }

    private static string? EnvironmentOverride()
    {
        var env = Environment.GetEnvironmentVariable("TRANSPOSE_DLL_PATH");
        return !string.IsNullOrEmpty(env) && File.Exists(env) ? env : null;
    }

    private static string Discover()
    {
        // Allow explicit override.
        if (EnvironmentOverride() is { } env) return env;

        // Resolve Transpose.dll from the NuGet global-packages cache using the same flow as h5
        // (Translator.GetPackagesCacheFolder): honour NUGET_PACKAGES first, then the per-platform
        // user-profile default. The base library ships as the "Transpose.BCL" package (its assembly
        // is still Transpose.dll). NuGet lower-cases the package-id folder on case-sensitive
        // filesystems (Linux/macOS) but preserves it on Windows, so probe both casings. The
        // historical "Transpose" id is still probed for backwards compatibility with older caches.
        var candidates =
            from root in NuGetRoots()
            where Directory.Exists(root)
            from id in new[] { "Transpose.BCL", "transpose.bcl", "Transpose", "transpose" }
            let pkg = Path.Combine(root, id)
            where Directory.Exists(pkg)
            from versionDir in Directory.GetDirectories(pkg)
            let dll = Path.Combine(versionDir, "lib", "netstandard2.0", "Transpose.dll")
            where File.Exists(dll)
            select (version: Path.GetFileName(versionDir), dll);

        // Newest wins — by version, not by folder name. The versions are CalVer (yy.M.buildId), so an
        // ordinal sort of the folder names silently prefers a *stale* runtime as soon as a component's
        // digit count changes ("26.7.999" over "26.7.3104", "26.7.x" over "26.10.x"). A stale runtime
        // does not fail loudly: it compiles fine and only misbehaves at runtime.
        var found = candidates
            .OrderBy(c => c.version, PackageVersionOrder.Instance)
            .Select(c => c.dll)
            .LastOrDefault();
        if (found is null)
        {
            throw new InvalidOperationException(
                "Could not locate Transpose.dll in the NuGet packages cache. Set the TRANSPOSE_DLL_PATH environment variable.");
        }
        return found;
    }

    /// <summary>
    /// Orders NuGet version-folder names by their numeric segments, with a release ranking above any
    /// prerelease of the same segments (<c>1.0.0</c> &gt; <c>1.0.0-beta</c>) — enough to pick between
    /// the versions of one package in the cache, like <c>ProjectResolver.CompareVersions</c> does for
    /// package references (a full NuGet.Versioning dependency would buy nothing here). A folder name
    /// that is not a version sorts below every real one.
    /// </summary>
    internal sealed class PackageVersionOrder : IComparer<string>
    {
        public static readonly PackageVersionOrder Instance = new();

        public int Compare(string? x, string? y)
        {
            var (left,  leftPre)  = Parse(x);
            var (right, rightPre) = Parse(y);

            for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
            {
                var a = i < left.Length  ? left[i]  : 0;
                var b = i < right.Length ? right[i] : 0;
                if (a != b) return a.CompareTo(b);
            }

            if (leftPre.Length == 0 && rightPre.Length == 0) return 0;
            if (leftPre.Length == 0) return 1;   // release > prerelease
            if (rightPre.Length == 0) return -1;
            return string.CompareOrdinal(leftPre, rightPre);
        }

        private static (int[] segments, string prerelease) Parse(string? version)
        {
            if (string.IsNullOrWhiteSpace(version)) return (Array.Empty<int>(), "");

            var core = version!.Trim().Split('+')[0];          // drop build metadata
            var dash = core.IndexOf('-');
            var prerelease = dash < 0 ? "" : core.Substring(dash + 1);
            if (dash >= 0) core = core.Substring(0, dash);

            // A non-numeric segment yields -1, so a junk folder name sorts below any real version.
            var segments = core.Split('.').Select(s => int.TryParse(s, out var n) ? n : -1).ToArray();
            return (segments, prerelease);
        }
    }

    /// <summary>
    /// The NuGet global-packages cache roots, mirroring how h5's <c>GetPackagesCacheFolder</c>
    /// resolves the cache: the <c>NUGET_PACKAGES</c> override first (expanded), then the
    /// per-platform user-profile default (<c>%userprofile%\.nuget\packages</c> on Windows,
    /// <c>~/.nuget/packages</c> elsewhere).
    /// </summary>
    private static System.Collections.Generic.IEnumerable<string> NuGetRoots()
    {
        var env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(env))
            yield return Path.GetFullPath(Environment.ExpandEnvironmentVariables(env));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            yield return Path.GetFullPath(Environment.ExpandEnvironmentVariables(@"%userprofile%\.nuget\packages"));
        else
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        yield return "/root/.nuget/packages";
    }

    /// <summary>
    /// Metadata tokens of the Transpose.dll methods that carry no IL body and are not abstract —
    /// i.e. C# <c>extern</c> methods/constructors whose behaviour is supplied by a
    /// hand-written JS runtime file (e.g. <c>Regex</c>). Transpose's <c>OverloadsCollection</c>
    /// excludes these from a non-external type's overload set, so they receive no
    /// <c>$N</c> suffix (matching the single dispatching name in the hand-written JS).
    /// </summary>
    public static HashSet<int> NoBodyMethodTokens
    {
        get
        {
            if (_noBodyTokens is not null) return _noBodyTokens;
            var set = new HashSet<int>();
            using (var fs = File.OpenRead(TransposeDllPath))
            using (var pe = new PEReader(fs))
            {
                var mr = pe.GetMetadataReader();
                foreach (var handle in mr.MethodDefinitions)
                {
                    var md = mr.GetMethodDefinition(handle);
                    var isAbstract = (md.Attributes & MethodAttributes.Abstract) != 0;
                    if (md.RelativeVirtualAddress == 0 && !isAbstract)
                        set.Add(MetadataTokens.GetToken(handle));
                }
            }
            return _noBodyTokens = set;
        }
    }

    /// <summary>
    /// The simple assembly name of a reference file, or null if it cannot be read as one. Used to
    /// recognise a second copy of the base library among a project's resolved references — see
    /// <c>CompilationBuilder.GetReferenceAssemblies</c> for why one is fatal.
    /// </summary>
    public static string? AssemblySimpleName(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata) return null;
            var mr = pe.GetMetadataReader();
            return mr.IsAssembly ? mr.GetString(mr.GetAssemblyDefinition().Name) : null;
        }
        catch (BadImageFormatException) { return null; }
        catch (IOException) { return null; }
    }

    /// <summary>The embedded Transpose JavaScript runtime (tps.js), read once from Transpose.dll.</summary>
    public static string RuntimeJs
    {
        get
        {
            if (_runtimeJs is not null) return _runtimeJs;
            var asm = Assembly.LoadFrom(TransposeDllPath);
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.Equals("tps.js", StringComparison.OrdinalIgnoreCase))
                ?? asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("tps.js", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("tps.js resource not found in Transpose.dll.");
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            _runtimeJs = reader.ReadToEnd();
            return _runtimeJs;
        }
    }
}
