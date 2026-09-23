using System.Threading.Tasks;

namespace Transpose.Translator.Tests;

/// <summary>
/// An array VALUE must carry its element type at runtime — <c>arr.GetType().GetElementType()</c>
/// (and <c>IsArray</c>, covariance, and the JSON serializer's byte[]/element-typed-array handling)
/// depend on it. Transpose emits array literals as <c>System.Array.init([…], element)</c> (h5 tags
/// every array literal the same way); a bare JS array literal would have no element type.
/// <para>
/// An array that reaches C# from JavaScript — <c>JSON.parse</c>, a foreign-JS call — carries none,
/// and that is <c>object[]</c>: <c>is object[]</c> is true and <c>is string[]</c> is false. An
/// explicit cast <c>(T[])x</c> is the one place the element type is written down, so it MARKS the
/// value (<c>System.Array.markElementType</c>) and every later <c>is</c> / <c>as</c> / pattern
/// agrees with it. Those cases have no .NET counterpart to diff against — every native array has an
/// element type — so they run with <c>skipRoslyn</c> and assert the JS output directly.
/// </para>
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

    [TestMethod]
    public async Task ReferenceArrayIsChecksFollowCovariance()
    {
        await RunTest(@"
using System;
public class App
{
    public static void Main()
    {
        object strings = new string[]{""a""};
        Console.WriteLine(strings is string[]);
        Console.WriteLine(strings is object[]);
        Console.WriteLine(strings is int[]);
        object objects = new object[]{""a""};
        Console.WriteLine(objects is object[]);
        Console.WriteLine(objects is string[]);
    }
}");
    }

    /// <summary>
    /// Marking must never overwrite an element type the value already has: an upcast would otherwise
    /// widen a known <c>string[]</c> to <c>object[]</c>, and a downcast of a deserialized array would
    /// replace what the serializer recorded. Both directions are exercised here.
    /// </summary>
    [TestMethod]
    public async Task CastToAnArrayNeverOverwritesAKnownElementType()
    {
        await RunTest(@"
using System;
public class App
{
    public static void Main()
    {
        object o = new string[]{""a"",""b""};
        var asObjects = (object[])o;
        Console.WriteLine(asObjects.Length + ""|"" + o.GetType().GetElementType().Name + ""|"" + (o is string[]));
        object[] wide = new string[]{""c""};
        var narrow = (string[])wide;
        Console.WriteLine(narrow[0] + ""|"" + wide.GetType().GetElementType().Name + ""|"" + (wide is string[]));
    }
}");
    }

    /// <summary>An array from JavaScript carries no element type, which makes it <c>object[]</c>.</summary>
    [TestMethod]
    public async Task PlainJavaScriptArrayIsObjectArray()
    {
        var output = await RunTest(@"
using System;
using Transpose;
public class App
{
    public static void Main()
    {
        object strings = Script.Write<object>(""(['a','b'])"");
        Console.WriteLine(strings is object[]);
        Console.WriteLine(strings is string[]);
        Console.WriteLine(strings is int[]);
        object numbers = Script.Write<object>(""JSON.parse('[1,2]')"");
        Console.WriteLine(numbers is object[]);
        Console.WriteLine(numbers is int[]);
        object empty = Script.Write<object>(""([])"");
        Console.WriteLine(empty is object[]);
        Console.WriteLine(empty is string[]);
    }
}", skipRoslyn: true);

        Assert.AreEqual("True\nFalse\nFalse\nTrue\nFalse\nTrue\nFalse", output,
            "an array with no element type is object[], never T[]\n" + output);
    }

    /// <summary>
    /// The explicit cast is the author writing the element type down, so it is recorded on the value
    /// and every later type test agrees with it.
    /// </summary>
    [TestMethod]
    public async Task ExplicitCastMarksAPlainJavaScriptArray()
    {
        var output = await RunTest(@"
using System;
using Transpose;
public class App
{
    public static void Main()
    {
        object o = Script.Write<object>(""(['a','b'])"");
        Console.WriteLine(o is string[]);
        var arr = (string[])o;
        Console.WriteLine(arr.Length + ""|"" + arr[0]);
        Console.WriteLine(o is string[]);
        Console.WriteLine(o is object[]);
        Console.WriteLine(o is int[]);
        Console.WriteLine((o as string[]) != null);
        Console.WriteLine(o.GetType().GetElementType().Name);
    }
}", skipRoslyn: true);

        Assert.AreEqual("False\n2|a\nTrue\nTrue\nFalse\nTrue\nString", output,
            "an explicit cast to T[] must record T as the array's element type\n" + output);
    }

    /// <summary>
    /// What does NOT mark: <c>as</c> and <c>is</c> are questions rather than assertions, so marking
    /// there would let the question answer itself; and a multidimensional array's representation is
    /// its dimensions, which a flat JS array does not have and a cast cannot invent.
    /// </summary>
    [TestMethod]
    public async Task TypeTestsAndMultidimensionalCastsDoNotMark()
    {
        var output = await RunTest(@"
using System;
using Transpose;
public class App
{
    public static void Main()
    {
        object asked = Script.Write<object>(""(['a'])"");
        Console.WriteLine((asked as string[]) == null);
        Console.WriteLine(asked is string[]);
        object tested = Script.Write<object>(""(['a'])"");
        if (tested is string[] hit) Console.WriteLine(""matched "" + hit.Length);
        Console.WriteLine(tested is string[]);
        object flat = Script.Write<object>(""(['a'])"");
        var md = (string[,])flat;
        Console.WriteLine(flat is string[,]);
        Console.WriteLine(flat is object[]);
    }
}", skipRoslyn: true);

        Assert.AreEqual("True\nFalse\nFalse\nFalse\nTrue", output,
            "only an explicit single-rank array cast marks\n" + output);
    }

    /// <summary>
    /// A TYPED array (Uint8Array &amp; co.) is an array here too, and also carries no element type of its
    /// own — but it is not a plain JS array and must keep matching byte[]/int[]/… through the typed-array
    /// path rather than being read as object[]. Treating "no element type" as object[] across the board
    /// made <c>someUint8Array is byte[]</c> false, which is what this pins.
    /// </summary>
    [TestMethod]
    public async Task TypedArraysStillMatchTheirPrimitiveElementType()
    {
        var output = await RunTest(@"
using System;
using Transpose;
public class App
{
    public static void Main()
    {
        object bytes = Script.Write<object>(""(new Uint8Array([1,2,3]))"");
        Console.WriteLine(bytes is byte[]);
        object ints = Script.Write<object>(""(new Int32Array([1,2]))"");
        Console.WriteLine(ints is int[]);
        object plain = Script.Write<object>(""JSON.parse('[1,2,3]')"");
        Console.WriteLine(plain is byte[]);
    }
}", skipRoslyn: true);

        Assert.AreEqual("True\nTrue\nFalse", output,
            "a typed array keeps its primitive element match; a plain JS array of numbers is not byte[]\n" + output);
    }
}
