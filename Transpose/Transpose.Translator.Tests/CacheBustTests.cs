using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// <see cref="CacheBust"/>: the token a build stamps into every bundle it emits, which
    /// <c>Transpose.Require</c> then appends to each URL it fetches, so a redeployed page does not
    /// serve a browser's cached copy of a file whose name did not change.
    ///
    /// <para>
    /// The suite pins the token (<see cref="TestAssemblySetup"/>) so that comparing two compilations
    /// means something; these tests take the pin off and put it back, which is safe because MSTest runs
    /// this assembly sequentially.
    /// </para>
    /// </summary>
    [TestClass]
    public class CacheBustTests
    {
        private string? _pinned;

        [TestInitialize]
        public void Unpin()
        {
            _pinned = Environment.GetEnvironmentVariable(CacheBust.EnvVar);
            Environment.SetEnvironmentVariable(CacheBust.EnvVar, null);
        }

        [TestCleanup]
        public void Repin() => Environment.SetEnvironmentVariable(CacheBust.EnvVar, _pinned);

        [TestMethod]
        public void AMintedTokenIsFixedWidthAndOrdersByBuildTime()
        {
            var first = CacheBust.NewToken();
            var second = CacheBust.NewToken();

            Assert.AreEqual(11, first.Length, "8 base-36 digits of the build time plus 3 random ones");
            Assert.AreEqual(11, second.Length, "every token is the same width — which is what makes "
                + "comparing two of them as strings mean 'which build is newer'");
            foreach (var c in first)
                Assert.IsTrue(char.IsAsciiDigit(c) || (c >= 'a' && c <= 'z'),
                    $"'{c}' is not a base-36 digit; the token goes into a URL query and a JS string "
                    + "literal, so it has to need escaping in neither");

            // What orders is the 8-digit TIME STAMP, and only that: the runtime keeps the greatest token
            // a page's assemblies give it, and that has to be the newest build. The 3-character tail is
            // random and orders nothing — it is there to keep two builds apart, and two tokens minted
            // in the same millisecond (which is what this loop does, and what no two builds do) can
            // therefore compare either way.
            Assert.IsTrue(string.CompareOrdinal(second.Substring(0, 8), first.Substring(0, 8)) >= 0,
                $"'{second}' minted after '{first}' must not stamp an earlier time");
        }

        [TestMethod]
        public void TwoBuildsGetDifferentTokens()
        {
            // A millisecond stamp alone repeats when two builds land in the same millisecond, which is
            // exactly what a test loop does; the random tail is what keeps them apart.
            var tokens = new System.Collections.Generic.HashSet<string>();
            for (var i = 0; i < 200; i++) tokens.Add(CacheBust.NewToken());

            Assert.IsTrue(tokens.Count > 190,
                $"200 tokens minted back to back produced only {tokens.Count} distinct values");
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("0")]
        [DataRow("none")]
        [DataRow("FALSE")]
        public void TheEnvironmentVariableCanSwitchBustingOff(string value)
        {
            Environment.SetEnvironmentVariable(CacheBust.EnvVar, value);

            Assert.AreEqual("", CacheBust.NewToken(),
                "an empty token emits no cacheBust call at all, which is what a build whose output has "
                + "to be byte-identical to another's asks for");
        }

        [TestMethod]
        public void TheEnvironmentVariablePinsTheToken()
        {
            Environment.SetEnvironmentVariable(CacheBust.EnvVar, " v2.1_rc-3 ");

            Assert.AreEqual("v2.1_rc-3", CacheBust.NewToken(),
                "a pinned token is taken as given, trimmed");
        }

        [TestMethod]
        public void APinnedTokenIsStrippedOfWhatWouldNeedEscaping()
        {
            Environment.SetEnvironmentVariable(CacheBust.EnvVar, "a\"b&c d/e");

            Assert.AreEqual("abcde", CacheBust.NewToken(),
                "the token is written into a JS string literal and into a URL's query, so anything that "
                + "would have to be escaped in either is dropped rather than trusted");
        }
    }
}
