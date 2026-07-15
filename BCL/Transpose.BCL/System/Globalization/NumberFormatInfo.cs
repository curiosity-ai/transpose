namespace System.Globalization
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    public sealed class NumberFormatInfo : IFormatProvider, ICloneable, Transpose.ITransposeClass
    {
        public extern NumberFormatInfo();

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public static extern NumberFormatInfo InvariantInfo
        {
            get;
        }

        [Transpose.Name("nanSymbol")]
        public extern string NaNSymbol
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern string NegativeSign
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern string PositiveSign
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern string NegativeInfinitySymbol
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern string PositiveInfinitySymbol
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern string PercentSymbol
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern int[] PercentGroupSizes
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern int PercentDecimalDigits
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern string PercentDecimalSeparator
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern string PercentGroupSeparator
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern int PercentPositivePattern
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern int PercentNegativePattern
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern string CurrencySymbol
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern int[] CurrencyGroupSizes
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern int CurrencyDecimalDigits
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern string CurrencyDecimalSeparator
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern string CurrencyGroupSeparator
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern int CurrencyPositivePattern
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern int CurrencyNegativePattern
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern int[] NumberGroupSizes
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern int NumberDecimalDigits
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern string NumberDecimalSeparator
        {
            get;
            set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern string NumberGroupSeparator
        {
            get;
            set;
        }

        public extern object GetFormat(Type formatType);

        public extern object Clone();

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public static extern NumberFormatInfo CurrentInfo
        {
            get;
        }
    }
}