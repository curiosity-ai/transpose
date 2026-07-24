using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Transpose.Translator;

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
        LanguageVersion languageVersion = LanguageVersion.Latest,
        IEnumerable<string>? extraReferencePaths = null,
        IEnumerable<string>? preprocessorSymbols = null,
        bool selfContainedBcl = false)
    {
        var parseOptions = new CSharpParseOptions(languageVersion)
            .WithFeatures(new[] { new KeyValuePair<string, string>("strict", "false") });
        if (preprocessorSymbols is not null)
            parseOptions = parseOptions.WithPreprocessorSymbols(preprocessorSymbols);

        // Give each source text an explicit encoding: emitting the assembly with embedded debug
        // information (as the package build does) requires it — Roslyn otherwise reports CS8055
        // ("Cannot emit debug information for a source text without encoding").
        //
        // Parsing is embarrassingly parallel (each file is independent) and a real project has
        // hundreds of files, so fan it out. The result must keep the input order: the emitted JS is
        // ordered by declaration, and reordering the trees would reorder the bundle for no reason.
        var sourceList = sources as IList<(string path, string text)> ?? sources.ToList();
        var treeArray = new SyntaxTree[sourceList.Count];
        Parallel.For(0, sourceList.Count, i =>
        {
            var (path, text) = sourceList[i];
            treeArray[i] = CSharpSyntaxTree.ParseText(
                Microsoft.CodeAnalysis.Text.SourceText.From(text, System.Text.Encoding.UTF8),
                parseOptions, path: path);
        });
        var trees = treeArray;

        // Nullable reference types only exist from C# 8; enabling the annotations context
        // under an earlier language version (e.g. a project pinned to 7.2) is a hard error.
        var effectiveLang = languageVersion == LanguageVersion.Latest ? LanguageVersion.Latest : languageVersion;
        var nullable = effectiveLang != LanguageVersion.Latest && effectiveLang < LanguageVersion.CSharp8
            ? NullableContextOptions.Disable
            : NullableContextOptions.Annotations;

        var options = new CSharpCompilationOptions(
            OutputKind.ConsoleApplication,
            optimizationLevel: OptimizationLevel.Debug,
            allowUnsafe: true, // allowed by the compiler so we can detect+report it ourselves
            nullableContextOptions: nullable,
            // Import non-public members of referenced assemblies. A referenced Transpose-compiled package
            // (built with --emit-package) numbers its overloads (e.g. $ctorN) over its FULL member
            // set including private ones; the consumer must see the same set for its call sites to
            // resolve to the same JS names.
            metadataImportOptions: MetadataImportOptions.All);
        
        // The base runtime library (Transpose.BCL) *defines* the BCL (System.Object, …), so it is
        // compiled self-contained with no base reference — like compiling corlib. Every other
        // project references Transpose.dll as its whole BCL.
        var references = selfContainedBcl
            ? (extraReferencePaths ?? System.Array.Empty<string>())
                .Where(File.Exists)
                .Select(ReadReference)
                .ToList()
            : GetReferenceAssemblies(extraReferencePaths);

        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: trees,
            references: references,
            options: options);
    }

    /// <summary>
    /// References the Transpose assembly (Transpose.dll) as the sole BCL, exactly like the Transpose
    /// compiler. Transpose.dll redefines System.* with the [External]/[Name]/[Template]
    /// attributes that drive JavaScript emission and interop with the tps.js runtime.
    /// Any <paramref name="extraReferencePaths"/> (e.g. tps.core, tps.Newtonsoft.Json for a
    /// real project) are added alongside it.
    /// </summary>
    private static IReadOnlyList<MetadataReference> GetReferenceAssemblies(IEnumerable<string>? extraReferencePaths)
    {
        var refs = new List<MetadataReference> { ReadReference(TransposeAssemblies.TransposeDllPath) };
        if (extraReferencePaths is not null)
        {
            var tpsDll = Path.GetFullPath(TransposeAssemblies.TransposeDllPath);
            foreach (var path in extraReferencePaths)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                if (string.Equals(Path.GetFullPath(path), tpsDll, StringComparison.OrdinalIgnoreCase)) continue;
                refs.Add(ReadReference(path));
            }
        }
        return refs;
    }

    /// <summary>
    /// Reads one reference assembly into a <see cref="MetadataReference"/>, logging its full path to
    /// the compilation log first so a build records exactly which assembly file was read (and from
    /// where — the NuGet cache, a sibling bin folder, a <c>--reference</c> path). Every reference the
    /// compilation binds against — the base Transpose.dll, package DLLs, and the self-contained BCL's
    /// own inputs — goes through here.
    /// </summary>
    private static MetadataReference ReadReference(string path)
    {
        CompileProgress.Report($"reading assembly {Path.GetFullPath(path)}");
        return MetadataReference.CreateFromFile(path);
    }
}
