using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// The two invariants the incremental build cache rests on (see <see cref="IncrementalPlan"/> and
    /// <c>Transpose.Compiler.BuildCache</c>):
    ///
    /// 1. the declaration-surface hash notices *every* change outside a method/accessor body and
    ///    ignores changes inside one — it is what decides whether a cached build may be reused at all;
    /// 2. splicing a type's cached JavaScript back in produces exactly the bundle a full build would,
    ///    so an incremental build's output is byte-identical rather than merely equivalent.
    ///
    /// If either slips, an incremental build is silently *wrong* — it produces plausible output instead
    /// of failing — which is why they are unit-tested rather than left to a whole-project comparison.
    /// </summary>
    [TestClass]
    public class IncrementalEmitTests
    {
        private const string GreeterCs = @"
using System;

public class Greeter
{
    private int _count = 1;

    public string Name { get; set; }

    public string Greet(string who)
    {
        var prefix = ""hello "";
        return prefix + who + _count;
    }

    public int Twice(int n) => n * 2;
}
";

        private const string OtherCs = @"
public class Other
{
    public string Use(Greeter g) { return g.Greet(""x"") + g.Twice(2); }
}
";

        private static string Hash(string source)
            => IncrementalPlan.DeclarationHash(
                CompilationBuilder.ParseOne("Greeter.cs", source, LanguageVersion.Latest, null));

        // ---- the declaration-surface hash ------------------------------------------------------

        [TestMethod]
        public void BodyEditsDoNotChangeTheDeclarationHash()
        {
            var edits = new (string what, string source)[]
            {
                ("a string literal in a body", GreeterCs.Replace(@"""hello """, @"""hi there """)),
                ("statements, a closure and a local function added to a body",
                    GreeterCs.Replace(@"var prefix = ""hello "";",
                        @"Func<int,int> f = n => n + 1; int L(int q) { return f(q); } var prefix = ""hello "" + L(2);")),
                ("an expression-bodied member's body", GreeterCs.Replace("=> n * 2;", "=> n * 3;")),
                ("whitespace inside a body", GreeterCs.Replace("return prefix + who", "return    prefix + who")),
            };

            foreach (var (what, edited) in edits)
            {
                Assert.AreNotEqual(GreeterCs, edited, $"the test's own edit did not apply: {what}");
                Assert.AreEqual(Hash(GreeterCs), Hash(edited),
                    $"{what} is confined to a method body and must not count as a declaration change");
            }
        }

        [TestMethod]
        public void DeclarationEditsChangeTheDeclarationHash()
        {
            var edits = new (string what, string source)[]
            {
                ("a new method", GreeterCs.Replace("public int Twice", "public void Added() { } public int Twice")),
                ("a renamed method", GreeterCs.Replace("public int Twice", "public int Thrice")),
                ("a new overload", GreeterCs.Replace("public int Twice", "public int Twice(long n) => 0; public int Twice")),
                ("an added optional parameter", GreeterCs.Replace("Twice(int n)", "Twice(int n, int m = 0)")),
                ("a property initializer", GreeterCs.Replace("public string Name { get; set; }", @"public string Name { get; set; } = ""a"";")),
                ("a field initializer", GreeterCs.Replace("_count = 1", "_count = 2")),
                ("a new attribute", GreeterCs.Replace("public class Greeter", "[Obsolete] public class Greeter")),
                ("a changed base list", GreeterCs.Replace("public class Greeter", "public class Greeter : IDisposable")),
                ("a new type", GreeterCs + "public class Extra { }"),
                ("changed accessibility", GreeterCs.Replace("public int Twice", "internal int Twice")),
            };

            foreach (var (what, edited) in edits)
            {
                Assert.AreNotEqual(GreeterCs, edited, $"the test's own edit did not apply: {what}");
                Assert.AreNotEqual(Hash(GreeterCs), Hash(edited),
                    $"{what} is a declaration change and must invalidate the cache");
            }
        }

        [TestMethod]
        public void TheDeclarationHashIsStableAcrossParses()
            => Assert.AreEqual(Hash(GreeterCs), Hash(GreeterCs));

        /// <summary>
        /// The hash is a hash of *text* with the body spans cut out, so a few body-only edits still
        /// change it — swapping <c>=&gt; expr;</c> for <c>{ return expr; }</c> moves the trailing
        /// semicolon, and reformatting a declaration changes its text. Those cost a full rebuild that
        /// was not strictly necessary, which is the safe direction to be wrong in: the hash must never
        /// miss a change, and may report one that turns out not to matter. This test pins that
        /// asymmetry down so nobody "fixes" it into an under-reporting normaliser by accident.
        /// </summary>
        [TestMethod]
        public void TheDeclarationHashMayOverReportButNeverUnderReports()
        {
            Assert.AreNotEqual(Hash(GreeterCs), Hash(GreeterCs.Replace("=> n * 2;", "{ return n * 2; }")),
                "an over-report is allowed here — it only costs a full rebuild");
            Assert.AreNotEqual(Hash(GreeterCs), Hash(GreeterCs.Replace("public int Twice", "public  int  Twice")),
                "reformatting a declaration is an over-report too");
        }

        // ---- reusing cached per-type JavaScript ------------------------------------------------

        [TestMethod]
        public void ReusingEveryTypeReproducesTheFullBundleExactly()
        {
            var (fullJs, fullMeta) = Emit(GreeterCs, OtherCs, plan: null);

            // Round-trip: take the per-type JavaScript a build recorded, feed it back as the cache for
            // a build in which no file changed, and the bundle — type order, prelude, reflection
            // metadata and all — has to come out identical.
            var recording = Record(GreeterCs, OtherCs);
            var replay = Plan(changed: new string[0], cached: recording);
            var (reusedJs, reusedMeta) = Emit(GreeterCs, OtherCs, replay);

            Assert.AreEqual(2, replay.ReusedTypes, "both types should have come from the cache");
            Assert.AreEqual(0, replay.ReemittedTypes);
            Assert.AreEqual(fullJs, reusedJs);
            Assert.AreEqual(fullMeta, reusedMeta);
        }

        [TestMethod]
        public void ReusingTheUnchangedFilesTypesMatchesAFullBuild()
        {
            var recording = Record(GreeterCs, OtherCs);

            // Greeter.cs was edited inside a body; Other.cs was not touched, so `Other` — which calls
            // both of Greeter's methods, and so depends on their emitted names — is reused from the
            // cache. The bundle must still equal a full build of the edited sources.
            var editedGreeter = GreeterCs.Replace(@"""hello """, @"""good day """);
            var plan = Plan(changed: new[] { "Greeter.cs" }, cached: recording);
            var (incrementalJs, incrementalMeta) = Emit(editedGreeter, OtherCs, plan);

            Assert.AreEqual(1, plan.ReusedTypes, "Other lives in an unchanged file and must be reused");
            Assert.AreEqual(1, plan.ReemittedTypes, "Greeter's file changed and must be re-emitted");

            var (fullJs, fullMeta) = Emit(editedGreeter, OtherCs, plan: null);
            Assert.AreEqual(fullJs, incrementalJs);
            Assert.AreEqual(fullMeta, incrementalMeta);
            Assert.IsTrue(incrementalJs.Contains("good day"), "the edit has to reach the output");
        }

        [TestMethod]
        public void ACachedEntryIsNeverUsedForAChangedFile()
        {
            // A cache entry that does not match the current source must be ignored when its file is in
            // the changed set — otherwise a real edit would be silently dropped.
            var poisoned = new Dictionary<string, string>
            {
                ["global::Greeter"] = "/* stale Greeter */",
                ["global::Other"] = "/* stale Other */",
            };
            var editedGreeter = GreeterCs.Replace(@"""hello """, @"""fresh """);
            var plan = Plan(changed: new[] { "Greeter.cs", "Other.cs" }, cached: poisoned);
            var (js, _) = Emit(editedGreeter, OtherCs, plan);

            Assert.AreEqual(0, plan.ReusedTypes);
            Assert.IsFalse(js.Contains("stale"));
            Assert.IsTrue(js.Contains("fresh "));
        }

        [TestMethod]
        public void TypeKeysDistinguishArityAndNesting()
        {
            var source = @"
public class Box { public class Inner { } }
public class Box<T> { }
public class Box<T, U> { }
namespace N { public class Box { } }
";
            var recorded = Record(source, "");
            CollectionAssert.IsSubsetOf(
                new[] { "global::Box", "global::Box.Inner", "global::Box<T>", "global::Box<T, U>", "global::N.Box" },
                recorded.Keys.ToList());
        }

        // ---- helpers ---------------------------------------------------------------------------

        /// <summary>The per-type JavaScript a build of these sources produces — i.e. what the cache
        /// would hold afterwards.</summary>
        private static Dictionary<string, string> Record(string greeter, string other)
        {
            var plan = Plan(new string[0], new Dictionary<string, string>());
            Emit(greeter, other, plan);
            return plan.FinalTypeJs.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        private static IncrementalPlan Plan(string[] changed, IReadOnlyDictionary<string, string> cached)
            => new IncrementalPlan { ChangedSources = changed, TypeJs = cached };

        private static (string javascript, string? metadata) Emit(string greeter, string other, IncrementalPlan? plan)
        {
            var sources = new List<(string, string)> { ("Greeter.cs", greeter) };
            if (other.Length > 0) sources.Add(("Other.cs", other));

            var result = new RoslynTranslator().BuildAssembly(
                sources, "App", null, null, LanguageVersion.Latest,
                reflectionEnabled: true, metadataTarget: MetadataTarget.File,
                emitAssembly: false, incremental: plan);

            Assert.IsTrue(result.Success, string.Join("\n", result.Errors.Select(e => e.GetMessage())));
            return (result.Javascript!, result.MetadataJavascript);
        }
    }
}
