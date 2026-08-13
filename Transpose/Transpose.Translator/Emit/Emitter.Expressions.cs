using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

public sealed partial class Emitter
{
    /// <summary>Emits an expression used in statement position, terminated with ";".</summary>
    private void EmitExpressionStatement(ExpressionSyntax expr)
    {
        // `x++;` / `--x;` on a sub-32-bit integer must re-narrow like `x = (T)(x ± 1)` — a bare JS
        // `x++` skips that (T) cast, so a byte at 255 became 256 instead of wrapping to 0. Handled
        // here (statement context: the result value is discarded, so prefix/postfix are equivalent).
        if (TryEmitNarrowingIncDecStatement(expr)) return;
        EmitExpression(expr);
        _w.WriteLine(";");
    }

    /// <summary>Emits a stand-alone ++/-- on a sub-word integer as a width-wrapping assignment.
    /// Restricted to side-effect-free lvalues (identifier / simple field access) because the operand
    /// is emitted twice; anything else falls through to the plain (un-narrowed) form.</summary>
    private bool TryEmitNarrowingIncDecStatement(ExpressionSyntax expr)
    {
        ExpressionSyntax operand;
        string binOp;
        switch (expr)
        {
            case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PostIncrementExpression } post: operand = post.Operand; binOp = "+"; break;
            case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PostDecrementExpression } post: operand = post.Operand; binOp = "-"; break;
            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PreIncrementExpression } pre: operand = pre.Operand; binOp = "+"; break;
            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PreDecrementExpression } pre: operand = pre.Operand; binOp = "-"; break;
            default: return false;
        }

        var t = _model.GetTypeInfo(operand).Type;
        if (!IsSubWordIntegerTarget(t)) return false;
        // Re-emitting a receiver with side effects (indexer, method call) would run it twice.
        if (operand is not IdentifierNameSyntax
            && operand is not MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or IdentifierNameSyntax })
            return false;

        EmitExpression(operand);
        _w.Write(" = ");
        _w.Write(NarrowIntegerClip(t!.SpecialType));
        _w.Write("((");
        EmitExpression(operand);
        _w.Write($") {binOp} 1);");
        _w.WriteLine();
        return true;
    }

    /// <summary>True if the expression is a call whose [Template] is a comment-only no-op
    /// (starts with <c>0 /*</c>) — e.g. the code-contract helpers Contract.Ensures/Result. Such a
    /// statement is elided (the reference runtime emits nothing), which also avoids the illegal
    /// nested block comments that would form when the condition itself contains such a template.</summary>
    private bool IsElidedNoOpCall(ExpressionSyntax expr)
    {
        if (expr is not InvocationExpressionSyntax inv) return false;
        if (_model.GetSymbolInfo(inv).Symbol is not IMethodSymbol m) return false;
        var t = TransposeNaming.GetTemplate(m);
        return t is not null && t.TrimStart().StartsWith("0 /*", System.StringComparison.Ordinal);
    }

    /// <summary>
    /// True if this expression is a call to a method whose
    /// <c>[System.Diagnostics.Conditional("SYM")]</c> conditions are ALL undefined in the current
    /// compilation — the call (and the evaluation of its arguments) is removed entirely, matching
    /// C#'s conditional-method semantics. A method with no <c>[Conditional]</c> is never removed;
    /// if any one of its conditions is defined, the call stays.
    /// </summary>
    private bool IsRemovedConditionalCall(ExpressionSyntax expr)
    {
        if (expr is not InvocationExpressionSyntax inv) return false;
        if (_model.GetSymbolInfo(inv).Symbol is not IMethodSymbol m) return false;

        var conditions = m.OriginalDefinition.GetAttributes()
            .Where(a => TransposeNaming.AttrIs(a, "System.Diagnostics.ConditionalAttribute"))
            .Select(a => a.ConstructorArguments.Length > 0 ? a.ConstructorArguments[0].Value as string : null)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
        if (conditions.Count == 0) return false;

        var defined = inv.SyntaxTree.Options is CSharpParseOptions o
            ? o.PreprocessorSymbolNames
            : Enumerable.Empty<string>();
        var definedSet = new System.Collections.Generic.HashSet<string>(defined, StringComparer.Ordinal);
        return !conditions.Any(c => definedSet.Contains(c!));
    }

    private void EmitExpression(ExpressionSyntax expr)
    {
        switch (expr)
        {
            case LiteralExpressionSyntax lit:
                EmitLiteral(lit);
                break;
            // GenericNameSyntax shares this path: in expression position it is either an explicitly
            // instantiated method group (`Func<Colour> g = Def<Colour>;`) or a constructed type used
            // as a static-member receiver — both of which EmitIdentifier's symbol switch resolves.
            case IdentifierNameSyntax or GenericNameSyntax:
                EmitIdentifier((SimpleNameSyntax)expr);
                break;
            case MemberAccessExpressionSyntax member:
                EmitMemberAccess(member);
                break;
            case MemberBindingExpressionSyntax binding:
                // The head of a null-conditional continuation (the `.b` in a?.b), resolved
                // against the captured receiver; the rest of the chain is ordinary access.
                EmitMemberBinding(binding);
                break;
            case ElementBindingExpressionSyntax elemBinding:
                EmitElementBinding(elemBinding);
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
            case RangeExpressionSyntax rangeExpr:
                EmitRangeExpression(rangeExpr);
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
                // Each branch is implicitly converted to the conditional's type (C# §12.15). Emit each
                // through the conversion path so a narrower branch is lifted — e.g. in
                // `int==0 ? long : intField` the ternary type is `long`, so the `int` branch must be
                // wrapped System.Int64(...) or the ternary yields a plain number and a downstream Int64
                // op (`.sub`) throws (MigrationsView.RenderTasks OrderByDescending key).
                var condType = _model.GetTypeInfo(cond).Type;
                _w.Write("(");
                EmitExpression(cond.Condition);
                _w.Write(" ? ");
                EmitExpressionConverted(cond.WhenTrue, condType);
                _w.Write(" : ");
                EmitExpressionConverted(cond.WhenFalse, condType);
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
                // new[] { … } → tag with the inferred element type so the value carries $elementType.
                EmitTypedInitializerArray(implicitArray.Initializer,
                    (_model.GetTypeInfo(implicitArray).Type ?? _model.GetTypeInfo(implicitArray).ConvertedType) is IArrayTypeSymbol ia ? ia.ElementType : null);
                break;
            case InitializerExpressionSyntax initializer:
                // A bare initializer targeting a multi-dimensional array (int[,] g = {{…},{…}})
                // must build a System.Array with dimension metadata, not a plain nested JS array.
                if (_model.GetTypeInfo(initializer).ConvertedType is IArrayTypeSymbol { Rank: > 1 } mdInit)
                    EmitMultiDimArray(mdInit.ElementType, null, initializer);
                else
                    EmitTypedInitializerArray(initializer,
                        _model.GetTypeInfo(initializer).ConvertedType is IArrayTypeSymbol { Rank: 1 } sdInit ? sdInit.ElementType : null);
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
            {
                // `x ?? throw new E(await Msg())` awaits inside the wrapper, so it needs the async form.
                var hasAwait = OpenIife(throwExpr.Expression);
                _w.Write("throw ");
                EmitExpression(throwExpr.Expression);
                _w.Write("; ");
                CloseIife(hasAwait);
                break;
            }
            case CheckedExpressionSyntax checkedExpr:
                EmitExpression(checkedExpr.Expression);
                break;
            case RefExpressionSyntax refExpr:
                // JavaScript has no by-ref aliases, so `ref <expr>` (e.g. a ref-returning indexer's
                // `return ref _array[i]`) collapses to the referenced expression's value. This is
                // correct for the ref structs the BCL defines (Span/ReadOnlySpan), which are
                // represented as the underlying JS array — element access yields the value directly.
                EmitExpression(refExpr.Expression);
                break;
            case AwaitExpressionSyntax await:
                // tps.js Tasks are not natively thenable; Transpose.toPromise adapts a Task (or an
                // already-native Promise) into something JS `await` can drive.
                _w.Write("(await Transpose.toPromise(");
                EmitExpression(await.Expression);
                _w.Write("))");
                break;
            case ConditionalAccessExpressionSyntax condAccess:
                EmitConditionalAccess(condAccess);
                break;
            case TypeOfExpressionSyntax typeOf:
                _w.Write(TypeRef(_model.GetTypeInfo(typeOf.Type).Type!));
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
                // Arrow so a `this`-qualified initializer value (record `with { X = this.Y }`)
                // keeps the enclosing instance rather than rebinding `this` to undefined.
                _w.Write("(($w) => { var $c = TransposeR.clone($w); ");
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

        // User-defined IMPLICIT conversion operator. C# inserts these silently at conversion sites
        // (assignment, argument, return, field/property initializer). The operator can change the
        // runtime representation — e.g. LanguageDTO's `implicit operator LanguageDTO(Language)` turns
        // an enum into its string code — so it must actually be invoked; emitting the raw source
        // value leaks the wrong representation (and later breaks JSON round-tripping). Explicit casts
        // go through EmitCast/EmitNumericConversion, not here.
        if (targetType is not null && sourceType is not null
            && !SymbolEqualityComparer.Default.Equals(sourceType, targetType)
            && _compilation.ClassifyConversion(sourceType, targetType)
                is { IsUserDefined: true, IsImplicit: true, MethodSymbol: IMethodSymbol convMethod }
            && ShouldEmitUserConversion(convMethod))
        {
            EmitUserDefinedConversion(convMethod, expr);
            return;
        }
        if (targetType is not null && sourceType is not null
            && IsIntegerType(targetType) && IsFloatingType(sourceType))
        {
            _w.Write("TransposeR.trunc(");
            EmitExpression(expr);
            _w.Write(")");
            return;
        }

        // Implicit widening of long/ulong to float/double → read the numeric magnitude. The 64-bit
        // value is an Int64/UInt64 object at runtime, and leaving it raw means JS operates on the
        // object: `d += someLong` string-concatenated ("42" + "4" → "424") because Int64 has no
        // valueOf, and a `double` parameter received an Int64 instance.
        if (IsFloatingType(targetType) && Is64BitInteger(sourceType) && !EmitsAsPlainJsNumber(expr))
        {
            _w.Write("(");
            EmitExpression(expr);
            _w.Write(").toNumber()");
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

        if (TryEmitEnumToReferenceConversion(sourceType, targetType, expr)) return;

        // char → object / interface / ValueType / dynamic (a boxing conversion). A char is a bare
        // code-point number at runtime; box it so the boxed value stringifies and compares as its
        // character (e.g. `object o = 'A'; o.ToString()` → "A", not "65").
        if (IsCharType(sourceType) && targetType is { IsReferenceType: true })
        {
            _w.Write("TransposeR.boxChar(");
            EmitExpression(expr);
            _w.Write(")");
            return;
        }

        // Value types (user-defined structs) are copied when assigned / passed / returned
        // from a referencing expression, so mutations to the copy don't alias the source.
        if (IsSourceStruct(sourceType) && IsReferencingExpression(expr))
        {
            _w.Write("TransposeR.clone(");
            EmitExpression(expr);
            _w.Write(")");
            return;
        }

        EmitExpression(expr);
    }

    /// <summary>
    /// Emits an enum → string / object conversion, returning false when the conversion is not one
    /// (so the caller carries on). Matching h5:
    /// <list type="bullet">
    /// <item>An enum with no runtime type object to name or box against (see
    /// <see cref="HasRuntimeEnumObject"/>) stays the bare number its members inline as.</item>
    /// <item>enum → string is the name (<c>System.Enum.toString</c>), except under a StringName* mode
    /// (3–6) where the runtime value already IS that string.</item>
    /// <item>enum → object / interface boxes the value together with its enum type, whatever the mode,
    /// so <c>GetType()</c> is the enum (not Int32/String) and <c>ToString()</c> is the name. The box
    /// stays transparent for everything else — a boxed StringName* value still <c>is string</c>, casts
    /// to it, compares equal to it and reaches a JS API as it, which is the mode's contract.</item>
    /// </list>
    /// Shared by the implicit conversion sites (assignment, argument, return, …) and by an explicit
    /// <c>(object)</c> cast: C# classifies both as the same boxing conversion, so both must box. Only
    /// the implicit sites did, which left <c>((object)Color.Red).ToString()</c> reading "1" off a bare
    /// number where <c>object o = Color.Red; o.ToString()</c> correctly read "Red".
    /// </summary>
    private bool TryEmitEnumToReferenceConversion(ITypeSymbol? sourceType, ITypeSymbol? targetType, ExpressionSyntax expr)
    {
        if (sourceType is not { TypeKind: TypeKind.Enum } || targetType is not { IsReferenceType: true })
            return false;

        var stringMode = TransposeNaming.EnumEmitMode(sourceType) is 3 or 4 or 5 or 6;
        if (!HasRuntimeEnumObject(sourceType))
        {
            EmitExpression(expr); // a nameless external ordinal
        }
        else if (targetType.SpecialType == SpecialType.System_String)
        {
            if (stringMode)
            {
                EmitExpression(expr); // already the name string
            }
            else
            {
                _w.Write($"System.Enum.toString({TypeRef(sourceType)}, ");
                EmitExpression(expr);
                _w.Write(")");
            }
        }
        else
        {
            _w.Write($"Transpose.box(");
            EmitExpression(expr);
            _w.Write($", {TypeRef(sourceType)}, function ($v) {{ return System.Enum.toString({TypeRef(sourceType)}, $v); }})");
        }
        return true;
    }

    /// <summary>Whether a user-defined conversion operator should be emitted as an actual call rather
    /// than erased. We only materialise operators declared in THIS compilation's own source (or ones
    /// carrying a [Template], which is real JS) — e.g. the FrontEnd's own LanguageDTO/UID128 structs,
    /// whose operators change the runtime representation and must run. Operators coming from a
    /// referenced assembly (a compiled library such as Tesserae, the BCL) or an EXTERNAL/DOM-union
    /// type stay erased, matching how those bundles were built and the legacy compiler's behaviour:
    /// materialising them can call a non-existent method (System.Object.op_Implicit) or re-enter a
    /// library conversion that was compiled to expect the erased form (HSLColor → stack overflow).</summary>
    private static bool ShouldEmitUserConversion(IMethodSymbol convMethod)
    {
        if (TransposeNaming.GetTemplate(convMethod.OriginalDefinition) is not null
            || TransposeNaming.GetTemplate(convMethod) is not null)
            return true;

        // External / DOM-union operators are transparent in JS and would call a non-existent method.
        if (TransposeNaming.IsExternalType(convMethod.ContainingType)) return false;

        // Every other user-defined operator is materialised as a real op_X call: in-source types
        // (this compilation's own LanguageDTO/UID128), the Transpose runtime/BCL primitives (System.*,
        // the implicit DateTime→DateTimeOffset), AND referenced user libraries. The current compiler
        // always emits the operator method (op_Implicit/op_Explicit) into the library it defines, so a
        // cross-assembly call resolves — e.g. Curiosity.FrontEnd calling the implicit
        // Language→Mosaik.Schema.Optional<Language> defined in Curiosity.FrontEnd.Core (erasing it left
        // `SaveUILanguage(l.Value, …)` passing a raw enum → `undefined.HasValue` on #/preferences).
        // Only external/DOM operators (guarded above) stay erased.
        return true;
    }

    /// <summary>Emits a call to a user-defined conversion operator (op_Implicit / op_Explicit),
    /// honouring a [Template] on the operator (some BCL/binding types define theirs that way) and
    /// otherwise emitting the static Type.op_X(operand) call.</summary>
    private void EmitUserDefinedConversion(IMethodSymbol convMethod, ExpressionSyntax expr)
    {
        var template = TransposeNaming.GetTemplate(convMethod.OriginalDefinition)
                       ?? TransposeNaming.GetTemplate(convMethod);
        if (template is not null)
        {
            var argJs = Capture(() => EmitExpression(expr));
            var byName = new Dictionary<string, string>();
            if (convMethod.Parameters.Length > 0) byName[convMethod.Parameters[0].Name] = argJs;
            WriteTemplate(template, isStatic: true, isExtension: false, receiver: null, byName,
                new List<string> { argJs });
            return;
        }

        _w.Write($"{TypeRef(convMethod.ContainingType)}.{TransposeNaming.MemberJsName(convMethod)}(");
        // The operand may itself need converting to the operator's parameter type (e.g. an int
        // literal widened before a `operator T(long)`); route it through the converter.
        EmitExpressionConverted(expr, convMethod.Parameters.Length > 0 ? convMethod.Parameters[0].Type : null);
        _w.Write(")");
    }

    /// <summary>A user-defined (source) struct — value-copy semantics apply. Primitive value
    /// types (int, double, bool, char, …) are excluded: they are backed by JS primitives with
    /// native value semantics, and cloning them is both unnecessary and wrong. They matter here
    /// only when self-building the runtime, where System.Int32 &amp; co. are themselves in source.</summary>
    private static bool IsSourceStruct(ITypeSymbol? type)
        => type is { TypeKind: TypeKind.Struct } && type.Locations.Any(l => l.IsInSource)
           && !type.IsTupleType && !IsJsPrimitiveValueType(type);

    /// <summary>A value type backed by a JS primitive (number / boolean / string-like) — no clone.</summary>
    private static bool IsJsPrimitiveValueType(ITypeSymbol type) => type.SpecialType is
        SpecialType.System_Boolean or SpecialType.System_Char
        or SpecialType.System_SByte or SpecialType.System_Byte
        or SpecialType.System_Int16 or SpecialType.System_UInt16
        or SpecialType.System_Int32 or SpecialType.System_UInt32
        or SpecialType.System_Int64 or SpecialType.System_UInt64
        or SpecialType.System_Single or SpecialType.System_Double
        or SpecialType.System_IntPtr or SpecialType.System_UIntPtr;

    /// <summary>An expression that references existing storage (so could alias).</summary>
    private static bool IsReferencingExpression(ExpressionSyntax expr) => expr switch
    {
        IdentifierNameSyntax => true,
        MemberAccessExpressionSyntax => true,
        ElementAccessExpressionSyntax => true,
        ThisExpressionSyntax => true,
        ParenthesizedExpressionSyntax paren => IsReferencingExpression(paren.Expression),
        // A cast aliases whatever the operand aliases: unboxing (`(S)o`) hands back the very object
        // the box holds, so `var c = (S)o; c.X = 1;` wrote through to the boxed value. Casting a
        // freshly produced value (a call, `new`) still needs no copy — the operand decides.
        CastExpressionSyntax cast => IsReferencingExpression(cast.Expression),
        _ => false,
    };

    // ---- literals ----------------------------------------------------------

    private void EmitLiteral(LiteralExpressionSyntax lit)
    {
        switch (lit.Kind())
        {
            case SyntaxKind.NumericLiteralExpression:
                var litInfo = _model.GetTypeInfo(lit);
                // The CONVERTED type decides the representation — `double d = 5L` emits the plain
                // number 5. But a BOXING conversion (`object o = 5L`) converts to object, and the
                // boxed value still has to be a real Int64/UInt64/Decimal instance: emitting a bare
                // number made `o is long` false and `o.GetType()` report Int32.
                // Nullable<T> is represented as the value or null, so `long? x = 7L` needs the same
                // Int64 instance a plain `long` would get — otherwise x is a bare number and a later
                // `x.add(…)` throws.
                var conv = UnwrapNullable(_model.GetConversion(lit).IsBoxing ? litInfo.Type : litInfo.ConvertedType);
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
    /// A 64-bit integer literal → an tps.js System.Int64/UInt64 instance. Values within JS
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

    private void EmitIdentifier(SimpleNameSyntax id)
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
                _w.Write(ev.IsStatic ? StaticMemberAccess(ev) : $"this.{TransposeNaming.MemberJsName(ev)}");
                break;
            // A query-expression range variable: `x` in `from x in xs …`. Each clause is emitted as a
            // one-parameter lambda, so once a query introduces a second range variable they are carried
            // in a frame object and `x` has to read `$q0.x` — see Emitter.Query.cs.
            case IRangeVariableSymbol range when _queryRanges is not null
                                                 && _queryRanges.TryGetValue(range.Name, out var rangeAccess):
                _w.Write(rangeAccess);
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

    /// <summary>
    /// <summary>
    /// Emits a <c>const</c> read. Inlining the literal bakes the value into every consumer, so
    /// updating a base package without rebuilding the packages between it and the app leaves those
    /// carrying the OLD value; reading the emitted static slot instead takes the value from whichever
    /// build of the declaring package is actually loaded.
    ///
    /// Three cases:
    /// Every type Transpose DEFINES gets a static slot per const (see <see cref="EmitStatics"/>) —
    /// this compilation's source, a referenced user library, and the runtime/BCL and binding projects
    /// alike — so the read goes through the slot in all of those. The one exception is an
    /// <c>[External]</c> type: Transpose never emits a definition for it (its members are hand-written
    /// or native JS), so there is no slot and its consts still inline — <c>int.MaxValue</c> is
    /// <c>[External] System.Int32</c>, and DOM bindings are the same.
    ///
    /// Enum members are not routed here; they have their own access path.
    /// </summary>
    private void EmitConstRead(IFieldSymbol field)
    {
        var type = field.ContainingType;

        if (type is { TypeKind: not TypeKind.Enum } && !TransposeNaming.IsExternalType(type))
            _w.Write(StaticMemberAccess(field));
        else
            _w.Write(ConstantLiteral(field.ConstantValue, field.Type));
    }

    private void EmitFieldAccess(IFieldSymbol field, ExpressionSyntax? thisTarget)
    {
        // A [Template] on the field defines how the access emits — e.g. the DOM literal fields
        // dom.InsertPosition.afterend ([Template("<self>\"afterend\"")]) → the string "afterend",
        // not a Type.member reference that would resolve to undefined.
        if (TransposeNaming.GetTemplate(field.OriginalDefinition) is { } template)
        {
            var recv = field.IsStatic || thisTarget is null ? null : Capture(() => EmitExpression(thisTarget));
            WriteTemplate(template, field.IsStatic, isExtension: false, recv, new(), new());
            return;
        }
        if (field.IsConst)
        {
            EmitConstRead(field);
            return;
        }
        if (field.IsStatic)
        {
            _w.Write(StaticMemberAccess(field));
            return;
        }
        EmitReceiver(thisTarget);
        _w.Write(TransposeNaming.MemberJsName(field));
    }

    private void EmitPropertyAccess(IPropertySymbol prop, ExpressionSyntax? thisTarget)
    {
        // [Template] getter (BCL properties like string.Length).
        var template = prop.GetMethod is not null ? TransposeNaming.GetTemplate(prop.GetMethod.OriginalDefinition) : null;
        if (template is not null)
        {
            // For a static member, {this} is the declaring type (e.g. CultureInfo.CurrentCulture
            // → System.Globalization.CultureInfo.getCurrentCulture()); for instance, the receiver.
            var receiver = prop.IsStatic ? TypeRef(prop.ContainingType)
                : thisTarget is null ? "this" : Capture(() => EmitExpression(thisTarget));
            WriteTemplate(template, isStatic: prop.IsStatic, isExtension: false, receiver, new(), new(), TemplateTypeArgs(prop));
            return;
        }
        if (prop.IsStatic)
        {
            _w.Write(StaticMemberAccess(prop));
            return;
        }
        // `base.P` must reach the BASE type's accessor. `base` emits as `this`, so a plain `this.P`
        // dispatches to the most-derived override — exactly what `base` exists to bypass. Only
        // properties with real accessors need this; a plain auto-property IS its slot, which the base
        // and any (necessarily non-virtual) redeclaration cannot both own.
        if (thisTarget is BaseExpressionSyntax && HasEmittedAccessors(prop))
        {
            _w.Write($"TransposeR.baseGet({TypeRef(prop.ContainingType)}.prototype, ");
            _w.Write(JsString(TransposeNaming.MemberJsName(prop)));
            _w.Write(", this)");
            return;
        }
        EmitReceiver(thisTarget);
        _w.Write(TransposeNaming.MemberJsName(prop));
    }

    /// <summary>True when a property is emitted as a real accessor pair (a <c>props:</c> entry) rather
    /// than as a plain slot named after it — the same rule <c>EmitInstanceProperties</c> applies.</summary>
    private static bool HasEmittedAccessors(IPropertySymbol prop)
        => !prop.IsIndexer && !IsExternProperty(prop) && (!IsAutoProperty(prop) || IsFieldBackedProperty(prop));

    /// <summary>
    /// Maps a member's generic type-parameter names to the type arguments bound at the call
    /// site — for both the constructed containing type and (for generic methods) the method
    /// itself — so a [Template] token like {T} resolves to the concrete runtime type.
    /// </summary>
    private Dictionary<string, string>? TemplateTypeArgs(ISymbol member)
    {
        Dictionary<string, string>? map = null;
        void Add(System.Collections.Immutable.ImmutableArray<ITypeParameterSymbol> ps,
                 System.Collections.Immutable.ImmutableArray<ITypeSymbol> args)
        {
            if (ps.Length != args.Length) return;
            for (var i = 0; i < ps.Length; i++)
            {
                (map ??= new())[ps[i].Name] = member is IMethodSymbol mm
                    ? TemplateTypeRef(mm, args[i])
                    : TypeRef(args[i]);
                // {T:default} in a template → the default value of the bound type argument.
                map[ps[i].Name + ":default"] = DefaultValueLiteral(args[i]);
                // {T:ToString} → a value→string FUNCTION for the bound type (fn arg of
                // System.Nullable.toString). Only types whose native toString diverges from
                // .NET get one; the rest drop the arg (see SubstituteTemplate) and fall back.
                if (ToStringFnLiteral(args[i]) is { } tsFn)
                    map[ps[i].Name + ":ToString"] = tsFn;
            }
        }
        if (member.ContainingType is { IsGenericType: true } ct)
            Add(ct.OriginalDefinition.TypeParameters, ct.TypeArguments);
        if (member is IMethodSymbol { IsGenericMethod: true } m)
            Add(m.OriginalDefinition.TypeParameters, m.TypeArguments);
        return map;
    }

    /// <summary>
    /// The runtime type a <c>[Template]</c> token like <c>{T}</c> resolves to. Same as
    /// <see cref="TypeRef(ITypeSymbol)"/>, except that a type argument which is still the CALLEE's OWN
    /// type parameter — i.e. inference never substituted it — degrades to <c>System.Object</c> rather
    /// than emitting its bare name. That happens when a generic method is invoked with a
    /// <c>dynamic</c> argument: <c>Enumerable.Count(dyn)</c> expanded to
    /// <c>Enumerable.from(dyn, TSource)</c> and threw "TSource is not defined" at runtime. A type
    /// parameter of the *enclosing* generic method or type is a real substitution and keeps its name,
    /// which is in scope there as a threaded argument.
    /// </summary>
    private string TemplateTypeRef(IMethodSymbol callee, ITypeSymbol argument)
        => argument is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } tp
           && SymbolEqualityComparer.Default.Equals(
                  tp.DeclaringMethod?.OriginalDefinition, callee.OriginalDefinition)
            ? "System.Object"
            : TypeRef(argument);

    /// <summary>
    /// The JS function that converts a value of <paramref name="t"/> to its .NET <c>ToString()</c>
    /// form — used to resolve the <c>{T:ToString}</c> template placeholder (the <c>fn</c> argument of
    /// <c>System.Nullable.toString</c>). Returns <c>null</c> when the value's own runtime
    /// <c>toString()</c> already matches .NET (int, double, Int64, decimal, DateTime, structs, …);
    /// the runtime helper then falls back to <c>a.toString()</c>. Only <c>enum</c> (native toString
    /// gives the underlying number, not the name), <c>bool</c> ("true"/"false" vs .NET "True"/"False")
    /// and <c>char</c> (a code-point number vs the character) need an explicit converter.
    /// </summary>
    private string? ToStringFnLiteral(ITypeSymbol t)
    {
        if (t.TypeKind == TypeKind.Enum)
            return $"System.Enum.toStringFn({TypeRef(t)})";
        // A type parameter still unbound at emit time (a generic method calling string.Join over its
        // own IEnumerable<T>): defer the choice to the runtime, which hands back a converter only for
        // the types that need one and null otherwise — the same fallback the dropped slot produces.
        // Only emitted where the type parameter is in scope as a JS value: a method's parameters are
        // threaded as leading arguments (unless [IgnoreGeneric] drops them), a type's are parameters
        // of its define. Anything else — a parameter inherited from an enclosing generic type — is
        // not in scope, and drops out as before. Mirrors DefaultValueLiteral's scoping rule.
        if (t is ITypeParameterSymbol tp)
            return TypeParamInScope(tp) ? $"TransposeR.toStrFn({TypeRef(t)})" : null;
        return t.SpecialType switch
        {
            SpecialType.System_Boolean => "function ($v) { return System.Boolean.toString($v); }",
            SpecialType.System_Char    => "function ($v) { return String.fromCharCode($v); }",
            _                          => null,
        };
    }

    /// <summary>
    /// True if <paramref name="tp"/> is bound to a JS value in the current emit scope: a method type
    /// parameter is threaded as a leading argument of the emitted function (unless [IgnoreGeneric]
    /// drops them), a type's own parameter is a parameter of its <c>define</c>. A parameter from any
    /// other scope — one inherited from an enclosing generic type — is not available. Mirrors the
    /// scoping rule <see cref="DefaultValueLiteral"/> applies to <c>default(T)</c>.
    /// </summary>
    private bool TypeParamInScope(ITypeParameterSymbol tp)
        => tp.TypeParameterKind == TypeParameterKind.Method
            ? tp.DeclaringMethod is { } dm && ThreadsTypeArgs(dm)
            : _currentEmitType is not null && EffectiveTypeParameters(_currentEmitType)
                .Any(p => SymbolEqualityComparer.Default.Equals(p, tp));

    /// <summary>
    /// Emits <paramref name="operand"/> rendered as its .NET <c>ToString()</c> form when its static
    /// type is a type parameter only bound at runtime, letting the runtime pick the converter from the
    /// threaded type argument — so `T.ToString()` / `"" + t` inside a generic method renders an enum's
    /// name and a char's character rather than the underlying number, as a concrete call site does.
    /// Returns false for a concrete type (the caller's own char/enum/fallback branches handle those)
    /// and for a type parameter that is not in scope.
    /// </summary>
    private bool TryEmitTypeParamToString(ExpressionSyntax operand, ITypeSymbol? type)
    {
        if (type is not ITypeParameterSymbol tp || !TypeParamInScope(tp)) return false;
        _w.Write("TransposeR.toStrT(");
        EmitExpression(operand);
        _w.Write($", {TypeRef(type)})");
        return true;
    }

    private void EmitMethodGroup(IMethodSymbol method, ExpressionSyntax? thisTarget)
    {
        // A [Template] method referenced as a method group / delegate resolves to its `Fn` template
        // (the delegate form), not a bare member reference — e.g. `Func<string> f = someBool.ToString`
        // must be `System.Boolean.toString`, not `(b).toString.bind(b)` (native toString → "true", not
        // .NET "True"). The Fn expression is a function taking the value as its first argument
        // (System.Boolean.toString(v), System.DateTime.format(date, fmt), System.Nullable.toStringFn(fn)
        // → function(v)…), so an instance method group binds the receiver as that first argument.
        if (TransposeNaming.GetTemplateFn(method.OriginalDefinition) is { } fnTemplate)
        {
            var byName = new Dictionary<string, string>();
            AddTypeArguments(byName, method.OriginalDefinition, method);
            var fn = SubstituteTemplate(fnTemplate, null, byName, new List<string>());
            if (method.IsStatic)
            {
                _w.Write(fn);
            }
            else
            {
                _w.Write("(");
                _w.Write(fn);
                _w.Write(").bind(null, ");
                EmitReceiverExpr(thisTarget);
                _w.Write(")");
            }
            return;
        }

        // A [Template] method with no `Fn` form still has to expand its template when referenced as a
        // method group: the bare member reference either points at nothing (an [External] method has no
        // emitted body) or at a runtime function whose signature is not the template's — e.g.
        // `values.Select(JsonConvert.DeserializeObject<T>)` emitted
        // `Newtonsoft.Json.JsonConvert.DeserializeObject`, dropping `{T}` and landing Select's element
        // INDEX in the runtime's `type` slot. Wrap the expansion in a function whose parameters feed the
        // template's argument slots.
        if ((TransposeNaming.GetTemplate(method.OriginalDefinition) ?? TransposeNaming.GetTemplate(method)) is { } template)
        {
            EmitTemplateMethodGroup(method, template, thisTarget);
            return;
        }

        // A generic method that threads its type arguments takes them as LEADING parameters, so a
        // method group has to bind them into the delegate. Without this the delegate's first VALUE
        // argument landed in the T slot: `new[]{1,2}.Select(Show)` called Show(1, undefined), so
        // `typeof(T).Name` read "Number" and every T-typed parameter was undefined.
        var boundTypeArgs = ThreadsTypeArgs(method)
            ? string.Concat(method.TypeArguments.Select(t => ", " + TypeRef(t)))
            : "";

        if (method.IsStatic)
        {
            _w.Write(StaticMemberAccess(method));
            if (boundTypeArgs.Length > 0) _w.Write($".bind(null{boundTypeArgs})");
        }
        else
        {
            _w.Write("(");
            EmitReceiverExpr(thisTarget);
            _w.Write($").{TransposeNaming.MemberJsName(method)}.bind(");
            EmitReceiverExpr(thisTarget);
            _w.Write($"{boundTypeArgs})");
        }
    }

    /// <summary>
    /// Emits a method group over a <c>[Template]</c> method as a function whose parameters are the
    /// template's argument slots — <c>function ($a0) { return Newtonsoft.Json.JsonConvert.DeserializeObject($a0, Foo); }</c>
    /// — so the delegate applies the same expansion a direct call would. An instance (or extension)
    /// receiver is bound with <c>.bind(null, recv)</c> as the leading parameter, which evaluates the
    /// receiver once, where the method group is written, exactly as C# does.
    /// </summary>
    private void EmitTemplateMethodGroup(IMethodSymbol method, string template, ExpressionSyntax? thisTarget)
    {
        var reduced   = method is { IsExtensionMethod: true, ReducedFrom: { } r } ? r : null;
        var argNames  = method.Parameters.Select((_, i) => "$a" + i).ToList();
        var bindsRecv = !method.IsStatic || reduced is not null;
        var receiver  = bindsRecv ? "$this" : null;

        var byName    = new Dictionary<string, string>();
        var byPos     = new List<string>(argNames);

        // Reduced extension method: the receiver binds to the original first parameter (e.g. {source}),
        // the delegate's own arguments to the remaining ones — mirroring the invocation path.
        if (reduced is not null)
        {
            byName[reduced.Parameters[0].Name] = receiver!;
            for (var i = 0; i < argNames.Count && i + 1 < reduced.Parameters.Length; i++)
                byName[reduced.Parameters[i + 1].Name] = argNames[i];
            byPos = new List<string> { receiver! }.Concat(argNames).ToList();
            AddTypeArguments(byName, reduced, method);
        }
        else
        {
            for (var i = 0; i < method.Parameters.Length; i++) byName[method.Parameters[i].Name] = argNames[i];
            AddTypeArguments(byName, method.OriginalDefinition, method);
        }

        var body = Capture(() => WriteTemplate(template, method.IsStatic && reduced is null, reduced is not null, receiver, byName, byPos));
        var ps   = bindsRecv ? new List<string> { receiver! }.Concat(argNames) : argNames;

        _w.Write($"(function ({string.Join(", ", ps)}) {{ return {body}; }})");

        if (bindsRecv)
        {
            _w.Write(".bind(null, ");
            EmitReceiverExpr(thisTarget);
            _w.Write(")");
        }
    }

    private void EmitReceiver(ExpressionSyntax? thisTarget)
    {
        if (thisTarget is null) { _w.Write("this."); }
        else { EmitReceiverExpr(thisTarget); _w.Write("."); }
    }

    private void EmitReceiverExpr(ExpressionSyntax? thisTarget)
    {
        if (thisTarget is null) { _w.Write("this"); return; }
        // A numeric-constant receiver must be parenthesized: `0.toString()` is a JS syntax error
        // (the `.` parses as a decimal point), so emit `(0).toString()`.
        if (NeedsReceiverParens(thisTarget))
        {
            _w.Write("("); EmitExpression(thisTarget); _w.Write(")");
        }
        else EmitExpression(thisTarget);
    }

    /// <summary>True if the receiver would emit a bare numeric literal (integer constant), which is
    /// an invalid member-access target in JS without parentheses.</summary>
    private bool NeedsReceiverParens(ExpressionSyntax expr)
    {
        var cv = _model.GetConstantValue(expr);
        if (!cv.HasValue || cv.Value is null) return false;
        return cv.Value is int or long or short or byte or sbyte or uint or ulong or ushort or char;
    }

    private void EmitMemberAccess(MemberAccessExpressionSyntax member)
    {
        // `Transpose.Script.ToDynamic().Transpose.global.console` — ToDynamic() is the JS global
        // root ([GlobalTarget]); a member access on it is a plain global reference, so drop the
        // elided call and its dot and emit the member as a root identifier (→ Transpose.global.console).
        if (member.Expression is InvocationExpressionSyntax inv
            && _model.GetSymbolInfo(inv).Symbol is IMethodSymbol dynM
            && TransposeNaming.IsDynamicCast(dynM) && TransposeNaming.GlobalTargetName(dynM) is not null)
        {
            _w.Write(NameMangler.JsIdentifier(member.Name.Identifier.Text));
            return;
        }

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
                EmitConstRead(constField);
                return;
            case IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } enumField:
                EmitEnumMemberAccess(enumField);
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
                if (ev.IsStatic) { _w.Write(StaticMemberAccess(ev)); }
                else { EmitExpression(member.Expression); _w.Write("." + TransposeNaming.MemberJsName(ev)); }
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
    /// Emits a reference to an enum member honouring Transpose's <c>[Enum(Emit.X)]</c> mode:
    /// <c>Value</c> emits the numeric constant, the <c>StringName*</c> modes emit the
    /// member name as a (cased) string literal, and the <c>Name*</c> modes (and the
    /// default) reference the runtime enum object's member.
    /// </summary>
    private void EmitEnumMemberAccess(IFieldSymbol enumField)
    {
        switch (TransposeNaming.EnumEmitMode(enumField.ContainingType))
        {
            case 2: // Emit.Value
                _w.Write(ConstantLiteral(enumField.ConstantValue,
                    enumField.ContainingType.EnumUnderlyingType ?? enumField.Type));
                return;
            case 3 or 4 or 5 or 6: // Emit.StringName*
                _w.Write(JsString(TransposeNaming.EnumStringName(enumField, TransposeNaming.EnumEmitMode(enumField.ContainingType))));
                return;
            default: // Emit.Name* / default → the runtime enum object's member
                _w.Write(NameMangler.JsMemberAccess(TypeRef(enumField.ContainingType),
                                                    TransposeNaming.MemberJsName(enumField)));
                return;
        }
    }

    /// <summary>
    /// Emits an Transpose [Template]. For plain instance members whose template does not
    /// reference {this}, the template is relative to the receiver (e.g. "getTotalHours()"
    /// → "recv.getTotalHours()"); otherwise it is absolute.
    /// </summary>
    private void WriteTemplate(string template, bool isStatic, bool isExtension, string? receiver, Dictionary<string, string> argsByName, List<string> argsByPos, Dictionary<string, string>? typeArgs = null)
    {
        var sub = SubstituteTemplate(template, receiver, argsByName, argsByPos, typeArgs);
        // A leading "<self>" marker (or a {this...} reference) means the template is
        // self-contained; otherwise a bare instance template is relative to the receiver.
        var absolute = isStatic || isExtension || receiver is null
                       || template.Contains("{this") || template.Contains("<self>");
        _w.Write(absolute ? sub : receiver + "." + sub);
    }

    /// <summary>
    /// Substitutes an Transpose [Template] string. {this} → receiver, {paramName}/{index} → argument JS.
    /// </summary>
    private string SubstituteTemplate(string template, string? receiver, Dictionary<string, string> argsByName, List<string> argsByPos, Dictionary<string, string>? typeArgs = null)
    {
        // Strip the self-reference marker used by some Transpose templates (e.g. GetType()).
        template = template.Replace("<self>", "");
        var recv = receiver ?? "this";
        // {this:type} / {key:type} → runtime type via Transpose.getType(expr).
        template = System.Text.RegularExpressions.Regex.Replace(template, @"\{(this|\*?[A-Za-z_][A-Za-z0-9_]*|\d+):type\}", m =>
        {
            var tok = m.Groups[1].Value;
            var expr = tok == "this" ? recv
                : argsByName.TryGetValue(tok, out var av) ? av
                : int.TryParse(tok, out var i2) && i2 < argsByPos.Count ? argsByPos[i2]
                : recv;
            return $"Transpose.getType({expr})";
        });

        // {T:defaultFn} → a factory FUNCTION returning default(T), used where each consumer needs
        // an independent default value (Array.Clear/Resize fill one struct instance per slot):
        // System.Array.fill(dst, function () { return default(T); }, index, count). Resolved from
        // the same precomputed "T:default" value. Handled before {T:default} so it wins the match.
        template = System.Text.RegularExpressions.Regex.Replace(template, @"\{([A-Za-z_][A-Za-z0-9_]*):defaultFn\}", m =>
        {
            var key = m.Groups[1].Value + ":default";
            var d = argsByName.TryGetValue(key, out var dv) ? dv
                : typeArgs is not null && typeArgs.TryGetValue(key, out var td) ? td
                : "null";
            return "function () { return " + d + "; }";
        });

        // {T:default} → the default value of the type argument bound to T (precomputed in
        // the type-argument maps under the "T:default" key).
        template = System.Text.RegularExpressions.Regex.Replace(template, @"\{([A-Za-z_][A-Za-z0-9_]*):default\}", m =>
        {
            var key = m.Groups[1].Value + ":default";
            if (argsByName.TryGetValue(key, out var d)) return d;
            if (typeArgs is not null && typeArgs.TryGetValue(key, out var td)) return td;
            return "null";
        });

        // Sentinel for a template slot that resolves to no argument (e.g. an optional
        // trailing param not supplied); the slot and its leading comma are stripped after.
        const string drop = "￿";
        var posCursor = 0;
        // {name} / {*name} / {index}, optionally with an Transpose modifier ({name:array},
        // {name:nobox}, {name:raw}, …). The :type and :default modifiers were already
        // resolved above; the remaining modifiers don't change how the token resolves —
        // a params argument is captured in array form ("[a, b]") regardless — so the
        // modifier is accepted and the token is resolved the same as an unmodified one.
        var result = System.Text.RegularExpressions.Regex.Replace(template, @"\{(\*?[A-Za-z_][A-Za-z0-9_]*|\d+)(?::([A-Za-z_][A-Za-z0-9_]*))?\}", m =>
        {
            var token = m.Groups[1].Value;
            var modifier = m.Groups[2].Success ? m.Groups[2].Value : null;
            // {param:version} — the assembly/compiler version string (used by the SystemAssembly
            // version-marker template). Resolved to the version this build was invoked with.
            if (modifier == "version") return "\"" + AssemblyVersion + "\"";
            // {T:ToString} / {T:GetHashCode} → a value→string / value→hash FUNCTION for the bound
            // type, the fn arg of System.Nullable.toString / .getHashCode. Only a type whose native
            // toString diverges from .NET carries an explicit converter (in the type-arg map); every
            // other type — and all GetHashCode — drops the arg so the runtime falls back to
            // a.toString() / Transpose.getHashCode(a). Passing the bare type here (the old behaviour)
            // called the type as a function and crashed with "$initialize of undefined".
            if (modifier is "ToString" or "GetHashCode")
            {
                var fnKey = token + ":" + modifier;
                if (argsByName.TryGetValue(fnKey, out var fnExpr)) return fnExpr;
                if (typeArgs is not null && typeArgs.TryGetValue(fnKey, out var fnExpr2)) return fnExpr2;
                return drop;
            }
            if (token == "this") return ApplyArgModifier(modifier, receiver ?? "this");
            if (token.StartsWith("*"))
            {
                var n = token.Substring(1);
                return argsByName.TryGetValue(n, out var av) ? av : string.Join(", ", argsByPos);
            }
            if (argsByName.TryGetValue(token, out var v)) { posCursor++; return ApplyArgModifier(modifier, v); }
            // A generic type-parameter placeholder ({T}, {TSource}, …) → the type argument
            // bound at the call site (e.g. Comparer<string>.Default's {T} → System.String).
            if (typeArgs is not null && typeArgs.TryGetValue(token, out var ta)) return ta;
            if (int.TryParse(token, out var idx))
            {
                if (idx >= argsByPos.Count) return drop;
                posCursor = idx + 1;
                return argsByPos[idx];
            }
            // A named token with no matching parameter (some Transpose templates reuse a name like
            // {result} for the next positional slot) → the next unconsumed argument, else drop.
            if (posCursor < argsByPos.Count) return argsByPos[posCursor++];
            return drop;
        });

        // Remove dropped slots together with an adjacent comma so the call stays well-formed.
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s*,\s*￿", "");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"￿\s*,\s*", "");
        return result.Replace(drop, "");
    }

    /// <summary>
    /// Applies the <c>:raw</c> template modifier: a constant string argument is inserted as raw
    /// JS code rather than a quoted string — e.g. <c>Script.Call&lt;string&gt;("x.toString", 16)</c>
    /// with template <c>{name:raw}({args})</c> emits <c>x.toString(16)</c>, not <c>"x.toString"(…)</c>.
    /// </summary>
    private static string ApplyArgModifier(string? modifier, string value)
    {
        switch (modifier)
        {
            // :raw — a constant string argument inserted as raw JS code, not a quoted string.
            case "raw":
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    return value.Substring(1, value.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
                return value;
            // :array — the params argument as a JS array literal ([a, b]); params otherwise
            // resolve to the bare spread form (a, b).
            case "array":
                return "[" + value + "]";
            default:
                return value;
        }
    }

    /// <summary>
    /// The JS receiver a member/element binding resolves against inside a null-conditional
    /// continuation — the captured non-null temp (see <see cref="EmitConditionalAccess"/>).
    /// </summary>
    private string? _condReceiver;

    /// <summary>
    /// Emits the head of a null-conditional continuation (the <c>.b</c> in <c>a?.b</c>) against
    /// the captured receiver, honouring a property's [Template] (e.g. string.Length → x.length).
    /// </summary>
    private void EmitMemberBinding(MemberBindingExpressionSyntax binding)
    {
        var recv = _condReceiver ?? "this";
        var sym = _model.GetSymbolInfo(binding).Symbol;
        if (sym is IPropertySymbol { GetMethod: { } getM } prop
            && TransposeNaming.GetTemplate(getM.OriginalDefinition) is { } propTpl)
        {
            _w.Write(SubstituteTemplate(propTpl, recv, new(), new(), TemplateTypeArgs(prop)));
            return;
        }
        _w.Write($"{recv}.{(sym is not null ? TransposeNaming.MemberJsName(sym) : NameMangler.JsIdentifier(binding.Name.Identifier.Text))}");
    }

    private void EmitConditionalAccess(ConditionalAccessExpressionSyntax condAccess)
    {
        // a?.CHAIN → (function ($r) { return $r == null ? null : CHAIN($r); })(a), where the
        // leading member/element binding in CHAIN resolves against $r. A capture (rather than JS
        // optional chaining) is needed so the receiver can be threaded into an extension method's
        // static call (a?.Concat(x) → Enumerable.from($r).concat(x)).
        var temp = NextTemp("$nc");
        var recv = Capture(() => EmitExpression(condAccess.Expression));
        // Arrow (not `function`) so `this` inside the continuation is captured lexically.
        _w.Write($"(({temp}) => {temp} == null ? null : ");
        var saved = _condReceiver;
        _condReceiver = temp;
        EmitExpression(condAccess.WhenNotNull);
        _condReceiver = saved;
        _w.Write($")({recv})");
    }

    private void EmitElementBinding(ElementBindingExpressionSyntax elemBind)
    {
        var recv = _condReceiver ?? "this";
        if (_model.GetSymbolInfo(elemBind).Symbol is IPropertySymbol { IsIndexer: true } idx
            && idx.ContainingType.SpecialType != SpecialType.System_String
            && !TransposeNaming.IsNativeIndexer(idx))
        {
            _w.Write($"{recv}.{TransposeNaming.IndexerAccessorName(idx, isGet: true)}(");
            EmitArgumentList(elemBind.ArgumentList);
            _w.Write(")");
        }
        else
        {
            _w.Write($"{recv}[");
            EmitArgumentList(elemBind.ArgumentList);
            _w.Write("]");
        }
    }

}
