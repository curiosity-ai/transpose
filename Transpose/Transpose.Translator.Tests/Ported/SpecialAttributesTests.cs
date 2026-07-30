using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests.Ported
{
    [TestClass]
    public class SpecialAttributesTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task TestObjectLiteral_Default()
        {
            var code = @"
using Transpose;
using System;

[ObjectLiteral]
public class MyConfig
{
    public string Name { get; set; }
    public int Value { get; set; }
}

public class Program
{
    public static void Main()
    {
        var c = new MyConfig { Name = ""Test"", Value = 123 };

        // Check if it's a plain object
        bool isPlain = Script.Write<bool>(""Object.getPrototypeOf({0}) === Object.prototype"", c);
        Console.WriteLine(""IsPlain: "" + isPlain);
        Console.WriteLine(""Name: "" + c.Name);
        Console.WriteLine(""Value: "" + c.Value);

        // Verify key presence
        bool hasName = Script.Write<bool>(""'name' in {0}"", c);
        bool hasNamePascal = Script.Write<bool>(""'Name' in {0}"", c);
        bool hasValue = Script.Write<bool>(""'value' in {0}"", c);
        bool hasValuePascal = Script.Write<bool>(""'Value' in {0}"", c);
        Console.WriteLine(""Has 'name': "" + hasName);
        Console.WriteLine(""Has 'Name': "" + hasNamePascal);
        Console.WriteLine(""Has 'value': "" + hasValue);
        Console.WriteLine(""Has 'Value': "" + hasValuePascal);

        if (!isPlain) throw new Exception(""Expected plain object"");
        if (hasName) throw new Exception(""Property 'name' should not exist"");
        if (!hasNamePascal) throw new Exception(""Property 'Name' should exist"");
        if (hasValue) throw new Exception(""Property 'value' should not exist"");
        if (!hasValuePascal) throw new Exception(""Property 'Value' should exist"");
    }
}";
            await RunTest(code, skipRoslyn: true);
        }

        [TestMethod]
        public async Task TestObjectLiteral_InitializerMode()
        {
             // Test ObjectInitializationMode.Initializer
             // Based on previous run, it seems Initializer mode INCLUDES property initializers,
             // and ObjectLiteral defaults to PascalCase (Preserve casing).
             var code = @"
using Transpose;
using System;

[ObjectLiteral(ObjectInitializationMode.Initializer)]
public class MyOptions
{
    public int A { get; set; } = 10;
    public int B { get; set; } = 20;
}

public class Program
{
    public static void Main()
    {
        var o = new MyOptions { A = 99 };

        Console.WriteLine(""A: "" + o.A);

        // Check if 'B' is present in the underlying JS object
        bool hasB = Script.Write<bool>(""'B' in {0}"", o);
        Console.WriteLine(""Has 'B': "" + hasB);

        // If 'B' is present, it should have the initialized value 20.
        Console.WriteLine(""B value: "" + o.B);

        if (!hasB) throw new Exception(""Property 'B' should exist in Initializer mode because it has a property initializer"");
        if (o.B != 20) throw new Exception(""Property 'B' should have value 20"");
    }
}";
            await RunTest(code, skipRoslyn: true);
        }

        [TestMethod]
        public async Task TestObjectLiteral_IgnoreMode()
        {
             // Test ObjectInitializationMode.Ignore
             // Based on previous run, Ignore mode EXCLUDES property initializers (unless in object init).
             var code = @"
using Transpose;
using System;

[ObjectLiteral(ObjectInitializationMode.Ignore)]
public class MyIgnore
{
    public int X { get; set; } = 5;
    public int Y { get; set; }
}

public class Program
{
    public static void Main()
    {
        var o = new MyIgnore { Y = 10 };

        Console.WriteLine(""Y: "" + o.Y);

        // X has initializer but not set in object init.
        // In Ignore mode, X should be missing.
        bool hasX = Script.Write<bool>(""'X' in {0}"", o);
        Console.WriteLine(""Has 'X': "" + hasX);
        Console.WriteLine(""X value: "" + o.X);

        if (hasX) throw new Exception(""Property 'X' should NOT exist in Ignore mode"");
    }
}";
            await RunTest(code, skipRoslyn: true);
        }

        [TestMethod]
        public async Task TestNameAttribute()
        {
            var code = @"
using Transpose;
using System;

namespace TestNamespace
{
    public class Program
    {
        [Name(""customMethodName"")]
        public static void OriginalName()
        {
            Console.WriteLine(""Called customMethodName"");
        }

        public static void Main()
        {
            OriginalName();

            // Check if the function exists with the custom name on the class
            // Program is compiled to TestNamespace.Program
            bool exists = Script.Write<bool>(""typeof TestNamespace.Program.customMethodName === 'function'"");
            Console.WriteLine(""Custom Name Exists: "" + exists);

            // Verify original name does not exist
            bool originalExists = Script.Write<bool>(""typeof TestNamespace.Program.OriginalName === 'function'"");
            Console.WriteLine(""Original Name Exists: "" + originalExists);

            if (!exists) throw new Exception(""customMethodName should exist"");
            if (originalExists) throw new Exception(""OriginalName should not exist"");
        }
    }
}
";
            await RunTest(code, skipRoslyn: true);
        }

        [TestMethod]
        public async Task TestTemplateAttribute()
        {
            var code = @"
using Transpose;
using System;

public class Utils
{
    [Template(""console.log('Template: ' + {0})"")]
    public static extern void CustomLog(string message);
}

public class Program
{
    public static void Main()
    {
        Utils.CustomLog(""Hello World"");
    }
}";
            // Expect output to contain "Template: Hello World"
            // The console output is captured and returned.
            // We can check it manually in the test runner output or rely on standard behavior.
            // But since we are skipping Roslyn, we can't compare output against Roslyn.
            // However, RunTest returns the output, so we could assert on it if we wanted.
            // For now, printing to console is what was requested ("prints enough info to verify").
            // The CustomLog does exactly that.

            await RunTest(code, skipRoslyn: true);
        }

        // Each expected value below is the SHIPPED CONTRACT of its [Enum(Emit.X)] mode — a library
        // author picks a mode precisely to pin what its members look like at runtime, so these must
        // never be "updated" to match a changed emitter. See EnumEmitModeTests for the full nine-mode
        // table (including the enum→string paths and [Name] overrides), which is the canonical
        // statement of the contract; this ported test covers the boxing (enum → object) side of it.
        //
        // Two of these expectations were wrong as originally ported and were corrected once the
        // emitter was fixed: a Value-mode member boxed to a bare number, so `((object)EnumValue.A)`
        // reported "1" and typed as Int32 where native .NET reports "A" typed as the enum. Value mode
        // chooses the ordinal as the runtime *representation*; it does not throw the name away (the
        // enum's runtime object still carries the name table), and Tesserae's UIcons/Emoji read those
        // names back to build their CSS classes.
        [TestMethod]
        public async Task TestEnumAttribute()
        {
            var code = @"
using Transpose;
using System;

[Enum(Emit.Value)]
public enum EnumValue { A = 1, B = 2 }

[Enum(Emit.StringName)]
public enum EnumString { First, Second }

[Enum(Emit.StringNamePreserveCase)]
public enum EnumPreserve { MixedCase, UPPERCASE }

[Enum(Emit.StringNameLowerCase)]
public enum EnumLower { SomeValue }

[Enum(Emit.StringNameUpperCase)]
public enum EnumUpper { otherValue }

public class Program
{
    public static void Main()
    {
        // Emit.Value: the ordinal is the runtime representation, but the enum keeps its runtime type
        // object (and its name table), so boxing carries the enum type and ToString() is the NAME —
        // exactly as native .NET. Casting back to the enum recovers the ordinal.
        object valA = EnumValue.A;
        Console.WriteLine(""ValA: "" + valA);
        if (valA.ToString() != ""A"") throw new Exception(""EnumValue.A should stringify to 'A'"");
        if (valA is string) throw new Exception(""EnumValue.A must not box to a string"");
        if ((int)(EnumValue)valA != 1) throw new Exception(""EnumValue.A should unbox to the ordinal 1"");
        if (EnumValue.B.ToString() != ""B"") throw new Exception(""EnumValue.B should stringify to 'B'"");

        // Emit.StringName: the runtime value IS the name string (camelCased first letter), and it
        // boxes to that raw string — the mode exists so a value can be handed straight to a JS API,
        // so `is string` being true is part of the contract (a documented divergence from native).
        object valFirst = EnumString.First;
        Console.WriteLine(""EnumString.First: "" + valFirst);
        if (!(valFirst is string)) throw new Exception(""EnumString should emit string"");
        if ((string)valFirst != ""first"") throw new Exception(""EnumString.First should be 'first'"");
        // ToString() reports the same string: a StringName* member's JS slot carries the emitted
        // string, so the name table hands back the value a consumer sees (h5: `first: ""first""`).
        if (EnumString.First.ToString() != ""first"") throw new Exception(""EnumString.First should stringify to 'first'"");
        if (valFirst.GetType() != typeof(EnumString)) throw new Exception(""a boxed string-mode enum keeps its enum type"");

        // Emit.StringNamePreserveCase
        object valMixed = EnumPreserve.MixedCase;
        Console.WriteLine(""EnumPreserve.MixedCase: "" + valMixed);
        if ((string)valMixed != ""MixedCase"") throw new Exception(""EnumPreserve.MixedCase should be 'MixedCase'"");

        // Emit.StringNameLowerCase
        object valLower = EnumLower.SomeValue;
        Console.WriteLine(""EnumLower.SomeValue: "" + valLower);
        if ((string)valLower != ""somevalue"") throw new Exception(""EnumLower.SomeValue should be 'somevalue'"");

        // Emit.StringNameUpperCase
        object valUpper = EnumUpper.otherValue;
        Console.WriteLine(""EnumUpper.otherValue: "" + valUpper);
        if ((string)valUpper != ""OTHERVALUE"") throw new Exception(""EnumUpper.otherValue should be 'OTHERVALUE'"");
    }
}";
            await RunTest(code, skipRoslyn: true);
        }
    }
}
