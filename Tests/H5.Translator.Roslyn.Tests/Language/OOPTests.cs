using System.Threading.Tasks;

namespace H5.Translator.Roslyn.Tests;

[TestClass]
public class OOPTests : TranslatorTestBase
{
    [TestMethod]
    public async Task InheritanceAndVirtualMethods()
    {
        var code = """
using System;

public abstract class Animal
{
    public string Name { get; set; }
    protected int legs;
    public Animal(string name, int legs) { Name = name; this.legs = legs; }
    public abstract string Speak();
    public virtual string Describe() => Name + " has " + legs + " legs and says " + Speak();
}

public class Dog : Animal
{
    public Dog(string name) : base(name, 4) { }
    public override string Speak() => "Woof";
}

public class Cat : Animal
{
    public Cat(string name) : base(name, 4) { }
    public override string Speak() => "Meow";
    public override string Describe() => "Cat " + base.Describe();
}

public class Program
{
    public static void Main()
    {
        Animal[] animals = new Animal[] { new Dog("Rex"), new Cat("Tom") };
        foreach (var a in animals) { Console.WriteLine(a.Describe()); }
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task StaticMembersAndConstructors()
    {
        var code = """
using System;

public class Counter
{
    private int count;
    public static int Instances;
    public Counter() { count = 0; Instances++; }
    public void Inc() { count++; }
    public int Value => count;
}

public class Program
{
    public static void Main()
    {
        var c = new Counter();
        c.Inc(); c.Inc(); c.Inc();
        Console.WriteLine("Count: " + c.Value);
        var c2 = new Counter();
        Console.WriteLine("Instances: " + Counter.Instances);
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task PropertiesWithLogic()
    {
        var code = """
using System;

public class Temperature
{
    private double celsius;
    public double Celsius { get => celsius; set => celsius = value; }
    public double Fahrenheit
    {
        get { return celsius * 9 / 5 + 32; }
        set { celsius = (value - 32) * 5 / 9; }
    }
}

public class Program
{
    public static void Main()
    {
        var t = new Temperature();
        t.Celsius = 100;
        Console.WriteLine(t.Fahrenheit);
        t.Fahrenheit = 32;
        Console.WriteLine(t.Celsius);
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task ConstructorOverloadsAndChaining()
    {
        var code = """
using System;

public class Point
{
    public int X, Y;
    public Point() : this(0, 0) { }
    public Point(int x) : this(x, 0) { }
    public Point(int x, int y) { X = x; Y = y; }
    public override string ToString() => "(" + X + ", " + Y + ")";
}

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new Point().ToString());
        Console.WriteLine(new Point(5).ToString());
        Console.WriteLine(new Point(3, 7).ToString());
    }
}
""";
        await RunTest(code);
    }
}
