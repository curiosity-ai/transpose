namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    [Transpose.Constructor("Number")]
#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
    public struct UInt16 : IComparable, IComparable<ushort>, IEquatable<ushort>, IFormattable
    {
        private extern UInt16(int i);

        [Transpose.InlineConst]
        
        public const ushort MinValue = 0;

        [Transpose.InlineConst]
        
        public const ushort MaxValue = 65535;

        [Transpose.Template("System.UInt16.parse({s})")]
        
        public static extern ushort Parse(string s);

        [Transpose.Template("System.UInt16.parse({s}, {radix})")]
        
        public static extern ushort Parse(string s, int radix);

        [Transpose.Template("System.UInt16.tryParse({s}, {result})")]
        
        public static extern bool TryParse(string s, out ushort result);

        [Transpose.Template("System.UInt16.tryParse({s}, {result}, {radix})")]
        
        public static extern bool TryParse(string s, out ushort result, int radix);

        public extern string ToString(int radix);

        [Transpose.Template("System.UInt16.format({this}, {format})")]
        public extern string Format(string format);

        [Transpose.Template("System.UInt16.format({this}, {format}, {provider})")]
        public extern string Format(string format, IFormatProvider provider);

        [Transpose.Template("System.UInt16.format({this}, {format})")]
        public extern string ToString(string format);

        [Transpose.Template("System.UInt16.format({this}, {format}, {provider})")]
        public extern string ToString(string format, IFormatProvider provider);

        [Transpose.Template("Transpose.compare({this}, {other})")]
        
        public extern int CompareTo(ushort other);

        [Transpose.Template("Transpose.compare({this}, {obj})")]
        public extern int CompareTo(object obj);

        [Transpose.Template("{this} === {other}")]
        
        public extern bool Equals(ushort other);

        [Transpose.Template("System.UInt16.equals({this}, {other})")]
        public override extern bool Equals(object other);
    }
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
}