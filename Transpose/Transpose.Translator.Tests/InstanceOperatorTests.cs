using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// C# 14 user-defined <b>instance</b> compound-assignment and increment/decrement operators
    /// (<c>public void operator +=(Money other)</c>, <c>public void operator ++()</c>). They are instance
    /// methods that mutate their receiver, and <c>m += x</c> used to be emitted as a JavaScript <c>+=</c>
    /// on the object — string concatenation into <c>m</c>, with no diagnostic. It is now a call on the
    /// receiver (<c>Emitter.InstanceOperators.cs</c>). Every test runs natively too and must match.
    /// </summary>
    [TestClass]
    public class InstanceOperatorTests : TranslatorTestBase
    {
        private const string Types = @"
using System;
using System.Collections.Generic;

public class Tally
{
    public int Total;
    public int Calls;
    public void operator +=(int amount) { Total += amount; Calls++; }
    public void operator -=(int amount) { Total -= amount; Calls++; }
    public void operator *=(Tally other) { Total *= other.Total; Calls++; }
    public void operator ++() { Total++; Calls++; }
    public void operator --() { Total--; Calls++; }
    public override string ToString() => Total + ""/"" + Calls;
}

public struct Counter
{
    public int Value;
    public void operator +=(int amount) { Value += amount; }
    public void operator ++() { Value++; }
    public override string ToString() => ""Counter("" + Value + "")"";
}
";

        /// <summary>Each compound operator and ++/-- on a class runs the operator on the receiver.</summary>
        [TestMethod]
        public async Task CompoundAndIncrementOperatorsOnAClass()
        {
            await RunTest(Types + @"
public class Program
{
    public static void Main()
    {
        var t = new Tally();
        t += 5;
        t -= 2;
        t *= new Tally { Total = 4 };
        t++;
        ++t;
        t--;
        --t;
        Console.WriteLine(t);
    }
}");
        }

        /// <summary>On a struct the operator mutates the variable itself — a local, a field, an array
        /// element — and a copy taken before is unaffected.</summary>
        [TestMethod]
        public async Task OperatorsOnAStructMutateTheVariable()
        {
            await RunTest(Types + @"
public class Holder { public Counter C; }

public class Program
{
    public static void Main()
    {
        var c = new Counter();
        var before = c;
        c += 3;
        c++;
        Console.WriteLine(c + "" "" + before);

        var h = new Holder();
        h.C += 10;
        h.C++;
        Console.WriteLine(h.C);

        var arr = new Counter[2];
        arr[1] += 7;
        Console.WriteLine(arr[0] + "" "" + arr[1]);
    }
}");
        }

        /// <summary>The receiver is a field of another object, a local aliasing a list element, or a
        /// field of a call's result; a call with side effects runs once.</summary>
        [TestMethod]
        public async Task ReceiverShapes()
        {
            await RunTest(Types + @"
public class Box { public Tally T = new Tally(); }

public class Program
{
    static int _made;
    static Box _box = new Box();
    static Box Get() { _made++; return _box; }

    public static void Main()
    {
        var box = new Box();
        box.T += 4;
        var list = new List<Tally> { new Tally(), new Tally() };
        var second = list[1];
        second += 9;
        Console.WriteLine(box.T + "" "" + list[0] + "" "" + list[1]);

        Get().T += 1;
        Get().T++;
        Console.WriteLine(_made + "" "" + _box.T);
    }
}");
        }

        /// <summary>Used as a value, the compound expression yields the operand after the operation,
        /// as C# specifies for an instance operator.</summary>
        [TestMethod]
        public async Task CompoundExpressionUsedAsAValue()
        {
            await RunTest(Types + @"
public class Program
{
    public static void Main()
    {
        var t = new Tally();
        var same = (t += 6);
        Console.WriteLine(ReferenceEquals(t, same) + "" "" + same);
        Console.WriteLine((++t).Total);
    }
}");
        }

        /// <summary>A class with the classic static operators still uses them: <c>a += b</c> is
        /// <c>a = a + b</c>, and <c>a++</c> is <c>a = op_Increment(a)</c>, which replace the reference.
        /// The static <c>++</c> used to be emitted as a JavaScript <c>++</c> on the object (NaN).</summary>
        [TestMethod]
        public async Task StaticOperatorsAreUnchanged()
        {
            await RunTest(@"
using System;

public class Money
{
    public decimal Amount;
    public static Money operator +(Money a, Money b) => new Money { Amount = a.Amount + b.Amount };
    public static Money operator ++(Money a) => new Money { Amount = a.Amount + 1 };
    public static Money operator --(Money a) => new Money { Amount = a.Amount - 1 };
}

public class Program
{
    public static void Main()
    {
        var m = new Money { Amount = 1 };
        var original = m;
        m += new Money { Amount = 2 };
        m++;
        ++m;
        var beforePost = m;
        var post = m++;
        var pre = ++m;
        Console.WriteLine(m.Amount + "" "" + original.Amount + "" "" + ReferenceEquals(m, original));
        Console.WriteLine(ReferenceEquals(post, beforePost) + "" "" + post.Amount + "" "" + ReferenceEquals(pre, m));
        var list = new System.Collections.Generic.List<Money> { new Money { Amount = 5 } };
        list[0]++;
        Console.WriteLine(list[0].Amount);
    }
}");
        }
    }
}
