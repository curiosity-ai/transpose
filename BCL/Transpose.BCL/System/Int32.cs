namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Constructor("Number")]
    [Transpose.Reflectable]
#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
    public struct Int32 : IComparable, IComparable<int>, IEquatable<int>, IFormattable
    {
        private extern Int32(int i);

        [Transpose.InlineConst]
        public const int MinValue = -2147483648;

        [Transpose.InlineConst]
        public const int MaxValue = 2147483647;

        [Transpose.Template("System.Int32.parse({s})")]
        public static extern int Parse(string s);

        [Transpose.Template("System.Int32.parse({s}, {radix})")]
        public static extern int Parse(string s, int radix);

        [Transpose.Template("System.Int32.tryParse({s}, {result})")]
        public static extern bool TryParse(string s, out int result);

        [Transpose.Template("System.Int32.tryParse({s}, {result}, {radix})")]
        public static extern bool TryParse(string s, out int result, int radix);

        public extern string ToString(int radix);

        [Transpose.Template("System.Int32.format({this}, {format})")]
        public extern string Format(string format);

        [Transpose.Template("System.Int32.format({this}, {format}, {provider})")]
        public extern string Format(string format, IFormatProvider provider);

        [Transpose.Template("System.Int32.format({this}, {format})")]
        public extern string ToString(string format);

        [Transpose.Template("System.Int32.format({this}, {format}, {provider})")]
        public extern string ToString(string format, IFormatProvider provider);

        [Transpose.Template("Transpose.compare({this}, {other})")]
        public extern int CompareTo(int other);

        [Transpose.Template("Transpose.compare({this}, {obj})")]
        public extern int CompareTo(object obj);

        [Transpose.Template("{this} === {other}")]
        public extern bool Equals(int other);

        [Transpose.Template("System.Int32.equals({this}, {other})")]
        public override extern bool Equals(object other);
    }
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
}