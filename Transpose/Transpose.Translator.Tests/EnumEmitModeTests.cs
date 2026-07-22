using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Validates every <c>[Enum(Emit.X)]</c> style against the emitted JavaScript, matching the
    /// nine H5 modes: 1 Name, 2 Value, 3 StringName, 4 StringNamePreserveCase, 5 StringNameLowerCase,
    /// 6 StringNameUpperCase, 7 NamePreserveCase, 8 NameLowerCase, 9 NameUpperCase.
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
            Assert.IsTrue(js.Contains("var x = 0"), "Emit.Value should inline the ordinal\n" + js);
        }

        [TestMethod]
        public void Name_PreservesMemberNameAndReferencesEnumObject()
        {
            var js = Emit("Name");
            Assert.IsTrue(js.Contains("TopLeft: 0"), "define key preserves case\n" + js);
            Assert.IsTrue(js.Contains("Dir.TopLeft"), "reference uses the enum object member\n" + js);
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

        [TestMethod]
        public void StringName_CamelCasesFirstLetterAsStringValue()
        {
            var js = Emit("StringName");
            Assert.IsTrue(js.Contains("\"topLeft\""), "member value is camelCase string\n" + js);
            Assert.IsTrue(js.Contains("$utype: System.String"), "string-backed enum declares $utype\n" + js);
        }

        [TestMethod]
        public void StringNamePreserveCase_KeepsCaseAsStringValue()
        {
            var js = Emit("StringNamePreserveCase");
            Assert.IsTrue(js.Contains("\"TopLeft\""), "member value preserves case as string\n" + js);
            Assert.IsTrue(js.Contains("$utype: System.String"), "string-backed enum declares $utype\n" + js);
        }

        [TestMethod]
        public void StringNameLowerCase_LowercasesStringValue()
        {
            var js = Emit("StringNameLowerCase");
            Assert.IsTrue(js.Contains("\"topleft\""), "member value is lowercase string\n" + js);
        }

        [TestMethod]
        public void StringNameUpperCase_UppercasesStringValue()
        {
            var js = Emit("StringNameUpperCase");
            Assert.IsTrue(js.Contains("\"TOPLEFT\""), "member value is uppercase string\n" + js);
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
    }
}
