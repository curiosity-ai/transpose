namespace Transpose.Translator.Tests;

/// <summary>
/// Covers which <c>Transpose.BCL</c> version <c>TransposeAssemblies.Discover</c> picks out of the NuGet
/// cache when several are extracted there — i.e. which JavaScript runtime every compile without
/// <c>TRANSPOSE_DLL_PATH</c> binds against.
///
/// The version folders are CalVer (<c>yy.M.buildId</c>), and the resolver used to sort them as plain
/// strings, which silently prefers the *older* runtime as soon as a component's digit count changes:
/// ordinally <c>26.7.999</c> &gt; <c>26.7.3104</c> and <c>26.7.x</c> &gt; <c>26.10.x</c>. A stale runtime
/// does not fail the build — it compiles clean and only misbehaves in Node, which is how a fixed BCL bug
/// appears to still be there (the JSON suite's <c>FractionalSecondsRoundTrip</c> reporting inconclusive
/// against a runtime that predates the <c>Date.js</c> fractional-seconds fix is exactly that symptom).
/// </summary>
[TestClass]
public sealed class RuntimeAssemblyVersionOrderTests
{
    private static string Newest(params string[] versions) =>
        versions.OrderBy(v => v, TransposeAssemblies.PackageVersionOrder.Instance).Last();

    [TestMethod]
    public void MoreDigitsInABuildIdIsANewerVersion()
    {
        Assert.AreEqual("26.7.3104", Newest("26.7.999", "26.7.3104"));
        Assert.AreEqual("26.7.10001", Newest("26.7.3104", "26.7.10001"));
    }

    [TestMethod]
    public void ATwoDigitMonthIsNewerThanASingleDigitOne()
    {
        Assert.AreEqual("26.10.1", Newest("26.7.3104", "26.10.1"));
        Assert.AreEqual("27.1.1", Newest("26.12.9999", "27.1.1"));
    }

    [TestMethod]
    public void TheNewestOfAFullCacheWins()
    {
        Assert.AreEqual(
            "26.7.3104",
            Newest("26.7.2749", "26.7.2947", "26.7.3001", "26.7.3055", "26.7.3064", "26.7.3104"));
    }

    [TestMethod]
    public void AReleaseOutranksAPrereleaseOfTheSameVersion()
    {
        Assert.AreEqual("26.7.3104", Newest("26.7.3104-beta", "26.7.3104"));
        Assert.AreEqual("26.7.3105-alpha", Newest("26.7.3104", "26.7.3105-alpha"));
        Assert.AreEqual("26.7.3104-beta2", Newest("26.7.3104-beta2", "26.7.3104-beta1"));
    }

    [TestMethod]
    public void MissingSegmentsCountAsZeroAndJunkSortsLowest()
    {
        Assert.AreEqual("26.7.1", Newest("26.7", "26.7.1"));
        Assert.AreEqual("26.7.0", Newest("26.7", "26.7.0"));            // equal: the later one is kept
        Assert.AreEqual("26.7.3104", Newest("not-a-version", "26.7.3104"));
        Assert.AreEqual("26.7.3104", Newest("26.7.3104", "not-a-version"));
    }
}
