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

    /// <summary>Path to Transpose.dll, discovered from the NuGet global-packages cache (overridable).</summary>
    public static string TransposeDllPath
    {
        get => _tpsDllPath ??= Discover();
        set => _tpsDllPath = value;
    }

    private static string Discover()
    {
        // Allow explicit override.
        var env = Environment.GetEnvironmentVariable("TRANSPOSE_DLL_PATH");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

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
            orderby Path.GetFileName(versionDir)
            select dll;

        var found = candidates.LastOrDefault();
        if (found is null)
        {
            throw new InvalidOperationException(
                "Could not locate Transpose.dll in the NuGet packages cache. Set the TRANSPOSE_DLL_PATH environment variable.");
        }
        return found;
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
