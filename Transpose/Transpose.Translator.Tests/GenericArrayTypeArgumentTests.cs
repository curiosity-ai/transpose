using System.Threading.Tasks;

namespace Transpose.Translator.Tests;

/// <summary>
/// Tests that an array type used as a generic type argument is reified to its concrete runtime
/// array type — <c>System.Array.type(element, rank)</c> — rather than the bare <c>System.Array</c>
/// base. The latter has no element type, so anything reflecting over the threaded argument (e.g.
/// JsonConvert.DeserializeObject&lt;T[]&gt;) misidentifies it as a non-array.
/// </summary>
[TestClass]
public class GenericArrayTypeArgumentTests : TranslatorTestBase
{
    [TestMethod]
    public async Task ArrayTypeArgumentIsReflectableAsTypedArray()
    {
        await RunTest(@"
using System;
public class App
{
    static string Describe<T>()
    {
        var t = typeof(T);
        return t.IsArray + "" "" + (t.IsArray ? t.GetElementType().Name : ""-"");
    }
    public static void Main()
    {
        Console.WriteLine(Describe<int[]>());
        Console.WriteLine(Describe<string[]>());
        Console.WriteLine(Describe<int>());
    }
}");
    }

    [TestMethod]
    public async Task JaggedAndMultiDimArrayTypeArgumentsReify()
    {
        await RunTest(@"
using System;
public class App
{
    // Walk the element chain instead of the outer array's Name: a jagged array's element is itself
    // an array (so it reifies with its own element type) whereas a rank-N array's element is the
    // scalar — which is what distinguishes the two reifications, independent of how the runtime
    // renders a multidimensional array's display name.
    static string Describe<T>()
    {
        var t = typeof(T);
        var e = t.IsArray ? t.GetElementType() : null;
        var e2 = (e != null && e.IsArray) ? e.GetElementType() : null;
        return t.IsArray + ""|"" + (e == null ? ""-"" : e.Name) + ""|"" + (e2 == null ? ""-"" : e2.Name);
    }
    public static void Main()
    {
        Console.WriteLine(Describe<int[]>());
        Console.WriteLine(Describe<int[][]>());
        Console.WriteLine(Describe<int[,]>());
        Console.WriteLine(Describe<string[,,]>());
    }
}");
    }
}
