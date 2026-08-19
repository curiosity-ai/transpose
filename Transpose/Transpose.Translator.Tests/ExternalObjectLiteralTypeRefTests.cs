using System.Threading.Tasks;

namespace Transpose.Translator.Tests;

/// <summary>
/// An <c>[ObjectLiteral]</c> type from an EXTERNAL binding library has no runtime type object:
/// nothing emits a <c>Transpose.define</c> for it (it is external) and the browser has no global of
/// that name (it is a WebIDL dictionary — dom.AnimationKeyFrame, dom.EventInit, … — not a class).
/// Naming it in a runtime type position therefore emitted a reference to something that never
/// exists, and the failure only ever surfaced at run time, in one of two shapes depending on
/// whether something else had put the namespace object there:
///
///   <c>ReferenceError: Ext is not defined</c>                          (nothing did)
///   <c>Cannot read properties of undefined (reading 'constructor')</c> (something did — this is
///   <c>Transpose.is</c> reading its type argument; <c>'$$name'</c> is the same thing one frame on)
///
/// Instances of such a type ARE plain JS objects, so its runtime type is System.Object — the answer
/// `dynamic` and anonymous types already get, and the one the BCL's own plain-object bindings state
/// by hand (<c>[Name("System.Object")]</c> on <c>Transpose.ObjectLiteral</c> and <c>Union&lt;…&gt;</c>).
/// </summary>
[TestClass]
public class ExternalObjectLiteralTypeRefTests : TranslatorTestBase
{
    private const string Bindings = @"
using System;
using System.Collections.Generic;
using Transpose;

namespace Ext
{
    [External] [ObjectLiteral] public class Thing { public string a { get; set; } }
    [External] [ObjectLiteral] public interface IThing { }
    [External] [ObjectLiteral] public class Derived : Thing { }
}
";

    /// <summary>The three positions the symptom was reported from — <c>is</c>, <c>as</c>, and a BCL
    /// generic's type argument — plus an array element, which has been tagged System.Object all
    /// along. Node-only: an [External] type has no native counterpart to run against.</summary>
    [TestMethod]
    public async Task TypeTestsAndGenericsOverAPlainObjectExternalRunInsteadOfThrowing()
    {
        var js = await RunTest(Bindings + @"
public class Program
{
    public static void Main()
    {
        object o = new Ext.Thing { a = ""x"" };
        Console.WriteLine(o is Ext.Thing);
        Console.WriteLine((o as Ext.Thing) != null);
        Console.WriteLine(o is Ext.IThing);
        Console.WriteLine(o is Ext.Derived);
        var list = new List<Ext.Thing>();
        list.Add((Ext.Thing)o);
        Console.WriteLine(list.Count);
        var arr = new Ext.Thing[2];
        Console.WriteLine(arr.Length);
        Console.WriteLine(((object)null) is Ext.Thing);
    }
}", skipRoslyn: true);

        Assert.AreEqual("True\nTrue\nTrue\nTrue\n1\n2\nFalse", js);
    }

    /// <summary>The emitted shape: every runtime type reference to such a type is System.Object, and
    /// the dangling name is gone. A derived dictionary counts through its base ([ObjectLiteral] is
    /// Inherited).</summary>
    [TestMethod]
    public void APlainObjectExternalIsNeverNamedAsARuntimeType()
    {
        var result = new RoslynTranslator().Translate(Bindings + @"
public class Program
{
    public static void Main()
    {
        object o = new Ext.Thing();
        var b = o is Ext.Thing;
        var c = o as Ext.Derived;
        var d = new System.Collections.Generic.List<Ext.Thing>();
        var e = new Ext.Thing[1];
    }
}");
        Assert.IsTrue(result.Success, "translation should succeed");
        var js = result.Javascript!;
        Assert.IsFalse(js.Contains("Ext.Thing"), "no runtime reference may name the dictionary type\n" + js);
        Assert.IsFalse(js.Contains("Ext.Derived"), "a derived dictionary type counts too\n" + js);
        StringAssert.Contains(js, "TransposeR.is(o, System.Object)", js);
        StringAssert.Contains(js, "TransposeR.as(o, System.Object)", js);
        StringAssert.Contains(js, "System.Collections.Generic.List$1(System.Object)", js);
    }

    /// <summary>An [ObjectLiteral] type that is NOT external is emitted (carrying <c>$literal</c>),
    /// so its own name resolves and must be kept.</summary>
    [TestMethod]
    public void ANonExternalObjectLiteralKeepsItsOwnName()
    {
        var result = new RoslynTranslator().Translate(@"
using Transpose;
[ObjectLiteral] public class Bag { public string a { get; set; } }
public class Program
{
    public static void Main()
    {
        object o = new Bag();
        var b = o is Bag;
        var l = new System.Collections.Generic.List<Bag>();
    }
}");
        Assert.IsTrue(result.Success, "translation should succeed");
        var js = result.Javascript!;
        StringAssert.Contains(js, "TransposeR.is(o, Bag)", js);
        StringAssert.Contains(js, "System.Collections.Generic.List$1(Bag)", js);
    }

    /// <summary>An external type declared in the GLOBAL namespace is named by itself. Roslyn's
    /// display string for the global namespace is the literal "&lt;global namespace&gt;", which used
    /// to be emitted verbatim — <c>TransposeR.is(o, &lt;global namespace&gt;.Ext)</c> is not
    /// JavaScript, so the whole bundle failed to parse.</summary>
    [TestMethod]
    public void AnExternalTypeInTheGlobalNamespaceIsNamedByItself()
    {
        var result = new RoslynTranslator().Translate(@"
using Transpose;
[External] public class GlobalThing { public extern GlobalThing(); }
public class Program
{
    public static void Main()
    {
        object o = null;
        var b = o is GlobalThing;
    }
}");
        Assert.IsTrue(result.Success, "translation should succeed");
        var js = result.Javascript!;
        Assert.IsFalse(js.Contains("<global namespace>"), "the global namespace has no emitted name\n" + js);
        StringAssert.Contains(js, "TransposeR.is(o, GlobalThing)", js);
    }
}
