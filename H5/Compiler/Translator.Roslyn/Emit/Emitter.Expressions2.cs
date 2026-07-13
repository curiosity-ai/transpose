using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace H5.Translator.Roslyn;

public sealed partial class Emitter
{
    // ---- invocation --------------------------------------------------------

    private void EmitInvocation(InvocationExpressionSyntax invocation)
    {
        var symbol = _model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;

        // base.Method(...) → Base.prototype.Method.call(this, args)
        if (invocation.Expression is MemberAccessExpressionSyntax { Expression: BaseExpressionSyntax } baseAccess
            && symbol is not null)
        {
            _w.Write($"{_names.TypeReference(symbol.ContainingType)}.prototype.{_names.MethodName(symbol)}.call(this");
            if (invocation.ArgumentList.Arguments.Count > 0) { _w.Write(", "); EmitArguments(invocation.ArgumentList, symbol); }
            _w.Write(")");
            return;
        }

        // Console.Write/WriteLine(char) must display the character, not its code point.
        if (symbol is { ContainingType.Name: "Console", Name: "Write" or "WriteLine" }
            && symbol.ContainingType.ContainingNamespace?.Name == "System"
            && invocation.ArgumentList.Arguments.Count == 1
            && IsCharType(_model.GetTypeInfo(invocation.ArgumentList.Arguments[0].Expression).Type))
        {
            _w.Write($"System.Console.{symbol.Name}(H5R.chr(");
            EmitExpression(invocation.ArgumentList.Arguments[0].Expression);
            _w.Write("))");
            return;
        }

        // x.ToString()  → H5R.toStr(x)
        if (symbol is { Name: "ToString", Parameters.Length: 0 }
            && invocation.Expression is MemberAccessExpressionSyntax toStrAccess)
        {
            _w.Write("H5R.toStr(");
            EmitExpression(toStrAccess.Expression);
            _w.Write(")");
            return;
        }

        // Delegate invocation: symbol is the Invoke method of a delegate type.
        if (symbol is { MethodKind: MethodKind.DelegateInvoke })
        {
            EmitExpression(invocation.Expression);
            _w.Write("(");
            EmitArguments(invocation.ArgumentList, symbol);
            _w.Write(")");
            return;
        }

        if (symbol is null)
        {
            // Fallback: emit target then args verbatim.
            EmitExpression(invocation.Expression);
            _w.Write("(");
            EmitArguments(invocation.ArgumentList, null);
            _w.Write(")");
            return;
        }

        // Extension method: Ext(this x, ...) called as x.Ext(...) → StaticType.Ext(x, ...)
        if (symbol.IsExtensionMethod && symbol.ReducedFrom is not null
            && invocation.Expression is MemberAccessExpressionSyntax extAccess)
        {
            _w.Write($"{_names.TypeReference(symbol.ContainingType)}.{_names.MethodName(symbol.ReducedFrom)}(");
            EmitExpression(extAccess.Expression);
            if (invocation.ArgumentList.Arguments.Count > 0) { _w.Write(", "); EmitArguments(invocation.ArgumentList, symbol.ReducedFrom); }
            _w.Write(")");
            return;
        }

        // by-ref (out/ref/in) arguments need holder objects with write-back.
        if (HasByRefArguments(invocation.ArgumentList, symbol))
        {
            EmitByRefInvocation(invocation, symbol);
            return;
        }

        EmitCallee(invocation, symbol);
        _w.Write("(");
        EmitArguments(invocation.ArgumentList, symbol);
        _w.Write(")");
    }

    private void EmitCallee(InvocationExpressionSyntax invocation, IMethodSymbol symbol)
    {
        if (symbol.IsStatic)
        {
            _w.Write($"{_names.TypeReference(symbol.ContainingType)}.{_names.MethodName(symbol)}");
        }
        else if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            EmitExpression(memberAccess.Expression);
            _w.Write($".{_names.MethodName(symbol)}");
        }
        else
        {
            // Unqualified instance call → implicit this.
            _w.Write($"this.{_names.MethodName(symbol)}");
        }
    }

    private bool HasByRefArguments(ArgumentListSyntax argList, IMethodSymbol symbol)
    {
        foreach (var arg in argList.Arguments)
        {
            if (arg.RefKindKeyword.RawKind != 0
                && (arg.RefKindKeyword.IsKind(SyntaxKind.OutKeyword) || arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword)))
            {
                return true;
            }
        }
        return false;
    }

    private void EmitByRefInvocation(InvocationExpressionSyntax invocation, IMethodSymbol symbol)
    {
        var args = invocation.ArgumentList.Arguments;
        var holders = new string?[args.Count];

        _w.Write("(function () { ");

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            var isOut = arg.RefKindKeyword.IsKind(SyntaxKind.OutKeyword);
            var isRef = arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword);
            if (!isOut && !isRef) continue;

            var holder = "$ref" + i;
            holders[i] = holder;
            _w.Write($"var {holder} = {{ v: ");
            if (isOut)
            {
                var t = i < symbol.Parameters.Length ? symbol.Parameters[i].Type : null;
                _w.Write(t is not null ? DefaultValueLiteral(t) : "null");
            }
            else
            {
                EmitExpression(arg.Expression); // ref: seed with current value
            }
            _w.Write(" }; ");
        }

        _w.Write("var $ret = ");
        EmitCallee(invocation, symbol);
        _w.Write("(");
        for (var i = 0; i < args.Count; i++)
        {
            if (i > 0) _w.Write(", ");
            if (holders[i] is not null) _w.Write(holders[i]!);
            else EmitExpressionConverted(args[i].Expression, i < symbol.Parameters.Length ? symbol.Parameters[i].Type : null);
        }
        _w.Write("); ");

        // Write back out/ref values to their targets.
        for (var i = 0; i < args.Count; i++)
        {
            if (holders[i] is null) continue;
            EmitByRefWriteBackTarget(args[i].Expression);
            _w.Write($" = {holders[i]}.v; ");
        }

        _w.Write("return $ret; })()");
    }

    private void EmitByRefWriteBackTarget(ExpressionSyntax expr)
    {
        if (expr is DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax decl })
        {
            _w.Write(NameMangler.JsIdentifier(decl.Identifier.Text));
        }
        else
        {
            EmitExpression(expr);
        }
    }

    private void EmitArguments(ArgumentListSyntax argList, IMethodSymbol? method)
    {
        var args = argList.Arguments;

        // Reorder named arguments to parameter order when we know the method.
        if (method is not null && args.Any(a => a.NameColon is not null))
        {
            var ordered = new ExpressionSyntax?[method.Parameters.Length];
            var positional = 0;
            foreach (var arg in args)
            {
                if (arg.NameColon is not null)
                {
                    var idx = method.Parameters.ToList().FindIndex(p => p.Name == arg.NameColon.Name.Identifier.Text);
                    if (idx >= 0) ordered[idx] = arg.Expression;
                }
                else
                {
                    ordered[positional++] = arg.Expression;
                }
            }

            var first = true;
            for (var i = 0; i < ordered.Length; i++)
            {
                if (ordered[i] is null) continue; // rely on callee default
                if (!first) _w.Write(", ");
                first = false;
                EmitExpressionConverted(ordered[i]!, i < method.Parameters.Length ? method.Parameters[i].Type : null);
            }
            return;
        }

        for (var i = 0; i < args.Count; i++)
        {
            if (i > 0) _w.Write(", ");
            var targetType = method is not null && i < method.Parameters.Length ? method.Parameters[i].Type : null;
            EmitExpressionConverted(args[i].Expression, targetType);
        }
    }

    private void EmitArgumentList(ArgumentListSyntax argList) => EmitArguments(argList, null);
    private void EmitArgumentList(BracketedArgumentListSyntax argList)
    {
        for (var i = 0; i < argList.Arguments.Count; i++)
        {
            if (i > 0) _w.Write(", ");
            EmitExpression(argList.Arguments[i].Expression);
        }
    }

    // ---- object creation ---------------------------------------------------

    private void EmitObjectCreation(ObjectCreationExpressionSyntax creation)
    {
        var symbol = _model.GetSymbolInfo(creation).Symbol as IMethodSymbol;
        var type = _model.GetTypeInfo(creation).Type as INamedTypeSymbol ?? symbol?.ContainingType;
        EmitConstructionCore(type, symbol, creation.ArgumentList, creation.Initializer);
    }

    private void EmitImplicitObjectCreation(ImplicitObjectCreationExpressionSyntax creation)
    {
        var symbol = _model.GetSymbolInfo(creation).Symbol as IMethodSymbol;
        var type = _model.GetTypeInfo(creation).Type as INamedTypeSymbol ?? symbol?.ContainingType;
        EmitConstructionCore(type, symbol, creation.ArgumentList, creation.Initializer);
    }

    private void EmitConstructionCore(INamedTypeSymbol? type, IMethodSymbol? ctor, ArgumentListSyntax? argList, InitializerExpressionSyntax? initializer)
    {
        if (initializer is { Expressions.Count: > 0 })
        {
            _w.Write("(function () { var $o = ");
            EmitBareConstruction(type, ctor, argList);
            _w.Write("; ");
            EmitInitializer("$o", initializer);
            _w.Write("return $o; })()");
            return;
        }

        EmitBareConstruction(type, ctor, argList);
    }

    private void EmitBareConstruction(INamedTypeSymbol? type, IMethodSymbol? ctor, ArgumentListSyntax? argList)
    {
        if (type is null)
        {
            _w.Write("{}");
            return;
        }

        // Delegate construction: new Func<...>(target) → target
        if (type.TypeKind == TypeKind.Delegate)
        {
            if (argList is { Arguments.Count: 1 }) EmitExpression(argList.Arguments[0].Expression);
            else _w.Write("null");
            return;
        }

        if (type.Locations.Any(l => l.IsInSource))
        {
            var ctorName = ctor is not null ? _names.MethodName(ctor) : "$ctor";
            _w.Write($"H5R.create({_names.TypeReference(type)}, \"{ctorName}\", [");
            if (argList is not null) EmitArguments(argList, ctor);
            _w.Write("])");
        }
        else
        {
            // BCL type: use JS `new` on the runtime-provided constructor.
            _w.Write($"new {_names.TypeReference(type)}(");
            if (argList is not null) EmitArguments(argList, ctor);
            _w.Write(")");
        }
    }

    private void EmitInitializer(string target, InitializerExpressionSyntax initializer)
    {
        foreach (var expr in initializer.Expressions)
        {
            switch (expr)
            {
                // Object initializer member: X = value
                case AssignmentExpressionSyntax { Left: IdentifierNameSyntax name } assign:
                    _w.Write($"{target}.{NameMangler.JsIdentifier(name.Identifier.Text)} = ");
                    EmitExpression(assign.Right);
                    _w.Write("; ");
                    break;
                // Index initializer: [key] = value
                case AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax idx } assign:
                    _w.Write($"{target}.set_Item(");
                    EmitArgumentList(idx.ArgumentList);
                    _w.Write(", ");
                    EmitExpression(assign.Right);
                    _w.Write("); ");
                    break;
                // Collection element with multiple values: { k, v }  (e.g. dictionary)
                case InitializerExpressionSyntax nested:
                    _w.Write($"{target}.Add(");
                    for (var i = 0; i < nested.Expressions.Count; i++)
                    {
                        if (i > 0) _w.Write(", ");
                        EmitExpression(nested.Expressions[i]);
                    }
                    _w.Write("); ");
                    break;
                // Collection element: single value
                default:
                    _w.Write($"{target}.Add(");
                    EmitExpression(expr);
                    _w.Write("); ");
                    break;
            }
        }
    }

    // ---- binary / unary / assignment --------------------------------------

    private void EmitBinary(BinaryExpressionSyntax binary)
    {
        var op = binary.OperatorToken.Text;

        var leftType = _model.GetTypeInfo(binary.Left).ConvertedType ?? _model.GetTypeInfo(binary.Left).Type;
        var rightType = _model.GetTypeInfo(binary.Right).ConvertedType ?? _model.GetTypeInfo(binary.Right).Type;
        var resultType = _model.GetTypeInfo(binary).Type;

        // is / as
        if (binary.IsKind(SyntaxKind.IsExpression))
        {
            var t = _model.GetTypeInfo(binary.Right).Type;
            _w.Write("H5R.is(");
            EmitExpression(binary.Left);
            _w.Write($", {_names.TypeReference(t!)})");
            return;
        }
        if (binary.IsKind(SyntaxKind.AsExpression))
        {
            var t = _model.GetTypeInfo(binary.Right).Type;
            _w.Write("H5R.as(");
            EmitExpression(binary.Left);
            _w.Write($", {_names.TypeReference(t!)})");
            return;
        }

        // String concatenation
        if (binary.IsKind(SyntaxKind.AddExpression)
            && (IsStringType(leftType) || IsStringType(rightType) || IsStringType(resultType)))
        {
            EmitConcatOperand(binary.Left, leftType);
            _w.Write(" + ");
            EmitConcatOperand(binary.Right, rightType);
            return;
        }

        // Integer division
        if (binary.IsKind(SyntaxKind.DivideExpression) && IsIntegerType(leftType) && IsIntegerType(rightType))
        {
            _w.Write("H5R.idiv(");
            EmitExpression(binary.Left);
            _w.Write(", ");
            EmitExpression(binary.Right);
            _w.Write(")");
            return;
        }

        // Null-coalescing
        if (binary.IsKind(SyntaxKind.CoalesceExpression))
        {
            _w.Write("(");
            EmitExpression(binary.Left);
            _w.Write(" ?? ");
            EmitExpression(binary.Right);
            _w.Write(")");
            return;
        }

        var jsOp = op switch
        {
            "==" => "===",
            "!=" => "!==",
            _ => op,
        };

        EmitExpression(binary.Left);
        _w.Write($" {jsOp} ");
        EmitExpression(binary.Right);
    }

    private void EmitConcatOperand(ExpressionSyntax operand, ITypeSymbol? type)
    {
        if (IsStringType(type))
        {
            EmitExpression(operand);
        }
        else if (IsCharType(type))
        {
            _w.Write("H5R.chr(");
            EmitExpression(operand);
            _w.Write(")");
        }
        else
        {
            _w.Write("H5R.toStr(");
            EmitExpression(operand);
            _w.Write(")");
        }
    }

    private void EmitAssignment(AssignmentExpressionSyntax assignment)
    {
        var op = assignment.OperatorToken.Text;
        var leftType = _model.GetTypeInfo(assignment.Left).Type;
        var rightType = _model.GetTypeInfo(assignment.Right).Type;

        // Indexer set on a collection: coll[i] = v → coll.set_Item(i, v)
        if (op == "=" && assignment.Left is ElementAccessExpressionSyntax ea
            && _model.GetSymbolInfo(ea).Symbol is IPropertySymbol { IsIndexer: true } idx
            && idx.ContainingType.SpecialType != SpecialType.System_String)
        {
            EmitExpression(ea.Expression);
            _w.Write(".set_Item(");
            EmitArgumentList(ea.ArgumentList);
            _w.Write(", ");
            EmitExpressionConverted(assignment.Right, leftType);
            _w.Write(")");
            return;
        }

        // Compound string concat: s += x
        if (op == "+=" && IsStringType(leftType))
        {
            EmitExpression(assignment.Left);
            _w.Write(" = ");
            EmitConcatOperand(assignment.Left, leftType);
            _w.Write(" + ");
            EmitConcatOperand(assignment.Right, rightType);
            return;
        }

        // Compound integer division: x /= y
        if (op == "/=" && IsIntegerType(leftType) && IsIntegerType(rightType))
        {
            EmitExpression(assignment.Left);
            _w.Write(" = H5R.idiv(");
            EmitExpression(assignment.Left);
            _w.Write(", ");
            EmitExpression(assignment.Right);
            _w.Write(")");
            return;
        }

        EmitExpression(assignment.Left);
        _w.Write($" {op} ");
        EmitExpressionConverted(assignment.Right, leftType);
    }

    private void EmitPrefixUnary(PrefixUnaryExpressionSyntax prefix)
    {
        _w.Write(prefix.OperatorToken.Text);
        EmitExpression(prefix.Operand);
    }

    private void EmitPostfixUnary(PostfixUnaryExpressionSyntax postfix)
    {
        EmitExpression(postfix.Operand);
        _w.Write(postfix.OperatorToken.Text);
    }

    // ---- cast --------------------------------------------------------------

    private void EmitCast(CastExpressionSyntax cast)
    {
        var targetType = _model.GetTypeInfo(cast.Type).Type;
        var sourceType = _model.GetTypeInfo(cast.Expression).Type;

        // Numeric narrowing to an integer type truncates toward zero.
        if (IsIntegerType(targetType) && IsFloatingType(sourceType))
        {
            _w.Write("H5R.trunc(");
            EmitExpression(cast.Expression);
            _w.Write(")");
            return;
        }

        // char <-> int: chars are represented as their code point (number).
        // Other reference casts are erased.
        EmitExpression(cast.Expression);
    }

    // ---- interpolated string -----------------------------------------------

    private void EmitInterpolatedString(InterpolatedStringExpressionSyntax interp)
    {
        _w.Write("(");
        var first = true;
        var hadContent = false;
        foreach (var content in interp.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    if (!first) _w.Write(" + ");
                    _w.Write(JsString(text.TextToken.ValueText));
                    first = false; hadContent = true;
                    break;
                case InterpolationSyntax interpolation:
                    if (!first) _w.Write(" + ");
                    if (interpolation.FormatClause is not null)
                    {
                        _w.Write("H5R.formatValue(");
                        EmitExpression(interpolation.Expression);
                        _w.Write($", {JsString(interpolation.FormatClause.FormatStringToken.ValueText)})");
                    }
                    else if (IsCharType(_model.GetTypeInfo(interpolation.Expression).Type))
                    {
                        _w.Write("H5R.chr(");
                        EmitExpression(interpolation.Expression);
                        _w.Write(")");
                    }
                    else
                    {
                        _w.Write("H5R.toStr(");
                        EmitExpression(interpolation.Expression);
                        _w.Write(")");
                    }
                    first = false; hadContent = true;
                    break;
            }
        }
        if (!hadContent) _w.Write("\"\"");
        _w.Write(")");
    }

    // ---- element access ----------------------------------------------------

    private void EmitElementAccess(ElementAccessExpressionSyntax element)
    {
        var symbol = _model.GetSymbolInfo(element).Symbol;

        if (symbol is IPropertySymbol { IsIndexer: true } indexer)
        {
            // string[i] yields a char (code point).
            if (indexer.ContainingType.SpecialType == SpecialType.System_String)
            {
                EmitExpression(element.Expression);
                _w.Write(".charCodeAt(");
                EmitArgumentList(element.ArgumentList);
                _w.Write(")");
                return;
            }

            // Source types and BCL collections route through get_Item.
            EmitExpression(element.Expression);
            _w.Write(".get_Item(");
            EmitArgumentList(element.ArgumentList);
            _w.Write(")");
            return;
        }

        // Arrays: native element access.
        EmitExpression(element.Expression);
        _w.Write("[");
        EmitArgumentList(element.ArgumentList);
        _w.Write("]");
    }

    // ---- arrays ------------------------------------------------------------

    private void EmitArrayCreation(ArrayCreationExpressionSyntax array)
    {
        if (array.Initializer is not null)
        {
            EmitInitializerArray(array.Initializer);
            return;
        }

        // new T[n] → H5R.array(n, default)
        var rankSpec = array.Type.RankSpecifiers.FirstOrDefault();
        var elementType = _model.GetTypeInfo(array.Type.ElementType).Type;
        if (rankSpec is { Sizes.Count: 1 } && rankSpec.Sizes[0] is not OmittedArraySizeExpressionSyntax)
        {
            _w.Write("H5R.array(");
            EmitExpression(rankSpec.Sizes[0]);
            _w.Write($", {DefaultValueLiteral(elementType!)})");
            return;
        }

        _w.Write("[]");
    }

    private void EmitInitializerArray(InitializerExpressionSyntax initializer)
    {
        _w.Write("[");
        for (var i = 0; i < initializer.Expressions.Count; i++)
        {
            if (i > 0) _w.Write(", ");
            EmitExpression(initializer.Expressions[i]);
        }
        _w.Write("]");
    }

    // ---- lambda ------------------------------------------------------------

    private void EmitLambda(IEnumerable<string> parameters, CSharpSyntaxNode body, bool isAsync)
    {
        if (isAsync) _w.Write("async ");
        _w.Write("function (");
        _w.Write(string.Join(", ", parameters.Select(NameMangler.JsIdentifier)));
        _w.Write(") ");

        if (body is BlockSyntax block)
        {
            _w.Block(() => { foreach (var s in block.Statements) EmitStatement(s); });
        }
        else if (body is ExpressionSyntax exprBody)
        {
            _w.Block(() =>
            {
                // If the lambda's body has a value, return it.
                var typeInfo = _model.GetTypeInfo(exprBody);
                _w.Write("return ");
                EmitExpression(exprBody);
                _w.WriteLine(";");
            });
        }
    }

    // ---- constant / type helpers -------------------------------------------

    private string ConstantLiteral(object? value, ITypeSymbol type)
    {
        if (value is null) return type.SpecialType == SpecialType.System_String ? "null" : DefaultValueLiteral(type);
        return value switch
        {
            bool b => b ? "true" : "false",
            string s => JsString(s),
            char c => ((int)c).ToString(CultureInfo.InvariantCulture),
            double d => FormatDouble(d),
            float f => FormatDouble(f),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "null",
        };
    }

    private static bool IsStringType(ITypeSymbol? type) => type?.SpecialType == SpecialType.System_String;
    private static bool IsCharType(ITypeSymbol? type) => type?.SpecialType == SpecialType.System_Char;

    private static bool IsIntegerType(ITypeSymbol? type)
    {
        if (type is null) return false;
        if (type.TypeKind == TypeKind.Enum) return true;
        return type.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte
            or SpecialType.System_Int16 or SpecialType.System_UInt16
            or SpecialType.System_Int32 or SpecialType.System_UInt32
            or SpecialType.System_Int64 or SpecialType.System_UInt64;
    }

    private static bool IsFloatingType(ITypeSymbol? type)
        => type?.SpecialType is SpecialType.System_Single or SpecialType.System_Double;
}
