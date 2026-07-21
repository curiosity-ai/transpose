using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Regression tests for two runtime/reflection gaps found migrating the Tesserae toolkit:
    ///  - <see cref="TestStringComparerOrderByAsync"/>: an <c>IComparer&lt;T&gt;</c> implemented by an
    ///    abstract base and overridden in a derived class (StringComparer/OrdinalComparer) must emit
    ///    the override under the interface member's camelCase name, so <c>OrderBy(.., comparer)</c>
    ///    (and any caller) reaches <c>.compare</c> rather than a PascalCase <c>.Compare</c>.
    ///  - <see cref="TestPropertyInfoCanReadWriteAsync"/>: a field-backed auto-property must emit its
    ///    g/s accessor records so <c>PropertyInfo.CanRead</c>/<c>CanWrite</c>/<c>GetValue</c>/<c>SetValue</c> work.
    /// </summary>
    [TestClass]
    public class ReflectionAndComparerTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task TestStringComparerOrderByAsync()
        {
            await RunTest(
                @"
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var items = new List<string> { ""Banana"", ""apple"", ""Cherry"", ""date"", ""Apple"" };

        // OrderBy with StringComparer.OrdinalIgnoreCase (an IComparer<string> whose Compare is an
        // abstract-base implementation overridden by OrdinalComparer).
        foreach (var s in items.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine(s);

        // Direct IComparer<string>.Compare call (compare to zero, to stay culture-independent).
        IComparer<string> c = StringComparer.OrdinalIgnoreCase;
        Console.WriteLine(c.Compare(""a"", ""B"") < 0);
        Console.WriteLine(c.Compare(""B"", ""a"") > 0);
        Console.WriteLine(c.Compare(""x"", ""X"") == 0);
    }
}
                ");
        }

        [TestMethod]
        public async Task TestPropertyInfoCanReadWriteAsync()
        {
            await RunTest(
                @"
using System;
using System.Reflection;

public class Person
{
    public string Name { get; set; }
    public int Age { get; set; }
    public string ReadOnlyId { get; } = ""id-1"";
}

public class Program
{
    public static void Main()
    {
        var t = typeof(Person);

        var name = t.GetProperty(""Name"");
        var age  = t.GetProperty(""Age"");
        var ro   = t.GetProperty(""ReadOnlyId"");

        Console.WriteLine(name.CanRead + "" "" + name.CanWrite);
        Console.WriteLine(age.CanRead + "" "" + age.CanWrite);
        Console.WriteLine(ro.CanRead + "" "" + ro.CanWrite);

        var p = new Person { Name = ""Ada"", Age = 36 };
        Console.WriteLine(name.GetValue(p));
        Console.WriteLine(age.GetValue(p));
        Console.WriteLine(ro.GetValue(p));

        name.SetValue(p, ""Grace"");
        age.SetValue(p, 45);
        Console.WriteLine(p.Name + "" "" + p.Age);

        // Instance public properties are discoverable via BindingFlags.
        Console.WriteLine(t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Length);
    }
}
                ");
        }
    }
}
