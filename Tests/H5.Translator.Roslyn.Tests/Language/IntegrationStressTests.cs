using System.Threading.Tasks;

namespace H5.Translator.Roslyn.Tests;

[TestClass]
public class IntegrationStressTests : TranslatorTestBase
{
    [TestMethod]
    public async Task ShapesEmployeesPrimesAndPatterns()
    {
        var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public interface IShape { double Area(); string Name { get; } }

public abstract class Shape : IShape
{
    public abstract double Area();
    public abstract string Name { get; }
    public override string ToString() => $"{Name}(area={Area():F2})";
}

public class Circle : Shape
{
    private readonly double r;
    public Circle(double r) { this.r = r; }
    public override double Area() => Math.PI * r * r;
    public override string Name => "Circle";
}

public class Rect : Shape
{
    public double W { get; }
    public double H { get; }
    public Rect(double w, double h) { W = w; H = h; }
    public override double Area() => W * H;
    public override string Name => "Rect";
}

public record Employee(string Name, string Dept, int Salary);

public class Program
{
    static IEnumerable<int> Primes(int max)
    {
        for (int n = 2; n <= max; n++)
        {
            bool isPrime = true;
            for (int d = 2; d * d <= n; d++)
                if (n % d == 0) { isPrime = false; break; }
            if (isPrime) yield return n;
        }
    }

    static void Main()
    {
        List<IShape> shapes = new() { new Circle(2), new Rect(3, 4), new Circle(1) };
        foreach (var s in shapes) Console.WriteLine(s);
        Console.WriteLine("Total area: " + shapes.Sum(s => s.Area()).ToString("F2"));
        Console.WriteLine("Largest: " + shapes.OrderByDescending(s => s.Area()).First().Name);

        Console.WriteLine("Primes: " + string.Join(",", Primes(30)));

        var employees = new List<Employee>
        {
            new("Alice", "Eng", 100), new("Bob", "Eng", 90),
            new("Carol", "Sales", 80), new("Dave", "Sales", 85), new("Eve", "Eng", 120)
        };

        var byDept = from e in employees
                     group e by e.Dept into g
                     select new { Dept = g.Key, Avg = g.Average(x => x.Salary), Count = g.Count() };
        foreach (var d in byDept.OrderBy(d => d.Dept))
            Console.WriteLine($"{d.Dept}: avg={d.Avg:F1} count={d.Count}");

        var top = employees.Where(e => e.Salary > 85).Select(e => e.Name).OrderBy(n => n);
        Console.WriteLine("High earners: " + string.Join(", ", top));

        var counts = new Dictionary<string, int>();
        foreach (var e in employees)
            counts[e.Dept] = counts.TryGetValue(e.Dept, out var c) ? c + 1 : 1;
        foreach (var kv in counts.OrderBy(k => k.Key))
            Console.WriteLine($"{kv.Key}={kv.Value}");

        object[] things = { 42, "hi", 3.14, true, new Circle(5) };
        foreach (var t in things)
        {
            string desc = t switch
            {
                int i => $"int {i}",
                string str => $"str '{str}'",
                double d => $"double {d}",
                bool b => $"bool {b}",
                IShape sh => $"shape {sh.Name}",
                _ => "?"
            };
            Console.WriteLine(desc);
        }
    }
}

""";
        await RunTest(code);
    }
}
