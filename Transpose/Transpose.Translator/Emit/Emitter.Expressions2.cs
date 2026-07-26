using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

public sealed partial class Emitter
{
    // ---- invocation --------------------------------------------------------

    private void EmitInvocation(InvocationExpressionSyntax invocation)
    {
        // nameof(...) is a compile-time constant string.
        if (invocation.Expression is IdentifierNameSyntax { Identifier.Text: "nameof" }
            && _model.GetConstantValue(invocation) is { HasValue: true, Value: string nameofValue })
        {
            _w.Write(JsString(nameofValue));
            return;
        }

        var symbol = _model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;

        if (symbol is null)
        {
            // A `Script.Write(...)` call with a `dynamic` argument binds as late-bound, so Symbol is
            // null — recover it from the candidates so the raw-JS template still inlines rather than
            // emitting a bogus `Transpose.Write("{0}…", …)` call.
            if (TryEmitScriptWrite(invocation, null)) return;
            EmitExpression(invocation.Expression);
            _w.Write("(");
            EmitArguments(invocation.ArgumentList, null);
            _w.Write(")");
            return;
        }

        // [GlobalTarget(name)]: an extern method that maps to a global JS function `name`
        // (e.g. `[GlobalTarget("alert")]` → `alert(...)`). An EMPTY name means the call compiles away
        // to a no-op — the pattern used for a `LazyLoad()` marker whose only purpose is to force the
        // assembly to be referenced ("compiles to an empty call"). Emit `void 0` so it is a valid
        // no-op in statement or expression position. (The ToDynamic global-root form is handled below.)
        if (!TransposeNaming.IsDynamicCast(symbol)
            && TransposeNaming.GlobalTargetName(symbol) is { } globalTarget)
        {
            if (string.IsNullOrWhiteSpace(globalTarget)) { _w.Write("void 0"); return; }
            _w.Write(globalTarget);
            _w.Write("(");
            EmitArguments(invocation.ArgumentList, symbol);
            _w.Write(")");
            return;
        }

        // Inside a null-conditional continuation, an invocation whose target is a member
        // binding (a?.M(...)) resolves M against the captured receiver.
        var condRecv = invocation.Expression is MemberBindingExpressionSyntax && _condReceiver is not null
            ? _condReceiver : null;

        // Delegate invocation: d(...) or d.Invoke(...) — the delegate is a plain callable.
        if (symbol.MethodKind == MethodKind.DelegateInvoke)
        {
            // For d.Invoke(...) call the receiver directly, dropping the ".Invoke".
            if (condRecv is not null)
                _w.Write(condRecv);
            else if (invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Invoke" } dm)
                EmitExpression(dm.Expression);
            else
                EmitExpression(invocation.Expression);
            _w.Write("(");
            EmitArguments(invocation.ArgumentList, symbol);
            _w.Write(")");
            return;
        }

        // Local function call → bare name. by-ref/out arguments still need the holder-object
        // mechanism (the local function's out param is emitted as a { v: … } holder), so route those
        // through EmitByRefInvocation exactly like a regular method call — otherwise an `out var _`
        // discard (or a named out arg) would be emitted as a raw argument and never write back.
        if (symbol.MethodKind == MethodKind.LocalFunction)
        {
            if (TransposeNaming.GetTemplate(symbol.OriginalDefinition) is null
                && HasByRefArguments(invocation.ArgumentList, symbol))
            {
                EmitByRefInvocation(invocation, symbol);
                return;
            }
            _w.Write(NameMangler.JsIdentifier(symbol.Name));
            _w.Write("(");
            EmitArguments(invocation.ArgumentList, symbol);
            _w.Write(")");
            return;
        }

        // ToDynamic() — a dynamic-cast window. Static [GlobalTarget] form (Transpose.Script.ToDynamic())
        // is the JS global root: emit the target name (member access on it is handled where it is the
        // receiver of a member access). Instance form (view.ToDynamic()) is an identity cast: emit the
        // receiver directly, dropping the call (view.ToDynamic().setInt16 → view.setInt16).
        if (TransposeNaming.IsDynamicCast(symbol))
        {
            if (TransposeNaming.GlobalTargetName(symbol) is { } gt) { _w.Write(gt); return; }
            if (invocation.Expression is MemberAccessExpressionSyntax { Expression: { } dynRecv })
            { EmitReceiverExpr(dynRecv); return; }
        }

        // enum.ToString() → System.Enum.toString(EnumType, value)
        if (symbol is { Name: "ToString", Parameters.Length: 0 }
            && invocation.Expression is MemberAccessExpressionSyntax { Expression: { } enumRecv }
            && _model.GetTypeInfo(enumRecv).Type is { TypeKind: TypeKind.Enum } enumType)
        {
            _w.Write($"System.Enum.toString({TypeRef(enumType)}, ");
            EmitExpression(enumRecv);
            _w.Write(")");
            return;
        }

        // char.ToString() → the single-character string. A char is a bare code-point number at
        // runtime, so its default `.toString()` would give the number ("65") not the character.
        if (symbol is { Name: "ToString", Parameters.Length: 0 }
            && invocation.Expression is MemberAccessExpressionSyntax { Expression: { } charRecv }
            && IsCharType(_model.GetTypeInfo(charRecv).Type))
        {
            _w.Write("String.fromCharCode(");
            EmitExpression(charRecv);
            _w.Write(")");
            return;
        }

        // bool.ToString() → "True"/"False" (.NET casing). A bool is a JS primitive whose native
        // .toString() gives "true"/"false"; route through System.Boolean.toString for parity.
        if (symbol is { Name: "ToString", Parameters.Length: 0 }
            && invocation.Expression is MemberAccessExpressionSyntax { Expression: { } boolRecv }
            && _model.GetTypeInfo(boolRecv).Type?.SpecialType == SpecialType.System_Boolean)
        {
            _w.Write("System.Boolean.toString(");
            EmitExpression(boolRecv);
            _w.Write(")");
            return;
        }

        // Transpose.Script.Write(code, args) — inject raw JavaScript, substituting {0},{1}… with args.
        if (TryEmitScriptWrite(invocation, symbol)) return;

        var origin = symbol.OriginalDefinition;
        var template = TransposeNaming.GetTemplate(origin) ?? TransposeNaming.GetTemplate(symbol);

        // A 2-arg [Template(format, nonExpandedFormat)]: when the trailing `params` argument is supplied
        // as a single array passed directly (non-expanded), prefer the nonExpandedFormat variant — e.g.
        // MethodInfo.Invoke(obj, argsArray) → midel(this,obj).apply(null, {arguments:array}) rather than
        // the expanded midel(this,obj)({*arguments}). Individual-element (expanded) calls keep `format`.
        if (template is not null && IsNonExpandedParamsCall(invocation.ArgumentList, symbol)
            && (TransposeNaming.GetTemplateNonExpanded(origin) ?? TransposeNaming.GetTemplateNonExpanded(symbol)) is { } nonExpandedTemplate)
        {
            template = nonExpandedTemplate;
        }

        // by-ref args (no template): holder objects with write-back.
        if (template is null && HasByRefArguments(invocation.ArgumentList, symbol))
        {
            EmitByRefInvocation(invocation, symbol);
            return;
        }

        var isBase = invocation.Expression is MemberAccessExpressionSyntax { Expression: BaseExpressionSyntax };
        var receiverExpr = invocation.Expression is MemberAccessExpressionSyntax ma && !isBase ? ma.Expression : null;

        if (template is not null && HasByRefArguments(invocation.ArgumentList, symbol))
        {
            EmitByRefTemplateInvocation(invocation, symbol, template);
            return;
        }

        if (template is not null)
        {
            var (byName, byPos) = CaptureArguments(invocation.ArgumentList, symbol);
            var receiver = symbol.IsStatic && !symbol.IsExtensionMethod ? null
                : receiverExpr is not null ? Capture(() => EmitReceiverExpr(receiverExpr))
                : condRecv ?? "this";

            // Reduced extension method: the receiver binds to the original first
            // parameter (e.g. {source}); actual args map to the remaining params.
            if (symbol is { IsExtensionMethod: true, ReducedFrom: { } reduced } && receiver is not null)
            {
                var rebuilt = new Dictionary<string, string>();
                rebuilt[reduced.Parameters[0].Name] = receiver;
                for (var i = 0; i < byPos.Count && i + 1 < reduced.Parameters.Length; i++)
                    rebuilt[reduced.Parameters[i + 1].Name] = byPos[i];
                byName = rebuilt;
                byPos = new List<string> { receiver }.Concat(byPos).ToList();
                AddTypeArguments(byName, reduced, symbol);
            }
            else
            {
                AddTypeArguments(byName, symbol.OriginalDefinition, symbol);
            }

            WriteTemplate(template, symbol.IsStatic, symbol.IsExtensionMethod, receiver, byName, byPos);
            return;
        }

        // base.Method(...) → Base.prototype.Method.call(this, args) for instance methods
        // (instance methods live on the prototype in the Transpose runtime); statics on the type.
        if (isBase)
        {
            var baseAccess = symbol.IsStatic ? "" : ".prototype";
            _w.Write($"{TypeRef(symbol.ContainingType)}{baseAccess}.{TransposeNaming.MemberJsName(symbol)}.call(this");
            if (invocation.ArgumentList.Arguments.Count > 0) { _w.Write(", "); EmitArguments(invocation.ArgumentList, symbol); }
            _w.Write(")");
            return;
        }

        // Extension method (no template) → StaticType.Method([typeArgs, ] receiver, args).
        // The static signature is Method(T…, this-param, params…): a generic extension threads
        // its *inferred* type arguments (from the constructed symbol) as the leading arguments,
        // then the receiver, then the remaining arguments mapped through the reduced symbol
        // (whose parameters already exclude `this` and preserve params-array collection).
        if (symbol is { IsExtensionMethod: true, ReducedFrom: { } reducedFrom } && (receiverExpr is not null || condRecv is not null))
        {
            _w.Write($"{TypeRef(symbol.ContainingType)}.{TransposeNaming.MemberJsName(reducedFrom)}(");
            var lead = EmitLeadingTypeArgs(symbol);
            if (lead) _w.Write(", ");
            if (receiverExpr is not null) EmitExpression(receiverExpr); else _w.Write(condRecv!);
            if (invocation.ArgumentList.Arguments.Count > 0)
            {
                _w.Write(", ");
                EmitArguments(invocation.ArgumentList, symbol, threadTypeArgs: false);
            }
            _w.Write(")");
            return;
        }

        // Instance method on an [ObjectLiteral] class → dispatch through the prototype:
        // Type.prototype.Method.call(receiver, args). The receiver is a plain JS object (typically
        // JSON-parsed) that carries no methods of its own, so a direct `receiver.Method(...)` would
        // throw "is not a function". `.call` binds `this` to the receiver, then the generic type
        // arguments and real arguments follow. Mirrors the legacy compiler's object-literal handling.
        if (IsObjectLiteralInstanceCall(symbol))
        {
            _w.Write($"{TypeRef(symbol.ContainingType)}.prototype.{TransposeNaming.MemberJsName(symbol)}.call(");
            if (receiverExpr is not null) EmitReceiverExpr(receiverExpr);
            else if (condRecv is not null) _w.Write(condRecv);
            else _w.Write("this");
            var rest = Capture(() => EmitArguments(invocation.ArgumentList, symbol));
            if (rest.Length > 0) { _w.Write(", "); _w.Write(rest); }
            _w.Write(")");
            return;
        }

        // Ordinary call.
        if (symbol.IsStatic)
        {
            _w.Write(StaticMemberAccess(symbol));
        }
        else if (receiverExpr is not null)
        {
            EmitReceiverExpr(receiverExpr);
            _w.Write($".{TransposeNaming.MemberJsName(symbol)}");
        }
        else if (condRecv is not null)
        {
            _w.Write($"{condRecv}.{TransposeNaming.MemberJsName(symbol)}");
        }
        else
        {
            _w.Write($"this.{TransposeNaming.MemberJsName(symbol)}");
        }
        _w.Write("(");
        EmitArguments(invocation.ArgumentList, symbol);
        _w.Write(")");
    }

    /// <summary>
    /// Handles <c>Transpose.Script.Write(code, args)</c> — inject raw JavaScript, substituting
    /// {0},{1}… with the emitted argument expressions. Returns true if the call was emitted.
    /// <paramref name="symbol"/> is the resolved method, or null when the call binds as late-bound
    /// (any <c>dynamic</c> argument makes Roslyn drop the symbol) — in that case the intended
    /// overload is recovered from the candidate symbols.
    /// </summary>
    private bool TryEmitScriptWrite(InvocationExpressionSyntax invocation, IMethodSymbol symbol)
    {
        var write = symbol ?? _model.GetSymbolInfo(invocation).CandidateSymbols
            .OfType<IMethodSymbol>().FirstOrDefault();

        if (write is not { Name: "Write" } || write.ContainingType?.ToDisplayString() != "Transpose.Script")
            return false;
        if (invocation.ArgumentList.Arguments.Count < 1
            || _model.GetConstantValue(invocation.ArgumentList.Arguments[0].Expression).Value is not string rawJs)
            return false;

        var argJs = invocation.ArgumentList.Arguments.Skip(1)
            .Select(a =>
            {
                var js = Capture(() => EmitExpression(a.Expression));
                // A lambda/delegate argument emits as a bare arrow function `() => {…}`, which is not a
                // primary expression: if the template drops it into call position (`{n}()`) or after a
                // member access, `() => {…}()` is a syntax error. Wrap it so it is safe anywhere.
                return EmitsAsInlineFunction(a.Expression) ? $"({js})" : js;
            }).ToList();
        // With no substitution arguments the code is injected verbatim — {…} sequences are
        // literal JS (e.g. regex quantifiers in "/^(.{8})(.{4})…$/"), not {0}/{1} placeholders.
        _w.Write(argJs.Count == 0 ? rawJs : SubstituteTemplate(rawJs, null, new(), argJs));
        return true;
    }

    /// <summary>
    /// True when <paramref name="expr"/> emits as an inline function expression (an arrow function),
    /// i.e. a lambda / anonymous method, or a delegate-creation / cast wrapping one. Such an emission
    /// is not a primary JS expression and must be parenthesized before it can sit in call or
    /// member-access position.
    /// </summary>
    private bool EmitsAsInlineFunction(ExpressionSyntax expr)
    {
        switch (expr)
        {
            case ParenthesizedExpressionSyntax paren:
                return EmitsAsInlineFunction(paren.Expression);
            case AnonymousFunctionExpressionSyntax:
                return true;
            case CastExpressionSyntax cast:
                return EmitsAsInlineFunction(cast.Expression);
            case ObjectCreationExpressionSyntax oc
                when _model.GetTypeInfo(oc).Type is INamedTypeSymbol { TypeKind: TypeKind.Delegate }
                     && oc.ArgumentList is { Arguments.Count: 1 }:
                return EmitsAsInlineFunction(oc.ArgumentList.Arguments[0].Expression);
            default:
                return false;
        }
    }

    /// <summary>Captures each argument's JS, keyed by parameter name and by position.</summary>
    /// <summary>
    /// True when the call supplies the method's trailing <c>params</c> parameter as a SINGLE array
    /// passed directly (non-expanded) — one positional argument per parameter, the last assignable to
    /// the params array type — rather than as individual elements. Mirrors the spread test in
    /// <see cref="CaptureArguments"/>; used to select the 2-arg [Template]'s nonExpandedFormat.
    /// </summary>
    private bool IsNonExpandedParamsCall(ArgumentListSyntax argList, IMethodSymbol method)
    {
        if (method.Parameters.Length == 0 || !method.Parameters[^1].IsParams) return false;

        var args = argList.Arguments;
        if (args.Count != method.Parameters.Length) return false;
        if (args.Any(a => a.NameColon is not null)) return false;

        var pi = method.Parameters.Length - 1;
        var soleArgType = _model.GetTypeInfo(args[pi].Expression).Type;
        return soleArgType is not null && _compilation.ClassifyConversion(soleArgType, method.Parameters[pi].Type).Exists;
    }

    private (Dictionary<string, string> byName, List<string> byPos) CaptureArguments(ArgumentListSyntax argList, IMethodSymbol method)
    {
        var byName = new Dictionary<string, string>();
        var byPos = new List<string>();
        var args = argList.Arguments;

        for (var i = 0; i < args.Count; i++)
        {
            var pType = i < method.Parameters.Length ? method.Parameters[i].Type : null;
            var idx = i;
            byPos.Add(Capture(() => EmitExpressionConverted(args[idx].Expression, pType)));
        }

        for (var pi = 0; pi < method.Parameters.Length; pi++)
        {
            var p = method.Parameters[pi];
            if (p.IsParams)
            {
                // A single trailing argument that IS the params array (e.g. an object[] passed to
                // `params object[]`) is the array itself, not one element: emit it JS-spread so
                // {args:array} yields the array ([...arr]) instead of double-wrapping it into [[...]]
                // (which made Activator.CreateInstance(type, new object[]{…}) pick the wrong ctor),
                // and a bare {args} still spreads to individual call arguments.
                var trailingCount = byPos.Count - pi;
                var soleArgType = trailingCount == 1 && pi < args.Count
                    ? _model.GetTypeInfo(args[pi].Expression).Type : null;
                byName[p.Name] = trailingCount == 1 && soleArgType is not null
                    && _compilation.ClassifyConversion(soleArgType, p.Type).Exists
                        ? "..." + byPos[pi]
                        // Otherwise resolve as the SPREAD (comma-joined) form — {args} in
                        // "System.String.format({format}, {args})" → format(fmt, a, b); a template that
                        // needs an array uses the :array modifier ({values:array} → [a, b]).
                        : string.Join(", ", byPos.Skip(pi));
            }
            else if (pi < byPos.Count)
            {
                byName[p.Name] = byPos[pi];
            }
        }
        return (byName, byPos);
    }

    /// <summary>Adds generic type-parameter bindings (e.g. {TSource} → System.Int32) for templates.</summary>
    private void AddTypeArguments(Dictionary<string, string> byName, IMethodSymbol definition, IMethodSymbol constructed)
    {
        for (var i = 0; i < definition.TypeParameters.Length && i < constructed.TypeArguments.Length; i++)
        {
            byName[definition.TypeParameters[i].Name] = TypeRef(constructed.TypeArguments[i]);
            byName[definition.TypeParameters[i].Name + ":default"] = DefaultValueLiteral(constructed.TypeArguments[i]);
            if (ToStringFnLiteral(constructed.TypeArguments[i]) is { } tsFn)
                byName[definition.TypeParameters[i].Name + ":ToString"] = tsFn;
        }

        var defType = definition.ContainingType;
        var conType = constructed.ContainingType;
        if (defType is not null && conType is not null)
        {
            for (var i = 0; i < defType.TypeParameters.Length && i < conType.TypeArguments.Length; i++)
            {
                byName[defType.TypeParameters[i].Name] = TypeRef(conType.TypeArguments[i]);
                byName[defType.TypeParameters[i].Name + ":default"] = DefaultValueLiteral(conType.TypeArguments[i]);
                if (ToStringFnLiteral(conType.TypeArguments[i]) is { } tsFn)
                    byName[defType.TypeParameters[i].Name + ":ToString"] = tsFn;
            }
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

    private void EmitCallee(InvocationExpressionSyntax invocation, IMethodSymbol symbol)
    {
        if (symbol.MethodKind == MethodKind.LocalFunction)
        {
            _w.Write(NameMangler.JsIdentifier(symbol.Name)); // local functions are called by bare name
        }
        else if (symbol.IsStatic)
        {
            _w.Write(StaticMemberAccess(symbol));
        }
        else if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            EmitExpression(memberAccess.Expression);
            _w.Write($".{TransposeNaming.MemberJsName(symbol)}");
        }
        else
        {
            _w.Write($"this.{TransposeNaming.MemberJsName(symbol)}");
        }
    }

    /// <summary>Template call with out/ref args → holder objects + write-back.</summary>
    private void EmitByRefTemplateInvocation(InvocationExpressionSyntax invocation, IMethodSymbol symbol, string template)
    {
        var args = invocation.ArgumentList.Arguments;
        var holders = new string?[args.Count];
        var byName = new Dictionary<string, string>();
        var byPos = new List<string>();

        // The whole invocation is the await scope, receiver included: `(await Q()).Items.TryGetFirst(out
        // var n)` awaits in the *receiver*, which the template's {this} emits inside the IIFE just as an
        // argument would.
        var hasAwait = OpenIife(invocation);
        for (var i = 0; i < args.Count; i++)
        {
            var isRef = args[i].RefKindKeyword.IsKind(SyntaxKind.OutKeyword) || args[i].RefKindKeyword.IsKind(SyntaxKind.RefKeyword);
            string val;
            if (isRef)
            {
                var holder = "$ref" + i;
                holders[i] = holder;
                var t = i < symbol.Parameters.Length ? symbol.Parameters[i].Type : null;
                var seed = args[i].RefKindKeyword.IsKind(SyntaxKind.OutKeyword) ? (t is not null ? DefaultValueLiteral(t) : "null") : Capture(() => EmitExpression(args[i].Expression));
                _w.Write($"var {holder} = {{ v: {seed} }}; ");
                val = holder;
            }
            else
            {
                var pType = i < symbol.Parameters.Length ? symbol.Parameters[i].Type : null;
                var idx = i;
                val = Capture(() => EmitExpressionConverted(args[idx].Expression, pType));
            }
            byPos.Add(val);
            if (i < symbol.Parameters.Length) byName[symbol.Parameters[i].Name] = val;
        }

        // Bind generic type parameters referenced by the template (e.g. Enum.TryParse<TEnum>).
        AddTypeArguments(byName, symbol.OriginalDefinition, symbol);

        // Skip write-back for discard targets (out _).
        for (var i = 0; i < args.Count; i++)
            if (holders[i] is not null && IsDiscardTarget(args[i].Expression)) holders[i] = null;

        _w.Write("var $ret = ");
        _w.Write(SubstituteTemplate(template, symbol.IsStatic ? null : "this", byName, byPos));
        _w.Write("; ");

        for (var i = 0; i < args.Count; i++)
        {
            if (holders[i] is null) continue;
            EmitByRefWriteBackTarget(args[i].Expression);
            _w.Write($" = {holders[i]}.v; ");
        }
        _w.Write("return $ret; ");
        CloseIife(hasAwait);
    }

    private void EmitByRefInvocation(InvocationExpressionSyntax invocation, IMethodSymbol symbol)
    {
        var args = invocation.ArgumentList.Arguments;
        var holders = new string?[args.Count];

        // The whole invocation is the await scope, receiver included — the reported failure was
        // `(await Query()).Nodes.TryGetFirst(out var n)`, where the await sits in the receiver rather
        // than in an argument, yet still lands inside the holder IIFE.
        var hasAwait = OpenIife(invocation);

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
        // An extension method with a by-ref/out parameter (e.g. IComponent.Var<T>(this T, out T))
        // is still a static call: emit ExtClass.Method(typeArgs, receiver, args…), not
        // receiver.Method(args…) — the receiver is the reduced method's first argument.
        var reduced = symbol.IsExtensionMethod ? symbol.ReducedFrom : null;
        var extReceiver = reduced is not null && invocation.Expression is MemberAccessExpressionSyntax ma ? ma.Expression : null;

        // Instance method on an [ObjectLiteral] class (with out/ref args) → dispatch through the
        // prototype, same as the ordinary path: Type.prototype.Method.call(receiver, typeArgs, args).
        // The `.call` receiver must precede the generic type arguments.
        var objLitCall = extReceiver is null && IsObjectLiteralInstanceCall(symbol);
        var objLitReceiver = objLitCall && invocation.Expression is MemberAccessExpressionSyntax malit
            && malit.Expression is not BaseExpressionSyntax ? malit.Expression : null;

        if (extReceiver is not null)
            _w.Write($"{TypeRef(symbol.ContainingType)}.{TransposeNaming.MemberJsName(reduced!)}");
        else if (objLitCall)
            _w.Write($"{TypeRef(symbol.ContainingType)}.prototype.{TransposeNaming.MemberJsName(symbol)}.call");
        else
            EmitCallee(invocation, symbol);
        _w.Write("(");
        bool first;
        if (objLitCall)
        {
            // Receiver first (becomes `this`), then the leading generic type arguments.
            if (objLitReceiver is not null) EmitExpression(objLitReceiver); else _w.Write("this");
            if (ThreadsTypeArgs(symbol))
                for (var ti = 0; ti < symbol.TypeArguments.Length; ti++)
                    { _w.Write(", "); _w.Write(TypeRef(symbol.TypeArguments[ti])); }
            first = false;
        }
        else
        {
            var lead = EmitLeadingTypeArgs(symbol);
            first = !lead;
            if (extReceiver is not null)
            {
                if (!first) _w.Write(", ");
                EmitExpression(extReceiver);
                first = false;
            }
        }
        if (args.Any(a => a.NameColon is not null))
        {
            // Named arguments: rebuild the positional list in parameter order and fill any omitted
            // optional that precedes a provided one with its default (a JS call can't skip a hole).
            // Without this a skipped optional shifted every later argument by one — e.g. Popover's
            // `Tippy.ShowFor(anchor, content, out hide, …, manualTrigger: true, …)` (no onClickOutside)
            // put `true` into the onClickOutside slot, so tippy's `props.onClickOutside` was a boolean
            // and its `.apply` threw. The out/ref holders are keyed by source-arg index, so they carry
            // across the reorder. (Any extension-method receiver was already emitted above.)
            var slotArg = new int[symbol.Parameters.Length];
            for (var k = 0; k < slotArg.Length; k++) slotArg[k] = -1;
            for (var i = 0; i < args.Count; i++)
            {
                if (args[i].NameColon is { } nc)
                {
                    var pi = ParameterIndex(symbol, nc.Name.Identifier.Text);
                    if (pi >= 0) slotArg[pi] = i;
                }
                // A positional argument maps to the parameter at its ORDINAL position (a positional arg
                // that follows a named one must be in its correct slot per C# 7.2), not to a running
                // count of positional args — otherwise a trailing positional overwrites the slot a
                // named argument already claimed.
                else if (i < slotArg.Length) slotArg[i] = i;
            }
            var lastSlot = -1;
            for (var k = 0; k < slotArg.Length; k++) if (slotArg[k] >= 0) lastSlot = k;
            for (var k = 0; k <= lastSlot; k++)
            {
                if (!first) _w.Write(", ");
                first = false;
                var ai = slotArg[k];
                if (ai >= 0)
                {
                    if (holders[ai] is not null) _w.Write(holders[ai]!);
                    else EmitExpressionConverted(args[ai].Expression, symbol.Parameters[k].Type);
                }
                else
                    // Omitted optional (a gap a JS call can't skip) → `void 0`, so the callee's
                    // `arg === undefined` default check applies its default (see EmitArguments).
                    _w.Write("void 0");
            }
        }
        else
        {
            for (var i = 0; i < args.Count; i++)
            {
                if (!first) _w.Write(", ");
                first = false;
                if (holders[i] is not null) _w.Write(holders[i]!);
                else EmitExpressionConverted(args[i].Expression, i < symbol.Parameters.Length ? symbol.Parameters[i].Type : null);
            }
        }
        _w.Write("); ");

        // Write back out/ref values to their targets (skip discards: out _).
        for (var i = 0; i < args.Count; i++)
        {
            if (holders[i] is null) continue;
            if (IsDiscardTarget(args[i].Expression)) continue;
            EmitByRefWriteBackTarget(args[i].Expression);
            _w.Write($" = {holders[i]}.v; ");
        }

        _w.Write("return $ret; ");
        CloseIife(hasAwait);
    }

    /// <summary>True if the expression contains an <c>await</c> in its own async context — i.e. not
    /// inside a nested lambda or local function, which have their own (possibly non-async) context.</summary>
    private static bool ContainsAwait(SyntaxNode node)
    {
        foreach (var n in node.DescendantNodesAndSelf(descendIntoChildren: c =>
                     c is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax))
        {
            if (n is AwaitExpressionSyntax) return true;
        }
        return false;
    }

    /// <summary>
    /// Opens an expression IIFE — <c>(() =&gt; { … })()</c> — the construct used wherever a C# expression
    /// needs statements to emit (out/ref holders, an object initializer, argument temporaries, …).
    ///
    /// When the wrapped syntax contains an <c>await</c> the arrow must be <c>async</c> and the call
    /// awaited: <c>await</c> inside a plain arrow is a JavaScript *syntax* error, so the whole bundle
    /// fails to parse — not just that one call. The enclosing function is guaranteed to be async, since
    /// C# only allows <c>await</c> inside an async method or lambda and the emitter emits those as async
    /// JS functions, so the added <c>await</c> is always legal.
    ///
    /// <paramref name="awaitScopes"/> must be the syntax that will actually be emitted *inside* the
    /// IIFE — pass the operands, not the whole expression, so a call that awaits somewhere outside the
    /// wrapper keeps its plain-arrow form.
    /// </summary>
    /// <returns>Whether the async form was written; hand it back to <see cref="CloseIife"/>.</returns>
    private bool OpenIife(params SyntaxNode?[] awaitScopes)
    {
        var hasAwait = false;
        foreach (var scope in awaitScopes)
        {
            if (scope is not null && ContainsAwait(scope)) { hasAwait = true; break; }
        }
        // Arrow (not `function`) so a `this`-qualified expression inside resolves to the enclosing
        // instance rather than being rebound to undefined in strict mode.
        _w.Write(hasAwait ? "(await (async () => { " : "(() => { ");
        return hasAwait;
    }

    /// <summary>Closes an IIFE opened by <see cref="OpenIife"/> — the extra parenthesis balances the
    /// <c>(await …</c> the async form opened with.</summary>
    private void CloseIife(bool hasAwait) => _w.Write(hasAwait ? "})())" : "})()");

    /// <summary>True for a discard target (out _ or a discard designation).</summary>
    private bool IsDiscardTarget(ExpressionSyntax expr)
        => expr is DeclarationExpressionSyntax { Designation: DiscardDesignationSyntax }
           || _model.GetSymbolInfo(expr).Symbol is IDiscardSymbol
           || (expr is IdentifierNameSyntax { Identifier.Text: "_" } && _model.GetSymbolInfo(expr).Symbol is null);

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

    /// <summary>
    /// Emits a generic method's type arguments as leading call arguments (matching the
    /// leading type parameters in its definition). Returns true if anything was written.
    /// </summary>
    private bool EmitLeadingTypeArgs(IMethodSymbol? method)
    {
        if (method is null || !ThreadsTypeArgs(method) || method.TypeArguments.Length == 0) return false;
        for (var i = 0; i < method.TypeArguments.Length; i++)
        {
            if (i > 0) _w.Write(", ");
            _w.Write(TypeRef(method.TypeArguments[i]));
        }
        return true;
    }

    /// <summary>Index of the parameter named <paramref name="name"/>, or -1. Scans the
    /// <c>ImmutableArray</c> in place (no <c>ToList</c> allocation) for a named-argument lookup.</summary>
    private static int ParameterIndex(IMethodSymbol method, string name)
    {
        var ps = method.Parameters;
        for (var i = 0; i < ps.Length; i++)
            if (ps[i].Name == name) return i;
        return -1;
    }

    private void EmitArguments(ArgumentListSyntax argList, IMethodSymbol? method, bool threadTypeArgs = true)
    {
        var args = argList.Arguments;
        var lead = threadTypeArgs && EmitLeadingTypeArgs(method);

        // Reorder named arguments to parameter order when we know the method.
        if (method is not null && args.Any(a => a.NameColon is not null))
        {
            var ordered = new ExpressionSyntax?[method.Parameters.Length];
            var paramIndexOf = new int[args.Count]; // source-arg k → the parameter index it binds to
            for (var i = 0; i < args.Count; i++)
            {
                var arg = args[i];
                if (arg.NameColon is not null)
                {
                    var idx = ParameterIndex(method, arg.NameColon.Name.Identifier.Text);
                    paramIndexOf[i] = idx;
                    if (idx >= 0) ordered[idx] = arg.Expression;
                }
                else if (i < ordered.Length)
                {
                    // A positional argument maps to the parameter at its ORDINAL position: C# requires a
                    // positional argument that follows a named one (C# 7.2+) to sit in its correct slot,
                    // so its index in the argument list IS its parameter index. A running "count of
                    // positional args so far" counter is wrong — a named argument earlier in the list
                    // (e.g. Do(type, accept, retriever: fn, flag)) already claimed slot 2, and the
                    // trailing positional `flag` must land in slot 3, not overwrite slot 2.
                    ordered[i] = arg.Expression;
                    paramIndexOf[i] = i;
                }
                else paramIndexOf[i] = -1;
            }

            // Positional JS calls can't skip a hole, so fill any omitted argument that
            // precedes a provided one with its parameter's default value. Trailing
            // omitted optionals are left off (the callee supplies its own defaults).
            var lastProvided = -1;
            for (var i = 0; i < ordered.Length; i++) if (ordered[i] is not null) lastProvided = i;

            // "Effectively reordered": the provided arguments' parameter indices are not ascending in
            // SOURCE order, so emitting them in parameter order would change the order side-effecting
            // argument expressions run in. C# evaluates arguments in source order; preserve that by
            // evaluating into temps (source order) and passing them back in parameter order. (When
            // already ascending, inline parameter-order emission is already source order — no wrap.)
            var reordered = false;
            for (int k = 0, prev = -1; k < paramIndexOf.Length; k++)
            {
                if (paramIndexOf[k] < 0) continue;
                if (paramIndexOf[k] < prev) { reordered = true; break; }
                prev = paramIndexOf[k];
            }
            var hasParams = method.Parameters.Length > 0 && method.Parameters[^1].IsParams;
            if (reordered && !hasParams)
            {
                if (lead) _w.Write(", ");
                // Every argument is evaluated inside this wrapper, so an await in any of them makes it
                // an async IIFE: `M(b: await X(), a: 1)`.
                _w.Write("...");
                var reorderHasAwait = OpenIife(argList);
                _w.Write("var $ = [");
                for (var k = 0; k < args.Count; k++)
                {
                    if (k > 0) _w.Write(", ");
                    var pIdx = paramIndexOf[k];
                    var pType = pIdx >= 0 && pIdx < method.Parameters.Length ? method.Parameters[pIdx].Type : null;
                    EmitExpressionConverted(args[k].Expression, pType);
                }
                _w.Write("]; return [");
                for (var i = 0; i <= lastProvided; i++)
                {
                    if (i > 0) _w.Write(", ");
                    var src = Array.IndexOf(paramIndexOf, i);
                    _w.Write(src >= 0 ? $"$[{src}]" : "void 0");
                }
                _w.Write("]; ");
                CloseIife(reorderHasAwait);
                return;
            }

            var first = !lead;
            for (var i = 0; i <= lastProvided; i++)
            {
                if (!first) _w.Write(", ");
                first = false;
                if (ordered[i] is null)
                {
                    // An omitted optional argument that a JS call cannot skip (it precedes a provided
                    // one) is passed as `void 0` (undefined), not its default value: the callee applies
                    // its own default via `if (arg === undefined) arg = <default>`, matching the legacy
                    // compiler. Passing `null` would defeat that check when the default is non-null.
                    _w.Write("void 0");
                }
                else if (i == method.Parameters.Length - 1 && method.Parameters[i].IsParams && ShouldWrapParams(method))
                {
                    // A single element supplied to the params parameter (positionally after named args,
                    // or BY NAME — e.g. `new SidebarNav(…, commands: cmd)`) must be wrapped into the
                    // params array; the array/collection itself passes through. Without this the callee
                    // receives a bare element and a later `foreach` over it throws "Cannot create
                    // Enumerator". (The multi-element / no-named-args case is handled by the params
                    // branch below, which this early-returning named path would otherwise skip.)
                    EmitParamsSlot(method.Parameters[i].Type, ordered[i]!);
                }
                else
                {
                    EmitExpressionConverted(ordered[i]!, method.Parameters[i].Type);
                }
            }
            return;
        }

        // [ExpandParams]: a native variadic function (e.g. DOMTokenList.add(params string[])).
        // Its trailing args are spread as individual arguments (add("a","b")), and a single array
        // argument is spread with JS spread (add(...arr)) so it reaches the native function as
        // separate tokens. Without this the params would be passed as ONE array argument —
        // classList.add(["a","b"]) coerces to the single malformed token "a,b".
        if (method is { Parameters.Length: > 0 } && method.Parameters[^1].IsParams && HasExpandParams(method))
        {
            var fixedCount = method.Parameters.Length - 1;
            var first = !lead;
            for (var i = 0; i < fixedCount && i < args.Count; i++)
            {
                if (!first) _w.Write(", ");
                first = false;
                EmitExpressionConverted(args[i].Expression, method.Parameters[i].Type);
            }
            var trailing = args.Skip(fixedCount).ToList();
            var elem = (method.Parameters[^1].Type as IArrayTypeSymbol)?.ElementType;
            if (trailing.Count == 1 && _model.GetTypeInfo(trailing[0].Expression).Type is IArrayTypeSymbol)
            {
                if (!first) _w.Write(", ");
                _w.Write("...");
                EmitExpression(trailing[0].Expression);
            }
            else
            {
                for (var i = 0; i < trailing.Count; i++)
                {
                    if (!first) _w.Write(", ");
                    first = false;
                    EmitExpressionConverted(trailing[i].Expression, elem);
                }
            }
            return;
        }

        // params array handling: collect trailing args into a JS array (except for
        // the variadic BCL formatters, whose runtime functions take individual args).
        if (method is { Parameters.Length: > 0 } && method.Parameters[^1].IsParams && ShouldWrapParams(method))
        {
            var fixedCount = method.Parameters.Length - 1;
            var first = !lead;
            for (var i = 0; i < fixedCount; i++)
            {
                if (!first) _w.Write(", ");
                first = false;
                // An optional fixed parameter omitted before the params array can't be skipped in a
                // positional JS call: emit `void 0` so the callee applies its default. Without this
                // the params array shifted into the optional's slot (e.g. M("x") on
                // M(string,int=7,params object[]) emitted M("x", []) — [] landed in the int slot).
                if (i < args.Count) EmitExpressionConverted(args[i].Expression, method.Parameters[i].Type);
                else _w.Write("void 0");
            }

            var trailing = args.Skip(fixedCount).ToList();
            if (!first) _w.Write(", ");

            var paramsType = method.Parameters[^1].Type;
            var paramsElem = ParamsElementType(paramsType);
            // A single argument that is itself the params ARRAY (convertible to the array type, e.g.
            // an object[] passed to `params object[]`, a List, a collection expression) is passed
            // through as the params value; otherwise the scattered args are collected into the params
            // collection. The test must be against the ARRAY type, not the element type: an object[]
            // IS convertible to the element `object`, so an element-type test double-wrapped it into
            // [object[]] (Length 1 instead of the array's real length).
            var soleArgType = trailing.Count == 1 ? _model.GetTypeInfo(trailing[0].Expression).Type : null;
            if (trailing.Count == 1
                && (soleArgType is null || _compilation.ClassifyConversion(soleArgType, paramsType).Exists))
            {
                EmitExpressionConverted(trailing[0].Expression, paramsType);
            }
            else
            {
                EmitCollectionOf(paramsType, () =>
                {
                    _w.Write("[");
                    for (var i = 0; i < trailing.Count; i++)
                    {
                        if (i > 0) _w.Write(", ");
                        EmitExpressionConverted(trailing[i].Expression, paramsElem);
                    }
                    _w.Write("]");
                }, trailing);
            }
            return;
        }

        for (var i = 0; i < args.Count; i++)
        {
            if (i > 0 || lead) _w.Write(", ");
            var targetType = method is not null && i < method.Parameters.Length ? method.Parameters[i].Type : null;
            EmitExpressionConverted(args[i].Expression, targetType);
        }
    }

    /// <summary>
    /// Whether a params argument should be wrapped into a JS array. The variadic BCL
    /// formatters (Console.Write/WriteLine, String.Format/Concat) take individual
    /// arguments in the runtime, so they are excluded.
    /// </summary>
    /// <summary>A method whose <c>params</c> array must be expanded (spread) at the call site,
    /// per Transpose's <c>[ExpandParams]</c> — the native variadic DOM/JS functions.</summary>
    private static bool HasExpandParams(IMethodSymbol method)
        => method.OriginalDefinition.GetAttributes()
            .Any(a => TransposeNaming.AttrIs(a, "Transpose.ExpandParamsAttribute"));

    /// <summary>Emits a SINGLE argument supplied to a <c>params</c> parameter: the array/collection
    /// itself is passed through, but a lone element (convertible to the element type) is wrapped into
    /// the params array. Shared by the named-argument path and mirrors the positional params branch.</summary>
    private void EmitParamsSlot(ITypeSymbol paramsType, ExpressionSyntax arg)
    {
        var paramsElem = ParamsElementType(paramsType);
        var argType = _model.GetTypeInfo(arg).Type;
        // Pass through when the argument IS the params array (convertible to the array type); wrap a
        // lone element otherwise. Testing the element type wrapped an object[] into [object[]], since
        // object[] → object exists.
        if (argType is null || _compilation.ClassifyConversion(argType, paramsType).Exists)
        {
            EmitExpressionConverted(arg, paramsType);   // already the collection
        }
        else
        {
            EmitCollectionOf(paramsType, () =>
            {
                _w.Write("[");
                EmitExpressionConverted(arg, paramsElem);
                _w.Write("]");
            }, [arg]);
        }
    }

    private static bool ShouldWrapParams(IMethodSymbol method)
    {
        var containing = method.ContainingType?.ToDisplayString();
        var name = method.Name;
        if (containing == "System.Console" && name is "Write" or "WriteLine") return false;
        if (containing == "System.String" && name is "Format" or "Concat") return false;
        return true;
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
        // new T() on a generic type parameter (new() constraint) → runtime instantiation. Both a
        // *type* parameter (threaded via the generic type's defining function) and a *method*
        // parameter (threaded as a leading JS argument of the generic method — the new() constraint
        // guarantees the method threads T) are in scope as the identifier tp.Name at runtime.
        if (_model.GetTypeInfo(creation).Type is ITypeParameterSymbol tp)
        {
            _w.Write($"Transpose.createInstance({tp.Name})");
            return;
        }
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
            // Both the constructor arguments and the initializer body are emitted inside the wrapper, so
            // either can carry the await: `new T(await A()) { X = await B() }`.
            var hasAwait = OpenIife(argList, initializer);
            _w.Write("var $o = ");
            EmitBareConstruction(type, ctor, argList);
            _w.Write("; ");
            EmitInitializer("$o", initializer);
            _w.Write("return $o; ");
            CloseIife(hasAwait);
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

        // A constructor [Template] defines how `new T(...)` is built — e.g. a DOM element's
        // ctor maps to document.createElement("span") instead of an (illegal) native `new`.
        if (ctor is not null && TransposeNaming.GetTemplate(ctor.OriginalDefinition) is { } ctorTemplate)
        {
            var (byName, byPos) = argList is not null ? CaptureArguments(argList, ctor) : (new(), new List<string>());
            WriteTemplate(ctorTemplate, isStatic: true, isExtension: false, receiver: null, byName, byPos, TemplateTypeArgs(ctor));
            return;
        }

        // Delegate construction: new Func<...>(target) → target
        if (type.TypeKind == TypeKind.Delegate)
        {
            if (argList is { Arguments.Count: 1 }) EmitExpression(argList.Arguments[0].Expression);
            else _w.Write("null");
            return;
        }

        // `new object()` is a plain empty JS object ({}), not an Transpose System.Object instance — matching
        // the legacy compiler. Code commonly uses it as a dynamic property bag whose own keys are
        // iterated (e.g. a Baklava node's inputs/outputs map); an Transpose instance would carry prototype
        // members that pollute that iteration.
        if (type.SpecialType == SpecialType.System_Object)
        {
            _w.Write("{ }");
            return;
        }

        // [ObjectLiteral] type → a plain JS object; the object initializer sets its members. The
        // ObjectInitializationMode controls which property initializers seed the object:
        // Initializer(1) emits the members that carry a `= value` initializer, DefaultValue(2)
        // emits every property, Ignore(0)/unspecified emits an empty object.
        if (type.GetAttributes().FirstOrDefault(a => TransposeNaming.AttrIs(a, "Transpose.ObjectLiteralAttribute")) is { } objLit
            // ObjectCreateMode.Constructor builds the instance by RUNNING its constructor (the runtime
            // Class is the real ctor when the type defines one), so `new T(a,b,c)` must invoke the ctor
            // and set the members — fall through to the normal constructor-call path below. Only
            // ObjectCreateMode.Plain (the default) emits the {} + initializer form here.
            && ObjectLiteralCreateMode(objLit) != 1)
        {
            var mode = ObjectLiteralInitMode(objLit);
            _w.Write("{");
            if (mode is 1 or 2)
            {
                var first = true;
                foreach (var prop in type.GetMembers().OfType<IPropertySymbol>()
                             .Where(p => !p.IsStatic && !p.IsIndexer && !p.IsWriteOnly))
                {
                    var propInit = PropertyInitializerExpr(prop);
                    if (mode == 1 && propInit is null) continue; // Initializer: only initialized members
                    if (!first) _w.Write(", ");
                    first = false;
                    _w.Write($"{NameMangler.JsPropertyKey(TransposeNaming.MemberJsName(prop))}: ");
                    if (propInit is not null) EmitExpression(propInit);
                    else _w.Write(DefaultValueLiteral(prop.Type));
                }
            }
            _w.Write("}");
            return;
        }

        // Constructor [Template] (some BCL types).
        var template = ctor is not null ? TransposeNaming.GetTemplate(ctor.OriginalDefinition) : null;
        if (template is not null && argList is not null)
        {
            var (byName, byPos) = CaptureArguments(argList, ctor!);
            _w.Write(SubstituteTemplate(template, null, byName, byPos));
            return;
        }

        var ctorName = ctor is not null ? CtorName(ctor) : "ctor";
        var typeRef = TypeRef(type);
        // A generic instantiation like Factory$1(Item) must be parenthesized before `new`.
        var newTarget = typeRef.Contains('(') ? $"({typeRef})" : typeRef;

        if (type.ToDisplayString() == "System.Exception" || TransposeNaming.IsExternalType(type))
        {
            // External / ambient-JS types (StringBuilder, Exception, RegExp, DOM globals like
            // MutationObserver, …) map to a native constructor that dispatches on arguments.
            // Checked before the in-source branch: an external stub is also "in source" when
            // self-building the BCL, but must still emit native `new RegExp(a, b)`, not `.$ctorN`.
            _w.Write($"new {typeRef}(");
            if (argList is not null) EmitArguments(argList, ctor);
            _w.Write(")");
        }
        else
        {
            // Any Transpose-defined type — whether in this compilation or a referenced assembly (its
            // BCL/library types go through the same Transpose.define runtime): the primary/default
            // constructor is directly `new`-able as `new Type(args)`; other overloads are named
            // methods (`new Type.$ctorN(args)`). This matches the legacy compiler, which never emitted
            // a `.ctor` call for the default constructor.
            _w.Write(ctorName == "ctor" ? $"new {newTarget}(" : $"new {newTarget}.{ctorName}(");
            if (argList is not null) EmitArguments(argList, ctor);
            _w.Write(")");
        }
    }

    /// <summary>True if <paramref name="type"/> or any of its base types is [ObjectLiteral]. The
    /// attribute is Inherited, so a derived literal type counts even when only the base carries it.</summary>
    private static bool IsObjectLiteralType(ITypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (t.GetAttributes().Any(a => TransposeNaming.AttrIs(a, "Transpose.ObjectLiteralAttribute")))
                return true;
        return false;
    }

    /// <summary>A call to an ordinary instance method declared on an [ObjectLiteral] class. Such
    /// instances are plain JS objects (typically JSON-parsed) with no prototype methods of their own,
    /// so the call must dispatch through the type's prototype (Type.prototype.Method.call(receiver, …))
    /// rather than as a direct member call. Interface members are excluded — that is a separate, rarer
    /// form the legacy compiler routes through the runtime type instead.</summary>
    private static bool IsObjectLiteralInstanceCall(IMethodSymbol symbol)
        => symbol is { IsStatic: false, IsExtensionMethod: false, MethodKind: MethodKind.Ordinary }
           && symbol.ContainingType is { TypeKind: not TypeKind.Interface }
           && IsObjectLiteralType(symbol.ContainingType);

    /// <summary>The ObjectInitializationMode of an [ObjectLiteral] attribute (0=Ignore, 1=Initializer,
    /// 2=DefaultValue), or 0 when unspecified — only the ObjectInitializationMode constructor argument
    /// is consulted (an ObjectCreateMode-only overload leaves the init mode at its Ignore default).</summary>
    private static int ObjectLiteralInitMode(AttributeData attr)
    {
        foreach (var arg in attr.ConstructorArguments)
            if (arg.Type?.ToDisplayString() == "Transpose.ObjectInitializationMode" && arg.Value is int v)
                return v;
        return 0;
    }

    /// <summary>The ObjectCreateMode of an [ObjectLiteral] attribute (0=Plain, 1=Constructor), or 0
    /// (Plain) when unspecified. Constructor means `new T(...)` runs the type's constructor instead of
    /// emitting a {} literal.</summary>
    private static int ObjectLiteralCreateMode(AttributeData attr)
    {
        foreach (var arg in attr.ConstructorArguments)
            if (arg.Type?.ToDisplayString() == "Transpose.ObjectCreateMode" && arg.Value is int v)
                return v;
        return 0;
    }

    /// <summary>The `= value` initializer expression of an auto-property, or null.</summary>
    private static ExpressionSyntax? PropertyInitializerExpr(IPropertySymbol prop)
        => (prop.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as PropertyDeclarationSyntax)?.Initializer?.Value;

    private void EmitInitializer(string target, InitializerExpressionSyntax initializer)
    {
        foreach (var expr in initializer.Expressions)
        {
            switch (expr)
            {
                // Object initializer member: X = value
                case AssignmentExpressionSyntax { Left: IdentifierNameSyntax name } assign:
                    var memberSym = _model.GetSymbolInfo(name).Symbol;
                    var memberName = memberSym is not null ? TransposeNaming.MemberJsName(memberSym) : NameMangler.JsIdentifier(name.Identifier.Text);
                    _w.Write($"{target}.{memberName} = ");
                    EmitExpression(assign.Right);
                    _w.Write("; ");
                    break;
                // Index initializer: [key] = value
                case AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax idx } assign:
                    _w.Write($"{target}.setItem(");
                    EmitArgumentList(idx.ArgumentList);
                    _w.Write(", ");
                    EmitExpression(assign.Right);
                    _w.Write("); ");
                    break;
                // Collection element with multiple values: { k, v }  (e.g. dictionary)
                case InitializerExpressionSyntax nested:
                    _w.Write($"{target}.{AddMethodName(nested)}(");
                    for (var i = 0; i < nested.Expressions.Count; i++)
                    {
                        if (i > 0) _w.Write(", ");
                        EmitExpression(nested.Expressions[i]);
                    }
                    _w.Write("); ");
                    break;
                // Collection element: single value
                default:
                    _w.Write($"{target}.{AddMethodName(expr)}(");
                    EmitExpression(expr);
                    _w.Write("); ");
                    break;
            }
        }
    }

    /// <summary>Resolves the JS name of the Add method a collection initializer element binds to.</summary>
    private string AddMethodName(ExpressionSyntax element)
    {
        if (_model.GetCollectionInitializerSymbolInfo(element).Symbol is IMethodSymbol add)
            return TransposeNaming.MemberJsName(add);
        return "add";
    }

    // ---- tuples ------------------------------------------------------------

    private void EmitTuple(TupleExpressionSyntax tuple)
    {
        var n = tuple.Arguments.Count;
        var tupleType = (_model.GetTypeInfo(tuple).ConvertedType ?? _model.GetTypeInfo(tuple).Type) as INamedTypeSymbol;

        // Emit a real System.ValueTuple$N instance so Equals / GetHashCode / ToString ("(a, b)")
        // behave like .NET; element access still reads .ItemN so deconstruction/named-element access
        // are unaffected. h5 emits `new (System.ValueTuple$N(types)).$ctor1(values)`. Arity > 7 uses a
        // nested ValueTuple (TRest) — fall back to the plain {Item1,…} object there (rare).
        if (tupleType is { IsTupleType: true } && n is >= 1 and <= 7)
        {
            var elements = tupleType.TupleElements;
            _w.Write($"new ({TypeRef(tupleType.TupleUnderlyingType ?? tupleType)}).$ctor1(");
            for (var i = 0; i < n; i++)
            {
                if (i > 0) _w.Write(", ");
                EmitExpressionConverted(tuple.Arguments[i].Expression, elements[i].Type);
            }
            _w.Write(")");
            return;
        }

        // Fallback (arity > 7 or a non-tuple type): a plain object; named-element access maps to ItemN.
        _w.Write("{ ");
        for (var i = 0; i < n; i++)
        {
            if (i > 0) _w.Write(", ");
            _w.Write($"Item{i + 1}: ");
            EmitExpression(tuple.Arguments[i].Expression);
        }
        _w.Write(" }");
    }

    private void EmitAnonymousObject(AnonymousObjectCreationExpressionSyntax anon)
    {
        _w.Write("{ ");
        for (var i = 0; i < anon.Initializers.Count; i++)
        {
            if (i > 0) _w.Write(", ");
            var init = anon.Initializers[i];
            // Member name: explicit (Name = expr) or inferred from the expression.
            var name = init.NameEquals?.Name.Identifier.Text
                ?? (init.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text
                ?? (init.Expression as IdentifierNameSyntax)?.Identifier.Text
                ?? $"Item{i + 1}";
            _w.Write($"{NameMangler.JsIdentifier(name)}: ");
            EmitExpression(init.Expression);
        }
        _w.Write(" }");
    }

    // ---- binary / unary / assignment --------------------------------------

    /// <summary>A user-defined operator on a non-external Transpose BCL struct whose operators are
    /// transpiled to real <c>op_</c> methods in the runtime (e.g. <c>System.DateTimeOffset</c>, whose
    /// <c>-</c>/<c>+</c>/comparisons must call those methods, not raw JS <c>-</c> on two objects).
    /// <c>DateTime</c> and <c>TimeSpan</c> are excluded: their arithmetic is handled by the
    /// <c>dt*</c>/<c>ts*</c> runtime helpers below and they expose no <c>op_</c> methods. External
    /// types (DateTime is not one, but Int64/Decimal/… are) and <c>extern</c> operators are excluded
    /// too — their operators are hand-written runtime primitives, not emitted <c>op_</c> methods.</summary>
    private static bool IsEmittableBclOperator(IMethodSymbol op)
    {
        var t = op.ContainingType;
        if (t is null || op.IsExtern || TransposeNaming.IsExternalType(t)) return false;
        if (t.ToDisplayString() is "System.DateTime" or "System.TimeSpan") return false;
        var asm = t.ContainingAssembly?.Name;
        return asm == "Transpose" || (asm is not null && asm.StartsWith("Transpose.", System.StringComparison.Ordinal));
    }

    /// <summary>True if the operand is the <c>null</c> literal (or a constant that evaluates to null,
    /// e.g. <c>default</c> for a reference/nullable type or a const null) — the marker for a
    /// null-test comparison rather than a value comparison.</summary>
    private bool IsNullLiteralOperand(ExpressionSyntax operand)
    {
        var e = operand is ParenthesizedExpressionSyntax paren ? paren.Expression : operand;
        if (e.IsKind(SyntaxKind.NullLiteralExpression)) return true;
        var cv = _model.GetConstantValue(e);
        return cv.HasValue && cv.Value is null;
    }

    private void EmitBinary(BinaryExpressionSyntax binary)
    {
        var op = binary.OperatorToken.Text;

        var leftType = _model.GetTypeInfo(binary.Left).ConvertedType ?? _model.GetTypeInfo(binary.Left).Type;
        var rightType = _model.GetTypeInfo(binary.Right).ConvertedType ?? _model.GetTypeInfo(binary.Right).Type;
        var resultType = _model.GetTypeInfo(binary).Type;

        // `x == null` / `x != null` (null literal on either side) is a null test in C#, NEVER a call
        // to a user-defined ==/!= operator — comparing a struct / Nullable<T> / reference to the null
        // literal tests the operand for null (a nullable value type checks HasValue). Emit a direct JS
        // null test (loose == null, matching the `is null` pattern, so it also catches undefined). This
        // must run before the user-defined-operator and value-equality paths below, otherwise e.g.
        // `dateTimeOffsetNullable != null` emitted System.DateTimeOffset.op_Inequality(x, null) which
        // then dereferenced the null argument (`null.UtcDateTime` → TypeError).
        if ((binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression))
            && (IsNullLiteralOperand(binary.Left) || IsNullLiteralOperand(binary.Right)))
        {
            var nonNull = IsNullLiteralOperand(binary.Left) ? binary.Right : binary.Left;
            EmitExpression(nonNull);
            _w.Write(binary.IsKind(SyntaxKind.NotEqualsExpression) ? " != null" : " == null");
            return;
        }

        // User-defined operator overloads → static op_ method call.
        // (Records synthesize op_Equality/op_Inequality; those are implicitly declared
        // and handled by the value-equality path below, so exclude them here.)
        if (_model.GetSymbolInfo(binary).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator, IsImplicitlyDeclared: false } opMethod
            && (opMethod.Locations.Any(l => l.IsInSource) || IsEmittableBclOperator(opMethod)))
        {
            // A [Template] operator (e.g. DateTime + TimeSpan → adddt({0}, {1})) expands via the
            // template. Bind the operands both by parameter name ({d}/{t}) AND positionally
            // ({0}/{1}) — operator templates use the positional form.
            if (TransposeNaming.GetTemplate(opMethod.OriginalDefinition) is { } opTpl)
            {
                var l = Capture(() => EmitExpression(binary.Left));
                var r = Capture(() => EmitExpression(binary.Right));
                var pars = opMethod.Parameters;
                WriteTemplate(opTpl, isStatic: true, isExtension: false, null,
                    new() { [pars[0].Name] = l, [pars[1].Name] = r }, new() { l, r });
                return;
            }
            // Only call the static op_ method when it is actually implemented (has a body). An
            // `extern` operator with no [Template] — e.g. System.Type's ==/!= on the [External]
            // reflection type — is reference equality; fall through to the built-in path below.
            if (!opMethod.IsExtern)
            {
                _w.Write($"{TypeRef(opMethod.ContainingType)}.{TransposeNaming.MemberJsName(opMethod)}(");
                EmitExpression(binary.Left);
                _w.Write(", ");
                EmitExpression(binary.Right);
                _w.Write(")");
                return;
            }
        }

        // is / as
        if (binary.IsKind(SyntaxKind.IsExpression))
        {
            // `x is Foo.Bar` parses as an is-EXPRESSION — a type test — whenever the right side looks
            // syntactically like a type name, even when it actually binds to a *constant*. An enum
            // member is exactly that, and GetTypeInfo only reports the constant's type (the enum), so
            // this used to emit `TransposeR.is(x, Small)`: true for every value of Small, making
            // `s is Small.A` true when s was Small.B. (The nested forms — `is not Small.A`,
            // `is Small.A or Small.B` — parse as real patterns and were always compared by value.)
            if (_model.GetSymbolInfo(binary.Right).Symbol is IFieldSymbol constantField)
            {
                // binary.Right is type-NAME syntax (a QualifiedName), which EmitExpression cannot emit,
                // so render the constant from its symbol the way a member access would.
                var constantJs = constantField switch
                {
                    { ContainingType.TypeKind: TypeKind.Enum } enumField => Capture(() => EmitEnumMemberAccess(enumField)),
                    { IsConst: true } c => ConstantLiteral(c.ConstantValue, c.Type),
                    _ => null,
                };

                if (constantJs is not null)
                {
                    // The subject is repeated by the value-equality test, so bind it once.
                    var subject = NextTemp("$is");
                    _w.Write($"(function ({subject}) {{ return ");
                    EmitConstantEqualityAgainst(subject, constantJs, constantField.Type);
                    _w.Write("; })(");
                    EmitExpression(binary.Left);
                    _w.Write(")");
                    return;
                }
            }

            var t = _model.GetTypeInfo(binary.Right).Type;
            _w.Write("TransposeR.is(");
            EmitExpression(binary.Left);
            _w.Write($", {TypeRef(t!)})");
            return;
        }
        if (binary.IsKind(SyntaxKind.AsExpression))
        {
            var t = _model.GetTypeInfo(binary.Right).Type;
            // `x as dynamic` is an identity in JS — member access on the result resolves
            // dynamically against the value itself, so there's no runtime type to check.
            if (t is null || t.TypeKind == TypeKind.Dynamic)
            {
                EmitExpression(binary.Left);
                return;
            }
            _w.Write("TransposeR.as(");
            EmitExpression(binary.Left);
            _w.Write($", {TypeRef(t)})");
            return;
        }

        // Delegate combine/remove via the binary + / - operators (d1 + d2, d1 - d2). Method groups
        // on either side are converted to the delegate type, so the result type is the delegate.
        // Mirrors the compound-assignment path (d += h / d -= h → combine/remove).
        if ((binary.IsKind(SyntaxKind.AddExpression) || binary.IsKind(SyntaxKind.SubtractExpression))
            && (resultType is { TypeKind: TypeKind.Delegate } || leftType is { TypeKind: TypeKind.Delegate }))
        {
            _w.Write($"TransposeR.{(binary.IsKind(SyntaxKind.AddExpression) ? "combine" : "remove")}(");
            EmitExpression(binary.Left);
            _w.Write(", ");
            EmitExpression(binary.Right);
            _w.Write(")");
            return;
        }

        // DateTime / TimeSpan arithmetic.
        var lName = leftType?.ToDisplayString();
        var rName = rightType?.ToDisplayString();
        if ((lName is "System.DateTime" or "System.TimeSpan") && (binary.IsKind(SyntaxKind.AddExpression) || binary.IsKind(SyntaxKind.SubtractExpression)))
        {
            var helper = (lName, rName, sub: binary.IsKind(SyntaxKind.SubtractExpression)) switch
            {
                ("System.DateTime", "System.DateTime", true) => "TransposeR.dtSub",
                ("System.DateTime", "System.TimeSpan", true) => "TransposeR.dtSubTs",
                ("System.DateTime", "System.TimeSpan", false) => "TransposeR.dtAddTs",
                ("System.TimeSpan", "System.TimeSpan", true) => "TransposeR.tsSub",
                ("System.TimeSpan", "System.TimeSpan", false) => "TransposeR.tsAdd",
                _ => null,
            };
            if (helper is not null)
            {
                _w.Write($"{helper}(");
                EmitExpression(binary.Left);
                _w.Write(", ");
                EmitExpression(binary.Right);
                _w.Write(")");
                return;
            }
        }

        // String concatenation
        if (binary.IsKind(SyntaxKind.AddExpression)
            && (IsStringType(leftType) || IsStringType(rightType) || IsStringType(resultType)))
        {
            // Use each operand's own type (not the converted/boxed type) so char
            // operands render as characters rather than their code points.
            EmitConcatOperand(binary.Left, _model.GetTypeInfo(binary.Left).Type ?? leftType);
            _w.Write(" + ");
            EmitConcatOperand(binary.Right, _model.GetTypeInfo(binary.Right).Type ?? rightType);
            return;
        }

        // Lifted operators on Nullable<T>: a null operand makes an arithmetic result null
        // and a relational result false (C# semantics). (Equality is fine with ===/!== since
        // nullable is represented as value-or-null.)
        //
        // This is tested BEFORE the 64-bit and decimal branches below, which match on the operand
        // types with Nullable stripped and so used to claim `long? + 1L` first, emitting
        // `System.Int64(null).add(1)` — 1 rather than null, while `int? + 1` propagated correctly.
        var arith = op is "+" or "-" or "*" or "/" or "%" or "&" or "|" or "^" or "<<" or ">>";
        var relational = op is "<" or ">" or "<=" or ">=";
        if ((arith || relational) && (IsNullableValueType(leftType) || IsNullableValueType(rightType)))
        {
            var l = Capture(() => EmitExpression(binary.Left));
            var r = Capture(() => EmitExpression(binary.Right));
            _w.Write($"({l} == null || {r} == null ? {(relational ? "false" : "null")} : ");
            EmitLiftedInnerOperation(binary, l, r, op, leftType, rightType);
            _w.Write(")");
            return;
        }

        // Lifted == / != where the underlying type is a runtime OBJECT (long/ulong/decimal). A plain
        // `===` compares object identity, so two `long?`s holding the same value were unequal.
        // System.Nullable.equals is exactly C#'s lifted equality: null equals only null, otherwise
        // value equality. (For plain-number underlying types `===` is already correct, null included.)
        if (op is "==" or "!="
            && (IsNullableValueType(leftType) || IsNullableValueType(rightType))
            && (IsRuntimeObjectNumeric(leftType) || IsRuntimeObjectNumeric(rightType)))
        {
            if (op == "!=") _w.Write("!");
            _w.Write("System.Nullable.equals(");
            EmitExpression(binary.Left);
            _w.Write(", ");
            EmitExpression(binary.Right);
            _w.Write(")");
            return;
        }

        // 64-bit integer arithmetic/comparison → System.Int64/UInt64 method calls. Decide on the
        // operands' DECLARED types, not the converted ones: `int >= uint` is promoted to `long` by
        // C#, but int/uint are plain JS numbers (only actual long/ulong are boxed Int64/UInt64
        // instances with .gte/.add/… methods), so such a comparison (bool result) stays a plain
        // operator. An ARITHMETIC op whose RESULT is 64-bit even though both operands are narrow —
        // `int + uint` promotes to `long` — must still produce a real Int64, or the long value flows
        // as a plain number into a `long` variable whose later `.lt`/`.add`/… throws
        // (`visualIndex.lt is not a function` in LogsView.ValidateVisibleRowHeights).
        var leftDeclared = _model.GetTypeInfo(binary.Left).Type ?? leftType;
        var rightDeclared = _model.GetTypeInfo(binary.Right).Type ?? rightType;
        // `long op double` (and float, and vice-versa) is promoted to floating point by C#, so it
        // must NOT go through the Int64 path: that lifts the FLOATING operand with System.Int64(…),
        // truncating it. `Sample() * range` in Random.Next(min, max) became
        // `System.Int64(Sample()).mul(range)` — the 0..1 sample truncated to 0, so the overload
        // always returned minValue. Read the 64-bit operand's magnitude and use plain JS arithmetic.
        if ((Is64BitInteger(leftDeclared) || Is64BitInteger(rightDeclared))
            && (IsFloatingType(leftDeclared) || IsFloatingType(rightDeclared))
            && Long64Op(binary) is not null)
        {
            EmitFloatingWith64BitOperand(binary, leftDeclared, rightDeclared);
            return;
        }
        // `long op decimal` (and vice-versa) is promoted to decimal by C#, so it must go through the
        // decimal path below — not Int64 (which would do integer division etc.). Guard against a
        // decimal operand here.
        if ((Is64BitInteger(leftDeclared) || Is64BitInteger(rightDeclared) || Is64BitInteger(resultType))
            && Long64Op(binary) is not null
            && !IsDecimalType(leftDeclared) && !IsDecimalType(rightDeclared))
        {
            EmitLong64Binary(binary, leftDeclared, rightDeclared);
            return;
        }

        // decimal arithmetic/comparison → System.Decimal method calls. Decide the receiver wrap on
        // the operand's DECLARED type (like the Int64 path above): only an actual `decimal` operand is
        // a System.Decimal instance with .add/.div/…; an int/long promoted to decimal by C# is still a
        // raw JS number / Int64 at runtime, so it must be lifted with System.Decimal(...). Testing the
        // converted type instead made `i + m` emit `i.add(m)` (TypeError) and `5L / 2m` do integer
        // division via Int64.div.
        if ((IsDecimalType(leftType) || IsDecimalType(rightType)) && DecimalOp(binary) is { } decOp)
        {
            if (IsDecimalType(leftDeclared)) EmitExpression(binary.Left);
            else { _w.Write("System.Decimal("); EmitExpression(binary.Left); _w.Write(")"); }
            _w.Write($".{decOp}(");
            if (IsDecimalType(rightDeclared)) EmitExpression(binary.Right);
            else { _w.Write("System.Decimal("); EmitExpression(binary.Right); _w.Write(")"); }
            _w.Write(")");
            return;
        }

        // Integer division. A 32-bit result is clipped for parity with .NET's unchecked wrapping —
        // int.MinValue / -1 overflows to int.MinValue (JS would give 2147483648).
        if (binary.IsKind(SyntaxKind.DivideExpression) && IsIntegerType(leftType) && IsIntegerType(rightType))
        {
            var divClip = Integer32Clip(resultType);
            if (divClip is not null) { _w.Write(divClip); _w.Write("("); }
            _w.Write("TransposeR.idiv(");
            EmitExpression(binary.Left);
            _w.Write(", ");
            EmitExpression(binary.Right);
            _w.Write(")");
            if (divClip is not null) _w.Write(")");
            return;
        }

        // 32-bit integer multiplication wraps (unchecked C# semantics); JS "*" does not,
        // so route through Math.imul via Transpose.Int.mul / umul.
        if (binary.IsKind(SyntaxKind.MultiplyExpression)
            && resultType?.SpecialType is SpecialType.System_Int32 or SpecialType.System_UInt32)
        {
            _w.Write(resultType.SpecialType == SpecialType.System_UInt32 ? "Transpose.Int.umul(" : "Transpose.Int.mul(");
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

        // Value equality for == / != on records and non-primitive structs (TimeSpan,
        // DateTime, DateTimeOffset, Guid, user structs…), which need memberwise/operator
        // equality rather than JS reference identity.
        if ((binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression))
            && (IsValueEqualityType(leftType) || IsValueEqualityType(rightType)))
        {
            if (binary.IsKind(SyntaxKind.NotEqualsExpression)) _w.Write("!");
            _w.Write("TransposeR.equals(");
            EmitExpression(binary.Left);
            _w.Write(", ");
            EmitExpression(binary.Right);
            _w.Write(")");
            return;
        }

        // Managed (H5-parity) 32-bit integer arithmetic: wrap results that overflow / need
        // unsigned reinterpretation, so `int + int`, `uint << n`, etc. match .NET's unchecked
        // semantics rather than JS Number arithmetic.
        if (TryEmitInteger32Binary(binary, resultType)) return;

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

    /// <summary>The runtime clip helper for a 32-bit integer result type (int → clip32,
    /// uint → clipu32), or null for any other type.</summary>
    private static string? Integer32Clip(ITypeSymbol? type) => type?.SpecialType switch
    {
        SpecialType.System_Int32 => "Transpose.Int.clip32",
        SpecialType.System_UInt32 => "Transpose.Int.clipu32",
        _ => null,
    };

    /// <summary>
    /// Emits a 32-bit integer <c>+ - &amp; | ^ &lt;&lt; &gt;&gt;</c> with .NET unchecked semantics
    /// (H5's "managed" integer rule): an int32 result that JS could push past 2^31 is clipped with
    /// clip32; a uint32 result is reinterpreted unsigned (clipu32) because JS <c>+</c>/bitwise yield
    /// a signed number, and <c>uint &gt;&gt;</c> uses a logical shift. Returns false for operators /
    /// types that already behave (%, comparisons, int32 bitwise, 64-bit, …) so the caller emits them
    /// plainly. Multiplication and division are wrapped by their own branches above.
    /// </summary>
    private bool TryEmitInteger32Binary(BinaryExpressionSyntax binary, ITypeSymbol? resultType)
    {
        var t = resultType?.SpecialType;
        if (t is not (SpecialType.System_Int32 or SpecialType.System_UInt32)) return false;
        var unsigned = t == SpecialType.System_UInt32;

        string? clip = null;
        var jsOp = binary.OperatorToken.Text;
        switch (binary.Kind())
        {
            case SyntaxKind.AddExpression:
            case SyntaxKind.SubtractExpression:
                clip = unsigned ? "Transpose.Int.clipu32" : "Transpose.Int.clip32";
                break;
            case SyntaxKind.BitwiseAndExpression:
            case SyntaxKind.BitwiseOrExpression:
            case SyntaxKind.ExclusiveOrExpression:
            case SyntaxKind.LeftShiftExpression:
                // JS bitwise/shift produce a signed int32; a uint result needs unsigned reinterpretation.
                if (unsigned) clip = "Transpose.Int.clipu32";
                break;
            case SyntaxKind.RightShiftExpression:
                // uint uses a logical shift (>>>) which is already an in-range uint; int uses >>.
                if (unsigned) jsOp = ">>>"; else return false;
                break;
            default:
                return false; // %, comparisons, etc. — no wrapping needed.
        }

        if (clip is not null) { _w.Write(clip); _w.Write("("); }
        EmitExpression(binary.Left);
        _w.Write($" {jsOp} ");
        EmitExpression(binary.Right);
        if (clip is not null) _w.Write(")");
        return true;
    }

    private static bool IsNullableValueType(ITypeSymbol? t)
        => t is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    /// <summary><c>T</c> for a <c>Nullable&lt;T&gt;</c>, otherwise the type unchanged.</summary>
    private static ITypeSymbol? UnwrapNullable(ITypeSymbol? t)
        => t is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1 } n
            ? n.TypeArguments[0]
            : t;

    /// <summary>
    /// The non-null half of a lifted <c>Nullable&lt;T&gt;</c> operator. The operands arrive as
    /// already-emitted JS snippets (they are evaluated twice — once for the null test, once here —
    /// so the caller captures them), and the operation is chosen by T's runtime representation:
    /// long/ulong and decimal are objects with method arithmetic, integer division truncates, and
    /// everything else is the plain JS operator.
    /// </summary>
    private void EmitLiftedInnerOperation(BinaryExpressionSyntax binary, string left, string right,
        string op, ITypeSymbol? leftType, ITypeSymbol? rightType)
    {
        // Decide on the operands' DECLARED types, as the non-nullable 64-bit path does: `long? * 0.5`
        // converts the left operand to double?, but at runtime it is still an Int64 instance.
        var lu = UnwrapNullable(_model.GetTypeInfo(binary.Left).Type ?? leftType);
        var ru = UnwrapNullable(_model.GetTypeInfo(binary.Right).Type ?? rightType);
        var resultType = UnwrapNullable(_model.GetTypeInfo(binary).Type);

        // Promoted to floating point despite a 64-bit operand (`long? * 0.5`): read the 64-bit
        // side's magnitude and use plain JS arithmetic, as the non-nullable path does.
        if ((Is64BitInteger(lu) || Is64BitInteger(ru)) && (IsFloatingType(lu) || IsFloatingType(ru)))
        {
            _w.Write(Is64BitInteger(lu) ? $"({left}).toNumber()" : left);
            _w.Write($" {op} ");
            _w.Write(Is64BitInteger(ru) ? $"({right}).toNumber()" : right);
            return;
        }

        if ((Is64BitInteger(lu) || Is64BitInteger(ru)) && Long64Op(binary) is { } longOp)
        {
            var unsigned = Is64BitUnsigned(lu) || Is64BitUnsigned(ru);
            if (longOp == "shr" && unsigned) longOp = "shru";

            // The receiver must be a 64-bit instance; lift a plain-number left operand.
            if (Is64BitInteger(lu)) _w.Write(left);
            else { _w.Write(unsigned ? "System.UInt64(" : "System.Int64("); _w.Write(left); _w.Write(")"); }

            _w.Write($".{longOp}({right})");
            return;
        }

        if ((IsDecimalType(lu) || IsDecimalType(ru)) && DecimalOp(binary) is { } decOp)
        {
            if (IsDecimalType(lu)) _w.Write(left);
            else { _w.Write("System.Decimal("); _w.Write(left); _w.Write(")"); }

            _w.Write($".{decOp}(");

            if (IsDecimalType(ru)) _w.Write(right);
            else { _w.Write("System.Decimal("); _w.Write(right); _w.Write(")"); }

            _w.Write(")");
            return;
        }

        var clip = Integer32Clip(resultType);

        // Integer division truncates toward zero; JS `/` does not.
        if (op == "/" && IsIntegerType(lu) && IsIntegerType(ru))
        {
            if (clip is not null) { _w.Write(clip); _w.Write("("); }
            _w.Write($"TransposeR.idiv({left}, {right})");
            if (clip is not null) _w.Write(")");
            return;
        }

        // 32-bit multiplication wraps; route through Math.imul as the non-nullable path does.
        if (op == "*" && clip is not null)
        {
            _w.Write(resultType!.SpecialType == SpecialType.System_UInt32 ? "Transpose.Int.umul(" : "Transpose.Int.mul(");
            _w.Write($"{left}, {right})");
            return;
        }

        // A uint32 result needs unsigned reinterpretation (JS `+`/bitwise yield a signed int32),
        // and `uint >>` is a logical shift — the same wrapping TryEmitInteger32Binary applies.
        var unsigned32 = resultType?.SpecialType == SpecialType.System_UInt32;
        var jsOp = op == ">>" && unsigned32 ? ">>>" : op;
        var needsClip = clip is not null && op switch
        {
            "+" or "-" => true,
            "&" or "|" or "^" or "<<" => unsigned32,
            _ => false,
        };

        if (needsClip) { _w.Write(clip!); _w.Write("("); }
        _w.Write($"{left} {jsOp} {right}");
        if (needsClip) _w.Write(")");
    }

    /// <summary>
    /// A type whose == / != is value equality rather than JS reference identity: records and
    /// non-primitive structs (TimeSpan, DateTime, Guid, user structs…). Primitive numerics,
    /// bool, char, enums, and the specially-handled long/decimal are excluded.
    /// </summary>
    private static bool IsValueEqualityType(ITypeSymbol? t)
    {
        if (t is { IsRecord: true }) return true;
        if (t is not { TypeKind: TypeKind.Struct }) return false;
        if (t.SpecialType != SpecialType.None) return false; // primitive value types
        if (Is64BitInteger(t) || IsDecimalType(t)) return false; // handled by their own ops
        if (t is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }) return false;
        return true;
    }

    /// <summary>The System.Int64/UInt64 method name for a binary operator, or null.</summary>
    private static string? Long64Op(BinaryExpressionSyntax b) => b.Kind() switch
    {
        SyntaxKind.AddExpression => "add",
        SyntaxKind.SubtractExpression => "sub",
        SyntaxKind.MultiplyExpression => "mul",
        SyntaxKind.DivideExpression => "div",
        SyntaxKind.ModuloExpression => "mod",
        SyntaxKind.LessThanExpression => "lt",
        SyntaxKind.GreaterThanExpression => "gt",
        SyntaxKind.LessThanOrEqualExpression => "lte",
        SyntaxKind.GreaterThanOrEqualExpression => "gte",
        SyntaxKind.EqualsExpression => "eq",
        SyntaxKind.NotEqualsExpression => "ne",
        SyntaxKind.BitwiseAndExpression => "and",
        SyntaxKind.BitwiseOrExpression => "or",
        SyntaxKind.ExclusiveOrExpression => "xor",
        SyntaxKind.LeftShiftExpression => "shl",
        SyntaxKind.RightShiftExpression => "shr",
        _ => null,
    };

    private static bool IsDecimalType(ITypeSymbol? t) => t?.SpecialType == SpecialType.System_Decimal;

    /// <summary>The System.Decimal method name for a binary operator, or null.</summary>
    private static string? DecimalOp(BinaryExpressionSyntax b) => b.Kind() switch
    {
        SyntaxKind.AddExpression => "add",
        SyntaxKind.SubtractExpression => "sub",
        SyntaxKind.MultiplyExpression => "mul",
        SyntaxKind.DivideExpression => "div",
        SyntaxKind.ModuloExpression => "mod",
        SyntaxKind.LessThanExpression => "lt",
        SyntaxKind.GreaterThanExpression => "gt",
        SyntaxKind.LessThanOrEqualExpression => "lte",
        SyntaxKind.GreaterThanOrEqualExpression => "gte",
        SyntaxKind.EqualsExpression => "equals",
        SyntaxKind.NotEqualsExpression => "ne",
        _ => null,
    };

    private void EmitLong64Binary(BinaryExpressionSyntax binary, ITypeSymbol? leftType, ITypeSymbol? rightType)
    {
        var op = Long64Op(binary)!;
        var unsigned = Is64BitUnsigned(leftType) || Is64BitUnsigned(rightType);
        if (op == "shr" && unsigned) op = "shru";

        // The receiver must be a 64-bit instance; lift the left operand if it is a plain number.
        if (Is64BitInteger(leftType))
        {
            EmitExpression(binary.Left);
        }
        else
        {
            _w.Write(unsigned ? "System.UInt64(" : "System.Int64(");
            EmitExpression(binary.Left);
            _w.Write(")");
        }
        _w.Write($".{op}(");
        EmitExpression(binary.Right);
        _w.Write(")");
    }

    /// <summary>
    /// Emits a binary operator whose operands C# promoted to <c>double</c>/<c>float</c> even though one
    /// of them is a <c>long</c>/<c>ulong</c> (an Int64/UInt64 object at runtime, not a JS number).
    /// The 64-bit side is read with <c>.toNumber()</c> — the same magnitude read an explicit
    /// <c>(double)someLong</c> cast emits — and the operator itself is plain JS, so the arithmetic is
    /// floating point as .NET does it.
    /// </summary>
    private void EmitFloatingWith64BitOperand(BinaryExpressionSyntax binary, ITypeSymbol? leftType, ITypeSymbol? rightType)
    {
        var jsOp = binary.Kind() switch
        {
            SyntaxKind.EqualsExpression => "===",
            SyntaxKind.NotEqualsExpression => "!==",
            _ => binary.OperatorToken.Text,
        };

        EmitOperandAsNumber(binary.Left, leftType);
        _w.Write($" {jsOp} ");
        EmitOperandAsNumber(binary.Right, rightType);

        void EmitOperandAsNumber(ExpressionSyntax operand, ITypeSymbol? type)
        {
            if (!Is64BitInteger(type) || EmitsAsPlainJsNumber(operand)) { EmitExpression(operand); return; }
            _w.Write("(");
            EmitExpression(operand);
            _w.Write(").toNumber()");
        }
    }

    /// <summary>
    /// True if a <c>long</c>/<c>ulong</c>-typed operand is nevertheless emitted as a plain JS number,
    /// so it must not be given a <c>.toNumber()</c> call. A numeric literal follows its CONVERTED type
    /// (see <c>EmitLiteral</c>): in <c>0.5 &gt; 0L</c> the literal is converted to double and emitted
    /// as <c>0</c>. A long-typed *identifier* in the same position is not — it stays an Int64 object.
    /// </summary>
    private bool EmitsAsPlainJsNumber(ExpressionSyntax operand)
    {
        var e = operand;
        while (true)
        {
            switch (e)
            {
                case ParenthesizedExpressionSyntax paren:
                    e = paren.Expression;
                    continue;
                // `-1L` / `+1L`: the sign is emitted around the literal, which folds the same way.
                case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.UnaryMinusExpression or (int)SyntaxKind.UnaryPlusExpression } unary:
                    e = unary.Operand;
                    continue;
            }
            break;
        }

        return e.IsKind(SyntaxKind.NumericLiteralExpression)
            && !Is64BitInteger(_model.GetTypeInfo(e).ConvertedType);
    }

    /// <summary>True if a string-typed concat operand can never be null, so it needs no <c>?? ""</c>
    /// coercion: a string literal, an interpolated string, the result of a string concatenation, or a
    /// constant with a non-null value.</summary>
    private bool IsNonNullStringOperand(ExpressionSyntax operand)
    {
        var e = operand is ParenthesizedExpressionSyntax paren ? paren.Expression : operand;
        if (e is LiteralExpressionSyntax or InterpolatedStringExpressionSyntax) return true;
        if (e is BinaryExpressionSyntax be && be.IsKind(SyntaxKind.AddExpression)
            && IsStringType(_model.GetTypeInfo(e).Type)) return true;
        var cv = _model.GetConstantValue(e);
        return cv.HasValue && cv.Value is not null;
    }

    private void EmitConcatOperand(ExpressionSyntax operand, ITypeSymbol? type)
    {
        if (IsStringType(type))
        {
            // C# string concatenation treats a null operand as "" (`null + "x"` is "x"), but JS `+`
            // renders null as "null". Coerce a possibly-null string operand with `?? ""`. Operands that
            // are provably non-null (a string literal, an interpolated string, the result of another
            // string concatenation, or a non-null constant) are emitted as-is.
            if (IsNonNullStringOperand(operand))
            {
                EmitExpression(operand);
            }
            else
            {
                _w.Write("(");
                EmitExpression(operand);
                _w.Write(" ?? \"\")");
            }
        }
        else if (IsCharType(type))
        {
            _w.Write("TransposeR.chr(");
            EmitExpression(operand);
            _w.Write(")");
        }
        else if (type is { TypeKind: TypeKind.Enum })
        {
            _w.Write($"System.Enum.toString({TypeRef(type)}, ");
            EmitExpression(operand);
            _w.Write(")");
        }
        else
        {
            _w.Write("TransposeR.toStr(");
            EmitExpression(operand);
            _w.Write(")");
        }
    }

    private void EmitAssignment(AssignmentExpressionSyntax assignment)
    {
        var op = assignment.OperatorToken.Text;
        var leftType = _model.GetTypeInfo(assignment.Left).Type;
        var rightType = _model.GetTypeInfo(assignment.Right).Type;

        // Discard assignment: _ = expr → evaluate expr for its side effects only.
        if (op == "=" && _model.GetSymbolInfo(assignment.Left).Symbol is IDiscardSymbol)
        {
            EmitExpression(assignment.Right);
            return;
        }

        // `this = expr` inside a struct member (JS cannot assign `this`): copy the value's fields
        // onto the current instance, matching C# struct value-replacement semantics.
        if (op == "=" && assignment.Left is ThisExpressionSyntax)
        {
            _w.Write("Object.assign(this, ");
            EmitExpression(assignment.Right);
            _w.Write(")");
            return;
        }

        // Multi-dimensional array element write grid[i, j] = v → System.Array.set(grid, v, i, j).
        if (op == "=" && assignment.Left is ElementAccessExpressionSyntax mdea
            && mdea.ArgumentList.Arguments.Count > 1
            && _model.GetTypeInfo(mdea.Expression).Type is IArrayTypeSymbol { Rank: > 1 })
        {
            _w.Write("System.Array.set(");
            EmitExpression(mdea.Expression);
            _w.Write(", ");
            EmitExpressionConverted(assignment.Right, leftType);
            foreach (var a in mdea.ArgumentList.Arguments) { _w.Write(", "); EmitExpression(a.Expression); }
            _w.Write(")");
            return;
        }

        // Indexer set on a collection: coll[i] = v → coll.setItem(i, v)
        if (op == "=" && assignment.Left is ElementAccessExpressionSyntax ea
            && _model.GetSymbolInfo(ea).Symbol is IPropertySymbol { IsIndexer: true } idx
            && idx.ContainingType.SpecialType != SpecialType.System_String)
        {
            // Indexer setter [Template].
            if (idx.SetMethod is { } setIdx && TransposeNaming.GetTemplate(setIdx.OriginalDefinition) is { } setIdxTpl)
            {
                var recv = Capture(() => EmitExpression(ea.Expression));
                var args = ea.ArgumentList.Arguments.Select(a => Capture(() => EmitExpression(a.Expression))).ToList();
                args.Add(Capture(() => EmitExpressionConverted(assignment.Right, leftType)));
                WriteTemplate(setIdxTpl, idx.IsStatic, isExtension: false, recv, new(), args);
                return;
            }
            // An [External] type's plain indexer sets via native bracket access (domElement["name"] = v).
            if (TransposeNaming.IsNativeIndexer(idx))
            {
                EmitExpression(ea.Expression);
                _w.Write("[");
                EmitArgumentList(ea.ArgumentList);
                _w.Write("] = ");
                EmitExpressionConverted(assignment.Right, leftType);
                return;
            }
            EmitExpression(ea.Expression);
            _w.Write("." + TransposeNaming.IndexerAccessorName(idx, isGet: false) + "(");
            EmitArgumentList(ea.ArgumentList);
            _w.Write(", ");
            EmitExpressionConverted(assignment.Right, leftType);
            _w.Write(")");
            return;
        }

        // Property setter with a [Template] (e.g. StringBuilder.Length → setLength({0})).
        if (op == "=" && _model.GetSymbolInfo(assignment.Left).Symbol is IPropertySymbol { SetMethod: { } setter } setProp
            && !setProp.IsIndexer
            && TransposeNaming.GetTemplate(setter.OriginalDefinition) is { } setTemplate)
        {
            var recv = setProp.IsStatic ? TypeRef(setProp.ContainingType)
                : assignment.Left is MemberAccessExpressionSyntax sma ? Capture(() => EmitExpression(sma.Expression))
                : "this";
            var val = Capture(() => EmitExpressionConverted(assignment.Right, leftType));
            WriteTemplate(setTemplate, setProp.IsStatic, isExtension: false, recv,
                new() { ["value"] = val }, new() { val });
            return;
        }

        // Compound assignment to a collection indexer that stores via setItem: `coll[i] op= v` becomes
        // `coll.setItem(i, <coll[i] op v>)`. The generic compound branches below emit the write target
        // as `coll.getItem(i)` — correct to READ but invalid as an assignment target (`getItem(i) = …`
        // assigns to an rvalue). Route the write through setItem here, computing the new element value
        // with the same rules the plain-lvalue paths use. A native-bracket or [Template]-setter indexer
        // is a real JS lvalue (`d[i] op= v`) and falls through unchanged.
        if (op != "=" && assignment.Left is ElementAccessExpressionSyntax cea
            && _model.GetSymbolInfo(cea).Symbol is IPropertySymbol { IsIndexer: true } cidx
            && cidx.ContainingType.SpecialType != SpecialType.System_String
            && !TransposeNaming.IsNativeIndexer(cidx)
            && !(cidx.SetMethod is { } cset && TransposeNaming.GetTemplate(cset.OriginalDefinition) is not null))
        {
            EmitExpression(cea.Expression);
            _w.Write("." + TransposeNaming.IndexerAccessorName(cidx, isGet: false) + "(");
            EmitArgumentList(cea.ArgumentList);
            _w.Write(", ");
            EmitCompoundElementValue(assignment, op, leftType, rightType);
            _w.Write(")");
            return;
        }

        // Compound assignment to a property with a [Template] setter: `obj.P op= v` becomes
        // `<setter template>(<obj.P op v>)` — e.g. StringBuilder.Length (getLength/setLength). As with
        // indexers, the plain-lvalue branches would emit the getter template (`obj.getLength()`) as the
        // write target, which is invalid ("assignment to rvalue").
        if (op != "=" && _model.GetSymbolInfo(assignment.Left).Symbol is IPropertySymbol { SetMethod: { } cpSetter } cpProp
            && !cpProp.IsIndexer
            && TransposeNaming.GetTemplate(cpSetter.OriginalDefinition) is { } cpSetTemplate)
        {
            var recv = cpProp.IsStatic ? TypeRef(cpProp.ContainingType)
                : assignment.Left is MemberAccessExpressionSyntax csma ? Capture(() => EmitExpression(csma.Expression))
                : "this";
            var val = Capture(() => EmitCompoundElementValue(assignment, op, leftType, rightType));
            WriteTemplate(cpSetTemplate, cpProp.IsStatic, isExtension: false, recv,
                new() { ["value"] = val }, new() { val });
            return;
        }

        // Delegate / event subscription: d += h, d -= h, ev += h, ev -= h → combine/remove.
        if ((op == "+=" || op == "-=")
            && (leftType is { TypeKind: TypeKind.Delegate }
                || _model.GetSymbolInfo(assignment.Left).Symbol is IEventSymbol))
        {
            EmitExpression(assignment.Left);
            _w.Write($" = TransposeR.{(op == "+=" ? "combine" : "remove")}(");
            EmitExpression(assignment.Left);
            _w.Write(", ");
            EmitExpression(assignment.Right);
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

        // Compound arithmetic/bitwise assignment to a ≤32-bit integer: `lhs op= rhs` is
        // `lhs = (T)(lhs op rhs)`, wrapping the result to T's width with .NET unchecked semantics.
        // JS compound ops skip that (T) cast (and are signed / can overflow), so rebuild the
        // assignment explicitly with the same wrapping the binary path uses — keeping `i += j`
        // consistent with `i = i + j`. Multiplication uses imul (plain `*` loses precision above
        // 2^53); division uses idiv. 64-bit and non-integer targets fall through.
        if (IsNarrowIntegerTarget(leftType)
            && op is "+=" or "-=" or "*=" or "/=" or "%=" or "&=" or "|=" or "^=" or "<<=" or ">>=")
        {
            var t = leftType!.SpecialType;
            var sub = IsSubWordIntegerTarget(leftType);
            var binOp = op[..^1];

            EmitExpression(assignment.Left);
            _w.Write(" = ");

            // 32-bit multiplication needs imul; sub-word uses plain `*` (operands < 2^16) then clip.
            if (binOp == "*" && !sub)
            {
                _w.Write(t == SpecialType.System_UInt32 ? "Transpose.Int.umul(" : "Transpose.Int.mul(");
                EmitExpression(assignment.Left); _w.Write(", "); EmitExpression(assignment.Right); _w.Write(")");
                return;
            }

            // Wrapper needed to narrow / reinterpret the JS result to T (null where JS is already
            // correct): sub-word narrows every op but `%`; int32 only +,-,/ can leave int32 range
            // (bitwise/shift already yield int32, `%` is in range); uint32 needs unsigned reinterpret
            // for everything but `%` and `>>` (which uses the logical shift `>>>`).
            var innerOp = binOp == ">>" && t == SpecialType.System_UInt32 ? ">>>" : binOp;
            string? clip = sub ? (binOp == "%" ? null : NarrowIntegerClip(t))
                : t == SpecialType.System_UInt32 ? (binOp is "%" or ">>" ? null : "Transpose.Int.clipu32")
                : (binOp is "+" or "-" or "/" ? "Transpose.Int.clip32" : null);

            if (clip is not null) { _w.Write(clip); _w.Write("("); }
            if (binOp == "/")
            {
                _w.Write("TransposeR.idiv("); EmitExpression(assignment.Left); _w.Write(", "); EmitExpression(assignment.Right); _w.Write(")");
            }
            else
            {
                _w.Write("("); EmitExpression(assignment.Left); _w.Write($") {innerOp} ("); EmitExpression(assignment.Right); _w.Write(")");
            }
            if (clip is not null) _w.Write(")");
            return;
        }

        // 64-bit integer / decimal compound assignment: `lhs op= rhs` → `lhs = lhs.<method>(rhs)`.
        // The target is a boxed Int64/UInt64/Decimal instance, so a native JS `+=`/`/=`/… would run
        // string-concat / float ops on the object rather than the type's method (10L += 3L → "103").
        if (op != "=" && (Is64BitInteger(leftType) || IsDecimalType(leftType))
            && Long64OrDecimalCompoundMethod(op, leftType) is not null)
        {
            EmitExpression(assignment.Left);
            _w.Write(" = ");
            EmitLong64OrDecimalCompoundValue(assignment.Left, assignment.Right, op, leftType);
            return;
        }

        EmitExpression(assignment.Left);
        _w.Write($" {op} ");
        EmitExpressionConverted(assignment.Right, leftType);
    }

    /// <summary>The Int64/UInt64 or Decimal instance-method name for a compound-assignment operator
    /// (<c>+=</c> → add, …), or null if the operator/type has no such method.</summary>
    private static string? Long64OrDecimalCompoundMethod(string op, ITypeSymbol? leftType)
    {
        var binOp = op[..^1];
        if (Is64BitInteger(leftType))
            return binOp switch
            {
                "+" => "add", "-" => "sub", "*" => "mul", "/" => "div", "%" => "mod",
                "&" => "and", "|" => "or", "^" => "xor", "<<" => "shl",
                ">>" => Is64BitUnsigned(leftType) ? "shru" : "shr",
                _ => null,
            };
        if (IsDecimalType(leftType))
            return binOp switch { "+" => "add", "-" => "sub", "*" => "mul", "/" => "div", "%" => "mod", _ => null };
        return null;
    }

    /// <summary>Emits the rebuilt value <c>left.&lt;method&gt;(right)</c> for a 64-bit / decimal compound
    /// assignment (used by both the plain-lvalue and the indexer-element paths). Shift counts stay raw
    /// ints; a decimal right operand that is a plain number is lifted with System.Decimal(...).</summary>
    private void EmitLong64OrDecimalCompoundValue(ExpressionSyntax left, ExpressionSyntax right, string op, ITypeSymbol? leftType)
    {
        var method = Long64OrDecimalCompoundMethod(op, leftType)!;
        EmitExpression(left);
        _w.Write($".{method}(");
        if (IsDecimalType(leftType) && !IsDecimalType(_model.GetTypeInfo(right).Type))
        {
            _w.Write("System.Decimal("); EmitExpression(right); _w.Write(")");
        }
        else
        {
            // Int64 add/sub/… and shifts both accept a plain-number right operand (the runtime
            // coerces it), matching EmitLong64Binary which emits the right operand raw.
            EmitExpression(right);
        }
        _w.Write(")");
    }

    /// <summary>
    /// Emits the NEW element value for a compound assignment to a collection indexer — the second
    /// argument of <c>coll.setItem(i, …)</c>. Mirrors the value computation of the plain-lvalue
    /// compound-assignment branches (delegate combine, string concat, narrow-integer clip, or a plain
    /// binary op), reading the current element via <c>coll.getItem(i)</c> (what <c>EmitExpression</c>
    /// of the indexer produces).
    /// </summary>
    private void EmitCompoundElementValue(AssignmentExpressionSyntax assignment, string op,
        ITypeSymbol? leftType, ITypeSymbol? rightType)
    {
        // Delegate / event combine-remove.
        if ((op == "+=" || op == "-=")
            && (leftType is { TypeKind: TypeKind.Delegate }
                || _model.GetSymbolInfo(assignment.Left).Symbol is IEventSymbol))
        {
            _w.Write($"TransposeR.{(op == "+=" ? "combine" : "remove")}(");
            EmitExpression(assignment.Left); _w.Write(", "); EmitExpression(assignment.Right); _w.Write(")");
            return;
        }

        // String concat.
        if (op == "+=" && IsStringType(leftType))
        {
            EmitConcatOperand(assignment.Left, leftType);
            _w.Write(" + ");
            EmitConcatOperand(assignment.Right, rightType);
            return;
        }

        // Narrow-integer arithmetic/bitwise: reuse the exact wrapping the plain-lvalue path applies.
        if (IsNarrowIntegerTarget(leftType)
            && op is "+=" or "-=" or "*=" or "/=" or "%=" or "&=" or "|=" or "^=" or "<<=" or ">>=")
        {
            var t = leftType!.SpecialType;
            var sub = IsSubWordIntegerTarget(leftType);
            var binOp = op[..^1];

            if (binOp == "*" && !sub)
            {
                _w.Write(t == SpecialType.System_UInt32 ? "Transpose.Int.umul(" : "Transpose.Int.mul(");
                EmitExpression(assignment.Left); _w.Write(", "); EmitExpression(assignment.Right); _w.Write(")");
                return;
            }

            var innerOp = binOp == ">>" && t == SpecialType.System_UInt32 ? ">>>" : binOp;
            string? clip = sub ? (binOp == "%" ? null : NarrowIntegerClip(t))
                : t == SpecialType.System_UInt32 ? (binOp is "%" or ">>" ? null : "Transpose.Int.clipu32")
                : (binOp is "+" or "-" or "/" ? "Transpose.Int.clip32" : null);

            if (clip is not null) { _w.Write(clip); _w.Write("("); }
            if (binOp == "/")
            {
                _w.Write("TransposeR.idiv("); EmitExpression(assignment.Left); _w.Write(", "); EmitExpression(assignment.Right); _w.Write(")");
            }
            else
            {
                _w.Write("("); EmitExpression(assignment.Left); _w.Write($") {innerOp} ("); EmitExpression(assignment.Right); _w.Write(")");
            }
            if (clip is not null) _w.Write(")");
            return;
        }

        // 64-bit integer / decimal element: `elem op= rhs` → `elem.<method>(rhs)` (see the
        // plain-lvalue path); a native JS operator would corrupt the boxed Int64/Decimal.
        if (op != "=" && (Is64BitInteger(leftType) || IsDecimalType(leftType))
            && Long64OrDecimalCompoundMethod(op, leftType) is not null)
        {
            EmitLong64OrDecimalCompoundValue(assignment.Left, assignment.Right, op, leftType);
            return;
        }

        // Plain compound (double / etc.): current-value binOp right.
        _w.Write("(");
        EmitExpression(assignment.Left);
        _w.Write($") {op[..^1]} (");
        EmitExpressionConverted(assignment.Right, leftType);
        _w.Write(")");
    }

    private void EmitPrefixUnary(PrefixUnaryExpressionSyntax prefix)
    {
        // `^n` (from-end index) as a value → a System.Index. Array element access handles `^n`
        // inline (arr.length - n) before reaching here; this covers every other position — an
        // Index-typed argument, an `Index i = ^1;` initializer, etc.
        if (prefix.IsKind(SyntaxKind.IndexExpression))
        {
            _w.Write("System.Index.FromEnd(");
            EmitExpression(prefix.Operand);
            _w.Write(")");
            return;
        }

        if (_model.GetSymbolInfo(prefix).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } opm
            && opm.Locations.Any(l => l.IsInSource)
            && !prefix.IsKind(SyntaxKind.PreIncrementExpression) && !prefix.IsKind(SyntaxKind.PreDecrementExpression))
        {
            _w.Write($"{TypeRef(opm.ContainingType)}.{TransposeNaming.MemberJsName(opm)}(");
            EmitExpression(prefix.Operand);
            _w.Write(")");
            return;
        }

        // 64-bit negation / bitwise-not → System.Int64/UInt64 methods.
        if (Is64BitInteger(_model.GetTypeInfo(prefix.Operand).Type))
        {
            if (prefix.IsKind(SyntaxKind.UnaryMinusExpression)) { EmitExpression(prefix.Operand); _w.Write(".neg()"); return; }
            if (prefix.IsKind(SyntaxKind.BitwiseNotExpression)) { EmitExpression(prefix.Operand); _w.Write(".not()"); return; }
            if (prefix.IsKind(SyntaxKind.UnaryPlusExpression)) { EmitExpression(prefix.Operand); return; }
        }

        // decimal negation → System.Decimal.neg().
        if (IsDecimalType(_model.GetTypeInfo(prefix.Operand).Type))
        {
            if (prefix.IsKind(SyntaxKind.UnaryMinusExpression)) { EmitExpression(prefix.Operand); _w.Write(".neg()"); return; }
            if (prefix.IsKind(SyntaxKind.UnaryPlusExpression)) { EmitExpression(prefix.Operand); return; }
        }

        // Managed 32-bit integer unary: `-int.MinValue` overflows back to int.MinValue, and `~uint`
        // must be reinterpreted unsigned (JS `~` yields a signed int32). Clip to match .NET.
        if (Integer32Clip(_model.GetTypeInfo(prefix).Type) is { } uClip
            && (prefix.IsKind(SyntaxKind.UnaryMinusExpression) || prefix.IsKind(SyntaxKind.BitwiseNotExpression))
            && !(prefix.IsKind(SyntaxKind.BitwiseNotExpression) && _model.GetTypeInfo(prefix).Type?.SpecialType == SpecialType.System_Int32))
        {
            _w.Write(uClip); _w.Write("(");
            _w.Write(prefix.OperatorToken.Text);
            EmitExpression(prefix.Operand);
            _w.Write(")");
            return;
        }

        // ++ / -- on a 64-bit integer or decimal: the operand is a boxed Int64/UInt64/Decimal, so a
        // native JS ++/-- would coerce it to a plain number (precision loss above 2^53, decimal
        // corruption). Rebuild through the type's method. Prefix yields the NEW value.
        if ((prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression))
            && IncDecStep(_model.GetTypeInfo(prefix.Operand).Type, prefix.IsKind(SyntaxKind.PreIncrementExpression)) is { } step)
        {
            _w.Write("(");
            EmitExpression(prefix.Operand);
            _w.Write(" = ");
            EmitExpression(prefix.Operand);
            _w.Write(step);
            _w.Write(")");
            return;
        }

        _w.Write(prefix.OperatorToken.Text);
        EmitExpression(prefix.Operand);
    }

    private void EmitPostfixUnary(PostfixUnaryExpressionSyntax postfix)
    {
        // ++ / -- on a 64-bit integer or decimal (see EmitPrefixUnary). Postfix must yield the OLD
        // value; in a void (statement / for-incrementor) context the result is discarded, so the
        // cheaper new-value form suffices.
        if ((postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression))
            && IncDecStep(_model.GetTypeInfo(postfix.Operand).Type, postfix.IsKind(SyntaxKind.PostIncrementExpression)) is { } step)
        {
            if (IsVoidContext(postfix))
            {
                _w.Write("(");
                EmitExpression(postfix.Operand);
                _w.Write(" = ");
                EmitExpression(postfix.Operand);
                _w.Write(step);
                _w.Write(")");
            }
            else
            {
                // Arrow so a `this`-qualified operand (e.g. `this.Count++` in expression position)
                // resolves to the enclosing instance rather than rebinding `this` to undefined.
                _w.Write("(($v) => { ");
                EmitExpression(postfix.Operand);
                _w.Write(" = $v");
                _w.Write(step);
                _w.Write("; return $v; })(");
                EmitExpression(postfix.Operand);
                _w.Write(")");
            }
            return;
        }

        EmitExpression(postfix.Operand);
        _w.Write(postfix.OperatorToken.Text);
    }

    /// <summary>The instance-method call suffix that increments/decrements a boxed 64-bit integer or
    /// decimal (e.g. <c>.add(System.Int64(1))</c>, <c>.inc()</c>), or null for other types.</summary>
    private static string? IncDecStep(ITypeSymbol? type, bool increment)
    {
        if (Is64BitInteger(type))
        {
            var one = Is64BitUnsigned(type) ? "System.UInt64(1)" : "System.Int64(1)";
            return increment ? $".add({one})" : $".sub({one})";
        }
        if (IsDecimalType(type)) return increment ? ".inc()" : ".dec()";
        return null;
    }

    /// <summary>True if an expression's value is discarded — it is the whole expression of an
    /// expression-statement, or an incrementor of a <c>for</c> loop.</summary>
    private static bool IsVoidContext(ExpressionSyntax e)
        => e.Parent is ExpressionStatementSyntax
           || (e.Parent is ForStatementSyntax f && f.Incrementors.Contains(e));

    // ---- cast --------------------------------------------------------------

    private void EmitCast(CastExpressionSyntax cast)
    {
        var targetType = _model.GetTypeInfo(cast.Type).Type;
        var sourceType = _model.GetTypeInfo(cast.Expression).Type;

        // A cast that resolves to a user-defined conversion operator (implicit or explicit) must
        // invoke it — e.g. `(int)myInt` → MyInt.op_Explicit(myInt). Erasing it leaks the source
        // value through, which then mismatches (and breaks a matching implicit conversion elsewhere).
        if (targetType is not null && sourceType is not null
            && !SymbolEqualityComparer.Default.Equals(sourceType, targetType)
            && _compilation.ClassifyConversion(sourceType, targetType)
                is { IsUserDefined: true, MethodSymbol: IMethodSymbol convMethod }
            && ShouldEmitUserConversion(convMethod))
        {
            EmitUserDefinedConversion(convMethod, cast.Expression);
            return;
        }

        EmitNumericConversion(targetType, sourceType, cast.Expression);
    }

    /// <summary>
    /// Emits an explicit numeric conversion honouring C#'s truncation / wrapping rules. Handles
    /// every primitive-to-primitive cast: to/from 64-bit (long/ulong) and decimal, narrowing to a
    /// ≤32-bit integer / char (with correct sign extension and bit-width wrapping), and reference
    /// casts (erased). The <paramref name="expr"/> is emitted at most once.
    /// </summary>
    private void EmitNumericConversion(ITypeSymbol? targetType, ITypeSymbol? sourceType, ExpressionSyntax expr)
    {
        // --- to a ≤32-bit integer / char target. ---
        // From an integer source (int/long/char/enum): truncate to the target's bit width, wrapping
        // (`(short)70000` → 4464, `(uint)-1` → 4294967295, `(sbyte)200` → -56, `(int)(a+b)` re-wraps
        // an overflow). From a float/double source: saturate to int32 first (CLR maps out-of-range
        // to Min/Max and NaN → 0, NOT wrap — `(byte)5e9` → 255, `(int)1e20` → int.MaxValue), then
        // mask to width. Runtime Transpose.Int.clip* do the final mask + sign-extend.
        if (IsNarrowIntegerTarget(targetType) && IsNumericSource(sourceType))
        {
            var t = targetType!.SpecialType;
            if (IsFloatingType(sourceType) && t == SpecialType.System_Int32)
            {
                _w.Write("TransposeR.fclip32("); EmitExpression(expr); _w.Write(")");
                return;
            }
            if (IsFloatingType(sourceType) && t == SpecialType.System_UInt32)
            {
                _w.Write("TransposeR.fclipu32("); EmitExpression(expr); _w.Write(")");
                return;
            }
            _w.Write(NarrowIntegerClip(t));
            _w.Write("(");
            if (Is64BitInteger(sourceType))
            {
                // Bring the 64-bit value down to its low 32-bit word (a plain JS number) first;
                // clip* then mask/sign-extend to the final width.
                _w.Write("System.Int64.clip32("); EmitExpression(expr); _w.Write(")");
            }
            else if (IsDecimalType(sourceType))
            {
                _w.Write("("); EmitExpression(expr); _w.Write(").toFloat()");
            }
            else if (IsFloatingType(sourceType))
            {
                // Sub-word float target: saturate to int32, then the outer clip masks to width.
                _w.Write("TransposeR.fclip32("); EmitExpression(expr); _w.Write(")");
            }
            else
            {
                EmitExpression(expr);
            }
            _w.Write(")");
            return;
        }

        // --- to 64-bit integer (long/ulong) ---
        // From a float/double: saturate to the 64-bit range (Min/Max, NaN → 0). From another
        // non-64-bit numeric: wrap. Between long/ulong of differing sign: reinterpret the 64 bits
        // (`(long)ulong`, `(ulong)long`) — the Int64/UInt64 ctor does value.toSigned()/toUnsigned().
        // Same 64-bit type is a no-op (erased below).
        if (Is64BitInteger(targetType) && IsFloatingType(sourceType))
        {
            _w.Write(Is64BitUnsigned(targetType) ? "TransposeR.fclipu64(" : "TransposeR.fclip64(");
            EmitExpression(expr);
            _w.Write(")");
            return;
        }
        // decimal → long/ulong: truncate toward zero (via the numeric magnitude) then wrap to 64-bit.
        if (Is64BitInteger(targetType) && IsDecimalType(sourceType))
        {
            _w.Write(Is64BitUnsigned(targetType) ? "TransposeR.fclipu64((" : "TransposeR.fclip64((");
            EmitExpression(expr);
            _w.Write(").toFloat())");
            return;
        }
        if (Is64BitInteger(targetType) && !IsDecimalType(sourceType)
            && !(Is64BitInteger(sourceType) && sourceType!.SpecialType == targetType!.SpecialType))
        {
            _w.Write(Is64BitUnsigned(targetType) ? "System.UInt64(" : "System.Int64(");
            EmitExpression(expr);
            _w.Write(")");
            return;
        }

        // --- from 64-bit to floating: read the numeric magnitude. ---
        if (Is64BitInteger(sourceType) && IsFloatingType(targetType))
        {
            _w.Write("("); EmitExpression(expr); _w.Write(").toNumber()");
            return;
        }

        // --- from 64-bit to an enum: enum ordinals are emitted as plain JS numbers even when the
        // enum's underlying type is long/ulong, so `(SomeLongEnum)someLong` must read the magnitude.
        // Leaving the Int64 instance in place made System.Enum.toString fail to match any member and
        // print the raw number instead of the member name. ---
        if (Is64BitInteger(sourceType) && targetType is { TypeKind: TypeKind.Enum })
        {
            _w.Write("("); EmitExpression(expr); _w.Write(").toNumber()");
            return;
        }

        // --- to decimal: wrap. ---
        if (IsDecimalType(targetType) && !IsDecimalType(sourceType))
        {
            _w.Write("System.Decimal("); EmitExpression(expr); _w.Write(")");
            return;
        }
        // --- from decimal to floating (integer targets handled above). ---
        if (IsDecimalType(sourceType) && IsFloatingType(targetType))
        {
            _w.Write("("); EmitExpression(expr); _w.Write(").toFloat()");
            return;
        }

        // Reference downcast / unboxing conversion → a checked cast (Transpose.cast throws
        // InvalidCastException) matching .NET (H5 IgnoreCast=false). `(Dog)someAnimal`, `(IFoo)obj`,
        // `(int)someObject` all verify the runtime type. Upcasts, identity, numeric/enum, boxing,
        // user-defined operators, dynamic, `null`, casts to a type parameter, and casts to an
        // external (native-JS) type stay erased — the same set H5's CastBlock skips.
        //
        // Array and delegate targets are also erased: a native JS array carries no element-type
        // metadata to verify (`(Emoji[])Enum.GetValues(...)` cannot be checked, and its runtime type
        // token is the un-parameterised System.Array), and a delegate is a plain JS function whose
        // generic type token (`ComponentEventHandler$2(T, MouseEvent)`) is not a constructible/callable
        // runtime type — emitting a checked cast for either throws where .NET/H5 would succeed.
        if (targetType is not null && sourceType is not null
            && targetType.TypeKind is not (TypeKind.TypeParameter or TypeKind.Dynamic
                                            or TypeKind.Array or TypeKind.Delegate)
            && targetType.SpecialType != SpecialType.System_Object
            && !targetType.IsTupleType
            && !IsUncheckableExternalCast(targetType)
            && !expr.IsKind(SyntaxKind.NullLiteralExpression))
        {
            var conv = _compilation.ClassifyConversion(sourceType, targetType);
            if ((conv.IsReference && conv.IsExplicit) || conv.IsUnboxing)
            {
                _w.Write("Transpose.cast(");
                EmitExpression(expr);
                _w.Write($", {TypeRef(targetType)})");
                return;
            }
        }

        // char <-> int (chars are their code point), int → 64-bit float, widening, and safe
        // reference conversions are representation-preserving and so erased.
        EmitExpression(expr);
    }

    /// <summary>A cast target that can't be runtime type-checked: an external (native-JS) type
    /// from a DOM / binding library. The base BCL (assembly "Transpose") marks primitives like
    /// System.String/Int32 [External] too, but those ARE checkable, so they are NOT excluded here —
    /// matching H5, which erases H5.Core casts yet checks System.* casts.</summary>
    private static bool IsUncheckableExternalCast(ITypeSymbol type)
        => TransposeNaming.IsExternalType(type) && type.ContainingAssembly?.Name != "Transpose";

    /// <summary>A ≤32-bit integer or char target — a narrowing/reinterpretation to a fixed
    /// bit width. Enums (SpecialType.None) are excluded: they carry their underlying value as-is.</summary>
    private static bool IsNarrowIntegerTarget(ITypeSymbol? type) => type?.SpecialType is
        SpecialType.System_SByte or SpecialType.System_Byte
        or SpecialType.System_Int16 or SpecialType.System_UInt16
        or SpecialType.System_Int32 or SpecialType.System_UInt32
        or SpecialType.System_Char;

    /// <summary>A numeric source that can feed an integer narrowing (int/long/float/decimal/char/enum).</summary>
    private static bool IsNumericSource(ITypeSymbol? type)
        => IsIntegerType(type) || IsFloatingType(type) || IsDecimalType(type) || IsCharType(type);

    /// <summary>A sub-32-bit integer / char type (byte/sbyte/short/ushort/char). Arithmetic on these
    /// is promoted to int, so a compound assignment (`b += x`) must re-narrow the result to width —
    /// and because both operands are &lt; 2^16 the promoted result is exact in JS, so a plain JS
    /// operation followed by a clip reproduces the CLR result exactly.</summary>
    private static bool IsSubWordIntegerTarget(ITypeSymbol? type) => type?.SpecialType is
        SpecialType.System_SByte or SpecialType.System_Byte
        or SpecialType.System_Int16 or SpecialType.System_UInt16
        or SpecialType.System_Char;

    /// <summary>The runtime clip helper that truncates + wraps a JS number to the given integer
    /// width (mask + sign-extend), matching the CLR's unchecked conversion.</summary>
    private static string NarrowIntegerClip(SpecialType target) => target switch
    {
        SpecialType.System_SByte => "Transpose.Int.clip8",
        SpecialType.System_Byte => "Transpose.Int.clipu8",
        SpecialType.System_Int16 => "Transpose.Int.clip16",
        SpecialType.System_UInt16 or SpecialType.System_Char => "Transpose.Int.clipu16",
        SpecialType.System_UInt32 => "Transpose.Int.clipu32",
        _ => "Transpose.Int.clip32", // int
    };

    // ---- interpolated string -----------------------------------------------

    /// <summary>
    /// True for a raw interpolated string (<c>$"""…"""</c>, any number of <c>$</c>). Raw strings have
    /// no brace-doubling escape — the <c>$</c> count decides how many consecutive braces open an
    /// interpolation, and any shorter brace run is literal text (doubling in a single-<c>$</c> raw
    /// string is CS9006/CS9007, not an escape). Classic and verbatim interpolated strings do use
    /// <c>{{</c>/<c>}}</c>, so the two families need opposite brace handling.
    /// </summary>
    private static bool IsRawInterpolatedString(InterpolatedStringExpressionSyntax interp)
        => interp.StringStartToken.Kind() is SyntaxKind.InterpolatedSingleLineRawStringStartToken
                                          or SyntaxKind.InterpolatedMultiLineRawStringStartToken;

    /// <summary>
    /// The literal text of an interpolated-string text segment. Roslyn's <c>ValueText</c> decodes
    /// standard escape sequences (<c>\n</c>, <c>\"</c>) but deliberately KEEPS composite-format brace
    /// escaping (<c>{{</c>/<c>}}</c>) for classic/verbatim interpolated strings, so those must be
    /// collapsed to a single brace here — the string target concatenates the text directly and never
    /// passes it through a composite-format parser that would unescape it. A raw string's ValueText
    /// already holds literal braces and is used verbatim.
    /// </summary>
    private static string InterpolatedTextValue(InterpolatedStringExpressionSyntax interp, InterpolatedStringTextSyntax text)
    {
        var value = text.TextToken.ValueText;
        if (IsRawInterpolatedString(interp) || value.IndexOfAny(Braces) < 0) return value;
        // Non-overlapping left-to-right, matching C#: "{{{{" is two literal braces, not one.
        return value.Replace("{{", "{").Replace("}}", "}");
    }

    /// <summary>
    /// The same text segment rendered for a <em>composite format string</em> (the
    /// <c>FormattableStringFactory.Create</c> first argument), where a literal brace is escaped by
    /// doubling. Classic/verbatim ValueText is already in that form; a raw string's literal braces
    /// must be doubled, or a raw <c>{0}</c> would be misread as a placeholder.
    /// </summary>
    private static string CompositeFormatTextValue(InterpolatedStringExpressionSyntax interp, InterpolatedStringTextSyntax text)
    {
        var value = text.TextToken.ValueText;
        if (!IsRawInterpolatedString(interp) || value.IndexOfAny(Braces) < 0) return value;
        return value.Replace("{", "{{").Replace("}", "}}");
    }

    private static readonly char[] Braces = ['{', '}'];

    private void EmitInterpolatedString(InterpolatedStringExpressionSyntax interp)
    {
        // An interpolated string CONVERTED to FormattableString / IFormattable is not a plain string:
        // the C# compiler lowers it to FormattableStringFactory.Create("{0}…{1}", args). Emit that so
        // the result carries Format / GetArguments() (consumers like a `t(FormattableString)` translation
        // helper call GetArguments()). Only the string target uses concatenation.
        var converted = _model.GetTypeInfo(interp).ConvertedType;
        if (converted is { ContainingNamespace: { } cns } && cns.ToDisplayString() == "System"
            && converted.Name is "FormattableString" or "IFormattable")
        {
            EmitFormattableString(interp);
            return;
        }

        _w.Write("(");
        var first = true;
        var hadContent = false;
        foreach (var content in interp.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    if (!first) _w.Write(" + ");
                    _w.Write(JsString(InterpolatedTextValue(interp, text)));
                    first = false; hadContent = true;
                    break;
                case InterpolationSyntax interpolation:
                    if (!first) _w.Write(" + ");
                    // Alignment component `{x,N}` pads the formatted value to width |N| (N>0 right-,
                    // N<0 left-aligned). Apply it to the already-stringified value so char/enum/bool
                    // still render correctly (routing the raw value through String.format would print
                    // a char's code point). The width is a compile-time constant.
                    var align = interpolation.AlignmentClause is { } ac
                        ? _model.GetConstantValue(ac.Value).Value : null;
                    if (align is not null) _w.Write("System.String.alignString(");
                    if (interpolation.FormatClause is not null)
                    {
                        _w.Write("TransposeR.formatValue(");
                        EmitExpression(interpolation.Expression);
                        _w.Write($", {JsString(interpolation.FormatClause.FormatStringToken.ValueText)})");
                    }
                    else if (IsCharType(_model.GetTypeInfo(interpolation.Expression).Type))
                    {
                        _w.Write("TransposeR.chr(");
                        EmitExpression(interpolation.Expression);
                        _w.Write(")");
                    }
                    else if (_model.GetTypeInfo(interpolation.Expression).Type is { TypeKind: TypeKind.Enum } enumT)
                    {
                        _w.Write($"System.Enum.toString({TypeRef(enumT)}, ");
                        EmitExpression(interpolation.Expression);
                        _w.Write(")");
                    }
                    else
                    {
                        _w.Write("TransposeR.toStr(");
                        EmitExpression(interpolation.Expression);
                        _w.Write(")");
                    }
                    if (align is not null) _w.Write($", {Convert.ToInt32(align).ToString(CultureInfo.InvariantCulture)})");
                    first = false; hadContent = true;
                    break;
            }
        }
        if (!hadContent) _w.Write("\"\"");
        _w.Write(")");
    }

    /// <summary>
    /// Emits an interpolated string that was converted to FormattableString / IFormattable as
    /// <c>FormattableStringFactory.Create("composite {0}…", [args])</c> — the composite format string
    /// with <c>{N[,align][:fmt]}</c> placeholders (literal braces doubled) plus the argument array,
    /// matching the C# compiler's lowering.
    /// </summary>
    private void EmitFormattableString(InterpolatedStringExpressionSyntax interp)
    {
        var format = new System.Text.StringBuilder();
        var args = new List<ExpressionSyntax>();
        foreach (var content in interp.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    format.Append(CompositeFormatTextValue(interp, text));
                    break;
                case InterpolationSyntax interpolation:
                    format.Append('{').Append(args.Count);
                    if (interpolation.AlignmentClause is { } align)
                        format.Append(',').Append(align.Value.ToString());
                    if (interpolation.FormatClause is { } fmt)
                        format.Append(':').Append(fmt.FormatStringToken.ValueText);
                    format.Append('}');
                    args.Add(interpolation.Expression);
                    break;
            }
        }
        _w.Write("System.Runtime.CompilerServices.FormattableStringFactory.Create(");
        _w.Write(JsString(format.ToString()));
        _w.Write(", [");
        for (var i = 0; i < args.Count; i++)
        {
            if (i > 0) _w.Write(", ");
            // Arguments are object[]: box value types so the array holds boxed values, matching .NET.
            EmitExpressionConverted(args[i], _compilation.GetSpecialType(SpecialType.System_Object));
        }
        _w.Write("])");
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

            // Indexer getter [Template] (e.g. some BCL indexers).
            if (indexer.GetMethod is { } getM && TransposeNaming.GetTemplate(getM.OriginalDefinition) is { } getTpl)
            {
                var recv = Capture(() => EmitExpression(element.Expression));
                var args = element.ArgumentList.Arguments.Select(a => Capture(() => EmitExpression(a.Expression))).ToList();
                WriteTemplate(getTpl, indexer.IsStatic, isExtension: false, recv, new(), args);
                return;
            }

            // An [External] type's plain indexer is native bracket access (e.g. domElement["name"]).
            if (TransposeNaming.IsNativeIndexer(indexer))
            {
                EmitExpression(element.Expression);
                _w.Write("[");
                EmitArgumentList(element.ArgumentList);
                _w.Write("]");
                return;
            }

            // Source types and BCL collections route through the indexer accessor.
            EmitExpression(element.Expression);
            _w.Write("." + TransposeNaming.IndexerAccessorName(indexer, isGet: true) + "(");
            EmitArgumentList(element.ArgumentList);
            _w.Write(")");
            return;
        }

        // Arrays with an Index (^n) or Range (a..b) argument.
        var arg = element.ArgumentList.Arguments.Count == 1 ? element.ArgumentList.Arguments[0].Expression : null;
        if (arg is RangeExpressionSyntax range)
        {
            var arr = Capture(() => EmitExpression(element.Expression));
            _w.Write($"{arr}.slice(");
            EmitIndexValue(range.LeftOperand, arr, isEnd: false);
            _w.Write(", ");
            EmitIndexValue(range.RightOperand, arr, isEnd: true);
            _w.Write(")");
            return;
        }
        if (arg is not null && arg.IsKind(SyntaxKind.IndexExpression))
        {
            var arr = Capture(() => EmitExpression(element.Expression));
            _w.Write($"{arr}[");
            EmitIndexValue(arg, arr, isEnd: false);
            _w.Write("]");
            return;
        }

        // Multi-dimensional array element read grid[i, j] → System.Array.get(grid, i, j).
        if (element.ArgumentList.Arguments.Count > 1
            && _model.GetTypeInfo(element.Expression).Type is IArrayTypeSymbol { Rank: > 1 })
        {
            _w.Write("System.Array.get(");
            EmitExpression(element.Expression);
            foreach (var a in element.ArgumentList.Arguments) { _w.Write(", "); EmitExpression(a.Expression); }
            _w.Write(")");
            return;
        }

        // Array indexed by a System.Index value (an `Index` variable, or a `^n` already lowered to
        // System.Index): arr[idx.GetOffset(arr.length)]. A `^n` literal is handled inline above.
        if (arg is not null && !arg.IsKind(SyntaxKind.IndexExpression)
            && _model.GetTypeInfo(arg).Type?.ToDisplayString() == "System.Index"
            && _model.GetTypeInfo(element.Expression).Type is IArrayTypeSymbol)
        {
            var arr = Capture(() => EmitExpression(element.Expression));
            var idx = Capture(() => EmitExpression(arg));
            _w.Write($"{arr}[{idx}.GetOffset({arr}.length)]");
            return;
        }

        // Arrays: bounds-checked element access (ArrayIndex = Managed). `arr[System.Array.index(i,
        // arr)]` throws IndexOutOfRangeException for an out-of-range index, matching .NET (plain JS
        // would read/write undefined). Used for both reads and — when this is an assignment target —
        // writes. Non-array element access (dynamic, etc.) stays a plain bracket access.
        if (_model.GetTypeInfo(element.Expression).Type is IArrayTypeSymbol)
        {
            var arr = Capture(() => EmitExpression(element.Expression));
            _w.Write($"{arr}[System.Array.index(");
            EmitArgumentList(element.ArgumentList);
            _w.Write($", {arr})]");
            return;
        }

        EmitExpression(element.Expression);
        _w.Write("[");
        EmitArgumentList(element.ArgumentList);
        _w.Write("]");
    }

    /// <summary>Emits a JS index/bound for C# Index/Range on an array (`^n` → len-n).</summary>
    private void EmitIndexValue(ExpressionSyntax? expr, string arrRef, bool isEnd)
    {
        if (expr is null) { _w.Write(isEnd ? $"{arrRef}.length" : "0"); return; }
        if (expr.IsKind(SyntaxKind.IndexExpression) && expr is PrefixUnaryExpressionSyntax fromEnd)
        {
            _w.Write($"{arrRef}.length - ");
            EmitExpression(fromEnd.Operand);
            return;
        }
        EmitExpression(expr);
    }

    // ---- arrays ------------------------------------------------------------

    private void EmitArrayCreation(ArrayCreationExpressionSyntax array)
    {
        var rankSpec = array.Type.RankSpecifiers.FirstOrDefault();
        // The array's OWN element type — from the semantic type, NOT array.Type.ElementType: for a
        // jagged `int[][]` the syntax element type is the innermost `int` (with two rank specifiers),
        // but the array being created is `int[]`-element, so tagging must use `int[]`. The semantic
        // IArrayTypeSymbol.ElementType gives the correct one for jagged and multidim alike.
        var elementType = ((_model.GetTypeInfo(array).Type ?? _model.GetTypeInfo(array).ConvertedType) as IArrayTypeSymbol)?.ElementType
            ?? _model.GetTypeInfo(array.Type.ElementType).Type;
        var rank = rankSpec?.Sizes.Count ?? 1;

        // Multi-dimensional array (int[,] …) → System.Array.create(default, initValues, T, dims…),
        // a flat backing array carrying its dimension sizes ($s); element access/assignment go
        // through System.Array.get/set.
        if (rank > 1)
        {
            EmitMultiDimArray(elementType!, rankSpec, array.Initializer);
            return;
        }

        if (array.Initializer is not null)
        {
            EmitTypedInitializerArray(array.Initializer, elementType);
            return;
        }

        // new T[n] → a JS array tagged with its element type (System.Array.init), so the value's
        // runtime type carries $elementType — matching h5 (System.Array.init(...)) and native
        // reflection (arr.GetType().GetElementType()), and letting the JSON serializer recognise a
        // byte[] for base64, an element-typed array for covariance, etc.
        if (rankSpec is { Sizes.Count: 1 } && rankSpec.Sizes[0] is not OmittedArraySizeExpressionSyntax)
        {
            _w.Write("System.Array.init(TransposeR.array(");
            EmitExpression(rankSpec.Sizes[0]);
            _w.Write($", {DefaultValueLiteral(elementType!)}), {TypeRef(elementType!)})");
            return;
        }

        _w.Write($"System.Array.init([], {TypeRef(elementType!)})");
    }

    /// <summary>Emits a single-dimensional array-creation initializer as a JS array tagged with its
    /// element type — <c>System.Array.init([…], element)</c> — so the resulting value's runtime type
    /// exposes <c>$elementType</c> (h5 tags every array literal the same way). The array returned is
    /// the same JS array, so indexing/length/spread are unaffected.</summary>
    private void EmitTypedInitializerArray(InitializerExpressionSyntax initializer, ITypeSymbol? elementType)
    {
        if (elementType is null)
        {
            EmitInitializerArray(initializer);
            return;
        }

        _w.Write("System.Array.init(");
        EmitInitializerArray(initializer);
        _w.Write($", {TypeRef(elementType)})");
    }

    /// <summary>Emits a multi-dimensional array via System.Array.create(defaultValue, initValues,
    /// elementType, dim0, dim1, …). Sizes come from explicit bounds or, for an initializer-only
    /// creation, the initializer's shape.</summary>
    private void EmitMultiDimArray(ITypeSymbol elementType, ArrayRankSpecifierSyntax? rankSpec, InitializerExpressionSyntax? initializer)
    {
        _w.Write("System.Array.create(");
        // A struct element needs a fresh instance per slot, so pass a factory; primitives pass a value.
        if (IsSourceStruct(elementType) && !IsJsPrimitiveValueType(elementType))
            _w.Write($"function () {{ return {DefaultValueLiteral(elementType)}; }}");
        else
            _w.Write(DefaultValueLiteral(elementType));
        _w.Write(", ");
        if (initializer is not null) EmitInitializerArray(initializer); else _w.Write("null");
        _w.Write($", {TypeRef(elementType)}");

        var explicitSizes = rankSpec is not null && rankSpec.Sizes.Count > 0
            && rankSpec.Sizes[0] is not OmittedArraySizeExpressionSyntax;
        if (explicitSizes)
        {
            foreach (var s in rankSpec!.Sizes) { _w.Write(", "); EmitExpression(s); }
        }
        else if (initializer is not null)
        {
            // Derive each dimension's length from the (rectangular) initializer's nesting.
            for (var dim = initializer; dim is not null; dim = dim.Expressions.FirstOrDefault() as InitializerExpressionSyntax)
                _w.Write($", {dim.Expressions.Count}");
        }
        _w.Write(")");
    }

    private void EmitInitializerArray(InitializerExpressionSyntax initializer)
    {
        _w.Write("[");
        for (var i = 0; i < initializer.Expressions.Count; i++)
        {
            if (i > 0) _w.Write(", ");
            // Nested initializer (a multi-dimensional array's rows) recurses to a nested JS array.
            if (initializer.Expressions[i] is InitializerExpressionSyntax nested)
                EmitInitializerArray(nested);
            else
                // Apply the element's converted type so an element widened to the array's element
                // type is boxed/cloned correctly (e.g. `object[] { 'A' }` boxes the char).
                EmitExpressionConverted(initializer.Expressions[i], _model.GetTypeInfo(initializer.Expressions[i]).ConvertedType);
        }
        _w.Write("]");
    }

    /// <summary>
    /// C# 12 collection expression `[a, b, ..spread]`. Arrays / spans map directly to a
    /// JS array literal (spreads become `...`); a List&lt;T&gt; (or other constructible
    /// collection) is built from that array via its enumerable constructor.
    /// </summary>
    private void EmitCollectionExpression(CollectionExpressionSyntax collection)
    {
        void EmitArray()
        {
            _w.Write("[");
            for (var i = 0; i < collection.Elements.Count; i++)
            {
                if (i > 0) _w.Write(", ");
                if (collection.Elements[i] is SpreadElementSyntax spread)
                {
                    // Arrays spread directly; other enumerables are drained to an array.
                    if (_model.GetTypeInfo(spread.Expression).Type is IArrayTypeSymbol)
                    {
                        _w.Write("...");
                        EmitExpression(spread.Expression);
                    }
                    else
                    {
                        _w.Write("...TransposeR.spread(");
                        EmitExpression(spread.Expression);
                        _w.Write(")");
                    }
                }
                else if (collection.Elements[i] is ExpressionElementSyntax elem)
                {
                    EmitExpression(elem.Expression);
                }
            }
            _w.Write("]");
        }

        EmitCollectionOf(_model.GetTypeInfo(collection).ConvertedType, EmitArray, [collection]);
    }

    /// <summary>
    /// Emits a value of collection type <paramref name="target"/> whose elements are produced by
    /// <paramref name="emitArrayLiteral"/> (a JS array literal). Arrays, spans, and collection
    /// interfaces (IEnumerable/IReadOnlyList/…) are the array itself — tps.js enumerates arrays
    /// natively; a concrete collection (e.g. List&lt;T&gt;) is built and filled via <c>add</c>
    /// (works regardless of its constructor overload numbering).
    /// </summary>
    /// <param name="awaitScopes">The syntax <paramref name="emitArrayLiteral"/> will emit, so an
    /// awaited element (<c>List&lt;int&gt; l = [await X()]</c>) gets an async IIFE.</param>
    private void EmitCollectionOf(ITypeSymbol? target, Action emitArrayLiteral,
                                  IReadOnlyList<SyntaxNode>? awaitScopes = null)
    {
        // A concrete array target tags the literal with its element type (System.Array.init) so the
        // value's runtime type carries $elementType — same as a `new T[]{…}` creation.
        if (target is IArrayTypeSymbol { Rank: 1 } arr)
        {
            _w.Write("System.Array.init(");
            emitArrayLiteral();
            _w.Write($", {TypeRef(arr.ElementType)})");
            return;
        }

        if (target is IArrayTypeSymbol
            || target is { TypeKind: TypeKind.Interface }
            || target?.OriginalDefinition.ToDisplayString() is "System.Span<T>" or "System.ReadOnlySpan<T>"
            || target is null)
        {
            emitArrayLiteral();
            return;
        }

        var hasAwait = OpenIife(awaitScopes is null ? [] : [.. awaitScopes]);
        _w.Write($"var $c = new ({TypeRef(target)})(); var $s = ");
        emitArrayLiteral();
        _w.Write("; for (var $i = 0; $i < $s.length; $i++) { $c.add($s[$i]); } return $c; ");
        CloseIife(hasAwait);
    }

    /// <summary>The element type of a params parameter (array element or the collection's T).</summary>
    private static ITypeSymbol? ParamsElementType(ITypeSymbol paramType)
        => paramType is IArrayTypeSymbol arr ? arr.ElementType
         : paramType is INamedTypeSymbol { TypeArguments.Length: 1 } n ? n.TypeArguments[0]
         : null;

    // ---- lambda ------------------------------------------------------------

    private void EmitLambda(IEnumerable<string> parameters, CSharpSyntaxNode body, bool isAsync,
        SeparatedSyntaxList<ParameterSyntax>? paramSyntax = null)
    {
        // Emit an arrow function so `this` is captured lexically, matching C# lambda semantics
        // (a plain `function` would rebind `this` and break `this`-referencing closures).
        // An async lambda returns an tps.js Task (via the TransposeR.fromPromise wrapper in
        // EmitMaybeAsyncBody), so it composes with Task.Run/WhenAll/ContinueWith; the outer
        // function is therefore not itself `async`.
        _w.Write("(");
        // Uniquify discard parameters ("_") so JS doesn't see duplicate names.
        var discardN = 0;
        _w.Write(string.Join(", ", parameters.Select(p =>
            p == "_" ? "$d" + discardN++ : NameMangler.JsIdentifier(p))));
        _w.Write(") => ");

        // Optional lambda parameters (C# 12): default when undefined.
        var defaults = paramSyntax?.Where(p => p.Default is not null).ToList();

        _w.Block(() =>
        {
            EmitLambdaParamDefaults(defaults);
            EmitMaybeAsyncBody(isAsync, () =>
            {
                if (body is BlockSyntax block)
                {
                    EmitStatements(block.Statements);
                }
                else if (body is ExpressionSyntax exprBody)
                {
                    // Hoist out-var / is-pattern variables the body introduces (e.g.
                    // `p => dict.TryGetValue(p, out var i) ? i : 0`) before returning it.
                    PredeclareInlineVars(exprBody);
                    // If the lambda's body has a value, return it.
                    _w.Write("return ");
                    EmitExpression(exprBody);
                    _w.WriteLine(";");
                }
            });
        });
    }

    private void EmitLambdaParamDefaults(System.Collections.Generic.List<ParameterSyntax>? defaults)
    {
        if (defaults is null) return;
        foreach (var p in defaults)
        {
            var name = NameMangler.JsIdentifier(p.Identifier.Text);
            _w.Write($"if ({name} === undefined) {{ {name} = ");
            EmitExpression(p.Default!.Value);
            _w.WriteLine("; }");
        }
    }

    // ---- constant / type helpers -------------------------------------------

    private string ConstantLiteral(object? value, ITypeSymbol type)
    {
        if (value is null) return type.SpecialType == SpecialType.System_String ? "null" : DefaultValueLiteral(type);
        // A string-backed enum ([Enum(Emit.StringName*)]) constant emits its string name, not the
        // numeric ordinal — so a defaulted enum parameter (e.g. `format = default`) seeds the
        // string the runtime actually compares against.
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } en && TransposeNaming.EnumEmitMode(en) is 3 or 4 or 5 or 6)
            return EnumConstantLiteral(en, value);
        // 64-bit integer constants (e.g. long.MinValue) must be System.Int64/UInt64 instances.
        if (value is long or ulong && Is64BitInteger(type))
            return Long64Literal(value, Is64BitUnsigned(type));
        // decimal constants (e.g. decimal.MaxValue) must be System.Decimal instances.
        if (value is decimal && IsDecimalType(type))
            return $"System.Decimal(\"{((decimal)value).ToString(CultureInfo.InvariantCulture)}\")";
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

    /// <summary>The string literal for a string-backed enum constant with the given numeric value.</summary>
    private string EnumConstantLiteral(INamedTypeSymbol enumType, object value)
    {
        var mode = TransposeNaming.EnumEmitMode(enumType);
        var v = EnumOrdinalText(value);
        var field = enumType.GetMembers().OfType<IFieldSymbol>()
            .FirstOrDefault(f => f.HasConstantValue && EnumOrdinalText(f.ConstantValue) == v);
        return field is not null ? JsString(TransposeNaming.EnumStringName(field, mode)) : "null";
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

    /// <summary>64-bit integer type (long/ulong) — tps.js models these as System.Int64/UInt64 objects.</summary>
    private static bool Is64BitInteger(ITypeSymbol? type)
        => type?.SpecialType is SpecialType.System_Int64 or SpecialType.System_UInt64;

    private static bool Is64BitUnsigned(ITypeSymbol? type)
        => type?.SpecialType == SpecialType.System_UInt64;
}
