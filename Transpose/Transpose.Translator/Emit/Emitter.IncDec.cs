using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

/// <summary>
/// <c>++</c> / <c>--</c> where JavaScript's own operator gives the wrong answer or is not even valid:
/// <list type="bullet">
/// <item><b>An indexer element</b> — <c>counts[key]++</c>, <c>list[i]--</c>. A source or collection
/// indexer is emitted as <c>getItem</c>/<c>setItem</c> calls, and <c>coll.getItem(i)++</c> is a
/// JavaScript <i>syntax</i> error ("Invalid left-hand side expression in postfix operation"), so a
/// single such statement stopped the whole bundle from loading. It goes through
/// <c>TransposeR.incItem</c>, which evaluates the receiver and the index once, reads, writes, and
/// yields the old or the new value as C# requires.</item>
/// <item><b>A 32-bit integer</b> — <c>int</c> and <c>uint</c> wrap on overflow in unchecked C#, but a
/// bare JS <c>i++</c> on <c>int.MaxValue</c> gave <c>2147483648</c>, and <c>uint</c> <c>0--</c> gave
/// <c>-1</c>. Emitted inline as <c>(i + 1) | 0</c> / <c>(u - 1) &gt;&gt;&gt; 0</c>, which costs a loop
/// counter nothing. Sub-word types (<c>byte</c>, <c>short</c>, <c>char</c>, …) get their clip helper
/// here too in expression position; a statement was already handled by
/// <c>TryEmitNarrowingIncDecStatement</c>.</item>
/// </list>
/// </summary>
public sealed partial class Emitter
{
    private bool TryEmitIncDec(ExpressionSyntax node, ExpressionSyntax operand, bool increment, bool prefix)
    {
        var type = _model.GetTypeInfo(operand).Type;
        var op = increment ? "+" : "-";

        // A classic static `operator ++(T)` / `operator --(T)`: the step is a call, and the variable
        // takes its result. A bare JS ++ on the object gave NaN.
        var userOp = _model.GetSymbolInfo(node).Symbol is IMethodSymbol
            {
                MethodKind: MethodKind.UserDefinedOperator, IsStatic: true, IsImplicitlyDeclared: false, Parameters.Length: 1,
            } uop
            && (TransposeNaming.GetTemplate(uop.OriginalDefinition) is not null || !uop.IsExtern)
            ? uop
            : null;
        if (_model.GetSymbolInfo(node).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } && userOp is null)
            return false;
        string Step(string value) => userOp is not null
            ? Capture(() => WriteUnaryOperator(userOp, value))
            : StepValue(type, value, op);

        // ---- an indexer element ----------------------------------------------------------------
        if (operand is ElementAccessExpressionSyntax element
            && _model.GetSymbolInfo(element).Symbol is IPropertySymbol { IsIndexer: true } indexer
            && indexer.ContainingType.SpecialType != SpecialType.System_String
            && !TransposeNaming.IsNativeIndexer(indexer)
            && !(indexer.GetMethod is { } g && TransposeNaming.GetTemplate(g.OriginalDefinition) is not null)
            && !(indexer.SetMethod is { } s && TransposeNaming.GetTemplate(s.OriginalDefinition) is not null)
            && !ContainsAwait(element))
        {
            _w.Write("TransposeR.incItem(");
            EmitExpression(element.Expression);
            _w.Write($", {JsString(TransposeNaming.IndexerAccessorName(indexer, isGet: true))}");
            _w.Write($", {JsString(TransposeNaming.IndexerAccessorName(indexer, isGet: false))}, [");
            EmitArgumentList(element.ArgumentList);
            _w.Write("], ($x) => ");
            _w.Write(Step("$x"));
            _w.Write(prefix || IsVoidContext(node) ? ", true)" : ", false)");
            return true;
        }

        // ---- a plain variable or field: a wrapping integer, a nullable number, or a static user operator
        // A null `int?` stays null under ++ in C#; JavaScript's `null++` made it 1.
        var nullableNumber = IsNullableValueType(type)
            && ((INamedTypeSymbol)type!).TypeArguments[0] is var inner
            && (IsIntegerType(inner) || IsFloatingType(inner) || IsDecimalType(inner) || IsCharType(inner));
        if (userOp is null && !nullableNumber && WrapIntegerStep(type) is null) return false;
        if (operand is not IdentifierNameSyntax
            && operand is not MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or IdentifierNameSyntax })
            return false;
        // A ref local or ref parameter reads through `.v`, which EmitExpression already writes.
        var target = Capture(() => EmitExpression(operand));

        if (userOp is not null || nullableNumber)
        {
            if (IsVoidContext(node) || prefix) { _w.Write($"({target} = {Step(target)})"); return true; }
            // Postfix as a value: the old object, after the variable took the operator's result.
            _w.Write($"TransposeR.postStep({target}, ($o) => {{ {target} = {Step("$o")}; }})");
            return true;
        }

        var assign = $"{target} = {Step(target)}";
        if (IsVoidContext(node)) { _w.Write(assign); return true; }
        if (prefix) { _w.Write($"({assign})"); return true; }

        // Postfix used as a value yields the old value. Stepping back from the new one is exact in
        // modular arithmetic, so this needs no temporary (and no closure in a hot `a[i++]`).
        _w.Write(StepValue(type, $"({assign})", increment ? "-" : "+"));
        return true;
    }

    /// <summary>The JS expression for <paramref name="value"/> stepped by one in the given direction,
    /// wrapped to the width of <paramref name="type"/>.</summary>
    private string StepValue(ITypeSymbol? type, string value, string op)
    {
        if (IsNullableValueType(type))
        {
            var inner = ((INamedTypeSymbol)type!).TypeArguments[0];
            return $"({value} == null ? null : {StepValue(inner, value, op)})";
        }
        if (IncDecStep(type, op == "+") is { } step) return $"{value}{step}";
        return WrapIntegerStep(type) is { } wrap ? wrap(value, op) : $"({value}) {op} 1";
    }

    /// <summary>How a step of an integer type of at most 32 bits wraps, or null for any other type.</summary>
    private static System.Func<string, string, string>? WrapIntegerStep(ITypeSymbol? type)
    {
        switch (type?.SpecialType)
        {
            // Parenthesized as a whole: `|` and `>>>` bind looser than a comparison, so `k-- > 1`
            // would otherwise read as `… | (0 > 1)`.
            case SpecialType.System_Int32: return (v, op) => $"((({v}) {op} 1) | 0)";
            case SpecialType.System_UInt32: return (v, op) => $"((({v}) {op} 1) >>> 0)";
        }
        if (IsSubWordIntegerTarget(type))
        {
            var clip = NarrowIntegerClip(type!.SpecialType);
            return (v, op) => $"{clip}(({v}) {op} 1)";
        }
        return null;
    }

    /// <summary>Set while an expression is being written as a temporary (<see cref="EmitExpression"/>).</summary>
    private (ExpressionSyntax node, string text)? _exprOverride;

    /// <summary>
    /// A compound assignment to an array element or accessor-indexer element whose receiver or index
    /// has a side effect — <c>arr[k++] += 10</c>, <c>Next()[i] -= 1</c>. The ordinary form writes the
    /// target twice (<c>arr[k++] = arr[k++] + 10</c>), so the side effect ran twice and the value was read
    /// from the wrong element. Here the receiver and the index are evaluated once, by
    /// <c>TransposeR.updElem</c> / <c>TransposeR.incItem</c>, and the new value is computed from the
    /// element they read. Everything else keeps its existing (byte-identical) form.
    /// </summary>
    private bool TryEmitSingleEvaluationCompound(AssignmentExpressionSyntax assignment, string op,
        ITypeSymbol? leftType, ITypeSymbol? rightType)
    {
        if (op == "=" || assignment.Left is not ElementAccessExpressionSyntax element) return false;
        if (!HasSideEffects(element.Expression) && !element.ArgumentList.Arguments.Any(a => HasSideEffects(a.Expression)))
            return false;
        if (ContainsAwait(element)) return false;
        if (op is "??=") return false;

        var symbol = _model.GetSymbolInfo(element).Symbol;
        string head;
        if (symbol is IPropertySymbol { IsIndexer: true } indexer)
        {
            if (indexer.ContainingType.SpecialType == SpecialType.System_String
                || TransposeNaming.IsNativeIndexer(indexer)
                || (indexer.GetMethod is { } g && TransposeNaming.GetTemplate(g.OriginalDefinition) is not null)
                || (indexer.SetMethod is { } s && TransposeNaming.GetTemplate(s.OriginalDefinition) is not null))
                return false;
            head = Capture(() =>
            {
                _w.Write("TransposeR.incItem(");
                EmitExpression(element.Expression);
                _w.Write($", {JsString(TransposeNaming.IndexerAccessorName(indexer, isGet: true))}");
                _w.Write($", {JsString(TransposeNaming.IndexerAccessorName(indexer, isGet: false))}, [");
                EmitArgumentList(element.ArgumentList);
                _w.Write("], ");
            });
        }
        else if (_model.GetTypeInfo(element.Expression).Type is IArrayTypeSymbol { Rank: 1 }
                 && element.ArgumentList.Arguments.Count == 1
                 && !element.ArgumentList.Arguments[0].Expression.IsKind(SyntaxKind.IndexExpression)
                 && element.ArgumentList.Arguments[0].Expression is not RangeExpressionSyntax)
        {
            head = Capture(() =>
            {
                _w.Write("TransposeR.updElem(");
                EmitExpression(element.Expression);
                _w.Write(", ");
                EmitExpression(element.ArgumentList.Arguments[0].Expression);
                _w.Write(", ");
            });
        }
        else
        {
            return false;
        }

        var previous = _exprOverride;
        _exprOverride = (element, "$x");
        string value;
        try { value = Capture(() => EmitCompoundElementValue(assignment, op, leftType, rightType)); }
        finally { _exprOverride = previous; }

        _w.Write(head);
        _w.Write($"($x) => {value}");
        _w.Write(symbol is IPropertySymbol ? ", true)" : ")");
        return true;
    }

    /// <summary>True if evaluating <paramref name="expr"/> can change state or give a different answer
    /// the second time: a call, an assignment, an increment, an object creation. A lambda body does not
    /// run where it is written, so it is not looked into.</summary>
    private static bool HasSideEffects(ExpressionSyntax expr)
    {
        foreach (var n in expr.DescendantNodesAndSelf(descendIntoChildren: c => c is not AnonymousFunctionExpressionSyntax))
        {
            switch (n)
            {
                case InvocationExpressionSyntax:
                case BaseObjectCreationExpressionSyntax:
                case AssignmentExpressionSyntax:
                case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PostIncrementExpression or (int)SyntaxKind.PostDecrementExpression }:
                case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PreIncrementExpression or (int)SyntaxKind.PreDecrementExpression }:
                    return true;
            }
        }
        return false;
    }
}
