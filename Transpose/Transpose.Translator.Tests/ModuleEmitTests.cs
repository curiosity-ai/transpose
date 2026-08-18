using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// <c>outputBy: Module</c> — the emitter half of lazily-loaded modules (Emitter.Modules.cs).
    ///
    /// A chunk is a strongly-connected component of the reference graph, so the chunk graph is a DAG
    /// and a chunk can pull in what it references with a side-effect <c>import</c>. Chunks reachable
    /// from the entry point are imported by the entry module; the rest are declared to
    /// <c>Transpose.Modules</c> and fetched on demand (see <see cref="LazyModuleActivatorTests"/> for
    /// the runtime side).
    /// </summary>
    [TestClass]
    public class ModuleEmitTests : TranslatorTestBase
    {
        private static Emitter.ModuleOutput Emit(
            string source,
            bool packageModules = false,
            IReadOnlyDictionary<string, string>? externalChunks = null,
            string chunkDirectory = "chunks")
        {
            var result = new RoslynTranslator().BuildAssembly(
                new[] { ("App.cs", source) }, CompilationBuilder.DefaultAssemblyName,
                extraReferencePaths: null, preprocessorSymbols: new[] { "DEBUG", "TRACE" },
                emitAssembly: false, emitModules: true,
                chunkDirectory: chunkDirectory, externalChunks: externalChunks, packageModules: packageModules);
            if (!result.Success)
                Assert.Fail("translation failed:\n" + string.Join("\n", result.Errors.Select(d => d.GetMessage())));
            return result.Modules!;
        }

        /// <summary>The chunk a type's <c>Transpose.define</c> was emitted into.</summary>
        private static string ChunkOf(Emitter.ModuleOutput m, string typeName) =>
            m.Chunks.First(c => Regex.IsMatch(c.js, @"Transpose\.definei?\(""" + Regex.Escape(typeName) + @"""")).relPath;

        private static IEnumerable<string> ImportsOf(string js) =>
            Regex.Matches(js, @"^import '\./(c\d+\.mjs)';$", RegexOptions.Multiline).Select(x => x.Groups[1].Value);

        [TestMethod]
        public void MutuallyReferencingTypesShareOneChunk()
        {
            var m = Emit(@"
public class Ping { public Pong Make() { return new Pong(); } }
public class Pong { public Ping Make() { return new Ping(); } }
public class Lonely { public int N; }
public class Program { public static void Main() { System.Console.WriteLine(new Ping().Make()); } }
");
            // Ping <-> Pong is a cycle, so no import order could satisfy both: they have to be one
            // chunk. This is the whole reason a chunk is an SCC rather than a single class.
            Assert.AreEqual(ChunkOf(m, "Ping"), ChunkOf(m, "Pong"), "Ping and Pong must share a chunk");
            Assert.AreNotEqual(ChunkOf(m, "Ping"), ChunkOf(m, "Lonely"), "an unrelated type must not be fused in");
        }

        [TestMethod]
        public void ABaseTypeIsImportedByTheChunkThatExtendsIt()
        {
            var m = Emit(@"
public class Animal { public virtual string Speak() { return ""...""; } }
public class Dog : Animal { public override string Speak() { return ""woof""; } }
public class Program { public static void Main() { System.Console.WriteLine(new Dog().Speak()); } }
");
            var baseChunk = ChunkOf(m, "Animal");
            var derived = m.Chunks.First(c => c.relPath == ChunkOf(m, "Dog"));
            Assert.AreNotEqual(baseChunk, derived.relPath, "a base and its subclass need not share a chunk");
            // Transpose.define resolves `inherits` eagerly, so the base must already be defined.
            CollectionAssert.Contains(ImportsOf(derived.js).ToList(),
                System.IO.Path.GetFileName(baseChunk), "the derived chunk must import its base's chunk");
        }

        [TestMethod]
        public void TypeofDoesNotCreateADependency()
        {
            var m = Emit(@"
using System;
public class Other { public int N; }
public class Holder { public Type What() { return typeof(Other); } }
public class Program { public static void Main() { Console.WriteLine(new Holder().What()); } }
");
            // typeof wants a Type object, which a Transpose.Modules stub already answers for. Making
            // it a dependency would fuse together every type a `see also`-style list mentions.
            var holder = m.Chunks.First(c => c.relPath == ChunkOf(m, "Holder"));
            Assert.AreNotEqual(ChunkOf(m, "Other"), holder.relPath);
            CollectionAssert.DoesNotContain(ImportsOf(holder.js).ToList(),
                System.IO.Path.GetFileName(ChunkOf(m, "Other")));
        }

        [TestMethod]
        public void TypeofAConstructedGenericDoesCreateADependency()
        {
            var m = Emit(@"
using System;
public class Box<T> { public T Value; }
public class Item { public int N; }
public class Holder { public Type What() { return typeof(Box<Item>); } }
public class Program { public static void Main() { Console.WriteLine(new Holder().What()); } }
");
            // typeof is soft because a stub object satisfies it — but a CONSTRUCTED generic has no
            // object to point at: TypeRefCore emits Box$1(Item), an application of the definition,
            // and applying a stub throws. So both the definition and the argument are real
            // dependencies, exactly as they are outside a typeof.
            var holder = m.Chunks.First(c => c.relPath == ChunkOf(m, "Holder"));
            var imports = ImportsOf(holder.js).ToList();
            CollectionAssert.Contains(imports, System.IO.Path.GetFileName(ChunkOf(m, "Box$1")),
                "typeof(Box<Item>) applies Box$1, so its chunk has to be imported");
            CollectionAssert.Contains(imports, System.IO.Path.GetFileName(ChunkOf(m, "Item")),
                "the type argument is applied to the definition, so it is a dependency too");
        }

        [TestMethod]
        public void TypeofAnUnboundGenericStaysSoft()
        {
            var m = Emit(@"
using System;
public class Box<T> { public T Value; }
public class Holder { public Type What() { return typeof(Box<>); } }
public class Program { public static void Main() { Console.WriteLine(new Holder().What()); } }
");
            // An UNBOUND typeof emits the definition object itself, never an application, so a stub
            // answers it and the soft-reference exemption still holds.
            var holder = m.Chunks.First(c => c.relPath == ChunkOf(m, "Holder"));
            Assert.AreNotEqual(ChunkOf(m, "Box$1"), holder.relPath);
            CollectionAssert.DoesNotContain(ImportsOf(holder.js).ToList(),
                System.IO.Path.GetFileName(ChunkOf(m, "Box$1")));
        }

        [TestMethod]
        public void AConstructedGenericBaseKeepsItsTypeArgumentsInTheManifest()
        {
            var m = Emit(@"
using System;
public interface IHandler<T> { void Handle(T item); }
public class Order { public int Id; }
public class OrderHandler : IHandler<Order> { public void Handle(Order item) { } }
public class Program { public static void Main() { Console.WriteLine(""hi""); } }
");
            // The stub has to report IHandler<Order>, not the bare definition IHandler$1: a
            // constructed generic is a distinct runtime object, and varianceAssignable matches on
            // $genericTypeDefinition + $typeArguments, which a definition object does not carry.
            // The array form is what the runtime applies (Modules.$resolveType).
            StringAssert.Contains(m.EntryJs, "i: [[\"IHandler$1\", \"Order\"]]");
        }

        [TestMethod]
        public void AnOpenGenericBaseFallsBackToTheDefinitionName()
        {
            var m = Emit(@"
using System;
public interface IHandler<T> { void Handle(T item); }
public class Relay<T> : IHandler<T> { public void Handle(T item) { } }
public class Program { public static void Main() { Console.WriteLine(""hi""); } }
");
            // `Relay<T> : IHandler<T>` has no argument to write down — T does not exist until the
            // definition is applied — so the manifest reports the definition-level relationship and
            // a question about one instantiation still needs the module.
            StringAssert.Contains(m.EntryJs, "i: [\"IHandler$1\"]");
        }

        [TestMethod]
        public void SkipTypeClusteringMovesAFacadesEdgesToItsCallers()
        {
            // The real shape: a facade builds the components, and the components call back into it
            // for a helper. That cycle is what fuses them — a facade nothing referenced back would
            // already be a leaf.
            const string source = @"
using System;
public class Card { public int N; public string Tag() { return Hub.Helper(); } }
public class Badge { public int N; public string Tag() { return Hub.Helper(); } }
public class Hub
{
    public static string Helper() { return ""x""; }
    public static Card MakeCard() { return new Card(); }
    public static Badge MakeBadge() { return new Badge(); }
}
public class UsesCard { public Card Get() { return Hub.MakeCard(); } }
public class Program { public static void Main() { Console.WriteLine(new UsesCard().Get()); } }
";
            var plain = Emit(source);
            Assert.AreEqual(ChunkOf(plain, "Card"), ChunkOf(plain, "Badge"),
                "a facade that builds both should fuse them into one chunk");

            var skipped = Emit(source.Replace("public class Hub", "[Transpose.SkipTypeClustering] public class Hub"));
            Assert.AreNotEqual(ChunkOf(skipped, "Card"), ChunkOf(skipped, "Badge"),
                "with the attribute the components must not be fused by the facade");

            // The dependency did not vanish: it moved to the caller, which is where the call happens.
            var caller = skipped.Chunks.First(c => c.relPath == ChunkOf(skipped, "UsesCard"));
            CollectionAssert.Contains(ImportsOf(caller.js).ToList(),
                System.IO.Path.GetFileName(ChunkOf(skipped, "Card")),
                "the caller of Hub.MakeCard() has to import Card's chunk");
            CollectionAssert.DoesNotContain(ImportsOf(caller.js).ToList(),
                System.IO.Path.GetFileName(ChunkOf(skipped, "Badge")),
                "...and only that one: it never calls MakeBadge()");
        }

        [TestMethod]
        public void SkipTypeClusteringPublishesItsMemberDepsForConsumers()
        {
            var m = Emit(@"
using System;
public class Card { public int N; }
[Transpose.SkipTypeClustering]
public class Hub { public static Card MakeCard() { return new Card(); } }
public class Program { public static void Main() { Console.WriteLine(""hi""); } }
", packageModules: true);

            // A consumer has the facade only as metadata: it can neither walk the member body nor
            // name what it builds, so the package publishes the sets keyed by the one name both
            // compilations can compute - the documentation comment id.
            var entry = m.SkipClusterDeps.FirstOrDefault(kv => kv.Key.Contains("MakeCard"));
            Assert.IsNotNull(entry.Key, "the facade member deps must be published");
            CollectionAssert.Contains(entry.Value, "Card");
        }

        [TestMethod]
        public void MetadataNamesAConstructedGenericThroughTheRuntime()
        {
            var m = Emit(@"
using System;
public class Box<T> { public T Value; }
public class Holder { public Box<string> Boxed; }
public class Program { public static void Main() { Console.WriteLine(typeof(Holder).Name); } }
");
            // Reflection metadata is emitted once for the whole assembly, outside the per-type walk,
            // so its references never take part in chunking - the type it names can sit in a chunk
            // nothing imported. Applying a stub throws, so it goes through $metaType, which answers
            // with the stub until the module arrives.
            StringAssert.Contains(m.EntryJs, "Transpose.Modules.$metaType(Box$1,");
        }

        [TestMethod]
        public void ConstructionAndStaticAccessDoCreateADependency()
        {
            var m = Emit(@"
using System;
public class Made { public int N = 7; }
public class Helper { public static int Twice(int n) { return n * 2; } }
public class UsesBoth
{
    public int Go() { return Helper.Twice(new Made().N); }
}
public class Program { public static void Main() { Console.WriteLine(new UsesBoth().Go()); } }
");
            var user = m.Chunks.First(c => c.relPath == ChunkOf(m, "UsesBoth"));
            var imports = ImportsOf(user.js).ToList();
            CollectionAssert.Contains(imports, System.IO.Path.GetFileName(ChunkOf(m, "Made")), "new Made() is a dependency");
            CollectionAssert.Contains(imports, System.IO.Path.GetFileName(ChunkOf(m, "Helper")), "a static call is a dependency");
        }

        [TestMethod]
        public void TheChunkGraphIsADagInFileOrder()
        {
            var m = Emit(@"
using System;
public interface IShape { double Area(); }
public class Square : IShape { public double S; public double Area() { return S * S; } }
public class Circle : IShape { public double R; public double Area() { return R * R * 3.14; } }
public class Pair { public Square A = new Square(); public Circle B = new Circle(); }
public class Program { public static void Main() { Console.WriteLine(new Pair().A.Area()); } }
");
            // Chunks are numbered in topological order, so every import points at a LOWER index.
            // That is what makes the side-effect imports sound and the file names deterministic.
            foreach (var (relPath, js) in m.Chunks)
            {
                var self = int.Parse(Regex.Match(relPath, @"c(\d+)\.mjs").Groups[1].Value);
                foreach (var dep in ImportsOf(js))
                {
                    var to = int.Parse(Regex.Match(dep, @"c(\d+)\.mjs").Groups[1].Value);
                    Assert.IsTrue(to < self, $"{relPath} imports {dep}, which is not earlier in the order");
                }
            }
        }

        [TestMethod]
        public void OnlyTheEntryClosureIsImportedAndTheRestIsDeclared()
        {
            var m = Emit(@"
using System;
public interface IPlugin { string Run(); }
public class Used : IPlugin { public string Run() { return ""used""; } }
public class NeverReferenced : IPlugin { public string Run() { return ""lazy""; } }
public class Program { public static void Main() { Console.WriteLine(new Used().Run()); } }
");
            Assert.IsTrue(m.LazyChunkCount > 0, "a type nothing references should have been deferred");
            StringAssert.Contains(m.EntryJs, "Transpose.Modules.register({");
            StringAssert.Contains(m.EntryJs, "\"NeverReferenced\": { m: \"./chunks/");
            // ...and its declared base list is what lets IsAssignableFrom work while it is a stub.
            StringAssert.Contains(m.EntryJs, "i: [\"IPlugin\"]");
            // The entry never imports the deferred chunk.
            var lazyChunk = System.IO.Path.GetFileName(ChunkOf(m, "NeverReferenced"));
            Assert.IsFalse(m.EntryJs.Contains($"import './chunks/{lazyChunk}'"),
                "the entry module must not statically import a deferred chunk");
            Assert.IsTrue(m.EntryJs.Contains($"import './chunks/{System.IO.Path.GetFileName(ChunkOf(m, "Used"))}'"),
                "the entry module must import what it reaches");
        }

        [TestMethod]
        public void MetadataIsEmittedBeforeTheManifest()
        {
            var m = Emit(@"
using System;
public interface IPlugin { string Run(); }
public class Deferred : IPlugin { public string Run() { return ""x""; } }
public class Program { public static void Main() { Console.WriteLine(typeof(IPlugin)); } }
");
            var meta = m.EntryJs.IndexOf("var $m = Transpose.setMetadata", StringComparison.Ordinal);
            var reg = m.EntryJs.IndexOf("Transpose.Modules.register(", StringComparison.Ordinal);
            var init = m.EntryJs.IndexOf("Transpose.init();", StringComparison.Ordinal);
            Assert.IsTrue(meta >= 0 && reg >= 0 && init >= 0, "entry module is missing one of its sections");
            // register() ends with a Transpose.init(), and init runs the entry point — so metadata
            // emitted after it would not exist yet when Main runs. This ordering is load-bearing:
            // it is what kept Tesserae's [SampleDetails] attributes readable off the stubs.
            Assert.IsTrue(meta < reg, "reflection metadata must be emitted before the manifest");
            Assert.IsTrue(reg < init, "the manifest must be registered before the final init");
        }

        [TestMethod]
        public void EveryTypeIsEmittedExactlyOnce()
        {
            var m = Emit(@"
using System;
public class A { public B B = new B(); }
public class B { public C C = new C(); }
public class C { public int N; }
public class Program { public static void Main() { Console.WriteLine(new A().B.C.N); } }
");
            var defines = m.Chunks
                .SelectMany(c => Regex.Matches(c.js, @"Transpose\.definei?\(""([^""]+)""").Select(x => x.Groups[1].Value))
                .ToList();
            CollectionAssert.AllItemsAreUnique(defines, "a type must not be emitted into two chunks");
            foreach (var t in new[] { "A", "B", "C", "Program" }) CollectionAssert.Contains(defines, t);
        }

        [TestMethod]
        public void OutputIsDeterministic()
        {
            const string src = @"
using System;
public class One { public Two T = new Two(); }
public class Two { public One O; }
public class Three { public int N; }
public class Program { public static void Main() { Console.WriteLine(new One().T); } }
";
            var a = Emit(src);
            var b = Emit(src);
            Assert.AreEqual(a.EntryJs, b.EntryJs);
            CollectionAssert.AreEqual(a.Chunks.Select(c => c.relPath).ToList(), b.Chunks.Select(c => c.relPath).ToList());
            for (var i = 0; i < a.Chunks.Count; i++) Assert.AreEqual(a.Chunks[i].js, b.Chunks[i].js);
        }

        // ---- packages -----------------------------------------------------------------------

        [TestMethod]
        public void ALibraryDefersEverythingItDoesNotHaveToRun()
        {
            var m = Emit(@"
public class Widget { public int N; }
public class Gadget { public Widget W = new Widget(); }
", packageModules: true);

            // A library has no entry point to be lazy relative to, so nothing is eager: its consumer's
            // chunks import what they actually use, and the rest waits to be asked for. Making it all
            // eager instead would produce chunk files that always load, which is strictly worse than
            // one bundle.
            Assert.AreEqual(0, m.EagerChunkCount);
            Assert.AreEqual(m.Chunks.Count, m.LazyChunkCount);
            StringAssert.Contains(m.EntryJs, "Transpose.Modules.register({");
            Assert.IsFalse(m.EntryJs.Contains("import './chunks/"), "a library entry imports nothing");
        }

        [TestMethod]
        public void ALibraryKeepsItsReadyHandlersEager()
        {
            var m = Emit(@"
public class Widget { public int N; }
public static class Boot
{
    [Transpose.Ready]
    public static void Start() { System.Console.WriteLine(""up""); }
}
", packageModules: true);

            // A [Ready] handler runs on load, so it cannot be deferred — its chunk (and whatever that
            // chunk needs) is the library's eager set.
            Assert.IsTrue(m.EagerChunkCount >= 1, "the [Ready] handler's chunk must load up front");
            StringAssert.Contains(m.EntryJs, "import './chunks/");
            StringAssert.Contains(m.EntryJs, "Transpose.ready(");
        }

        [TestMethod]
        public void EveryEmittedTypeIsInThePublishedChunkMap()
        {
            var m = Emit(@"
public class Alpha { public int N; }
public class Beta { public Alpha A = new Alpha(); }
public class Program { public static void Main() { System.Console.WriteLine(new Beta().A.N); } }
");
            // This map is what a package embeds for its consumers; a type missing from it would make
            // the consumer emit no import and hit the library's stub at runtime.
            foreach (var t in new[] { "Alpha", "Beta", "Program" })
            {
                Assert.IsTrue(m.TypeToChunk.ContainsKey(t), $"{t} is missing from the chunk map");
                Assert.IsTrue(m.Chunks.Any(c => c.relPath == m.TypeToChunk[t]), $"{t} maps to a chunk that was not emitted");
            }
        }

        /// <summary>The specifier a chunk uses to reach another chunk, possibly in a different
        /// assembly's folder. <see cref="ModulePackageTests"/> covers the cross-assembly protocol
        /// end to end; this pins the path arithmetic it depends on.</summary>
        [TestMethod]
        public void RelativeImportWalksBetweenChunkFolders()
        {
            Assert.AreEqual("./c2.mjs", Emitter.RelativeImport("chunks/app/c1.mjs", "chunks/app/c2.mjs"));
            Assert.AreEqual("../lib/c9.mjs", Emitter.RelativeImport("chunks/app/c1.mjs", "chunks/lib/c9.mjs"));
            Assert.AreEqual("./chunks/app/c1.mjs", Emitter.RelativeImport("app.js", "chunks/app/c1.mjs"));
            Assert.AreEqual("../../top.mjs", Emitter.RelativeImport("chunks/app/c1.mjs", "top.mjs"));
        }

        [TestMethod]
        public async Task TheEmittedChunksRunAsync()
        {
            const string src = @"
using System;
public abstract class Shape { public abstract double Area(); }
public class Square : Shape { public double S = 3; public override double Area() { return S * S; } }
public class Program
{
    public static void Main()
    {
        Shape s = new Square();
        Console.WriteLine(s.Area());
        Console.WriteLine(typeof(Square).Name);
    }
}
";
            var m = Emit(src);

            // Chunk indices are a topological order, so concatenating them in that order — with the
            // side-effect imports stripped — reproduces exactly the evaluation order ES modules
            // would give. That lets the emitted chunks run on plain Node.
            var flat = string.Join("\n", m.Chunks.Select(c => Regex.Replace(c.js, @"^import '[^']+';$", "", RegexOptions.Multiline)));
            var entry = Regex.Replace(m.EntryJs, @"^import '[^']+';$", "", RegexOptions.Multiline);
            var full = RoslynTranslator.LoadRuntime() + "\n" + flat + "\n" + entry;

            var output = (await NodeJsRunner.RunAsync(full)).Trim();
            StringAssert.Contains(output, "9");
            StringAssert.Contains(output, "Square");
        }
    }
}
