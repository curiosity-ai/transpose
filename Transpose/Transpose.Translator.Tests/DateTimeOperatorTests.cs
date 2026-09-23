using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests;

/// <summary>
/// Every operation between <c>DateTime</c>, <c>DateTimeOffset</c> and <c>TimeSpan</c>, run natively
/// and as JavaScript and diffed. The three types share a family of operators, two of them are
/// runtime objects with no useful JS operator behaviour, and C# silently converts a DateTime to a
/// DateTimeOffset wherever the two meet — so an operator the emitter does not recognise falls
/// through to the JavaScript one and quietly produces something else:
///
/// <list type="bullet">
/// <item><c>DateTime.UtcNow - someDateTimeOffset</c> binds to DateTimeOffset's operator with the
/// left operand implicitly converted. The conversion was not emitted, so the operator read
/// <c>m_dateTime</c> off a raw JS Date and threw "Cannot read properties of undefined (reading
/// 'ticks')". Same for <c>??</c>, whose right half is a conversion site too.</item>
/// <item><c>-someTimeSpan</c> emitted the JS <c>-</c> against a TimeSpan object → <c>NaN</c>: the
/// unary path only knew operators declared in the compilation being translated, and never looked
/// for a <c>[Template]</c> (TimeSpan's is <c>System.TimeSpan.neg({t})</c>).</item>
/// <item>A lifted (<c>Nullable&lt;T&gt;</c>) operator guarded the null and then applied the JS
/// operator to the underlying values: <c>DateTime? - DateTime?</c> yielded a millisecond number
/// instead of a TimeSpan and <c>TimeSpan? + TimeSpan?</c> concatenated the two strings.</item>
/// <item>A lifted <c>==</c> on a user-defined operator ran that operator on the nulls
/// (<c>null.UtcDateTime</c>), and compound assignment (<c>dateTime += timeSpan</c>) emitted the JS
/// <c>+=</c>, concatenating a Date and a TimeSpan into a string.</item>
/// </list>
/// </summary>
[TestClass]
public class DateTimeOperatorTests : TranslatorTestBase
{
    private const string Preamble = @"
using System;

public class Program
{
    static readonly DateTime       DT   = new DateTime(2020, 3, 4, 5, 6, 7, DateTimeKind.Utc);
    static readonly DateTime       DT2  = new DateTime(2020, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    static readonly DateTimeOffset DTO  = new DateTimeOffset(new DateTime(2020, 3, 2, 0, 0, 0), TimeSpan.Zero);
    static readonly DateTimeOffset DTO2 = new DateTimeOffset(new DateTime(2020, 3, 3, 0, 0, 0), TimeSpan.Zero);
    static readonly TimeSpan       TS   = TimeSpan.FromHours(30);
    static readonly TimeSpan       TS2  = TimeSpan.FromHours(5);

    static void P(string n, object v) { Console.WriteLine(n + "" = "" + (v ?? ""(null)"")); }
    static string U(DateTimeOffset o) { return o.UtcDateTime.ToString(""O""); }

    public static void Main()
    {
";

    private const string Epilogue = @"
        Console.WriteLine(""<<DONE>>"");
    }
}";

    private static string Program(string body) => Preamble + body + Epilogue;

    [TestMethod]
    public async Task DateTimeAndTimeSpanArithmetic()
    {
        await RunTest(Program(@"
        P(""DT + TS"",  (DT + TS).ToString(""O""));
        P(""DT - TS"",  (DT - TS).ToString(""O""));
        P(""DT - DT2"", DT - DT2);
        P(""TS + TS2"", TS + TS2);
        P(""TS - TS2"", TS - TS2);
        P(""-TS"",      -TS);
        P(""+TS"",      +TS);
"), waitForOutput: "<<DONE>>");
    }

    [TestMethod]
    public async Task DateTimeAndTimeSpanComparisons()
    {
        await RunTest(Program(@"
        P(""DT <  DT2"",  DT <  DT2);
        P(""DT <= DT2"",  DT <= DT2);
        P(""DT >  DT2"",  DT >  DT2);
        P(""DT >= DT2"",  DT >= DT2);
        P(""DT == DT2"",  DT == DT2);
        P(""DT != DT2"",  DT != DT2);
        P(""DT == DT"",   DT == DT);
        P(""TS <  TS2"",  TS <  TS2);
        P(""TS <= TS2"",  TS <= TS2);
        P(""TS >  TS2"",  TS >  TS2);
        P(""TS >= TS2"",  TS >= TS2);
        P(""TS == TS2"",  TS == TS2);
        P(""TS != TS2"",  TS != TS2);
        P(""TS == TS"",   TS == TS);
"), waitForOutput: "<<DONE>>");
    }

    [TestMethod]
    public async Task DateTimeOffsetArithmeticAndComparisons()
    {
        await RunTest(Program(@"
        P(""DTO + TS"",     U(DTO + TS));
        P(""DTO - TS"",     U(DTO - TS));
        P(""DTO - DTO2"",   DTO - DTO2);
        P(""DTO <  DTO2"",  DTO <  DTO2);
        P(""DTO <= DTO2"",  DTO <= DTO2);
        P(""DTO >  DTO2"",  DTO >  DTO2);
        P(""DTO >= DTO2"",  DTO >= DTO2);
        P(""DTO == DTO2"",  DTO == DTO2);
        P(""DTO != DTO2"",  DTO != DTO2);
        P(""DTO == DTO"",   DTO == DTO);
"), waitForOutput: "<<DONE>>");
    }

    /// <summary>
    /// The reported crash: mixing the two types binds to DateTimeOffset's operator and converts the
    /// DateTime operand through <c>implicit operator DateTimeOffset(DateTime)</c>, which has to run.
    /// </summary>
    [TestMethod]
    public async Task MixedDateTimeAndDateTimeOffsetConvertsImplicitly()
    {
        await RunTest(Program(@"
        P(""DT - DTO"",    DT - DTO);
        P(""DTO - DT"",    DTO - DT);
        P(""DT <  DTO"",   DT <  DTO);
        P(""DT >  DTO"",   DT >  DTO);
        P(""DTO <= DT"",   DTO <= DT);
        P(""DTO >= DT"",   DTO >= DT);
        P(""DT == DTO"",   DT == DTO);
        P(""DT != DTO"",   DT != DTO);
        P(""DTO == DT"",   DTO == DT);
        P(""assign"",      U(DT));
"), waitForOutput: "<<DONE>>");
    }

    [TestMethod]
    public async Task CompoundAssignment()
    {
        await RunTest(Program(@"
        var cdt = DT;  cdt += TS;   P(""dt += ts"",  cdt.ToString(""O""));
        cdt = DT;      cdt -= TS;   P(""dt -= ts"",  cdt.ToString(""O""));
        var cts = TS;  cts += TS2;  P(""ts += ts2"", cts);
        cts = TS;      cts -= TS2;  P(""ts -= ts2"", cts);
        var cdo = DTO; cdo += TS;   P(""dto += ts"", U(cdo));
        cdo = DTO;     cdo -= TS;   P(""dto -= ts"", U(cdo));

        DateTime? ndt = DT;  ndt += TS;  P(""ndt += ts"", ndt?.ToString(""O""));
        DateTime? xdt = null; xdt += TS; P(""xdt += ts"", xdt?.ToString(""O""));
"), waitForOutput: "<<DONE>>");
    }

    [TestMethod]
    public async Task LiftedOperatorsWithValues()
    {
        await RunTest(Program(@"
        DateTime?       ndt  = DT;
        DateTime?       ndt2 = DT2;
        DateTimeOffset? ndo  = DTO;
        DateTimeOffset? ndo2 = DTO2;
        TimeSpan?       nts  = TS;
        TimeSpan?       nts2 = TS2;

        P(""ndt - ndt2"",  ndt - ndt2);
        P(""ndt + nts"",   (ndt + nts)?.ToString(""O""));
        P(""ndt - nts"",   (ndt - nts)?.ToString(""O""));
        P(""nts + nts2"",  nts + nts2);
        P(""nts - nts2"",  nts - nts2);
        P(""-nts"",        -nts);
        P(""ndo - ndo2"",  ndo - ndo2);
        P(""ndo + nts"",   (ndo + nts).HasValue ? U((ndo + nts).Value) : null);
        P(""ndo - nts"",   (ndo - nts).HasValue ? U((ndo - nts).Value) : null);
        P(""ndt > ndt2"",  ndt > ndt2);
        P(""ndt < ndt2"",  ndt < ndt2);
        P(""nts > nts2"",  nts > nts2);
        P(""ndo > ndo2"",  ndo > ndo2);
        P(""ndo < ndo2"",  ndo < ndo2);
        P(""ndt == ndt2"", ndt == ndt2);
        P(""ndt != ndt2"", ndt != ndt2);
        P(""nts == nts2"", nts == nts2);
        P(""ndo == ndo2"", ndo == ndo2);
        P(""ndo != ndo2"", ndo != ndo2);
        P(""ndt - ndo"",   ndt - ndo);
        P(""ndo - ndt"",   ndo - ndt);
        P(""ndt > ndo"",   ndt > ndo);
        P(""ndo > ndt"",   ndo > ndt);
"), waitForOutput: "<<DONE>>");
    }

    [TestMethod]
    public async Task LiftedOperatorsWithNulls()
    {
        await RunTest(Program(@"
        DateTime?       ndt  = DT;
        DateTime?       ndt2 = DT2;
        DateTimeOffset? ndo  = DTO;
        DateTimeOffset? ndo2 = DTO2;
        TimeSpan?       nts2 = TS2;
        DateTime?       xdt  = null;
        DateTimeOffset? xdo  = null;
        TimeSpan?       xts  = null;

        P(""xdt - ndt2"",  xdt - ndt2);
        P(""ndt - xdt"",   ndt - xdt);
        P(""xts + nts2"",  xts + nts2);
        P(""-xts"",        -xts);
        P(""xdo - ndo2"",  xdo - ndo2);
        P(""ndo - xdo"",   ndo - xdo);
        P(""xdt > ndt2"",  xdt > ndt2);
        P(""xdo > ndo2"",  xdo > ndo2);
        P(""xts > nts2"",  xts > nts2);
        P(""xdt == ndt2"", xdt == ndt2);
        P(""xdo == ndo2"", xdo == ndo2);
        P(""xdo != ndo2"", xdo != ndo2);
        P(""xdo == xdo"",  xdo == xdo);
        P(""xdt == null"", xdt == null);
        P(""xdo == null"", xdo == null);
        P(""xdo != null"", xdo != null);
        P(""xts == null"", xts == null);
        P(""xdt - ndo"",   xdt - ndo);
        P(""ndt - xdo"",   ndt - xdo);
"), waitForOutput: "<<DONE>>");
    }

    /// <summary>Both halves of <c>??</c> are conversion sites, so the DateTime half has to run the
    /// implicit operator — this is the exact shape the crash was reported against.</summary>
    [TestMethod]
    public async Task NullCoalescingAppliesTheImplicitConversion()
    {
        await RunTest(Program(@"
        DateTime?       xdt = null;
        DateTimeOffset? xdo = null;
        DateTimeOffset? ndo = DTO;
        TimeSpan?       xts = null;

        P(""(xdo ?? DT)"",       U(xdo ?? DT));
        P(""(ndo ?? DT)"",       U(ndo ?? DT));
        P(""DT - (xdo ?? DT2)"", DT - (xdo ?? DT2));
        P(""DT - (ndo ?? DT2)"", DT - (ndo ?? DT2));
        P(""(xdt ?? DT2)"",      (xdt ?? DT2).ToString(""O""));
        P(""(xts ?? TS2)"",      xts ?? TS2);
        P(""elapsed"",           (DateTime.UtcNow - (xdo ?? DateTime.UtcNow)).TotalSeconds < 5);
"), waitForOutput: "<<DONE>>");
    }

    // ---- emission -----------------------------------------------------------

    private static string Js(string source) => new RoslynTranslator().Translate(source).Javascript ?? "";

    [TestMethod]
    public void AnOperandOfAUserDefinedOperatorIsConverted()
    {
        var js = Js(@"
using System;
public class P
{
    public static TimeSpan M(DateTime dt, DateTimeOffset dto) { return dt - dto; }
}");

        StringAssert.Contains(js, "op_Implicit(",
            $"the DateTime operand must reach DateTimeOffset's operator through its implicit conversion:\n{js}");
    }

    [TestMethod]
    public void AStructOperandIsNotCloned()
    {
        // An operator takes its operands by value and cannot write back through them, so cloning
        // every operand of every == would only cost output size.
        var js = Js(@"
public struct S
{
    public int V;
    public static bool operator ==(S a, S b) { return a.V == b.V; }
    public static bool operator !=(S a, S b) { return a.V != b.V; }
    public override bool Equals(object o) { return o is S s && s.V == V; }
    public override int GetHashCode() { return V; }
}
public class P { public static bool M(S a, S b) { return a == b; } }");

        Assert.IsFalse(js.Contains("op_Equality(TransposeR.clone("),
            $"an operator operand is passed by value and must not be cloned:\n{js}");
    }

    [TestMethod]
    public void UnaryMinusOnATimeSpanUsesTheRuntimeHelper()
    {
        var js = Js("using System; public class P { public static TimeSpan M(TimeSpan t) { return -t; } }");

        StringAssert.Contains(js, "System.TimeSpan.neg(",
            $"unary minus must go through the operator's [Template], not the JS `-`:\n{js}");
    }

    [TestMethod]
    public void ALiftedSubtractionUsesTheUnderlyingOperator()
    {
        var js = Js("using System; public class P { public static TimeSpan? M(DateTime? a, DateTime? b) { return a - b; } }");

        StringAssert.Contains(js, "TransposeR.dtSub(",
            $"the non-null half of a lifted operator still runs the real operation:\n{js}");
    }

    [TestMethod]
    public void ACompoundAssignmentUsesTheUnderlyingOperator()
    {
        var js = Js("using System; public class P { public static DateTime M(DateTime d, TimeSpan t) { d += t; return d; } }");

        StringAssert.Contains(js, "TransposeR.dtAddTs(",
            $"`d += t` is `d = d + t`, not the JavaScript `+=`:\n{js}");
    }
}
