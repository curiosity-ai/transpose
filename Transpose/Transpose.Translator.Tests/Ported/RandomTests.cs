using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests.Ported
{
    /// <summary>
    /// System.Random. Most tests print a *seeded* sequence and let the harness diff it against
    /// native .NET, so the generator has to agree with .NET value-for-value, not merely stay in
    /// range. The exceptions are the tests that exercise the seedless constructor, where only
    /// distribution properties are observable.
    /// </summary>
    [TestClass]
    public class RandomTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task TestNextAsync()
        {
            await RunTest(
                @"
using System;

public class Program
{
    public static void Main()
    {
        var r = new Random(42);
        for (int i = 0; i < 8; i++) Console.WriteLine(r.Next());
    }
}");
        }

        [TestMethod]
        public async Task TestNextMaxValueAsync()
        {
            await RunTest(
                @"
using System;

public class Program
{
    public static void Main()
    {
        var r = new Random(42);
        for (int i = 0; i < 10; i++) Console.WriteLine(r.Next(10));

        // Documented edge cases.
        Console.WriteLine(r.Next(0));
        Console.WriteLine(r.Next(1));

        try { r.Next(-1); Console.WriteLine(""no throw""); }
        catch (ArgumentOutOfRangeException) { Console.WriteLine(""ArgumentOutOfRangeException""); }
    }
}");
        }

        /// <summary>
        /// The two-argument overload used to return <c>minValue</c> every single time: it computes
        /// <c>(int)(Sample() * range)</c> with a <c>long</c> range, and mixed double/long arithmetic
        /// was emitted as <c>System.Int64(Sample()).mul(range)</c> — truncating the 0..1 sample to 0.
        /// </summary>
        [TestMethod]
        public async Task TestNextMinMaxAsync()
        {
            await RunTest(
                @"
using System;

public class Program
{
    public static void Main()
    {
        var r = new Random(42);
        for (int i = 0; i < 10; i++) Console.WriteLine(r.Next(0, 7));
        for (int i = 0; i < 10; i++) Console.WriteLine(r.Next(3, 9));
        for (int i = 0; i < 10; i++) Console.WriteLine(r.Next(-5, 5));

        // A range wider than int.MaxValue takes the GetSampleForLargeRange path.
        for (int i = 0; i < 5; i++) Console.WriteLine(r.Next(int.MinValue, int.MaxValue));

        // Documented edge cases.
        Console.WriteLine(r.Next(5, 5));
        Console.WriteLine(r.Next(-3, -3));

        try { r.Next(9, 3); Console.WriteLine(""no throw""); }
        catch (ArgumentOutOfRangeException) { Console.WriteLine(""ArgumentOutOfRangeException""); }
    }
}");
        }

        /// <summary>
        /// The regression that made the bug visible: the sequence has to actually move, and stay
        /// inside the half-open range, for a seedless instance too.
        /// </summary>
        [TestMethod]
        public async Task TestNextMinMaxIsNotConstantAsync()
        {
            await RunTest(
                @"
using System;
using System.Collections.Generic;

public class Program
{
    public static void Main()
    {
        var r = new Random();

        Check(r, 0, 7);
        Check(r, 3, 9);
        Check(r, -5, 5);
        Check(r, 0, 2);
    }

    private static void Check(Random r, int min, int max)
    {
        var seen = new HashSet<int>();
        bool inRange = true;

        for (int i = 0; i < 1000; i++)
        {
            int v = r.Next(min, max);
            if (v < min || v >= max) inRange = false;
            seen.Add(v);
        }

        Console.WriteLine(""["" + min + "","" + max + "") in range: "" + inRange);
        Console.WriteLine(""["" + min + "","" + max + "") distinct values: "" + seen.Count);
    }
}");
        }

        /// <summary>
        /// The samples are scaled to integers rather than printed directly: transpose's
        /// <c>Console.WriteLine(double)</c> truncates to 14 significant digits (unrelated to Random),
        /// which would mask the sequence comparison this test is for.
        /// </summary>
        [TestMethod]
        public async Task TestNextDoubleAsync()
        {
            await RunTest(
                @"
using System;

public class Program
{
    public static void Main()
    {
        var r = new Random(42);
        for (int i = 0; i < 8; i++) Console.WriteLine((long)(r.NextDouble() * 1000000000L));
    }
}");
        }

        [TestMethod]
        public async Task TestNextDoubleIsInRangeAsync()
        {
            await RunTest(
                @"
using System;

public class Program
{
    public static void Main()
    {
        var r = new Random();
        bool inRange = true;
        bool varies = false;
        double first = r.NextDouble();

        for (int i = 0; i < 1000; i++)
        {
            double d = r.NextDouble();
            if (d < 0.0 || d >= 1.0) inRange = false;
            if (d != first) varies = true;
        }

        Console.WriteLine(""NextDouble in [0,1): "" + inRange);
        Console.WriteLine(""NextDouble varies: "" + varies);
    }
}");
        }

        [TestMethod]
        public async Task TestNextInt64Async()
        {
            await RunTest(
                @"
using System;

public class Program
{
    public static void Main()
    {
        var r = new Random(42);

        // Unbounded
        for (int i = 0; i < 3; i++) Console.WriteLine(r.NextInt64());

        // Max value
        for (int i = 0; i < 5; i++) Console.WriteLine(r.NextInt64(1000));

        // Range
        for (int i = 0; i < 10; i++) Console.WriteLine(r.NextInt64(50, 60));

        // Range wider than int.MaxValue
        for (int i = 0; i < 3; i++) Console.WriteLine(r.NextInt64(long.MinValue, long.MaxValue));
        for (int i = 0; i < 3; i++) Console.WriteLine(r.NextInt64(long.MaxValue - 100, long.MaxValue));

        // Documented edge cases.
        Console.WriteLine(r.NextInt64(0));
        Console.WriteLine(r.NextInt64(1));
        Console.WriteLine(r.NextInt64(7, 7));
        Console.WriteLine(r.NextInt64(7, 8));

        try { r.NextInt64(-1); Console.WriteLine(""no throw""); }
        catch (ArgumentOutOfRangeException) { Console.WriteLine(""ArgumentOutOfRangeException""); }

        try { r.NextInt64(9, 3); Console.WriteLine(""no throw""); }
        catch (ArgumentOutOfRangeException) { Console.WriteLine(""ArgumentOutOfRangeException""); }
    }
}");
        }

        [TestMethod]
        public async Task TestNextInt64IsInRangeAsync()
        {
            await RunTest(
                @"
using System;
using System.Collections.Generic;

public class Program
{
    public static void Main()
    {
        var r = new Random();
        var seen = new HashSet<long>();
        bool inRange = true;

        for (int i = 0; i < 1000; i++)
        {
            long v = r.NextInt64(50, 60);
            if (v < 50L || v >= 60L) inRange = false;
            seen.Add(v);
        }

        Console.WriteLine(""NextInt64(50,60) in range: "" + inRange);
        Console.WriteLine(""NextInt64(50,60) distinct values: "" + seen.Count);

        bool nonNegative = true;
        for (int i = 0; i < 200; i++)
        {
            if (r.NextInt64() < 0L) nonNegative = false;
        }

        Console.WriteLine(""NextInt64() non-negative: "" + nonNegative);
    }
}");
        }

        /// <summary>
        /// Only the range is asserted, not the value: transpose keeps a <c>float</c> at double
        /// precision, so printing one does not match .NET's single-precision rendering.
        /// </summary>
        [TestMethod]
        public async Task TestNextSingleAsync()
        {
            await RunTest(
                @"
using System;

public class Program
{
    public static void Main()
    {
        var r = new Random(42);
        bool inRange = true;
        bool varies = false;
        float first = r.NextSingle();

        for (int i = 0; i < 500; i++)
        {
            float f = r.NextSingle();
            if (f < 0.0f || f >= 1.0f) inRange = false;
            if (f != first) varies = true;
        }

        Console.WriteLine(""NextSingle in [0,1): "" + inRange);
        Console.WriteLine(""NextSingle varies: "" + varies);
    }
}");
        }

        [TestMethod]
        public async Task TestNextBytesAsync()
        {
            await RunTest(
                @"
using System;

public class Program
{
    public static void Main()
    {
        var r = new Random(42);

        var buffer = new byte[16];
        r.NextBytes(buffer);
        Console.WriteLine(string.Join("","", buffer));

        // An empty buffer is legal and consumes nothing.
        r.NextBytes(new byte[0]);
        Console.WriteLine(r.Next(1000));

        try { r.NextBytes(null); Console.WriteLine(""no throw""); }
        catch (ArgumentNullException) { Console.WriteLine(""ArgumentNullException""); }
    }
}");
        }

        /// <summary>
        /// The same seed must replay the same sequence, and different seeds must not.
        /// </summary>
        [TestMethod]
        public async Task TestSeedDeterminismAsync()
        {
            await RunTest(
                @"
using System;

public class Program
{
    public static void Main()
    {
        var a = new Random(12345);
        var b = new Random(12345);
        var c = new Random(54321);

        bool sameSeedAgrees = true;
        bool differentSeedDiffers = false;

        for (int i = 0; i < 100; i++)
        {
            int x = a.Next(0, 1000);
            int y = b.Next(0, 1000);
            int z = c.Next(0, 1000);

            if (x != y) sameSeedAgrees = false;
            if (x != z) differentSeedDiffers = true;
        }

        Console.WriteLine(""same seed agrees: "" + sameSeedAgrees);
        Console.WriteLine(""different seed differs: "" + differentSeedDiffers);

        // Negative seeds and the int.MinValue special case are accepted.
        Console.WriteLine(new Random(-7).Next(0, 100) == new Random(-7).Next(0, 100));
        Console.WriteLine(new Random(int.MinValue).Next(0, 100) == new Random(int.MinValue).Next(0, 100));
        Console.WriteLine(new Random(int.MaxValue).Next(0, 100) >= 0);
        Console.WriteLine(new Random(0).Next(0, 100) >= 0);
    }
}");
        }

        /// <summary>
        /// The seedless constructor seeds from <c>Math.random()</c> rather than
        /// <c>(int)DateTime.Now.Ticks</c>. A tick seed only has millisecond resolution once
        /// transpiled, so a batch of instances built in one go all shared a sequence.
        /// </summary>
        [TestMethod]
        public async Task TestSeedlessInstancesDoNotShareASequenceAsync()
        {
            await RunTest(
                @"
using System;
using System.Collections.Generic;

public class Program
{
    public static void Main()
    {
        var seen = new HashSet<int>();

        for (int i = 0; i < 50; i++)
        {
            seen.Add(new Random().Next());
        }

        // Tick-seeded instances created in the same millisecond collapsed to a single value.
        Console.WriteLine(""distinct first draws (>1): "" + (seen.Count > 1));
        Console.WriteLine(""mostly distinct (>40/50): "" + (seen.Count > 40));
    }
}");
        }

        /// <summary>Random.Shared, as in modern .NET.</summary>
        [TestMethod]
        public async Task TestSharedAsync()
        {
            await RunTest(
                @"
using System;
using System.Collections.Generic;

public class Program
{
    public static void Main()
    {
        Console.WriteLine(Random.Shared != null);

        // Same instance on every access.
        Console.WriteLine(ReferenceEquals(Random.Shared, Random.Shared));

        // Every member reachable through it, and in range.
        bool inRange = true;
        var seen = new HashSet<int>();

        for (int i = 0; i < 500; i++)
        {
            int v = Random.Shared.Next(0, 10);
            if (v < 0 || v >= 10) inRange = false;
            seen.Add(v);

            if (Random.Shared.Next() < 0) inRange = false;
            if (Random.Shared.Next(100) is < 0 or >= 100) inRange = false;

            double d = Random.Shared.NextDouble();
            if (d < 0.0 || d >= 1.0) inRange = false;

            float f = Random.Shared.NextSingle();
            if (f < 0.0f || f >= 1.0f) inRange = false;

            long l = Random.Shared.NextInt64(5, 15);
            if (l < 5L || l >= 15L) inRange = false;
        }

        var buffer = new byte[8];
        Random.Shared.NextBytes(buffer);
        Console.WriteLine(""buffer length: "" + buffer.Length);

        Console.WriteLine(""Shared in range: "" + inRange);
        Console.WriteLine(""Shared distinct Next(0,10): "" + seen.Count);

        // Shared advances a single sequence: consecutive draws are not locked together.
        bool advances = false;
        for (int i = 0; i < 100; i++)
        {
            if (Random.Shared.Next() != Random.Shared.Next()) advances = true;
        }

        Console.WriteLine(""Shared advances: "" + advances);
    }
}");
        }

        /// <summary>Random is <c>virtual</c> throughout, so a derived generator can override Sample.</summary>
        [TestMethod]
        public async Task TestDerivedRandomAsync()
        {
            await RunTest(
                @"
using System;

public class Fixed : Random
{
    private readonly double _value;
    public Fixed(double value) { _value = value; }
    protected override double Sample() { return _value; }
}

public class Program
{
    public static void Main()
    {
        // Sample() feeds Next(min,max)/Next(max)/NextDouble, so a fixed sample pins them all.
        var r = new Fixed(0.5);
        Console.WriteLine(r.Next(0, 10));
        Console.WriteLine(r.Next(100));
        Console.WriteLine(r.NextDouble());

        var zero = new Fixed(0.0);
        Console.WriteLine(zero.Next(3, 9));

        // Just under 1.0 must stay below maxValue.
        var high = new Fixed(0.9999999);
        Console.WriteLine(high.Next(0, 10));
        Console.WriteLine(high.Next(3, 9));
    }
}");
        }
    }
}
