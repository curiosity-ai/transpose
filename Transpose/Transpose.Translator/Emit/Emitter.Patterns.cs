using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

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
                else if (_model.GetSymbolInfo(constant.Expression).Symbol is ITypeSymbol typeSym)
                    // A bare identifier/qualified name that binds to a TYPE is a type pattern the
                    // parser represented as a constant pattern (`o switch { SomeType => ... }` or a
                    // nested `{ Prop: SomeType }`). Emit a type test, not `subject === <typeref>`
                    // (which compares the value to the type's constructor and is always false).
                    EmitTypeTest(subject, typeSym);
                else
                {
                    EmitConstantEqualityTest(subject, constant.Expression);
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
                EmitRelationalPatternTest(subject, rel);
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

            case ListPatternSyntax listPat:
                EmitListPattern(subject, listPat);
                break;

            default:
                Unsupported(pattern, pattern.Kind().ToString());
                break;
        }
    }

    /// <summary>
    /// C# 11 list pattern `[p0, p1, .. rest, pk]` over an array. Emits a length check plus
    /// per-element tests (pre-slice indexed from the start, post-slice from the end); a slice
    /// designation binds the middle span via .slice(...).
    /// </summary>
    private void EmitListPattern(string subject, ListPatternSyntax list)
    {
        var patterns = list.Patterns;
        var sliceIndex = -1;
        for (var i = 0; i < patterns.Count; i++)
            if (patterns[i] is SlicePatternSyntax) { sliceIndex = i; break; }
        var hasSlice = sliceIndex >= 0;
        var preCount = hasSlice ? sliceIndex : patterns.Count;
        var postCount = hasSlice ? patterns.Count - sliceIndex - 1 : 0;

        _w.Write($"({subject} != null && {subject}.length {(hasSlice ? ">=" : "===")} {preCount + postCount}");

        for (var i = 0; i < preCount; i++)
        {
            _w.Write(" && ");
            EmitPatternTest($"{subject}[{i}]", patterns[i]);
        }
        for (var j = 0; j < postCount; j++)
        {
            _w.Write(" && ");
            EmitPatternTest($"{subject}[{subject}.length - {postCount - j}]", patterns[sliceIndex + 1 + j]);
        }

        // A `.. rest` slice designation binds the middle span.
        if (hasSlice && ((SlicePatternSyntax)patterns[sliceIndex]).Pattern is { } slicePat)
        {
            var name = slicePat switch
            {
                VarPatternSyntax { Designation: SingleVariableDesignationSyntax d } => d.Identifier.Text,
                DeclarationPatternSyntax { Designation: SingleVariableDesignationSyntax d2 } => d2.Identifier.Text,
                _ => null,
            };
            if (name is not null)
                _w.Write($" && ({NameMangler.JsIdentifier(name)} = {subject}.slice({preCount}, {subject}.length - {postCount}), true)");
        }

        _w.Write(")");
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
                var memberSubject = $"{subject}.{PropertyPatternMemberJsName(sub)}";
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

    /// <summary>
    /// Resolves the emitted JS member name for a property-pattern subpattern (<c>{ Member: pat }</c>).
    /// Must go through the semantic model + <see cref="TransposeNaming.MemberJsName"/> so members with a
    /// JS-name override ([Name]/[Convention], e.g. <c>string.Length</c> → <c>length</c>) match the name
    /// used everywhere else — a raw identifier mangle would emit <c>.Length</c> and never match.
    /// </summary>
    private string PropertyPatternMemberJsName(SubpatternSyntax sub)
    {
        var nameExpr = sub.NameColon?.Name ?? sub.ExpressionColon?.Expression;
        if (nameExpr is not null && PropertyPatternMemberPath(nameExpr) is { } path)
            return path;

        var fallback = sub.NameColon?.Name.Identifier.Text
            ?? sub.ExpressionColon?.Expression.ToString()
            ?? "";
        return NameMangler.JsIdentifier(fallback);
    }

    /// <summary>
    /// Builds the dotted JS member path for a property-pattern subpattern name. A simple pattern
    /// names one member (<c>{ Length: … }</c>); an extended pattern (C# 10) names a chain
    /// (<c>{ Address.City: … }</c>) that must expand to <c>Address.City</c> — each segment resolved
    /// through <see cref="TransposeNaming.MemberJsName"/>. Returns null if any segment can't be
    /// resolved (caller falls back to a mangled identifier).
    /// </summary>
    private string? PropertyPatternMemberPath(ExpressionSyntax expr)
    {
        switch (expr)
        {
            case IdentifierNameSyntax:
                var s = _model.GetSymbolInfo(expr).Symbol;
                return s is IPropertySymbol or IFieldSymbol
                    ? TransposeNaming.MemberJsName(s)
                    : null;
            case MemberAccessExpressionSyntax ma:
                var left = PropertyPatternMemberPath(ma.Expression);
                if (left is null) return null;
                var m = _model.GetSymbolInfo(ma.Name).Symbol;
                return m is IPropertySymbol or IFieldSymbol
                    ? left + "." + TransposeNaming.MemberJsName(m)
                    : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Emits a value-equality test between an already-emitted <paramref name="subject"/> and a
    /// constant expression. long/ulong/decimal are System.Int64/UInt64/Decimal *instances* at
    /// runtime, so JS <c>===</c> compares object identity and is never true for two separately
    /// constructed values — <c>switch (someLong) { case 2L: }</c> and <c>x is 2L</c> silently fell
    /// through to the default arm. Those go through <c>Transpose.equals</c>; everything else keeps
    /// <c>===</c>.
    /// </summary>
    private void EmitConstantEqualityTest(string subject, ExpressionSyntax constant)
    {
        var info = _model.GetTypeInfo(constant);
        EmitConstantEqualityAgainst(subject, Capture(() => EmitExpression(constant)), info.ConvertedType ?? info.Type);
    }

    /// <summary>
    /// <see cref="EmitConstantEqualityTest"/> against an already-emitted constant (for callers whose
    /// right-hand side is not an emittable expression node — see the is-expression path, where an enum
    /// member arrives as type-name syntax).
    /// </summary>
    private void EmitConstantEqualityAgainst(string subject, string constantJs, ITypeSymbol? constantType)
    {
        if (IsRuntimeObjectNumeric(constantType))
        {
            // The null guard matters when the subject is a Nullable: `((long?)null) is 2L` is false in
            // C#, but Transpose.equals would reach into the null's `.low` and throw.
            _w.Write($"({subject} != null && Transpose.equals({subject}, {constantJs}))");
            return;
        }

        _w.Write($"{subject} === {constantJs}");
    }

    /// <summary>
    /// A relational pattern (<c>is &gt; 10L</c>). System.Int64/UInt64/Decimal instances have no
    /// <c>valueOf</c>, so a JS <c>&gt;</c> coerces both operands to STRINGS and compares them
    /// lexicographically — <c>9L is &gt; 10L</c> came out true ("9" &gt; "10"). Route those through the
    /// type's own comparison method, as the binary-operator path already does.
    /// </summary>
    private void EmitRelationalPatternTest(string subject, RelationalPatternSyntax rel)
    {
        var info = _model.GetTypeInfo(rel.Expression);
        var method = rel.OperatorToken.Text switch
        {
            ">" => "gt",
            ">=" => "gte",
            "<" => "lt",
            "<=" => "lte",
            _ => null,
        };

        if (method is not null && IsRuntimeObjectNumeric(info.ConvertedType ?? info.Type))
        {
            // A Nullable subject that is null matches no relational pattern.
            _w.Write($"({subject} != null && {subject}.{method}(");
            EmitExpression(rel.Expression);
            _w.Write("))");
            return;
        }

        _w.Write($"{subject} {rel.OperatorToken.Text} ");
        EmitExpression(rel.Expression);
    }

    /// <summary>long / ulong / decimal, or the Nullable form of one — the numeric types tps.js models
    /// as objects rather than plain JS numbers.</summary>
    private static bool IsRuntimeObjectNumeric(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1 } nullable)
            type = nullable.TypeArguments[0];

        return Is64BitInteger(type) || IsDecimalType(type);
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
            // long/ulong are excluded — see the note below.
            case SpecialType.System_Int32 or SpecialType.System_Int16
                or SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_UInt16
                or SpecialType.System_UInt32 or SpecialType.System_Char:
                _w.Write($"(typeof {subject} === \"number\" && Number.isInteger({subject}))");
                return;
            case SpecialType.System_Double or SpecialType.System_Single:
                _w.Write($"typeof {subject} === \"number\"");
                return;
        }

        // Note: enums, long/ulong and decimal fall through to the runtime type check below.
        // Enums box to `object` as a Transpose.box carrying their enum type (so o.GetType()/
        // o.ToString() stay correct) and long/ulong/decimal box as System.Int64/UInt64/Decimal
        // instances — none of them a plain number. A `typeof === "number"` test would both miss the
        // boxed form and fail to tell one such type from another (`case long l` matched a boxed int,
        // then `l.gt(…)` threw "l.gt is not a function"). TransposeR.is understands the boxed
        // representations, matching the plain `x is T` expression path.
        _w.Write($"TransposeR.is({subject}, {TypeRef(type)})");
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
        var isTuple = rhsType is { IsTupleType: true } || assign.Right is TupleExpressionSyntax;
        EmitDeconstructionBindings(targets, temp, isTuple, rhsType);
    }

    /// <summary>
    /// One position of a deconstruction's left-hand side: a newly declared local
    /// (<c>DeclaredName</c>), an existing lvalue to assign (<c>Assignee</c>), a nested group
    /// (<c>Nested</c>), or — all three null — a discard.
    /// </summary>
    private readonly record struct DeconstructionTarget(
        string?                        DeclaredName,
        ExpressionSyntax?              Assignee,
        List<DeconstructionTarget>?    Nested)
    {
        public static readonly DeconstructionTarget Discard = new(null, null, null);

        public static DeconstructionTarget Declare(string name)               => new(name, null, null);
        public static DeconstructionTarget Assign(ExpressionSyntax lvalue)    => new(null, lvalue, null);
        public static DeconstructionTarget Group(List<DeconstructionTarget> t) => new(null, null, t);

        public bool IsDiscard => DeclaredName is null && Assignee is null && Nested is null;
    }

    /// <summary>
    /// Binds deconstruction targets from an already-evaluated value <paramref name="temp"/>:
    /// tuple elements read <c>temp.Item{n}</c>, otherwise the value's Deconstruct(out …) runs.
    /// A target that is not a fresh local is written through <see cref="EmitSimpleAssignmentTo"/>, so
    /// a field, property, indexer or array element is qualified and stored the same way a plain
    /// assignment to it would be — emitting the bare source name would produce an undeclared global.
    /// </summary>
    private void EmitDeconstructionBindings(
        List<DeconstructionTarget> targets, string temp, bool isTuple, ITypeSymbol? valueType)
    {
        var elementTypes = DeconstructionElementTypes(valueType, targets.Count);

        if (isTuple)
        {
            for (var i = 0; i < targets.Count; i++)
            {
                EmitDeconstructionBinding(targets[i], $"{temp}.Item{i + 1}", elementTypes[i], declare: true);
            }
            return;
        }

        // Deconstruct method (out params) — pass holders and read back.
        var holders = targets.Select((_, i) => $"{temp}_h{i}").ToList();
        for (var i = 0; i < targets.Count; i++)
        {
            if (targets[i].DeclaredName is { } name) _w.WriteLine($"let {NameMangler.JsIdentifier(name)};");
            _w.WriteLine($"let {holders[i]} = {{ v: null }};");
        }

        _w.WriteLine($"{temp}.Deconstruct({string.Join(", ", holders)});");

        for (var i = 0; i < targets.Count; i++)
        {
            EmitDeconstructionBinding(targets[i], $"{holders[i]}.v", elementTypes[i], declare: false);
        }
    }

    /// <summary>Binds one deconstruction position to the JavaScript expression <paramref name="value"/>.
    /// <paramref name="declare"/> is false on the Deconstruct path, where fresh locals were already
    /// declared ahead of the call.</summary>
    private void EmitDeconstructionBinding(DeconstructionTarget target, string value, ITypeSymbol? valueType, bool declare)
    {
        if (target.IsDiscard) return; // position preserved, no binding

        if (target.Nested is { } nested)
        {
            var sub = NextTemp("$dc");
            _w.WriteLine($"let {sub} = {value};");
            EmitDeconstructionBindings(nested, sub, valueType is { IsTupleType: true }, valueType);
            return;
        }

        if (target.DeclaredName is { } name)
        {
            _w.WriteLine($"{(declare ? "let " : "")}{NameMangler.JsIdentifier(name)} = {value};");
            return;
        }

        EmitSimpleAssignmentTo(target.Assignee!, () => _w.Write(value));
        _w.WriteLine(";");
    }

    /// <summary>The element types a value of <paramref name="valueType"/> deconstructs into — needed to
    /// type a nested group. Nulls where the shape cannot be resolved.</summary>
    private static ITypeSymbol?[] DeconstructionElementTypes(ITypeSymbol? valueType, int count)
    {
        if (valueType is INamedTypeSymbol { IsTupleType: true } tuple && tuple.TupleElements.Length == count)
            return tuple.TupleElements.Select(e => (ITypeSymbol?)e.Type).ToArray();

        var deconstruct = valueType?.GetMembers("Deconstruct").OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.Parameters.Length == count && m.Parameters.All(p => p.RefKind == RefKind.Out));

        if (deconstruct is not null) return deconstruct.Parameters.Select(p => (ITypeSymbol?)p.Type).ToArray();

        return new ITypeSymbol?[count];
    }

    private IEnumerable<DeconstructionTarget> CollectDeconstructionTargets(ExpressionSyntax left)
    {
        switch (left)
        {
            case TupleExpressionSyntax tuple:
                foreach (var arg in tuple.Arguments) yield return TargetForExpression(arg.Expression);
                break;
            case DeclarationExpressionSyntax { Designation: { } designation }:
                foreach (var t in TargetsForDesignation(designation)) yield return t;
                break;
        }
    }

    private DeconstructionTarget TargetForExpression(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case DeclarationExpressionSyntax decl:
                return TargetForDesignation(decl.Designation);
            case TupleExpressionSyntax nested:
                return DeconstructionTarget.Group(CollectDeconstructionTargets(nested).ToList());
            default:
                if (_model.GetSymbolInfo(expression).Symbol is IDiscardSymbol) return DeconstructionTarget.Discard;
                return DeconstructionTarget.Assign(expression);
        }
    }

    private DeconstructionTarget TargetForDesignation(VariableDesignationSyntax designation) => designation switch
    {
        SingleVariableDesignationSyntax single => DeconstructionTarget.Declare(single.Identifier.Text),
        ParenthesizedVariableDesignationSyntax => DeconstructionTarget.Group(TargetsForDesignation(designation).ToList()),
        _                                      => DeconstructionTarget.Discard,
    };

    private IEnumerable<DeconstructionTarget> TargetsForDesignation(VariableDesignationSyntax designation)
        => designation is ParenthesizedVariableDesignationSyntax paren
            ? paren.Variables.Select(TargetForDesignation)
            : Enumerable.Empty<DeconstructionTarget>();
}
