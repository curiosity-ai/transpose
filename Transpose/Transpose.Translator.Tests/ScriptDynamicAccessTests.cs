using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// The `Script` dynamic-access primitives — reaching a JavaScript value by name instead of through
    /// a typed binding. They are pure `[Template]` members, so nothing about them is checked by the
    /// compiler: a template that stopped substituting (or started quoting a name it must insert raw)
    /// would emit JavaScript that still parses and reads the wrong thing at runtime. Hence both a
    /// behavioural test through Node and a check on the emitted form:
    ///  - `Script.Get`/`Get&lt;T&gt;(name)` inserts the name as RAW JavaScript (`{name:raw}`), so
    ///    `Get&lt;int&gt;("a.b.n")` reads the path `a.b.n` — not the string `"a.b.n"`;
    ///  - the scope overloads `Get`/`Get&lt;T&gt;(scope, name)` index the scope (`scope["s"]`), which is
    ///    what lets a caller hold a JS object in an `object` local and walk it;
    ///  - `Script.IsUndefined(x)` is `x === undefined` and `Script.Undefined` is the `undefined` literal
    ///    itself. Note that `Script.IsDefined` is only its complement (`typeof x !== "undefined"`), so a
    ///    `null` counts as DEFINED — `Script.HasValue` is the one that rejects both. The docs state that
    ///    distinction, so it is pinned here.
    /// </summary>
    [TestClass]
    public class ScriptDynamicAccessTests : TranslatorTestBase
    {
        // Native .NET cannot run any of this (there is no `Transpose.Script` outside a transpiled
        // build), so the expectations are asserted inside the snippet and `skipRoslyn` is set.
        [TestMethod]
        public async Task GetSetAndUndefinedPrimitivesRoundTrip()
        {
            var code = @"
using Transpose;
using System;

public class Program
{
    public static void Main()
    {
        Script.Write(""globalThis.tpsProbe = { n: 42, s: 'hi' };"");

        // The global (string-name) overloads: the name is a raw JS path.
        object nAsObject = Script.Get(""globalThis.tpsProbe.n"");
        int    n         = Script.Get<int>(""globalThis.tpsProbe.n"");

        // The scope overloads: index a value the caller already holds.
        object scope     = Script.Get(""globalThis.tpsProbe"");
        object sAsObject = Script.Get(scope, ""s"");
        string s         = Script.Get<string>(scope, ""s"");

        Console.WriteLine(""n(object): "" + nAsObject);
        Console.WriteLine(""n(int): "" + n);
        Console.WriteLine(""s(object): "" + sAsObject);
        Console.WriteLine(""s(string): "" + s);

        // Both Set overloads, read back through both Get forms.
        Script.Set(""globalThis.tpsProbe.n"", 7);
        Script.Set(scope, ""s"", ""bye"");
        int    n2 = Script.Get<int>(""globalThis.tpsProbe.n"");
        string s2 = Script.Get<string>(scope, ""s"");
        Console.WriteLine(""n after Set: "" + n2);
        Console.WriteLine(""s after Set: "" + s2);

        // The undefined primitives.
        object missing = Script.Get(scope, ""nope"");
        Console.WriteLine(""IsUndefined(missing): "" + Script.IsUndefined(missing));
        Console.WriteLine(""IsUndefined(present): "" + Script.IsUndefined(s));
        Console.WriteLine(""IsUndefined(Undefined): "" + Script.IsUndefined(Script.Undefined));
        Console.WriteLine(""IsDefined(missing): "" + Script.IsDefined(missing));
        Console.WriteLine(""IsDefined(present): "" + Script.IsDefined(s));

        if (n != 42) throw new Exception(""Get<int>(name) should read the path, got "" + n);
        if (s != ""hi"") throw new Exception(""Get<string>(scope, name) should read the member, got "" + s);
        if (nAsObject == null || sAsObject == null) throw new Exception(""the non-generic overloads should read the same values"");
        if (n2 != 7) throw new Exception(""Set(name, value) should write through the path, got "" + n2);
        if (s2 != ""bye"") throw new Exception(""Set(scope, name, value) should write the member, got "" + s2);
        if (!Script.IsUndefined(missing)) throw new Exception(""an absent member should be undefined"");
        if (Script.IsUndefined(s2)) throw new Exception(""a present member should not be undefined"");
        if (!Script.IsUndefined(Script.Undefined)) throw new Exception(""Script.Undefined should be undefined"");
        if (Script.IsDefined(missing)) throw new Exception(""IsDefined should reject an absent member"");
        if (!Script.IsDefined(s2)) throw new Exception(""IsDefined should accept a present member"");

        // null vs undefined: IsDefined is only the complement of IsUndefined, so a null is `defined`.
        object nul = null;
        Console.WriteLine(""IsUndefined(null): "" + Script.IsUndefined(nul));
        Console.WriteLine(""IsDefined(null): "" + Script.IsDefined(nul));
        Console.WriteLine(""IsNull(null): "" + Script.IsNull(nul));
        Console.WriteLine(""HasValue(null): "" + Script.HasValue(nul));
        Console.WriteLine(""HasValue(undefined): "" + Script.HasValue(Script.Undefined));
        Console.WriteLine(""HasValue(value): "" + Script.HasValue(s2));

        if (Script.IsUndefined(nul)) throw new Exception(""a null is not undefined"");
        if (!Script.IsDefined(nul)) throw new Exception(""IsDefined counts a null as defined"");
        if (!Script.IsNull(nul)) throw new Exception(""IsNull should accept a null"");
        if (Script.HasValue(nul) || Script.HasValue(Script.Undefined)) throw new Exception(""HasValue should reject both null and undefined"");
        if (!Script.HasValue(s2)) throw new Exception(""HasValue should accept a real value"");
    }
}";
            await RunTest(code, skipRoslyn: true);
        }

        [TestMethod]
        public void GetEmitsARawPathOrAScopeIndexerAndIsUndefinedAStrictComparison()
        {
            var code = @"
using Transpose;
using System;

public class Program
{
    public static void Main()
    {
        object scope = Script.Get(""globalThis.tpsProbe"");
        int    n     = Script.Get<int>(""globalThis.tpsProbe.n"");
        object o     = Script.Get(scope, ""s"");
        string s     = Script.Get<string>(scope, ""s"");
        bool absent  = Script.IsUndefined(o);
        bool none    = Script.IsUndefined(Script.Undefined);
        Console.WriteLine("""" + n + s + absent + none);
    }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;

            // {name:raw} — the name is JavaScript, so it must NOT arrive quoted.
            Assert.IsTrue(js.Contains("= globalThis.tpsProbe;"),
                "Script.Get(name) should emit the name as a raw path\n" + js);
            Assert.IsTrue(js.Contains("= globalThis.tpsProbe.n;"),
                "Script.Get<T>(name) should emit the name as a raw path\n" + js);
            Assert.IsFalse(js.Contains("\"globalThis.tpsProbe\""),
                "the name must not be emitted as a string literal\n" + js);

            // {scope:raw}[{name}] — the scope is the expression, the member the quoted key.
            Assert.IsTrue(js.Contains("scope[\"s\"]"),
                "Script.Get(scope, name) should index the scope\n" + js);

            Assert.IsTrue(js.Contains("o === undefined"),
                "Script.IsUndefined(x) should emit a strict comparison against undefined\n" + js);
            Assert.IsTrue(js.Contains("undefined === undefined"),
                "Script.Undefined should emit the undefined literal\n" + js);
        }
    }
}
