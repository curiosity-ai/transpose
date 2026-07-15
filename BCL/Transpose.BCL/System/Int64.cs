namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    public struct Int64 : IComparable, IComparable<long>, IEquatable<long>, IFormattable
    {
        private extern Int64(int i);

        [Transpose.Convention]
        public const long MinValue = -9223372036854775808;

        [Transpose.Convention]
        public const long MaxValue = 9223372036854775807;

        [Transpose.Template("System.Int64.parse({s})")]
        public static extern long Parse(string s);

        [Transpose.Template("System.Int64.tryParse({s}, {result})")]
        public static extern bool TryParse(string s, out long result);

        public extern string ToString(int radix);

        public extern string Format(string format);

        public extern string Format(string format, IFormatProvider provider);

        public extern string ToString(string format);

        public extern string ToString(string format, IFormatProvider provider);

        public extern int CompareTo(long other);

        public extern int CompareTo(object obj);

        public extern bool Equals(long other);

        //[Transpose.Template("System.Int64.lift({value})")]
        public static extern implicit operator long (byte value);

        //[Transpose.Template("System.Int64.lift({value})")]
        
        public static extern implicit operator long (sbyte value);

        //[Transpose.Template("System.Int64.lift({value})")]
        public static extern implicit operator long (short value);

        //[Transpose.Template("System.Int64.lift({value})")]
        
        public static extern implicit operator long (ushort value);

        //[Transpose.Template("System.Int64.lift({value})")]
        public static extern implicit operator long (char value);

        //[Transpose.Template("System.Int64.lift({value})")]
        public static extern implicit operator long (int value);

        //[Transpose.Template("System.Int64.lift({value})")]
        
        public static extern implicit operator long (uint value);

        //[Transpose.Template("System.Int64.lift(Transpose.Int.clip64({value}))")]
        public static extern explicit operator long (float value);

        //[Transpose.Template("System.Int64.lift(Transpose.Int.clip64({value}))")]
        public static extern explicit operator long (double value);

        //[Transpose.Template("System.Int64.lift({value})")]
        
        public static extern explicit operator long (ulong value);

        //[Transpose.Template("System.Int64.clip8({value})")]
        public static extern explicit operator byte (long value);

        //[Transpose.Template("System.Int64.clipu8({value})")]
        
        public static extern explicit operator sbyte (long value);

        //[Transpose.Template("System.Int64.clipu16({value})")]
        public static extern explicit operator char (long value);

        //[Transpose.Template("System.Int64.clip16({value})")]
        public static extern explicit operator short (long value);

        //[Transpose.Template("System.Int64.clipu16({value})")]
        
        public static extern explicit operator ushort (long value);

        //[Transpose.Template("System.Int64.clip32({value})")]
        public static extern explicit operator int (long value);

        //[Transpose.Template("System.Int64.clipu32({value})")]
        
        public static extern explicit operator uint (long value);

        //[Transpose.Template("System.UInt64.lift({value})")]
        
        public static extern explicit operator ulong (long value);

        //[Transpose.Template("System.Int64.toNumber({value})")]
        public static extern explicit operator float (long value);

        //[Transpose.Template("System.Int64.toNumber({value})")]
        public static extern explicit operator double (long value);
    }
}