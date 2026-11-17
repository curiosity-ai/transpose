using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// An explicit <c>[Convention]</c> on a compiled (non-[External]) type renames its properties,
    /// fields and events the same way in the declaring compilation as in a project that references
    /// the compiled DLL. The declaring compilation used to keep the verbatim C# name while a consumer
    /// applied the convention, so one property had two JS names: a library assigned
    /// <c>options.Layers.Skin</c> while the app built <c>{ background: ... }</c> on a sibling
    /// [ObjectLiteral], and the resulting object mixed both spellings.
    /// </summary>
    [TestClass]
    public class SourceConventionNamingTests : TranslatorTestBase
    {
        private const string Types = @"
using Transpose;

namespace Lib
{
    [ObjectLiteral, Convention(Notation.CamelCase)]
    public class LayerVisibility
    {
        public bool? Skin { get; set; }
        public string Background { get; set; }
    }

    [Convention(Notation.CamelCase)]
    public class Settings
    {
        public string Background { get; set; }
        public int Count;
        public LayerVisibility Layers { get; set; }
    }

    public class MemberLevel
    {
        [Convention(Notation.CamelCase)]
        public string Title { get; set; }
        public string Kept { get; set; }
    }

    public class Plain
    {
        public string Verbatim { get; set; }
    }

    // The convention would move Count onto the `count` field's slot, so Count keeps its C# name.
    [Convention(Notation.CamelCase)]
    public class Counter
    {
        private int count;
        public int Count => count * 10;
        public Counter(int c) { count = c; }
    }

    public static class Writer
    {
        public static string Run()
        {
            var s = new Settings { Background = ""#fff"", Count = 2, Layers = new LayerVisibility { Skin = true } };
            s.Layers.Skin = false;
            s.Background = s.Background + ""!"";
            s.Count += 1;
            var m = new MemberLevel { Title = ""t"", Kept = ""k"" };
            var p = new Plain { Verbatim = ""v"" };
            return s.Background + s.Count + (s.Layers.Skin == false ? ""off"" : ""on"") + m.Title + m.Kept + p.Verbatim + new Counter(4).Count;
        }
    }
}
";

        private const string Program = @"
public class Program
{
    public static void Main()
    {
        System.Console.WriteLine(Lib.Writer.Run());
    }
}
";

        [TestMethod]
        public void ConventionAppliesInTheDeclaringCompilation()
        {
            var result = new RoslynTranslator().Translate(new[] { ("Types.cs", Types), ("Program.cs", Program) });
            Assert.IsTrue(result.Success, string.Join("\n", result.Errors));
            var js = result.Javascript!;

            AssertCamelCase(js);
            Assert.IsTrue(HasSlot(js, "title"), "a member-level [Convention] applies in source\n" + js);
            Assert.IsTrue(HasSlot(js, "Kept") && !HasSlot(js, "kept"), "a member with no convention keeps its name\n" + js);
            Assert.IsTrue(HasSlot(js, "Verbatim") && !HasSlot(js, "verbatim"), "a type with no convention keeps its names\n" + js);
        }

        [TestMethod]
        public async System.Threading.Tasks.Task ConventionNamedMembersReadBackWhatWasWritten()
        {
            // JS-only: native .NET has no Transpose attributes. Every write must be read back from the
            // same slot, so a getter and a setter that disagree on the name would print undefined.
            var output = await RunTest(Types + Program, skipRoslyn: true);
            Assert.AreEqual("#fff!3offtkv40", output.Trim());
        }

        /// <summary>
        /// The library and a project that references its DLL must agree on every name: the consumer
        /// initializes and reads the library's types, and the library's own JS reads what it wrote.
        /// </summary>
        [TestMethod]
        public void LibraryAndConsumerAgreeOnConventionNames()
        {
            var root = Path.Combine(Path.GetTempPath(), "tps-srcconv-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var lib = new RoslynTranslator().BuildAssembly(
                    new[] { ("Types.cs", Types) }, "ConvLib", Array.Empty<string>(),
                    emitAssembly: true, emitDebugInformation: false);
                Assert.IsTrue(lib.Success, string.Join("\n", lib.Diagnostics.Select(d => d.GetMessage())));
                var dll = Path.Combine(root, "ConvLib.dll");
                File.WriteAllBytes(dll, lib.AssemblyBytes!);

                const string consumer = @"
public class Program
{
    public static void Main()
    {
        var s = new Lib.Settings { Background = ""#000"", Layers = new Lib.LayerVisibility { Skin = true, Background = ""#111"" } };
        s.Layers.Skin = null;
        s.Count = 3;
        var m = new Lib.MemberLevel { Title = ""t"" };
        var p = new Lib.Plain { Verbatim = ""v"" };
        var c = new Lib.Counter(4);
        System.Console.WriteLine(s.Background + s.Count + m.Title + m.Kept + p.Verbatim + c.Count);
    }
}";
                var app = new RoslynTranslator().Translate(new[] { ("App.cs", consumer) }, "ConvApp", new[] { dll });
                Assert.IsTrue(app.Success, string.Join("\n", app.Errors));

                AssertCamelCase(lib.Javascript!);
                AssertCamelCase(app.Javascript!);
                foreach (var js in new[] { lib.Javascript!, app.Javascript! })
                {
                    Assert.IsTrue(HasSlot(js, "title"), "member-level [Convention]\n" + js);
                    Assert.IsTrue(HasSlot(js, "Verbatim"), "no convention keeps the name\n" + js);
                    Assert.IsTrue(HasSlot(js, "Count"), "a convention name taken by a field keeps the C# name\n" + js);
                    Assert.IsTrue(HasSlot(js, "count"), "Settings.Count (no clash) is still camelCased\n" + js);
                }
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }

        /// <summary>The name is used as a JS slot: a member access (<c>x.name</c>) or an object key
        /// (<c>name: ...</c> in fields/props/initializers). Reflection metadata quotes the C# name
        /// (<c>"n":"Background"</c>) and is not a slot.</summary>
        private static bool HasSlot(string js, string name)
            => Regex.IsMatch(js, $@"\.{name}\b|(?<![""\w$]){name}\s*:");

        private static void AssertCamelCase(string js)
        {
            Assert.IsTrue(HasSlot(js, "background"), "Background must emit as background\n" + js);
            Assert.IsTrue(HasSlot(js, "skin"), "Skin must emit as skin\n" + js);
            Assert.IsFalse(HasSlot(js, "Background"), "no verbatim Background next to background\n" + js);
            Assert.IsFalse(HasSlot(js, "Skin"), "no verbatim Skin next to skin\n" + js);
            Assert.IsFalse(HasSlot(js, "Title"), "no verbatim Title next to title\n" + js);
        }
    }
}
