using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// A runtime type test against an <c>[ObjectLiteral]</c> type is rejected (TransposeR0005,
    /// <c>ObjectLiteralTypeTestScanner</c>).
    ///
    /// <para>
    /// A literal IS a plain JavaScript object — that is the whole point of the attribute — so nothing
    /// at run time can tell one literal type from another, or from an object that was never a literal
    /// at all. <c>o is Other</c> answers true for a <c>Lit</c>, <c>o as Other</c> hands back a
    /// non-null value, and <c>o is Derived</c> is true for a plain <c>Lit</c>: every one of those is
    /// the opposite of what .NET answers, and the code around it reads as if it were right.
    /// </para>
    ///
    /// <para>
    /// No structural rule would fix it — a <c>{}</c> deserialized into a literal whose only member is
    /// a <c>bool Flag</c> is a legitimate instance with <c>Flag == false</c> — and neither would
    /// tracking where the value came from: the examples below start from a literal Transpose itself
    /// built and are still undecidable. So the test is rejected and a <b>cast</b> is the answer,
    /// which is the right thing to say about a value whose shape the author knows and the runtime
    /// cannot check.
    /// </para>
    ///
    /// <para>
    /// The one test that IS sound is kept: when the value's static type already converts to the
    /// target, the test can only be asking whether the value is null, and the emitted
    /// <c>Transpose.is</c> answers that correctly.
    /// </para>
    /// </summary>
    [TestClass]
    public class ObjectLiteralTypeTestTests : TranslatorTestBase
    {
        private const string TYPES = """
using System;
using Transpose;

[ObjectLiteral] public class Lit { public int X; }
[ObjectLiteral] public class Derived : Lit { public int Y; }
[ObjectLiteral] public class Other { public string Name; }
public class Real { public int Z; }

""";

        private static string Body(string statements) => TYPES + """
public class Program
{
    public static void Main()
    {
        object o = new Lit { X = 1 };
        Console.WriteLine(o);
""" + statements + """

    }
}
""";

        // ---- rejected: every syntactic way of asking ---------------------------

        [TestMethod]
        public Task IsExpressionIsRejected()
            => RunTestExpectingError(Body("Console.WriteLine(o is Other);"),
                "'Other' cannot be tested at run time");

        [TestMethod]
        public Task AsExpressionIsRejected()
            => RunTestExpectingError(Body("var x = o as Other; Console.WriteLine(x);"),
                "an 'as' conversion to it succeeds for any object");

        [TestMethod]
        public Task DeclarationPatternIsRejected()
            => RunTestExpectingError(Body("if (o is Other named) Console.WriteLine(named.Name);"),
                "'Other' cannot be tested at run time");

        /// <summary>A bare name in pattern position parses as a CONSTANT pattern, not a type pattern —
        /// only the binder knows it names a type. Missing that is what let `is not Lit` and a `switch`
        /// arm through while `is Lit l` was caught.</summary>
        [TestMethod]
        public Task NegatedTypePatternIsRejected()
            => RunTestExpectingError(Body("Console.WriteLine(o is not Other);"),
                "'Other' cannot be tested at run time");

        [TestMethod]
        public Task SwitchExpressionArmIsRejected()
            => RunTestExpectingError(Body(@"Console.WriteLine(o switch { Other => ""other"", _ => ""no"" });"),
                "'Other' cannot be tested at run time");

        [TestMethod]
        public Task SwitchStatementCaseIsRejected()
            => RunTestExpectingError(Body(@"switch (o) { case Other ot: Console.WriteLine(ot.Name); break; default: break; }"),
                "'Other' cannot be tested at run time");

        [TestMethod]
        public Task RecursivePatternIsRejected()
            => RunTestExpectingError(Body(@"Console.WriteLine(o is Other { Name: ""a"" });"),
                "'Other' cannot be tested at run time");

        [TestMethod]
        public Task OrPatternIsRejected()
            => RunTestExpectingError(Body("Console.WriteLine(o is Real or Other);"),
                "'Other' cannot be tested at run time");

        /// <summary>Testing an array tests every element with the same undecidable question —
        /// <c>System.Array.matchesUntyped</c> reads the element type off the elements, and a literal
        /// element answers true for anything.</summary>
        [TestMethod]
        public Task ArrayOfLiteralIsRejected()
            => RunTestExpectingError(Body("if (o is Other[] many) Console.WriteLine(many.Length);"),
                "'Other[]' cannot be tested at run time");

        /// <summary>A DOWNCAST between two literals is as undecidable as one from object: a plain
        /// <c>Lit</c> answers true to <c>is Derived</c>, where .NET answers false.</summary>
        [TestMethod]
        public Task DowncastBetweenLiteralsIsRejected()
            => RunTestExpectingError(Body("Lit l = new Lit(); Console.WriteLine(l is Derived);"),
                "'Derived' cannot be tested at run time");

        /// <summary>An <c>[External]</c> literal — a binding's option bag, a DOM dictionary type — is
        /// a plain JS object for exactly the same reason, so it is exactly as untestable. (Its
        /// *members* are its author's business, which is where TransposeR0004 stops and this does
        /// not.)</summary>
        [TestMethod]
        public Task ExternalObjectLiteralIsRejected() => RunTestExpectingError("""
using System;
using Transpose;

[External, ObjectLiteral] public class HowlOptions { public extern string src { get; set; } }

public class Program
{
    public static void Main()
    {
        object o = null;
        Console.WriteLine(o is HowlOptions);
    }
}
""", "'HowlOptions' cannot be tested at run time");

        /// <summary>A sub-pattern is a type test too, and reaches the scanner as its own node.</summary>
        [TestMethod]
        public Task NestedSubPatternIsRejected() => RunTestExpectingError("""
using System;
using Transpose;

[ObjectLiteral] public class Lit { public int X; }
public class Box { public object Inner; }

public class Program
{
    public static void Main()
    {
        object b = new Box();
        Console.WriteLine(b is Box { Inner: Lit });
    }
}
""", "'Lit' cannot be tested at run time");

        /// <summary>The message has to say what to write instead — the point of the diagnostic is that
        /// a cast ASSERTS the type rather than asking about it, which is the whole fix.</summary>
        [TestMethod]
        public Task TheMessageNamesTheCast()
            => RunTestExpectingError(Body("Console.WriteLine(o is Other);"),
                "Cast instead - '(Other)value', 'Script.Write<Other>' and '.As<Other>()'");

        [TestMethod]
        public Task TheMessageExplainsWhy()
            => RunTestExpectingError(Body("Console.WriteLine(o is Other);"),
                "plain JavaScript objects carrying no type identity");

        // ---- allowed: the sound test, and everything that is not a test --------

        /// <summary>When the value's static type already converts to the target, the test can only be
        /// asking whether it is null — and <c>Transpose.is(null, …)</c> answers false, so it means
        /// what it says. Both lines below print what .NET prints.</summary>
        [TestMethod]
        public async Task ANullCheckOnALiteralTypedValueIsAllowed()
        {
            var output = await RunTest(TYPES + """
public class Program
{
    public static void Main()
    {
        Lit present = new Lit { X = 1 };
        Lit absent = null;
        Derived derived = new Derived { X = 1, Y = 2 };

        Console.WriteLine("present:" + (present is Lit));
        Console.WriteLine("absent:" + (absent is Lit));
        Console.WriteLine("upcast:" + (derived is Lit));
        Console.WriteLine("declared:" + (present is Lit p ? p.X : -1));
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            // What .NET prints for the same program, verified natively.
            StringAssert.Contains(output, "present:True", output);
            StringAssert.Contains(output, "absent:False", output);
            StringAssert.Contains(output, "upcast:True", output);
            StringAssert.Contains(output, "declared:1", output);
            StringAssert.Contains(output, "<<DONE>>", output);
        }

        /// <summary>A test against a REAL transpiled class is sound — such an instance carries a
        /// <c>$type</c> and a plain object does not — so nothing here should touch it.</summary>
        [TestMethod]
        public async Task ATestAgainstARealClassIsAllowed()
        {
            var output = await RunTest(TYPES + """
public class Program
{
    public static void Main()
    {
        object fromJs = Script.Write<object>("JSON.parse('{\"X\":7}')");
        Console.WriteLine("real:" + (fromJs is Real));
        Console.WriteLine("string:" + (fromJs is string));
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, "real:False", output);
            StringAssert.Contains(output, "string:False", output);
        }

        /// <summary>The fix the diagnostic names has to work: a cast asserts the type, and reading the
        /// literal back out of a JSON payload is exactly the case it exists for.</summary>
        [TestMethod]
        public async Task ACastToALiteralIsAllowedAndIsTheRemedy()
        {
            var output = await RunTest(TYPES + """
public class Program
{
    public static void Main()
    {
        object fromJs = Script.Write<object>("JSON.parse('{\"X\":7}')");
        Console.WriteLine("cast:" + ((Lit)fromJs).X);
        Console.WriteLine("write:" + Script.Write<Lit>("{0}", fromJs).X);
        Console.WriteLine("as:" + fromJs.As<Lit>().X);
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, "cast:7", output);
            StringAssert.Contains(output, "write:7", output);
            StringAssert.Contains(output, "as:7", output);
        }

        /// <summary>A constant pattern that really is a constant stays a constant — the
        /// ConstantPattern visitor only fires when the name binds to a type.</summary>
        [TestMethod]
        public async Task ConstantAndNullPatternsAreUntouched()
        {
            var output = await RunTest("""
using System;

public class Program
{
    const int Two = 2;
    public static void Main()
    {
        object o = 2;
        Console.WriteLine("const:" + (o is Two));
        Console.WriteLine("null:" + (o is null));
        Console.WriteLine("int:" + (o is int));
        Console.WriteLine("<<DONE>>");
    }
}
""");

            StringAssert.Contains(output, "<<DONE>>", output);
        }
    }
}
