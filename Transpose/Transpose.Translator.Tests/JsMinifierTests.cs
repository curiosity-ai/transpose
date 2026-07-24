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
}
