namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    [Transpose.Constructor("Number")]
#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
    public struct SByte : IComparable, IComparable<sbyte>, IEquatable<sbyte>, IFormattable
    {
        private extern SByte(int i);

        [Transpose.InlineConst]
        
        public const sbyte MinValue = -128;

        [Transpose.InlineConst]
        
        public const sbyte MaxValue = 127;

        [Transpose.Template("System.SByte.parse({s})")]
        
        public static extern sbyte Parse(string s);

        [Transpose.Template("System.SByte.parse({s}, {radix})")]
        
        public static extern sbyte Parse(string s, int radix);

        [Transpose.Template("System.SByte.tryParse({s}, {result})")]
        
        public static extern bool TryParse(string s, out sbyte result);

        [Transpose.Template("System.SByte.tryParse({s}, {result}, {radix})")]
        
        public static extern bool TryParse(string s, out sbyte result, int radix);

        public extern string ToString(int radix);

        [Transpose.Template("System.SByte.format({this}, {format})")]
        public extern string Format(string format);

        [Transpose.Template("System.SByte.format({this}, {format}, {provider})")]
        public extern string Format(string format, IFormatProvider provider);

        [Transpose.Template("System.SByte.format({this}, {format})")]
        public extern string ToString(string format);

        [Transpose.Template("System.SByte.format({this}, {format}, {provider})")]
        public extern string ToString(string format, IFormatProvider provider);

        [Transpose.Template("Transpose.compare({this}, {other})")]
        
        public extern int CompareTo(sbyte other);

        [Transpose.Template("Transpose.compare({this}, {obj})")]
        public extern int CompareTo(object obj);

        [Transpose.Template("{this} === {other}")]
        
        public extern bool Equals(sbyte other);

        [Transpose.Template("System.SByte.equals({this}, {other})")]
        public override extern bool Equals(object other);
    }
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
}