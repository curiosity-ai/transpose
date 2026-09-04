using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// <c>Script.ToPlainObjectCopy</c> (<c>Transpose.toPlainCopy</c>) and <c>Script.ToPlainObject</c>
    /// (<c>Transpose.toPlain</c>): the plain, structured-clone-safe forms of a value built from C#.
    /// Transpose-only behaviour - there is nothing to compare against natively - so every test runs
    /// JS-only and asserts on what Node printed. <c>structuredClone</c> is the oracle for "a worker
    /// would accept this", and <c>JSON.stringify</c> for "the data is intact".
    /// </summary>
    [TestClass]
    public class PlainObjectTests : TranslatorTestBase
    {
        private const string HELPERS = @"
using System;
using System.Collections.Generic;
using Transpose;
static class Probe
{
    // Whether structuredClone accepts a value - the exact test a worker's postMessage applies.
    public static bool Clones(object value) => Script.Write<bool>(""(function (v) { try { structuredClone(v); return true; } catch (e) { return false; } })(value)"");
    public static string Json(object value) => Script.Write<string>(""JSON.stringify(value)"");
    public static bool IsPlainProto(object value) => Script.Write<bool>(""Object.getPrototypeOf(value) === Object.prototype"");
    public static string TypeOfMember(object value, string name) => Script.Write<string>(""typeof value[name]"");
}
";

        [TestMethod]
        public async Task ToPlainObjectCopyDropsTheArrayTypeStampAndClones()
        {
            var output = await RunTest(HELPERS + @"
public class Program
{
    public static void Main()
    {
        var globs = new[] { ""*.json"", ""*.md"" };
        var copy  = Script.ToPlainObjectCopy(globs);

        Console.WriteLine(""source-type:"" + Probe.TypeOfMember(globs, ""$type""));
        Console.WriteLine(""source-clones:"" + Probe.Clones(globs));
        Console.WriteLine(""copy-type:"" + Probe.TypeOfMember(copy, ""$type""));
        Console.WriteLine(""copy-clones:"" + Probe.Clones(copy));
        Console.WriteLine(""copy-json:"" + Probe.Json(copy));
        Console.WriteLine(""copy-is-fresh:"" + !Script.StrictEquals(copy, globs));

        // Nested: a jagged array inside an anonymous object, a List<T> (its toJSON is its items).
        var nested = new { rows = new[] { new[] { 1, 2 }, new[] { 3 } }, names = new List<string> { ""a"", ""b"" } };
        var nestedCopy = Script.ToPlainObjectCopy(nested);

        Console.WriteLine(""nested-clones:"" + Probe.Clones(nestedCopy));
        Console.WriteLine(""nested-json:"" + Probe.Json(nestedCopy));
        Console.WriteLine(""<<DONE>>"");
    }
}", skipRoslyn: true);

            StringAssert.Contains(output, "source-type:function", "premise: a C# array carries a $type function\n" + output);
            StringAssert.Contains(output, "source-clones:False", "premise: structuredClone refuses the C# array\n" + output);
            StringAssert.Contains(output, "copy-type:undefined", output);
            StringAssert.Contains(output, "copy-clones:True", output);
            StringAssert.Contains(output, "copy-json:[\"*.json\",\"*.md\"]", output);
            StringAssert.Contains(output, "copy-is-fresh:True", output);
            StringAssert.Contains(output, "nested-clones:True", output);
            StringAssert.Contains(output, "nested-json:{\"rows\":[[1,2],[3]],\"names\":[\"a\",\"b\"]}", output);
        }

        [TestMethod]
        public async Task ToPlainObjectCopyFlattensAClassInstanceToItsData()
        {
            var output = await RunTest(HELPERS + @"
class Limits { public int MaxItems; public double Timeout; }
class WorkerSettings
{
    public string Name;
    public int Retries;
    public string[] Include;
    public Limits Limits;
    public Func<int, int> Transform = x => x * 2;
    public bool Enabled { get; set; }
}
public class Program
{
    public static void Main()
    {
        var settings = new WorkerSettings { Name = ""indexer"", Retries = 3, Include = new[] { ""*.cs"" }, Limits = new Limits { MaxItems = 500, Timeout = 2.5 }, Enabled = true };
        var copy     = Script.ToPlainObjectCopy(settings);

        Console.WriteLine(""source-clones:"" + Probe.Clones(settings));
        Console.WriteLine(""copy-clones:"" + Probe.Clones(copy));
        Console.WriteLine(""copy-plain-proto:"" + Probe.IsPlainProto(copy));
        Console.WriteLine(""copy-nested-plain-proto:"" + Probe.IsPlainProto(Script.Get(copy, ""Limits"")));
        Console.WriteLine(""copy-delegate:"" + Probe.TypeOfMember(copy, ""Transform""));
        Console.WriteLine(""copy-include-type:"" + Probe.TypeOfMember(Script.Get(copy, ""Include""), ""$type""));
        Console.WriteLine(""copy-json:"" + Probe.Json(copy));
        Console.WriteLine(""<<DONE>>"");
    }
}", skipRoslyn: true);

            StringAssert.Contains(output, "source-clones:False", "premise: a class instance with a delegate field does not clone\n" + output);
            StringAssert.Contains(output, "copy-clones:True", output);
            StringAssert.Contains(output, "copy-plain-proto:True", output);
            StringAssert.Contains(output, "copy-nested-plain-proto:True", output);
            StringAssert.Contains(output, "copy-delegate:undefined", output);
            StringAssert.Contains(output, "copy-include-type:undefined", output);
            StringAssert.Contains(output, "copy-json:{\"Name\":\"indexer\",\"Retries\":3,\"Include\":[\"*.cs\"],\"Limits\":{\"MaxItems\":500,\"Timeout\":2.5},\"Enabled\":true}", output);
        }

        [TestMethod]
        public async Task ToPlainObjectCopyKeepsSharedReferencesAndCycles()
        {
            var output = await RunTest(HELPERS + @"
public class Program
{
    public static void Main()
    {
        var shared = new[] { 1, 2 };
        var graph  = new { first = shared, second = shared };
        Script.Set(graph, ""self"", graph);

        var copy = Script.ToPlainObjectCopy(graph);

        Console.WriteLine(""copy-clones:"" + Probe.Clones(copy));
        Console.WriteLine(""shared-once:"" + Script.StrictEquals(Script.Get(copy, ""first""), Script.Get(copy, ""second"")));
        Console.WriteLine(""shared-is-copied:"" + !Script.StrictEquals(Script.Get(copy, ""first""), shared));
        Console.WriteLine(""cycle-kept:"" + Script.StrictEquals(Script.Get(copy, ""self""), copy));
        Console.WriteLine(""first-type:"" + Probe.TypeOfMember(Script.Get(copy, ""first""), ""$type""));
        Console.WriteLine(""<<DONE>>"");
    }
}", skipRoslyn: true);

            StringAssert.Contains(output, "copy-clones:True", output);
            StringAssert.Contains(output, "shared-once:True", output);
            StringAssert.Contains(output, "shared-is-copied:True", output);
            StringAssert.Contains(output, "cycle-kept:True", output);
            StringAssert.Contains(output, "first-type:undefined", output);
        }

        [TestMethod]
        public async Task ToPlainObjectCopyKeepsWhatTheJsonRoundTripLoses()
        {
            var output = await RunTest(HELPERS + @"
public class Program
{
    public static void Main()
    {
        // A Date, a typed array, NaN, a boxed value, a toJSON of its own, JSON Schema's $-keys, and
        // members holding undefined or a function.
        var value = Script.Write<object>(@""({
            when: new Date(86400000),
            bytes: new Uint32Array([1, 2, 3]),
            nan: NaN,
            boxed: Transpose.box(7, System.Int32),
            uri: { toJSON: function () { return { scheme: 'file', path: '/a.cs' }; } },
            $schema: 'https://json-schema.org/draft/2020-12/schema',
            owner: { $ref: '#/$defs/person' },
            gone: undefined,
            fn: function () { return 1; },
            kept: 1
        })"");

        var copy = Script.ToPlainObjectCopy(value);

        Console.WriteLine(""copy-clones:"" + Probe.Clones(copy));
        Console.WriteLine(""date-kept:"" + Script.Write<bool>(""copy.when instanceof Date && copy.when.getTime() === 86400000 && copy.when !== value.when""));
        Console.WriteLine(""typed-array-kept:"" + Script.Write<bool>(""copy.bytes === value.bytes""));
        Console.WriteLine(""nan-kept:"" + Script.Write<bool>(""Number.isNaN(copy.nan)""));
        Console.WriteLine(""unboxed:"" + Script.Write<bool>(""copy.boxed === 7""));
        Console.WriteLine(""tojson-honoured:"" + Probe.Json(Script.Get(copy, ""uri"")));
        Console.WriteLine(""schema-key:"" + Script.Get(copy, ""$schema""));
        Console.WriteLine(""ref-key:"" + Script.Get(Script.Get(copy, ""owner""), ""$ref""));
        Console.WriteLine(""gone-dropped:"" + !Script.In(copy, ""gone""));
        Console.WriteLine(""fn-dropped:"" + !Script.In(copy, ""fn""));
        Console.WriteLine(""null-is-null:"" + (Script.ToPlainObjectCopy<object>(null) == null));
        Console.WriteLine(""string-is-itself:"" + Script.StrictEquals(Script.ToPlainObjectCopy(""hello""), ""hello""));
        Console.WriteLine(""<<DONE>>"");
    }
}", skipRoslyn: true);

            StringAssert.Contains(output, "copy-clones:True", output);
            StringAssert.Contains(output, "date-kept:True", output);
            StringAssert.Contains(output, "typed-array-kept:True", output);
            StringAssert.Contains(output, "nan-kept:True", output);
            StringAssert.Contains(output, "unboxed:True", output);
            StringAssert.Contains(output, "tojson-honoured:{\"scheme\":\"file\",\"path\":\"/a.cs\"}", output);
            StringAssert.Contains(output, "schema-key:https://json-schema.org/draft/2020-12/schema", output);
            StringAssert.Contains(output, "ref-key:#/$defs/person", output);
            StringAssert.Contains(output, "gone-dropped:True", output);
            StringAssert.Contains(output, "fn-dropped:True", output);
            StringAssert.Contains(output, "null-is-null:True", output);
            StringAssert.Contains(output, "string-is-itself:True", output);
        }

        // ---- Script.ToPlainObject ({o:plain}) --------------------------------------
        // The :plain template modifier had no case in the emitter, so ToPlainObject / ToObjectLiteral
        // emitted their argument unchanged - a silent no-op. They are the SHALLOW form: Transpose.toPlain.

        [TestMethod]
        public void ToPlainObjectEmitsTheRuntimeToPlain()
        {
            var result = new RoslynTranslator().Translate(@"
using Transpose;
class Box { public int Value; }
public class Program
{
    public static void Main()
    {
        var a = Script.ToPlainObject(new Box { Value = 1 });
        var b = Script.ToObjectLiteral(new Box { Value = 2 });
    }
}");
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(js, @"Transpose\.toPlain\(").Count,
                "both {o:plain} templates must emit Transpose.toPlain(...)\n" + js);
        }

        [TestMethod]
        public async Task ToPlainObjectIsTheShallowForm()
        {
            var output = await RunTest(HELPERS + @"
class Box { public int Value; public string[] Tags; }
public class Program
{
    public static void Main()
    {
        var box   = new Box { Value = 1, Tags = new[] { ""x"" } };
        var plain = Script.ToPlainObject(box);

        Console.WriteLine(""plain-proto:"" + Probe.IsPlainProto(plain));
        Console.WriteLine(""plain-json:"" + Probe.Json(plain));
        // Shallow: the nested C# array is the same array, $type and all.
        Console.WriteLine(""nested-shared:"" + Script.StrictEquals(Script.Get(plain, ""Tags""), box.Tags));
        Console.WriteLine(""nested-type:"" + Probe.TypeOfMember(Script.Get(plain, ""Tags""), ""$type""));
        Console.WriteLine(""<<DONE>>"");
    }
}", skipRoslyn: true);

            StringAssert.Contains(output, "plain-proto:True", output);
            StringAssert.Contains(output, "plain-json:{\"Value\":1,\"Tags\":[\"x\"]}", output);
            StringAssert.Contains(output, "nested-shared:True", output);
            StringAssert.Contains(output, "nested-type:function", output);
        }
    }
}
