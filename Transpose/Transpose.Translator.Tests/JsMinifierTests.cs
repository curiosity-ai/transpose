using System.Threading.Tasks;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Guards the JS minifier (<see cref="JsMinifier"/>) against re-introducing invalid output for the
/// ES2020 nullish-coalescing operator. `??` may not be combined with `&&`/`||` without parentheses,
/// so a `??` operand of a logical operator MUST keep its grouping through minification. NUglify
/// 1.20.7 stripped those parens and emitted a hard syntax error (e.g. `a&&b&&c??d`); the pinned
/// NUglify (1.21.14+) preserves them. These tests fail if the dependency is ever rolled back to a
/// version with the bug, or the settings change in a way that reopens it.
/// </summary>
[TestClass]
public sealed class JsMinifierTests
{
    // The `??` group must survive in every position it can appear as a logical operand.
    [DataTestMethod]
    [DataRow("function f(){ return a && b && (c ?? d); }", "&&(c??d)")]
    [DataRow("function g(){ return a || (c ?? d); }",       "||(c??d)")]
    [DataRow("function h(){ return (c ?? d) && a; }",       "(c??d)&&")]
    public void CoalesceOperandOfLogicalKeepsItsParentheses(string source, string mustContain)
    {
        var min = JsMinifier.Minify(source, "app.js");

        StringAssert.Contains(min, mustContain,
            $"minifier dropped the required parentheses around a `??` operand: {min}");
        // The unparenthesised mix is the exact syntax error we are guarding against.
        Assert.IsFalse(min.Contains("&&c??") || min.Contains("||c??") || min.Contains("??d&&"),
            $"minifier produced an invalid `??`/`&&`/`||` mix: {min}");
    }

    // NUglify evaluates a loose `<literal> == null` by coercing `null` to 0, so every FALSY literal
    // folds to the wrong answer (`0 == null` -> true, `0 != null` -> false, same for `''` and
    // `false`). We emitted `0 == null` for a lifted `x?.Count > 0` and the fold turned the guard into
    // `x == null || true`. EvaluateNumericExpressions is killed for this; these rows fail if it comes
    // back. `1 == null` and the strict `0 === null` were always folded correctly.
    //
    // The minified expression is RUN rather than pattern-matched: what matters is that it still means
    // what JavaScript says it means, whether the minifier folded it or left it alone.
    [DataTestMethod]
    [DataRow("0 == null",     "false")]
    [DataRow("'' == null",    "false")]
    [DataRow("false == null", "false")]
    [DataRow("null == 0",     "false")]
    [DataRow("0 != null",     "true")]
    [DataRow("'' != null",    "true")]
    [DataRow("false != null", "true")]
    [DataRow("null != 0",     "true")]
    [DataRow("1 == null",     "false")]
    [DataRow("0 === null",    "false")]
    [DataRow("0 !== null",    "true")]
    public async Task LooseNullComparisonAgainstAFalsyLiteralIsNotMisfolded(string expression, string expected)
    {
        var min = JsMinifier.Minify($"function f(){{ return ({expression}); }} console.log(f());", "app.js");

        var actual = (await NodeJsRunner.RunAsync(min)).Trim();

        Assert.AreEqual(expected, actual,
            $"`{expression}` is {expected} in JavaScript, but minified to: {min}");
    }

    [TestMethod]
    public void BracedIfElseBodyUnwrapsWithoutStrayEmptyStatement()
    {
        // NUglify 1.22.0 regressed here: unwrapping the braces of an if/else whose body is a single
        // (braced) loop inserted a stray empty statement, e.g. `if (c) { for(...){...} } else …`
        // minified to `if(c)for(...)…;;else …` — the `;;` orphans the `else` (a syntax error). This
        // is the exact shape of TransposeR.array in tps.shim.js. 1.21.15 keeps it valid.
        var source =
            "var R={};R.f=function(n,d){var a=[];" +
            "if(typeof d==='function'){for(var i=0;i<n;i++){a[i]=d();}}" +
            "else if(d&&typeof d==='object'){for(var i=0;i<n;i++){a[i]=R.c(d);}}" +
            "else{for(var i=0;i<n;i++){a[i]=d;}}return a;};";

        var min = JsMinifier.Minify(source, "shim.js");

        Assert.IsFalse(min.Contains(";;else") || min.Contains(";;}"),
            $"minifier inserted a stray empty statement that orphans `else`: {min}");
    }

    [TestMethod]
    public void NullConditionalCoalesceInLogicalChainStaysValid()
    {
        // The exact shape emitted for `a != null && b >= 0 && (fn?.Invoke() ?? false)` — the `?.` is
        // lowered to a helper and the `?? false` is the right operand of `&&`. This reproduced the
        // reported `Uncaught SyntaxError: Unexpected token '??'` in tss.min.js.
        var source =
            "function f(){ return a != null && b >= 0 && (((($nc0) => $nc0 == null ? null : $nc0())(fn) ?? false)); }";

        var min = JsMinifier.Minify(source, "app.js");

        // The `?? !1` (false) must sit inside a parenthesised group next to the `&&`, never bare.
        StringAssert.Contains(min, ")??!1)",
            $"the coalesce group lost its parentheses under minification: {min}");
        Assert.IsFalse(min.Contains(")??!1}") || min.Contains(")??!1;"),
            $"minifier produced a bare `??` operand of `&&`: {min}");
    }

    // ---------------------------------------------------------------------------------------------
    // Block scoping: a `let` must never be moved into a narrower scope than it was emitted in.
    //
    // NUglify's InvertIfReturn / InvertIfContinue rewrite a guard clause by moving every following
    // statement into a NEW block (`if (c) return; rest…` -> `if (!c) { rest… }`). A `let` among
    // `rest…` then becomes block-scoped to that new block, and a closure emitted BEFORE the guard —
    // which is exactly where a hoisted C# local function lands — loses the binding. The failure is
    // `ReferenceError: <name> is not defined`, only ever in a minified bundle.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The reported Tesserae case, reduced to the JavaScript the emitter actually produces for
    /// <c>Router.Navigate</c>: the hoisted local function <c>ExecuteTheNavigation</c> is a
    /// <c>var</c> arrow at the top of the method, and the <c>let</c> it captures is declared after a
    /// guard clause. Minification must not separate the two.
    /// </summary>
    [TestMethod]
    public void HoistedLocalFunctionKeepsAccessToALetDeclaredAfterAGuardClause()
    {
        var source = """
            function Navigate(path, reload) {
                var ExecuteTheNavigation = () => {
                    if (!windowLocationSaysAlreadyThere) { go(path); return; }
                    locationChanged(reload);
                };
                if (onWillNavigate != null) {
                    if (!onWillNavigate(path)) { return; }
                }
                let windowLocationSaysAlreadyThere = alreadyThere(path);
                if (reload) { ExecuteTheNavigation(); return; }
                if (windowLocationSaysAlreadyThere) { return; }
                ExecuteTheNavigation();
            }
            """;

        var min = JsMinifier.Minify(source, "app.js");

        AssertLetIsNotNestedDeeperThanTheClosureCapturingIt(min, "windowLocationSaysAlreadyThere");
    }

    /// <summary>
    /// The same defect via <c>InvertIfContinue</c>: the guard is a `continue` in a loop body, and the
    /// captured local must stay `let` (a loop body needs a fresh per-iteration binding, so it cannot
    /// simply be emitted as `var`).
    /// </summary>
    [TestMethod]
    public void HoistedLocalFunctionKeepsAccessToALetDeclaredAfterAContinueGuard()
    {
        var source = """
            function Loop(items) {
                for (var i = 0; i < items.length; i++) {
                    var H = () => { use(doubled); };
                    if (i === 1) { continue; }
                    let doubled = i * 2;
                    H();
                }
            }
            """;

        var min = JsMinifier.Minify(source, "app.js");

        AssertLetIsNotNestedDeeperThanTheClosureCapturingIt(min, "doubled");
    }

    /// <summary>
    /// Asserts that the minified output does not open a brace between the closure that reads
    /// <paramref name="name"/> and the <c>let</c> that declares it — i.e. the declaration was not
    /// pushed into a block the closure sits outside of. Brace-depth comparison rather than a literal
    /// match, so the test keeps working as other (sound) transforms reshape the output.
    /// </summary>
    private static void AssertLetIsNotNestedDeeperThanTheClosureCapturingIt(string min, string name)
    {
        var declaration = min.IndexOf("let " + name, StringComparison.Ordinal);
        Assert.IsTrue(declaration >= 0, $"expected a `let {name}` declaration to survive: {min}");

        // The closure's own body is one brace level deeper than where it is declared, so measure the
        // depth at the arrow's `=>` (the declaration site of the closure) rather than inside it.
        var closure = min.IndexOf("=>", StringComparison.Ordinal);
        Assert.IsTrue(closure >= 0 && closure < declaration,
            $"expected the capturing closure to be emitted before the declaration: {min}");

        Assert.IsTrue(BraceDepth(min, declaration) <= BraceDepth(min, closure),
            $"minifier moved `let {name}` into a block nested deeper than the closure that captures " +
            $"it — the closure will throw `ReferenceError: {name} is not defined`: {min}");
    }

    private static int BraceDepth(string s, int index)
    {
        var depth = 0;
        for (var i = 0; i < index; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}') depth--;
        }
        return depth;
    }


    /// <summary>
    /// A module entry and its chunks are minified like everything else, and keep their own names while
    /// being so. This was believed impossible for a long time — the emitter's own notes said the module
    /// output was "formatted only" because it carries `import` syntax — and it simply is not: NUglify
    /// parses ES module syntax and preserves the specifiers verbatim, which is all that is needed,
    /// since the runtime fetches a chunk by the path written in the import.
    /// </summary>
    [TestMethod]
    public void ModuleSyntaxSurvivesMinification()
    {
        const string source = @"import './chunks/c0.mjs';
import './chunks/lib/c9.mjs';

Transpose.define(""App.Widget"", { statics: { methods: {
    Go: function (x) {
        let doubled = x + x;
        return doubled;
    }
} } });
";
        var min = JsMinifier.Minify(source, "c2.mjs");

        StringAssert.Contains(min, "./chunks/c0.mjs", "the specifier is the path the runtime fetches — it must survive verbatim");
        StringAssert.Contains(min, "./chunks/lib/c9.mjs");
        StringAssert.Contains(min, "\"App.Widget\"", "the define name is the chunk map's key");
        Assert.IsTrue(min.Length < source.Length, $"it should actually shrink (got {min.Length} from {source.Length})");
        Assert.IsFalse(min.Contains("\n\n"), "and be minified rather than merely reprinted");
    }
}
