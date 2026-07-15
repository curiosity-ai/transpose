namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    [Transpose.Constructor("Number")]
#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
    public struct UInt32 : IComparable, IComparable<uint>, IEquatable<uint>, IFormattable
    {
        private extern UInt32(int i);

        [Transpose.InlineConst]
        
        public const uint MinValue = 0;

        [Transpose.InlineConst]
        
        public const uint MaxValue = 4294967295;

        [Transpose.Template("System.UInt32.parse({s})")]
        
        public static extern uint Parse(string s);

        [Transpose.Template("System.UInt32.parse({s}, {radix})")]
        
        public static extern uint Parse(string s, int radix);

        [Transpose.Template("System.UInt32.tryParse({s}, {result})")]
        
        public static extern bool TryParse(string s, out uint result);

        [Transpose.Template("System.UInt32.tryParse({s}, {result}, {radix})")]
        
        public static extern bool TryParse(string s, out uint result, int radix);

        public extern string ToString(int radix);

        [Transpose.Template("System.UInt32.format({this}, {format})")]
        public extern string Format(string format);

        [Transpose.Template("System.UInt32.format({this}, {format}, {provider})")]
        public extern string Format(string format, IFormatProvider provider);

        [Transpose.Template("System.UInt32.format({this}, {format})")]
        public extern string ToString(string format);

        [Transpose.Template("System.UInt32.format({this}, {format}, {provider})")]
        public extern string ToString(string format, IFormatProvider provider);

        [Transpose.Template("Transpose.compare({this}, {other})")]
        
        public extern int CompareTo(uint other);

        [Transpose.Template("Transpose.compare({this}, {obj})")]
        public extern int CompareTo(object obj);

        [Transpose.Template("{this} === {other}")]
        
        public extern bool Equals(uint other);

        [Transpose.Template("System.UInt32.equals({this}, {other})")]
        public override extern bool Equals(object other);
    }
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
}