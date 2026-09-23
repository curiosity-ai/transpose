using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// A user-defined implicit conversion operator changes the runtime representation of a value, so
    /// every site where C# inserts one silently has to actually call it. Every site did — except an
    /// object/collection initializer, which wrote the source value straight into the slot.
    ///
    /// The Curiosity front-end's <c>LanguageDTO</c> is where that surfaced: it is a struct whose runtime
    /// value IS its language-code string ("en", "--"), produced by <c>implicit operator
    /// LanguageDTO(Language)</c>, so `new SpotterFromGraphInfo { Language = someLanguageEnum }` stored
    /// the enum's NUMBER and the request serialized as `{"Language":2}` instead of `{"Language":"de"}`.
    ///
    /// Two more bugs sat behind that same type, both covered here:
    ///  - an <c>extern</c> property (its accessor is a <c>[Template]</c>) was treated as an auto-property,
    ///    so a phantom backing field was emitted — and then leaked into the struct's default value,
    ///    <c>$clone</c>, <c>equals</c> and <c>getHashCode</c>. C# gives an extern property no backing field;
    ///  - <c>TransposeR.clone</c> ran a value copy of such a struct through <c>Object.assign</c>, turning
    ///    the string "de" into a String wrapper object (<c>{"0":"d","1":"e"}</c>).
    /// </summary>
    [TestClass]
    public class UserDefinedConversionTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task ImplicitConversionRunsAtEveryConversionSite()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public struct Dto
{
    public string V;
    public Dto(string v) { V = v; }
    public static implicit operator Dto(int value) => new Dto("i" + value);
    public override string ToString() => V ?? "<null>";
}

public class Inner { public Dto D { get; set; } }
public class Holder
{
    public Dto D { get; set; }
    public Dto F;
    public List<Dto> Items { get; } = new List<Dto>();
    public Dictionary<string, Dto> Map { get; } = new Dictionary<string, Dto>();
    public Inner Nest { get; set; } = new Inner();
}

public class Program
{
    static void Take(Dto d) { Console.WriteLine("argument:      " + d); }
    static Dto Make(int i) => i;

    public static void Main()
    {
        int n = 7;

        Dto a = n;
        Console.WriteLine("local:         " + a);
        Dto b; b = n;
        Console.WriteLine("assignment:    " + b);
        Take(n);
        Console.WriteLine("return:        " + Make(n));
        Console.WriteLine("array:         " + new Dto[] { n }[0]);

        var h = new Holder
        {
            D = n,
            F = n,
            Items = { n },
            Map = { ["a"] = n },
            Nest = { D = n },
        };
        Console.WriteLine("member prop:   " + h.D);
        Console.WriteLine("member field:  " + h.F);
        Console.WriteLine("nested member: " + h.Nest.D);
        Console.WriteLine("member coll:   " + h.Items[0]);
        Console.WriteLine("member index:  " + h.Map["a"]);

        Console.WriteLine("collection:    " + new List<Dto> { n }[0]);
        Console.WriteLine("dictionary:    " + new Dictionary<string, Dto> { { "k", n } }["k"]);
        Console.WriteLine("dict index:    " + new Dictionary<string, Dto> { ["k"] = n }["k"]);
    }
}
""");
        }

        [TestMethod]
        public void ExternPropertyGetsNoBackingField()
        {
            var code = """
using Transpose;
public struct S
{
    private extern int Value { [Template("S.Get({this})")] get; }
    private static int Get(S s) => 5;
    public int Read() => Value;
}
public class Program { public static void Main() { System.Console.WriteLine(new S().Read()); } }
""";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed\n"
                + string.Join("\n", result.Diagnostics.Select(d => d.GetMessage())));

            var js = result.Javascript!;
            Assert.IsFalse(js.Contains("$.Value = 0"), "the struct default must not carry a phantom field\n" + js);
            Assert.IsFalse(js.Contains("s.Value = this.Value"), "$clone must not copy a phantom field\n" + js);
            Assert.IsFalse(js.Contains("props:"), "an extern property has no accessor body to emit\n" + js);
            Assert.IsTrue(js.Contains("return S.Get(this)"), "the [Template] getter should still be applied\n" + js);
        }

        /// <summary>
        /// The <c>LanguageDTO</c> shape end to end: a struct with no storage of its own whose runtime
        /// value is a plain JS string, reached only through <c>[Template]</c> members.
        /// </summary>
        [TestMethod]
        public async Task TemplateBackedStructStaysItsPrimitiveValue()
        {
            var output = await RunTest("""
using System;
using Transpose;

public enum Language { Any, English, German }

public static class Languages
{
    public static string EnumToCode(Language input)
    {
        switch (input)
        {
            case Language.English: return "en";
            case Language.German:  return "de";
            default:               return "--";
        }
    }

    public static Language CodeToEnum(string input)
    {
        switch (input)
        {
            case "en": return Language.English;
            case "de": return Language.German;
            default:   return Language.Any;
        }
    }
}

public struct LanguageDTO
{
    [Template("LanguageDTO.Normalize({value})")]
    public extern LanguageDTO(string value);

    private extern Language Value { [Template("LanguageDTO.ValueOf({this})")] get; }

    public static implicit operator LanguageDTO(Language value) => new LanguageDTO(Languages.EnumToCode(value));
    public static implicit operator Language(LanguageDTO value) => value.Value;

    private static string Normalize(string code) => Languages.EnumToCode(Languages.CodeToEnum(code));
    private static Language ValueOf(LanguageDTO value) => Languages.CodeToEnum(Script.Write<string>("{0}", value));
}

public sealed class Request
{
    public string      NodeType { get; set; }
    public LanguageDTO Language { get; set; }
}

public class Program
{
    public static void Main()
    {
        var request = new Request { NodeType = "X", Language = Language.German };
        Console.WriteLine("payload:  " + Script.Write<string>("JSON.stringify({0})", request));

        var copy = request.Language;
        Console.WriteLine("copy:     " + Script.Write<string>("JSON.stringify({0})", copy));
        Console.WriteLine("typeof:   " + Script.Write<string>("typeof {0}", copy));

        Language asEnum = copy;
        Console.WriteLine("as enum:  " + asEnum);
    }
}
""", skipRoslyn: true);

            Assert.AreEqual("""
payload:  {"Language":"de","NodeType":"X"}
copy:     "de"
typeof:   string
as enum:  German
""".Replace("\r\n", "\n").Trim(), output);
        }

        /// <summary>
        /// A conversion operator is <c>static</c>, but the templates written for one spell the operand
        /// <c>{this}</c> — every <c>Transpose.Core</c> primitive does, e.g. <c>String</c>'s
        /// <c>implicit operator string(String value)</c> carries
        /// <c>"{this} != null ? {this}.valueOf() : {this}"</c>. Emitting the call passed no receiver,
        /// so <c>{this}</c> fell back to the literal <c>this</c> and the value being converted was
        /// dropped outright: <c>Show(errorText)</c> came out as
        /// <c>Show(this != null ? this.valueOf() : this)</c>, which reads the enclosing function's
        /// <c>this</c> (<c>undefined</c> in a static method under "use strict").
        /// </summary>
        [TestMethod]
        public void ConversionOperatorTemplateResolvesThisToTheOperand()
        {
            var code = """
using System;
using Transpose;

[External]
[Name("String")]
public class JsString
{
    [Template("{this} != null ? {this}.valueOf() : {this}")]
    public static extern implicit operator string(JsString value);
}

public class Program
{
    static void Show(string s) { Console.WriteLine(s); }

    public static void Main()
    {
        JsString wrapped = null;
        Show(wrapped);
    }
}
""";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed\n"
                + string.Join("\n", result.Diagnostics.Select(d => d.GetMessage())));

            var js = result.Javascript!;
            // The template is a ternary, so its expansion is parenthesized wherever it lands (see
            // TemplateIsNonPrimary) — harmless here, and required the moment it becomes an operand.
            StringAssert.Contains(js, "Program.Show((wrapped != null ? wrapped.valueOf() : wrapped))",
                "{this} in a conversion template is the operand\n" + js);
            Assert.IsFalse(js.Contains("this != null ? this.valueOf() : this"),
                "the operand must not be replaced by the enclosing `this`\n" + js);
        }

        /// <summary>The same conversion, end to end on Node: the wrapper's value has to survive.</summary>
        [TestMethod]
        public async Task ConversionOperatorTemplateConvertsTheValueAtRuntime()
        {
            var output = await RunTest("""
using System;
using Transpose;

[External]
[Name("String")]
public class JsString
{
    [Template("{this} != null ? {this}.valueOf() : {this}")]
    public static extern implicit operator string(JsString value);
}

public class Program
{
    static void Show(string s) { Console.WriteLine("got:    " + (s ?? "<null>")); }

    public static void Main()
    {
        JsString wrapped = Script.Write<JsString>("new String(\"hello\")");
        Show(wrapped);
        string unwrapped = wrapped;
        Console.WriteLine("typeof: " + Script.Write<string>("typeof {0}", unwrapped));
        JsString none = null;
        Show(none);
    }
}
""", skipRoslyn: true);

            Assert.AreEqual("""
got:    hello
typeof: string
got:    <null>
""".Replace("\r\n", "\n").Trim(), output);
        }
    }
}
