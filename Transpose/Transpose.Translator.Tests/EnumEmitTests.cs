using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Coverage for every <c>[Enum(Emit.XXX)]</c> mode across the ways enum values are used in the
    /// Curiosity front-end: <c>ToString()</c>, string interpolation, equality, and being passed as an
    /// argument (both as the concrete enum type and boxed to <c>object</c>). These attributes are a
    /// Transpose/h5 codegen concern with no native-.NET equivalent (they change the JS-side runtime
    /// representation), so the tests run only under Transpose (<see cref="RunTestOnlyInTranspose"/>)
    /// and assert the expected JS output directly.
    ///
    /// Modes: Name/Value keep the numeric ordinal at runtime (ToString reverse-looks-up the member
    /// name); StringName* back the enum with strings (StringName = camelCase, StringNamePreserveCase
    /// = exact, StringNameLowerCase / StringNameUpperCase = case-folded), so the string IS the value;
    /// Name*Case keep the ordinal but fold the emitted member name. A single shared program defines
    /// all nine enums so their interaction is exercised together.
    /// </summary>
    [TestClass]
    public class EnumEmitTests : TranslatorTestBase
    {
        private const string Enums = @"
using System;
using Transpose;

[Enum(Emit.Name)]                   public enum EName        { Alpha = 0, BetaValue = 1 }
[Enum(Emit.Value)]                  public enum EValue       { Alpha = 0, BetaValue = 1 }
[Enum(Emit.StringName)]             public enum EStrName     { Alpha = 0, BetaValue = 1 }
[Enum(Emit.StringNamePreserveCase)] public enum EStrPreserve { Alpha = 0, BetaValue = 1 }
[Enum(Emit.StringNameLowerCase)]    public enum EStrLower    { Alpha = 0, BetaValue = 1 }
[Enum(Emit.StringNameUpperCase)]    public enum EStrUpper    { Alpha = 0, BetaValue = 1 }
[Enum(Emit.NamePreserveCase)]       public enum ENamePres    { Alpha = 0, BetaValue = 1 }
[Enum(Emit.NameLowerCase)]          public enum ENameLower   { Alpha = 0, BetaValue = 1 }
[Enum(Emit.NameUpperCase)]          public enum ENameUpper   { Alpha = 0, BetaValue = 1 }
public enum Plain                                            { Alpha = 0, BetaValue = 1 }
";

        [TestMethod]
        public async Task ToStringMatchesEmitModeAcrossAllVariants()
        {
            var output = await RunTestOnlyInTranspose(Enums + @"
public class Program
{
    public static void Main()
    {
        Console.WriteLine(Plain.BetaValue.ToString());
        Console.WriteLine(EName.BetaValue.ToString());
        Console.WriteLine(EValue.BetaValue.ToString());
        Console.WriteLine(EStrName.BetaValue.ToString());
        Console.WriteLine(EStrPreserve.BetaValue.ToString());
        Console.WriteLine(EStrLower.BetaValue.ToString());
        Console.WriteLine(EStrUpper.BetaValue.ToString());
        Console.WriteLine(ENamePres.BetaValue.ToString());
        Console.WriteLine(ENameLower.BetaValue.ToString());
        Console.WriteLine(ENameUpper.BetaValue.ToString());
    }
}");
            Assert.AreEqual(string.Join("\n", new[]
            {
                "BetaValue", // Plain (numeric → member name)
                "BetaValue", // EName
                "BetaValue", // EValue
                // StringName* modes back the enum with a string whose distinct KEY stays the member's
                // PascalCase name, so ToString reverse-maps the value to that name.
                "BetaValue", // EStrName
                "BetaValue", // EStrPreserve
                "BetaValue", // EStrLower
                "BetaValue", // EStrUpper
                // Name*Case modes keep the numeric ordinal but FOLD the emitted member name/key, so
                // ToString returns the folded name.
                "BetaValue", // ENamePres
                "betavalue", // ENameLower
                "BETAVALUE", // ENameUpper
            }), output);
        }

        [TestMethod]
        public async Task StringInterpolationMatchesToString()
        {
            var output = await RunTestOnlyInTranspose(Enums + @"
public class Program
{
    public static void Main()
    {
        Console.WriteLine($""{Plain.BetaValue}|{EStrPreserve.BetaValue}|{EStrLower.BetaValue}|{EStrUpper.BetaValue}|{EValue.BetaValue}"");
    }
}");
            Assert.AreEqual("BetaValue|BetaValue|BetaValue|BetaValue|BetaValue", output);
        }

        [TestMethod]
        public async Task EqualityWorksForEveryVariant()
        {
            var output = await RunTestOnlyInTranspose(Enums + @"
public class Program
{
    static string YN(bool b) => b ? ""Y"" : ""N"";
    public static void Main()
    {
        Console.WriteLine(YN(Plain.BetaValue == Plain.BetaValue)        + YN(Plain.Alpha == Plain.BetaValue));
        Console.WriteLine(YN(EValue.BetaValue == EValue.BetaValue)      + YN(EValue.Alpha == EValue.BetaValue));
        Console.WriteLine(YN(EStrName.BetaValue == EStrName.BetaValue)  + YN(EStrName.Alpha == EStrName.BetaValue));
        Console.WriteLine(YN(EStrPreserve.BetaValue == EStrPreserve.BetaValue) + YN(EStrPreserve.Alpha == EStrPreserve.BetaValue));
        Console.WriteLine(YN(EStrLower.BetaValue == EStrLower.BetaValue) + YN(EStrLower.Alpha == EStrLower.BetaValue));
        Console.WriteLine(YN(EStrUpper.BetaValue == EStrUpper.BetaValue) + YN(EStrUpper.Alpha == EStrUpper.BetaValue));
        Console.WriteLine(YN(ENameLower.BetaValue.Equals(ENameLower.BetaValue)) + YN(ENameLower.Alpha.Equals(ENameLower.BetaValue)));
    }
}");
            Assert.AreEqual(string.Join("\n", new[] { "YN", "YN", "YN", "YN", "YN", "YN", "YN" }), output);
        }

        [TestMethod]
        public async Task PassedAsArgumentPreservesValue()
        {
            // Passing to a method typed as the concrete enum, and boxed to `object` (the latter is
            // what exercises the box-with-toString path used when an enum flows through a non-generic
            // Action<object>/Dictionary value etc.).
            var output = await RunTestOnlyInTranspose(Enums + @"
public class Program
{
    static string Concrete(EStrPreserve e) => e.ToString();
    static string Boxed(object o)           => o.ToString();
    public static void Main()
    {
        Console.WriteLine(Concrete(EStrPreserve.BetaValue));
        Console.WriteLine(Boxed(EStrPreserve.BetaValue));
        Console.WriteLine(Boxed(Plain.BetaValue));
        Console.WriteLine(Boxed(EStrLower.BetaValue));
    }
}");
            Assert.AreEqual(string.Join("\n", new[] { "BetaValue", "BetaValue", "BetaValue", "betavalue" }), output);
        }

        [TestMethod]
        public async Task SwitchOnStringBackedEnumMatches()
        {
            // The front-end frequently switches on string-backed enums; the case labels are the same
            // string members, so the comparison must hold at runtime.
            var output = await RunTestOnlyInTranspose(Enums + @"
public class Program
{
    static string Which(EStrPreserve e)
    {
        switch (e)
        {
            case EStrPreserve.Alpha:     return ""a"";
            case EStrPreserve.BetaValue: return ""b"";
            default:                     return ""?"";
        }
    }
    public static void Main()
    {
        Console.WriteLine(Which(EStrPreserve.Alpha));
        Console.WriteLine(Which(EStrPreserve.BetaValue));
    }
}");
            Assert.AreEqual("a\nb", output);
        }
    }
}
