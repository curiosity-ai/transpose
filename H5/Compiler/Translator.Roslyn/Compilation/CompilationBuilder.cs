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
    /// References the H5 assembly (H5.dll) as the sole BCL, exactly like the H5
    /// compiler. H5.dll redefines System.* with the [External]/[Name]/[Template]
    /// attributes that drive JavaScript emission and interop with the h5.js runtime.
    /// </summary>
    private static IReadOnlyList<MetadataReference> GetReferenceAssemblies()
    {
        return new[] { (MetadataReference)MetadataReference.CreateFromFile(H5Assemblies.H5DllPath) };
    }
}
