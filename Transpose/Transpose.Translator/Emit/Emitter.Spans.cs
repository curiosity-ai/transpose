using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

/// <summary>
/// <c>Span&lt;T&gt;</c> and <c>ReadOnlySpan&lt;T&gt;</c>.
///
/// <para>
/// A span is a real runtime object (a window of <c>_array</c>/<c>_offset</c>/<c>_length</c>, see
/// <c>BCL/Transpose.BCL/System/Span.cs</c>), and every place C# makes one out of something else goes
/// through <c>TransposeR.toSpan(value, SpanType)</c>. C# 14's first-class span conversions (array → span,
/// span → read-only span, string → <c>ReadOnlySpan&lt;char&gt;</c>) are language conversions rather than
/// calls to <c>op_Implicit</c>, so before this nothing built the span at all: <c>Span&lt;int&gt; s = new
/// int[3]</c> left the bare array in <c>s</c>, and <c>s[0] = 1</c> threw "setItem is not a function".
/// The same holds for the other span-producing forms handled here — a collection expression or a
/// <c>params</c> argument whose target is a span, <c>stackalloc</c> into a span, and a UTF-8 string
/// literal (<c>"abc"u8</c>).
/// </para>
/// </summary>
public sealed partial class Emitter
{
    /// <summary>True for <c>System.Span&lt;T&gt;</c> or <c>System.ReadOnlySpan&lt;T&gt;</c>.</summary>
    private static bool IsSpanType(ITypeSymbol? type)
        => type is INamedTypeSymbol { IsGenericType: true } named
           && named.OriginalDefinition.ToDisplayString() is "System.Span<T>" or "System.ReadOnlySpan<T>";

    /// <summary>A span whose element is <c>char</c> — the subject a string-constant pattern can test.</summary>
    private static bool IsCharSpanType(ITypeSymbol? type)
        => IsSpanType(type) && ((INamedTypeSymbol)type!).TypeArguments[0].SpecialType == SpecialType.System_Char;

    /// <summary>
    /// Emits <paramref name="expr"/> converted to the span type <paramref name="targetType"/> when that
    /// is a span conversion — the source is an array, a string, or the other span type — and returns
    /// true. Anything else (already the target span type, not a span target) is left to the caller.
    /// </summary>
    private bool TryEmitSpanConversion(ExpressionSyntax expr, ITypeSymbol? targetType)
    {
        if (!IsSpanType(targetType)) return false;
        var sourceType = _model.GetTypeInfo(expr).Type;
        if (sourceType is null || SymbolEqualityComparer.Default.Equals(sourceType, targetType)) return false;
        var sourceElement = sourceType switch
        {
            IArrayTypeSymbol { Rank: 1 } array => array.ElementType,
            _ when sourceType.SpecialType == SpecialType.System_String => _compilation.GetSpecialType(SpecialType.System_Char),
            INamedTypeSymbol named when IsSpanType(named) => named.TypeArguments[0],
            _ => null,
        };
        if (sourceElement is null) return false;

        // An extension receiver's slot is the method's unconstructed parameter (`ReadOnlySpan<T>` of
        // `MemoryExtensions.IndexOf<T>`); the element type is then the source's own.
        var target = (INamedTypeSymbol)targetType!;
        if (target.TypeArguments[0] is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method })
            target = target.OriginalDefinition.Construct(sourceElement);
        // Same kind of span over the same element: nothing to convert.
        if (SymbolEqualityComparer.Default.Equals(sourceType, target)) return false;
        targetType = target;

        _w.Write("TransposeR.toSpan(");
        EmitExpression(expr);
        _w.Write($", {TypeRef(targetType!)})");
        return true;
    }

    /// <summary>Wraps the array <paramref name="emitArray"/> writes into a span of <paramref name="spanType"/>.</summary>
    private void EmitSpanOver(ITypeSymbol spanType, System.Action emitArray)
    {
        _w.Write("TransposeR.toSpan(");
        emitArray();
        _w.Write($", {TypeRef(spanType)})");
    }

    /// <summary>
    /// <c>stackalloc T[n]</c>, <c>stackalloc T[] { … }</c> and <c>stackalloc[] { … }</c> converted to a
    /// span. JavaScript has no stack to allocate on, so the span is over an ordinary zeroed (or
    /// initialized) array — observably the same, since a span can never outlive its own storage. A
    /// <c>stackalloc</c> into a pointer is unsafe code and rejected by the scanner.
    /// </summary>
    private void EmitStackAlloc(ExpressionSyntax stackAlloc, TypeSyntax? arrayTypeSyntax, InitializerExpressionSyntax? initializer)
    {
        var spanType = _model.GetTypeInfo(stackAlloc).ConvertedType ?? _model.GetTypeInfo(stackAlloc).Type;
        if (!IsSpanType(spanType))
        {
            Unsupported(stackAlloc, "stackalloc into a pointer");
            return;
        }
        var elementType = ((INamedTypeSymbol)spanType!).TypeArguments[0];

        EmitSpanOver(spanType, () =>
        {
            if (initializer is not null)
            {
                EmitTypedInitializerArray(initializer, elementType);
                return;
            }
            var size = arrayTypeSyntax is ArrayTypeSyntax { RankSpecifiers: [{ Sizes: [var sizeExpr] }] }
                       && sizeExpr is not OmittedArraySizeExpressionSyntax
                ? sizeExpr
                : null;
            _w.Write("System.Array.init(TransposeR.array(");
            if (size is not null) EmitExpression(size); else _w.Write("0");
            _w.Write($", {DefaultValueLiteral(elementType)}), {ArrayElementTypeRef(elementType)})");
        });
    }

    /// <summary><c>"text"u8</c>: a <c>ReadOnlySpan&lt;byte&gt;</c> over the UTF-8 bytes of the text,
    /// encoded at compile time as C# does.</summary>
    private void EmitUtf8Literal(LiteralExpressionSyntax literal)
    {
        var spanType = _model.GetTypeInfo(literal).Type!;
        var text = literal.Token.ValueText;
        var bytes = Encoding.UTF8.GetBytes(text);
        EmitSpanOver(spanType, () =>
        {
            _w.Write("System.Array.init([");
            _w.Write(string.Join(", ", bytes.Select(b => b.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            _w.Write("], System.Byte)");
        });
    }

    /// <summary>
    /// <c>span[a..b]</c> on a span: C# binds a range over a span to <c>Slice(start, length)</c>. The
    /// array path emits JavaScript's <c>slice</c>, which a span object does not have.
    /// </summary>
    private bool TryEmitSpanRange(ElementAccessExpressionSyntax element)
    {
        if (element.ArgumentList.Arguments.Count != 1
            || element.ArgumentList.Arguments[0].Expression is not RangeExpressionSyntax range
            || !IsSpanType(_model.GetTypeInfo(element.Expression).Type))
            return false;

        // The bounds are arrows over the span so a from-end index (`^1`) can read its length, and so
        // the span expression is evaluated once. An await cannot sit inside a plain arrow.
        if (ContainsAwait(range)) return false;
        _w.Write("TransposeR.spanRange(");
        EmitExpression(element.Expression);
        _w.Write(", ($s) => ");
        EmitIndexValue(range.LeftOperand, "$s", isEnd: false);
        _w.Write(", ($s) => ");
        EmitIndexValue(range.RightOperand, "$s", isEnd: true);
        _w.Write(")");
        return true;
    }

    /// <summary>
    /// C#'s implicit <c>Index</c> support: a type with an <c>int</c> indexer and a <c>Length</c> or
    /// <c>Count</c> property accepts <c>x[^n]</c> and <c>x[someIndex]</c>, which mean
    /// <c>x[x.Length - n]</c> and <c>x[someIndex.GetOffset(x.Length)]</c>. The argument used to be
    /// emitted as the <c>System.Index</c> object itself, so <c>list[^1]</c> read nothing,
    /// <c>list[^1] = v</c> wrote nowhere and <c>"hello"[^1]</c> gave <c>'h'</c> — for every indexer
    /// except an array's, which has its own inline form. Rewriting the argument here covers every
    /// indexer shape (accessor, native, string, template) and every use (read, write, compound, ++)
    /// at once, since all of them emit their argument through <see cref="EmitExpression"/>.
    /// </summary>
    private ExpressionSyntax? _implicitIndexInProgress;

    private bool TryEmitImplicitIndexArgument(ExpressionSyntax expr)
    {
        if (ReferenceEquals(_implicitIndexInProgress, expr)) return false;
        if (expr.Parent is not ArgumentSyntax { Parent: BracketedArgumentListSyntax { Parent: ElementAccessExpressionSyntax element } } argument)
            return false;
        if (_model.GetTypeInfo(expr).Type?.ToDisplayString() != "System.Index") return false;
        if (_model.GetSymbolInfo(element).Symbol is not IPropertySymbol { IsIndexer: true } indexer) return false;
        var position = element.ArgumentList.Arguments.IndexOf(argument);
        if (position < 0 || position >= indexer.Parameters.Length
            || indexer.Parameters[position].Type.SpecialType != SpecialType.System_Int32)
            return false;

        var receiverType = _model.GetTypeInfo(element.Expression).Type;
        var lengthProperty = LengthOrCount(receiverType);
        if (lengthProperty is null) return false;
        // The receiver is read once more, for its length: fine for a variable or a field, wrong for
        // anything that does work when evaluated.
        if (HasSideEffects(element.Expression))
        {
            Unsupported(element, "a from-end index (^n) into an indexer whose receiver has side effects; store the receiver in a local first");
            return true;
        }

        var length = Capture(() => EmitPropertyAccess(lengthProperty, element.Expression is ThisExpressionSyntax ? null : element.Expression));
        if (expr is PrefixUnaryExpressionSyntax fromEnd && expr.IsKind(SyntaxKind.IndexExpression))
        {
            _w.Write($"({length} - ");
            EmitExpression(fromEnd.Operand);
            _w.Write(")");
        }
        else
        {
            _w.Write("(");
            _implicitIndexInProgress = expr;
            try { EmitExpression(expr); }
            finally { _implicitIndexInProgress = null; }
            _w.Write($").GetOffset({length})");
        }
        return true;
    }

    /// <summary>The <c>Length</c> or <c>Count</c> property C# uses for implicit Index support.</summary>
    private static IPropertySymbol? LengthOrCount(ITypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            foreach (var name in new[] { "Length", "Count" })
            {
                var p = t.GetMembers(name).OfType<IPropertySymbol>()
                    .FirstOrDefault(m => !m.IsStatic && !m.IsIndexer && m.GetMethod is not null
                                         && m.Type.SpecialType == SpecialType.System_Int32);
                if (p is not null) return p;
            }
        }
        return null;
    }
}
