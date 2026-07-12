using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite case S21 (records lowered to synthesized classes) and S23 (with-expressions)
    [TestClass]
    public class RC_S21_RecordTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task Records_EqualityHashToStringDeconstruct()
        {
            var code = """
using System;

public record Point(int X, int Y);
public record Tagged(string Name)
{
    public int Extra { get; set; }
}

public class Program
{
    public static void Main()
    {
        var a = new Point(1, 2);
        var b = new Point(1, 2);
        var c = new Point(3, 4);

        Console.WriteLine(a == b);
        Console.WriteLine(a == c);
        Console.WriteLine(a.Equals(b));
        Console.WriteLine(a != c);
        Console.WriteLine(a.GetHashCode() == b.GetHashCode());
        Console.WriteLine(a.ToString());
        Console.WriteLine(c);

        var (x, y) = c;
        Console.WriteLine(x + "," + y);

        // extra mutable state does not participate in positional ctor
        var t1 = new Tagged("n") { Extra = 1 };
        Console.WriteLine(t1.Name + ":" + t1.Extra);
        Console.WriteLine(t1.ToString());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Records_InheritanceAndWith()
        {
            var code = """
using System;

public record Animal(string Name)
{
    public virtual string Speak() => "...";
}
public record Dog(string Name, string Breed) : Animal(Name)
{
    public override string Speak() => "woof";
}

public class Program
{
    public static void Main()
    {
        var d = new Dog("Rex", "Lab");
        Console.WriteLine(d.Name + "/" + d.Breed);
        Console.WriteLine(d.Speak());

        Animal a = d;
        Console.WriteLine(a.Name);
        Console.WriteLine(a.Speak());

        // with-expression: copy + modify, original untouched
        var d2 = d with { Breed = "Poodle" };
        Console.WriteLine(d2.Name + "/" + d2.Breed);
        Console.WriteLine(d.Breed);
        Console.WriteLine(d == d2);
        var d3 = d with { };
        Console.WriteLine(d == d3);

        // nested with over multiple properties
        var d4 = d with { Name = "Max", Breed = "Husky" };
        Console.WriteLine(d4.Name + "/" + d4.Breed);

        // records with user-defined members interacting with `with`
        var w1 = new Wrapped(5) { Note = "keep" };
        var w2 = w1 with { V = 6 };
        Console.WriteLine(w2.V + ":" + w2.Note);
    }
}

public record Wrapped(int V)
{
    public string Note { get; init; } = "";
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task RecordStructs_And_UserDefinedMembers()
        {
            var code = """
using System;

public record struct Size(int W, int H)
{
    public int Area => W * H;
}

public record Custom(int N)
{
    public override string ToString() => "Custom!" + N;   // user ToString wins
}

public class Program
{
    public static void Main()
    {
        var s1 = new Size(2, 3);
        var s2 = new Size(2, 3);
        Console.WriteLine(s1 == s2);
        Console.WriteLine(s1.Area);
        var s3 = s1 with { W = 10 };
        Console.WriteLine(s3.W + "x" + s3.H);

        // mutation on record struct
        var s4 = new Size(1, 1);
        s4.W = 7;
        Console.WriteLine(s4.W);

        Console.WriteLine(new Custom(3).ToString());
        Console.WriteLine(new Custom(3) == new Custom(3));
    }
}
""";
            await RunTest(code);
        }
    }
}
