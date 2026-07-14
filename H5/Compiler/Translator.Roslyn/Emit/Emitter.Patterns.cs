using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace H5.Translator.Roslyn;

public sealed partial class Emitter
{
    // ---- is-pattern expression --------------------------------------------

    private void EmitIsPattern(IsPatternExpressionSyntax isPattern)
    {
        // Evaluate the operand once into a temp so bindings and tests share it.
        var subject = NextTemp("$is");
        _w.Write($"(function ({subject}) {{ return ");
        EmitPatternTest(subject, isPattern.Pattern);
        _w.Write("; })(");
        EmitExpression(isPattern.Expression);
        _w.Write(")");
    }

    // ---- switch expression -------------------------------------------------

    private void EmitSwitchExpression(SwitchExpressionSyntax switchExpr)
    {
        var subject = NextTemp("$sw");
        _w.Write($"(function ({subject}) {{ ");

        // Pre-declare pattern variables bound in the arms (e.g. `int i` in `> 0 and int i`).
        var patternVars = new List<string>();
        foreach (var arm in switchExpr.Arms)
        {
            CollectInlineDesignations(arm.Pattern, isRoot: true, patternVars);
            if (arm.WhenClause is not null) CollectInlineDesignations(arm.WhenClause, isRoot: true, patternVars);
        }
        foreach (var v in patternVars.Distinct())
            _w.Write($"var {NameMangler.JsIdentifier(v)}; ");

        foreach (var arm in switchExpr.Arms)
        {
            _w.Write("if (");
            EmitPatternTest(subject, arm.Pattern);
            if (arm.WhenClause is not null)
            {
                _w.Write(" && (");
                EmitExpression(arm.WhenClause.Condition);
                _w.Write(")");
            }
            _w.Write(") { return ");
            EmitExpression(arm.Expression);
            _w.Write("; } ");
        }
        _w.Write("throw new System.InvalidOperationException(\"No matching switch arm\"); })(");
        EmitExpression(switchExpr.GoverningExpression);
        _w.Write(")");
    }

    // ---- pattern test emission ---------------------------------------------

    /// <summary>
    /// Writes a JavaScript boolean expression that tests <paramref name="subject"/>
    /// against the pattern, binding any pattern variables as a side effect
    /// (variables are pre-declared by <see cref="PredeclareInlineVars"/> / IIFE scope).
    /// </summary>
    private void EmitPatternTest(string subject, PatternSyntax pattern)
    {
        switch (pattern)
        {
            case ConstantPatternSyntax constant:
                if (constant.Expression.IsKind(SyntaxKind.NullLiteralExpression))
                    _w.Write($"{subject} == null");
                else
                {
                    _w.Write($"{subject} === ");
                    EmitExpression(constant.Expression);
                }
                break;

            case DiscardPatternSyntax:
                _w.Write("true");
                break;

            case DeclarationPatternSyntax decl:
                _w.Write("(");
                EmitTypeTest(subject, _model.GetTypeInfo(decl.Type).Type);
                EmitDesignationBinding(subject, decl.Designation);
                _w.Write(")");
                break;

            case TypePatternSyntax typePat:
                EmitTypeTest(subject, _model.GetTypeInfo(typePat.Type).Type);
                break;

            case VarPatternSyntax varPat:
                _w.Write("(");
                EmitDesignationBindingBare(subject, varPat.Designation);
                _w.Write("true)");
                break;

            case RelationalPatternSyntax rel:
                _w.Write($"{subject} {rel.OperatorToken.Text} ");
                EmitExpression(rel.Expression);
                break;

            case ParenthesizedPatternSyntax paren:
                _w.Write("(");
                EmitPatternTest(subject, paren.Pattern);
                _w.Write(")");
                break;

            case UnaryPatternSyntax unary when unary.IsKind(SyntaxKind.NotPattern):
                _w.Write("!(");
                EmitPatternTest(subject, unary.Pattern);
                _w.Write(")");
                break;

            case BinaryPatternSyntax binary:
                var op = binary.IsKind(SyntaxKind.AndPattern) ? " && " : " || ";
                _w.Write("(");
                EmitPatternTest(subject, binary.Left);
                _w.Write(op);
                EmitPatternTest(subject, binary.Right);
                _w.Write(")");
                break;

            case RecursivePatternSyntax recursive:
                EmitRecursivePattern(subject, recursive);
                break;

            default:
                Unsupported(pattern, pattern.Kind().ToString());
                break;
        }
    }

    private void EmitRecursivePattern(string subject, RecursivePatternSyntax recursive)
    {
        _w.Write("(");
        var wroteCondition = false;

        _w.Write($"{subject} != null");
        wroteCondition = true;

        if (recursive.Type is not null)
        {
            _w.Write(" && ");
            EmitTypeTest(subject, _model.GetTypeInfo(recursive.Type).Type);
        }

        if (recursive.PropertyPatternClause is not null)
        {
            foreach (var sub in recursive.PropertyPatternClause.Subpatterns)
            {
                _w.Write(" && ");
                var memberName = sub.NameColon?.Name.Identifier.Text
                    ?? sub.ExpressionColon?.Expression.ToString()
                    ?? "";
                var memberSubject = $"{subject}.{NameMangler.JsIdentifier(memberName)}";
                EmitPatternTest(memberSubject, sub.Pattern);
            }
        }

        if (recursive.PositionalPatternClause is not null)
        {
            var i = 0;
            foreach (var sub in recursive.PositionalPatternClause.Subpatterns)
            {
                _w.Write(" && ");
                EmitPatternTest($"{subject}.Item{i + 1}", sub.Pattern);
                i++;
            }
        }

        if (recursive.Designation is not null)
        {
            EmitDesignationBinding(subject, recursive.Designation);
        }

        _ = wroteCondition;
        _w.Write(")");
    }

    private void EmitTypeTest(string subject, ITypeSymbol? type)
    {
        if (type is null) { _w.Write("true"); return; }

        // Primitive/value-type "is" tests via typeof where possible.
        switch (type.SpecialType)
        {
            case SpecialType.System_String:
                _w.Write($"typeof {subject} === \"string\"");
                return;
            case SpecialType.System_Boolean:
                _w.Write($"typeof {subject} === \"boolean\"");
                return;
            // Integer types: number AND integral (best-effort distinction from double).
            case SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Int16
                or SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_UInt16
                or SpecialType.System_UInt32 or SpecialType.System_UInt64 or SpecialType.System_Char:
                _w.Write($"(typeof {subject} === \"number\" && Number.isInteger({subject}))");
                return;
            case SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Decimal:
                _w.Write($"typeof {subject} === \"number\"");
                return;
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            _w.Write($"typeof {subject} === \"number\"");
            return;
        }

        _w.Write($"H5R.is({subject}, {_names.TypeReference(type)})");
    }

    private void EmitDesignationBinding(string subject, VariableDesignationSyntax designation)
    {
        if (designation is SingleVariableDesignationSyntax single)
        {
            _w.Write($" && ({NameMangler.JsIdentifier(single.Identifier.Text)} = {subject}, true)");
        }
    }

    private void EmitDesignationBindingBare(string subject, VariableDesignationSyntax designation)
    {
        if (designation is SingleVariableDesignationSyntax single)
        {
            _w.Write($"{NameMangler.JsIdentifier(single.Identifier.Text)} = {subject}, ");
        }
    }

    private int _tempCounter;
    private string NextTemp(string prefix) => $"{prefix}{_tempCounter++}";

    // ---- deconstruction ----------------------------------------------------

    private bool IsDeconstruction(AssignmentExpressionSyntax assign)
        => assign.OperatorToken.IsKind(SyntaxKind.EqualsToken)
           && (assign.Left is TupleExpressionSyntax
               || assign.Left is DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax });

    private void EmitDeconstruction(AssignmentExpressionSyntax assign)
    {
        var targets = CollectDeconstructionTargets(assign.Left).ToList();
        var temp = NextTemp("$dc");

        _w.Write($"let {temp} = ");
        EmitExpression(assign.Right);
        _w.WriteLine(";");

        var rhsType = _model.GetTypeInfo(assign.Right).Type;

        if (rhsType is { IsTupleType: true } || assign.Right is TupleExpressionSyntax)
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var (name, isNew, isDiscard) = targets[i];
                if (isDiscard) continue; // position preserved, no binding
                _w.WriteLine($"{(isNew ? "let " : "")}{NameMangler.JsIdentifier(name!)} = {temp}.Item{i + 1};");
            }
            return;
        }

        // Deconstruct method (out params) — pass holders and read back.
        var holders = targets.Select((_, i) => $"{temp}_h{i}").ToList();
        for (var i = 0; i < targets.Count; i++)
        {
            if (targets[i].isNew && !targets[i].isDiscard) _w.WriteLine($"let {NameMangler.JsIdentifier(targets[i].name!)};");
            _w.WriteLine($"let {holders[i]} = {{ v: null }};");
        }
        _w.WriteLine($"{temp}.Deconstruct({string.Join(", ", holders)});");
        for (var i = 0; i < targets.Count; i++)
        {
            if (targets[i].isDiscard) continue;
            _w.WriteLine($"{NameMangler.JsIdentifier(targets[i].name!)} = {holders[i]}.v;");
        }
    }

    private System.Collections.Generic.IEnumerable<(string? name, bool isNew, bool isDiscard)> CollectDeconstructionTargets(ExpressionSyntax left)
    {
        switch (left)
        {
            case TupleExpressionSyntax tuple:
                foreach (var arg in tuple.Arguments)
                {
                    switch (arg.Expression)
                    {
                        case DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax d }:
                            yield return (d.Identifier.Text, true, false);
                            break;
                        case DeclarationExpressionSyntax { Designation: DiscardDesignationSyntax }:
                            yield return (null, false, true);
                            break;
                        case IdentifierNameSyntax { Identifier.Text: "_" } when _model.GetSymbolInfo(arg.Expression).Symbol is IDiscardSymbol:
                            yield return (null, false, true);
                            break;
                        case IdentifierNameSyntax id:
                            yield return (id.Identifier.Text, false, false);
                            break;
                    }
                }
                break;
            case DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax paren }:
                foreach (var v in paren.Variables)
                {
                    if (v is SingleVariableDesignationSyntax single)
                        yield return (single.Identifier.Text, true, false);
                    else if (v is DiscardDesignationSyntax)
                        yield return (null, false, true);
                }
                break;
        }
    }
}
