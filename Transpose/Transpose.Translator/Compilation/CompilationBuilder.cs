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
        var parseOptions = ParseOptionsFor(languageVersion, preprocessorSymbols);

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
            metadataImportOptions: MetadataImportOptions.All,
            // Emit the assembly reproducibly: without this Roslyn stamps a fresh module MVID and a
            // wall-clock PE timestamp on every emit, so two compiles of identical sources produced
            // assemblies differing in 16 bytes. The emitted *JavaScript* was already reproducible; this
            // extends that to the DLL — which makes it diffable as a correctness gate, and means an
            // incremental build that reuses a cached assembly is indistinguishable from one that
            // re-emitted it. (Mono.Cecil preserves both stamps through the resource embed, so the
            // shipped DLL is byte-identical across builds; measured.)
            deterministic: true);
        
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
    /// The parse options a build uses. Exposed so anything that has to re-parse a single file
    /// *outside* a compilation — the incremental cache, hashing one file's declaration surface —
    /// produces the same tree this would, and therefore the same hash.
    /// </summary>
    public static CSharpParseOptions ParseOptionsFor(LanguageVersion languageVersion, IEnumerable<string>? preprocessorSymbols)
    {
        var parseOptions = new CSharpParseOptions(languageVersion)
            .WithFeatures(new[] { new KeyValuePair<string, string>("strict", "false") });
        if (preprocessorSymbols is not null)
            parseOptions = parseOptions.WithPreprocessorSymbols(preprocessorSymbols);
        return parseOptions;
    }

    /// <summary>Parses one source file exactly as <see cref="Build"/> would.</summary>
    public static SyntaxTree ParseOne(string path, string text, LanguageVersion languageVersion,
        IEnumerable<string>? preprocessorSymbols)
        => CSharpSyntaxTree.ParseText(
            Microsoft.CodeAnalysis.Text.SourceText.From(text, System.Text.Encoding.UTF8),
            ParseOptionsFor(languageVersion, preprocessorSymbols), path: path);

    /// <summary>
    /// References the Transpose assembly (Transpose.dll) as the sole BCL, exactly like the Transpose
    /// compiler. Transpose.dll redefines System.* with the [External]/[Name]/[Template]
    /// attributes that drive JavaScript emission and interop with the tps.js runtime.
    /// Any <paramref name="extraReferencePaths"/> (e.g. tps.core, tps.Newtonsoft.Json for a
    /// real project) are added alongside it.
    /// </summary>
    /// <summary>Assembly name of the base library. Its package id is Transpose.BCL, but the assembly
    /// it ships stays <c>Transpose</c> — that is what a reference has to be matched on.</summary>
    private const string BaseLibraryAssemblyName = "Transpose";

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

                // …and a SECOND copy of the base library at a different path, which is what a project
                // whose Transpose.BCL PackageReference is not the version TransposeAssemblies
                // discovered resolves to (a newer one in the NuGet cache, or a TRANSPOSE_DLL_PATH
                // pointing at a locally built runtime). Both would bind, and the damage is silent:
                // overload numbering asks whether a method has an IL body by looking its metadata
                // TOKEN up in a set read from TransposeDllPath (see TransposeNaming.HasNoBody), and a
                // token from the other file names an unrelated method. Members are then misread as
                // extern JS-backed ones and emitted under their bare, unsuffixed names, so a call
                // binds to whichever overload happens to hold that name — List&lt;T&gt;.Sort(Comparison&lt;T&gt;)
                // compiled to Sort(), sorting with the default comparer and throwing "Cannot compare
                // items" on the first element type that is not IComparable.
                if (string.Equals(TransposeAssemblies.AssemblySimpleName(path), BaseLibraryAssemblyName, StringComparison.Ordinal))
                    continue;

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

        var full = Path.GetFullPath(path);
        var info = new FileInfo(full);
        var stamp = (info.Length, info.LastWriteTimeUtc.Ticks);

        lock (_references)
        {
            if (_references.TryGetValue(full, out var cached) && cached.Stamp == stamp)
            {
                cached.LastUsed = ++_useCounter;
                return cached.Reference;
            }

            // CreateFromImage, not CreateFromFile: the reference is cached, and CreateFromFile keeps
            // the file mapped for as long as it lives — which is the very thing that made a rebuild of
            // a referenced project fail to write its own DLL (see OutputBuilder.AssemblyResources,
            // which avoids Assembly.LoadFrom for the same reason). Reading the bytes costs one copy
            // and leaves nothing holding the file.
            var reference = MetadataReference.CreateFromImage(File.ReadAllBytes(full), filePath: full);
            // Keyed by path, so a rebuilt assembly replaces its predecessor rather than accumulating:
            // a long-running host recompiles the same paths over and over.
            _references[full] = new Cached(stamp, reference, ++_useCounter);

            // A build binds against a handful of assemblies and asks for the same ones every time, so
            // the cap is only ever reached by a host that compiles unrelated projects one after
            // another — the test suites, which build a package DLL under a fresh temp path per test.
            // Dropping the least recently used entry lets Roslyn release what it decoded from it,
            // which is the whole point; the working set of an ordinary build never comes close.
            while (_references.Count > MaxCachedReferences)
            {
                var oldest = _references.OrderBy(e => e.Value.LastUsed).First().Key;
                _references.Remove(oldest);
            }

            return reference;
        }
    }

    /// <summary>How many assemblies stay decoded at once — comfortably more than any one project
    /// binds against, so a real build never evicts anything.</summary>
    private const int MaxCachedReferences = 64;

    private sealed class Cached((long Length, long Ticks) stamp, MetadataReference reference, long lastUsed)
    {
        public (long Length, long Ticks) Stamp { get; } = stamp;
        public MetadataReference Reference { get; } = reference;
        public long LastUsed { get; set; } = lastUsed;
    }

    private static long _useCounter;

    /// <summary>
    /// One <see cref="MetadataReference"/> per assembly file, reused across compilations.
    ///
    /// <para>
    /// Roslyn decodes a reference's metadata — and caches the symbols it builds from it — against the
    /// <see cref="MetadataReference"/> INSTANCE. A fresh instance per compilation therefore re-reads
    /// and re-decodes the whole assembly, and holds the result in native memory the GC cannot account
    /// for or reclaim: measured at ~12 MB per compilation for the 10.4 MB base library alone, which is
    /// what walked the translator test suite's host up to the container's 13 GB limit and had it
    /// OOM-killed partway through. It matters wherever one process compiles more than once — the test
    /// suites, <c>tps --watch</c>, and <c>Transpose.Compiler.Library</c> hosts like
    /// <c>curiosity-cli serve --watch</c>. A one-shot <c>tps</c> build compiles once and neither gains
    /// nor loses.
    /// </para>
    ///
    /// <para>
    /// The stamp (length + last write time) is what keeps that sound in exactly the case that makes
    /// caching tempting to get wrong: watch mode rebuilds a referenced project's DLL between
    /// compilations, and the next build must bind against the new one.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, Cached> _references = new(StringComparer.Ordinal);
}
