using System.Text;
using System.Text.Json;
using Mono.Cecil;

namespace Transpose.Compiler;

/// <summary>One embedded Transpose resource: the file bytes plus its manifest entry. <see cref="Output"/>
/// is the output subdirectory a consumer extracts it to (null for a top-level script).</summary>
internal sealed record EmbeddedItem(string Name, byte[] Content, string? Output, bool Load = true);

/// <summary>
/// Embeds the compiled JavaScript (and tps.json resource files) into a .NET assembly as private
/// manifest resources, alongside an <c>Transpose.Resources.json</c> manifest listing them — exactly the
/// shape the existing compiler produces and that <see cref="OutputBuilder"/> extracts when the
/// assembly is referenced. This is the "produce a distributable package" half of the protocol.
/// </summary>
internal static class ResourceEmbedder
{
    private const string ManifestName = "Transpose.Resources.json";
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>True if the assembly already carries the embedded Transpose resource manifest — i.e. it
    /// was produced as an Transpose package (with its JS embedded), not a plain csc build. Used to decide
    /// whether a referenced project's DLL needs to be (re)built by the translator.</summary>
    public static bool HasManifest(string assemblyPath)
    {
        try
        {
            using var asm = AssemblyDefinition.ReadAssembly(assemblyPath);
            return asm.MainModule.Resources.Any(r => r.Name == ManifestName);
        }
        catch { return false; }
    }

    public static void Embed(string assemblyPath, IReadOnlyList<EmbeddedItem> items, IEnumerable<string>? referencePaths = null)
    {
        // Cecil resolves referenced assemblies when it re-serializes metadata on Write() — e.g. to
        // determine the underlying type of a parameter's default value whose type is a referenced
        // enum. The default resolver only searches the assembly's own directory, so seed it with the
        // directories of the project's references (NuGet-cache DLLs, sibling project bin folders).
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!);
        if (referencePaths is not null)
            foreach (var dir in referencePaths.Select(p => Path.GetDirectoryName(Path.GetFullPath(p))!).Distinct())
                resolver.AddSearchDirectory(dir);

        // Read → modify → write back the assembly in place (ReadWrite so we can overwrite it).
        using var asm = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { ReadWrite = true, AssemblyResolver = resolver });
        var resources = asm.MainModule.Resources;

        void Replace(string name, byte[] bytes)
        {
            for (var i = resources.Count - 1; i >= 0; i--)
                if (resources[i].Name == name) resources.RemoveAt(i);
            resources.Add(new EmbeddedResource(name, ManifestResourceAttributes.Private, bytes));
        }

        foreach (var item in items)
            Replace(item.Name, item.Content);

        // The manifest lists { FileName, Name, Path } per resource (Parts omitted — we don't
        // split combined bundles). Serialized with the same field names the compiler reads.
        var manifest = items.Select(i => new
        {
            FileName = i.Name,
            Name = i.Name,
            Path = i.Output,
            Parts = (object?)null,
            Load = i.Load,   // false → copied to the site but not injected into index.html (.dontload)
        }).ToArray();
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        Replace(ManifestName, Utf8NoBom.GetBytes(json));

        asm.Write();
    }
}
