using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Pins the console format of every error and warning <c>tps</c> prints to the canonical
/// MSBuild/Visual Studio diagnostic format, so a build that shells out to <c>tps</c> gets real
/// errors and warnings instead of anonymous text (see <see cref="MsBuildDiagnostic"/>).
///
/// The oracle is MSBuild's own parser: <see cref="Canonical"/> is the regular expression
/// <c>CanonicalError.Parse</c> applies to each line of a tool's output, copied verbatim from
/// <c>Microsoft.Build.Utilities.Core</c>. Matching it is the whole contract — a diagnostic line that
/// does not match is invisible to the build, and a *non*-diagnostic line that does match becomes a
/// build error invented out of the compiler's own chatter, which is why both directions are tested.
/// </summary>
[TestClass]
public sealed class MsBuildDiagnosticFormatTests
{
    /// <summary>
    /// Verbatim from MSBuild's <c>CanonicalError</c> (the origin/category/code/text form):
    /// <c>Origin : Subcategory Category Code : Text</c>. Case-insensitive, as MSBuild applies it.
    /// </summary>
    private const string Canonical =
        @"^\s*(((?<ORIGIN>(((\d+>)?[a-zA-Z]?:[^:]*)|([^:]*))):)|())(?<SUBCATEGORY>(()|([^:]*? )))(?<CATEGORY>(error|warning))( \s*(?<CODE>[^: ]*))?\s*:(?<TEXT>.*)$";

    private static Match Parse(string line) => Regex.Match(line, Canonical, RegexOptions.IgnoreCase);

    /// <summary>The first error Roslyn reports for a snippet, so the test formats a real diagnostic
    /// (with a real location and message) rather than a hand-built stand-in.</summary>
    private static Diagnostic FirstError(string fileName, string code)
        => new RoslynTranslator().Translate(new[] { (fileName, code) }, "FmtTest")
            .Errors.First(d => d.Id == "CS0103");

    [TestMethod]
    public void ADiagnosticWithASourceLocationIsParsedByMsBuild()
    {
        var d = FirstError("Main.cs", "public class C { public int M() { return Missing(); } }");
        var line = MsBuildDiagnostic.Format(d);

        var m = Parse(line);
        Assert.IsTrue(m.Success, $"MSBuild would not recognise this as a diagnostic: {line}");
        Assert.AreEqual("error", m.Groups["CATEGORY"].Value, "category must be literally 'error'");
        Assert.AreEqual("CS0103", m.Groups["CODE"].Value);
        StringAssert.Contains(m.Groups["TEXT"].Value, "does not exist");

        // The origin is what makes the diagnostic navigable: a file, then its 1-based line/column.
        StringAssert.Matches(m.Groups["ORIGIN"].Value, new Regex(@"Main\.cs\(1,4[0-9]\)$"),
            "origin must be the file with a (line,column) position");
    }

    [TestMethod]
    public void TheSourcePathIsAbsolute()
    {
        // A relative path is legal but is resolved against the caller's working directory, which is not
        // ours to assume — only an absolute path always lands in the right file when double-clicked.
        var line = MsBuildDiagnostic.Format("error", "CS0103", "nope", Path.Combine("sub", "Main.cs"), 3, 9);

        var origin = Parse(line).Groups["ORIGIN"].Value;
        Assert.IsTrue(Path.IsPathRooted(origin), $"origin must be rooted: {origin}");
        StringAssert.EndsWith(origin, $"Main.cs(3,9)");
    }

    [TestMethod]
    public void AToolLevelDiagnosticIsAttributedToTheToolAndStillParses()
    {
        var line = MsBuildDiagnostic.Format("error", MsBuildDiagnostic.CodeProjectNotFound,
            "No .csproj found at '/nope'.");

        var m = Parse(line);
        Assert.IsTrue(m.Success, $"MSBuild would not recognise this as a diagnostic: {line}");
        Assert.AreEqual("tps", m.Groups["ORIGIN"].Value.Trim(), "a tool-level diagnostic names the tool");
        Assert.AreEqual("error", m.Groups["CATEGORY"].Value);
        Assert.AreEqual("TPS0002", m.Groups["CODE"].Value);
    }

    [TestMethod]
    public void AWarningIsCategorisedAsAWarning()
    {
        var d = Microsoft.CodeAnalysis.Diagnostic.Create(
            new DiagnosticDescriptor("TPS9999", "t", "a warning message", "c", DiagnosticSeverity.Warning, true),
            Location.None);

        var m = Parse(MsBuildDiagnostic.Format(d));
        Assert.AreEqual("warning", m.Groups["CATEGORY"].Value);
        Assert.AreEqual("TPS9999", m.Groups["CODE"].Value);
    }

    [TestMethod]
    public void AMultiLineMessageIsFlattenedOntoOneLine()
    {
        // MSBuild matches line by line, so a newline mid-message would truncate the diagnostic — and
        // leave its tail loose on the console, where it may parse as something else entirely.
        var line = MsBuildDiagnostic.Format("error", "TPS0004", "Boom\n   at Foo.Bar()\r\n   at Baz()");

        Assert.IsFalse(line.Contains('\n') || line.Contains('\r'), "the formatted line must be one line");
        StringAssert.Contains(Parse(line).Groups["TEXT"].Value, "Boom at Foo.Bar() at Baz()");
    }

    [TestMethod]
    public void AnEmptyMessageStillSaysSomething()
    {
        var m = Parse(MsBuildDiagnostic.Format("error", "TPS0004", "   "));
        Assert.IsTrue(m.Success);
        Assert.AreNotEqual("", m.Groups["TEXT"].Value.Trim(), "an empty diagnostic tells the user nothing");
    }

    [TestMethod]
    public void EveryReportedErrorIsParsable()
    {
        // The whole error list, not just a sample: ordering and truncation must not produce a line the
        // build cannot read.
        var errors = ProjectBuild.OrderErrorsForReport(
            new RoslynTranslator().Translate(new[]
            {
                ("A.cs", "public class A { public int M() { return X(); } }"),
                ("B.cs", "public class B { public int M() { return Y(); } }"),
            }, "FmtTest").Errors.ToList());

        Assert.IsTrue(errors.Count >= 2);
        foreach (var d in errors)
            Assert.IsTrue(Parse(MsBuildDiagnostic.Format(d)).Success,
                $"MSBuild would not recognise: {MsBuildDiagnostic.Format(d)}");
    }

    /// <summary>
    /// The other half of the contract: the compiler's own progress, summary and timing output must not
    /// look like a diagnostic. These lines are real output from <c>tps</c>; if one of them ever starts
    /// matching, a plain build grows an error nobody wrote — which is exactly why the error summary
    /// does not end in "error(s):".
    /// </summary>
    [TestMethod]
    public void TheCompilersOwnChatterIsNotMistakenForADiagnostic()
    {
        string[] lines =
        [
            "tps: compiling App.csproj",
            "  sources:    263 file(s) (own sources only)",
            "  references: 3 assembly(ies) — Transpose, Transpose.Core",
            "  config:     Debug",
            "  assembly:   metadata only (throw-null bodies, not packable)",
            "  scanning for unsupported features",
            "  emitting JavaScript: 12/263",
            "3 error(s), by id: CS0103×2, CS0246×1",
            "… and 245 more (raise or drop --max-errors to see them)",
            "12 warning(s) total",
            "FAILED in 1621 ms.",
            "FAILED building referenced projects.",
            "  timing breakdown:",
            "      120 ms   12.0%     30 MB alloc  bind + diagnostics (error path)",
            "  dependency up-to-date: Tesserae",
            "OK — built package app.dll (1,234 bytes) with 2 embedded resource(s) in 900 ms.",
            "--- inner System.InvalidOperationException",
            "   at Transpose.Translator.Emit.Emitter.Emit() in /src/Emitter.cs:line 42",
        ];

        foreach (var line in lines)
            Assert.IsFalse(Parse(line).Success,
                $"MSBuild would invent a diagnostic out of this line: {line}");
    }
}
