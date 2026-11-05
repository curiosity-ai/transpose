using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests;

/// <summary>
/// <c>StringBuilder.Length</c> is read after every mutation in the loops a parser writes, so it must be
/// answered without joining the pieces appended so far.
/// </summary>
[TestClass]
public class StringBuilderLengthTests : TranslatorTestBase
{
    [TestMethod]
    public async Task Length_agrees_with_native_after_every_mutator()
    {
        var code = """
using System;
using System.Text;
public class Program
{
    static void Show(string step, StringBuilder sb)
    {
        Console.WriteLine(step + ": " + sb.Length + " [" + sb.ToString() + "]");
    }

    public static void Main()
    {
        Show("empty", new StringBuilder());
        Show("ctor string", new StringBuilder("abc"));
        Show("ctor capacity", new StringBuilder("abc", 32));
        Show("ctor substring", new StringBuilder("abcdef", 1, 3, 16));

        var sb = new StringBuilder();
        sb.Append("hello");                 Show("append string", sb);
        sb.Append(' ');                     Show("append char", sb);
        sb.Append('x', 3);                  Show("append char repeat", sb);
        sb.Append("0123456789", 2, 4);      Show("append substring", sb);
        sb.Append(42);                      Show("append int", sb);
        sb.Append(1.5);                     Show("append double", sb);
        sb.Append((string)null);            Show("append null", sb);
        sb.Append("");                      Show("append empty", sb);
        sb.Append('\n');                    Show("newline", sb);
        sb.Append("end\n");                 Show("text and newline", sb);
        sb.AppendFormat("{0}-{1}", 1, "b"); Show("append format", sb);

        sb.Insert(0, "<<");                 Show("insert start", sb);
        sb.Insert(4, "..");                 Show("insert middle", sb);
        sb.Insert(sb.Length, ">>");         Show("insert end", sb);
        sb.Insert(2, "ab", 3);              Show("insert repeat", sb);

        sb.Remove(0, 2);                    Show("remove start", sb);
        sb.Remove(3, 4);                    Show("remove middle", sb);
        sb.Remove(2, 0);                    Show("remove nothing", sb);

        sb.Replace("ab", "Z");              Show("replace shorter", sb);
        sb.Replace("Z", "longer");          Show("replace longer", sb);
        sb.Replace('l', 'L');               Show("replace char", sb);
        sb.Replace("L", "", 0, 10);         Show("replace range", sb);

        sb[0] = 'Q';                        Show("indexer set", sb);
        sb.Length = sb.Length + 3;          Show("length grow", sb);
        sb.Length = 5;                      Show("length shrink", sb);
        sb.Length = 0;                      Show("length zero", sb);

        sb.Append("again");                 Show("append after clear", sb);
        sb.Clear();                         Show("clear", sb);

        for (int i = 0; i < 5; i++)
        {
            sb.Append(i);
            Console.WriteLine(sb.Length + " " + sb.ToString());
            sb.Insert(0, "-");
            Console.WriteLine(sb.Length + " " + sb.ToString());
        }

        Console.WriteLine("<<DONE>>");
    }
}
""";
        await RunTest(code, waitForOutput: "<<DONE>>");
    }

    [TestMethod]
    public async Task Reading_Length_after_each_append_is_not_quadratic()
    {
        // A base64 frame is one 100 KB+ line built a piece at a time. Joining the pieces on every read of Length
        // made that O(n^2): tens of seconds here, against a few milliseconds when the length is kept as it grows.
        var code = """
using System;
using System.Diagnostics;
using System.Text;
public class Program
{
    public static void Main()
    {
        var watch = Stopwatch.StartNew();
        var sb    = new StringBuilder();
        long checks = 0;

        for (int i = 0; i < 200000; i++)
        {
            sb.Append('a');

            if (sb.Length > 5000000) break;

            checks += sb.Length;
        }

        Console.WriteLine(sb.Length);
        Console.WriteLine(checks);
        Console.WriteLine(watch.ElapsedMilliseconds < 2000);
        Console.WriteLine("<<DONE>>");
    }
}
""";
        await RunTest(code, waitForOutput: "<<DONE>>");
    }
}
