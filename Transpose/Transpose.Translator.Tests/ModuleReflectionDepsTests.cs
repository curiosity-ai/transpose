using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// The dependency a reflection-driven activator creates, which nothing in the emitted code shows
    /// — <c>[ConstructsTypeArguments]</c> and <c>[NeverDefer]</c> (Emitter.ReflectionDeps.cs).
    ///
    /// The chunker records an edge exactly when a reference is emitted, so a type stays loadable
    /// because some code names it. <c>JsonConvert.DeserializeObject&lt;Order&gt;(json)</c> emits
    /// nothing about <c>Order</c> that a stub cannot answer, then constructs it and every member type
    /// below it from the metadata — and constructing a stub throws.
    ///
    /// These snippets apply attributes the BCL only carries once it is rebuilt from this tree, so
    /// they need a <c>TRANSPOSE_DLL_PATH</c> pointing at a locally built <c>Transpose.dll</c> — the
    /// same thing the runtime-behaviour tests need, and what CI sets.
    /// </summary>
    [TestClass]
    public class ModuleReflectionDepsTests
    {
        private const string Dtos = @"
public class Country { public string Name; }
public class Address { public string City; public Country Where; }
public class Line    { public string Sku; }
public class Order   { public Address Ship; public System.Collections.Generic.List<Line> Lines; }
public class Nobody  { public int N; }
";

        /// <summary>A stand-in for a binding library's deserializer: it hands back a T it built from
        /// T's metadata, so nothing it emits mentions T's members.</summary>
        private const string Activator = @"
public static class Wire
{
    public static T Read<T>(string json) { return default(T); }
    public static string Write<T>(T value) { return """"; }
}
";

        private static IEnumerable<string> ImportsOf(string js) =>
            Regex.Matches(js, @"^import '\./(c\d+\.mjs)';$", RegexOptions.Multiline).Select(x => x.Groups[1].Value);

        /// <summary>Everything the chunk holding <paramref name="typeName"/> pulls in, transitively.</summary>
        private static HashSet<string> ChunkClosure(Emitter.ModuleOutput m, string typeName)
        {
            var start = System.IO.Path.GetFileName(ModuleEmitTests.ChunkOf(m, typeName));
            var seen = new HashSet<string> { start };
            var stack = new Stack<string>(new[] { start });
            while (stack.Count > 0)
            {
                var file = stack.Pop();
                var js = m.Chunks.First(c => c.relPath.EndsWith(file)).js;
                foreach (var i in ImportsOf(js)) if (seen.Add(i)) stack.Push(i);
            }
            return seen;
        }

        private static bool Reaches(Emitter.ModuleOutput m, string from, string to) =>
            ChunkClosure(m, from).Contains(System.IO.Path.GetFileName(ModuleEmitTests.ChunkOf(m, to)));

        private static string Program(string marker) => Dtos + Activator.Replace("public static T Read<T>", marker + " public static T Read<T>") + @"
public class Screen  { public Order Load(string j) { return Wire.Read<Order>(j); } }
public class Program { public static void Main() { System.Console.WriteLine(new Screen().Load(""{}"")); } }
";

        [TestMethod]
        public void WithoutTheAttributeAnActivatedTypesMembersAreNotReached()
        {
            // The baseline the attribute exists to fix. `Read<Order>` names Order — a Type object,
            // which a stub answers — and nothing names Address, Country or Line at all, so the
            // deserializer's `new Address()` would hit a stub.
            var m = ModuleEmitTests.Emit(Program(""));
            Assert.IsFalse(Reaches(m, "Screen", "Address"),
                "nothing emitted mentions Address, so its chunk should not be reachable from the caller");
            Assert.IsFalse(Reaches(m, "Screen", "Line"));
        }

        [TestMethod]
        public void ConstructsTypeArgumentsPullsTheWholeMemberGraphIntoTheCallersChunk()
        {
            var m = ModuleEmitTests.Emit(Program("[Transpose.ConstructsTypeArguments]"));

            // The type argument itself...
            Assert.IsTrue(Reaches(m, "Screen", "Order"), "the type argument's chunk must be imported");
            // ...its member types, recursively...
            Assert.IsTrue(Reaches(m, "Screen", "Address"), "a member type is constructed too");
            Assert.IsTrue(Reaches(m, "Screen", "Country"), "the walk is transitive");
            // ...and through a collection: the member is declared List<Line>, and Line is what gets built.
            Assert.IsTrue(Reaches(m, "Screen", "Line"), "a generic argument of a member type counts");
        }

        [TestMethod]
        public void AnUnrelatedTypeIsNotDraggedIn()
        {
            var m = ModuleEmitTests.Emit(Program("[Transpose.ConstructsTypeArguments]"));
            Assert.IsFalse(Reaches(m, "Screen", "Nobody"),
                "the walk follows the activated type's members, not the whole assembly");
        }

        [TestMethod]
        public void TheAttributeOnTheContainingTypeCoversEveryGenericMember()
        {
            var source = Dtos + "[Transpose.ConstructsTypeArguments]" + Activator + @"
public class Screen  { public Order Load(string j) { return Wire.Read<Order>(j); } }
public class Program { public static void Main() { System.Console.WriteLine(new Screen().Load(""{}"")); } }
";
            var m = ModuleEmitTests.Emit(source);
            Assert.IsTrue(Reaches(m, "Screen", "Address"),
                "a binding library should be able to mark the activator class rather than each overload");
        }

        [TestMethod]
        public void ANonGenericCallIsUnaffected()
        {
            // Nothing to record: the type is a runtime value, not a type argument. This is the case
            // [NeverDefer] exists for.
            var m = ModuleEmitTests.Emit(Dtos + @"
public static class Wire { [Transpose.ConstructsTypeArguments] public static object Read(string json, System.Type t) { return null; } }
public class Screen  { public object Load(string j) { return Wire.Read(j, typeof(Order)); } }
public class Program { public static void Main() { System.Console.WriteLine(new Screen().Load(""{}"")); } }
");
            Assert.IsFalse(Reaches(m, "Screen", "Address"),
                "a Type-valued argument writes nothing down for the compiler to follow");
        }

        [TestMethod]
        public void AnActivatorInAReferencedAssemblyIsHonoured()
        {
            // The shape that matters in practice: the activator is JsonConvert, compiled into a
            // package, and the attribute is read back out of its metadata rather than its source.
            var library = Path.Combine(Path.GetTempPath(), "tps-activator-" + Guid.NewGuid().ToString("N") + ".dll");
            try
            {
                var built = new RoslynTranslator().BuildAssembly(
                    new[] { ("Lib.cs", @"
namespace Wiring
{
    public static class Wire
    {
        [Transpose.ConstructsTypeArguments]
        public static T Read<T>(string json) { return default(T); }
    }
}") },
                    "Wiring", extraReferencePaths: null, preprocessorSymbols: new[] { "DEBUG", "TRACE" },
                    emitAssembly: true);
                Assert.IsTrue(built.Success, string.Join("\n", built.Errors.Select(d => d.GetMessage())));
                File.WriteAllBytes(library, built.AssemblyBytes!);

                var app = new RoslynTranslator().BuildAssembly(
                    new[] { ("App.cs", Dtos + @"
public class Screen  { public Order Load(string j) { return Wiring.Wire.Read<Order>(j); } }
public class Program { public static void Main() { System.Console.WriteLine(new Screen().Load(""{}"")); } }
") },
                    CompilationBuilder.DefaultAssemblyName, extraReferencePaths: new[] { library },
                    preprocessorSymbols: new[] { "DEBUG", "TRACE" },
                    emitAssembly: false, emitModules: true, minChunkBytes: 0, maxChunkBytes: 0);
                Assert.IsTrue(app.Success, string.Join("\n", app.Errors.Select(d => d.GetMessage())));

                var m = app.Modules!;
                Assert.IsTrue(Reaches(m, "Screen", "Order"), "the type argument's chunk must be imported");
                Assert.IsTrue(Reaches(m, "Screen", "Country"), "and the graph below it");
                Assert.IsFalse(Reaches(m, "Screen", "Nobody"));
            }
            finally
            {
                try { if (File.Exists(library)) File.Delete(library); } catch { }
            }
        }

        [TestMethod]
        public void AnAssemblyCanDeclareAnActivatorItDoesNotOwn()
        {
            // The escape hatch for a library that cannot be edited, or has not been re-released with
            // the annotation: name the activator type from the calling assembly. It applies to every
            // generic method of that type, and only to calls made from here.
            var source = "[assembly: Transpose.ConstructsTypeArguments(typeof(Wire))]" + Dtos + Activator + @"
public class Screen  { public Order Load(string j) { return Wire.Read<Order>(j); } }
public class Program { public static void Main() { System.Console.WriteLine(new Screen().Load(""{}"")); } }
";
            var m = ModuleEmitTests.Emit(source);
            Assert.IsTrue(Reaches(m, "Screen", "Order"));
            Assert.IsTrue(Reaches(m, "Screen", "Country"), "the walk is the same as for the marked method");
            Assert.IsFalse(Reaches(m, "Screen", "Nobody"));
        }

        [TestMethod]
        public void DeclaringOneActivatorDoesNotMarkAnother()
        {
            var source = "[assembly: Transpose.ConstructsTypeArguments(typeof(Elsewhere))]" + Dtos + Activator + @"
public static class Elsewhere { public static T Read<T>(string json) { return default(T); } }
public class Screen  { public Order Load(string j) { return Wire.Read<Order>(j); } }
public class Program { public static void Main() { System.Console.WriteLine(new Screen().Load(""{}"")); } }
";
            var m = ModuleEmitTests.Emit(source);
            Assert.IsFalse(Reaches(m, "Screen", "Address"),
                "the declaration names one activator type, not every generic method in the project");
        }

        [TestMethod]
        public void SerializingTheSameTypeIsNotAffected()
        {
            // Serialization reads an instance it was handed; it never constructs the type, so there
            // is no hidden edge and marking it would only inflate what a screen fetches.
            var m = ModuleEmitTests.Emit(Dtos + Activator.Replace("public static T Read<T>",
                "[Transpose.ConstructsTypeArguments] public static T Read<T>") + @"
public class Screen  { public string Save(Order o) { return Wire.Write<Order>(o); } }
public class Program { public static void Main() { System.Console.WriteLine(new Screen().Save(null)); } }
");
            Assert.IsFalse(Reaches(m, "Screen", "Country"),
                "Write<T> is not marked, so it pulls in nothing beyond what the emitted code names");
        }

        [TestMethod]
        public void NeverDeferKeepsATypeOutOfTheDeferredManifest()
        {
            const string source = @"
public class Settings { public string Theme; }
public class Program { public static void Main() { System.Console.WriteLine(1); } }
";
            var deferred = ModuleEmitTests.Emit(source);
            StringAssert.Contains(deferred.EntryJs, "\"Settings\": { m: ",
                "nothing references Settings, so it is deferred — that is the default this attribute overrides");

            var eager = ModuleEmitTests.Emit(source.Replace("public class Settings",
                "[Transpose.NeverDefer] public class Settings"));
            Assert.IsFalse(eager.EntryJs.Contains("\"Settings\": { m: "),
                "[NeverDefer] must keep the type out of the stub manifest");
            Assert.AreEqual(0, eager.LazyChunkCount, "its chunk has to be one the entry module imports");
        }

        [TestMethod]
        public void NeverDeferAlsoPullsInWhatTheTypeItselfNeeds()
        {
            var m = ModuleEmitTests.Emit(@"
public class Palette { public string Accent; }
public class Settings { public Palette Colors = new Palette(); }
public class Program { public static void Main() { System.Console.WriteLine(1); } }
".Replace("public class Settings", "[Transpose.NeverDefer] public class Settings"));

            // The eager set is closed over chunk dependencies, so a [NeverDefer] root brings its own
            // references with it — the same closure the entry point gets.
            Assert.IsFalse(m.EntryJs.Contains("\"Palette\": { m: "),
                "a [NeverDefer] type's own dependencies have to be loaded with it");
        }
    }
}
