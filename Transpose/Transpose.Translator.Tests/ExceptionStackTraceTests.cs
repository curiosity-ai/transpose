using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// A value caught by <c>catch (Exception)</c> is either a real <c>System.Exception</c> — which
    /// captured an <c>Error</c> into <c>errorStack</c> when it was constructed — or a RAW JavaScript
    /// error thrown by interop or a rejected promise, which has a native <c>stack</c> and no
    /// <c>errorStack</c>. C# matches both, but <c>StackTrace</c> read <c>errorStack.stack</c>
    /// unconditionally, so it came back null for every raw error while a C#-thrown exception worked.
    ///
    /// The read now goes through <c>TransposeR.stackTrace</c>, which takes whichever shape arrived.
    /// The alternative — normalising every caught value into a wrapper the way h5 does — was rejected:
    /// it allocates on entry to every catch clause and makes <c>throw;</c> rethrow the wrapper rather
    /// than the original error, losing its identity and native stack for any outer JS handler.
    /// </summary>
    [TestClass]
    public class ExceptionStackTraceTests : TranslatorTestBase
    {
        [TestMethod]
        public void StackTraceReadsGoThroughTheHelper()
        {
            var code = @"
using System;

public class Program
{
    public static string Read()
    {
        try { throw new InvalidOperationException(""x""); }
        catch (Exception e) { return e.StackTrace; }
    }
    public static void Main() { }
}";
            var result = new RoslynTranslator().Translate(code);

            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains("TransposeR.stackTrace("),
                "Exception.StackTrace must read through the helper so a raw JS error also resolves\n"
                + result.Javascript);
        }

        /// <summary>A C#-thrown exception keeps its captured stack, and ToString() (which reads
        /// StackTrace internally) still works.</summary>
        [TestMethod]
        public async Task CSharpThrownExceptionStillHasAStackTraceAsync()
        {
            await RunTest(@"
using System;

public class Program
{
    static void Deep() => throw new InvalidOperationException(""deep"");

    public static void Main()
    {
        try { Deep(); }
        catch (Exception e)
        {
            Console.WriteLine(""has stack: "" + (e.StackTrace != null));
            Console.WriteLine(""tostring: "" + e.ToString().Contains(""deep""));
        }
    }
}");
        }

        /// <summary>A raw JS error crossing into C# resolves its native stack; a thrown non-Error
        /// (a bare string, an object literal) has no stack and correctly yields null.</summary>
        [TestMethod]
        public async Task RawJavaScriptErrorExposesItsNativeStackAsync()
        {
            var js = await RunTest(@"
using System;
using Transpose;

public class Native
{
    [Script(""throw new TypeError('js boom');"")] public static extern void ThrowError();
    [Script(""throw 'bare string';"")]            public static extern void ThrowString();
}

public class Program
{
    public static void Main()
    {
        try { Native.ThrowError(); }
        catch (Exception e) { Console.WriteLine(""error stack: "" + (e.StackTrace ?? """").Split('\n')[0]); }

        try { Native.ThrowString(); }
        catch (Exception e) { Console.WriteLine(""string stack null: "" + (e.StackTrace == null)); }
    }
}", skipRoslyn: true);

            StringAssert.Contains(js, "error stack: TypeError: js boom",
                "a raw JS error must expose its native stack through Exception.StackTrace");
            StringAssert.Contains(js, "string stack null: True",
                "a thrown non-Error has no stack, so StackTrace must be null rather than undefined");
        }
    }
}
