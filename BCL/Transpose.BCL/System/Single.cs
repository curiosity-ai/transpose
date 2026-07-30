namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    [Transpose.Constructor("Number")]
    public struct Single : IComparable, IComparable<float>, IEquatable<float>, IFormattable
    {
        private extern Single(int i);

        [Transpose.InlineConst]
        public const float MaxValue = (float)3.40282346638528859e+38;

        [Transpose.InlineConst]
        public const float MinValue = (float)-3.40282346638528859e+38;

        [Transpose.InlineConst]
        public const float Epsilon = (float)1.4e-45;

        [Transpose.Template("Number.NaN")]
        public const float NaN = 0f / 0f;

        [Transpose.Template("Number.NEGATIVE_INFINITY")]
        public const float NegativeInfinity = -1f / 0f;

        [Transpose.Template("Number.POSITIVE_INFINITY")]
        public const float PositiveInfinity = 1f / 0f;

        [Transpose.Template("System.Single.format({this}, {format})")]
        public extern string Format(string format);

        [Transpose.Template("System.Single.format({this}, {format}, {provider})")]
        public extern string Format(string format, IFormatProvider provider);

        public extern string ToString(int radix);

        [Transpose.Template("System.Single.format({this}, {format})", Fn = "System.Single.format")]
        public extern string ToString(string format);

        [Transpose.Template("System.Single.format({this}, {format}, {provider})", Fn = "System.Single.format")]
        public extern string ToString(string format, IFormatProvider provider);

        [Transpose.Template(Fn = "System.Single.format")]
        public override extern string ToString();

        [Transpose.Template("System.Single.format({this}, \"G\", {provider})", Fn = "function ($v, $p) { return System.Single.format($v, \"G\", $p); }")]
        public extern string ToString(IFormatProvider provider);

        [Transpose.Template("System.Single.parse({s})")]
        public static extern float Parse(string s);

        [Transpose.Template("System.Single.parse({s}, {provider})")]
        public static extern float Parse(string s, IFormatProvider provider);

        [Transpose.Template("System.Single.tryParse({s}, null, {result})")]
        public static extern bool TryParse(string s, out float result);

        [Transpose.Template("System.Single.tryParse({s}, {provider}, {result})")]
        public static extern bool TryParse(string s, IFormatProvider provider, out float result);

        public extern string ToExponential();

        public extern string ToExponential(int fractionDigits);

        public extern string ToFixed();

        public extern string ToFixed(int fractionDigits);

        public extern string ToPrecision();

        public extern string ToPrecision(int precision);

        [Transpose.Template("({d} === Number.POSITIVE_INFINITY)")]
        public static extern bool IsPositiveInfinity(float d);

        [Transpose.Template("({d} === Number.NEGATIVE_INFINITY)")]
        public static extern bool IsNegativeInfinity(float d);

        [Transpose.Template("(Math.abs({d}) === Number.POSITIVE_INFINITY)")]
        public static extern bool IsInfinity(float d);

        [Transpose.Template("isFinite({d})")]
        public static extern bool IsFinite(float d);

        [Transpose.Template("isNaN({d})")]
        public static extern bool IsNaN(float d);

        [Transpose.Template("Transpose.compare({this}, {other})")]
        public extern int CompareTo(float other);

        [Transpose.Template("Transpose.compare({this}, {obj})")]
        public extern int CompareTo(object obj);

        [Transpose.Template("{this} === {other}")]
        public extern bool Equals(float other);

        [Transpose.Template("System.Single.equals({this}, {other})")]
        public override extern bool Equals(object other);

        [Transpose.Template(Fn = "System.Single.getHashCode")]
        public override extern int GetHashCode();
    }
}