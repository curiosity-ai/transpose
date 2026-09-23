using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// User-defined operators on a struct declared in the compilation, each run against native .NET.
    /// <para>
    /// An operator takes its operands by value, but the emitter passes a struct operand uncopied (cloning
    /// every operand of every <c>==</c> would only cost output). That is sound until the operator writes to
    /// its parameter — <c>operator ++(Counter c) { c.V++; return c; }</c>, a perfectly ordinary way to write
    /// one — and then the write reached the caller's variable: <c>var old = c++</c> saw the new value and
    /// <c>c + 5</c> changed <c>c</c>. Such an operator now gets its operand cloned
    /// (<c>OperatorMutatesParameter</c>), and only such an operator. Two more bugs lived nearby: a user
    /// <c>++</c> on an array element was JavaScript's own <c>++</c> on the element object (<c>NaN</c>), and a
    /// lifted user <c>++</c> on a null <c>Nullable&lt;T&gt;</c> called the operator on null.
    /// </para>
    /// </summary>
    [TestClass]
    public class StructOperatorTests : TranslatorTestBase
    {
        private const string Types = @"
using System;
using System.Collections.Generic;

public struct Fresh
{
    public int V;
    public static Fresh operator ++(Fresh c) => new Fresh { V = c.V + 1 };
    public static Fresh operator --(Fresh c) => new Fresh { V = c.V - 1 };
    public static Fresh operator +(Fresh a, Fresh b) => new Fresh { V = a.V + b.V };
    public static Fresh operator -(Fresh a) => new Fresh { V = -a.V };
    public static bool operator ==(Fresh a, Fresh b) => a.V == b.V;
    public static bool operator !=(Fresh a, Fresh b) => a.V != b.V;
    public override bool Equals(object o) => o is Fresh f && f.V == V;
    public override int GetHashCode() => V;
    public override string ToString() => ""F"" + V;
}

public struct Mutating
{
    public int V;
    public static Mutating operator ++(Mutating c) { c.V += 10; return c; }
    public static Mutating operator --(Mutating c) { c.V--; return c; }
    public static Mutating operator +(Mutating a, int n) { a.V += n; return a; }
    public static Mutating operator +(int n, Mutating a) { a.Add(n); return a; }
    public static Mutating operator -(Mutating a) { a = new Mutating { V = -a.V }; return a; }
    public static Mutating operator *(Mutating a, Mutating b) { Scale(ref a, b.V); return a; }
    static void Scale(ref Mutating m, int k) => m.V *= k;
    void Add(int n) => V += n;
    public override string ToString() => ""M"" + V;
}

public struct Inner { public int V; }
public struct Outer
{
    public Inner In;
    public static Outer operator ++(Outer o) { o.In.V++; return o; }
    public override string ToString() => ""O"" + In.V;
}

public class Holder { public Fresh F; public Mutating M; public Mutating Prop { get; set; } }
";

        private static string Program(string body) => Types + @"
public class Program
{
    public static void Main()
    {
" + body + @"
    }
}";

        private Task<string> Run(string body) => RunTest(Program(body));

        /// <summary>An operator that builds a new value: every prefix/postfix/compound shape.</summary>
        [TestMethod]
        public async Task FreshValueOperatorsMatchNative()
        {
            await Run(@"
        var f = new Fresh { V = 1 };
        f++;
        ++f;
        var oldF = f++;
        var newF = ++f;
        f--;
        --f;
        f += new Fresh { V = 100 };
        var neg = -f;
        Console.WriteLine($""{f} {oldF} {newF} {neg} {f == new Fresh { V = 102 }} {f != neg}"");");
        }

        /// <summary>An operator that writes to its parameter must not write to the caller's variable.</summary>
        [TestMethod]
        public async Task MutatingOperatorDoesNotWriteThroughItsOperand()
        {
            await Run(@"
        var m = new Mutating { V = 1 };
        var before = m;
        var oldM = m++;
        var newM = ++m;
        Console.WriteLine($""{m} {before} {oldM} {newM}"");
        var sum = m + 5;
        Console.WriteLine($""{m} {sum}"");
        var sum2 = 3 + m;
        Console.WriteLine($""{m} {sum2}"");
        m += 7;
        Console.WriteLine(m);
        var neg = -m;
        Console.WriteLine($""{m} {neg}"");
        var k = new Mutating { V = 2 };
        var prod = m * k;
        Console.WriteLine($""{m} {k} {prod}"");
        var oldDec = m--;
        Console.WriteLine($""{m} {oldDec}"");");
        }

        /// <summary>A write to a nested struct field of the parameter is a write to the copy too.</summary>
        [TestMethod]
        public async Task NestedFieldWriteInOperatorIsOnTheCopy()
        {
            await Run(@"
        var o = new Outer();
        var old = o++;
        var copy = o;
        ++o;
        Console.WriteLine($""{old} {copy} {o}"");");
        }

        /// <summary>Fields, auto-properties, array elements and list elements as operands.</summary>
        [TestMethod]
        public async Task OperatorsOnFieldsPropertiesArraysAndLists()
        {
            await Run(@"
        var h = new Holder();
        h.F++;
        h.M++;
        h.M += 3;
        h.Prop++;
        var oldProp = h.Prop++;
        Console.WriteLine($""{h.F} {h.M} {h.Prop} {oldProp}"");

        var arr = new[] { new Mutating { V = 1 }, new Mutating { V = 2 } };
        arr[0]++;
        var oldArr = arr[1]++;
        ++arr[1];
        arr[0] += 4;
        Console.WriteLine($""{arr[0]} {arr[1]} {oldArr}"");

        var fresh = new[] { new Fresh { V = 1 } };
        var i = 0;
        fresh[i++]++;
        Console.WriteLine($""{fresh[0]} {i}"");

        var list = new List<Mutating> { new Mutating { V = 5 } };
        list[0]++;
        var oldList = list[0]++;
        Console.WriteLine($""{list[0]} {oldList}"");");
        }

        /// <summary>The lifted form over <c>Nullable&lt;T&gt;</c>: null stays null and never reaches the operator.</summary>
        [TestMethod]
        public async Task LiftedOperatorsOverNullableStruct()
        {
            await Run(@"
        Mutating? n = null;
        n++;
        ++n;
        var oldN = n++;
        Mutating? n2 = new Mutating { V = 1 };
        var oldN2 = n2++;
        Console.WriteLine($""{n == null} {oldN == null} {n2} {oldN2}"");
        var sum = n + 1;
        var sum2 = n2 + 1;
        Console.WriteLine($""{sum == null} {sum2} {n2}"");
        Fresh? f = null;
        var neg = -f;
        f++;
        Fresh? g = new Fresh { V = 4 };
        g--;
        Console.WriteLine($""{neg == null} {f == null} {g}"");
        Mutating?[] arr = { null, new Mutating { V = 1 } };
        arr[0]++;
        arr[1]++;
        Console.WriteLine($""{arr[0] == null} {arr[1]}"");");
        }

        /// <summary>A mutating operator is cloned; a fresh-value one is not — the output stays lean for
        /// the common case.</summary>
        [TestMethod]
        public async Task OnlyMutatingOperatorsCloneTheirOperand()
        {
            await Run(@"
        var f = new Fresh { V = 1 };
        var f2 = f + f;
        var m = new Mutating { V = 1 };
        var m2 = m + 1;
        Console.WriteLine($""{f2} {m2}"");");
            var js = new RoslynTranslator().Translate(Program(@"
        var f = new Fresh { V = 1 };
        var f2 = f + f;
        var m = new Mutating { V = 1 };
        var m2 = m + 1;")).Javascript ?? "";
            StringAssert.Contains(js, "op_Addition(TransposeR.clone(m), 1)");
            Assert.IsFalse(js.Contains("op_Addition(TransposeR.clone(f)"), "a non-mutating operator's operand was cloned");
        }
    }
}
