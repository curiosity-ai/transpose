using System.Text.Json;

namespace Transpose.Compiler;

/// <summary>
/// Reads the chunk maps that module-mode packages publish (<c>Transpose.Modules.json</c>, embedded
/// by <see cref="ResourceEmbedder"/>) and merges them into the single lookup the translator needs:
/// emitted type name → site-relative chunk file.
///
/// A consuming build uses it to turn a reference to a library type into an <c>import</c> of the
/// chunk that defines it. Without that the reference would resolve to the library's stub, and a stub
/// cannot be resolved synchronously — which is exactly the failure the module runtime is designed to
/// report loudly rather than paper over.
/// </summary>
internal static class ModuleMap
{
    /// <summary>Merges the chunk maps of every reference that has one. A later reference never
    /// overwrites an earlier entry: two assemblies claiming the same type name would be a name
    /// collision the compiler cannot resolve, and the first (dependency-order) reference wins,
    /// matching how the site build orders their JavaScript.</summary>
    public static Dictionary<string, string> Read(IEnumerable<string> referencePaths)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var dll in referencePaths)
        {
            var json = TryReadResource(dll, ResourceEmbedder.ModuleMapName);
            if (json is null) continue;
            try
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (map is null) continue;
                foreach (var kv in map) merged.TryAdd(kv.Key, kv.Value);
            }
            catch (JsonException) { /* a malformed map is not worth failing a build over */ }
        }
        return merged;
    }

    private static string? TryReadResource(string dllPath, string name)
    {
        try
        {
            // Read through Cecil, never Assembly.LoadFrom: loading would lock the file for the
            // process's lifetime and break the next rebuild of a referenced project.
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(dllPath);
            foreach (var r in asm.MainModule.Resources)
            {
                if (r is not Mono.Cecil.EmbeddedResource e || e.Name != name) continue;
                using var s = e.GetResourceStream();
                using var sr = new StreamReader(s);
                return sr.ReadToEnd();
            }
        }
        catch { /* not a readable assembly, or no such resource */ }
        return null;
    }
}
