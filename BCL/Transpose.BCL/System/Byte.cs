namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Constructor("Number")]
    [Transpose.Reflectable]
#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
    public struct Byte : IComparable, IComparable<byte>, IEquatable<byte>, IFormattable
    {
        private extern Byte(int i);

        [Transpose.InlineConst]
        public const byte MinValue = 0;

        [Transpose.InlineConst]
        public const byte MaxValue = 255;

        [Transpose.Template("System.Byte.parse({s})")]
        public static extern byte Parse(string s);

        [Transpose.Template("System.Byte.parse({s}, {radix})")]
        public static extern byte Parse(string s, int radix);

        [Transpose.Template("System.Byte.tryParse({s}, {result})")]
        public static extern bool TryParse(string s, out byte result);

        [Transpose.Template("System.Byte.tryParse({s}, {result}, {radix})")]
        public static extern bool TryParse(string s, out byte result, int radix);

        public extern string ToString(int radix);

        [Transpose.Template("System.Byte.format({this}, {format})")]
        public extern string Format(string format);

        [Transpose.Template("System.Byte.format({this}, {format}, {provider})")]
        public extern string Format(string format, IFormatProvider provider);

        [Transpose.Template("System.Byte.format({this}, {format})")]
        public extern string ToString(string format);

        [Transpose.Template("System.Byte.format({this}, {format}, {provider})")]
        public extern string ToString(string format, IFormatProvider provider);

        [Transpose.Template("Transpose.compare({this}, {other})")]
        public extern int CompareTo(byte other);

        [Transpose.Template("Transpose.compare({this}, {obj})")]
        public extern int CompareTo(object obj);

        [Transpose.Template("{this} === {other}")]
        public extern bool Equals(byte other);

        [Transpose.Template("System.Byte.equals({this}, {other})")]
        public override extern bool Equals(object other);
    }
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
}