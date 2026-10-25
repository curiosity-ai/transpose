using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// The implicit preprocessor symbols a target framework moniker carries, which
/// <see cref="ProjectResolver"/> has to reproduce because the Transpose SDK overrides the project's
/// own <c>TargetFramework</c> and the real .NET SDK never gets to define them.
///
/// <para>These went uncovered, and the gap they left was silent: the <c>NETx_0_OR_GREATER</c> chain
/// for net5.0+ was walked from a hard-coded list of versions, so the first .NET release past the end
/// of that list got its own <c>NETx_0</c> symbol and no <c>_OR_GREATER</c> — a <c>#if</c> that had
/// been true for every previous framework quietly turned false. The ladder is derived now, and the
/// net11.0 and net12.0 cases below are the ones that would have caught it.</para>
/// </summary>
[TestClass]
public sealed class FrameworkDefinesTests
{
    private static string[] Defines(string tfm) => ProjectResolver.FrameworkDefines(tfm).ToArray();

    // ---- net5.0+ ------------------------------------------------------------

    /// <summary>
    /// A net5.0+ moniker carries <c>NET</c>, <c>NETCOREAPP</c>, its own <c>NETx_y</c>, the whole
    /// legacy netcoreapp chain, and an <c>_OR_GREATER</c> for every major from 5 up to its own.
    /// </summary>
    [TestMethod]
    public void NetFiveAndLaterCarryTheFullChain()
    {
        var net11 = Defines("net11.0");

        CollectionAssert.Contains(net11, "NET");
        CollectionAssert.Contains(net11, "NETCOREAPP");
        CollectionAssert.Contains(net11, "NET11_0");

        for (var major = 5; major <= 11; major++)
            CollectionAssert.Contains(net11, $"NET{major}_0_OR_GREATER", $"net11.0 implies NET{major}_0_OR_GREATER");

        // The legacy chain is implied in full, whatever the net5.0+ version is.
        foreach (var legacy in new[] { "NETCOREAPP1_0_OR_GREATER", "NETCOREAPP2_1_OR_GREATER", "NETCOREAPP3_1_OR_GREATER" })
            CollectionAssert.Contains(net11, legacy);

        CollectionAssert.DoesNotContain(net11, "NETSTANDARD");
        CollectionAssert.DoesNotContain(net11, "NETFRAMEWORK");
    }

    /// <summary>
    /// The chain stops at the target's own major: a net8.0 project must not see a symbol that claims
    /// a later framework. This is the half of the derivation a wrong loop bound would break.
    /// </summary>
    [TestMethod]
    public void TheChainStopsAtTheTargetsOwnVersion()
    {
        var net8 = Defines("net8.0");

        CollectionAssert.Contains(net8,       "NET8_0");
        CollectionAssert.Contains(net8,       "NET8_0_OR_GREATER");
        CollectionAssert.DoesNotContain(net8, "NET9_0_OR_GREATER");
        CollectionAssert.DoesNotContain(net8, "NET10_0_OR_GREATER");
        CollectionAssert.DoesNotContain(net8, "NET11_0_OR_GREATER");
        CollectionAssert.DoesNotContain(net8, "NET9_0");

        var net5 = Defines("net5.0");

        CollectionAssert.Contains(net5,       "NET5_0_OR_GREATER");
        CollectionAssert.DoesNotContain(net5, "NET6_0_OR_GREATER");
    }

    /// <summary>
    /// A framework the compiler has never heard of needs no code change: the ladder is computed from
    /// the moniker, so the next .NET behaves like every release before it.
    /// </summary>
    [TestMethod]
    public void AFutureFrameworkNeedsNoListToBeUpdated()
    {
        var net12 = Defines("net12.0");

        CollectionAssert.Contains(net12, "NET12_0");
        CollectionAssert.Contains(net12, "NET12_0_OR_GREATER");
        CollectionAssert.Contains(net12, "NET11_0_OR_GREATER");
        CollectionAssert.Contains(net12, "NET5_0_OR_GREATER");
    }

    /// <summary>An OS platform suffix decides no defines and is dropped before the version is read.</summary>
    [TestMethod]
    public void APlatformSuffixIsIgnored()
    {
        CollectionAssert.AreEquivalent(Defines("net11.0"), Defines("net11.0-windows"));
        CollectionAssert.AreEquivalent(Defines("net11.0"), Defines("net11.0-windows10.0.19041.0"));
    }

    // ---- the other families -------------------------------------------------

    /// <summary>netstandard keeps its own irregular ladder and borrows nothing from .NET.</summary>
    [TestMethod]
    public void NetStandardCarriesItsOwnChain()
    {
        var ns20 = Defines("netstandard2.0");

        CollectionAssert.Contains(ns20,       "NETSTANDARD");
        CollectionAssert.Contains(ns20,       "NETSTANDARD2_0");
        CollectionAssert.Contains(ns20,       "NETSTANDARD1_6_OR_GREATER");
        CollectionAssert.Contains(ns20,       "NETSTANDARD2_0_OR_GREATER");
        CollectionAssert.DoesNotContain(ns20, "NETSTANDARD2_1_OR_GREATER");
        CollectionAssert.DoesNotContain(ns20, "NET");
        CollectionAssert.DoesNotContain(ns20, "NETCOREAPP");
    }

    /// <summary>netcoreapp3.1 and earlier stop inside the legacy chain and never reach the net5.0+ one.</summary>
    [TestMethod]
    public void LegacyNetCoreAppStopsAtItsOwnVersion()
    {
        var nca31 = Defines("netcoreapp3.1");

        CollectionAssert.Contains(nca31,       "NETCOREAPP");
        CollectionAssert.Contains(nca31,       "NETCOREAPP3_1");
        CollectionAssert.Contains(nca31,       "NETCOREAPP2_2_OR_GREATER");
        CollectionAssert.Contains(nca31,       "NETCOREAPP3_1_OR_GREATER");
        CollectionAssert.DoesNotContain(nca31, "NET5_0_OR_GREATER");
        CollectionAssert.DoesNotContain(nca31, "NET");
    }

    /// <summary>
    /// .NET Framework is the other reading of a "net" prefix: the digits are the version, so
    /// <c>net472</c> must not be mistaken for the three digits of a net5.0+ moniker.
    /// </summary>
    [TestMethod]
    public void NetFrameworkIsReadAsDigits()
    {
        var net472 = Defines("net472");

        CollectionAssert.Contains(net472,       "NETFRAMEWORK");
        CollectionAssert.Contains(net472,       "NET472");
        CollectionAssert.DoesNotContain(net472, "NET");
        CollectionAssert.DoesNotContain(net472, "NETCOREAPP");
        CollectionAssert.DoesNotContain(net472, "NET5_0_OR_GREATER");
    }
}
