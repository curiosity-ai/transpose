using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Regression tests for the chain of bugs behind Tesserae's chat crash
    /// ("Argument_AddingDuplicate" in ReconcileChildren): Guid.NewGuid() returned a constant
    /// all-zero GUID, so every message got the same dictionary key.
    ///
    /// Three root causes, each also a standalone bug:
    ///  - a constructor chaining `: this(...)` re-ran the instance field initializers, wiping what
    ///    the delegated ctor set (Random's SeedArray reset to zeros);
    ///  - `(int)long` was emitted as `.toNumber()` with no Int32 truncation, so
    ///    `(int)DateTime.Now.Ticks` fed Random a garbage (huge) seed;
    ///  - `(uint)`/`(ushort)`/`(byte)` of an int was erased, so a negative value stayed negative and
    ///    Guid.ToString()'s hex formatting produced a malformed string.
    /// </summary>
    [TestClass]
    public class GuidAndIntegerCastTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task TestConstructorThisChainKeepsFieldInitAsync()
        {
            await RunTest(
                @"
using System;

public class Box
{
    public int[] Data = new int[3];   // field initializer must NOT re-run in the `: this(...)` ctor
    public Box() : this(7) { }
    public Box(int seed) { Data[0] = seed; Data[1] = seed + 1; Data[2] = seed + 2; }
}

public class Program
{
    public static void Main()
    {
        var b = new Box();
        Console.WriteLine(b.Data[0] + "","" + b.Data[1] + "","" + b.Data[2]);   // 7,8,9 — not 0,0,0
    }
}
                ");
        }

        [TestMethod]
        public async Task TestLongToIntTruncationAsync()
        {
            await RunTest(
                @"
using System;

public class Program
{
    public static void Main()
    {
        long a = 639202306861580000L;
        Console.WriteLine((int)a);                     // wraps to the low 32 bits
        long b = 4294967296L + 5L;                     // 2^32 + 5
        Console.WriteLine((int)b);                     // 5
        int neg = -1;
        Console.WriteLine((uint)neg);                  // 4294967295
    }
}
                ");
        }

        [TestMethod]
        public async Task TestUnsignedNarrowingAsync()
        {
            await RunTest(
                @"
using System;

public class Program
{
    public static void Main()
    {
        int neg = -123;
        Console.WriteLine((uint)neg);                  // 4294967173
        Console.WriteLine(((uint)neg).ToString(""x8""));  // ffffff85
        int big = 300;
        Console.WriteLine((byte)big);                  // 44
        int negOne = -1;
        Console.WriteLine((ushort)negOne);             // 65535
    }
}
                ");
        }

        [TestMethod]
        public async Task TestGuidNewGuidIsUniqueAndFormatsAsync()
        {
            var output = await RunTest(
                @"
using System;
using System.Collections.Generic;

public class Program
{
    public static void Main()
    {
        var seen = new HashSet<string>();
        int dups = 0;
        string sample = null;
        for (int i = 0; i < 500; i++)
        {
            var s = Guid.NewGuid().ToString();
            sample = sample ?? s;
            if (!seen.Add(s)) dups++;
        }
        Console.WriteLine(""dups="" + dups);            // 0
        Console.WriteLine(""len="" + sample.Length);    // 36 (8-4-4-4-12)
        Console.WriteLine(""dashes="" + (sample.Split('-').Length - 1)); // 4
    }
}
                ",
                skipRoslyn: true); // GUIDs are random — assert shape via the script's own output, not a native diff

            StringAssert.Contains(output, "dups=0");
            StringAssert.Contains(output, "len=36");
            StringAssert.Contains(output, "dashes=4");
        }
    }
}
