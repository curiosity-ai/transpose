using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace H5.Translator.Roslyn;

/// <summary>
/// Builds a <see cref="CSharpCompilation"/> from source, wiring up the reference
/// assemblies so the semantic model can bind the BCL. Targets C# Latest.
/// </summary>
public static class CompilationBuilder
{
    public const string DefaultAssemblyName = "App";

    public static CSharpCompilation Build(
        IEnumerable<(string path, string text)> sources,
        string assemblyName = DefaultAssemblyName,
        LanguageVersion languageVersion = LanguageVersion.Latest)
    {
        var parseOptions = new CSharpParseOptions(languageVersion)
            .WithFeatures(new[] { new KeyValuePair<string, string>("strict", "false") });

        var trees = sources
            .Select(s => CSharpSyntaxTree.ParseText(s.text, parseOptions, path: s.path))
            .ToList();

        var options = new CSharpCompilationOptions(
            OutputKind.ConsoleApplication,
            optimizationLevel: OptimizationLevel.Debug,
            allowUnsafe: true, // allowed by the compiler so we can detect+report it ourselves
            nullableContextOptions: NullableContextOptions.Annotations);

        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: trees,
            references: GetReferenceAssemblies(),
            options: options);
    }

    /// <summary>
    /// Resolves the reference assemblies from the currently running runtime's
    /// trusted platform assemblies. This gives the semantic model a complete
    /// view of the BCL for symbol/overload/conversion resolution.
    /// </summary>
    private static IReadOnlyList<MetadataReference> GetReferenceAssemblies()
    {
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? string.Empty;

        var refs = tpa
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Where(File.Exists)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        if (refs.Count == 0)
        {
            throw new InvalidOperationException(
                "Could not resolve any reference assemblies from TRUSTED_PLATFORM_ASSEMBLIES.");
        }

        return refs;
    }
}
