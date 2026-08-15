namespace Transpose.SystemTextJson.Tests;

/// <summary>Arrays, lists, sets, dictionaries, the collection interfaces, and nesting between them.</summary>
[TestClass]
public sealed class CollectionTests : JsonTestBase
{
    [TestMethod]
    public async Task ArraysOfPrimitives() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(JsonSerializer.Serialize(new[] { 1, 2, 3 }));
                Console.WriteLine(JsonSerializer.Serialize(new[] { "a", null, "c" }));
                Console.WriteLine(JsonSerializer.Serialize(new[] { true, false }));
                Console.WriteLine(JsonSerializer.Serialize(new double[] { 1.5, 2 }));
                Console.WriteLine(JsonSerializer.Serialize(new int[0]));
            }
        }
        """);

    [TestMethod]
    public async Task ArraysRoundTrip() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public static class Program
        {
            public static void Main()
            {
                var back = JsonSerializer.Deserialize<int[]>("[1,2,3]");
                Console.WriteLine(back.Length + "/" + back[0] + "/" + back[2]);

                var strings = JsonSerializer.Deserialize<string[]>("[\"a\",null]");
                Console.WriteLine(strings.Length + "/" + (strings[1] ?? "<null>"));

                Console.WriteLine(JsonSerializer.Deserialize<int[]>("[]").Length);
            }
        }
        """);

    [TestMethod]
    public async Task ListsAndSets() => await RunAndCompare("""
        using System;
        using System.Collections.Generic;
        using System.Text.Json;

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(JsonSerializer.Serialize(new List<string> { "a", "b" }));
                Console.WriteLine(JsonSerializer.Serialize(new HashSet<int> { 1, 2, 3 }));

                var list = JsonSerializer.Deserialize<List<string>>("[\"a\",\"b\"]");
                Console.WriteLine(list.Count + "/" + list[1]);

                var set = JsonSerializer.Deserialize<HashSet<int>>("[1,2,2,3]");
                Console.WriteLine(set.Count + "/" + set.Contains(3));
            }
        }
        """);

    [TestMethod]
    public async Task CollectionInterfaceTargets() => await RunAndCompare("""
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Text.Json;

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(JsonSerializer.Deserialize<IList<int>>("[1,2]").Count);
                Console.WriteLine(JsonSerializer.Deserialize<ICollection<int>>("[1,2,3]").Count);
                Console.WriteLine(JsonSerializer.Deserialize<IEnumerable<int>>("[1,2,3,4]").Count());
                Console.WriteLine(JsonSerializer.Deserialize<IReadOnlyList<string>>("[\"a\"]").Count);
            }
        }
        """);

    [TestMethod]
    public async Task ACollectionValuedMemberThatIsOnlyIEnumerable() => await RunAndCompare("""
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Text.Json;

        public class T
        {
            public IEnumerable<string>     Items    { get; set; }
            public IReadOnlyList<int>      Numbers  { get; set; }
            public IReadOnlyCollection<int> Others  { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var t = JsonSerializer.Deserialize<T>("{\"Items\":[\"a\",\"b\"],\"Numbers\":[1,2,3],\"Others\":[9]}");
                Console.WriteLine(t.Items.Count() + "/" + t.Numbers.Count + "/" + t.Others.Count);
                Console.WriteLine(JsonSerializer.Serialize(t));
            }
        }
        """);

    [TestMethod]
    public async Task DictionariesWithStringKeys() => await RunAndCompare("""
        using System;
        using System.Collections.Generic;
        using System.Text.Json;

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }));
                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, string> { ["k"] = null }));

                var back = JsonSerializer.Deserialize<Dictionary<string, int>>("{\"a\":1,\"b\":2}");
                Console.WriteLine(back.Count + "/" + back["b"]);

                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, int>()));
            }
        }
        """);

    [TestMethod]
    public async Task DictionariesWithNonStringKeys() => await RunAndCompare("""
        using System;
        using System.Collections.Generic;
        using System.Text.Json;

        public enum Kind { Alpha = 1, Beta = 2 }

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<int, string> { [3] = "x" }));
                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<Kind, string> { [Kind.Beta] = "y" }));

                var ints = JsonSerializer.Deserialize<Dictionary<int, string>>("{\"3\":\"x\"}");
                Console.WriteLine(ints[3]);

                var enums = JsonSerializer.Deserialize<Dictionary<Kind, string>>("{\"Beta\":\"y\"}");
                Console.WriteLine(enums[Kind.Beta]);
            }
        }
        """);

    [TestMethod]
    public async Task DictionaryOfObjects() => await RunAndCompare("""
        using System;
        using System.Collections.Generic;
        using System.Text.Json;

        public class Item { public string Name { get; set; } public int Value { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var map = new Dictionary<string, Item>
                {
                    ["one"] = new Item { Name = "a", Value = 1 },
                    ["two"] = new Item { Name = "b", Value = 2 }
                };

                var json = JsonSerializer.Serialize(map);
                Console.WriteLine(json);

                var back = JsonSerializer.Deserialize<Dictionary<string, Item>>(json);
                Console.WriteLine(back.Count + "/" + back["two"].Name + "/" + back["two"].Value);
            }
        }
        """);

    [TestMethod]
    public async Task NestedCollections() => await RunAndCompare("""
        using System;
        using System.Collections.Generic;
        using System.Text.Json;

        public static class Program
        {
            public static void Main()
            {
                var value = new List<List<int>> { new List<int> { 1, 2 }, new List<int>(), new List<int> { 3 } };
                var json  = JsonSerializer.Serialize(value);
                Console.WriteLine(json);

                var back = JsonSerializer.Deserialize<List<List<int>>>(json);
                Console.WriteLine(back.Count + "/" + back[0].Count + "/" + back[1].Count + "/" + back[2][0]);

                var maps = new Dictionary<string, List<string>> { ["k"] = new List<string> { "a", "b" } };
                Console.WriteLine(JsonSerializer.Serialize(maps));
                Console.WriteLine(JsonSerializer.Deserialize<Dictionary<string, List<string>>>(JsonSerializer.Serialize(maps))["k"][1]);
            }
        }
        """);

    [TestMethod]
    public async Task ACollectionOfObjectsRoundTrips() => await RunAndCompare("""
        using System;
        using System.Collections.Generic;
        using System.Text.Json;

        public class Item { public string Name { get; set; } public int Value { get; set; } }
        public class Holder { public List<Item> Items { get; set; } public Item[] Array { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var value = new Holder
                {
                    Items = new List<Item> { new Item { Name = "a", Value = 1 }, new Item { Name = "b", Value = 2 } },
                    Array = new[] { new Item { Name = "c", Value = 3 } }
                };

                var json = JsonSerializer.Serialize(value);
                var back = JsonSerializer.Deserialize<Holder>(json);

                Console.WriteLine(back.Items.Count + "/" + back.Items[1].Name + "/" + back.Array[0].Value);
                Console.WriteLine(JsonSerializer.Serialize(back) == json);
            }
        }
        """);

    [TestMethod]
    public async Task ANullCollectionMemberStaysNull() => await RunAndCompare("""
        using System;
        using System.Collections.Generic;
        using System.Text.Json;

        public class T { public List<int> Items { get; set; } public int[] Array { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var json = JsonSerializer.Serialize(new T());
                Console.WriteLine(json);

                var back = JsonSerializer.Deserialize<T>(json);
                Console.WriteLine((back.Items == null) + "/" + (back.Array == null));
            }
        }
        """);
}
