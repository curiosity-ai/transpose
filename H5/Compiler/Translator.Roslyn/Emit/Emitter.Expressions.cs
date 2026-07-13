using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace H5.Translator.Roslyn;

public sealed partial class Emitter
{
    /// <summary>Emits an expression used in statement position, terminated with ";".</summary>
    private void EmitExpressionStatement(ExpressionSyntax expr)
    {
        EmitExpression(expr);
        _w.WriteLine(";");
    }

    private void EmitExpression(ExpressionSyntax expr)
    {
        switch (expr)
        {
            case LiteralExpressionSyntax lit:
                EmitLiteral(lit);
                break;
            case IdentifierNameSyntax id:
                EmitIdentifier(id);
                break;
            case MemberAccessExpressionSyntax member:
                EmitMemberAccess(member);
                break;
            case InvocationExpressionSyntax invocation:
                EmitInvocation(invocation);
                break;
            case ObjectCreationExpressionSyntax creation:
                EmitObjectCreation(creation);
                break;
            case ImplicitObjectCreationExpressionSyntax implicitCreation:
                EmitImplicitObjectCreation(implicitCreation);
                break;
            case BinaryExpressionSyntax binary:
                EmitBinary(binary);
                break;
            case AssignmentExpressionSyntax assignment:
                EmitAssignment(assignment);
                break;
            case PrefixUnaryExpressionSyntax prefix:
                EmitPrefixUnary(prefix);
                break;
            case PostfixUnaryExpressionSyntax postfix:
                EmitPostfixUnary(postfix);
                break;
            case ParenthesizedExpressionSyntax paren:
                _w.Write("(");
                EmitExpression(paren.Expression);
                _w.Write(")");
                break;
            case ConditionalExpressionSyntax cond:
                _w.Write("(");
                EmitExpression(cond.Condition);
                _w.Write(" ? ");
                EmitExpression(cond.WhenTrue);
                _w.Write(" : ");
                EmitExpression(cond.WhenFalse);
                _w.Write(")");
                break;
            case CastExpressionSyntax cast:
                EmitCast(cast);
                break;
            case InterpolatedStringExpressionSyntax interp:
                EmitInterpolatedString(interp);
                break;
            case ElementAccessExpressionSyntax element:
                EmitElementAccess(element);
                break;
            case ThisExpressionSyntax:
                _w.Write("this");
                break;
            case BaseExpressionSyntax:
                _w.Write("this"); // base.M() handled specially in invocation
                break;
            case ArrayCreationExpressionSyntax arrayCreation:
                EmitArrayCreation(arrayCreation);
                break;
            case ImplicitArrayCreationExpressionSyntax implicitArray:
                EmitInitializerArray(implicitArray.Initializer);
                break;
            case InitializerExpressionSyntax initializer:
                EmitInitializerArray(initializer);
                break;
            case ParenthesizedLambdaExpressionSyntax lambda:
                EmitLambda(lambda.ParameterList.Parameters.Select(p => p.Identifier.Text), lambda.Body, lambda.Modifiers.Any(SyntaxKind.AsyncKeyword));
                break;
            case SimpleLambdaExpressionSyntax simpleLambda:
                EmitLambda(new[] { simpleLambda.Parameter.Identifier.Text }, simpleLambda.Body, simpleLambda.Modifiers.Any(SyntaxKind.AsyncKeyword));
                break;
            case AnonymousMethodExpressionSyntax anon:
                EmitLambda(anon.ParameterList?.Parameters.Select(p => p.Identifier.Text) ?? Enumerable.Empty<string>(), anon.Body, anon.Modifiers.Any(SyntaxKind.AsyncKeyword));
                break;
            case DefaultExpressionSyntax def:
                _w.Write(DefaultValueLiteral(_model.GetTypeInfo(def).Type ?? _model.GetTypeInfo(def).ConvertedType!));
                break;
            case AwaitExpressionSyntax await:
                _w.Write("(await ");
                EmitExpression(await.Expression);
                _w.Write(")");
                break;
            case ConditionalAccessExpressionSyntax condAccess:
                EmitConditionalAccess(condAccess);
                break;
            case TypeOfExpressionSyntax typeOf:
                _w.Write(_names.TypeReference(_model.GetTypeInfo(typeOf.Type).Type!));
                break;
            case IsPatternExpressionSyntax isPattern:
                EmitIsPattern(isPattern);
                break;
            case SwitchExpressionSyntax switchExpr:
                EmitSwitchExpression(switchExpr);
                break;
            case TupleExpressionSyntax tuple:
                EmitTuple(tuple);
                break;
            case AnonymousObjectCreationExpressionSyntax anon:
                EmitAnonymousObject(anon);
                break;
            case QueryExpressionSyntax query:
                EmitQuery(query);
                break;
            case WithExpressionSyntax with:
                _w.Write("(function ($w) { var $c = H5R.clone($w); ");
                EmitInitializer("$c", with.Initializer);
                _w.Write("return $c; })(");
                EmitExpression(with.Expression);
                _w.Write(")");
                break;
            case PredefinedTypeSyntax predefined:
                _w.Write(_model.GetTypeInfo(predefined).Type?.Name ?? "Object");
                break;
            default:
                if (expr.IsKind(SyntaxKind.DefaultLiteralExpression))
                {
                    _w.Write(DefaultValueLiteral(_model.GetTypeInfo(expr).ConvertedType!));
                    break;
                }
                Unsupported(expr, expr.Kind().ToString());
                break;
        }
    }

    /// <summary>Emits an expression applying a required conversion for its target type.</summary>
    private void EmitExpressionConverted(ExpressionSyntax expr, ITypeSymbol? targetType)
    {
        // Numeric narrowing to an integer type needs truncation.
        var sourceType = _model.GetTypeInfo(expr).Type;
        if (targetType is not null && sourceType is not null
            && IsIntegerType(targetType) && IsFloatingType(sourceType))
        {
            _w.Write("H5R.trunc(");
            EmitExpression(expr);
            _w.Write(")");
            return;
        }

        // Value types (user-defined structs) are copied when assigned / passed / returned
        // from a referencing expression, so mutations to the copy don't alias the source.
        if (IsSourceStruct(sourceType) && IsReferencingExpression(expr))
        {
            _w.Write("H5R.clone(");
            EmitExpression(expr);
            _w.Write(")");
            return;
        }

        EmitExpression(expr);
    }

    /// <summary>A user-defined (source) struct — value-copy semantics apply.</summary>
    private static bool IsSourceStruct(ITypeSymbol? type)
        => type is { TypeKind: TypeKind.Struct } && type.Locations.Any(l => l.IsInSource) && !type.IsTupleType;

    /// <summary>An expression that references existing storage (so could alias).</summary>
    private static bool IsReferencingExpression(ExpressionSyntax expr) => expr switch
    {
        IdentifierNameSyntax => true,
        MemberAccessExpressionSyntax => true,
        ElementAccessExpressionSyntax => true,
        ThisExpressionSyntax => true,
        ParenthesizedExpressionSyntax paren => IsReferencingExpression(paren.Expression),
        _ => false,
    };

    // ---- literals ----------------------------------------------------------

    private void EmitLiteral(LiteralExpressionSyntax lit)
    {
        switch (lit.Kind())
        {
            case SyntaxKind.NumericLiteralExpression:
                _w.Write(FormatNumericLiteral(lit));
                break;
            case SyntaxKind.StringLiteralExpression:
                _w.Write(JsString((string)lit.Token.Value!));
                break;
            case SyntaxKind.CharacterLiteralExpression:
                _w.Write(((int)(char)lit.Token.Value!).ToString(CultureInfo.InvariantCulture));
                break;
            case SyntaxKind.TrueLiteralExpression:
                _w.Write("true");
                break;
            case SyntaxKind.FalseLiteralExpression:
                _w.Write("false");
                break;
            case SyntaxKind.NullLiteralExpression:
                _w.Write("null");
                break;
            case SyntaxKind.DefaultLiteralExpression:
                _w.Write(DefaultValueLiteral(_model.GetTypeInfo(lit).ConvertedType!));
                break;
            default:
                Unsupported(lit, lit.Kind().ToString());
                break;
        }
    }

    private string FormatNumericLiteral(LiteralExpressionSyntax lit)
    {
        var value = lit.Token.Value!;
        return value switch
        {
            int i => i.ToString(CultureInfo.InvariantCulture),
            uint u => u.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            ulong ul => ul.ToString(CultureInfo.InvariantCulture),
            double d => FormatDouble(d),
            float f => FormatDouble(f),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            byte b => b.ToString(CultureInfo.InvariantCulture),
            sbyte sb => sb.ToString(CultureInfo.InvariantCulture),
            short s => s.ToString(CultureInfo.InvariantCulture),
            ushort us => us.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString()!,
        };
    }

    private static string FormatDouble(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "Infinity";
        if (double.IsNegativeInfinity(d)) return "-Infinity";
        var s = d.ToString("R", CultureInfo.InvariantCulture);
        return s;
    }

    private static string JsString(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    // ---- identifiers & member access ---------------------------------------

    private void EmitIdentifier(IdentifierNameSyntax id)
    {
        var symbol = _model.GetSymbolInfo(id).Symbol;
        switch (symbol)
        {
            case ILocalSymbol local:
                _w.Write(NameMangler.JsIdentifier(local.Name));
                break;
            case IParameterSymbol param:
                _w.Write(NameMangler.JsIdentifier(param.Name));
                // ref/out parameters are holder objects ({ v: ... }) inside the body.
                if (param.RefKind is RefKind.Ref or RefKind.Out) _w.Write(".v");
                break;
            case IFieldSymbol field:
                EmitFieldAccess(field, thisTarget: null);
                break;
            case IPropertySymbol prop:
                EmitPropertyAccess(prop, thisTarget: null);
                break;
            case IMethodSymbol { MethodKind: MethodKind.LocalFunction } localFn:
                _w.Write(NameMangler.JsIdentifier(localFn.Name));
                break;
            case IMethodSymbol method:
                // Method group reference (delegate creation).
                EmitMethodGroup(method, thisTarget: null);
                break;
            case INamedTypeSymbol type:
                _w.Write(TypeRef(type));
                break;
            default:
                _w.Write(NameMangler.JsIdentifier(id.Identifier.Text));
                break;
        }
    }

    private void EmitFieldAccess(IFieldSymbol field, ExpressionSyntax? thisTarget)
    {
        if (field.IsConst)
        {
            _w.Write(ConstantLiteral(field.ConstantValue, field.Type));
            return;
        }
        if (field.IsStatic)
        {
            _w.Write($"{TypeRef(field.ContainingType)}.{H5Naming.MemberJsName(field)}");
            return;
        }
        EmitReceiver(thisTarget);
        _w.Write(H5Naming.MemberJsName(field));
    }

    private void EmitPropertyAccess(IPropertySymbol prop, ExpressionSyntax? thisTarget)
    {
        // [Template] getter (BCL properties like string.Length).
        var template = prop.GetMethod is not null ? H5Naming.GetTemplate(prop.GetMethod.OriginalDefinition) : null;
        if (template is not null)
        {
            var receiver = thisTarget is null ? "this" : Capture(() => EmitExpression(thisTarget));
            _w.Write(SubstituteTemplate(template, receiver, new(), new()));
            return;
        }
        if (prop.IsStatic)
        {
            _w.Write($"{TypeRef(prop.ContainingType)}.{H5Naming.MemberJsName(prop)}");
            return;
        }
        EmitReceiver(thisTarget);
        _w.Write(H5Naming.MemberJsName(prop));
    }

    private void EmitMethodGroup(IMethodSymbol method, ExpressionSyntax? thisTarget)
    {
        if (method.IsStatic)
        {
            _w.Write($"{TypeRef(method.ContainingType)}.{H5Naming.MemberJsName(method)}");
        }
        else
        {
            _w.Write("(");
            EmitReceiverExpr(thisTarget);
            _w.Write($").{H5Naming.MemberJsName(method)}.bind(");
            EmitReceiverExpr(thisTarget);
            _w.Write(")");
        }
    }

    private void EmitReceiver(ExpressionSyntax? thisTarget)
    {
        if (thisTarget is null) { _w.Write("this."); }
        else { EmitExpression(thisTarget); _w.Write("."); }
    }

    private void EmitReceiverExpr(ExpressionSyntax? thisTarget)
    {
        if (thisTarget is null) _w.Write("this");
        else EmitExpression(thisTarget);
    }

    private void EmitMemberAccess(MemberAccessExpressionSyntax member)
    {
        var symbol = _model.GetSymbolInfo(member).Symbol;

        // Nullable<T> — represented as the value itself or null.
        if (symbol?.ContainingType is { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
        {
            switch (symbol.Name)
            {
                case "HasValue":
                    _w.Write("("); EmitExpression(member.Expression); _w.Write(" != null)");
                    return;
                case "Value":
                    EmitExpression(member.Expression);
                    return;
            }
        }

        switch (symbol)
        {
            case IFieldSymbol { IsConst: true } constField:
                _w.Write(ConstantLiteral(constField.ConstantValue, constField.Type));
                return;
            case IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } enumField:
                // Enum member → the enum object's member (h5 enums are objects).
                _w.Write($"{TypeRef(enumField.ContainingType)}.{H5Naming.MemberJsName(enumField)}");
                return;
            case IFieldSymbol field:
                if (field.ContainingType is { IsTupleType: true })
                {
                    EmitExpression(member.Expression);
                    _w.Write("." + (field.CorrespondingTupleField ?? field).Name);
                    return;
                }
                EmitFieldAccess(field, field.IsStatic ? null : member.Expression);
                return;
            case IPropertySymbol prop:
                EmitPropertyAccess(prop, prop.IsStatic ? null : member.Expression);
                return;
            case IMethodSymbol method:
                EmitMethodGroup(method, member.Expression is ThisExpressionSyntax ? null : member.Expression);
                return;
            case INamedTypeSymbol type:
                _w.Write(TypeRef(type));
                return;
            default:
                EmitExpression(member.Expression);
                _w.Write("." + NameMangler.JsIdentifier(member.Name.Identifier.Text));
                return;
        }
    }

    /// <summary>
    /// Substitutes an H5 [Template] string. {this} → receiver, {paramName}/{index} → argument JS.
    /// </summary>
    private string SubstituteTemplate(string template, string? receiver, Dictionary<string, string> argsByName, List<string> argsByPos)
    {
        return System.Text.RegularExpressions.Regex.Replace(template, @"\{(\*?[A-Za-z_][A-Za-z0-9_]*|\d+)\}", m =>
        {
            var token = m.Groups[1].Value;
            if (token == "this") return receiver ?? "this";
            if (token.StartsWith("*"))
            {
                var n = token.Substring(1);
                return argsByName.TryGetValue(n, out var av) ? av : string.Join(", ", argsByPos);
            }
            if (argsByName.TryGetValue(token, out var v)) return v;
            if (int.TryParse(token, out var idx) && idx < argsByPos.Count) return argsByPos[idx];
            return m.Value;
        });
    }

    private void EmitConditionalAccess(ConditionalAccessExpressionSyntax condAccess)
    {
        // a?.b  =>  (a == null ? null : a.b)  — simplified via JS optional chaining
        EmitExpression(condAccess.Expression);
        EmitWhenNotNull(condAccess.WhenNotNull);
    }

    private void EmitWhenNotNull(ExpressionSyntax whenNotNull)
    {
        switch (whenNotNull)
        {
            case MemberBindingExpressionSyntax binding:
                _w.Write("?.");
                _w.Write(NameMangler.JsIdentifier(binding.Name.Identifier.Text));
                break;
            case InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax mb } inv:
                _w.Write("?.");
                _w.Write(NameMangler.JsIdentifier(mb.Name.Identifier.Text));
                _w.Write("(");
                EmitArgumentList(inv.ArgumentList);
                _w.Write(")");
                break;
            default:
                _w.Write("?.");
                EmitExpression(whenNotNull);
                break;
        }
    }
}
