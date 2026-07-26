using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Regression tests for the left-hand side of a deconstruction. Every target used to be reduced to
    /// its bare source identifier, which is only correct for a local or parameter:
    ///  - an implicit-<c>this</c> field/property (<c>(_a, _b) = Get();</c>) emitted <c>_a = …</c>, an
    ///    assignment to an undeclared name — a <c>ReferenceError</c> under the bundle's "use strict".
    ///    This is the Curiosity <c>SearchResultsComponent</c> crash:
    ///    <c>(_firstViewportElement, _firstViewportElementTopOffset) = GetFirstVisibleChild();</c>
    ///    (h5 emitted <c>H5.Deconstruct(…, H5.ref(this, "_firstViewportElement"), …)</c>);
    ///  - a static field emitted the same unqualified name instead of <c>Type.Field</c>;
    ///  - any other lvalue — <c>this.F</c>, <c>obj.F</c>, <c>arr[i]</c>, <c>map[k]</c> — matched no case
    ///    at all and was DROPPED, which also shortened the target list and so shifted every later
    ///    target onto the wrong tuple element (silently wrong values, no error);
    ///  - a nested group (<c>var (a, (b, c))</c>) was dropped the same way.
    /// Targets now bind through the shared simple-assignment emitter, so a deconstruction stores to a
    /// field, property, indexer or array element exactly as <c>x = v</c> does.
    /// </summary>
    [TestClass]
    public class DeconstructionTargetTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task InstanceFieldTargetsAreQualifiedAsync()
        {
            await RunTest(@"
using System;

public class Viewport
{
    private string _element = null;
    private double _topOffset = 0;

    private (string child, double top) GetFirstVisibleChild() => (""child"", 42.5);

    public void Refresh()
    {
        (_element, _topOffset) = GetFirstVisibleChild();
        Console.WriteLine(_element + "" "" + _topOffset);
    }
}

public class Program
{
    public static void Main()
    {
        new Viewport().Refresh();
    }
}");
        }

        [TestMethod]
        public void ImplicitThisFieldTargetEmitsThisQualifier()
        {
            var code = @"
public class Viewport
{
    private string _element = null;
    private double _topOffset = 0;

    private (string child, double top) GetFirstVisibleChild() => (""child"", 42.5);

    public void Refresh()
    {
        (_element, _topOffset) = GetFirstVisibleChild();
    }
}

public class Program
{
    public static void Main() { }
}";
            var result = new RoslynTranslator().Translate(code);

            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains("this._element = "),
                "an implicit-this field deconstruction target must be qualified with `this.`\n" + result.Javascript);
            Assert.IsTrue(result.Javascript!.Contains("this._topOffset = "),
                "an implicit-this field deconstruction target must be qualified with `this.`\n" + result.Javascript);
        }

        [TestMethod]
        public async Task StaticFieldAndPropertyTargetsAsync()
        {
            await RunTest(@"
using System;

public class Program
{
    private static string _name = null;
    private static double _value = 0;

    public static string Name { get; set; }
    public static double Value { get; set; }

    private static (string, double) Get() => (""s"", 1.5);

    public static void Main()
    {
        (_name, _value) = Get();
        Console.WriteLine(_name + "" "" + _value);

        (Name, Value) = Get();
        Console.WriteLine(Name + "" "" + Value);
    }
}");
        }

        [TestMethod]
        public async Task ExplicitThisAndMemberAccessTargetsAsync()
        {
            await RunTest(@"
using System;

public class Box
{
    public int A;
    public int B;
}

public class Holder
{
    public int F;
    public Box Inner = new Box();

    private (int, int) Get() => (1, 2);

    public void Run()
    {
        (this.F, this.Inner.A) = Get();
        Console.WriteLine(F + "" "" + Inner.A);

        (Inner.A, Inner.B) = Get();
        Console.WriteLine(Inner.A + "" "" + Inner.B);
    }
}

public class Program
{
    public static void Main()
    {
        new Holder().Run();
    }
}");
        }

        /// <summary>
        /// A non-local target used to be dropped from the target list, which shifted every LATER target
        /// down one tuple element — so the second target silently read Item1.
        /// </summary>
        [TestMethod]
        public async Task NonLocalTargetDoesNotShiftLaterElementsAsync()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;

public class Holder
{
    public string[] Arr = new string[2];
    public Dictionary<string, int> Map = new Dictionary<string, int>();
    public int Field;

    private (string, double) GetPair() => (""first"", 42.5);

    public void Run()
    {
        double after;
        (Arr[0], after) = GetPair();
        Console.WriteLine(Arr[0] + "" "" + after);

        int tail;
        (Map[""k""], tail) = (7, 9);
        Console.WriteLine(Map[""k""] + "" "" + tail);

        string trailing;
        (Field, trailing) = (3, ""end"");
        Console.WriteLine(Field + "" "" + trailing);
    }
}

public class Program
{
    public static void Main()
    {
        new Holder().Run();
    }
}");
        }

        [TestMethod]
        public async Task NestedDeconstructionAsync()
        {
            await RunTest(@"
using System;

public class Program
{
    private static int _x;

    public static void Main()
    {
        var (a, (b, c)) = (1, (2, 3));
        Console.WriteLine(a + "" "" + b + "" "" + c);

        int x, y, z;
        (x, (y, z)) = (4, (5, 6));
        Console.WriteLine(x + "" "" + y + "" "" + z);

        int deep;
        (_x, (deep, _)) = (7, (8, 9));
        Console.WriteLine(_x + "" "" + deep);
    }
}");
        }

        [TestMethod]
        public async Task DeconstructMethodIntoFieldsAsync()
        {
            await RunTest(@"
using System;

public class Point
{
    public int X;
    public int Y;

    public Point(int x, int y) { X = x; Y = y; }

    public void Deconstruct(out int x, out int y) { x = X; y = Y; }
}

public class Holder
{
    public int A;
    public int B;
    public int[] Slot = new int[1];

    public void Run()
    {
        (A, B) = new Point(11, 12);
        Console.WriteLine(A + "" "" + B);

        int local;
        (Slot[0], local) = new Point(13, 14);
        Console.WriteLine(Slot[0] + "" "" + local);
    }
}

public class Program
{
    public static void Main()
    {
        new Holder().Run();
    }
}");
        }

        /// <summary>foreach shares the binding emitter, so a nested designation must expand there too
        /// (it used to be dropped, leaving the inner names undeclared).</summary>
        [TestMethod]
        public async Task ForEachNestedDeconstructionAsync()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;

public class Program
{
    public static void Main()
    {
        var rows = new List<(int, (string, double))>
        {
            (1, (""one"", 1.5)),
            (2, (""two"", 2.5)),
        };

        foreach (var (id, (name, weight)) in rows)
        {
            Console.WriteLine(id + "" "" + name + "" "" + weight);
        }

        foreach (var (id, (_, weight)) in rows)
        {
            Console.WriteLine(id + "" "" + weight);
        }
    }
}");
        }

        [TestMethod]
        public async Task DiscardsPreservePositionAsync()
        {
            await RunTest(@"
using System;

public class Holder
{
    public int Last;

    private (int, int, int) Get() => (1, 2, 3);

    public void Run()
    {
        (_, _, Last) = Get();
        Console.WriteLine(Last);

        var (_, middle, _) = Get();
        Console.WriteLine(middle);
    }
}

public class Program
{
    public static void Main()
    {
        new Holder().Run();
    }
}");
        }
    }
}
