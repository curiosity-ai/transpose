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
            case CollectionExpressionSyntax collection:
                EmitCollectionExpression(collection);
                break;
            case FieldExpressionSyntax fieldExpr:
                // C# 14 `field` keyword → the property's synthesized backing field.
                if (fieldExpr.FirstAncestorOrSelf<PropertyDeclarationSyntax>() is { } pd
                    && _model.GetDeclaredSymbol(pd) is IPropertySymbol fp)
                    _w.Write($"this.{PropertyBackingName(fp)}");
                else
                    _w.Write("this.$field");
                break;
            case ParenthesizedLambdaExpressionSyntax lambda:
                EmitLambda(lambda.ParameterList.Parameters.Select(p => p.Identifier.Text), lambda.Body,
                    lambda.Modifiers.Any(SyntaxKind.AsyncKeyword), lambda.ParameterList.Parameters);
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
            case DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax d }:
                _w.Write(NameMangler.JsIdentifier(d.Identifier.Text));
                break;
            case ThrowExpressionSyntax throwExpr:
                _w.Write("(function () { throw ");
                EmitExpression(throwExpr.Expression);
                _w.Write("; })()");
                break;
            case CheckedExpressionSyntax checkedExpr:
                EmitExpression(checkedExpr.Expression);
                break;
            case AwaitExpressionSyntax await:
                // h5.js Tasks are not natively thenable; H5.toPromise adapts a Task (or an
                // already-native Promise) into something JS `await` can drive.
                _w.Write("(await H5.toPromise(");
                EmitExpression(await.Expression);
                _w.Write("))");
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

        // Implicit widening of a 32-bit integer to long/ulong → wrap as a 64-bit instance.
        // (Numeric literals already self-wrap via their converted type in EmitLiteral.)
        if (Is64BitInteger(targetType) && sourceType is not null && !Is64BitInteger(sourceType)
            && IsIntegerType(sourceType) && expr is not LiteralExpressionSyntax)
        {
            _w.Write(Is64BitUnsigned(targetType) ? "System.UInt64(" : "System.Int64(");
            EmitExpression(expr);
            _w.Write(")");
            return;
        }

        // Implicit widening of a numeric value to decimal → wrap as a System.Decimal.
        if (IsDecimalType(targetType) && sourceType is not null && !IsDecimalType(sourceType)
            && (IsIntegerType(sourceType) || IsFloatingType(sourceType)) && expr is not LiteralExpressionSyntax)
        {
            _w.Write("System.Decimal(");
            EmitExpression(expr);
            _w.Write(")");
            return;
        }

        // Enum → object/string uses the enum's name (System.Enum.toString).
        if (sourceType is { TypeKind: TypeKind.Enum }
            && targetType?.SpecialType is SpecialType.System_Object or SpecialType.System_String)
        {
            _w.Write($"System.Enum.toString({TypeRef(sourceType)}, ");
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
                var conv = _model.GetTypeInfo(lit).ConvertedType;
                if (conv?.SpecialType is SpecialType.System_Int64 or SpecialType.System_UInt64
                    && lit.Token.Value is not double and not float and not decimal)
                    _w.Write(Long64Literal(lit.Token.Value!, conv.SpecialType == SpecialType.System_UInt64));
                else if (conv?.SpecialType == SpecialType.System_Decimal)
                    _w.Write($"System.Decimal(\"{Convert.ToString(lit.Token.Value, CultureInfo.InvariantCulture)}\")");
                else
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

    /// <summary>
    /// A 64-bit integer literal → an h5.js System.Int64/UInt64 instance. Values within JS
    /// safe-integer range pass a number; larger ones pass a decimal string to keep precision.
    /// </summary>
    internal static string Long64Literal(object value, bool unsigned)
    {
        var type = unsigned ? "System.UInt64" : "System.Int64";
        var str = System.Convert.ToString(value, CultureInfo.InvariantCulture)!;
        var safe = value switch
        {
            long l => l >= -9007199254740992L && l <= 9007199254740992L,
            ulong u => u <= 9007199254740992UL,
            _ => false,
        };
        return safe ? $"{type}({str})" : $"{type}(\"{str}\")";
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
                // A captured primary-constructor parameter referenced from an instance
                // member is stored on the instance (this.<param>). Inside the primary
                // ctor's own body the parameter is still the JS function parameter.
                if (!_inPrimaryCtorBody && IsCapturedPrimaryCtorParam(param))
                {
                    _w.Write($"this.{NameMangler.JsIdentifier(param.Name)}");
                    break;
                }
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
            case IEventSymbol ev:
                _w.Write(ev.IsStatic ? $"{TypeRef(ev.ContainingType)}.{H5Naming.MemberJsName(ev)}" : $"this.{H5Naming.MemberJsName(ev)}");
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
            WriteTemplate(template, isStatic: prop.IsStatic, isExtension: false, receiver, new(), new());
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
                    // Nullable<T>.Value throws InvalidOperationException when null.
                    _w.Write("System.Nullable.getValue(");
                    EmitExpression(member.Expression);
                    _w.Write(")");
                    return;
            }
        }

        switch (symbol)
        {
            case IFieldSymbol { IsConst: true, ContainingType.TypeKind: not TypeKind.Enum } constField:
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
            case IEventSymbol ev:
                if (ev.IsStatic) { _w.Write($"{TypeRef(ev.ContainingType)}.{H5Naming.MemberJsName(ev)}"); }
                else { EmitExpression(member.Expression); _w.Write("." + H5Naming.MemberJsName(ev)); }
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
    /// Emits an H5 [Template]. For plain instance members whose template does not
    /// reference {this}, the template is relative to the receiver (e.g. "getTotalHours()"
    /// → "recv.getTotalHours()"); otherwise it is absolute.
    /// </summary>
    private void WriteTemplate(string template, bool isStatic, bool isExtension, string? receiver, Dictionary<string, string> argsByName, List<string> argsByPos)
    {
        var sub = SubstituteTemplate(template, receiver, argsByName, argsByPos);
        // A leading "<self>" marker (or a {this...} reference) means the template is
        // self-contained; otherwise a bare instance template is relative to the receiver.
        var absolute = isStatic || isExtension || receiver is null
                       || template.Contains("{this") || template.Contains("<self>");
        _w.Write(absolute ? sub : receiver + "." + sub);
    }

    /// <summary>
    /// Substitutes an H5 [Template] string. {this} → receiver, {paramName}/{index} → argument JS.
    /// </summary>
    private string SubstituteTemplate(string template, string? receiver, Dictionary<string, string> argsByName, List<string> argsByPos)
    {
        // Strip the self-reference marker used by some H5 templates (e.g. GetType()).
        template = template.Replace("<self>", "");
        var recv = receiver ?? "this";
        // {this:type} / {key:type} → runtime type via H5.getType(expr).
        template = System.Text.RegularExpressions.Regex.Replace(template, @"\{(this|\*?[A-Za-z_][A-Za-z0-9_]*|\d+):type\}", m =>
        {
            var tok = m.Groups[1].Value;
            var expr = tok == "this" ? recv
                : argsByName.TryGetValue(tok, out var av) ? av
                : int.TryParse(tok, out var i2) && i2 < argsByPos.Count ? argsByPos[i2]
                : recv;
            return $"H5.getType({expr})";
        });

        // Sentinel for a template slot that resolves to no argument (e.g. an optional
        // trailing param not supplied); the slot and its leading comma are stripped after.
        const string drop = "￿";
        var posCursor = 0;
        var result = System.Text.RegularExpressions.Regex.Replace(template, @"\{(\*?[A-Za-z_][A-Za-z0-9_]*|\d+)\}", m =>
        {
            var token = m.Groups[1].Value;
            if (token == "this") return receiver ?? "this";
            if (token.StartsWith("*"))
            {
                var n = token.Substring(1);
                return argsByName.TryGetValue(n, out var av) ? av : string.Join(", ", argsByPos);
            }
            if (argsByName.TryGetValue(token, out var v)) { posCursor++; return v; }
            if (int.TryParse(token, out var idx))
            {
                if (idx >= argsByPos.Count) return drop;
                posCursor = idx + 1;
                return argsByPos[idx];
            }
            // A named token with no matching parameter (some H5 templates reuse a name like
            // {result} for the next positional slot) → the next unconsumed argument, else drop.
            if (posCursor < argsByPos.Count) return argsByPos[posCursor++];
            return drop;
        });

        // Remove dropped slots together with an adjacent comma so the call stays well-formed.
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s*,\s*￿", "");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"￿\s*,\s*", "");
        return result.Replace(drop, "");
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
                // Honour a property's [Template] (e.g. string.Length → {this}.length).
                if (_model.GetSymbolInfo(binding).Symbol is IPropertySymbol { GetMethod: { } getM } prop
                    && H5Naming.GetTemplate(getM.OriginalDefinition) is { } propTpl)
                {
                    var sub = SubstituteTemplate(propTpl, "", new(), new());
                    _w.Write(sub.StartsWith(".") ? "?" + sub : "?." + sub);
                }
                else
                {
                    _w.Write("?.");
                    var bsym = _model.GetSymbolInfo(binding).Symbol;
                    _w.Write(bsym is not null ? H5Naming.MemberJsName(bsym) : NameMangler.JsIdentifier(binding.Name.Identifier.Text));
                }
                break;
            case InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax mb } inv:
                // Delegate invoke (event?.Invoke(...)) → optional call, not a member call.
                if (_model.GetSymbolInfo(inv).Symbol is IMethodSymbol { MethodKind: MethodKind.DelegateInvoke })
                {
                    _w.Write("?.(");
                    EmitArguments(inv.ArgumentList, _model.GetSymbolInfo(inv).Symbol as IMethodSymbol);
                    _w.Write(")");
                    break;
                }
                _w.Write("?.");
                _w.Write(NameMangler.JsIdentifier(mb.Name.Identifier.Text));
                _w.Write("(");
                EmitArgumentList(inv.ArgumentList);
                _w.Write(")");
                break;
            case ElementBindingExpressionSyntax elemBind:
                // a?[i] — indexer access on a possibly-null receiver.
                if (_model.GetSymbolInfo(elemBind).Symbol is IPropertySymbol { IsIndexer: true } idx
                    && idx.ContainingType.SpecialType != SpecialType.System_String)
                {
                    _w.Write("?." + H5Naming.IndexerAccessorName(idx, isGet: true) + "(");
                    EmitArgumentList(elemBind.ArgumentList);
                    _w.Write(")");
                }
                else
                {
                    _w.Write("?.[");
                    EmitArgumentList(elemBind.ArgumentList);
                    _w.Write("]");
                }
                break;
            default:
                _w.Write("?.");
                EmitExpression(whenNotNull);
                break;
        }
    }
}
