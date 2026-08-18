using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// The lazily-loaded module surface: <c>Transpose.Modules</c> (Resources/Modules.js) and
    /// <c>Activator.CreateInstanceAsync</c>.
    ///
    /// A build that splits its output into JavaScript modules leaves the types it deferred as
    /// *stubs*: registered at the same global path and in the same assembly <c>$types</c> map a real
    /// <c>Transpose.define</c> would use, so reflection still enumerates and tests them, but with no
    /// constructor behind them. Fetching the module is asynchronous, so instantiating such a type
    /// cannot go through the synchronous <c>Activator.CreateInstance</c> — that throws and names the
    /// module — and goes through <c>CreateInstanceAsync</c> instead.
    ///
    /// Every test runs with <c>skipRoslyn: true</c>: <c>CreateInstanceAsync</c> and
    /// <c>Transpose.Modules</c> have no native .NET counterpart to diff against, so the assertions
    /// are on the JS output. The loader is replaced with one that defines the type from C#, which
    /// keeps the tests free of a real network fetch or a real ESM import.
    /// </summary>
    [TestClass]
    public class LazyModuleActivatorTests : TranslatorTestBase
    {
        /// <summary>Shared preamble: a real interface + a real type, then a manifest declaring a
        /// *second* type that only exists inside "chunk-1.mjs", with a loader that defines it.</summary>
        private const string Preamble = @"
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Transpose;

public interface IWidget { string Describe(); }

public class EagerWidget : IWidget
{
    public string Describe() => ""eager"";
}

public static class Chunk
{
    public static int LoadCount;

    // Stands in for a module the build deferred: registering the manifest creates a stub, and the
    // loader is what a real build would satisfy with a dynamic import() of the chunk file.
    public static void Register()
    {
        Modules.Register(Script.Write<object>(
            @""{ 'LazyWidget': { m: 'chunk-1.mjs', k: 'class', a: 'App', i: ['IWidget'] } }""));
    }

    public static void InstallLoader()
    {
        Modules.SetLoader(url =>
        {
            LoadCount++;
            // What the fetched chunk would have executed.
            Script.Write(@""Transpose.define('LazyWidget', { inherits: [IWidget], alias: ['Describe', 'IWidget$Describe'], methods: { Describe: function () { return 'lazy'; } } });"");
            return Task.CompletedTask;
        });
    }
}
";

        [TestMethod]
        public async Task StubIsVisibleToReflectionBeforeItsModuleLoadsAsync()
        {
            var output = await RunTest(Preamble + @"
public class Program
{
    public static void Main()
    {
        Chunk.Register();

        var t = Type.GetType(""LazyWidget"");
        Console.WriteLine(""found: "" + (t != null));
        Console.WriteLine(""name: "" + t.Name);
        Console.WriteLine(""isInterface: "" + t.IsInterface);
        Console.WriteLine(""assignable: "" + typeof(IWidget).IsAssignableFrom(t));
        Console.WriteLine(""isLoaded: "" + Modules.IsLoaded(t));
        Console.WriteLine(""isStub: "" + Modules.IsStub(t));
        Console.WriteLine(""<<DONE>>"");
    }
}
", skipRoslyn: true);

            StringAssert.Contains(output, "found: True");
            StringAssert.Contains(output, "name: LazyWidget");
            StringAssert.Contains(output, "isInterface: False");
            // The point of a stub: an interface scan finds the type while its code is absent.
            StringAssert.Contains(output, "assignable: True");
            StringAssert.Contains(output, "isLoaded: False");
            StringAssert.Contains(output, "isStub: True");
            StringAssert.Contains(output, "<<DONE>>");
        }

        [TestMethod]
        public async Task AssemblyGetTypesSeesAStubbedTypeAsync()
        {
            var output = await RunTest(Preamble + @"
public class Program
{
    public static void Main()
    {
        var before = typeof(EagerWidget).Assembly.GetTypes()
            .Count(x => typeof(IWidget).IsAssignableFrom(x) && !x.IsInterface);
        Chunk.Register();
        var after = typeof(EagerWidget).Assembly.GetTypes()
            .Count(x => typeof(IWidget).IsAssignableFrom(x) && !x.IsInterface);

        Console.WriteLine(""before: "" + before);
        Console.WriteLine(""after: "" + after);
        Console.WriteLine(""<<DONE>>"");
    }
}
", skipRoslyn: true);

            // This is exactly how Tesserae's sample gallery discovers its samples, and the reason a
            // module split must not hide a deferred type from Assembly.GetTypes().
            StringAssert.Contains(output, "before: 1");
            StringAssert.Contains(output, "after: 2");
        }

        [TestMethod]
        public async Task SynchronousCreateInstanceOnAStubThrowsNamingTheModuleAsync()
        {
            var output = await RunTest(Preamble + @"
public class Program
{
    public static void Main()
    {
        Chunk.Register();
        var t = Type.GetType(""LazyWidget"");
        try
        {
            Activator.CreateInstance(t);
            Console.WriteLine(""no throw"");
        }
        catch (Exception ex)
        {
            Console.WriteLine(""threw: "" + ex.Message);
        }
        Console.WriteLine(""<<DONE>>"");
    }
}
", skipRoslyn: true);

            // A silent or obscure failure here is the trap: the sync path must say what is wrong.
            StringAssert.Contains(output, "threw: ");
            StringAssert.Contains(output, "LazyWidget");
            StringAssert.Contains(output, "chunk-1.mjs");
            StringAssert.Contains(output, "<<DONE>>");
        }

        [TestMethod]
        public async Task CreateInstanceAsyncLoadsTheModuleAndConstructsAsync()
        {
            var output = await RunTest(Preamble + @"
public class Program
{
    public static async Task Main()
    {
        Chunk.Register();
        Chunk.InstallLoader();

        var t = Type.GetType(""LazyWidget"");
        Console.WriteLine(""before isLoaded: "" + Modules.IsLoaded(t));

        var instance = await Activator.CreateInstanceAsync(t);
        Console.WriteLine(""loads: "" + Chunk.LoadCount);
        Console.WriteLine(""instance null: "" + (instance == null));
        Console.WriteLine(""describe: "" + ((IWidget)instance).Describe());
        Console.WriteLine(""after isLoaded: "" + Modules.IsLoaded(Type.GetType(""LazyWidget"")));
        Console.WriteLine(""<<DONE>>"");
    }
}
", skipRoslyn: true);

            StringAssert.Contains(output, "before isLoaded: False");
            StringAssert.Contains(output, "loads: 1");
            StringAssert.Contains(output, "instance null: False");
            // The real class replaced the stub, and the instance behaves like the real type.
            StringAssert.Contains(output, "describe: lazy");
            StringAssert.Contains(output, "after isLoaded: True");
            StringAssert.Contains(output, "<<DONE>>");
        }

        [TestMethod]
        public async Task LoadAsyncIsIdempotentAndFetchesOnceAsync()
        {
            var output = await RunTest(Preamble + @"
public class Program
{
    public static async Task Main()
    {
        Chunk.Register();
        Chunk.InstallLoader();

        var t = Type.GetType(""LazyWidget"");
        var a = await Modules.LoadAsync(t);
        var b = await Modules.LoadAsync(t);
        var c = await Modules.LoadAsync(""LazyWidget"");

        Console.WriteLine(""loads: "" + Chunk.LoadCount);
        Console.WriteLine(""same: "" + (a == b && b == c));
        Console.WriteLine(""stub now: "" + Modules.IsStub(a));

        // Awaiting a type that was never deferred is a no-op, so a call site can await
        // unconditionally without knowing whether the type was split out.
        var eager = await Modules.LoadAsync(typeof(EagerWidget));
        Console.WriteLine(""eager: "" + (eager == typeof(EagerWidget)));
        Console.WriteLine(""<<DONE>>"");
    }
}
", skipRoslyn: true);

            StringAssert.Contains(output, "loads: 1");
            StringAssert.Contains(output, "same: True");
            StringAssert.Contains(output, "stub now: False");
            StringAssert.Contains(output, "eager: True");
            StringAssert.Contains(output, "<<DONE>>");
        }

        [TestMethod]
        public async Task CreateInstanceAsyncOnAnAlreadyLoadedTypeJustConstructsAsync()
        {
            var output = await RunTest(Preamble + @"
public class Program
{
    public static async Task Main()
    {
        Chunk.InstallLoader();
        var instance = await Activator.CreateInstanceAsync(typeof(EagerWidget));
        Console.WriteLine(""describe: "" + ((IWidget)instance).Describe());
        Console.WriteLine(""loads: "" + Chunk.LoadCount);

        var generic = await Activator.CreateInstanceAsync<EagerWidget>();
        Console.WriteLine(""generic: "" + generic.Describe());
        Console.WriteLine(""<<DONE>>"");
    }
}
", skipRoslyn: true);

            StringAssert.Contains(output, "describe: eager");
            // No module was registered for it, so nothing was fetched.
            StringAssert.Contains(output, "loads: 0");
            StringAssert.Contains(output, "generic: eager");
            StringAssert.Contains(output, "<<DONE>>");
        }

        [TestMethod]
        public async Task AFailedLoadRestoresTheStubAndCanBeRetriedAsync()
        {
            var output = await RunTest(Preamble + @"
public class Program
{
    public static async Task Main()
    {
        Chunk.Register();

        var attempts = 0;
        Modules.SetLoader(url =>
        {
            attempts++;
            if (attempts == 1) throw new InvalidOperationException(""network down"");
            Script.Write(@""Transpose.define('LazyWidget', { inherits: [IWidget], alias: ['Describe', 'IWidget$Describe'], methods: { Describe: function () { return 'lazy'; } } });"");
            return Task.CompletedTask;
        });

        try
        {
            await Activator.CreateInstanceAsync(Type.GetType(""LazyWidget""));
            Console.WriteLine(""no throw"");
        }
        catch (Exception ex)
        {
            Console.WriteLine(""first threw: "" + ex.Message);
        }

        // The failure must not have erased the type: reflection still sees the stub, and a retry
        // is allowed rather than being memoised as permanently failed.
        var t = Type.GetType(""LazyWidget"");
        Console.WriteLine(""still found: "" + (t != null));
        Console.WriteLine(""still stub: "" + Modules.IsStub(t));

        var instance = await Activator.CreateInstanceAsync(t);
        Console.WriteLine(""retry describe: "" + ((IWidget)instance).Describe());
        Console.WriteLine(""attempts: "" + attempts);
        Console.WriteLine(""<<DONE>>"");
    }
}
", skipRoslyn: true);

            StringAssert.Contains(output, "first threw: ");
            StringAssert.Contains(output, "still found: True");
            StringAssert.Contains(output, "still stub: True");
            StringAssert.Contains(output, "retry describe: lazy");
            StringAssert.Contains(output, "attempts: 2");
            StringAssert.Contains(output, "<<DONE>>");
        }

        /// <summary>A deferred type whose base class AND interface are CONSTRUCTED generics — the
        /// manifest carries them as [definition, ...arguments] rather than a flattened name, because
        /// a constructed generic is a distinct runtime object built by applying the definition.</summary>
        private const string GenericPreamble = @"
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Transpose;

public interface IHandler<T> { void Handle(T item); }
public class Order { public int Id; }
public class Repo<T> { public T Get() { return default(T); } }

public class EagerHandler : IHandler<Order> { public void Handle(Order item) { } }

public static class GenericChunk
{
    // What a module build emits for a deferred `class LazyHandler : Repo<Order>, IHandler<Order>`.
    public static void Register()
    {
        Modules.Register(Script.Write<object>(
            @""{ 'LazyHandler': { m: 'chunk-2.mjs', k: 'class', a: 'App', i: [['Repo$1', 'Order'], ['IHandler$1', 'Order']] } }""));
    }
}
";

        [TestMethod]
        public async Task IsAssignableFromAConstructedGenericAnswersFromAStubAsync()
        {
            var output = await RunTest(GenericPreamble + @"
public class Program
{
    public static void Main()
    {
        GenericChunk.Register();
        var t = Type.GetType(""LazyHandler"");

        Console.WriteLine(""iface: "" + typeof(IHandler<Order>).IsAssignableFrom(t));
        Console.WriteLine(""baseClass: "" + typeof(Repo<Order>).IsAssignableFrom(t));
        Console.WriteLine(""ifaceCount: "" + t.GetInterfaces().Length);
        Console.WriteLine(""stillStub: "" + Modules.IsStub(t));
        Console.WriteLine(""<<DONE>>"");
    }
}
", skipRoslyn: true);

            // Applying the definition is what produces the object varianceAssignable compares
            // against: it matches on $genericTypeDefinition + $typeArguments, and a bare definition
            // object carries neither, so a flattened `IHandler$1` in the manifest answered False.
            StringAssert.Contains(output, "iface: True");
            StringAssert.Contains(output, "baseClass: True");
            // getInterfaces() reads $allInterfaces, which a stub did not set at all — so
            // Type.GetInterfaces() on a deferred type used to report nothing.
            StringAssert.Contains(output, "ifaceCount: 1");
            // None of this loaded the module: the answer comes from the manifest.
            StringAssert.Contains(output, "stillStub: True");
            StringAssert.Contains(output, "<<DONE>>");
        }

        [TestMethod]
        public async Task AnUnresolvableBaseIsRetriedRatherThanCachedAsync()
        {
            var output = await RunTest(GenericPreamble + @"
public class Program
{
    public static void Main()
    {
        // Both the generic definition AND a type built on top of it are deferred. The base cannot
        // be built while its own definition is a stub — applying a stub throws — so the answer has
        // to stay open rather than being frozen as 'no bases'.
        Modules.Register(Script.Write<object>(@""{
            'DeferredBase$1': { m: 'chunk-3.mjs', k: 'class', a: 'App', i: [] },
            'UsesDeferred':   { m: 'chunk-4.mjs', k: 'class', a: 'App', i: [['DeferredBase$1', 'Order']] }
        }""));

        Console.WriteLine(""before: "" + Script.Write<int>(
            @""(Transpose.unroll('UsesDeferred').$$inherits || []).length""));

        // The definition's chunk arrives and defines it for real, retiring its stub.
        Script.Write(@""Transpose.define('DeferredBase$1', function (T) { return { }; });"");

        Console.WriteLine(""after: "" + Script.Write<int>(
            @""(Transpose.unroll('UsesDeferred').$$inherits || []).length""));
        Console.WriteLine(""<<DONE>>"");
    }
}
", skipRoslyn: true);

            StringAssert.Contains(output, "before: 0");
            StringAssert.Contains(output, "after: 1");
            StringAssert.Contains(output, "<<DONE>>");
        }

        /// <summary>An OPEN generic base — `class Relay&lt;T&gt; : IHandler&lt;T&gt;` — has no
        /// argument to write into the manifest, so the spec is the bare definition name. The stub
        /// still has to answer exactly what the loaded definition answers, which means applying that
        /// definition to its own placeholder type parameters rather than reporting it bare.</summary>
        private const string OpenBasePreamble = @"
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Transpose;

public interface IHandler<T> { void Handle(T item); }
public class Order { public int Id; }
public class Relay<T> : IHandler<T> { public void Handle(T item) { } }

public static class OpenChunk
{
    public static void Register()
    {
        Modules.Register(Script.Write<object>(
            @""{ 'StubRelay$1': { m: 'chunk-5.mjs', k: 'class', a: 'App', i: ['IHandler$1'] } }""));
    }
}
";

        [TestMethod]
        public async Task AnOpenGenericBaseAnswersAsTheLoadedDefinitionDoesAsync()
        {
            var output = await RunTest(OpenBasePreamble + @"
public class Program
{
    public static void Main()
    {
        OpenChunk.Register();

        // Relay<T> is really here; StubRelay<T> is the same shape, deferred.
        var loaded = typeof(Relay<>);
        var stub   = Type.GetType(""StubRelay$1"");

        Console.WriteLine(""constructed: "" + typeof(IHandler<Order>).IsAssignableFrom(loaded)
                                             + ""/"" + typeof(IHandler<Order>).IsAssignableFrom(stub));
        Console.WriteLine(""open: "" + typeof(IHandler<>).IsAssignableFrom(loaded)
                                     + ""/"" + typeof(IHandler<>).IsAssignableFrom(stub));
        Console.WriteLine(""ifaces: "" + loaded.GetInterfaces().Length + ""/"" + stub.GetInterfaces().Length);
        Console.WriteLine(""stillStub: "" + Modules.IsStub(stub));
        Console.WriteLine(""<<DONE>>"");
    }
}
", skipRoslyn: true);

            // Every answer is the loaded one. Reporting the bare definition instead made the middle
            // row True/False — a stub matching an unbound typeof that the real type does not match —
            // and left GetInterfaces() empty, because a definition object carries $kind "class"
            // whether or not it defines an interface.
            StringAssert.Contains(output, "constructed: False/False");
            StringAssert.Contains(output, "open: False/False");
            StringAssert.Contains(output, "ifaces: 1/1");
            StringAssert.Contains(output, "stillStub: True");
            StringAssert.Contains(output, "<<DONE>>");
        }

        /// <summary>
        /// The reflection metadata of a whole assembly shares ONE namespace array, and
        /// <c>Transpose.unroll</c> resolves it in place the first time any type's metadata is
        /// registered. With module output a namespace can be empty at that moment — every type in it
        /// was deferred, and the stubs are registered after the metadata — so the entry has to stay
        /// resolvable later. Overwriting it with null on the first pass made every later pass a no-op,
        /// and the metadata then read a member off undefined.
        /// </summary>
        [TestMethod]
        public async Task AnUnresolvableNamespaceIsLeftForALaterPassAsync()
        {
            var output = await RunTest(@"
using System;
using Transpose;

public static class Ns
{
    public static bool FirstPassKeepsTheName() => Script.Write<bool>(
        @""(function () { Transpose.global.$nsProbe = ['LateArrival']; Transpose.unroll(Transpose.global.$nsProbe); return typeof Transpose.global.$nsProbe[0] === 'string'; })()"");

    public static bool SecondPassResolvesIt() => Script.Write<bool>(
        @""(function () { Transpose.global.LateArrival = { Widget: 42 }; Transpose.unroll(Transpose.global.$nsProbe); return !!(Transpose.global.$nsProbe[0] && Transpose.global.$nsProbe[0].Widget === 42); })()"");
}

public class Program
{
    public static void Main()
    {
        Console.WriteLine(""kept: "" + Ns.FirstPassKeepsTheName());
        Console.WriteLine(""resolved: "" + Ns.SecondPassResolvesIt());
        Console.WriteLine(""<<DONE>>"");
    }
}
", skipRoslyn: true);

            StringAssert.Contains(output, "kept: True");
            StringAssert.Contains(output, "resolved: True");
            StringAssert.Contains(output, "<<DONE>>");
        }

        /// <summary>
        /// A chunk holding a nested type can evaluate before the chunk holding the type that contains
        /// it — chunk order is the reference graph's, not the source's — so <c>Transpose.define</c>
        /// meets a namespace placeholder where the containing type is about to go. What that
        /// placeholder holds has to survive the containing type taking the slot, at every depth: a
        /// two-level path (<c>Outer.Inner</c>) was already carried over, a three-level one
        /// (<c>Outer.Inner.Leaf</c>) was dropped, because the member being carried is then a plain
        /// object rather than a type. Dropping it left the deeper type reachable by name and absent
        /// from its own containing type, so reading it threw "cannot read properties of undefined".
        /// </summary>
        [TestMethod]
        public async Task ANestedTypeDefinedBeforeItsContainingTypesSurvivesAsync()
        {
            var output = await RunTest(@"
using System;
using Transpose;

public static class Chunks
{
    // What a chunk that evaluates first would run: the innermost type, whose containing types are
    // defined only by the chunk that comes after it.
    public static void DefineLeafFirst()
    {
        Script.Write(@""Transpose.define('Outer.Inner.Leaf', { $kind: 'nested enum', statics: { fields: { First: 0, Second: 1 } } });"");
    }

    public static void DefineContainers()
    {
        Script.Write(@""Transpose.define('Outer', { statics: { methods: { Name: function () { return 'outer'; } } } });"");
        Script.Write(@""Transpose.define('Outer.Inner', { $kind: 'nested class', statics: { methods: { Name: function () { return 'inner'; } } } });"");
    }

    public static string LeafFirstValue() => Script.Write<string>(""'' + Outer.Inner.Leaf.Second"");
    public static bool   LeafIsPresent()  => Script.Write<bool>(""typeof Outer.Inner.Leaf !== 'undefined'"");
    public static string InnerName()      => Script.Write<string>(""Outer.Inner.Name()"");
}

public class Program
{
    public static void Main()
    {
        Chunks.DefineLeafFirst();
        Chunks.DefineContainers();

        Console.WriteLine(""leaf present: "" + Chunks.LeafIsPresent());
        Console.WriteLine(""leaf value: "" + Chunks.LeafFirstValue());
        Console.WriteLine(""inner: "" + Chunks.InnerName());
        Console.WriteLine(""<<DONE>>"");
    }
}
", skipRoslyn: true);

            StringAssert.Contains(output, "leaf present: True");
            StringAssert.Contains(output, "leaf value: 1");
            StringAssert.Contains(output, "inner: inner");
            StringAssert.Contains(output, "<<DONE>>");
        }
    }
}
