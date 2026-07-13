using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace H5.Translator.Roslyn;

/// <summary>
/// Locates the H5 reference assembly (H5.dll) and extracts the embedded H5
/// JavaScript runtime (h5.js) so emitted code can run against the real H5 runtime.
/// </summary>
public static class H5Assemblies
{
    private static string? _h5DllPath;
    private static string? _runtimeJs;

    /// <summary>Path to H5.dll, discovered from the NuGet global-packages cache (overridable).</summary>
    public static string H5DllPath
    {
        get => _h5DllPath ??= Discover();
        set => _h5DllPath = value;
    }

    private static string Discover()
    {
        // Allow explicit override.
        var env = Environment.GetEnvironmentVariable("H5_DLL_PATH");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

        var candidates =
            from root in NuGetRoots()
            let pkg = Path.Combine(root, "h5")
            where Directory.Exists(pkg)
            from versionDir in Directory.GetDirectories(pkg)
            let dll = Path.Combine(versionDir, "lib", "netstandard2.0", "H5.dll")
            where File.Exists(dll)
            orderby versionDir
            select dll;

        var found = candidates.LastOrDefault();
        if (found is null)
        {
            throw new InvalidOperationException(
                "Could not locate H5.dll in the NuGet packages cache. Set the H5_DLL_PATH environment variable.");
        }
        return found;
    }

    private static System.Collections.Generic.IEnumerable<string> NuGetRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".nuget", "packages");
        var env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(env)) yield return env;
        yield return "/root/.nuget/packages";
    }

    /// <summary>The embedded H5 JavaScript runtime (h5.js), read once from H5.dll.</summary>
    public static string RuntimeJs
    {
        get
        {
            if (_runtimeJs is not null) return _runtimeJs;
            var asm = Assembly.LoadFrom(H5DllPath);
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.Equals("h5.js", StringComparison.OrdinalIgnoreCase))
                ?? asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("h5.js", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("h5.js resource not found in H5.dll.");
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            _runtimeJs = reader.ReadToEnd();
            return _runtimeJs;
        }
    }
}
