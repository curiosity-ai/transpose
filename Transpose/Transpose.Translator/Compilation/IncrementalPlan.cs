using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Transpose.Translator;

/// <summary>
/// What a build is allowed to reuse from the previous one, and what it produced for the next.
///
/// The translator itself owns no cache and does no file I/O: the CLI (<c>Transpose.Compiler</c>'s
/// <c>BuildCache</c>) decides what is still valid, hands the reusable pieces over in a plan, and
/// persists whatever the build reports back here. That keeps the reuse *policy* — which is where the
/// soundness argument lives — in one place, and leaves the translator with a mechanical rule:
///
///   * a type whose declaring files are all unchanged reuses its cached JavaScript verbatim,
///   * a tree that is unchanged is neither rescanned nor re-diagnosed,
///   * the reflection-metadata block and the .NET assembly are reused when the caller says so.
///
/// The plan is only ever populated when the *declaration surface* of every file is unchanged — i.e.
/// the edit touched method/accessor bodies only. That is what makes the rule above sound:
///
///   * emitted JavaScript for a type is a function of that type's own syntax plus the *declarations*
///     it binds against (member names and overload numbering, <c>[Template]</c>/<c>[Name]</c>
///     attributes, constants, base types). None of that can move when only bodies changed elsewhere,
///     and <see cref="NameMangler"/> derives every name from the symbol alone — there is no global
///     counter whose value depends on how many types were emitted.
///   * a body can only produce diagnostics in its own file, so an unchanged file's diagnostics
///     (Roslyn's and the unsupported-feature scan's) are unchanged too.
///   * the reflection metadata and a metadata-only assembly describe declarations only.
/// </summary>
public sealed class IncrementalPlan
{
    /// <summary>Absolute paths of the source files whose text changed since the cached build. Only
    /// these are rescanned, re-diagnosed, and have their types re-emitted.</summary>
    public required IReadOnlyCollection<string> ChangedSources { get; init; }

    /// <summary>Cached JavaScript per type, keyed by <see cref="TypeKey"/>.</summary>
    public required IReadOnlyDictionary<string, string> TypeJs { get; init; }

    /// <summary>The previous build's declaration-surface hashes. An unchanged file's hash is by
    /// definition still its hash, so only the changed files are re-hashed and the rest are carried
    /// forward — otherwise the cache would re-walk every file on every build to produce data it
    /// already has.</summary>
    public IReadOnlyDictionary<string, string>? PreviousDeclarationHashes { get; init; }

    /// <summary>The cached standalone reflection-metadata script (<c>reflection.target = file</c>),
    /// or null when the caller wants it rebuilt.</summary>
    public string? MetadataScript { get; init; }

    /// <summary>The cached inline reflection-metadata block (<c>reflection.target = inline</c>), or
    /// null when the caller wants it rebuilt.</summary>
    public string? InlineMetadata { get; init; }

    /// <summary>
    /// The unsupported-feature scanner's pre-computed filter — the simple names of every type in a
    /// denied namespace, across the BCL, every referenced package and the project's own types (see
    /// <c>UnsupportedFeatureScanner.CollectDeniedSimpleNames</c>). Building it means walking the merged
    /// global namespace of every reference, which on a real project costs more than scanning the files
    /// themselves, and it is a function of the references plus the declaration surface — both fixed on
    /// a body-only edit. Null when the caller wants it rebuilt.
    /// </summary>
    public IReadOnlyCollection<string>? DeniedSimpleNames { get; init; }

    /// <summary>The cached .NET assembly to reuse instead of running <c>Compilation.Emit</c>. Only
    /// ever supplied for a metadata-only assembly, whose content is a function of the declarations
    /// alone (see <c>ResolvedProject.MetadataOnlyAssembly</c>).</summary>
    public byte[]? AssemblyBytes { get; init; }

    // ---- what this build produced, for the caller to persist -------------------------------------

    /// <summary>Every type's JavaScript as it went into the bundle — reused and re-emitted alike — so
    /// the caller can write the complete cache back without holding the previous one.</summary>
    public ConcurrentDictionary<string, string> FinalTypeJs { get; } = new(StringComparer.Ordinal);

    /// <summary>The type keys in bundle order.</summary>
    public List<string> FinalOrder { get; } = new();

    /// <summary>The inline metadata block this build used, cached or freshly built.</summary>
    public string? FinalInlineMetadata { get; set; }

    /// <summary>The scanner's denied-name filter this build used, cached or freshly built.</summary>
    public IReadOnlyCollection<string>? FinalDeniedSimpleNames { get; set; }

    private int _reused;
    private int _reemitted;

    /// <summary>How many types took their JavaScript from the cache.</summary>
    public int ReusedTypes => _reused;

    /// <summary>How many types were re-emitted.</summary>
    public int ReemittedTypes => _reemitted;

    private HashSet<string>? _changedSet;

    private HashSet<string> ChangedSet => _changedSet ??=
        new HashSet<string>(ChangedSources, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a syntax tree has to be rescanned/re-diagnosed this build.</summary>
    public bool IsChanged(SyntaxTree tree) => ChangedSet.Contains(tree.FilePath);

    /// <summary>
    /// The cached JavaScript for a type, or null when it must be re-emitted: either one of the files
    /// declaring it changed (a <c>partial</c> type is declared in several, and every one of them
    /// counts), or the cache has never seen it.
    /// </summary>
    public string? TryReuse(INamedTypeSymbol type)
    {
        foreach (var reference in type.OriginalDefinition.DeclaringSyntaxReferences)
            if (ChangedSet.Contains(reference.SyntaxTree.FilePath))
                return null;
        return TypeJs.TryGetValue(TypeKey(type), out var js) ? js : null;
    }

    internal void RecordReused() => System.Threading.Interlocked.Increment(ref _reused);
    internal void RecordReemitted() => System.Threading.Interlocked.Increment(ref _reemitted);

    /// <summary>
    /// A stable identity for a type across compiler processes. The fully-qualified display form
    /// distinguishes namespaces, containing types and generic arity (<c>Foo.Bar&lt;T&gt;</c> versus
    /// <c>Foo.Bar&lt;T, U&gt;</c>), which is exactly what the emitted JavaScript keys off.
    /// </summary>
    public static string TypeKey(INamedTypeSymbol type)
        => type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>
    /// A hash per source file of everything in it *except* method/accessor bodies — the file's
    /// declaration surface. Two builds whose files all hash the same declared exactly the same types,
    /// members, signatures, attributes, constants and initializers, so every conclusion in the class
    /// comment above holds; any difference at all forces a full rebuild.
    ///
    /// This is deliberately a hash of *text* rather than of a normalised syntax model: it over-reports
    /// (reformatting a declaration counts as a change) and never under-reports, which is the right
    /// direction for a cache. Roslyn's own equivalent, <c>SyntaxNode.IsEquivalentTo(other,
    /// topLevel: true)</c>, needs both trees in memory — a cache only has a hash of the old one.
    /// </summary>
    public static Dictionary<string, string> DeclarationHashes(IEnumerable<SyntaxTree> trees, IncrementalPlan? plan = null)
    {
        var known = plan?.PreviousDeclarationHashes;
        var result = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        Parallel.ForEach(trees, tree =>
        {
            if (known is not null && !plan!.IsChanged(tree) && known.TryGetValue(tree.FilePath, out var cached))
                result[tree.FilePath] = cached;
            else
                result[tree.FilePath] = DeclarationHash(tree);
        });
        return new Dictionary<string, string>(result, StringComparer.Ordinal);
    }

    /// <summary>The declaration-surface hash of one tree — see <see cref="DeclarationHashes"/>.</summary>
    public static string DeclarationHash(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        var text = tree.GetText();

        // The spans to leave out: every executable body. A body cannot introduce, remove or rename a
        // member, so a change confined to one is invisible to any other file.
        var bodies = new List<TextSpan>();
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                // Methods, constructors, operators, destructors.
                case BaseMethodDeclarationSyntax m:
                    Add(m.Body); Add(m.ExpressionBody);
                    break;
                // get/set/init/add/remove.
                case AccessorDeclarationSyntax a:
                    Add(a.Body); Add(a.ExpressionBody);
                    break;
                // `int P => expr;` — the expression *is* the getter body.
                case PropertyDeclarationSyntax p:
                    Add(p.ExpressionBody);
                    break;
                case IndexerDeclarationSyntax ix:
                    Add(ix.ExpressionBody);
                    break;
            }
        }
        // Field initializers, default parameter values, attribute arguments and base-list arguments are
        // *not* excluded: a constant folds into other files' output, an initializer runs as part of the
        // type's construction, and an attribute argument steers emission ([Template], [Name]).

        bodies.Sort(static (x, y) => x.Start.CompareTo(y.Start));

        using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        var full = text.ToString();
        var at = 0;
        foreach (var span in bodies)
        {
            // Nested bodies (a local function inside a method) are already inside an earlier span.
            if (span.Start < at) continue;
            AppendUtf8(sha, full.AsSpan(at, span.Start - at));
            AppendUtf8(sha, " body ".AsSpan()); // a marker, so removing a body is not the same as emptying it
            at = span.End;
        }
        AppendUtf8(sha, full.AsSpan(at));
        return Convert.ToHexString(sha.GetHashAndReset());

        void Add(SyntaxNode? node) { if (node is not null) bodies.Add(node.Span); }
    }

    private static void AppendUtf8(System.Security.Cryptography.IncrementalHash sha, ReadOnlySpan<char> chars)
    {
        if (chars.Length == 0) return;
        var bytes = new byte[System.Text.Encoding.UTF8.GetMaxByteCount(chars.Length)];
        var written = System.Text.Encoding.UTF8.GetBytes(chars, bytes);
        sha.AppendData(bytes, 0, written);
    }
}
