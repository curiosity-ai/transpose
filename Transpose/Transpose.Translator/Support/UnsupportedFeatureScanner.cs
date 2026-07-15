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

    private UnsupportedFeatureScanner(SemanticModel model, List<Diagnostic> diagnostics)
    {
        _model = model;
        _diagnostics = diagnostics;
    }

    public static IReadOnlyList<Diagnostic> Scan(CSharpCompilation compilation)
    {
        var diagnostics = new List<Diagnostic>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var scanner = new UnsupportedFeatureScanner(model, diagnostics);
            scanner.Visit(tree.GetRoot());
        }
        return diagnostics;
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
        n is "Template" or "Name" or "External" or "Script" or "GlobalMethods" or "ObjectLiteral"
          or "Transpose.Template" or "Transpose.Name" or "Transpose.External" or "Transpose.Script"
          or "Transpose.GlobalMethods" or "Transpose.ObjectLiteral"
          or "TemplateAttribute" or "NameAttribute" or "ExternalAttribute" or "ScriptAttribute"
          or "GlobalMethodsAttribute" or "ObjectLiteralAttribute";

    private static bool HasCodegenAttribute(ISymbol symbol) =>
        symbol.GetAttributes().Any(a =>
        {
            var n = a.AttributeClass?.ToDisplayString();
            return n is "Transpose.ExternalAttribute" or "Transpose.TemplateAttribute" or "Transpose.NameAttribute"
                or "Transpose.ScriptAttribute" or "Transpose.GlobalMethodsAttribute" or "Transpose.ObjectLiteralAttribute"
                or "Transpose.ExternalInterfaceAttribute";
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
        base.VisitClassDeclaration(node);
    }

    public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        CheckUnsafeModifier(node.Modifiers, node);
        base.VisitFieldDeclaration(node);
    }

    private void CheckDllImport(MethodDeclarationSyntax node)
    {
        foreach (var attr in node.AttributeLists.SelectMany(a => a.Attributes))
        {
            var symbol = _model.GetSymbolInfo(attr).Symbol?.ContainingType;
            var name = symbol?.ToDisplayString();
            if (name is "System.Runtime.InteropServices.DllImportAttribute"
                     or "System.Runtime.InteropServices.LibraryImportAttribute")
            {
                Report(node, "Native interop (P/Invoke) is not supported in the browser environment.");
            }
        }
    }

    // ---- Language features not modeled by the runtime ----------------------

    // checked arithmetic: JS numbers do not trap on integer overflow, so an OverflowException
    // cannot be produced. `unchecked` is fine (it matches JS's wraparound-free default).
    public override void VisitCheckedStatement(CheckedStatementSyntax node)
    {
        if (node.Keyword.IsKind(SyntaxKind.CheckedKeyword))
            Report(node, "checked arithmetic (overflow checking) is not supported in the browser environment.");
        base.VisitCheckedStatement(node);
    }

    public override void VisitCheckedExpression(CheckedExpressionSyntax node)
    {
        if (node.Keyword.IsKind(SyntaxKind.CheckedKeyword))
            Report(node, "checked arithmetic (overflow checking) is not supported in the browser environment.");
        base.VisitCheckedExpression(node);
    }

    // Native-sized integers (nint/nuint) have no JS representation distinct from double.
    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (node.Identifier.Text is "nint" or "nuint"
            && _model.GetSymbolInfo(node).Symbol is INamedTypeSymbol { IsNativeIntegerType: true })
        {
            Report(node, "Native-sized integers (nint/nuint) are not supported in the browser environment.");
        }
        var symbol = _model.GetSymbolInfo(node).Symbol;
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
        if (node.Pattern is ConstantPatternSyntax
            && _model.GetTypeInfo(node.Expression).Type is INamedTypeSymbol t
            && t.OriginalDefinition.ToDisplayString() is "System.ReadOnlySpan<T>" or "System.Span<T>")
        {
            Report(node, "Span pattern matching is not supported in the browser environment.");
        }
        base.VisitIsPatternExpression(node);
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
        base.VisitStructDeclaration(node);
    }

    // ---- Runtime APIs that cannot exist in the browser ---------------------

    private static readonly (string ns, string message)[] DeniedNamespaces =
    {
        ("System.IO", "File I/O ({0}) is not supported in the browser environment."),
        ("System.Net.Sockets", "Sockets ({0}) are not supported in the browser environment."),
        ("System.Threading", "Threading primitives ({0}) are not supported in the browser environment."),
    };

    // Threading types that are allowed because they are modeled (Task-based async, etc.)
    private static readonly HashSet<string> AllowedThreadingTypes = new()
    {
        "System.Threading.Tasks.Task",
        "System.Threading.Tasks.Task`1",
        "System.Threading.Tasks.ValueTask",
        "System.Threading.Tasks.ValueTask`1",
        "System.Threading.Tasks.TaskCompletionSource",
        "System.Threading.Tasks.TaskCompletionSource`1",
        "System.Threading.CancellationToken",
        "System.Threading.CancellationTokenSource",
        "System.Threading.CancellationTokenRegistration",
        "System.Threading.Timeout",
    };

    private readonly HashSet<Location> _reportedApiLocations = new();

    private void CheckDeniedType(INamedTypeSymbol type, SyntaxNode node)
    {
        var full = type.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty);

        var metadataName = type.ConstructUnboundGenericTypeSafeName();
        if (AllowedThreadingTypes.Contains(metadataName)) return;

        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        foreach (var (deniedNs, message) in DeniedNamespaces)
        {
            if (ns == deniedNs || ns.StartsWith(deniedNs + ".", System.StringComparison.Ordinal))
            {
                if (_reportedApiLocations.Add(node.GetLocation()))
                {
                    Report(node, string.Format(message, full));
                }
                return;
            }
        }
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
