namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Constructor("Number")]
    [Transpose.Reflectable]
#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
    public struct Int16 : IComparable, IComparable<short>, IEquatable<short>, IFormattable
    {
        private extern Int16(int i);

        [Transpose.InlineConst]
        public const short MinValue = -32768;

        [Transpose.InlineConst]
        public const short MaxValue = 32767;

        [Transpose.Template("System.Int16.parse({s})")]
        public static extern short Parse(string s);

        [Transpose.Template("System.Int16.parse({s}, {radix})")]
        public static extern short Parse(string s, int radix);

        [Transpose.Template("System.Int16.tryParse({s}, {result})")]
        public static extern bool TryParse(string s, out short result);

        [Transpose.Template("System.Int16.tryParse({s}, {result}, {radix})")]
        public static extern bool TryParse(string s, out short result, int radix);

        public extern string ToString(int radix);

        [Transpose.Template("System.Int16.format({this}, {format})")]
        public extern string Format(string format);

        [Transpose.Template("System.Int16.format({this}, {format}, {provider})")]
        public extern string Format(string format, IFormatProvider provider);

        [Transpose.Template("System.Int16.format({this}, {format})")]
        public extern string ToString(string format);

        [Transpose.Template("System.Int16.format({this}, {format}, {provider})")]
        public extern string ToString(string format, IFormatProvider provider);

        [Transpose.Template("Transpose.compare({this}, {other})")]
        public extern int CompareTo(short other);

        [Transpose.Template("Transpose.compare({this}, {obj})")]
        public extern int CompareTo(object obj);

        [Transpose.Template("{this} === {other}")]
        public extern bool Equals(short other);

        [Transpose.Template("System.Int16.equals({this}, {other})")]
        public override extern bool Equals(object other);
    }
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
}