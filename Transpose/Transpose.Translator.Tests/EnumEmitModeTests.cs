using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Validates every <c>[Enum(Emit.X)]</c> style against the emitted JavaScript, matching the
    /// nine H5 modes: 1 Name, 2 Value, 3 StringName, 4 StringNamePreserveCase, 5 StringNameLowerCase,
    /// 6 StringNameUpperCase, 7 NamePreserveCase, 8 NameLowerCase, 9 NameUpperCase.
    /// <para>
    /// <b>Every expected value in this file is a shipped contract, not an observation.</b> A mode is
    /// chosen by a library author precisely to pin down what its members look like at runtime — a CSS
    /// class, a JS API's string constant, an ordinal an external function expects — so changing any
    /// expectation here silently rewrites every consumer's output. If a change to the emitter makes an
    /// assertion in this file fail, the emitter is wrong; do not "update" the expected value. Only a
    /// deliberate, breaking redefinition of a mode may touch these, and then the whole table below
    /// moves together.
    /// </para>
    /// </summary>
    [TestClass]
    public class EnumEmitModeTests : TranslatorTestBase
    {
        private static string Emit(string emitMode)
        {
            var code = $@"
using Transpose;
[Enum(Emit.{emitMode})]
public enum Dir {{ TopLeft = 0, BottomRight = 1 }}
public class Program {{ public static void Main() {{ var x = Dir.TopLeft; }} }}
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed\n" +
                string.Join("\n", result.Diagnostics.Select(d => d.GetMessage())));
            return result.Javascript!;
        }

        [TestMethod]
        public void Value_EmitsNumericConstantAtUseSite()
        {
            var js = Emit("Value");
            // Reference compiles to the raw ordinal, not Dir.TopLeft.
            Assert.IsTrue(js.Contains("let x = 0") || js.Contains("var x = 0"), "Emit.Value should inline the ordinal\n" + js);
        }

        // Emit.Name camelCases the member's first letter — it is the Name-family counterpart of
        // Emit.StringName, not a synonym for NamePreserveCase (h5 emits `topLeft: 0` here).
        [TestMethod]
        public void Name_CamelCasesMemberNameAndReferencesEnumObject()
        {
            var js = Emit("Name");
            Assert.IsTrue(js.Contains("topLeft: 0"), "define key camelCases the first letter\n" + js);
            Assert.IsTrue(js.Contains("Dir.topLeft"), "reference uses the enum object member\n" + js);
            Assert.IsFalse(js.Contains("Dir.TopLeft"), "Emit.Name is not NamePreserveCase\n" + js);
        }

        [TestMethod]
        public void NamePreserveCase_PreservesMemberName()
        {
            var js = Emit("NamePreserveCase");
            Assert.IsTrue(js.Contains("TopLeft: 0"), "define key preserves case\n" + js);
            Assert.IsTrue(js.Contains("Dir.TopLeft"), "reference preserves case\n" + js);
        }

        [TestMethod]
        public void NameLowerCase_LowercasesMemberName()
        {
            var js = Emit("NameLowerCase");
            Assert.IsTrue(js.Contains("topleft: 0"), "define key should be lowercased\n" + js);
            Assert.IsTrue(js.Contains("Dir.topleft"), "reference should be lowercased\n" + js);
            Assert.IsFalse(js.Contains("Dir.TopLeft"), "must not keep PascalCase\n" + js);
        }

        [TestMethod]
        public void NameUpperCase_UppercasesMemberName()
        {
            var js = Emit("NameUpperCase");
            Assert.IsTrue(js.Contains("TOPLEFT: 0"), "define key should be uppercased\n" + js);
            Assert.IsTrue(js.Contains("Dir.TOPLEFT"), "reference should be uppercased\n" + js);
        }

        // A StringName* mode names the member's JS slot with the SAME text it emits as the value
        // (h5: `topLeft: "topLeft"`), which is what makes ToString() report the string a consumer
        // sees rather than the C# member name.
        [TestMethod]
        public void StringName_CamelCasesFirstLetterAsStringValue()
        {
            var js = Emit("StringName");
            Assert.IsTrue(js.Contains("topLeft: \"topLeft\""), "slot and value are both the camelCase string\n" + js);
            Assert.IsTrue(js.Contains("$utype: System.String"), "string-backed enum declares $utype\n" + js);
        }

        [TestMethod]
        public void StringNamePreserveCase_KeepsCaseAsStringValue()
        {
            var js = Emit("StringNamePreserveCase");
            Assert.IsTrue(js.Contains("TopLeft: \"TopLeft\""), "slot and value both preserve case\n" + js);
            Assert.IsTrue(js.Contains("$utype: System.String"), "string-backed enum declares $utype\n" + js);
        }

        [TestMethod]
        public void StringNameLowerCase_LowercasesStringValue()
        {
            var js = Emit("StringNameLowerCase");
            Assert.IsTrue(js.Contains("topleft: \"topleft\""), "slot and value are both lowercase\n" + js);
        }

        [TestMethod]
        public void StringNameUpperCase_UppercasesStringValue()
        {
            var js = Emit("StringNameUpperCase");
            Assert.IsTrue(js.Contains("TOPLEFT: \"TOPLEFT\""), "slot and value are both uppercase\n" + js);
        }

        [TestMethod]
        public void DefaultMode_IsNamePreserveCase()
        {
            var code = @"
using Transpose;
public enum Dir { TopLeft = 0 }
public class Program { public static void Main() { var x = Dir.TopLeft; } }
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success);
            // No [Enum] attribute → default mode 7 (NamePreserveCase): named member, preserved case.
            Assert.IsTrue(result.Javascript!.Contains("TopLeft: 0"), "default preserves case\n" + result.Javascript);
            Assert.IsTrue(result.Javascript!.Contains("Dir.TopLeft"), "default references the enum member\n" + result.Javascript);
        }

        // ---- the RUNTIME contract: what a value of each mode stringifies to --------------------
        //
        // The tests above only inspect the emitted JS *text* — the define's keys and the shape of a
        // use-site reference. That left the whole enum→string surface (ToString(), concatenation,
        // interpolation) uncovered for every one of the nine modes, which is how a change that made
        // Value-mode ToString() return the raw ordinal instead of the member's name shipped without a
        // single test going red (it reached Tesserae as <i class="4615"> where every icon class should
        // have been <i class="fi-rr-bug">). These two tests close that gap: they run the JS and pin the
        // observable string for every mode, with and without a [Name] override.
        //
        // Transpose-only (skipRoslyn): [Name] is a Transpose codegen attribute native .NET ignores, and
        // the NameLowerCase/NameUpperCase modes deliberately re-case the name table itself, so their
        // ToString() cannot match native by construction. Native parity for the modes that DO promise
        // it is covered by EmitRegressionTests (DefaultModeEnumToStringStillMatchesNative,
        // BclValueModeEnumToStringStillMatchesNative).
        [TestMethod]
        public async Task EveryMode_StringifiesToItsContractedValue()
        {
            var js = await RunTest(@"
using System;
using Transpose;
[Enum(Emit.Value)]                  public enum V  { TopLeft = 0 }
[Enum(Emit.Name)]                   public enum N  { TopLeft = 0 }
[Enum(Emit.NamePreserveCase)]       public enum NP { TopLeft = 0 }
[Enum(Emit.NameLowerCase)]          public enum NL { TopLeft = 0 }
[Enum(Emit.NameUpperCase)]          public enum NU { TopLeft = 0 }
[Enum(Emit.StringName)]             public enum S  { TopLeft = 0 }
[Enum(Emit.StringNamePreserveCase)] public enum SP { TopLeft = 0 }
[Enum(Emit.StringNameLowerCase)]    public enum SL { TopLeft = 0 }
[Enum(Emit.StringNameUpperCase)]    public enum SU { TopLeft = 0 }
                                    public enum D  { TopLeft = 0 }
public class Program
{
    public static void Main()
    {
        // one line per mode: explicit ToString(), concatenation, interpolation
        Console.WriteLine(""V= ""  + V.TopLeft.ToString()  + "" | "" + V.TopLeft  + "" | "" + $""{V.TopLeft}"");
        Console.WriteLine(""N= ""  + N.TopLeft.ToString()  + "" | "" + N.TopLeft  + "" | "" + $""{N.TopLeft}"");
        Console.WriteLine(""NP= "" + NP.TopLeft.ToString() + "" | "" + NP.TopLeft + "" | "" + $""{NP.TopLeft}"");
        Console.WriteLine(""NL= "" + NL.TopLeft.ToString() + "" | "" + NL.TopLeft + "" | "" + $""{NL.TopLeft}"");
        Console.WriteLine(""NU= "" + NU.TopLeft.ToString() + "" | "" + NU.TopLeft + "" | "" + $""{NU.TopLeft}"");
        Console.WriteLine(""S= ""  + S.TopLeft.ToString()  + "" | "" + S.TopLeft  + "" | "" + $""{S.TopLeft}"");
        Console.WriteLine(""SP= "" + SP.TopLeft.ToString() + "" | "" + SP.TopLeft + "" | "" + $""{SP.TopLeft}"");
        Console.WriteLine(""SL= "" + SL.TopLeft.ToString() + "" | "" + SL.TopLeft + "" | "" + $""{SL.TopLeft}"");
        Console.WriteLine(""SU= "" + SU.TopLeft.ToString() + "" | "" + SU.TopLeft + "" | "" + $""{SU.TopLeft}"");
        Console.WriteLine(""D= ""  + D.TopLeft.ToString()  + "" | "" + D.TopLeft  + "" | "" + $""{D.TopLeft}"");
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>", skipRoslyn: true);

            // Value / NamePreserveCase / default: the ordinal is the runtime value, and the name is read
            // back off the enum object's table — the member name, case preserved.
            Assert.IsTrue(js.Contains("V= TopLeft | TopLeft | TopLeft"), "Emit.Value stringifies to the member NAME (the ordinal is only its runtime representation)\n" + js);
            Assert.IsTrue(js.Contains("NP= TopLeft | TopLeft | TopLeft"), "Emit.NamePreserveCase\n" + js);
            Assert.IsTrue(js.Contains("D= TopLeft | TopLeft | TopLeft"), "no [Enum] attribute → NamePreserveCase\n" + js);

            // The casing modes re-case the name table itself, so that is what comes back. Name (1) is
            // the camelCasing member of the Name family, paired with StringName (3) below.
            Assert.IsTrue(js.Contains("N= topLeft | topLeft | topLeft"), "Emit.Name camelCases\n" + js);
            Assert.IsTrue(js.Contains("NL= topleft | topleft | topleft"), "Emit.NameLowerCase\n" + js);
            Assert.IsTrue(js.Contains("NU= TOPLEFT | TOPLEFT | TOPLEFT"), "Emit.NameUpperCase\n" + js);

            // StringName*: the runtime value IS the (cased) string, and the member's slot carries the
            // same text, so ToString() reports the string a consumer actually sees.
            Assert.IsTrue(js.Contains("S= topLeft | topLeft | topLeft"), "Emit.StringName\n" + js);
            Assert.IsTrue(js.Contains("SP= TopLeft | TopLeft | TopLeft"), "Emit.StringNamePreserveCase\n" + js);
            Assert.IsTrue(js.Contains("SL= topleft | topleft | topleft"), "Emit.StringNameLowerCase\n" + js);
            Assert.IsTrue(js.Contains("SU= TOPLEFT | TOPLEFT | TOPLEFT"), "Emit.StringNameUpperCase\n" + js);
        }

        // A [Name] on a member overrides the emitted name in EVERY mode — it is the whole point of the
        // attribute, and what a binding library pins its runtime strings to (Tesserae's UIcons/Emoji
        // are [Enum(Emit.Value)] with a [Name] per member, and read those names back through
        // ToString()). A [Name] is free-form, so it is usually not a legal JS identifier: the define
        // quotes such a key, and a use-site reference has to bracket-index it (UIcons["fi-rr-bug"]) —
        // emitting UIcons.fi-rr-bug parses as subtraction and dies with "rr is not defined".
        [TestMethod]
        public async Task NameOverrideWins_InEveryMode()
        {
            var js = await RunTest(@"
using System;
using Transpose;
[Enum(Emit.Value)]               public enum VN { [Name(""fi-rr-bug"")] TopLeft = 0 }
[Enum(Emit.Name)]                public enum NN { [Name(""fi-rr-bug"")] TopLeft = 0 }
[Enum(Emit.NameLowerCase)]       public enum LN { [Name(""fi-rr-BUG"")] TopLeft = 0 }
[Enum(Emit.StringName)]          public enum SN { [Name(""fi-rr-bug"")] TopLeft = 0 }
[Enum(Emit.StringNameUpperCase)] public enum UN { [Name(""fi-rr-bug"")] TopLeft = 0 }
public class Program
{
    public static void Main()
    {
        Console.WriteLine(""VN= "" + VN.TopLeft.ToString() + "" | "" + VN.TopLeft + "" | "" + $""{VN.TopLeft}"");
        Console.WriteLine(""NN= "" + NN.TopLeft.ToString() + "" | "" + NN.TopLeft + "" | "" + $""{NN.TopLeft}"");
        Console.WriteLine(""LN= "" + LN.TopLeft.ToString() + "" | "" + LN.TopLeft + "" | "" + $""{LN.TopLeft}"");
        Console.WriteLine(""SN= "" + SN.TopLeft.ToString() + "" | "" + SN.TopLeft + "" | "" + $""{SN.TopLeft}"");
        Console.WriteLine(""UN= "" + UN.TopLeft.ToString() + "" | "" + UN.TopLeft + "" | "" + $""{UN.TopLeft}"");
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>", skipRoslyn: true);

            Assert.IsTrue(js.Contains("VN= fi-rr-bug | fi-rr-bug | fi-rr-bug"), "Emit.Value + [Name] → the [Name] (this is Tesserae's icon contract)\n" + js);
            Assert.IsTrue(js.Contains("NN= fi-rr-bug | fi-rr-bug | fi-rr-bug"), "Emit.Name + [Name]\n" + js);
            // An explicit [Name] is taken verbatim — a casing mode does not re-case it.
            Assert.IsTrue(js.Contains("LN= fi-rr-BUG | fi-rr-BUG | fi-rr-BUG"), "a casing mode must not re-case an explicit [Name]\n" + js);
            Assert.IsTrue(js.Contains("SN= fi-rr-bug | fi-rr-bug | fi-rr-bug"), "Emit.StringName + [Name]\n" + js);
            Assert.IsTrue(js.Contains("UN= fi-rr-bug | fi-rr-bug | fi-rr-bug"), "Emit.StringNameUpperCase + [Name]\n" + js);
        }

        // A StringName* member's slot and its value are the same text, so a [Name] makes BOTH the
        // [Name] — `"fi-rr-bug": "fi-rr-bug"`. That redundant-looking pair is intended, and h5 emits
        // it identically: the slot is what ToString()/Enum.GetNames report and the value is what the
        // member evaluates to, and a string enum's whole purpose is for those to agree. It is how
        // Tesserae's UIconsWeight ([Enum(Emit.StringName)] with [Name("fi-rr-")] per member) pins the
        // icon-weight prefixes. Do not "simplify" one side away.
        //
        // In particular, the half-way shape `Regular: "fi-rr-"` — the C# member name as the slot, the
        // [Name] as the value — is NOT emitted by any mode, in transpose or in h5 (verified across all
        // nine): a [Name] always replaces the member's slot, so the pair is either `[Name]: [Name]`
        // under a StringName* mode or `[Name]: <ordinal>` under the others. That shape is exactly what
        // the Value-mode ToString regression produced, and it is what made ToString() report the C#
        // member name ("Regular") instead of the string the member stands for ("fi-rr-").
        [TestMethod]
        public void StringNameWithNameOverride_EmitsTheNameAsBothSlotAndValue()
        {
            var code = @"
using Transpose;
[Enum(Emit.StringName)] public enum Dir { [Name(""fi-rr-bug"")] TopLeft = 0 }
public class Program { public static void Main() { var x = Dir.TopLeft; } }
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed\n" +
                string.Join("\n", result.Diagnostics.Select(d => d.GetMessage())));
            var js = result.Javascript!;
            Assert.IsTrue(js.Contains("\"fi-rr-bug\": \"fi-rr-bug\""), "slot and value are both the [Name] (h5-identical)\n" + js);
            Assert.IsTrue(js.Contains("$utype: System.String"), "still a string-backed enum\n" + js);
        }

        // The [Name] a non-identifier key is read back with must be bracket-indexed, not dotted.
        [TestMethod]
        public void NameOverride_ThatIsNotAnIdentifier_IsBracketIndexed()
        {
            var code = @"
using Transpose;
[Enum(Emit.Name)] public enum Dir { [Name(""fi-rr-bug"")] TopLeft = 0 }
public class Program { public static void Main() { var x = Dir.TopLeft; } }
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed\n" +
                string.Join("\n", result.Diagnostics.Select(d => d.GetMessage())));
            var js = result.Javascript!;
            Assert.IsTrue(js.Contains("\"fi-rr-bug\": 0"), "the define quotes a non-identifier key\n" + js);
            Assert.IsTrue(js.Contains("Dir[\"fi-rr-bug\"]"), "the reference must bracket-index it\n" + js);
            Assert.IsFalse(js.Contains("Dir.fi-rr-bug"), "a dotted reference is invalid JS (parses as subtraction)\n" + js);
        }
    }
}
