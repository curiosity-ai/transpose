using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

/// <summary>
/// Walks the syntax trees and reports language features that cannot run in a
/// browser environment as compilation errors (TransposeR0001). Detection is by syntax
/// for language constructs and by bound symbol namespace for runtime APIs.
/// </summary>
internal sealed class UnsupportedFeatureScanner : CSharpSyntaxWalker
{
    private readonly SemanticModel _model;
    private readonly List<Diagnostic> _diagnostics;

    /// <summary>Simple names of the types this scanner would reject (see <see cref="DeniedNamespaces"/>),
    /// computed once per compilation. <see cref="VisitIdentifierName"/> fires on nearly every token in
    /// the source, and asking the semantic model to bind each one is by far the scanner's dominant
    /// cost; an identifier whose *text* is not in this set cannot possibly name a denied type, so it
    /// needs no semantic query at all. See <see cref="CollectDeniedSimpleNames"/> for why this is
    /// exact rather than a heuristic.</summary>
    private readonly HashSet<string> _deniedSimpleNames;

    /// <summary>Extra identifier texts this file must resolve semantically, contributed by its own
    /// using directives: an alias's name (<c>using MyFile = System.IO.File;</c>) and the member names
    /// a static import brings into scope (<c>using static System.IO.File;</c>) — the only ways a
    /// denied type can be referenced without its own simple name appearing as an identifier. Null
    /// until such a directive is seen, which keeps the hot path in <see cref="VisitIdentifierName"/>
    /// down to a single set lookup for the overwhelming majority of files.</summary>
    private HashSet<string>? _aliasedNames;

    /// <summary>The type symbols whose emitted member names have already been checked for collisions,
    /// shared across the parallel walk. A partial type is declared in more than one file (and more
    /// than once per file for nested partials), but <c>GetMembers()</c> already returns the union, so
    /// it needs checking exactly once. Guarded by itself — contention is negligible, since it is
    /// touched once per type declaration rather than per node.</summary>
    private readonly HashSet<INamedTypeSymbol> _typesChecked;

    private UnsupportedFeatureScanner(SemanticModel model, List<Diagnostic> diagnostics, HashSet<string> deniedSimpleNames,
        HashSet<INamedTypeSymbol> typesChecked)
    {
        _model = model;
        _diagnostics = diagnostics;
        _deniedSimpleNames = deniedSimpleNames;
        _typesChecked = typesChecked;
    }

    /// <summary>
    /// Scans every syntax tree for browser-incompatible constructs.
    ///
    /// <paramref name="models"/> is the same semantic-model cache the JS emitter will use. Sharing it
    /// is what makes this scan close to free on a real build: a Roslyn <see cref="SemanticModel"/>
    /// retains the bound form of every member it is asked about, so a member this scan binds (to
    /// resolve, say, the inferred type of a <c>var</c>) is already bound when the emitter reaches it.
    /// Pass null to scan against throw-away models — only useful when there is no emit to follow.
    /// </summary>
    /// <param name="trees">The trees to scan; null scans the whole compilation. An incremental build
    /// passes only the files whose text changed — an unsupported construct is a property of the file it
    /// appears in, so an unchanged file's verdict from the cached build still holds.</param>
    /// <param name="incremental">The build's incremental plan, if any: supplies the cached
    /// denied-name filter (whose inputs — the references and the declaration surface — are fixed on a
    /// body-only edit) and receives the one this build used, for the next build to reuse.</param>
    public static IReadOnlyList<Diagnostic> Scan(CSharpCompilation compilation, TreeModel? models = null,
        IEnumerable<SyntaxTree>? trees = null, IncrementalPlan? incremental = null)
    {
        var deniedSimpleNames = incremental?.DeniedSimpleNames is { } cachedNames
            ? new HashSet<string>(cachedNames, StringComparer.Ordinal)
            : PhaseTimings.Measure("  ├ collect denied type names", () => CollectDeniedSimpleNames(compilation));
        if (incremental is not null) incremental.FinalDeniedSimpleNames = deniedSimpleNames;
        models ??= new TreeModel(compilation);

        var typesChecked = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        var allDiagnostics = new List<List<Diagnostic>>();
        // Measured separately from the enclosing phase, because the two are wildly different things and
        // the difference is the most useful number in an incremental build's timing table: this walk is
        // proportional to the files actually being scanned (a few ms for one file), while whatever is
        // left over in the parent phase is Roslyn building the compilation's symbol tables and importing
        // the reference metadata — a fixed per-process cost that lands on whichever phase binds first,
        // and the floor no on-disk cache can get under. See TODO.incremental.md.
        PhaseTimings.Measure("  ├ walk files", () =>
        Parallel.ForEach(trees ?? compilation.SyntaxTrees, tree =>
        {
            var diagnostics = new List<Diagnostic>();

            var model = models.SemanticModelFor(tree);
            var scanner = new UnsupportedFeatureScanner(model, diagnostics, deniedSimpleNames, typesChecked);
            scanner.Visit(tree.GetRoot());

            if (diagnostics.Count > 0)
            {
                lock (allDiagnostics)
                {
                    allDiagnostics.Add(diagnostics);
                }
            }
        }));
        return allDiagnostics.SelectMany(d => d).ToArray();
    }

    /// <summary>
    /// The simple names of every type that <see cref="CheckDeniedType"/> would report — i.e. every
    /// type in a denied namespace that is not on the allowed list. Enumerated from the compilation's
    /// merged global namespace, so it covers the Transpose BCL, every referenced package, and source
    /// types alike.
    ///
    /// This is a *sound* filter, not a heuristic: a syntactic identifier can only bind to a denied
    /// type if the identifier's text equals that type's name — the one exception being an alias
    /// (<c>using MyFile = System.IO.File;</c>) or a static import (<c>using static System.IO.File;</c>),
    /// which name the denied type in the using directive itself and are checked there
    /// (<see cref="VisitUsingDirective"/>).
    /// </summary>
    private static HashSet<string> CollectDeniedSimpleNames(CSharpCompilation compilation)
    {
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            // Native-sized integers are matched by identifier text, not by namespace.
            "nint", "nuint",
            // `var` binds to the inferred type, which may itself be denied even when that type's name
            // appears nowhere in the file (`var s = Factory.OpenFile();`). It is the one identifier
            // text that can name any type at all, so it always gets a semantic query.
            // Measured: including it moves ~0.8s from the JS emit into the scan and leaves the total
            // unchanged, because the two share semantic models — the bind is prepaid, not extra.
            "var",
        };

        foreach (var (segments, _) in DeniedNamespaces)
        {
            var ns = ResolveNamespace(compilation.GlobalNamespace, segments);
            if (ns is null) continue;
            AddTypeNames(ns, names);
        }
        return names;

        static INamespaceSymbol? ResolveNamespace(INamespaceSymbol root, string[] segments)
        {
            var cur = root;
            foreach (var segment in segments)
            {
                cur = cur.GetNamespaceMembers().FirstOrDefault(n => string.Equals(n.Name, segment, StringComparison.Ordinal));
                if (cur is null) return null;
            }
            return cur;
        }

        static void AddTypeNames(INamespaceSymbol ns, HashSet<string> names)
        {
            // System.Threading.Tasks is the supported async model — its types are allowed wholesale,
            // so keeping their names out of the filter spares a semantic query on every `Task`.
            if (NamespaceMatches(ns, TasksNamespaceSegments)) return;

            foreach (var type in ns.GetTypeMembers())
                AddTypeAndNested(type, names);
            foreach (var child in ns.GetNamespaceMembers())
                AddTypeNames(child, names);
        }

        static void AddTypeAndNested(INamedTypeSymbol type, HashSet<string> names)
        {
            if (!AllowedThreadingTypes.Contains(type.ConstructUnboundGenericTypeSafeName()))
                names.Add(type.Name);
            foreach (var nested in type.GetTypeMembers())
                AddTypeAndNested(nested, names);
        }
    }

    private void Report(SyntaxNode node, string message)
        => _diagnostics.Add(Diagnostics.Create(Diagnostics.Unsupported, node.GetLocation(), message));

    // ---- Pointers ----------------------------------------------------------

    public override void VisitPointerType(PointerTypeSyntax node)
    {
        Report(node, "Pointers are not supported in the browser environment.");
        base.VisitPointerType(node);
    }

    public override void VisitGlobalStatement(GlobalStatementSyntax node)
    {
        Report(node, "Top-level statements are not supported; use an explicit class with a Main method.");
        base.VisitGlobalStatement(node);
    }

    public override void VisitUsingDirective(UsingDirectiveSyntax node)
    {
        if (node.GlobalKeyword.RawKind != 0)
            Report(node, "Global usings are not supported; add per-file using directives instead.");

        // An alias (`using MyFile = System.IO.File;`) or a static import (`using static System.IO.File;`)
        // is the only way a denied type can be referenced without its own simple name appearing as an
        // identifier — which is exactly what the fast path in VisitIdentifierName relies on. Rather
        // than report the directive (that would reject an *unused* import, which the scanner never
        // did before), widen this file's interesting-name set so the usage site is still resolved and
        // reported where it occurs. A plain namespace import (`using System.IO;`) needs nothing:
        // importing a namespace is harmless, and a type used from it appears by its own name.
        //
        // A using directive always precedes the members that can see it, so widening the set here —
        // mid-walk — is in time for every usage.
        if (node.Alias is not null)
        {
            (_aliasedNames ??= new HashSet<string>(StringComparer.Ordinal)).Add(node.Alias.Name.Identifier.ValueText);
        }
        else if (node.StaticKeyword.RawKind != 0 && node.NamespaceOrType is { } staticTarget)
        {
            // A static import puts the type's members in scope under their own names, so those names
            // become resolvable identifiers that can reach a denied type.
            if (_model.GetSymbolInfo(staticTarget).Symbol is INamedTypeSymbol imported
                && DeniedNamespaceMessage(imported) is not null)
            {
                var set = _aliasedNames ??= new HashSet<string>(StringComparer.Ordinal);
                foreach (var member in imported.GetMembers()) set.Add(member.Name);
            }
        }

        base.VisitUsingDirective(node);
    }

    public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.AddressOfExpression) || node.IsKind(SyntaxKind.PointerIndirectionExpression))
        {
            Report(node, "Pointers are not supported in the browser environment.");
        }
        base.VisitPrefixUnaryExpression(node);
    }

    // ---- unsafe / fixed ----------------------------------------------------

    public override void VisitUnsafeStatement(UnsafeStatementSyntax node)
    {
        Report(node, "Unsafe code is not supported in the browser environment.");
        base.VisitUnsafeStatement(node);
    }

    public override void VisitFixedStatement(FixedStatementSyntax node)
    {
        Report(node, "Unsafe code is not supported in the browser environment.");
        base.VisitFixedStatement(node);
    }

    public override void VisitStackAllocArrayCreationExpression(StackAllocArrayCreationExpressionSyntax node)
    {
        Report(node, "stackalloc is not supported in the browser environment.");
        base.VisitStackAllocArrayCreationExpression(node);
    }

    private void CheckUnsafeModifier(SyntaxTokenList modifiers, SyntaxNode node)
    {
        if (modifiers.Any(SyntaxKind.UnsafeKeyword))
        {
            Report(node, "Unsafe code is not supported in the browser environment.");
        }
    }

    /// <summary>An `extern` member is only unsupported when it is real native interop.
    /// In the Transpose model an <c>extern</c> method is the normal way to declare a JavaScript
    /// binding: the method (or, far more commonly for binding libraries such as Transpose.Core, its
    /// containing type) carries a codegen attribute ([External]/[Template]/[Name]/[Script]/
    /// [GlobalMethods]/[ObjectLiteral]) that supplies the JS mapping. Only genuine P/Invoke
    /// (detected separately via [DllImport]/[LibraryImport]) is rejected.</summary>
    private void CheckExtern(SyntaxTokenList modifiers, SyntaxList<AttributeListSyntax> attributes, SyntaxNode node)
    {
        if (!modifiers.Any(SyntaxKind.ExternKeyword)) return;

        // Method-level codegen attribute (syntactic, fast path).
        var jsMapped = attributes.SelectMany(a => a.Attributes).Any(a => IsCodegenAttributeName(a.Name.ToString()));

        // Otherwise consult the semantic model: a binding declared on an [External]-family type
        // (the common DOM/binding-library shape), a scope/GlobalMethods binding, or an
        // [assembly: External] library (e.g. Transpose.Core) has bare extern members with no
        // per-member attribute.
        if (!jsMapped && _model.GetDeclaredSymbol(node) is IMethodSymbol method)
            jsMapped = HasCodegenAttribute(method)
                    || (method.ContainingType is { } t && (HasCodegenAttribute(t) || TransposeNaming.IsExternalType(t)))
                    || TransposeNaming.AssemblyHasExternalAttribute(method.ContainingAssembly);

        if (!jsMapped)
            Report(node, "Native interop (extern methods) is not supported in the browser environment.");
    }

    private static bool IsCodegenAttributeName(string n) =>
        n is "Template" or "Name" or "External" or "Script" or "GlobalMethods" or "ObjectLiteral" or "GlobalTarget"
          or "Transpose.Template" or "Transpose.Name" or "Transpose.External" or "Transpose.Script"
          or "Transpose.GlobalMethods" or "Transpose.ObjectLiteral" or "Transpose.GlobalTarget"
          or "TemplateAttribute" or "NameAttribute" or "ExternalAttribute" or "ScriptAttribute"
          or "GlobalMethodsAttribute" or "ObjectLiteralAttribute" or "GlobalTargetAttribute";

    private static bool HasCodegenAttribute(ISymbol symbol) =>
        symbol.GetAttributes().Any(a =>
        {
            var n = a.AttributeClass?.ToDisplayString();
            return n is "Transpose.ExternalAttribute" or "Transpose.TemplateAttribute" or "Transpose.NameAttribute"
                or "Transpose.ScriptAttribute" or "Transpose.GlobalMethodsAttribute" or "Transpose.ObjectLiteralAttribute"
                or "Transpose.ExternalInterfaceAttribute" or "Transpose.GlobalTargetAttribute";
        });

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        CheckUnsafeModifier(node.Modifiers, node);
        CheckExtern(node.Modifiers, node.AttributeLists, node);
        CheckDllImport(node);
        base.VisitMethodDeclaration(node);
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        CheckUnsafeModifier(node.Modifiers, node);
        CheckDuplicateJsNames(node);
        base.VisitClassDeclaration(node);
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        CheckDuplicateJsNames(node);
        base.VisitRecordDeclaration(node);
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        // Reached for a default interface implementation, which has a body and so is emitted.
        CheckDuplicateJsNames(node);
        base.VisitInterfaceDeclaration(node);
    }

    /// <summary>
    /// Reports members of this type that would be emitted under the same JavaScript name. Runs off the
    /// walk the scanner is already doing, reusing its semantic model, so it costs one
    /// <c>GetDeclaredSymbol</c> per type declaration rather than a second pass over the trees.
    /// </summary>
    private void CheckDuplicateJsNames(TypeDeclarationSyntax node)
    {
        if (_model.GetDeclaredSymbol(node) is not INamedTypeSymbol type) return;

        lock (_typesChecked)
        {
            if (!_typesChecked.Add(type)) return;
        }

        DuplicateJsNameScanner.Report(type, _diagnostics);
    }

    public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        CheckUnsafeModifier(node.Modifiers, node);
        base.VisitFieldDeclaration(node);
    }

    private void CheckDllImport(MethodDeclarationSyntax node)
    {
        foreach (var list in node.AttributeLists)
        foreach (var attr in list.Attributes)
        {
            // Screen on the written name before binding: resolving every attribute on every method
            // is a real cost across a large project, and only these two attribute names can ever
            // produce this diagnostic. `Name.ToString()` on the syntax covers the qualified form
            // (`System.Runtime.InteropServices.DllImport`) as well as the bare one.
            if (!LooksLikePInvokeAttributeName(attr.Name)) continue;

            var symbol = _model.GetSymbolInfo(attr).Symbol?.ContainingType;
            var name = symbol?.ToDisplayString();
            if (name is "System.Runtime.InteropServices.DllImportAttribute"
                     or "System.Runtime.InteropServices.LibraryImportAttribute")
            {
                Report(node, "Native interop (P/Invoke) is not supported in the browser environment.");
            }
        }
    }

    /// <summary>True if an attribute's written name could be <c>DllImport</c> or <c>LibraryImport</c>
    /// (bare, with the <c>Attribute</c> suffix, or namespace-qualified).</summary>
    private static bool LooksLikePInvokeAttributeName(NameSyntax name)
    {
        var simple = name switch
        {
            QualifiedNameSyntax q => q.Right.Identifier.ValueText,
            SimpleNameSyntax s => s.Identifier.ValueText,
            AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
            _ => name.ToString(),
        };
        return simple is "DllImport" or "DllImportAttribute" or "LibraryImport" or "LibraryImportAttribute";
    }

    // ---- Language features not modeled by the runtime ----------------------

    // checked *primitive integer* arithmetic: JS numbers do not trap on overflow, so an
    // OverflowException cannot be produced. `unchecked` is fine (it matches JS's default), and
    // `checked` around user-defined operators is fine too — those dispatch to op_Checked… methods
    // and need no trapping — so only reject built-in integer arithmetic under `checked`.
    public override void VisitCheckedStatement(CheckedStatementSyntax node)
    {
        if (node.Keyword.IsKind(SyntaxKind.CheckedKeyword) && ChecksBuiltinIntegerOverflow(node))
            Report(node, "checked arithmetic (overflow checking) is not supported in the browser environment.");
        base.VisitCheckedStatement(node);
    }

    public override void VisitCheckedExpression(CheckedExpressionSyntax node)
    {
        if (node.Keyword.IsKind(SyntaxKind.CheckedKeyword) && ChecksBuiltinIntegerOverflow(node))
            Report(node, "checked arithmetic (overflow checking) is not supported in the browser environment.");
        base.VisitCheckedExpression(node);
    }

    /// <summary>True if a `checked` region performs built-in (not user-defined-operator) arithmetic
    /// on a primitive integer type — the only case that would require runtime overflow trapping.
    /// A checked region inside a user-defined operator body is exempt (its result is what matters,
    /// and it emits as ordinary arithmetic).</summary>
    private bool ChecksBuiltinIntegerOverflow(SyntaxNode node)
    {
        if (node.FirstAncestorOrSelf<OperatorDeclarationSyntax>() is not null) return false;
        foreach (var bin in node.DescendantNodes().OfType<BinaryExpressionSyntax>())
        {
            if (!bin.IsKind(SyntaxKind.AddExpression) && !bin.IsKind(SyntaxKind.SubtractExpression)
                && !bin.IsKind(SyntaxKind.MultiplyExpression)) continue;
            if (_model.GetSymbolInfo(bin).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator })
                continue; // user-defined checked operator → supported
            if (_model.GetTypeInfo(bin).Type is { } t && IsPrimitiveInteger(t)) return true;
        }
        return false;
    }

    private static bool IsPrimitiveInteger(ITypeSymbol t) => t.SpecialType is
        SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16
        or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
        or SpecialType.System_Int64 or SpecialType.System_UInt64;

    // Native-sized integers (nint/nuint) have no JS representation distinct from double.
    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        // VisitIdentifierName fires on nearly every identifier in the source, so it is by a wide
        // margin the scanner's hottest path — and asking the semantic model to bind an identifier is
        // not cheap (it binds the whole enclosing member). Screen by identifier *text* first: unless
        // the text is the simple name of a type this scanner would reject, no semantic query can
        // change the outcome, and the vast majority of identifiers exit here having allocated nothing.
        // `var` is in the denied-name set too, so an inferred local whose type is never written out
        // (`var s = Factory.OpenFile();`) is still caught.
        var text = node.Identifier.ValueText;
        if (!_deniedSimpleNames.Contains(text) && !(_aliasedNames?.Contains(text) ?? false))
        {
            base.VisitIdentifierName(node);
            return;
        }

        // Resolve the symbol once and reuse it for both checks below rather than querying twice.
        var symbol = _model.GetSymbolInfo(node).Symbol;

        if (node.Identifier.Text is "nint" or "nuint"
            && symbol is INamedTypeSymbol { IsNativeIntegerType: true })
        {
            Report(node, "Native-sized integers (nint/nuint) are not supported in the browser environment.");
        }
        var type = symbol as INamedTypeSymbol ?? symbol?.ContainingType;
        if (type is not null) CheckDeniedType(type, node);
        base.VisitIdentifierName(node);
    }

    // ref/out/in modifiers on a lambda parameter (C# 14) — the runtime models a lambda as a plain
    // JS closure with no by-ref parameter passing.
    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
        foreach (var p in node.ParameterList.Parameters)
            if (p.Modifiers.Any(m => m.IsKind(SyntaxKind.RefKeyword) || m.IsKind(SyntaxKind.OutKeyword) || m.IsKind(SyntaxKind.InKeyword)))
            {
                Report(p, "ref/out/in parameters on lambdas are not supported in the browser environment.");
                break;
            }
        base.VisitParenthesizedLambdaExpression(node);
    }

    // Span/ReadOnlySpan constant-string pattern matching (`span is "literal"`) is not modeled.
    public override void VisitIsPatternExpression(IsPatternExpressionSyntax node)
    {
        // Only a *string-literal* constant pattern can be the span-pattern form (`span is "text"`),
        // so screen on the literal before binding the operand — `x is null`, `x is 0` and every
        // other constant pattern would otherwise pay for a semantic query that cannot match.
        if (node.Pattern is ConstantPatternSyntax { Expression: LiteralExpressionSyntax literal }
            && literal.Token.IsKind(SyntaxKind.StringLiteralToken)
            && _model.GetTypeInfo(node.Expression).Type is INamedTypeSymbol t
            && t.OriginalDefinition.ToDisplayString() is "System.ReadOnlySpan<T>" or "System.Span<T>")
        {
            Report(node, "Span pattern matching is not supported in the browser environment.");
        }
        base.VisitIsPatternExpression(node);
    }

    // An enum's members are emitted as plain JS numbers, so a 64-bit underlying type cannot be
    // represented: JavaScript numbers hold integers exactly only up to 2^53, and the runtime matches a
    // value to its member by that number. `enum E : long { X = long.MaxValue }` silently produced a
    // different ordinal (long.MaxValue round-tripped to long.MinValue), and members far apart above
    // 2^53 could collide onto one value. int/uint and narrower are safe and unaffected.
    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        if (node.BaseList?.Types.FirstOrDefault()?.Type is { } baseType
            && _model.GetTypeInfo(baseType).Type?.SpecialType
                is SpecialType.System_Int64 or SpecialType.System_UInt64)
        {
            Report(node, $"An enum with a 64-bit underlying type ('{baseType}') is not supported in the browser environment; enum members are JavaScript numbers, which represent integers exactly only up to 2^53.");
        }

        base.VisitEnumDeclaration(node);
    }

    // Inline arrays ([InlineArray] structs, C# 12) have no JS representation.
    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        CheckUnsafeModifier(node.Modifiers, node);
        if (node.AttributeLists.SelectMany(a => a.Attributes)
                .Any(a => a.Name.ToString() is "InlineArray" or "InlineArrayAttribute"
                    or "System.Runtime.CompilerServices.InlineArray"
                    or "System.Runtime.CompilerServices.InlineArrayAttribute"))
        {
            Report(node, "Inline arrays are not supported in the browser environment.");
        }
        CheckDuplicateJsNames(node);
        base.VisitStructDeclaration(node);
    }

    // ---- Runtime APIs that cannot exist in the browser ---------------------

    // Denied namespaces, stored as their segments (root → leaf) so a type's namespace can be matched
    // by walking namespace symbols — no per-identifier string allocation. `segments` is in outer→inner
    // order (e.g. ["System","IO"]); a type matches if its namespace equals or is nested under these.
    private static readonly (string[] segments, string message)[] DeniedNamespaces =
    {
        (new[] { "System", "IO" },              "File I/O ({0}) is not supported in the browser environment."),
        (new[] { "System", "Net", "Sockets" },  "Sockets ({0}) are not supported in the browser environment."),
        (new[] { "System", "Threading" },       "Threading primitives ({0}) are not supported in the browser environment."),
    };

    // Types inside the denied namespaces that ARE modeled by the Transpose runtime and so are allowed
    // (matching what h5 compiled). Two groups:
    //   * Task-based async + cancellation (System.Threading.*): Task/ValueTask, the completion source,
    //     cancellation tokens, and the cancellation exceptions — the runtime models cooperative
    //     cancellation, so `catch (TaskCanceledException)` etc. must compile.
    //   * In-memory streams / text readers-writers (System.IO.*): these operate on memory, not the OS
    //     file system, and the runtime provides them. The genuinely OS-bound types (File, FileStream,
    //     Directory, …) are NOT listed and stay denied.
    private static readonly HashSet<string> AllowedThreadingTypes = new()
    {
        "System.Threading.Tasks.Task",
        "System.Threading.Tasks.Task`1",
        "System.Threading.Tasks.ValueTask",
        "System.Threading.Tasks.ValueTask`1",
        "System.Threading.Tasks.TaskCompletionSource",
        "System.Threading.Tasks.TaskCompletionSource`1",
        "System.Threading.Tasks.TaskCanceledException",
        "System.Threading.Tasks.TaskStatus",
        "System.Threading.CancellationToken",
        "System.Threading.CancellationTokenSource",
        "System.Threading.CancellationTokenRegistration",
        "System.Threading.Timeout",
        // In-memory streams and text readers/writers (no OS file access) — modeled by the runtime.
        // Binary serialization (BinaryReader/BinaryWriter) is intentionally NOT here: it is not fully
        // modeled at runtime, so it stays denied with a clear compile-time error rather than failing
        // mysteriously in the browser.
        "System.IO.Stream",
        "System.IO.MemoryStream",
        "System.IO.BufferedStream",
        "System.IO.StringReader",
        "System.IO.StringWriter",
        "System.IO.StreamReader",
        "System.IO.StreamWriter",
        "System.IO.TextReader",
        "System.IO.TextWriter",
        "System.IO.SeekOrigin",
        "System.IO.IOException",
        "System.IO.EndOfStreamException",
    };

    private static readonly string[] TasksNamespaceSegments = { "System", "Threading", "Tasks" };

    private readonly HashSet<Location> _reportedApiLocations = new();

    /// <summary>The diagnostic message template for a type in a denied namespace, or null when the
    /// type is fine. Matches against the namespace *symbol* (a cheap segment walk) before doing any
    /// string work, so a type that is not denied costs nothing.</summary>
    private static string? DeniedNamespaceMessage(INamedTypeSymbol type)
    {
        var containing = type.ContainingNamespace;
        if (containing is null || containing.IsGlobalNamespace) return null;

        string? message = null;
        foreach (var (segments, msg) in DeniedNamespaces)
        {
            if (NamespaceMatches(containing, segments)) { message = msg; break; }
        }
        if (message is null) return null;

        // System.Threading.Tasks.* is the supported async model (Task, ValueTask, IPromise, the
        // completion source, cancellation exceptions, …) — allow the whole sub-namespace even though
        // its parent System.Threading is denied. The genuinely-unsupported threading primitives
        // (Thread, Monitor, locks, wait handles) live directly in System.Threading and stay denied.
        if (NamespaceMatches(containing, TasksNamespaceSegments)) return null;

        return AllowedThreadingTypes.Contains(type.ConstructUnboundGenericTypeSafeName()) ? null : message;
    }

    private void CheckDeniedType(INamedTypeSymbol type, SyntaxNode node)
    {
        var message = DeniedNamespaceMessage(type);
        if (message is null) return;

        if (_reportedApiLocations.Add(node.GetLocation()))
        {
            var full = type.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty);
            Report(node, string.Format(message, full));
        }
    }

    /// <summary>True if <paramref name="ns"/> equals the root-anchored namespace <paramref name="segments"/>
    /// (outer→inner, e.g. ["System","IO"]) or is nested beneath it — matches both System.IO and
    /// System.IO.Compression. Walks the namespace symbol chain (which runs innermost→outermost)
    /// without allocating.</summary>
    private static bool NamespaceMatches(INamespaceSymbol ns, string[] segments)
    {
        // How many named parts deep is ns? A denied prefix can only match if ns is at least that deep.
        var depth = 0;
        for (var n = ns; n is not null && !n.IsGlobalNamespace; n = n.ContainingNamespace) depth++;
        if (depth < segments.Length) return false;

        // Drop the inner (depth - segments.Length) parts so `cur` sits at the innermost denied segment.
        var cur = ns;
        for (var skip = depth - segments.Length; skip > 0 && cur is not null; skip--) cur = cur.ContainingNamespace;

        // Compare cur outward to the root against segments (inner→outer), then require anchoring at
        // the global namespace so a nested "MyLib.System.IO" does not match "System.IO".
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            if (cur is null || cur.IsGlobalNamespace) return false;
            if (!string.Equals(cur.Name, segments[i], System.StringComparison.Ordinal)) return false;
            cur = cur.ContainingNamespace;
        }
        return cur is null || cur.IsGlobalNamespace;
    }
}

internal static class SymbolExtensions
{
    /// <summary>Metadata-style name including arity marker, e.g. Task`1.</summary>
    public static string ConstructUnboundGenericTypeSafeName(this INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString();
        var name = type.MetadataName; // already includes `1 arity
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }
}
