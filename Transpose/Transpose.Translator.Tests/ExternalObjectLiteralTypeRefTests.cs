using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests;

/// <summary>
/// An <c>[ObjectLiteral]</c> type from an EXTERNAL binding library has no runtime type object:
/// nothing emits a <c>Transpose.define</c> for it (it is external) and the browser has no global of
/// that name (it is a WebIDL dictionary — dom.ScrollIntoViewOptions, dom.AddEventListenerOptions, …
/// — not a class). Naming it therefore emitted a reference to something that never exists, and the
/// failure only surfaced at run time, in one of two shapes depending on whether something else had
/// put the namespace object there:
///
///   <c>ReferenceError: Ext is not defined</c>                          (nothing did)
///   <c>Cannot read properties of undefined (reading 'constructor')</c> (something did — that is
///   <c>Transpose.is</c> reading its type argument; <c>'$$name'</c> is the same thing one frame on)
///
/// <para>
/// <see cref="ObjectLiteralTypeTestTests"/> covers the other half of this, and the two do not
/// overlap: a type test that asks an undecidable question (<c>object o; o is Lit</c>) is rejected
/// outright by TransposeR0005, literal or external literal alike. What is left here is every
/// position that stays legal — the null check that scanner deliberately keeps (the value's static
/// type already converts, so the test can only be asking whether it is null), a generic's type
/// argument, and an array's element token, none of which any type-test rule looks at.
/// </para>
///
/// <para>
/// Instances of such a type ARE plain JS objects, so its runtime type is System.Object — the answer
/// <c>dynamic</c> and anonymous types already get in <c>TypeRefCore</c>, and the one the BCL's own
/// plain-object bindings state by hand (<c>[Name("System.Object")]</c> on
/// <c>Transpose.ObjectLiteral</c> and <c>Union&lt;…&gt;</c>).
/// </para>
/// </summary>
[TestClass]
public class ExternalObjectLiteralTypeRefTests : TranslatorTestBase
{
    private const string Bindings = """
using System;
using System.Collections.Generic;
using Transpose;

namespace Ext
{
    [External] [ObjectLiteral] public class Thing { public string a { get; set; } }
    [External] [ObjectLiteral] public class Derived : Thing { }
}

""";

    /// <summary>The test TransposeR0005 keeps as sound: the value's static type already converts to
    /// the target, so it can only be a null check — and <c>Transpose.is</c> answers that correctly,
    /// as long as it is handed a type argument that exists. Node-only: an [External] type has no
    /// native counterpart to run against, so the expectations below are what .NET prints.</summary>
    [TestMethod]
    public async Task TheNullCheckThatStaysLegalRunsInsteadOfThrowing()
    {
        var output = await RunTest(Bindings + """
public class Program
{
    public static void Main()
    {
        Ext.Thing present = Script.Write<Ext.Thing>("({ a: 'x' })");
        Ext.Thing absent = null;
        Ext.Derived derived = Script.Write<Ext.Derived>("({ a: 'y' })");

        Console.WriteLine("present:" + (present is Ext.Thing));
        Console.WriteLine("absent:" + (absent is Ext.Thing));
        Console.WriteLine("upcast:" + (derived is Ext.Thing));
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

        StringAssert.Contains(output, "present:True", output);
        StringAssert.Contains(output, "absent:False", output);
        StringAssert.Contains(output, "upcast:True", output);
        StringAssert.Contains(output, "<<DONE>>", output);
    }

    /// <summary>A generic's type argument and an array's element token are not type tests, so no
    /// scanner looks at them — they are the positions that were left naming a type that does not
    /// exist.</summary>
    [TestMethod]
    public async Task AGenericArgumentOverAPlainObjectExternalRunsInsteadOfThrowing()
    {
        var output = await RunTest(Bindings + """
public class Program
{
    static int CountOf<T>(List<T> items) => items.Count;

    public static void Main()
    {
        var list = new List<Ext.Thing>();
        list.Add(Script.Write<Ext.Thing>("({ a: 'x' })"));
        Console.WriteLine("list:" + list.Count);
        Console.WriteLine("generic:" + CountOf(list));
        Console.WriteLine("dict:" + new Dictionary<string, Ext.Thing>().Count);
        Console.WriteLine("array:" + new Ext.Thing[2].Length);
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

        StringAssert.Contains(output, "list:1", output);
        StringAssert.Contains(output, "generic:1", output);
        StringAssert.Contains(output, "dict:0", output);
        StringAssert.Contains(output, "array:2", output);
        StringAssert.Contains(output, "<<DONE>>", output);
    }

    /// <summary>The emitted shape: no runtime type reference names the dictionary type. A derived
    /// dictionary counts through its base ([ObjectLiteral] is Inherited).</summary>
    [TestMethod]
    public void APlainObjectExternalIsNeverNamedAsARuntimeType()
    {
        var result = new RoslynTranslator().Translate(Bindings + """
public class Program
{
    public static void Main()
    {
        Ext.Thing t = null;
        var b = t is Ext.Thing;
        var l = new List<Ext.Thing>();
        var d = new List<Ext.Derived>();
        var a = new Ext.Thing[1];
    }
}
""");
        Assert.IsTrue(result.Success, "translation should succeed");
        var js = result.Javascript!;
        Assert.IsFalse(js.Contains("Ext.Thing"), "no runtime reference may name the dictionary type\n" + js);
        Assert.IsFalse(js.Contains("Ext.Derived"), "a derived dictionary type counts too\n" + js);
        StringAssert.Contains(js, "TransposeR.is(t, System.Object)", js);
        StringAssert.Contains(js, "System.Collections.Generic.List$1(System.Object)", js);
    }

    /// <summary>An [ObjectLiteral] type that is NOT external is emitted (carrying <c>$literal</c>),
    /// so its own name resolves and must be kept.</summary>
    [TestMethod]
    public void ANonExternalObjectLiteralKeepsItsOwnName()
    {
        var result = new RoslynTranslator().Translate("""
using Transpose;
[ObjectLiteral] public class Bag { public string a { get; set; } }
public class Program
{
    public static void Main()
    {
        var l = new System.Collections.Generic.List<Bag>();
        var a = new Bag[1];
    }
}
""");
        Assert.IsTrue(result.Success, "translation should succeed");
        var js = result.Javascript!;
        StringAssert.Contains(js, "System.Collections.Generic.List$1(Bag)", js);
    }
}
