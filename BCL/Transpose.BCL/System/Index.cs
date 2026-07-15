using System.Runtime.CompilerServices;

namespace System
{
    public readonly struct Index : IEquatable<Index>
    {
        private readonly int _value;

        public Index(int value, bool fromEnd = false)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "value must be non-negative");
            }

            if (fromEnd)
                _value = ~value;
            else
                _value = value;
        }

        // Stores the raw encoded value directly (a from-end index is stored as its bitwise
        // complement, which is negative), without the non-negative validation the public ctor
        // applies. FromStart/FromEnd/Start/End construct through this — matching .NET's private
        // Index(int) ctor, which they call with an already-encoded value.
        private Index(int value)
        {
            _value = value;
        }

        public static Index Start => new Index(0);
        public static Index End => new Index(~0);

        public static Index FromStart(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "value must be non-negative");
            }

            return new Index(value);
        }

        public static Index FromEnd(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "value must be non-negative");
            }

            return new Index(~value);
        }

        public int Value
        {
            get
            {
                if (_value < 0)
                    return ~_value;
                else
                    return _value;
            }
        }

        public bool IsFromEnd => _value < 0;

        public int GetOffset(int length)
        {
            int offset = _value;
            if (IsFromEnd)
            {
                offset = length - (~offset);
            }
            return offset;
        }

        public override bool Equals(object value) => value is Index && _value == ((Index)value)._value;

        public bool Equals(Index other) => _value == other._value;

        public override int GetHashCode() => _value;

        public static implicit operator Index(int value) => FromStart(value);

        public override string ToString()
        {
            if (IsFromEnd)
                return "^" + Value.ToString();

            return Value.ToString();
        }
    }
}
