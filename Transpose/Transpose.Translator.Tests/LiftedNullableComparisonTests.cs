using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// A lifted <c>Nullable&lt;T&gt;</c> operator used to null-test BOTH operands, because C# converts
/// both sides to <c>Nullable&lt;T&gt;</c> and the emitter read the converted type. For the very common
/// <c>x?.Count &gt; 0</c> that meant emitting <c>0 == null</c> - dead at runtime, and NUglify folds a
/// loose <c>&lt;falsy literal&gt; == null</c> to TRUE (it coerces <c>null</c> to 0 before comparing).
/// The guard became <c>x == null || true</c>, so the comparison answered false for every x, in
/// minified builds only.
///
/// Two things are guarded here: the emitter no longer writes a null test for an operand that cannot
/// be null, and the minified output is executed - the formatted output was always correct, which is
/// exactly why the Node suite never saw this.
/// </summary>
[TestClass]
public class LiftedNullableComparisonTests : TranslatorTestBase
{
    private static string Js(string source) => new RoslynTranslator().Translate(source).Javascript ?? "";

    // ---- emission -----------------------------------------------------------

    [TestMethod]
    public void AConstantOperandIsNotNullTested()
    {
        var js = Js(@"
using System.Collections.Generic;
public class P
{
    public static bool M(List<int> items) { return items?.Count > 0; }
}");

        Assert.IsFalse(js.Contains("|| 0 == null"),
            $"a literal can never be null, so the lifted operator must not test it:\n{js}");
        StringAssert.Contains(js, "== null ? false :",
            $"the nullable operand still has to be null-tested:\n{js}");
    }

    [TestMethod]
    public void BothOperandsAreStillTestedWhenBothCanBeNull()
    {
        var js = Js("public class P { public static bool M(int? a, int? b) { return a > b; } }");

        StringAssert.Contains(js, "== null || ",
            $"two nullable operands both need their null test:\n{js}");
    }

    [TestMethod]
    public void ANonNullableOperandIsNotNullTested()
    {
        // Only the right-hand side is nullable here; the left is a plain int local.
        var js = Js("public class P { public static bool M(int a, int? b) { return a > b; } }");

        Assert.IsFalse(js.Contains("== null || "),
            $"only one operand can be null, so only one null test belongs:\n{js}");
    }

    // ---- semantics, formatted AND minified ----------------------------------

    private const string LiftedComparisons = @"
using System;
using System.Collections.Generic;
public class Program
{
    static bool HasAny(List<int> items) { return items?.Count > 0; }
    public static void Main()
    {
        Console.WriteLine(HasAny(null));
        Console.WriteLine(HasAny(new List<int>()));
        Console.WriteLine(HasAny(new List<int> { 1 }));

        int? n = null;      Console.WriteLine(n > 0);
        int? z = 0;         Console.WriteLine(z > 0);
        int? p = 5;         Console.WriteLine(p > 0);
        Console.WriteLine(p >= 5);
        Console.WriteLine(n < 0);
        Console.WriteLine(n <= 0);

        int? a = null, b = 3;
        Console.WriteLine(a > b);
        Console.WriteLine(b > a);
        Console.WriteLine((a + b) is null);
        Console.WriteLine(b + 1);
        Console.WriteLine(n + 1 is null);

        double? d = 0.0;    Console.WriteLine(d > 0);
        long? l = 0L;       Console.WriteLine(l > 0);
        Console.WriteLine(""<<DONE>>"");
    }
}";

    [TestMethod]
    public async Task LiftedComparisonsMatchDotNet()
    {
        await RunTest(LiftedComparisons, waitForOutput: "<<DONE>>");
    }

    [TestMethod]
    public async Task LiftedComparisonsMatchDotNetAfterMinification()
    {
        // The bug this file is named for existed only after minification. Run the same program
        // through JsMinifier and compare against the same native output.
        var result = new RoslynTranslator().Translate(
            new[] { ("App.cs", LiftedComparisons) },
            CompilationBuilder.DefaultAssemblyName,
            extraReferencePaths: null,
            preprocessorSymbols: new[] { "DEBUG", "TRACE" });

        Assert.IsTrue(result.Success,
            "translation failed:\n" + string.Join("\n", result.Errors.Select(d => d.GetMessage())));

        var full     = RoslynTranslator.LoadRuntime() + "\n" + result.Javascript!;
        var minified = JsMinifier.Minify(full, "app.js");

        Assert.IsFalse(minified.Contains("||!0?"),
            "the minifier folded a `<falsy literal> == null` to true - the guard is now dead");

        var expected = RoslynNativeRunner.CompileAndRun(LiftedComparisons);
        var actual   = await NodeJsRunner.RunAsync(minified);

        Assert.AreEqual(Lines(expected), Lines(actual),
            "minified output disagrees with .NET while the formatted output agrees");
    }

    private static string Lines(string output) => string.Join("\n", output.Trim()
        .Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None)
        .Select(s => s.TrimEnd()));
}
