using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

/// <summary>
/// C# 14 user-defined <b>instance</b> compound-assignment and increment/decrement operators:
/// <c>public void operator +=(Money other)</c>, <c>public void operator ++()</c>. Unlike every other
/// user-defined operator these are instance methods that mutate their receiver and return nothing, so
/// <c>m += x</c> is a call on <c>m</c> — <c>m.op_AdditionAssignment(x)</c> — never a JavaScript
/// <c>+=</c>, which concatenated the two objects' strings into <c>m</c>.
/// </summary>
public sealed partial class Emitter
{
    /// <summary>The instance operator <paramref name="node"/> binds to, or null. A static operator (the
    /// classic form) is handled by the ordinary operator paths.</summary>
    private IMethodSymbol? InstanceUserDefinedOperator(ExpressionSyntax node)
        => _model.GetSymbolInfo(node).Symbol is IMethodSymbol
           {
               MethodKind: MethodKind.UserDefinedOperator, IsStatic: false, IsImplicitlyDeclared: false,
           } op
           && (TransposeNaming.GetTemplate(op.OriginalDefinition) is not null || !op.IsExtern)
            ? op
            : null;

    /// <summary>
    /// Emits <c>target.op(arg)</c> for an instance operator. In a statement that is the whole
    /// expression; used as a value (<c>var r = (m += x);</c>, a prefix <c>++m</c>), C# yields the
    /// operand after the operation, so the call goes through <c>TransposeR.opAssign</c>, which evaluates
    /// the receiver once and hands it back.
    /// </summary>
    private bool TryEmitInstanceOperator(ExpressionSyntax node, ExpressionSyntax target, ExpressionSyntax? arg)
    {
        if (InstanceUserDefinedOperator(node) is not { } op) return false;

        var name = TransposeNaming.MemberJsName(op);
        if (IsVoidContext(node))
        {
            EmitExpression(target);
            _w.Write($".{name}(");
            if (arg is not null) EmitExpressionConverted(arg, op.Parameters[0].Type);
            _w.Write(")");
            return true;
        }

        _w.Write("TransposeR.opAssign(");
        EmitExpression(target);
        _w.Write($", {JsString(name)}");
        if (arg is not null)
        {
            _w.Write(", ");
            EmitExpressionConverted(arg, op.Parameters[0].Type);
        }
        _w.Write(")");
        return true;
    }
}
