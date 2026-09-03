using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// A <c>[Ready]</c> handler must not run while <c>Transpose.assembly</c> is still registering the
    /// assembly's types.
    ///
    /// <para>
    /// <c>Transpose.assembly</c> forces <c>staticInitAllow</c> to false for the whole of the body it
    /// evaluates, and a type's <c>$staticInit</c> is a no-op while that is false — and does not
    /// re-arm. The emitted <c>Transpose.ready(...)</c> registrations sit at the end of that body, and
    /// <c>ready</c> ran the handler immediately whenever the document was already past parsing
    /// (<c>readyState === "interactive"</c>, which is what a <c>defer</c> script sees — and tps emits
    /// its scripts with <c>defer</c>) or absent entirely, as it is here and in any worker.
    /// </para>
    ///
    /// <para>
    /// So the handler ran with static initialization suppressed, and anything it touched whose
    /// initializer had not run yet came back with its static fields at their declared defaults. A
    /// <c>Dictionary</c> is the case that bites, because <c>HashHelpers</c>' prime table is exactly
    /// such a field: building one threw "Cannot read properties of null (reading 'length')". A
    /// constructed generic is the worst version of it — it has no global slot whose getter would
    /// offer the initializer a second time, the same hazard <c>Class.js</c> documents for
    /// <c>List$1(X)._emptyArray</c>.
    /// </para>
    /// </summary>
    [TestClass]
    public class ReadyHandlerStaticInitTests : TranslatorTestBase
    {
        /// <summary>
        /// The original failure: a dictionary whose value type is itself a constructed generic, built
        /// inside a <c>[Ready]</c> handler.
        /// </summary>
        [TestMethod]
        public async Task ADictionaryCanBeBuiltInsideAReadyHandler()
        {
            var output = await RunTest("""
using System;
using System.Collections.Generic;
using Transpose;

public sealed class Handlers
{
    private readonly Dictionary<string, List<Action<string>>> _byTopic = new Dictionary<string, List<Action<string>>>();

    public int Add(string topic)
    {
        List<Action<string>> list;

        if (!_byTopic.TryGetValue(topic, out list))
        {
            list = new List<Action<string>>();
            _byTopic[topic] = list;
        }

        list.Add(s => { });

        return _byTopic.Count;
    }
}

public class Program
{
    [Ready]
    public static void OnReady()
    {
        var handlers = new Handlers();

        Console.WriteLine("added " + handlers.Add("a"));
        Console.WriteLine("added " + handlers.Add("b"));
        Console.WriteLine("again " + handlers.Add("a"));
    }

    public static void Main() { }
}
""", skipRoslyn: true);

            Assert.AreEqual("added 1\nadded 2\nagain 2", output.Trim().Replace("\r\n", "\n"),
                "a [Ready] handler has to see fully initialized types\n" + output);
        }

        /// <summary>
        /// The same for a static field of a generic type, which is the shape the runtime's own
        /// comment calls out: a list built before its element type's statics ran had a null backing
        /// array rather than an empty one.
        /// </summary>
        [TestMethod]
        public async Task AGenericStaticIsInitializedForAReadyHandler()
        {
            var output = await RunTest("""
using System;
using System.Collections.Generic;
using System.Linq;
using Transpose;

public class Program
{
    [Ready]
    public static void OnReady()
    {
        var empty = new List<int>();

        Console.WriteLine("count " + empty.Count);

        var set = new HashSet<string>(new[] { "a", "b", "a" });

        Console.WriteLine("set " + set.Count);
        Console.WriteLine("sum " + Enumerable.Range(1, 4).Sum());
    }

    public static void Main() { }
}
""", skipRoslyn: true);

            Assert.AreEqual("count 0\nset 2\nsum 10", output.Trim().Replace("\r\n", "\n"),
                "static state a [Ready] handler reaches has to be initialized\n" + output);
        }

        /// <summary>
        /// The ordering the fix relies on: a handler deferred out of the assembly body still runs
        /// before anything the page does afterwards, so [Ready] keeps meaning "before the app runs".
        /// </summary>
        [TestMethod]
        public async Task AReadyHandlerStillRunsBeforeTheEntryPoint()
        {
            var output = await RunTest("""
using System;
using Transpose;

public class Program
{
    [Ready]
    public static void OnReady()
    {
        Console.WriteLine("ready");
    }

    public static void Main()
    {
        Console.WriteLine("main");
    }
}
""", skipRoslyn: true);

            Assert.AreEqual("ready\nmain", output.Trim().Replace("\r\n", "\n"),
                "[Ready] must still precede Main\n" + output);
        }
    }
}
