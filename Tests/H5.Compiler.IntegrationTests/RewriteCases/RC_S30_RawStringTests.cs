using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite case S30 (raw string literals) — now lexed natively by the
    // NRefactory tokenizer (consume_raw_string); these tests pin the exact
    // value semantics (delimiter runs, indentation stripping, blank lines).
    [TestClass]
    public class RC_S30_RawStringTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task RawStrings_ValueSemantics()
        {
            var code = """"""
using System;

public class Program
{
    public static void Main()
    {
        // single line with embedded quotes
        string a = """He said "hi" loudly""";
        Console.WriteLine(a);

        // longer delimiter than content quote runs
        string b = """"content with """ three quotes"""";
        Console.WriteLine(b);

        // multi-line with indentation stripping
        string c = """
            line one
              line two indented
            line three
            """;
        Console.WriteLine(c);

        // json-ish content with braces
        string d = """
            {"json": "value", "n": 1}
            """;
        Console.WriteLine(d);

        // blank line inside content
        string e = """
            first

            third
            """;
        Console.WriteLine(e);

        // lengths pin exact value equality beyond console normalization
        Console.WriteLine(a.Length + "," + b.Length + "," + c.Length + "," + d.Length + "," + e.Length);

        // raw string in expression positions
        Console.WriteLine("""inline""" + "-" + """tail""");
    }
}
"""""";
            await RunTest(code);
        }
    }
}
