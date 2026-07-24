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
