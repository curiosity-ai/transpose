namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Constructor("Number")]
    [Transpose.Reflectable]
    public struct Double : IComparable, IComparable<double>, IEquatable<double>, IFormattable
    {
        private extern Double(int i);

        [Transpose.Template("System.Double.max")]
        public const double MaxValue = 1.7976931348623157E+308;

        [Transpose.Template("System.Double.min")]
        public const double MinValue = -1.7976931348623157E+308;

        [Transpose.InlineConst]
        public const double Epsilon = 4.94065645841247E-324;

        [Transpose.Template("Number.NEGATIVE_INFINITY")]
        public const double NegativeInfinity = -1D / 0D;

        [Transpose.Template("Number.POSITIVE_INFINITY")]
        public const double PositiveInfinity = 1D / 0D;

        [Transpose.Template("Number.NaN")]
        public const double NaN = 0D / 0D;

        [Transpose.Template("System.Double.format({this}, {format})")]
        public extern string Format(string format);

        [Transpose.Template("System.Double.format({this}, {format}, {provider})")]
        public extern string Format(string format, IFormatProvider provider);

        public extern string ToString(int radix);

        [Transpose.Template("System.Double.format({this}, {format})")]
        public extern string ToString(string format);

        [Transpose.Template("System.Double.format({this}, {format}, {provider})")]
        public extern string ToString(string format, IFormatProvider provider);

        [Transpose.Template(Fn = "System.Double.format")]
        public override extern string ToString();

        [Transpose.Template("System.Double.format({this}, \"G\", {provider})")]
        public extern string ToString(IFormatProvider provider);

        [Transpose.Template("System.Double.parse({s})")]
        public static extern double Parse(string s);

        [Transpose.Template("Transpose.Int.parseFloat({s}, {provider})")]
        public static extern double Parse(string s, IFormatProvider provider);

        [Transpose.Template("System.Double.tryParse({s}, null, {result})")]
        public static extern bool TryParse(string s, out double result);

        [Transpose.Template("System.Double.tryParse({s}, {provider}, {result})")]
        public static extern bool TryParse(string s, IFormatProvider provider, out double result);

        public extern string ToExponential();

        public extern string ToExponential(int fractionDigits);

        public extern string ToFixed();

        public extern string ToFixed(int fractionDigits);

        public extern string ToPrecision();

        public extern string ToPrecision(int precision);

        [Transpose.Template("({d} === Number.POSITIVE_INFINITY)")]
        public static extern bool IsPositiveInfinity(double d);

        [Transpose.Template("({d} === Number.NEGATIVE_INFINITY)")]
        public static extern bool IsNegativeInfinity(double d);

        [Transpose.Template("(Math.abs({d}) === Number.POSITIVE_INFINITY)")]
        public static extern bool IsInfinity(double d);

        [Transpose.Template("isFinite({d})")]
        public static extern bool IsFinite(double d);

        [Transpose.Template("isNaN({d})")]
        public static extern bool IsNaN(double d);

        [Transpose.Template("Transpose.compare({this}, {other})")]
        public extern int CompareTo(double other);

        [Transpose.Template("Transpose.compare({this}, {obj})")]
        public extern int CompareTo(object obj);

        [Transpose.Template("{this} === {other}")]
        public extern bool Equals(double other);

        [Transpose.Template("System.Double.equals({this}, {other})")]
        public override extern bool Equals(object other);

        [Transpose.Template(Fn = "System.Double.getHashCode")]
        public override extern int GetHashCode();
    }
}