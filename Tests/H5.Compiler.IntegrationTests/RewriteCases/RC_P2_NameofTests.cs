using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite case P2 (NameofReplacer) — docs/REWRITE-REMOVAL-PLAN.md
    [TestClass]
    public class RC_P2_NameofTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task Nameof_LocalsParamsMembersAndTypes()
        {
            var code = """
using System;
using System.Collections.Generic;

public class Person
{
    public string Name { get; set; }
    public int Age;
    public void Greet() { }
    public static int Count;
}

public class Program
{
    public static void Main()
    {
        int local = 1;
        Console.WriteLine(nameof(local));
        Show(42);

        Console.WriteLine(nameof(Person));
        Console.WriteLine(nameof(Person.Name));
        Console.WriteLine(nameof(Person.Age));
        Console.WriteLine(nameof(Person.Greet));
        Console.WriteLine(nameof(Person.Count));

        Console.WriteLine(nameof(System.Console));
        Console.WriteLine(nameof(Console.WriteLine));
        Console.WriteLine(nameof(List<int>));
        Console.WriteLine(nameof(Main));
        Console.WriteLine(nameof(Program));

        var p = new Person { Name = "x" };
        Console.WriteLine(nameof(p.Name));
    }

    public static void Show(int amount)
    {
        Console.WriteLine(nameof(amount));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Nameof_InsideOtherConstructs()
        {
            var code = """
using System;

public class Program
{
    private static string _field = nameof(_field); // field initializer

    public static void Main()
    {
        Console.WriteLine(_field);

        // inside interpolation
        int counter = 3;
        Console.WriteLine($"{nameof(counter)}={counter}");

        // inside ternary and concatenation
        bool f = true;
        Console.WriteLine(f ? nameof(f) : nameof(Main));
        Console.WriteLine("member: " + nameof(Program.Main));

        // as argument and in switch
        Console.WriteLine(Pick(nameof(Pick)));
        switch (nameof(Main))
        {
            case "Main": Console.WriteLine("matched"); break;
            default: Console.WriteLine("no"); break;
        }

        // nameof of generic parameter inside generic method
        Generic<string>();
    }

    private static string Pick(string s) => s;

    private static void Generic<TItem>()
    {
        Console.WriteLine(nameof(TItem));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Nameof_UserMethodNamedNameof()
        {
            // A user-defined method called `nameof` must win over the operator
            var code = """
using System;

public class Program
{
    private static string nameof(string s) { return "user:" + s; }

    public static void Main()
    {
        Console.WriteLine(nameof("x"));
    }
}
""";
            await RunTest(code);
        }
    }
}
