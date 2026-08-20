using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace Transpose.Translator.Tests.Ported
{
    [TestClass]
    public class IteratorsTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task Iterators_YieldReturn()
        {
            var code = """
using System;
using System.Collections.Generic;

public class Program
{
    public static IEnumerable<int> GetNumbers()
    {
        yield return 1;
        yield return 2;
        yield return 3;
    }

    public static void Main()
    {
        foreach (var n in GetNumbers())
        {
            Console.WriteLine(n);
        }
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Iterators_YieldBreak()
        {
            var code = """
using System;
using System.Collections.Generic;

public class Program
{
    public static IEnumerable<int> GetNumbers(int limit)
    {
        int i = 0;
        while (true)
        {
            if (i >= limit)
                yield break;
            yield return i;
            i++;
        }
    }

    public static void Main()
    {
        foreach (var n in GetNumbers(3))
        {
            Console.WriteLine(n);
        }
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Iterators_StateMaintenance()
        {
            var code = """
using System;
using System.Collections.Generic;

public class Program
{
    public static IEnumerable<string> GetSteps()
    {
        Console.WriteLine("Start");
        yield return "Step 1";
        Console.WriteLine("After 1");
        yield return "Step 2";
        Console.WriteLine("End");
    }

    public static void Main()
    {
        foreach (var s in GetSteps())
        {
            Console.WriteLine("Received: " + s);
        }
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Iterators_Nested()
        {
            var code = """
using System;
using System.Collections.Generic;

public class Program
{
    public static IEnumerable<int> Range(int start, int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return start + i;
        }
    }

    public static void Main()
    {
        foreach (var n in Range(10, 3))
        {
            Console.WriteLine(n);
        }
    }
}
""";
            await RunTest(code);
        }

        /// <summary>
        /// An iterator may be declared to return the CURSOR (<c>IEnumerator&lt;T&gt;</c>) instead of the
        /// sequence. Both used to compile to <c>TransposeR.iter</c> — an *enumerable* — so a collection
        /// whose <c>GetEnumerator()</c> is an iterator method handed its caller a sequence where the
        /// contract promised a cursor, and any <c>foreach</c> over it died with
        /// "TypeError: e.MoveNext is not a function". This is the exact shape of
        /// <c>System.Net.Http.Headers.HttpHeaders</c>.
        /// </summary>
        [TestMethod]
        public async Task Iterators_GetEnumeratorIsItselfAnIterator()
        {
            var code = """
using System;
using System.Collections;
using System.Collections.Generic;

public class Headers : IEnumerable<KeyValuePair<string, string>>
{
    private readonly Dictionary<string, string> _store = new Dictionary<string, string>();

    public void Add(string k, string v) => _store.Add(k, v);

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _store.Count > 0
        ? GetEnumeratorCore()
        : ((IEnumerable<KeyValuePair<string, string>>)Array.Empty<KeyValuePair<string, string>>()).GetEnumerator();

    private IEnumerator<KeyValuePair<string, string>> GetEnumeratorCore()
    {
        foreach (KeyValuePair<string, string> header in _store)
        {
            yield return header;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class Program
{
    public static void Main()
    {
        foreach (var kv in new Headers()) Console.WriteLine("unexpected " + kv.Key);
        Console.WriteLine("empty ok");

        var headers = new Headers();
        headers.Add("Accept", "application/json");
        headers.Add("X-Trace", "42");
        foreach (var kv in headers) Console.WriteLine(kv.Key + ": " + kv.Value);
    }
}
""";
            await RunTest(code);
        }

        /// <summary>
        /// The cursor an <c>IEnumerator&lt;T&gt;</c> iterator returns must answer MoveNext/Current/Dispose
        /// directly, in a method, a property getter and a local function alike — and the enumerable form
        /// must stay re-enumerable.
        /// </summary>
        [TestMethod]
        public async Task Iterators_EnumeratorReturningFormsAreCursors()
        {
            var code = """
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    static IEnumerator<int> Cursor() { yield return 1; yield return 2; }

    static IEnumerator Untyped() { yield return "x"; yield return "y"; }

    static IEnumerable<int> Sequence() { yield return 7; yield return 8; }

    static IEnumerator<string> Tracked
    {
        get
        {
            try { yield return "a"; yield return "b"; }
            finally { Console.WriteLine("finally ran"); }
        }
    }

    public static void Main()
    {
        var e = Cursor();
        while (e.MoveNext()) Console.WriteLine("method " + e.Current);
        e.Dispose();

        var u = Untyped();
        while (u.MoveNext()) Console.WriteLine("untyped " + u.Current);

        var p = Tracked;
        p.MoveNext();
        Console.WriteLine("property " + p.Current);
        p.Dispose();

        IEnumerator<int> Local() { yield return 41; yield return 42; }
        var l = Local();
        while (l.MoveNext()) Console.WriteLine("local " + l.Current);

        // The enumerable form is unchanged: still re-enumerable and still visible to LINQ.
        var s = Sequence();
        Console.WriteLine("sequence " + s.Sum() + "/" + s.Sum());
    }
}
""";
            await RunTest(code);
        }
    }
}
