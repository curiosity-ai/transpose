using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// A large <em>dense</em> enum — members numbered 0…n-1 in declaration order — ships its member
    /// names as one delimited string rather than a member-per-name object literal, and the runtime
    /// expands it back (<c>TryDenseEnumNames</c> in the emitter, <c>Class.js</c> at the other end).
    ///
    /// The point is generated enums, which are enormous and are exactly the ones an application
    /// cannot avoid loading: Tesserae's 5,372-member icon table is 212 KB as an object literal and
    /// 100 KB as a string, and it sits in the initial payload of every application that renders an
    /// icon. The names carry all the information, because in a dense enum each one's value is its
    /// own index — the literal was writing down what the order already said.
    ///
    /// What has to keep working is everything that asks the enum a question, since a member must
    /// stay an ordinary own property of the type whichever form it arrived in: ToString, GetName,
    /// GetNames, GetValues, Parse, IsDefined, casting both ways. The encoding also has to refuse
    /// anything it could not round-trip, rather than lose a member quietly.
    /// </summary>
    [TestClass]
    public class DenseEnumEmissionTests : TranslatorTestBase
    {
        /// <summary>An enum past the size threshold, named with <c>[Name]</c> so the emitted names
        /// differ from the C# ones — the compact string carries emitted names, not declared ones.
        /// Transpose-only: real .NET has no such attribute, so anything using this cannot be run
        /// against native for comparison.</summary>
        private static string BigNamedEnum(string extra = "") => @"
using System;
using Transpose;
[Enum(Emit.Value)]
public enum Big
{
    [Name(""ic-zero"")] Zero,
    [Name(""ic-one"")] One,
    [Name(""ic-two"")] Two,
" + string.Join("\n", Enumerable.Range(3, 70).Select(i => $@"    [Name(""ic-{i}"")] M{i},")) + @"
}
" + extra;

        /// <summary>The same size and shape in plain C#, which native .NET can compile too.</summary>
        private static string BigPlainEnum(string extra = "") => @"
using System;
public enum Big
{
    Zero,
    One,
    Two,
" + string.Join("\n", Enumerable.Range(3, 70).Select(i => $"    M{i},")) + @"
}
" + extra;

        [TestMethod]
        public async Task ADenseEnumAnswersEveryQuestionTheSameAsNative()
        {
            // Each of these reaches the members through a different runtime path — the value→name
            // map, the name list, the values list, the parser — and every one of them needs a member
            // to be an ordinary own property of the type, which is what the expansion restores.
            await RunTest(BigPlainEnum(@"
public class Program
{
    public static void Main()
    {
        Console.WriteLine(Big.Zero.ToString());
        Console.WriteLine(Big.Two.ToString());
        Console.WriteLine(Big.M42.ToString());
        Console.WriteLine((int)Big.M42);
        Console.WriteLine(((Big)5).ToString());
        Console.WriteLine(Enum.GetNames(typeof(Big)).Length);
        Console.WriteLine(Enum.GetNames(typeof(Big))[0]);
        Console.WriteLine(Enum.GetNames(typeof(Big))[42]);
        Console.WriteLine(Enum.GetValues(typeof(Big)).Length);
        Console.WriteLine(Enum.GetName(typeof(Big), 2));
        Console.WriteLine(Enum.IsDefined(typeof(Big), 42));
        Console.WriteLine(Enum.IsDefined(typeof(Big), 9999));
        Console.WriteLine(Enum.Parse(typeof(Big), ""Two"").ToString());
        Console.WriteLine((int)(Big)Enum.Parse(typeof(Big), ""M42""));
        Console.WriteLine(""<<DONE>>"");
    }
}"), waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public async Task TheCompactedNamesAreTheEmittedOnes()
        {
            // [Name] renames a member in JavaScript, so the string must carry "ic-two", not "Two".
            // Transpose-only: native .NET has no [Name], so this runs the JS alone and reads it back.
            var output = await RunTest(BigNamedEnum(@"
public class Program
{
    public static void Main()
    {
        Console.WriteLine(Big.Two.ToString());
        Console.WriteLine(Enum.GetNames(typeof(Big))[42]);
        Console.WriteLine((int)(Big)Enum.Parse(typeof(Big), ""ic-42""));
        Console.WriteLine(""<<DONE>>"");
    }
}"), waitForOutput: "<<DONE>>", skipRoslyn: true);

            StringAssert.Contains(output, "ic-two", "ToString must report the emitted name");
            StringAssert.Contains(output, "ic-42", "…and so must GetNames");
        }

        // ---- the encoding itself ------------------------------------------------

        private static string EnumJs(string source) =>
            new RoslynTranslator().Translate(source).Javascript ?? "";

        [TestMethod]
        public void ALargeDenseEnumEmitsOneStringInsteadOfAPropertyPerMember()
        {
            var js = EnumJs(BigNamedEnum());

            StringAssert.Contains(js, "$denseNames:", "a dense enum past the threshold must use the compact form");
            StringAssert.Contains(js, "ic-zero,ic-one,ic-two,", "the names travel in declaration order, which is what implies the values");
            Assert.IsFalse(js.Contains("\"ic-one\": 1"), "and the member-per-name literal is gone");
        }

        [TestMethod]
        public void ASmallEnumIsUnchanged()
        {
            // Below the threshold the compact form buys nothing and would only add a split at define
            // time, so almost every enum in a program keeps the emission it always had.
            var js = EnumJs(@"
public enum Small { A, B, C }
");
            Assert.IsFalse(js.Contains("$denseNames"), "a small enum must keep the object literal");
            StringAssert.Contains(js, "A: 0");
        }

        [TestMethod]
        public void AnEnumWithAGapOrAnAliasKeepsTheObjectLiteral()
        {
            // Both break the one thing the compact form relies on: that a member's value is its index.
            var gap = EnumJs(@"
public enum Gappy
{
" + string.Join("\n", Enumerable.Range(0, 70).Select(i => $"    G{i} = {i * 2},")) + @"
}");
            Assert.IsFalse(gap.Contains("$denseNames"), "a gap means the position no longer implies the value");

            var alias = EnumJs(@"
public enum Aliased
{
" + string.Join("\n", Enumerable.Range(0, 70).Select(i => $"    A{i} = {i},")) + @"
    Same = 3,
}");
            Assert.IsFalse(alias.Contains("$denseNames"), "an alias means two names share a value, which the ordering cannot express");
        }

        [TestMethod]
        public void AFlagsEnumKeepsTheObjectLiteral()
        {
            var js = EnumJs(@"
using System;
[Flags]
public enum Bits
{
" + string.Join("\n", Enumerable.Range(0, 70).Select(i => $"    B{i} = {1 << (i % 30)},")) + @"
}");
            Assert.IsFalse(js.Contains("$denseNames"), "a bit pattern is not dense");
        }

        [TestMethod]
        public void ANameHoldingTheDelimiterFallsBackRatherThanLosingAMember()
        {
            // A C# identifier cannot contain a comma or a quote, but [Name(...)] can say anything —
            // and encoding one into a comma-separated string would silently split a member in two.
            foreach (var bad in new[] { "a,b", "a\"b", "a\\b" })
            {
                var js = EnumJs(@"
using Transpose;
public enum Risky
{
    [Name(""" + bad.Replace("\\", "\\\\").Replace("\"", "\\\"") + @""")] First,
" + string.Join("\n", Enumerable.Range(1, 70).Select(i => $"    R{i},")) + @"
}");
                Assert.IsFalse(js.Contains("$denseNames"),
                    $"a member named {bad} cannot be encoded and must keep the object literal");
            }
        }

        [TestMethod]
        public async Task AStringBackedEnumIsNeverCompacted()
        {
            // Its members' values are the names themselves rather than their positions, so nothing
            // is implied by order and there is nothing to leave out.
            var source = @"
using System;
using Transpose;
[Enum(Emit.StringName)]
public enum Named
{
" + string.Join("\n", Enumerable.Range(0, 70).Select(i => $@"    [Name(""n-{i}"")] N{i},")) + @"
}
public class Program
{
    public static void Main()
    {
        Console.WriteLine(Named.N5.ToString());
        Console.WriteLine(Enum.GetNames(typeof(Named)).Length);
        Console.WriteLine(""<<DONE>>"");
    }
}";
            Assert.IsFalse(EnumJs(source).Contains("$denseNames"));

            var output = await RunTest(source, waitForOutput: "<<DONE>>", skipRoslyn: true);
            StringAssert.Contains(output, "n-5", "a string-backed enum still reports its member's string");
            StringAssert.Contains(output, "70", "…and still knows how many members it has");
        }
    }
}
