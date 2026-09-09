using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// An <c>[ObjectLiteral]</c> instance IS a plain JavaScript object — <c>new Options { A = 1 }</c>
    /// emits <c>{A: 1}</c>, with no <c>Transpose.define</c>d class behind it — so every field and
    /// property it declares has to hold something JavaScript can represent on its own. Anything else
    /// is a tps.js runtime object (a <c>System.Int64</c> is a <c>{low, high}</c> pair, a
    /// <c>DateTime</c> a runtime instance, a <c>List&lt;T&gt;</c> a class with a prototype), which
    /// produces a literal that looks right in C#, serializes to something nobody wrote, and cannot be
    /// read by the JavaScript the literal exists to talk to.
    ///
    /// <para>
    /// So the declaration is rejected (TransposeR0004, <c>ObjectLiteralMemberScanner</c>) rather than
    /// emitted. 64-bit integers are the case worth naming: they are unwrapped to plain numbers inside
    /// a literal (<c>Emitter.Foreign64.cs</c>), which is representable but silently lossy above 2^53
    /// — the trade this check replaces with an error.
    /// </para>
    /// </summary>
    [TestClass]
    public class ObjectLiteralMemberTypeTests : TranslatorTestBase
    {
        // ---- rejected members --------------------------------------------------

        [TestMethod]
        public Task LongPropertyIsRejected() => RunTestExpectingError("""
using Transpose;

[ObjectLiteral]
public class Info
{
    public long Id { get; set; }
}

public class Program { public static void Main() { var i = new Info(); } }
""", "'Id' is a 'long'");

        /// <summary>The precision loss is the reason, so the message has to say it.</summary>
        [TestMethod]
        public Task LongRejectionExplainsThePrecisionLoss() => RunTestExpectingError("""
using Transpose;

[ObjectLiteral]
public class Info
{
    public long Id { get; set; }
}

public class Program { public static void Main() { var i = new Info(); } }
""", "loses precision above 2^53");

        [TestMethod]
        public Task ULongFieldIsRejected() => RunTestExpectingError("""
using Transpose;

[ObjectLiteral]
public class Info
{
    public ulong Bytes;
}

public class Program { public static void Main() { var i = new Info(); } }
""", "'Bytes' is a 'ulong'");

        /// <summary><c>long?</c> is a long slot plus null; the long is the problem either way.</summary>
        [TestMethod]
        public Task NullableLongIsRejected() => RunTestExpectingError("""
using Transpose;

[ObjectLiteral]
public class Info
{
    public long? Maybe { get; set; }
}

public class Program { public static void Main() { var i = new Info(); } }
""", "'Maybe' is a 'long?'");

        /// <summary>decimal is a tps.js runtime object, for the same reason Int64 is.</summary>
        [TestMethod]
        public Task DecimalIsRejected() => RunTestExpectingError("""
using Transpose;

[ObjectLiteral]
public class Money
{
    public decimal Amount { get; set; }
}

public class Program { public static void Main() { var m = new Money(); } }
""", "'Amount' is a 'decimal'");

        [TestMethod]
        public Task BclStructIsRejected() => RunTestExpectingError("""
using System;
using Transpose;

[ObjectLiteral]
public class Entry
{
    public DateTime When { get; set; }
}

public class Program { public static void Main() { var e = new Entry(); } }
""", "'When' is a 'DateTime'");

        [TestMethod]
        public Task CollectionIsRejected() => RunTestExpectingError("""
using System.Collections.Generic;
using Transpose;

[ObjectLiteral]
public class Bag
{
    public List<int> Items { get; set; }
}

public class Program { public static void Main() { var b = new Bag(); } }
""", "'Items' is a 'List<int>'");

        [TestMethod]
        public Task PlainClassMemberIsRejected() => RunTestExpectingError("""
using Transpose;

public class Nested { public int X; }

[ObjectLiteral]
public class Outer
{
    public Nested Inner { get; set; }
}

public class Program { public static void Main() { var o = new Outer(); } }
""", "'Inner' is a 'Nested'");

        /// <summary>A struct is a runtime object too — it has a $clone and a getHashCode.</summary>
        [TestMethod]
        public Task PlainStructMemberIsRejected() => RunTestExpectingError("""
using Transpose;

public struct Point { public int X; public int Y; }

[ObjectLiteral]
public class Shape
{
    public Point Origin { get; set; }
}

public class Program { public static void Main() { var s = new Shape(); } }
""", "'Origin' is a 'Point'");

        /// <summary>An array is only as plain as what it holds.</summary>
        [TestMethod]
        public Task ArrayOfNonLiteralElementsIsRejected() => RunTestExpectingError("""
using Transpose;

public class Widget { public int X; }

[ObjectLiteral]
public class Page
{
    public Widget[] Widgets { get; set; }
}

public class Program { public static void Main() { var p = new Page(); } }
""", "'Widgets' is a 'Widget[]'");

        /// <summary>A record's positional parameters ARE its members, so they are checked too.</summary>
        [TestMethod]
        public Task RecordPositionalMemberIsRejected() => RunTestExpectingError("""
using Transpose;

[ObjectLiteral]
public record Sample(int Index, long Ticks);

public class Program { public static void Main() { var s = new Sample(1, 2L); } }
""", "'Ticks' is a 'long'");

        /// <summary>[ObjectLiteral] is inherited, so a derived literal's own slots are checked as well.</summary>
        [TestMethod]
        public Task DerivedLiteralIsChecked() => RunTestExpectingError("""
using Transpose;

[ObjectLiteral]
public class Base { public int A { get; set; } }

public class Derived : Base { public long B { get; set; } }

public class Program { public static void Main() { var d = new Derived(); } }
""", "'B' is a 'long'");

        /// <summary>
        /// Every offending member is reported, not just the first: the error is on the declaration, so
        /// fixing one and rebuilding must not turn up the next one at a time.
        /// </summary>
        [TestMethod]
        public void EveryOffendingMemberIsReported()
        {
            var result = new RoslynTranslator().Translate("""
using System;
using Transpose;

[ObjectLiteral]
public class Info
{
    public long Id { get; set; }
    public ulong Bytes;
    public DateTime When { get; set; }
    public int Fine { get; set; }
}

public class Program { public static void Main() { var i = new Info(); } }
""");

            Assert.IsFalse(result.Success, "translation should have failed");
            var errors = string.Join("\n", result.Errors.Select(d => d.GetMessage()));
            StringAssert.Contains(errors, "'Id' is a 'long'", errors);
            StringAssert.Contains(errors, "'Bytes' is a 'ulong'", errors);
            StringAssert.Contains(errors, "'When' is a 'DateTime'", errors);
            Assert.IsFalse(errors.Contains("'Fine'"), "an int member is fine\n" + errors);
            Assert.AreEqual(3, result.Errors.Count(d => d.Id == "TransposeR0004"),
                "one error per offending member\n" + errors);
        }

        /// <summary>The error points at the member's own declaration, so an IDE lands on it.</summary>
        [TestMethod]
        public void TheErrorPointsAtTheMemberDeclaration()
        {
            var result = new RoslynTranslator().Translate("""
using Transpose;

[ObjectLiteral]
public class Info
{
    public int Ok { get; set; }
    public long Id { get; set; }
}

public class Program { public static void Main() { var i = new Info(); } }
""");

            Assert.IsFalse(result.Success, "translation should have failed");
            var error = result.Errors.Single(d => d.Id == "TransposeR0004");
            // 1-based line 7 is `public long Id { get; set; }`.
            Assert.AreEqual(7, error.Location.GetLineSpan().StartLinePosition.Line + 1,
                "the error should be on the member, not on the type\n" + error);
        }

        // ---- accepted members --------------------------------------------------

        /// <summary>
        /// The whole allowed surface, built and handed to JSON.stringify — which is the point of the
        /// attribute, and the check that nothing in the object is a runtime instance in disguise.
        /// </summary>
        [TestMethod]
        public async Task EveryPlainMemberTypeCompilesAndSerializes()
        {
            var output = await RunTest("""
using System;
using Transpose;

public enum Level { Low = 1, High = 2 }

[ObjectLiteral(ObjectInitializationMode.DefaultValue)]
public class Inner { public string Tag { get; set; } }

[ObjectLiteral(ObjectInitializationMode.DefaultValue)]
public class Shape
{
    public bool Flag { get; set; }
    public char Initial { get; set; }
    public sbyte  I8  { get; set; }
    public byte   U8  { get; set; }
    public short  I16 { get; set; }
    public ushort U16 { get; set; }
    public int    I32 { get; set; }
    public uint   U32 { get; set; }
    public float  F32 { get; set; }
    public double F64 { get; set; }
    public string Text { get; set; }
    public object Anything { get; set; }
    public Level  Rank { get; set; }
    public int?   Maybe { get; set; }
    public int[]  Numbers { get; set; }
    public string[][] Grid { get; set; }
    public Inner  Nested { get; set; }
    public Inner[] Many { get; set; }
}

public class Program
{
    public static void Main()
    {
        var s = new Shape
        {
            Flag = true, Initial = 'x', I8 = -1, U8 = 2, I16 = -3, U16 = 4, I32 = -5, U32 = 6,
            F32 = 1.5f, F64 = 2.5, Text = "hi", Anything = null, Rank = Level.High, Maybe = 9,
            Numbers = new[] { 1, 2 }, Grid = new[] { new[] { "a" } },
            Nested = new Inner { Tag = "n" }, Many = new[] { new Inner { Tag = "m" } },
        };

        Console.WriteLine(Script.Write<string>("JSON.stringify(s)"));
        Console.WriteLine("proto-is-plain:" + Script.Write<bool>("Object.getPrototypeOf(s) === Object.prototype"));
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, "\"Flag\":true", output);
            StringAssert.Contains(output, "\"I32\":-5", output);
            StringAssert.Contains(output, "\"Text\":\"hi\"", output);
            StringAssert.Contains(output, "\"Rank\":2", output);
            StringAssert.Contains(output, "\"Numbers\":[1,2]", output);
            StringAssert.Contains(output, "\"Grid\":[[\"a\"]]", output);
            StringAssert.Contains(output, "\"Nested\":{\"Tag\":\"n\"}", output);
            StringAssert.Contains(output, "\"Many\":[{\"Tag\":\"m\"}]", output);
            StringAssert.Contains(output, "proto-is-plain:True", output);
        }

        /// <summary>A callback slot is a JS function, which a plain object holds perfectly well.</summary>
        [TestMethod]
        public async Task DelegateMemberIsAllowed()
        {
            var output = await RunTest("""
using System;
using Transpose;

[ObjectLiteral]
public class Options
{
    public Action<string> OnDone { get; set; }
}

public class Program
{
    public static void Main()
    {
        var o = new Options { OnDone = m => Console.WriteLine("done:" + m) };
        Console.WriteLine("typeof:" + Script.Write<string>("typeof o.OnDone"));
        o.OnDone("now");
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, "typeof:function", output);
            StringAssert.Contains(output, "done:now", output);
        }

        /// <summary>
        /// A computed property is a method on the type's prototype — it holds nothing in the object, so
        /// its type says nothing about the object's shape and is not checked.
        /// </summary>
        [TestMethod]
        public void ComputedPropertyIsNotASlot()
        {
            var result = new RoslynTranslator().Translate("""
using System;
using System.Collections.Generic;
using Transpose;

[ObjectLiteral]
public class Info
{
    public int Count { get; set; }
    public List<int> Range => new List<int>();
    public DateTime Now() => DateTime.MinValue;
}

public class Program { public static void Main() { Console.WriteLine(new Info().Count); } }
""");

            Assert.IsTrue(result.Success, string.Join("\n", result.Errors.Select(d => d.GetMessage())));
        }

        /// <summary>Statics live on the type, not in the object, and a const is inlined.</summary>
        [TestMethod]
        public void StaticsAndConstantsAreNotSlots()
        {
            var result = new RoslynTranslator().Translate("""
using System;
using System.Collections.Generic;
using Transpose;

[ObjectLiteral]
public class Info
{
    public const long Limit = 10L;
    public static List<int> Shared = new List<int>();
    public static DateTime Epoch { get; set; }
    public int Count { get; set; }
}

public class Program { public static void Main() { Console.WriteLine(new Info().Count); } }
""");

            Assert.IsTrue(result.Success, string.Join("\n", result.Errors.Select(d => d.GetMessage())));
        }

        /// <summary>
        /// An indexer names no slot — it reads whatever key it is handed — so the type it returns is
        /// not part of the object's shape (the shape a DOM dictionary binding declares).
        /// </summary>
        [TestMethod]
        public void IndexerIsNotASlot()
        {
            var result = new RoslynTranslator().Translate("""
using System;
using System.Collections.Generic;
using Transpose;

[ObjectLiteral]
public class Keyframe
{
    public extern double? offset { get; set; }
    public extern List<int> this[string property] { get; set; }
}

public class Program { public static void Main() { Console.WriteLine("ok"); } }
""");

            Assert.IsTrue(result.Success, string.Join("\n", result.Errors.Select(d => d.GetMessage())));
        }

        /// <summary>
        /// An [External] literal describes an object that already exists in JavaScript — a binding's
        /// option bag, a DOM dictionary. Its author decides what its slots hold, and a <c>ulong</c>
        /// there is the browser's plain number (see <c>ForeignJs64BitTests</c>), not something this
        /// compiler is materialising.
        /// </summary>
        [TestMethod]
        public void ExternalLiteralIsNotChecked()
        {
            var result = new RoslynTranslator().Translate("""
using System;
using Transpose;

[External]
[ObjectLiteral]
public class BlobOptions
{
    public ulong size { get; set; }
    public double? volume { get; set; }
}

public class Program { public static void Main() { Console.WriteLine("ok"); } }
""");

            Assert.IsTrue(result.Success, string.Join("\n", result.Errors.Select(d => d.GetMessage())));
        }

        /// <summary>
        /// An unsubstituted type parameter cannot be judged here; the literal it is substituted with is
        /// checked at its own declaration.
        /// </summary>
        [TestMethod]
        public void TypeParameterMemberIsNotChecked()
        {
            var result = new RoslynTranslator().Translate("""
using System;
using Transpose;

[ObjectLiteral]
public class Box<T>
{
    public T Value { get; set; }
}

public class Program { public static void Main() { Console.WriteLine(new Box<int> { Value = 1 }.Value); } }
""");

            Assert.IsTrue(result.Success, string.Join("\n", result.Errors.Select(d => d.GetMessage())));
        }

        /// <summary>The control: none of this applies to an ordinary transpiled class.</summary>
        [TestMethod]
        public void APlainClassKeepsItsRuntimeTypedMembers()
        {
            var result = new RoslynTranslator().Translate("""
using System;
using System.Collections.Generic;

public class Info
{
    public long Id { get; set; }
    public decimal Amount { get; set; }
    public DateTime When { get; set; }
    public List<int> Items { get; set; }
}

public class Program { public static void Main() { Console.WriteLine(new Info().Id); } }
""");

            Assert.IsTrue(result.Success, string.Join("\n", result.Errors.Select(d => d.GetMessage())));
        }
    }
}
