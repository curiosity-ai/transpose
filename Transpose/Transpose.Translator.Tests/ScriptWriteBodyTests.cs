using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// A <c>Script.Write</c> that is the whole body of an expression-bodied lambda or member.
    ///
    /// <para>
    /// An expression-bodied lambda was always emitted as <c>return &lt;body&gt;;</c>, even when it
    /// converts to a void delegate, so a template of several statements came out as
    /// <c>(viz) =&gt; { return var gkViz = viz; gkViz.layouts[name](…); }</c>: everything after the
    /// template's first statement unreachable, and nothing thrown (curiosity-ai/graph-kit#53). A void
    /// lambda's body is now a statement, as an expression-bodied void member's already was.
    /// </para>
    ///
    /// <para>
    /// Where the value IS used, the template stands where one expression belongs, as it does in every
    /// other value position (<c>let x = …</c>, an operand, an argument), so a template of several
    /// statements there is rejected with TransposeR0006 rather than silently cut short. A trailing
    /// <c>;</c> is not a second statement.
    /// </para>
    ///
    /// <para>
    /// <c>Script.Write</c> has no native implementation, so these run on Node only
    /// (<c>skipRoslyn</c>) and assert the output and the emitted shape.
    /// </para>
    /// </summary>
    [TestClass]
    public class ScriptWriteBodyTests : TranslatorTestBase
    {
        private const string MultiStatementError = "template is several JavaScript statements, but its value is used";

        private static string Program(string members, string main) => @"
using System;
using Transpose;
public class Program
{
" + members + @"
    public static void Main()
    {
" + main + @"
        Console.WriteLine(""<<DONE>>"");
    }
}";

        private static string Translate(string code)
        {
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed:\n" + string.Join("\n", result.Errors));
            return result.Javascript!;
        }

        // ---- void: the template is emitted as statements -----------------------

        /// <summary>The graph-kit shape: an <c>Action&lt;object&gt;</c> lambda whose template declares
        /// a local, calls through it and chains a promise.</summary>
        [TestMethod]
        public async Task ActionLambdaRunsEveryStatementOfTheTemplate()
        {
            var code = Program(@"
    static void Deferred(Action<object> call) { call(new object()); }",
@"
        string name = ""grid"";
        Deferred(viz => Script.Write(
            ""var gk = {0}; console.log('first', typeof gk); console.log('second', {1})"", viz, name));");

            var output = await RunTest(code, skipRoslyn: true);
            StringAssert.Contains(output, "first object\nsecond grid\n<<DONE>>", output);

            var js = Translate(code);
            Assert.IsFalse(js.Contains("return var gk"), "a void lambda must not return its template\n" + js);
        }

        [TestMethod]
        public async Task ActionLambdaWithTemplateEndingInSemicolonRunsEveryStatement()
        {
            var output = await RunTest(Program("",
@"
        Action<int> act = n => Script.Write(""console.log('a', {0}); console.log('b');"", n);
        act(3);"), skipRoslyn: true);
            StringAssert.Contains(output, "a 3\nb\n<<DONE>>", output);
        }

        [TestMethod]
        public async Task ParameterlessActionLambdaRunsEveryStatement()
        {
            var output = await RunTest(Program("",
@"
        Action a = () => Script.Write(""console.log('p1'); console.log('p2')"");
        a();"), skipRoslyn: true);
            StringAssert.Contains(output, "p1\np2\n<<DONE>>", output);
        }

        /// <summary>A value-returning <c>Script.Write&lt;T&gt;</c> under a void delegate: the value is
        /// discarded, so a template of several statements is fine and runs whole.</summary>
        [TestMethod]
        public async Task GenericScriptWriteUnderAnActionRunsEveryStatement()
        {
            var output = await RunTest(Program("",
@"
        Action a = () => Script.Write<int>(""console.log('g1'); console.log('g2'); 3"");
        a();"), skipRoslyn: true);
            StringAssert.Contains(output, "g1\ng2\n<<DONE>>", output);
        }

        [TestMethod]
        public async Task AsyncVoidLambdaRunsEveryStatement()
        {
            var output = await RunTest(Program("",
@"
        Action a = async () => Script.Write(""console.log('async1'); console.log('async2')"");
        a();"), skipRoslyn: true);
            StringAssert.Contains(output, "async1\nasync2", output);
        }

        [TestMethod]
        public async Task ExpressionBodiedVoidMembersAndLocalFunctionsRunEveryStatement()
        {
            var output = await RunTest(Program(@"
    static void M() => Script.Write(""console.log('m1'); console.log('m2')"");
    static void Trailing() => Script.Write(""console.log('t1'); console.log('t2');"");
    static string _v;
    static string V { get => _v; set => Script.Write(""console.log('s1'); console.log('s2', {0})"", value); }",
@"
        void Local() => Script.Write(""console.log('l1'); console.log('l2')"");
        Action anon = delegate { Script.Write(""console.log('d1'); console.log('d2')""); };
        M();
        Trailing();
        V = ""x"";
        Local();
        anon();"), skipRoslyn: true);
            StringAssert.Contains(output, "m1\nm2\nt1\nt2\ns1\ns2 x\nl1\nl2\nd1\nd2\n<<DONE>>", output);
        }

        /// <summary>A void lambda whose body is an ordinary call is a statement too, and still runs.</summary>
        [TestMethod]
        public async Task VoidLambdaOverAnOrdinaryCallMatchesNative()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;
public class Program
{
    public static void Main()
    {
        var list = new List<int>();
        Action<int> add = list.Add;
        Action<int> addTwice = n => list.Add(n * 2);
        byte b = 255;
        Action wrap = () => b++;
        add(1); addTwice(2); wrap();
        Console.WriteLine(string.Join("","", list) + "" "" + b);
    }
}");
        }

        // ---- value-returning: one expression, or TransposeR0006 ---------------

        [TestMethod]
        public async Task FuncLambdaReturnsASingleExpressionTemplate()
        {
            var output = await RunTest(Program(@"
    static int Twice(int x) => Script.Write<int>(""{0} * 2"", x);
    static int Scope() => Script.Write<int>(""41 + 1;"");",
@"
        Func<int, int> f = n => Script.Write<int>(""{0} + 100"", n);
        Func<int, int> trailing = n => Script.Write<int>(""{0} + 200;"", n);
        Func<int, int> iife = n => Script.Write<int>(""(function(){ var t = {0} + 1; return t * 10; })()"", n);
        Console.WriteLine(f(1) + "" "" + trailing(1) + "" "" + iife(1) + "" "" + Twice(4) + "" "" + Scope());"),
                skipRoslyn: true);
            StringAssert.Contains(output, "101 201 20 8 42\n<<DONE>>", output);
        }

        [TestMethod]
        public Task FuncLambdaWithMultiStatementTemplateIsRejected()
            => RunTestExpectingError(Program("",
@"
        Func<int, int> f = n => Script.Write<int>(""var t = {0}; t + 100"", n);
        Console.WriteLine(f(1));"), MultiStatementError);

        [TestMethod]
        public Task ExpressionBodiedValueMemberWithMultiStatementTemplateIsRejected()
            => RunTestExpectingError(Program(@"
    static int F(int x) => Script.Write<int>(""var y = {0} + 1; y * 2"", x);",
@"
        Console.WriteLine(F(1));"), MultiStatementError);

        [TestMethod]
        public Task ReturnStatementWithMultiStatementTemplateIsRejected()
            => RunTestExpectingError(Program(@"
    static int F(int x) { return Script.Write<int>(""console.log('x'); {0}"", x); }",
@"
        Console.WriteLine(F(1));"), MultiStatementError);

        [TestMethod]
        public Task ValueLocalFunctionWithMultiStatementTemplateIsRejected()
            => RunTestExpectingError(Program("",
@"
        int Local(int x) => Script.Write<int>(""var y = {0}; y"", x);
        Console.WriteLine(Local(1));"), MultiStatementError);

        [TestMethod]
        public Task ExpressionBodiedPropertyWithMultiStatementTemplateIsRejected()
            => RunTestExpectingError(Program(@"
    static int P => Script.Write<int>(""var y = 1; y"");",
@"
        Console.WriteLine(P);"), MultiStatementError);

        // ---- the template scanner -----------------------------------------------

        [DataTestMethod]
        [DataRow("a; b", true)]
        [DataRow("var y = 1; y * 2", true)]
        [DataRow("a;\n// done\nb", true)]
        [DataRow("a", false)]
        [DataRow("a;", false)]
        [DataRow("a; ;  ", false)]
        [DataRow("hotkeys.getScope(); // trailing", false)]
        [DataRow("a; /* trailing */", false)]
        [DataRow("(function(){ var a = 1; return a; })()", false)]
        [DataRow("{0}.map(function(n){ return String(n); })", false)]
        [DataRow("'a; b'", false)]
        [DataRow("\"a; b\" + 'c;d' + `e;${f};g`", false)]
        [DataRow("s.replace(/;x/g, ';')", false)]
        [DataRow("s.replace(/[;]/g, ';')", false)]
        [DataRow("a / b; c", true)]
        [DataRow("new TransformStream({ transform(c, k) { k.enqueue(c); }, })", false)]
        [DataRow("a); b", false)]
        public void TopLevelSemicolonScanner(string js, bool multiStatement)
            => Assert.AreEqual(multiStatement, ScriptWriteValueScanner.HasCodeAfterTopLevelSemicolon(js), js);
    }
}
