using System.Threading.Tasks;

namespace H5.Translator.Roslyn.Tests;

[TestClass]
public class CollectionsTests : TranslatorTestBase
{
    [TestMethod]
    public async Task ListBasics()
    {
        var code = """
using System;
using System.Collections.Generic;
public class Program
{
    public static void Main()
    {
        var list = new List<int> { 1, 2, 3 };
        list.Add(4);
        list.Insert(0, 0);
        Console.WriteLine("Count=" + list.Count);
        int sum = 0;
        foreach (var x in list) sum += x;
        Console.WriteLine("Sum=" + sum);
        Console.WriteLine("First=" + list[0] + " Last=" + list[list.Count - 1]);
        Console.WriteLine("Contains 3: " + list.Contains(3));
        Console.WriteLine("IndexOf 2: " + list.IndexOf(2));
        list[1] = 99;
        Console.WriteLine("After set: " + list[1]);
        list.RemoveAt(0);
        Console.WriteLine("After remove: " + list.Count);
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task DictionaryBasics()
    {
        var code = """
using System;
using System.Collections.Generic;
public class Program
{
    public static void Main()
    {
        var dict = new Dictionary<string, int> { { "a", 1 }, { "b", 2 } };
        dict["c"] = 3;
        Console.WriteLine("dict[b]=" + dict["b"]);
        Console.WriteLine("has c: " + dict.ContainsKey("c"));
        Console.WriteLine("has z: " + dict.ContainsKey("z"));
        Console.WriteLine("count=" + dict.Count);
        foreach (var kv in dict) Console.WriteLine(kv.Key + "=" + kv.Value);
        if (dict.TryGetValue("a", out int v)) Console.WriteLine("a=" + v);
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task HashSetBasics()
    {
        var code = """
using System;
using System.Collections.Generic;
public class Program
{
    public static void Main()
    {
        var set = new HashSet<int>();
        Console.WriteLine(set.Add(1));
        Console.WriteLine(set.Add(1));
        set.Add(2);
        Console.WriteLine("Count=" + set.Count);
        Console.WriteLine("Contains 2: " + set.Contains(2));
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task ArrayBasics()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        int[] arr = new int[] { 5, 10, 15 };
        Console.WriteLine("Length=" + arr.Length);
        arr[1] = 20;
        int total = 0;
        for (int i = 0; i < arr.Length; i++) total += arr[i];
        Console.WriteLine("Total=" + total);
        int[] zeros = new int[3];
        Console.WriteLine(zeros[0] + "," + zeros[1] + "," + zeros[2]);
    }
}
""";
        await RunTest(code);
    }
}
