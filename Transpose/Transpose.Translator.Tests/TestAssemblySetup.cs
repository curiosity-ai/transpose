using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Suite-wide setup.
    ///
    /// <para>
    /// Every build mints a fresh cache-busting token and stamps it into the bundle it emits
    /// (<c>Transpose.Require.cacheBust("…")</c> — see <see cref="CacheBust"/>), which is the one thing
    /// about the emitted JavaScript that is deliberately different every time. Several tests here
    /// compile the same sources twice and compare the two bundles byte for byte — that a full build and
    /// an incremental one agree, that a different build of the base library changes nothing, that
    /// synthesized temp names are stable — so the token is pinned for the whole suite and those
    /// comparisons go back to being about the compiler.
    /// </para>
    ///
    /// <para>
    /// <see cref="CacheBustTests"/> is what covers the minting itself; it saves and restores the
    /// variable around each case, which is safe because MSTest runs this assembly's tests sequentially.
    /// </para>
    /// </summary>
    [TestClass]
    public static class TestAssemblySetup
    {
        [AssemblyInitialize]
        public static void Initialize(TestContext _) =>
            Environment.SetEnvironmentVariable(CacheBust.EnvVar, "testbuild");
    }
}
