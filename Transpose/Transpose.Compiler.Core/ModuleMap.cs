using System.Text.Json;

namespace Transpose.Compiler;

/// <summary>
/// Reads what a module-mode package publishes for the next compiler: its chunk map
/// (<c>Transpose.Modules.json</c> — emitted type name → the site-relative chunk file that defines it)
/// and its <c>[SkipTypeClustering]</c> member dependency sets, both embedded by
/// <see cref="ResourceEmbedder"/>.
///
/// The chunk map is what a site build resolves a package's type placeholders through
/// (<see cref="ModuleLinker"/>): a reference into a library has to become an <c>import</c> of the
/// chunk defining that type, or it would resolve to the library's stub — and a stub cannot be
/// resolved synchronously, which is exactly the failure the module runtime reports loudly rather
/// than papers over.
/// </summary>
internal static class ModuleMap
{
    /// <summary>One assembly's published chunk map — emitted type name → the site-relative chunk file
    /// that defines it — or null when it was not built as modules. This is the mapping a consuming
    /// build resolves a package's type placeholders through (see <see cref="ModuleLinker"/>).</summary>
    public static Dictionary<string, string>? ReadOne(string referencePath)
    {
        var json = TryReadResource(referencePath, ResourceEmbedder.ModuleMapName);
        if (json is null) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json); }
        catch (JsonException) { return null; }   // a malformed map is not worth failing a build over
    }

    /// <summary>The <c>[SkipTypeClustering]</c> member dependency sets of every reference that
    /// publishes them, merged. Same first-wins rule as <see cref="Read"/>; a documentation comment id
    /// identifies exactly one member, so a collision would mean two copies of the same assembly.</summary>
    public static Dictionary<string, List<string>> ReadSkipClusterDeps(IEnumerable<string> referencePaths)
    {
        var merged = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var dll in referencePaths)
        {
            var json = TryReadResource(dll, ResourceEmbedder.SkipClusterMapName);
            if (json is null) continue;
            try
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                if (map is null) continue;
                foreach (var kv in map) merged.TryAdd(kv.Key, kv.Value);
            }
            catch (JsonException) { /* as above: a malformed map must not fail a build */ }
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
