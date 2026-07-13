namespace H5.Translator.Roslyn.Tests;

[TestClass]
public class UnsupportedFeaturesTests : TranslatorTestBase
{
    [TestMethod]
    public void UnsafePointers()
    {
        var code = """
using System;

public class Program
{
    public static unsafe void Main()
    {
        int x = 10;
        int* p = &x;
        Console.WriteLine(*p);
    }
}
""";
        RunTestExpectingError(code, "Pointers are not supported");
    }

    [TestMethod]
    public void UnsafeBlock()
    {
        var code = """
using System;

public class Program
{
    public static void Main()
    {
        int x = 10;
        unsafe
        {
            Console.WriteLine(x);
        }
    }
}
""";
        RunTestExpectingError(code, "Unsafe code is not supported");
    }

    [TestMethod]
    public void StackAlloc()
    {
        var code = """
using System;

public class Program
{
    public static void Main()
    {
        Span<int> data = stackalloc int[10];
        Console.WriteLine(data.Length);
    }
}
""";
        RunTestExpectingError(code, "stackalloc is not supported");
    }

    [TestMethod]
    public void FileIo()
    {
        var code = """
using System;
using System.IO;

public class Program
{
    public static void Main()
    {
        File.WriteAllText("test.txt", "hello");
        Console.WriteLine("done");
    }
}
""";
        RunTestExpectingError(code, "not supported");
    }
}
