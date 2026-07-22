using System.Threading.Tasks;

namespace Transpose.Translator.Tests;

/// <summary>
/// An array VALUE must carry its element type at runtime — <c>arr.GetType().GetElementType()</c>
/// (and <c>IsArray</c>, covariance, and the JSON serializer's byte[]/element-typed-array handling)
/// depend on it. Transpose emits array literals as <c>System.Array.init([…], element)</c> (h5 tags
/// every array literal the same way); a bare JS array literal would have no element type.
/// </summary>
[TestClass]
public class ArrayValueElementTypeTests : TranslatorTestBase
{
    [TestMethod]
    public async Task ArrayLiteralValueCarriesElementType()
    {
        await RunTest(@"
using System;
public class App
{
    public static void Main()
    {
        Console.WriteLine(new int[]{1,2,3}.GetType().GetElementType().Name);
        Console.WriteLine(new byte[]{1,2}.GetType().GetElementType().Name);
        Console.WriteLine(new string[]{""a""}.GetType().GetElementType().Name);
        int[] implicitLocal = {4,5};
        Console.WriteLine(implicitLocal.GetType().GetElementType().Name);
        var inferred = new[]{1.5,2.5};
        Console.WriteLine(inferred.GetType().GetElementType().Name);
        Console.WriteLine(new int[3].GetType().GetElementType().Name);
        Console.WriteLine(new int[0].GetType().GetElementType().Name);
    }
}");
    }

    [TestMethod]
    public async Task CollectionExpressionArrayCarriesElementType()
    {
        await RunTest(@"
using System;
public class App
{
    public static void Main()
    {
        int[] a = [1,2,3];
        Console.WriteLine(a.GetType().GetElementType().Name + ""|"" + a.Length + ""|"" + a[2]);
        byte[] b = [9,8];
        Console.WriteLine(b.GetType().GetElementType().Name);
        int[] spread = [0, ..a, 4];
        Console.WriteLine(spread.GetType().GetElementType().Name + ""|"" + spread.Length);
    }
}");
    }

    [TestMethod]
    public async Task JaggedArrayValueElementTypeIsTheInnerArray()
    {
        await RunTest(@"
using System;
public class App
{
    public static void Main()
    {
        var jagged = new int[][]{ new int[]{1,2}, new int[]{3} };
        var t = jagged.GetType();
        Console.WriteLine(t.IsArray + ""|"" + t.GetElementType().Name + ""|"" + t.GetElementType().GetElementType().Name);
        Console.WriteLine(jagged[0].GetType().GetElementType().Name);
    }
}");
    }

    [TestMethod]
    public async Task ArrayCovarianceAndIsChecksUseElementType()
    {
        await RunTest(@"
using System;
public class App
{
    public static void Main()
    {
        object o = new int[]{1,2,3};
        Console.WriteLine(o is int[]);
        Console.WriteLine(o is string[]);
        object b = new byte[]{1};
        Console.WriteLine(b is byte[]);
        Console.WriteLine(b is int[]);
    }
}");
    }
}
