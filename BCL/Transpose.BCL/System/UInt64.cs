namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    public struct UInt64 : IComparable, IComparable<ulong>, IEquatable<ulong>, IFormattable
    {
        private extern UInt64(int i);

        
        public const ulong MinValue = 0;

        
        public const ulong MaxValue = 18446744073709551615;

        [Transpose.Template("System.UInt64.parse({s})")]
        
        public static extern ulong Parse(string s);

        [Transpose.Template("System.UInt64.tryParse({s}, {result})")]
        
        public static extern bool TryParse(string s, out ulong result);

        public extern string ToString(int radix);

        public extern string Format(string format);

        public extern string Format(string format, IFormatProvider provider);

        public extern string ToString(string format);

        public extern string ToString(string format, IFormatProvider provider);

        
        public extern int CompareTo(ulong other);

        public extern int CompareTo(object obj);

        
        public extern bool Equals(ulong other);

        
        public static extern implicit operator ulong (byte value);

        
        public static extern implicit operator ulong (sbyte value);

        
        public static extern implicit operator ulong (short value);

        
        public static extern implicit operator ulong (ushort value);

        
        public static extern implicit operator ulong (char value);

        
        public static extern implicit operator ulong (int value);

        
        public static extern implicit operator ulong (uint value);

        
        public static extern explicit operator ulong (float value);

        
        public static extern explicit operator ulong (double value);

        
        public static extern explicit operator byte (ulong value);

        
        public static extern explicit operator sbyte (ulong value);

        
        public static extern explicit operator char (ulong value);

        
        public static extern explicit operator short (ulong value);

        
        public static extern explicit operator ushort (ulong value);

        
        public static extern explicit operator int (ulong value);

        
        public static extern explicit operator uint (ulong value);

        
        public static extern explicit operator float (ulong value);

        
        public static extern explicit operator double (ulong value);
    }
}