using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Regression tests for a local of struct type declared without an initializer. C# lets such a
    /// local be definitely assigned one field at a time (`ElementCount result; result.uniqueCount = 0;`),
    /// so emitting a bare `let result;` left it undefined and the first field write threw
    /// "Cannot set properties of undefined". The BCL's own
    /// HashSet&lt;T&gt;.CheckUniqueAndUnfoundElements is written that way, which took down every
    /// SetEquals / IsSubsetOf / Overlaps call whose argument is not a same-comparer HashSet.
    /// </summary>
    [TestClass]
    public class UninitializedStructLocalTests : TranslatorTestBase
    {
        /// <summary>Mirrors the shape of HashSet&lt;T&gt;.CheckUniqueAndUnfoundElements: a struct local
        /// declared bare, filled in field by field on two separate paths, returned by value.</summary>
        [TestMethod]
        public async Task TestFieldWiseAssignmentOfUninitializedStructLocalAsync()
        {
            await RunTest(
                @"
using System;

public struct ElementCount
{
    public int uniqueCount;
    public int unfoundCount;
}

public class Program
{
    private static ElementCount CheckUniqueAndUnfoundElements(int count, bool returnIfUnfound)
    {
        ElementCount result;
        if (count == 0)
        {
            result.uniqueCount  = 0;
            result.unfoundCount = returnIfUnfound ? 1 : 0;
            return result;
        }
        result.uniqueCount  = count;
        result.unfoundCount = count - 1;
        return result;
    }

    public static void Main()
    {
        var a = CheckUniqueAndUnfoundElements(0, true);
        var b = CheckUniqueAndUnfoundElements(3, false);
        Console.WriteLine(a.uniqueCount + "","" + a.unfoundCount);
        Console.WriteLine(b.uniqueCount + "","" + b.unfoundCount);
    }
}
                ");
        }

        /// <summary>A bare struct local must be a fresh zeroed value each time its declaration runs, and
        /// a nested struct field must itself be an object so `o.Nested.Value = …` has somewhere to write.
        /// Non-struct and primitive locals keep their bare declaration — they are covered here only to
        /// show the change did not disturb them.</summary>
        [TestMethod]
        public async Task TestUninitializedStructLocalIsZeroedPerDeclarationAsync()
        {
            await RunTest(
                @"
using System;

public struct Inner
{
    public int Value;
}

public struct Outer
{
    public Inner  Nested;
    public string Name;
    public int    Count;
}

public class Program
{
    public static void Main()
    {
        for (int i = 0; i < 3; i++)
        {
            Outer o;
            o.Count        = i;
            o.Name         = ""set"";
            o.Nested.Value = i * 2;
            Console.WriteLine(o.Nested.Value + "","" + o.Name + "","" + o.Count);
        }

        int    n;
        string s;
        DateTime d;
        n = 5;
        s = ""hi"";
        d = new DateTime(2020, 1, 2);
        Console.WriteLine(n + "","" + s + "","" + d.Year);
    }
}
                ");
        }
    }
}
