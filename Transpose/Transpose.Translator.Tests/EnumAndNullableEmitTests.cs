using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests;

/// <summary>
/// Two emissions that named a type for no reason. Both are behaviour-preserving in a single bundle
/// and matter in module mode, where every emitted type reference is a dependency edge that decides
/// what an application has to fetch before it can start.
/// </summary>
[TestClass]
public class EnumAndNullableEmitTests : TranslatorTestBase
{
    private static string Js(string source) => new RoslynTranslator().Translate(source).Javascript ?? "";

    // ---- default(T?) --------------------------------------------------------

    private const string OptionalNullableEnum = @"
public enum Icon { None, Bug, Star }
public class P
{
    public static string M(Icon? icon = null) { return icon.HasValue ? ""set"" : ""unset""; }
}";

    [TestMethod]
    public void ADefaultNullableEmitsNullRatherThanNamingItsTypeArgument()
    {
        var js = Js(OptionalNullableEnum);

        Assert.IsFalse(js.Contains("getDefaultValue(System.Nullable$1"),
            "default(T?) is null whatever T is, so naming T is a dependency edge bought for nothing");
        StringAssert.Contains(js, "icon = null");
    }

    [TestMethod]
    public async Task ADefaultNullableStillBehavesLikeNative()
    {
        // The runtime's Nullable$1(T).getDefaultValue() returned null for every T, so this is the
        // same value by a shorter route — and every shape of it has to keep agreeing with .NET.
        await RunTest(@"
using System;
public enum Icon { None, Bug, Star }
public struct Point { public int X; }
public class Program
{
    static string Describe(Icon? icon = null) { return icon.HasValue ? icon.Value.ToString() : ""(none)""; }
    public static void Main()
    {
        Console.WriteLine(Describe());
        Console.WriteLine(Describe(Icon.Bug));
        Icon? a = default;              Console.WriteLine(a.HasValue);
        int? b = default;               Console.WriteLine(b.HasValue);
        Point? c = default;             Console.WriteLine(c.HasValue);
        DateTime? d = default;          Console.WriteLine(d.HasValue);
        Console.WriteLine(default(Icon?) == null);
        Console.WriteLine((default(int?) ?? 7));
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
    }

    [TestMethod]
    public async Task ANonNullableStructDefaultIsUnaffected()
    {
        // The change is scoped to Nullable<T>; a real struct still needs its zeroed value built.
        StringAssert.Contains(Js(@"
using System;
public struct Point { public int X; public int Y; }
public class P { public static Point M() { return default(Point); } }"),
            "getDefaultValue(", "a struct default still goes through the runtime");

        await RunTest(@"
using System;
public struct Point { public int X; public int Y; }
public class Program
{
    public static void Main()
    {
        var p = default(Point);
        Console.WriteLine(p.X + "","" + p.Y);
        Console.WriteLine(default(DateTime).Ticks);
        Console.WriteLine(default(int));
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
    }

    // ---- ToString on a string-backed enum ------------------------------------

    private const string StringBackedEnum = @"
using System;
using Transpose;
[Enum(Emit.StringName)]
public enum Weight
{
    [Name(""regular"")] Regular,
    [Name(""bold"")]    Bold,
}";

    [TestMethod]
    public void ToStringOnAStringBackedEnumEmitsTheValueRatherThanATableLookup()
    {
        // Its runtime value already IS the string, so System.Enum.toString could only ever find the
        // key it was handed — a lookup that costs a call and names the enum type.
        var js = Js(StringBackedEnum + @"
public class P
{
    public static string Concat(Weight w)      { return ""x-"" + w; }
    public static string Explicit(Weight w)    { return w.ToString(); }
    public static string Interpolated(Weight w) { return $""{w}""; }
}");

        StringAssert.Contains(js, @"""x-"" + TransposeR.toStr(w)");
        Assert.AreEqual(3, System.Text.RegularExpressions.Regex.Matches(js, @"TransposeR\.toStr\(w\)").Count,
            "every way of rendering the value should take it directly");
        Assert.IsFalse(js.Contains("System.Enum.toString(Weight, w)"),
            "none of them should look the value up in the enum's name table");
    }

    [TestMethod]
    public void BoxingAStringBackedEnumStillCarriesItsType()
    {
        // The deliberate exception. A box has to know which enum it came from, because GetType() and
        // a later ToString() off `object` are answered from it — so this reference is the point,
        // not an oversight.
        StringAssert.Contains(Js(StringBackedEnum + @"
public class P { public static object M(Weight w) { return (object)w; } }"),
            "Transpose.box(w, Weight");
    }

    [TestMethod]
    public async Task AStringBackedEnumStillRendersItsMembers()
    {
        var output = await RunTest(StringBackedEnum + @"
public class Program
{
    public static void Main()
    {
        Console.WriteLine(Weight.Bold.ToString());
        Console.WriteLine(""x-"" + Weight.Regular);
        Console.WriteLine($""{Weight.Bold}"");
        Weight w = Weight.Regular;
        Console.WriteLine(w.ToString());
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>", skipRoslyn: true);

        StringAssert.Contains(output, "bold");
        StringAssert.Contains(output, "x-regular");
    }

    [TestMethod]
    public void AValueModeEnumStillUsesItsNameTable()
    {
        // The other modes are numeric at runtime and genuinely need the lookup: the [Name] is what a
        // caller expects back, not the ordinal.
        StringAssert.Contains(Js(@"
using System;
using Transpose;
[Enum(Emit.Value)]
public enum Icon { [Name(""fi-bug"")] Bug, [Name(""fi-star"")] Star }
public class P { public static string M(Icon i) { return ""c-"" + i; } }"),
            "System.Enum.toString", "a Value-mode enum's name lives only in its table");
    }
}
