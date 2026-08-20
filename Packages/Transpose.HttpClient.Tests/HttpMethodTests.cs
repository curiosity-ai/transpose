namespace Transpose.HttpClient.Tests;

/// <summary>
/// <c>HttpMethod</c> is pure value semantics with no transport in it, so every test here is compared
/// against the real System.Net.Http rather than against a recorded string.
/// </summary>
[TestClass]
public class HttpMethodTests : HttpClientTestBase
{
    [TestMethod]
    public async Task TheStandardMethodsHaveTheirVerbs()
    {
        await RunAndCompare("""
using System;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        Console.WriteLine(HttpMethod.Get.Method);
        Console.WriteLine(HttpMethod.Post.Method);
        Console.WriteLine(HttpMethod.Put.Method);
        Console.WriteLine(HttpMethod.Delete.Method);
        Console.WriteLine(HttpMethod.Head.Method);
        Console.WriteLine(HttpMethod.Options.Method);
        Console.WriteLine(HttpMethod.Trace.Method);
        Console.WriteLine(HttpMethod.Patch.Method);
    }
}
""");
    }

    [TestMethod]
    public async Task ToStringIsTheVerb()
    {
        await RunAndCompare("""
using System;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        Console.WriteLine("[" + HttpMethod.Get + "]");
        Console.WriteLine("[" + HttpMethod.Patch.ToString() + "]");
    }
}
""");
    }

    [TestMethod]
    public async Task EqualityIsCaseInsensitiveOnTheVerb()
    {
        await RunAndCompare("""
using System;
using System.Net.Http;

public class Program
{
    public static void Main()
    {
        Console.WriteLine("same: " + (HttpMethod.Get == HttpMethod.Get));
        Console.WriteLine("different: " + (HttpMethod.Get == HttpMethod.Post));
        Console.WriteLine("notEqual: " + (HttpMethod.Get != HttpMethod.Post));
        Console.WriteLine("nullLeft: " + ((HttpMethod)null == HttpMethod.Get));
        Console.WriteLine("bothNull: " + ((HttpMethod)null == (HttpMethod)null));
        Console.WriteLine("equals: " + HttpMethod.Get.Equals(HttpMethod.Get));
        Console.WriteLine("equalsObject: " + HttpMethod.Get.Equals((object)HttpMethod.Get));
        Console.WriteLine("equalsOther: " + HttpMethod.Get.Equals((object)"GET"));
        Console.WriteLine("hashMatches: " + (HttpMethod.Get.GetHashCode() == HttpMethod.Get.GetHashCode()));
    }
}
""");
    }

    /// <summary>
    /// <b>Divergence — gap.</b> .NET's <c>HttpMethod</c> constructor is public, so an application can
    /// name a verb the class does not predefine (<c>LINK</c>, <c>PROPFIND</c>, a vendor extension).
    /// Here it is private, so only the eight static properties exist and such a request cannot be
    /// expressed at all. Asserted through reflection, because the divergence is that the snippet the
    /// note describes does not compile.
    /// </summary>
    [TestMethod]
    public async Task ACustomVerbCannotBeConstructed()
    {
        await RunJs("""
using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;

public class Program
{
    public static void Main()
    {
        var constructors = typeof(HttpMethod).GetConstructors();
        Console.WriteLine("public constructors: " + constructors.Length);
    }
}
""", """
public constructors: 0
""", nativePrints: """
public constructors: 1
LINK
""", nativeCode: """
using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;

public class Program
{
    public static void Main()
    {
        var constructors = typeof(HttpMethod).GetConstructors();
        Console.WriteLine("public constructors: " + constructors.Length);
        Console.WriteLine(new HttpMethod("LINK").Method);
    }
}
""");
    }
}
