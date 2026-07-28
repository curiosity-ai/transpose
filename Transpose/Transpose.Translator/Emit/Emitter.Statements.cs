using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

public sealed partial class Emitter
{
    // Break-target stack: null = plain `break` (loops / native switch);
    // a label string = `break <label>` (pattern-switch labelled block).
    private readonly Stack<string?> _breakTargets = new();

    // Inline-var names already `let`-declared in the current JS block, so a name reused by
    // sibling statements (e.g. two `if (… out var t …)` — invalid C# the runtime tolerates, or
    // repeated is-pattern names) is declared once, not redeclared (which `let` rejects).
    private readonly Stack<HashSet<string>> _predeclaredInScope = new();

    private void EmitStatement(StatementSyntax statement)
    {
        // C# out-var declarations and is-pattern variables have block scope; declare
        // them before the statement that introduces them.
        PredeclareInlineVars(statement);

        switch (statement)
        {
            case BlockSyntax block:
                EmitBlock(block);
                break;
            case LocalDeclarationStatementSyntax local:
                EmitLocalDeclaration(local);
                break;
            case ExpressionStatementSyntax expr:
                if (IsElidedNoOpCall(expr.Expression))
                {
                    // A call whose [Template] is a comment-only no-op (e.g. Contract.Ensures /
                    // Contract.Result, [Template("0 /*{condition}*/")]) is elided entirely — matching
                    // the reference runtime, and avoiding illegally nested block comments.
                }
                else if (IsRemovedConditionalCall(expr.Expression))
                {
                    // A call to a [Conditional("SYM")] method whose symbol is not defined is removed
                    // entirely (its arguments are not evaluated), matching C# semantics.
                }
                else if (expr.Expression is AssignmentExpressionSyntax da && IsDeconstruction(da))
                {
                    EmitDeconstruction(da);
                }
                else
                {
                    EmitExpressionStatement(expr.Expression);
                }
                break;
            case IfStatementSyntax ifStmt:
                EmitIf(ifStmt);
                break;
            case ForStatementSyntax forStmt:
                EmitFor(forStmt);
                break;
            case ForEachStatementSyntax forEach:
                EmitForEach(forEach);
                break;
            case ForEachVariableStatementSyntax forEachVar:
                EmitForEachVariable(forEachVar);
                break;
            case WhileStatementSyntax whileStmt:
                EmitWhile(whileStmt);
                break;
            case DoStatementSyntax doStmt:
                EmitDo(doStmt);
                break;
            case ReturnStatementSyntax ret:
                EmitReturn(ret);
                break;
            case BreakStatementSyntax:
                if (_breakTargets.Count > 0 && _breakTargets.Peek() is { } lbl)
                    _w.WriteLine($"break {lbl};");
                else
                    _w.WriteLine("break;");
                break;
            case ContinueStatementSyntax:
                _w.WriteLine("continue;");
                break;
            case ThrowStatementSyntax throwStmt:
                EmitThrow(throwStmt);
                break;
            case TryStatementSyntax tryStmt:
                EmitTry(tryStmt);
                break;
            case UsingStatementSyntax usingStmt:
                EmitUsing(usingStmt);
                break;
            case SwitchStatementSyntax switchStmt:
                EmitSwitch(switchStmt);
                break;
            case LocalFunctionStatementSyntax localFn:
                EmitLocalFunction(localFn);
                break;
            case LockStatementSyntax lockStmt:
                // Single-threaded: the lock is a no-op, emit the body.
                EmitStatement(lockStmt.Statement);
                break;
            case CheckedStatementSyntax checkedStmt:
                EmitBlock(checkedStmt.Block);
                break;
            case YieldStatementSyntax yieldStmt:
                if (yieldStmt.IsKind(SyntaxKind.YieldReturnStatement))
                {
                    _w.Write("yield ");
                    if (yieldStmt.Expression is not null) EmitExpression(yieldStmt.Expression);
                    _w.WriteLine(";");
                }
                else
                {
                    _w.WriteLine("return;"); // yield break
                }
                break;
            case GotoStatementSyntax gotoStmt:
                EmitGoto(gotoStmt);
                break;
            case LabeledStatementSyntax labeled:
                // Reached outside a state machine (label with no goto to it): emit its body.
                EmitStatement(labeled.Statement);
                break;
            case EmptyStatementSyntax:
                break;
            default:
                Unsupported(statement, statement.Kind().ToString());
                break;
        }
    }

    /// <summary>
    /// Declares block-scoped variables introduced inline by a statement's — or an
    /// expression-bodied member's / lambda's — own expressions (out-var declarations, is-pattern
    /// variables) at the top of the current JS block. Does not descend into nested
    /// statements/blocks or lambdas — those manage their own.
    /// </summary>
    private void PredeclareInlineVars(SyntaxNode scope)
    {
        // A local declaration's own initializer may contain out-vars, but its declared
        // variables are emitted normally; only collect designation-introduced names.
        var names = new List<string>();
        CollectInlineDesignations(scope, isRoot: true, names);

        // Match EmitLocalDeclaration's keyword rule: `let` inside a loop (fresh per-iteration binding
        // for closures), `var` otherwise. Using `var` outside loops is what lets a predeclared
        // out-var coexist with a same-named regular local elsewhere in the function (both flattened
        // to function scope) — a `let` predeclaration would collide with such a `var` local
        // ("Identifier 'x' has already been declared").
        var blockScope = _predeclaredInScope.Count > 0 ? _predeclaredInScope.Peek() : null;
        foreach (var name in names.Distinct())
        {
            var jsName = NameMangler.JsIdentifier(name);
            if (blockScope is not null && !blockScope.Add(jsName)) continue; // already declared in this block
            _w.WriteLine($"{LocalDeclKeyword(scope, jsName)} {jsName};");
        }
    }

    private void CollectInlineDesignations(SyntaxNode node, bool isRoot, List<string> names)
    {
        foreach (var child in node.ChildNodes())
        {
            // An `else if` chain: the nested `if` is emitted inline (`else if (...)`) and its
            // condition's out-var / pattern designations are scoped to the SAME enclosing block in
            // C#, so they must be predeclared here too. Descend into the nested if's condition and
            // its own else-clause, but not its then-body (a separate block scope). Without this, a
            // designation in an `else if` condition captured later (e.g. by a lambda in the branch)
            // was never declared — "<name> is not defined".
            if (child is IfStatementSyntax nestedIf && child.Parent is ElseClauseSyntax)
            {
                CollectInlineDesignations(nestedIf.Condition, isRoot: false, names);
                if (nestedIf.Else is not null) CollectInlineDesignations(nestedIf.Else, isRoot: false, names);
                continue;
            }

            // Do not cross scope boundaries.
            if (!isRoot && child is StatementSyntax) continue;
            if (child is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax) continue;

            if (child is SingleVariableDesignationSyntax single)
            {
                var parent = single.Parent;
                var include = parent is DeclarationPatternSyntax or RecursivePatternSyntax or VarPatternSyntax
                    // out-var declarations (not tuple-deconstruction, which declares its own).
                    || (parent is DeclarationExpressionSyntax { Parent: ArgumentSyntax arg }
                        && (arg.RefKindKeyword.IsKind(SyntaxKind.OutKeyword) || arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword)));
                if (include) names.Add(single.Identifier.Text);
            }

            CollectInlineDesignations(child, isRoot: false, names);
        }
    }

    private void EmitBlock(BlockSyntax block)
    {
        _w.Block(() => EmitStatements(block.Statements));
        _w.WriteLine();
    }

    /// <summary>
    /// Emits a statement sequence, desugaring `using var x = e;` declarations into a
    /// try/finally whose body is the remainder of the sequence (C# block-scoped dispose).
    /// </summary>
    internal void EmitStatements(IReadOnlyList<StatementSyntax> statements, int start = 0)
    {
        // A body containing labels (goto targets) is lowered into a state machine so that
        // `goto` can jump between top-level sections (works for backward loops and forward
        // skips, and — since async bodies are native `async` IIFEs — across `await`).
        if (start == 0 && statements.Any(s => s is LabeledStatementSyntax))
        {
            EmitGotoStateMachine(statements);
            return;
        }

        // C# local functions are hoisted (callable before their textual position), so emit
        // them first (as arrow closures) at the top of the block.
        if (start == 0)
            foreach (var fn in statements.OfType<LocalFunctionStatementSyntax>())
                EmitLocalFunction(fn);

        // Track inline-var names declared in this block (a `start > 0` continuation is the same
        // logical block re-entered — reuse the existing scope so names aren't redeclared).
        var pushedScope = start == 0;
        if (pushedScope) _predeclaredInScope.Push(new HashSet<string>());
        try
        {
        for (var i = start; i < statements.Count; i++)
        {
            var s = statements[i];
            if (s is LocalFunctionStatementSyntax) continue; // already hoisted above
            if (s is LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not 0 } u)
            {
                var resources = new List<string>();
                foreach (var v in u.Declaration.Variables)
                {
                    var name = NameMangler.JsIdentifier(v.Identifier.Text);
                    resources.Add(name);
                    _w.Write($"let {name} = ");
                    if (v.Initializer is not null) EmitExpression(v.Initializer.Value); else _w.Write("null");
                    _w.WriteLine(";");
                }
                _w.Write("try ");
                _w.Block(() => EmitStatements(statements, i + 1));
                _w.WriteLine();
                _w.Write("finally ");
                _w.Block(() => { foreach (var r in Enumerable.Reverse(resources)) _w.WriteLine($"TransposeR.dispose({r});"); });
                _w.WriteLine();
                return; // the rest of the sequence was emitted inside the try
            }
            EmitStatement(s);
        }
        }
        finally { if (pushedScope) _predeclaredInScope.Pop(); }
    }

    /// <summary>
    /// Lowers a statement body containing labels into a `for(;;) switch($state)` machine.
    /// Top-level statements are split into sections at each label; sequential flow is the
    /// switch's fall-through, and `goto L` sets $state and re-enters the loop. Locals are
    /// declared with `var` (function-scoped) so their values survive across loop iterations.
    /// </summary>
    private void EmitGotoStateMachine(IReadOnlyList<StatementSyntax> statements)
    {
        var sections = new List<(string? label, List<StatementSyntax> stmts)> { (null, new List<StatementSyntax>()) };
        var labelIndex = new Dictionary<string, int>();
        foreach (var s in statements)
        {
            if (s is LabeledStatementSyntax lbl)
            {
                labelIndex[lbl.Identifier.Text] = sections.Count;
                sections.Add((lbl.Identifier.Text, new List<StatementSyntax> { lbl.Statement }));
            }
            else
            {
                sections[^1].stmts.Add(s);
            }
        }

        var depth = _gotoContexts.Count;
        var loopLabel = depth == 0 ? "$goto" : "$goto" + depth;
        var stateVar = depth == 0 ? "$state" : "$state" + depth;

        _w.WriteLine($"var {stateVar} = 0;");
        _w.Write($"{loopLabel}: for (;;) ");
        _gotoContexts.Push((labelIndex, loopLabel, stateVar));
        _w.Block(() =>
        {
            _w.Write($"switch ({stateVar}) ");
            _w.Block(() =>
            {
                for (var i = 0; i < sections.Count; i++)
                {
                    _w.WriteLine($"case {i}:");
                    _w.Indent();
                    foreach (var st in sections[i].stmts) EmitStatement(st);
                    _w.Outdent();
                }
            });
            _w.WriteLine($"break {loopLabel};");
        });
        _gotoContexts.Pop();
        _w.WriteLine();
    }

    private void EmitGoto(GotoStatementSyntax gotoStmt)
    {
        if (gotoStmt.IsKind(SyntaxKind.GotoStatement) && gotoStmt.Expression is IdentifierNameSyntax id
            && _gotoContexts.Count > 0 && _gotoContexts.Peek().labels.TryGetValue(id.Identifier.Text, out var idx))
        {
            var ctx = _gotoContexts.Peek();
            _w.WriteLine($"{ctx.stateVar} = {idx}; continue {ctx.loopLabel};");
            return;
        }
        Unsupported(gotoStmt, "goto");
    }

    // Per-function cache of identifiers a Script.Write(...) raw-JS template declares with var/let/const
    // in the same JS function scope (see ScriptDeclaredVarNames).
    private readonly Dictionary<SyntaxNode, HashSet<string>> _scriptVarNamesCache = new();
    private static readonly HashSet<string> _noScriptVars = new();

    /// <summary>
    /// The keyword to declare a C# local <paramref name="jsName"/> at <paramref name="at"/>: normally
    /// <c>let</c> (block scope, so a loop local captured by a closure binds per-iteration), but
    /// <c>var</c> when a goto state machine needs the local to persist across <c>case</c> transitions,
    /// OR when a <c>Script.Write(...)</c> raw-JS block in the same function scope declares the same
    /// name and we are not inside a loop. Raw JS often redeclares a local it computes into (e.g.
    /// Tesserae's <c>Color.FromString</c>: C# <c>int r,g,b</c> plus a <c>Script.Write("… var r … var g
    /// … var b …")</c>); legacy h5 emitted the locals as <c>var</c> so the two merged, but a <c>let</c>
    /// local beside a <c>var</c> of the same name in one scope is a hard "Identifier already declared"
    /// error. Loops keep <c>let</c> — the closure-capture semantics outweigh this rare name clash.
    /// </summary>
    private string LocalDeclKeyword(SyntaxNode at, string jsName)
    {
        if (_gotoContexts.Count > 0) return "var";
        if (_loopDepth == 0 && ScriptDeclaredVarNames(at).Contains(jsName)) return "var";
        return "let";
    }

    /// <summary>
    /// Identifiers declared with <c>var</c>/<c>let</c>/<c>const</c> inside a <c>Script.Write(...)</c>
    /// template in the nearest enclosing function scope of <paramref name="node"/> (not descending into
    /// deeper lambdas / local functions). Cached per function-scope node.
    /// </summary>
    private HashSet<string> ScriptDeclaredVarNames(SyntaxNode node)
    {
        var fn = EnclosingFunctionScope(node);
        if (fn is null) return _noScriptVars;
        if (_scriptVarNamesCache.TryGetValue(fn, out var cached)) return cached;

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var inv in fn.DescendantNodes(n => n == fn || !IsFunctionScope(n)).OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "Write" } ma) continue;
            var recv = ma.Expression.ToString();
            if (recv != "Script" && !recv.EndsWith(".Script", StringComparison.Ordinal)) continue;
            if (inv.ArgumentList.Arguments.Count < 1) continue;
            if (_model.GetConstantValue(inv.ArgumentList.Arguments[0].Expression).Value is not string raw) continue;
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(raw, @"\b(?:var|let|const)\s+([A-Za-z_$][A-Za-z0-9_$]*)"))
                set.Add(m.Groups[1].Value);
        }
        _scriptVarNamesCache[fn] = set;
        return set;
    }

    private static bool IsFunctionScope(SyntaxNode n)
        => n is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax
              or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax;

    private static SyntaxNode? EnclosingFunctionScope(SyntaxNode node)
    {
        for (var n = node; n is not null; n = n.Parent)
            if (IsFunctionScope(n)) return n;
        return null;
    }

    private void EmitLocalDeclaration(LocalDeclarationStatementSyntax local)
    {
        if (local.UsingKeyword != default)
        {
            EmitUsingDeclaration(local);
            return;
        }

        // A local declared inside a loop body is emitted with `let` so each iteration gets a fresh
        // binding — a closure created in the loop then captures that iteration's value (C# block
        // scoping), not the final one. Outside a loop, `var` (function scope) is kept: it matches
        // Transpose's model and tolerates the same-name redeclarations across flattened scopes that some
        // code relies on (which `let` would reject). A goto state machine also needs `var` so a
        // local persists across `case` transitions as the loop re-enters the switch.
        foreach (var variable in local.Declaration.Variables)
        {
            var jsName = NameMangler.JsIdentifier(variable.Identifier.Text);
            var kw = LocalDeclKeyword(local, jsName);
            _w.Write($"{kw} {jsName}");
            if (variable.Initializer is not null)
            {
                _w.Write(" = ");
                EmitExpressionConverted(variable.Initializer.Value, _model.GetTypeInfo(variable.Initializer.Value).ConvertedType);
            }
            else if (UninitializedLocalStructType(variable) is { } structType)
            {
                _w.Write(" = ");
                _w.Write(DefaultValueLiteral(structType));
            }
            _w.WriteLine(";");
        }
    }

    /// <summary>
    /// The struct type of an initializer-less local that must still be emitted as a zeroed struct
    /// instance, or null when a bare declaration is fine. C# lets `SomeStruct s;` be definitely
    /// assigned field by field (`s.a = 1; s.b = 2;`) — the BCL's own
    /// <c>HashSet&lt;T&gt;.CheckUniqueAndUnfoundElements</c> does exactly that — so emitting a bare
    /// `let s;` leaves `s` undefined and the first field write throws "Cannot set properties of
    /// undefined". Primitives, enums and reference types are excluded: definite assignment means
    /// they are always written before they are read, so the extra initializer would be dead weight.
    /// Nullable&lt;T&gt; is excluded for the same reason (its default is a bare `null`), and a ref
    /// struct is left alone — it has its own emit path and never reaches JS as a plain object.
    /// </summary>
    private ITypeSymbol? UninitializedLocalStructType(VariableDeclaratorSyntax variable)
    {
        if (_model.GetDeclaredSymbol(variable) is not ILocalSymbol { Type: { } type }) return null;
        if (type.TypeKind != TypeKind.Struct || type.IsRefLikeType) return null;
        if (IsPrimitiveNumericOrBool(type)) return null;
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) return null;
        return type;
    }

    private void EmitIf(IfStatementSyntax ifStmt)
    {
        _w.Write("if (");
        EmitExpression(ifStmt.Condition);
        _w.Write(") ");
        EmitStatementAsBlock(ifStmt.Statement);

        if (ifStmt.Else is not null)
        {
            _w.Write("else ");
            if (ifStmt.Else.Statement is IfStatementSyntax elseIf)
            {
                EmitIf(elseIf);
            }
            else
            {
                EmitStatementAsBlock(ifStmt.Else.Statement);
            }
        }
    }

    /// <summary>Emits a statement, wrapping single statements into a block for safety.</summary>
    private void EmitStatementAsBlock(StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
        {
            EmitBlock(block);
        }
        else
        {
            _w.Block(() => EmitStatement(statement));
            _w.WriteLine();
        }
    }

    private void EmitFor(ForStatementSyntax forStmt)
    {
        _w.Write("for (");
        if (forStmt.Declaration is not null)
        {
            // `var`, not `let`: a C# for-loop variable is a SINGLE binding shared across iterations,
            // so a closure capturing it sees the final value. ES6 `let` in a for-header creates a
            // fresh per-iteration binding (closures would capture distinct values) — wrong for C#.
            _w.Write("var ");
            var first = true;
            foreach (var v in forStmt.Declaration.Variables)
            {
                if (!first) _w.Write(", ");
                first = false;
                _w.Write(NameMangler.JsIdentifier(v.Identifier.Text));
                if (v.Initializer is not null) { _w.Write(" = "); EmitExpression(v.Initializer.Value); }
            }
        }
        else
        {
            var first = true;
            foreach (var init in forStmt.Initializers)
            {
                if (!first) _w.Write(", ");
                first = false;
                EmitExpression(init);
            }
        }
        _w.Write("; ");
        if (forStmt.Condition is not null) EmitExpression(forStmt.Condition);
        _w.Write("; ");
        var firstInc = true;
        foreach (var inc in forStmt.Incrementors)
        {
            if (!firstInc) _w.Write(", ");
            firstInc = false;
            EmitExpression(inc);
        }
        _w.Write(") ");
        _breakTargets.Push(null);
        _loopDepth++;
        EmitStatementAsBlock(forStmt.Statement);
        _loopDepth--;
        _breakTargets.Pop();
    }

    private void EmitForEach(ForEachStatementSyntax forEach)
    {
        var iterVar = NameMangler.JsIdentifier(forEach.Identifier.Text);
        var enumVar = EmitEnumeratorInit(forEach, forEach.Expression);
        // foreach disposes the enumerator when the loop ends — including on break/return/throw. Wrap
        // the iteration in try/finally so an iterator's own `finally` (and any IDisposable cleanup)
        // runs on early exit, not only on full enumeration. TransposeR.dispose no-ops if not disposable.
        _w.Write("try ");
        _w.Block(() =>
        {
            _w.Write($"while ({enumVar}.moveNext()) ");
            _breakTargets.Push(null);
            _loopDepth++;
            _w.Block(() =>
            {
                _w.WriteLine($"let {iterVar} = {enumVar}.current;");
                EmitForEachBody(forEach.Statement);
            });
            _loopDepth--;
            _breakTargets.Pop();
            _w.WriteLine();
        });
        _w.Write("finally ");
        _w.Block(() => _w.WriteLine($"TransposeR.dispose({enumVar});"));
        _w.WriteLine();
    }

    /// <summary>foreach with deconstruction: foreach (var (a, b) in seq) — bind each element.</summary>
    private void EmitForEachVariable(ForEachVariableStatementSyntax forEach)
    {
        var enumVar = EmitEnumeratorInit(forEach, forEach.Expression);
        var elementType = _model.GetForEachStatementInfo(forEach).ElementType;
        var elementIsTuple = elementType is { IsTupleType: true };
        var targets = CollectDeconstructionTargets(forEach.Variable).ToList();
        _w.Write("try ");
        _w.Block(() =>
        {
            _w.Write($"while ({enumVar}.moveNext()) ");
            _breakTargets.Push(null);
            _loopDepth++;
            _w.Block(() =>
            {
                var cur = enumVar + "c";
                _w.WriteLine($"let {cur} = {enumVar}.current;");
                EmitDeconstructionBindings(targets, cur, elementIsTuple, elementType);
                EmitForEachBody(forEach.Statement);
            });
            _loopDepth--;
            _breakTargets.Pop();
            _w.WriteLine();
        });
        _w.Write("finally ");
        _w.Block(() => _w.WriteLine($"TransposeR.dispose({enumVar});"));
        _w.WriteLine();
    }

    /// <summary>
    /// Emits <c>var $e = TransposeR.getEnumerator(source)</c>, routing through an extension
    /// GetEnumerator when the foreach binds to one, and returns the enumerator variable name.
    /// </summary>
    private string EmitEnumeratorInit(CommonForEachStatementSyntax forEach, ExpressionSyntax source)
    {
        // Name the enumerator from the statement's source position, not from SyntaxNode.GetHashCode():
        // a node's hash code is reference-based, so it varies between runs and made the emitted bundle
        // differ byte-for-byte on every compile of unchanged sources. The span start is stable and is
        // unique within a file, which is all the uniqueness this needs — the variable is local to one
        // JS function, and two foreach statements in the same function always start at different
        // offsets (nested ones included).
        var enumVar = "$e" + forEach.SpanStart.ToString("x4");
        var getEnum = _model.GetForEachStatementInfo(forEach).GetEnumeratorMethod;
        var ext = getEnum is { IsExtensionMethod: true } ? (getEnum.ReducedFrom ?? getEnum) : null;
        _w.Write($"var {enumVar} = TransposeR.getEnumerator(");
        if (ext is not null && ext.Locations.Any(l => l.IsInSource))
        {
            _w.Write($"{TypeRef(ext.ContainingType)}.{TransposeNaming.MemberJsName(ext)}(");
            EmitExpression(source);
            _w.Write(")");
        }
        else
        {
            EmitExpression(source);
        }
        _w.WriteLine(");");
        return enumVar;
    }

    private void EmitForEachBody(StatementSyntax body)
    {
        // The body statements are emitted inline into the foreach's own JS block (opened by the
        // caller's _w.Block, which already wrote `let <iter> = ...`). Route through EmitStatements
        // so the block gets its OWN inline-var predeclare scope — otherwise inline out-vars/
        // is-pattern names (`let x;`) declared here would be tracked against the enclosing block and
        // suppressed as "already declared" in a sibling foreach body, leaving the second use of the
        // name undeclared (ReferenceError). Mirrors how EmitBlock delegates to EmitStatements.
        if (body is BlockSyntax block)
        {
            EmitStatements(block.Statements);
        }
        else
        {
            _predeclaredInScope.Push(new HashSet<string>());
            try { EmitStatement(body); }
            finally { _predeclaredInScope.Pop(); }
        }
    }

    private void EmitWhile(WhileStatementSyntax whileStmt)
    {
        _w.Write("while (");
        EmitExpression(whileStmt.Condition);
        _w.Write(") ");
        _breakTargets.Push(null);
        _loopDepth++;
        EmitStatementAsBlock(whileStmt.Statement);
        _loopDepth--;
        _breakTargets.Pop();
    }

    private void EmitDo(DoStatementSyntax doStmt)
    {
        _w.Write("do ");
        _breakTargets.Push(null);
        _loopDepth++;
        EmitStatementAsBlock(doStmt.Statement);
        _loopDepth--;
        _breakTargets.Pop();
        _w.Write("while (");
        EmitExpression(doStmt.Condition);
        _w.WriteLine(");");
    }

    private void EmitReturn(ReturnStatementSyntax ret)
    {
        if (ret.Expression is null)
        {
            _w.WriteLine("return;");
            return;
        }
        _w.Write("return ");
        var method = _model.GetEnclosingSymbol(ret) as IMethodSymbol;
        EmitExpressionConverted(ret.Expression, _model.GetTypeInfo(ret.Expression).ConvertedType);
        _w.WriteLine(";");
    }

    private void EmitThrow(ThrowStatementSyntax throwStmt)
    {
        if (throwStmt.Expression is null)
        {
            _w.WriteLine("throw $ex;"); // rethrow inside catch
            return;
        }
        _w.Write("throw ");
        EmitExpression(throwStmt.Expression);
        _w.WriteLine(";");
    }

    private void EmitTry(TryStatementSyntax tryStmt)
    {
        _w.Write("try ");
        EmitBlock(tryStmt.Block);

        if (tryStmt.Catches.Count > 0)
        {
            _w.WriteLine("catch ($ex) {");
            _w.Indent();

            // Bind each catch variable to $ex up front so it is in scope for exception
            // filters (`when (...)`), which are evaluated in the guard before the body.
            var boundNames = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            foreach (var katch in tryStmt.Catches)
            {
                var id = katch.Declaration?.Identifier;
                if (id is { RawKind: not 0 } token && !string.IsNullOrEmpty(token.Text))
                {
                    var jsName = NameMangler.JsIdentifier(token.Text);
                    if (boundNames.Add(jsName)) _w.WriteLine($"let {jsName} = $ex;");
                }
            }

            var first = true;
            var hasCatchAll = false;
            foreach (var katch in tryStmt.Catches)
            {
                var typeSyntax = katch.Declaration?.Type;
                var exType = typeSyntax is not null ? _model.GetTypeInfo(typeSyntax).Type : null;
                var isCatchAll = exType is null || exType.SpecialType == SpecialType.System_Object
                    || exType.ToDisplayString() == "System.Exception";

                var condition = isCatchAll ? null : $"TransposeR.is($ex, {ExceptionTypeRef(exType!)})";
                if (katch.Filter is not null)
                {
                    // exception filter appended
                }

                if (condition is null && katch.Filter is null)
                {
                    hasCatchAll = true;
                    if (first)
                    {
                        // Only/first clause with no type filter: body runs unconditionally.
                        EmitCatchBody(katch, null);
                    }
                    else
                    {
                        _w.Write("else ");
                        EmitCatchBodyBlock(katch);
                    }
                }
                else
                {
                    // A filter-only catch (catch (Exception) when (...)) still needs a guard.
                    condition ??= "true";
                    _w.Write(first ? "if (" : "else if (");
                    _w.Write(condition);
                    if (katch.Filter is not null)
                    {
                        _w.Write(" && (");
                        EmitExpression(katch.Filter.FilterExpression);
                        _w.Write(")");
                    }
                    _w.Write(") ");
                    EmitCatchBodyBlock(katch);
                }
                first = false;
            }
            if (!hasCatchAll)
            {
                _w.WriteLine("else { throw $ex; }");
            }
            _w.Outdent();
            _w.WriteLine("}");
        }

        if (tryStmt.Finally is not null)
        {
            _w.Write("finally ");
            EmitBlock(tryStmt.Finally.Block);
        }
    }

    private void EmitCatchBody(CatchClauseSyntax katch, string? prefix)
    {
        // The catch variable is bound once at the top of the catch block (see EmitTry).
        foreach (var s in katch.Block.Statements) EmitStatement(s);
    }

    private void EmitCatchBodyBlock(CatchClauseSyntax katch)
    {
        _w.Block(() =>
        {
            foreach (var s in katch.Block.Statements) EmitStatement(s);
        });
        _w.WriteLine();
    }

    private string ExceptionTypeRef(ITypeSymbol type) => TypeRef(type);

    private void EmitUsing(UsingStatementSyntax usingStmt)
    {
        // using (var x = expr) body  =>  { let x = expr; try { body } finally { TransposeR.dispose(x); } }
        _w.Block(() =>
        {
            string? resourceVar = null;
            if (usingStmt.Declaration is not null)
            {
                foreach (var v in usingStmt.Declaration.Variables)
                {
                    resourceVar = NameMangler.JsIdentifier(v.Identifier.Text);
                    _w.Write($"let {resourceVar} = ");
                    if (v.Initializer is not null) EmitExpression(v.Initializer.Value); else _w.Write("null");
                    _w.WriteLine(";");
                }
            }
            else if (usingStmt.Expression is not null)
            {
                // Name the resource from the statement's source position, not from
                // SyntaxNode.GetHashCode() — a node's hash code is reference-based, so it varies
                // between runs and made the emitted bundle differ byte-for-byte on every compile of
                // unchanged sources. Same reasoning (and format) as the enumerator in
                // EmitEnumeratorInit.
                resourceVar = "$using" + usingStmt.SpanStart.ToString("x4");
                _w.Write($"let {resourceVar} = ");
                EmitExpression(usingStmt.Expression);
                _w.WriteLine(";");
            }

            _w.Write("try ");
            EmitStatementAsBlock(usingStmt.Statement);
            _w.Write("finally ");
            _w.Block(() => _w.WriteLine($"TransposeR.dispose({resourceVar});"));
            _w.WriteLine();
        });
        _w.WriteLine();
    }

    private void EmitUsingDeclaration(LocalDeclarationStatementSyntax local)
    {
        // using var x = expr;  — dispose at end of enclosing block. Simplified: declare now.
        foreach (var variable in local.Declaration.Variables)
        {
            _w.Write($"let {NameMangler.JsIdentifier(variable.Identifier.Text)}");
            if (variable.Initializer is not null) { _w.Write(" = "); EmitExpression(variable.Initializer.Value); }
            _w.WriteLine(";");
        }
        // Note: deterministic dispose for using-declarations is a later phase.
    }

    private void EmitSwitch(SwitchStatementSyntax switchStmt)
    {
        // Pattern-based switch → if/else-if chain over a temp subject.
        var hasPatterns = switchStmt.Sections
            .SelectMany(s => s.Labels)
            .Any(l => l is CasePatternSwitchLabelSyntax);

        // A long/ulong/decimal subject is a runtime object, so a JS `switch` — which matches with
        // `===`, i.e. object identity — never hit any `case` and always fell through to `default`.
        // The if/else chain compares by value.
        var governingType = _model.GetTypeInfo(switchStmt.Expression).Type;

        if (hasPatterns || IsRuntimeObjectNumeric(governingType))
        {
            EmitPatternSwitch(switchStmt);
            return;
        }

        _w.Write("switch (");
        EmitExpression(switchStmt.Expression);
        _w.WriteLine(") {");
        _w.Indent();
        _breakTargets.Push(null);
        foreach (var section in switchStmt.Sections)
        {
            foreach (var label in section.Labels)
            {
                switch (label)
                {
                    case CaseSwitchLabelSyntax caseLabel:
                        _w.Write("case ");
                        EmitExpression(caseLabel.Value);
                        _w.WriteLine(":");
                        break;
                    case DefaultSwitchLabelSyntax:
                        _w.WriteLine("default:");
                        break;
                    default:
                        Unsupported(label, "pattern switch label");
                        break;
                }
            }
            _w.Indent();
            foreach (var stmt in section.Statements) EmitStatement(stmt);
            _w.Outdent();
        }
        _breakTargets.Pop();
        _w.Outdent();
        _w.WriteLine("}");
    }

    private void EmitPatternSwitch(SwitchStatementSyntax switchStmt)
    {
        var label = NextTemp("$switch");
        var subject = NextTemp("$subj");

        _w.WriteLine($"{label}: {{");
        _w.Indent();
        _w.Write($"let {subject} = ");
        EmitExpression(switchStmt.Expression);
        _w.WriteLine(";");

        _breakTargets.Push(label);

        SwitchSectionSyntax? defaultSection = null;
        var first = true;

        foreach (var section in switchStmt.Sections)
        {
            if (section.Labels.Any(l => l is DefaultSwitchLabelSyntax))
            {
                defaultSection = section;
                continue;
            }

            _w.Write(first ? "if (" : "else if (");
            first = false;

            for (var i = 0; i < section.Labels.Count; i++)
            {
                if (i > 0) _w.Write(" || ");
                _w.Write("(");
                switch (section.Labels[i])
                {
                    case CasePatternSwitchLabelSyntax patternLabel:
                        EmitPatternTest(subject, patternLabel.Pattern);
                        if (patternLabel.WhenClause is not null)
                        {
                            _w.Write(" && (");
                            EmitExpression(patternLabel.WhenClause.Condition);
                            _w.Write(")");
                        }
                        break;
                    case CaseSwitchLabelSyntax constLabel:
                        EmitConstantEqualityTest(subject, constLabel.Value);
                        break;
                }
                _w.Write(")");
            }

            _w.Write(") ");
            _w.Block(() => { foreach (var stmt in section.Statements) EmitStatement(stmt); });
            _w.WriteLine();
        }

        if (defaultSection is not null)
        {
            _w.Write(first ? "" : "else ");
            _w.Block(() => { foreach (var stmt in defaultSection.Statements) EmitStatement(stmt); });
            _w.WriteLine();
        }

        _breakTargets.Pop();
        _w.Outdent();
        _w.WriteLine("}");
    }

    private void EmitLocalFunction(LocalFunctionStatementSyntax localFn)
    {
        var symbol = _model.GetDeclaredSymbol(localFn) as IMethodSymbol;
        var isAsync = localFn.Modifiers.Any(SyntaxKind.AsyncKeyword);
        var isIterator = localFn.Body is not null && IsIteratorBody(localFn.Body);
        // Arrow function so `this` is captured lexically (C# local functions close over `this`);
        // the `var` binding keeps the name in scope for recursion.
        _w.Write($"var {NameMangler.JsIdentifier(localFn.Identifier.Text)} = (");
        if (symbol is not null) EmitParameterList(symbol);
        _w.Write(") => ");
        _w.Block(() =>
        {
            if (symbol is not null) EmitOptionalDefaults(symbol);
            if (isIterator)
            {
                // An iterator local function compiles to a generator, exactly like an iterator method:
                // a `function*` (can't be an arrow, so it rebinds `this` — bind it to the captured
                // enclosing instance) wrapped by TransposeR.iter. Emitting the yields straight into the
                // arrow would be invalid — a bare `yield` outside a generator is a strict-mode syntax
                // error.
                _w.Write("return TransposeR.iter((function* () ");
                _w.Block(() => { foreach (var s in localFn.Body!.Statements) EmitStatement(s); });
                _w.WriteLine(").bind(this));");
            }
            else
            {
                EmitMaybeAsyncBody(isAsync, () =>
                {
                    if (localFn.Body is not null)
                    {
                        EmitStatements(localFn.Body.Statements);
                    }
                    else if (localFn.ExpressionBody is not null)
                    {
                        // Hoist out-var / is-pattern variables the expression introduces (e.g.
                        // `string F() => dict.TryGetValue(k, out var v) ? v : null`) so their
                        // write-backs and later reads resolve — an expression body has no statement
                        // to predeclare them otherwise (matches EmitMethodBody / EmitAccessorBody).
                        PredeclareInlineVars(localFn.ExpressionBody.Expression);
                        if (symbol?.ReturnsVoid == true) EmitExpressionStatement(localFn.ExpressionBody.Expression);
                        else { _w.Write("return "); EmitExpression(localFn.ExpressionBody.Expression); _w.WriteLine(";"); }
                    }
                });
            }
        });
        _w.WriteLine(";");
    }
}
