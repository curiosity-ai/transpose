using System.Threading.Tasks;

namespace H5.Translator.Roslyn.Tests;

[TestClass]
public class InterfaceTests : TranslatorTestBase
{
    [TestMethod]
    public async Task InterfaceDispatchAndTypeTest()
    {
        var code = """
using System;
using System.Collections.Generic;
public interface IAnimal { string Speak(); }
public interface INamed { string Name { get; } }
public class Dog : IAnimal, INamed
{
    public string Name => "Dog";
    public string Speak() => "Woof";
}
public class Cat : IAnimal, INamed
{
    public string Name => "Cat";
    public string Speak() => "Meow";
}
public class Program
{
    public static void Main()
    {
        var animals = new List<IAnimal> { new Dog(), new Cat() };
        foreach (var a in animals)
        {
            Console.WriteLine(a.Speak());
            if (a is INamed named) Console.WriteLine("  named: " + named.Name);
        }
        object o = new Dog();
        Console.WriteLine(o is IAnimal);
        Console.WriteLine(o is INamed);
        Console.WriteLine(o is Cat);
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task AnonymousTypes()
    {
        var code = """
using System;
using System.Linq;
public class Program
{
    public static void Main()
    {
        var people = new[] { new { Name = "Alice", Age = 30 }, new { Name = "Bob", Age = 25 } };
        foreach (var p in people) Console.WriteLine($"{p.Name}: {p.Age}");
        var projected = people.Select(x => new { x.Name, IsAdult = x.Age >= 18 });
        foreach (var p in projected) Console.WriteLine($"{p.Name} adult={p.IsAdult}");
    }
}
""";
        await RunTest(code);
    }
}
