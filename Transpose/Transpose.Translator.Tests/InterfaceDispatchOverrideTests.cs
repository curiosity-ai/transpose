using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace Transpose.Translator.Tests;

/// <summary>
/// Interface dispatch reaches a *derived* type's override of an implementation declared by a base
/// type. A source interface is dispatched through a mangled slot (<c>IFoo$Bar</c>) that the
/// implementing class publishes as an <c>alias</c> of the plain slot, and a JS alias is a hard
/// binding to the function it was installed from — so a derived override of the plain slot does not
/// displace it, and the alias must be republished by every type that overrides.
/// </summary>
[TestClass]
public class InterfaceDispatchOverrideTests : TranslatorTestBase
{
    [TestMethod]
    public async Task Method_OverriddenInDerived_IsReachedThroughTheInterface()
    {
        var code = @"
using System;

public class Node { }

public interface INodeRenderer
{
    string CompactView(Node node);
}

public abstract class NodeRendererBase : INodeRenderer
{
    public virtual string CompactView(Node node) => ""BASE"";
}

public abstract class TechDataRendererBase : NodeRendererBase
{
    public override string CompactView(Node node) => ""DERIVED"";
}

public class ModificationRenderer : TechDataRendererBase { }

public class Program
{
    public static void Main()
    {
        INodeRenderer viaInterface = new ModificationRenderer();
        Console.WriteLine(viaInterface.CompactView(new Node()));

        NodeRendererBase viaBase = new ModificationRenderer();
        Console.WriteLine(viaBase.CompactView(new Node()));
    }
}";
        await RunTest(code);
    }

    [TestMethod]
    public async Task PropertyMethodAndEvent_OverriddenTwoLevelsDown_AreReachedThroughTheInterface()
    {
        var code = @"
using System;

public interface IShape
{
    string Name { get; }
    string Describe();
    event EventHandler Changed;
}

public abstract class ShapeBase : IShape
{
    public virtual string Name => ""base-name"";
    public virtual string Describe() => ""base-describe"";
    public virtual event EventHandler Changed;
    public void Fire() => Changed?.Invoke(this, EventArgs.Empty);
}

public abstract class MidShape : ShapeBase
{
    public override string Name => ""mid-name"";
    public override string Describe() => ""mid-describe"";
}

public class LeafShape : MidShape { }

// Overrides nothing: the base's alias must still be reached through the prototype chain.
public class PlainLeaf : ShapeBase { }

public class Program
{
    public static void Main()
    {
        IShape overridden = new LeafShape();
        Console.WriteLine(overridden.Name);
        Console.WriteLine(overridden.Describe());

        IShape inherited = new PlainLeaf();
        Console.WriteLine(inherited.Name);
        Console.WriteLine(inherited.Describe());

        var fired = 0;
        EventHandler handler = (s, e) => fired++;
        overridden.Changed += handler;
        ((LeafShape)overridden).Fire();
        overridden.Changed -= handler;
        ((LeafShape)overridden).Fire();
        Console.WriteLine(fired);
    }
}";
        await RunTest(code);
    }

    [TestMethod]
    public async Task AbstractImplementation_FilledByDerived_IsReachedThroughTheInterface()
    {
        var code = @"
using System;

public interface IThing { string Value(); }

public abstract class ThingBase : IThing
{
    public abstract string Value();
}

public class Thing : ThingBase
{
    public override string Value() => ""thing"";
}

public class Program
{
    public static void Main()
    {
        IThing thing = new Thing();
        Console.WriteLine(thing.Value());
    }
}";
        await RunTest(code);
    }

    [TestMethod]
    public async Task GenericInterface_OverriddenInDerived_IsReachedThroughTheInterface()
    {
        var code = @"
using System;

public interface IBox<T> { T Get(); }

public class BoxBase : IBox<int>
{
    public virtual int Get() => 1;
}

public class BoxDerived : BoxBase
{
    public override int Get() => 2;
}

public class Program
{
    public static void Main()
    {
        IBox<int> box = new BoxDerived();
        Console.WriteLine(box.Get());
    }
}";
        await RunTest(code);
    }

    [TestMethod]
    public async Task ShadowingMember_DoesNotTakeOverTheInterfaceSlot()
    {
        var code = @"
using System;

public interface IShape { string Describe(); }

public class ShapeBase : IShape
{
    public virtual string Describe() => ""base"";
}

// `new`, not `override`: interface dispatch must still reach ShapeBase.Describe.
public class Shadowing : ShapeBase
{
    public new string Describe() => ""shadow"";
}

public class Program
{
    public static void Main()
    {
        var shadowing = new Shadowing();
        Console.WriteLine(shadowing.Describe());
        Console.WriteLine(((IShape)shadowing).Describe());
        Console.WriteLine(((ShapeBase)shadowing).Describe());
    }
}";
        await RunTest(code);
    }

    [TestMethod]
    public async Task BclInterfaceProperty_OverriddenInDerived_IsReachedThroughTheInterface()
    {
        var code = @"
using System;
using System.Collections.Generic;

public class CollectionBase : IReadOnlyCollection<int>
{
    public virtual int Count => 10;
    public IEnumerator<int> GetEnumerator() { yield return 1; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public class CollectionDerived : CollectionBase
{
    public override int Count => 20;
}

public class Program
{
    public static void Main()
    {
        IReadOnlyCollection<int> collection = new CollectionDerived();
        Console.WriteLine(collection.Count);
    }
}";
        await RunTest(code);
    }
}
