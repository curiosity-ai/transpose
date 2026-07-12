using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite-case coverage for Wave 0 of docs/REWRITE-REMOVAL-PLAN.md:
    // R1 ([MethodImpl] stripping), S45 (constant initializer folding),
    // R3 (chained assignment splitting).
    [TestClass]
    public class RC_Wave0_Tests : IntegrationTestBase
    {
        [TestMethod]
        public async Task MethodImpl_OnMethodsPropertiesAndCtors()
        {
            var code = """
using System;
using System.Runtime.CompilerServices;

public class Calc
{
    private int _v;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Calc(int v) { _v = v; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Add(int a, int b) => a + b;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Mul(int a, int b) { return a * b; }

    public int Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { return _v; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set { _v = value; }
    }

    // Fully-qualified form
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public int Sub(int a, int b) => a - b;

    // Combined with another attribute in the same list
    [Obsolete("old"), MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Old() => 7;
}

public class Program
{
    public static void Main()
    {
        var c = new Calc(5);
        Console.WriteLine(c.Add(1, 2));
        Console.WriteLine(Calc.Mul(3, 4));
        Console.WriteLine(c.Value);
        c.Value = 42;
        Console.WriteLine(c.Value);
        Console.WriteLine(c.Sub(10, 4));
#pragma warning disable CS0618
        Console.WriteLine(c.Old());
#pragma warning restore CS0618
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task MethodImpl_WithUsingAlias()
        {
            var code = """
using System;
using MI = System.Runtime.CompilerServices.MethodImplAttribute;
using MIO = System.Runtime.CompilerServices.MethodImplOptions;

public class Program
{
    [MI(MIO.AggressiveInlining)]
    public static int Twice(int x) => 2 * x;

    public static void Main()
    {
        Console.WriteLine(Twice(21));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task ConstantFolding_ComputedConstants()
        {
            var code = """
using System;

public static class K
{
    public const int A = 3 * 7 + 1;
    public const int B = A * 2;
    public const string S = "ab" + "cd";
    public const string S2 = S + "!" ;
    public const char C = 'x';
    public const long BigNeg = long.MinValue;
    public const long BigPos = long.MaxValue;
    public const int IntMin = int.MinValue;
    public const double Third = 1.0 / 3.0;
    public const float ThirdF = 1.0f / 3.0f;
    public const bool Flag = (1 + 1) == 2;
}

public class Program
{
    private const int Local = K.B + 5;

    public static void Main()
    {
        Console.WriteLine(K.A);
        Console.WriteLine(K.B);
        Console.WriteLine(K.S);
        Console.WriteLine(K.S2);
        Console.WriteLine(K.C);
        Console.WriteLine(K.BigNeg);
        Console.WriteLine(K.BigPos);
        Console.WriteLine(K.IntMin);
        // NB: not printed directly — H5's double.ToString uses fewer digits than
        // .NET for non-terminating fractions (pre-existing runtime difference,
        // unrelated to constant folding). Compare the value instead.
        Console.WriteLine(K.Third == 1.0 / 3.0);
        Console.WriteLine(K.Third > 0.333333 && K.Third < 0.333334);
        Console.WriteLine(K.Flag);
        Console.WriteLine(Local);
        // const in expressions and default parameter values
        Console.WriteLine(WithDefault());
    }

    private static int WithDefault(int x = K.A) => x;
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task ConstantFolding_SpecialFloatingValues()
        {
            var code = """
using System;

public class Program
{
    private const double NaND = double.NaN;
    private const double PosInf = double.PositiveInfinity;
    private const double NegInf = double.NegativeInfinity;
    private const float NaNF = float.NaN;
    private const double Eps = double.Epsilon;

    public static void Main()
    {
        Console.WriteLine(double.IsNaN(NaND));
        Console.WriteLine(double.IsPositiveInfinity(PosInf));
        Console.WriteLine(double.IsNegativeInfinity(NegInf));
        Console.WriteLine(float.IsNaN(NaNF));
        Console.WriteLine(Eps > 0);
        Console.WriteLine(NegInf < 0);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task ChainedAssignment_LocalsFieldsAndProperties()
        {
            var code = """
using System;

public class Box
{
    private int _p;
    public int P
    {
        get { return _p; }
        set { Console.WriteLine("set P=" + value); _p = value; }
    }
    public int F;
}

public class Program
{
    private static int _sf;

    public static void Main()
    {
        // plain local chains
        int a, b, c;
        a = b = c = 5;
        Console.WriteLine(a + "," + b + "," + c);

        // chain into declaration
        int x;
        int y = x = 3;
        Console.WriteLine(x + "," + y);

        // fields and static fields
        var box = new Box();
        int v = box.F = _sf = 9;
        Console.WriteLine(v + "," + box.F + "," + _sf);

        // property setters observe assignment (and ordering)
        int w = box.P = 11;
        Console.WriteLine(w + "," + box.P);

        // array elements
        var arr = new int[3];
        arr[0] = arr[1] = arr[2] = 4;
        Console.WriteLine(arr[0] + "," + arr[1] + "," + arr[2]);

        // compound + simple mixed
        int m = 1, n;
        m += n = 10;
        Console.WriteLine(m + "," + n);

        // assignment as expression value in call
        int k;
        Console.WriteLine(Add(k = 6, k));

        // chained through ref-like usage in while condition
        int i = 0, j = 0;
        while ((i = j = j + 1) < 3) { Console.WriteLine("loop " + i); }
        Console.WriteLine(i + "," + j);
    }

    private static int Add(int p, int q) => p + q;
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task ChainedAssignment_SelfReferentialDeclaration()
        {
            var code = """
using System;

public class Program
{
    public static void Main()
    {
        // declaration whose initializer assigns the declared variable itself
        // (the exact shape ChainingAssigmentReplacer targets)
        int x = x = 5;
        Console.WriteLine(x);

        string s = s = "hi";
        Console.WriteLine(s);

        // nested inside an expression
        int y = (y = 7) + 1;
        Console.WriteLine(y);
    }
}
""";
            await RunTest(code);
        }
    }
}
