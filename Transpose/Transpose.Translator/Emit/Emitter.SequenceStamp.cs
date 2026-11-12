using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

/// <summary>
/// Recording the element type of a LINQ result, so <c>seq is IEnumerable&lt;T&gt;</c> can be answered.
///
/// <para>
/// The LINQ surface binds to linq.js, where one <c>Enumerable</c> constructor serves every element
/// type: a sequence registers only the non-generic <c>IEnumerable</c>, so every
/// <c>is IEnumerable&lt;T&gt;</c> test of a LINQ result answered <b>false</b>, and
/// <c>as IEnumerable&lt;T&gt;</c> answered null — <c>object o = xs.Where(…); o is IEnumerable&lt;int&gt;</c>
/// was false where .NET says true. The element type is not recorded anywhere at run time, but the
/// emitter knows it statically, so the outermost LINQ call of a chain is emitted as
/// <c>TransposeR.seqOf(call, T)</c>, which records it on the sequence; <c>Transpose.is</c> then compares
/// it with .NET's variance rules (<c>Enumerable.$isEnumerableOf</c> in linq.js).
/// </para>
///
/// <para>
/// Only the outermost call: a LINQ result that is immediately the receiver of another member
/// (<c>xs.Where(…).Select(…)</c>, <c>.Count()</c>, <c>.ToList()</c>) is never observable as a value,
/// so stamping it would only cost. Only the external LINQ bindings (<c>System.Linq</c>'s
/// <c>Enumerable</c>, <c>EnumerableInstance</c>, …): the transpiled C# operators
/// (<c>EnumerableExtras</c>) are iterators, which carry their own generic type.
/// </para>
/// </summary>
public sealed partial class Emitter
{
    /// <summary>The element type to record on the sequence <paramref name="expr"/> evaluates to, or
    /// null when it needs no stamp (see the type summary).</summary>
    private ITypeSymbol? SequenceStampElementType(ExpressionSyntax expr)
    {
        if (IsMemberReceiver(expr)) return null;

        if (expr is InvocationExpressionSyntax)
        {
            if (_model.GetSymbolInfo(expr).Symbol is not IMethodSymbol method) return null;
            var container = method.ContainingType;
            if (container is null || !TransposeNaming.IsExternalType(container)
                || container.ContainingNamespace?.ToDisplayString() != "System.Linq") return null;
            return LinqSequenceElementType(method.ReturnType);
        }

        // Query syntax lowers to the same operators; its value is the query expression itself.
        return expr is QueryExpressionSyntax ? LinqSequenceElementType(_model.GetTypeInfo(expr).Type) : null;
    }

    /// <summary><c>T</c> when <paramref name="type"/> is one of the shapes a linq.js sequence is
    /// declared as, and T can be named at run time where it is used.</summary>
    private static ITypeSymbol? LinqSequenceElementType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol { TypeArguments.Length: 1 } named) return null;
        var def = named.OriginalDefinition.ToDisplayString();
        if (def is not ("System.Collections.Generic.IEnumerable<T>" or "System.Linq.EnumerableInstance<TElement>"
            or "System.Linq.IOrderedEnumerable<TSource>" or "System.Linq.OrderedEnumerable<TElement>"))
            return null;
        var element = named.TypeArguments[0];
        return IsNameableAtRuntime(element) ? element : null;
    }

    /// <summary>False when <paramref name="type"/> mentions a method type parameter that has no runtime
    /// value — its method does not thread its type arguments ([IgnoreGeneric], a template).</summary>
    private static bool IsNameableAtRuntime(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method, DeclaringMethod: { } m } => ThreadsTypeArgs(m),
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => IsNameableAtRuntime(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.All(IsNameableAtRuntime),
        _ => true,
    };

    /// <summary>True when <paramref name="expr"/> (through any parentheses) is the receiver of a member
    /// access or a null-conditional access — its value goes nowhere but into that member.</summary>
    private static bool IsMemberReceiver(ExpressionSyntax expr)
    {
        SyntaxNode node = expr;
        while (node.Parent is ParenthesizedExpressionSyntax p) node = p;
        return node.Parent switch
        {
            MemberAccessExpressionSyntax ma => ma.Expression == node,
            ConditionalAccessExpressionSyntax ca => ca.Expression == node,
            _ => false,
        };
    }

    /// <summary>Emits <paramref name="emit"/>, wrapped in <c>TransposeR.seqOf(…, T)</c> when it produces a
    /// LINQ sequence whose element type should be recorded.</summary>
    private void EmitStampingSequence(ExpressionSyntax expr, System.Action emit)
    {
        var element = SequenceStampElementType(expr);
        if (element is null) { emit(); return; }

        _w.Write("TransposeR.seqOf(");
        emit();
        _w.Write(", ");
        // A Type value, exactly as typeof(T) emits it: soft in module mode (a stub answers the
        // variance question), except for the parts of a constructed generic that are built by a call.
        RecordConstructedTypeRefs(element);
        _softRefDepth++;
        try { _w.Write(TypeRef(element)); }
        finally { _softRefDepth--; }
        _w.Write(")");
    }
}
