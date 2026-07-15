using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace System
{
    /// <summary>
    /// The decimal data type.
    /// http://mikemcl.github.io/decimal.js/
    /// </summary>
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Constructor("System.Decimal")]
    [Transpose.Reflectable]
    public struct Decimal : IComparable, IComparable<decimal>, IEquatable<decimal>, IFormattable
    {
        [Transpose.Convention]
        public const decimal Zero = 0;

        [Transpose.Convention]
        public const decimal One = 1;

        [Transpose.Convention]
        public const decimal MinusOne = -1;

        [Transpose.Convention]
        public const decimal MaxValue = 79228162514264337593543950335m;

        [Transpose.Convention]
        public const decimal MinValue = -79228162514264337593543950335m;

        [Transpose.Template("System.Decimal(0)")]
        private extern Decimal(DummyTypeUsedToAddAttributeToDefaultValueTypeConstructor _);

        [Transpose.Template("System.Decimal({d})")]
        public extern Decimal(double d);

        [Transpose.Template("System.Decimal({i})")]
        public extern Decimal(int i);

        [Transpose.Template("System.Decimal({i})")]
        
        public extern Decimal(uint i);

        [Transpose.Template("System.Decimal({f})")]
        public extern Decimal(float f);

        [Transpose.Template("System.Decimal({n})")]
        public extern Decimal(long n);

        [Transpose.Template("System.Decimal({n})")]
        
        public extern Decimal(ulong n);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public extern Decimal(int lo, int mid, int hi, bool isNegative, byte scale);

        [Transpose.Template("Transpose.Int.format({this}, {format})")]
        public extern string Format(string format);

        [Transpose.Template("Transpose.Int.format({this}, {format}, {provider})")]
        public extern string Format(string format, IFormatProvider provider);

        [Transpose.Template("Transpose.Int.format({this}, {format})")]
        public extern string ToString(string format);

        [Transpose.Template("Transpose.Int.format({this}, {format}, {provider})")]
        public extern string ToString(string format, IFormatProvider provider);

        [Transpose.Template("Transpose.Int.format({this}, \"G\", {provider})")]
        public extern string ToString(IFormatProvider provider);

        public override extern string ToString();

        public extern decimal Abs();

        [Transpose.Name("ceil")]
        public extern decimal Ceiling();

        public extern int ComparedTo(decimal d);

        //[Transpose.Template("System.Decimal.lift({value})")]
        public static extern implicit operator decimal (byte value);

        //[Transpose.Template("System.Decimal.lift({value})")]
        
        public static extern implicit operator decimal (sbyte value);

        //[Transpose.Template("System.Decimal.lift({value})")]
        public static extern implicit operator decimal (short value);

        //[Transpose.Template("System.Decimal.lift({value})")]
        
        public static extern implicit operator decimal (ushort value);

        //[Transpose.Template("System.Decimal.lift({value})")]
        public static extern implicit operator decimal (char value);

        //[Transpose.Template("System.Decimal.lift({value})")]
        public static extern implicit operator decimal (int value);

        //[Transpose.Template("System.Decimal.lift({value})")]
        
        public static extern implicit operator decimal (uint value);

        //[Transpose.Template("System.Decimal.lift({value})")]
        public static extern implicit operator decimal (long value);

        //[Transpose.Template("System.Decimal.lift({value})")]
        
        public static extern implicit operator decimal (ulong value);

        //[Transpose.Template("System.Decimal.lift({value})")]
        public static extern explicit operator decimal (float value);

        //[Transpose.Template("System.Decimal.lift({value})")]
        public static extern explicit operator decimal (double value);

        //[Transpose.Template("System.Decimal.toInt({value}, System.Byte)")]
        public static extern explicit operator byte (decimal value);

        //[Transpose.Template("System.Decimal.toInt({value}, System.SByte)")]
        
        public static extern explicit operator sbyte (decimal value);

        //[Transpose.Template("System.Decimal.toInt({value}, Transpose.Char)")]
        public static extern explicit operator char (decimal value);

        //[Transpose.Template("System.Decimal.toInt({value}, System.Int16)")]
        public static extern explicit operator short (decimal value);

        //[Transpose.Template("System.Decimal.toInt({value}, System.UInt16)")]
        
        public static extern explicit operator ushort (decimal value);

        //[Transpose.Template("System.Decimal.toInt({value}, System.Int32)")]
        public static extern explicit operator int (decimal value);

        //[Transpose.Template("System.Decimal.toInt({value}, System.UInt32)")]
        
        public static extern explicit operator uint (decimal value);

        //[Transpose.Template("System.Decimal.toInt({value}, System.Int64)")]
        public static extern explicit operator long (decimal value);

        //[Transpose.Template("System.Decimal.toInt({value}, System.UInt64)")]
        
        public static extern explicit operator ulong (decimal value);

        //[Transpose.Template("System.Decimal.toFloat({value})")]
        public static extern explicit operator float (decimal value);

        //[Transpose.Template("System.Decimal.toFloat({value})")]
        public static extern explicit operator double (decimal value);

        [Transpose.Template("{d}.clone()")]
        public static extern decimal operator +(decimal d);

        [Transpose.Template("{d}.neg()")]
        public static extern decimal operator -(decimal d);

        [Transpose.Template("{d1}.add({d2})")]
        public static extern decimal operator +(decimal d1, decimal d2);

        [Transpose.Template("{d1}.sub({d2})")]
        public static extern decimal operator -(decimal d1, decimal d2);

        [Transpose.Template("{d}.inc()")]
        public static extern decimal operator ++(decimal d);

        [Transpose.Template("{d}.dec()")]
        public static extern decimal operator --(decimal d);

        [Transpose.Template("{d1}.mul({d2})")]
        public static extern decimal operator *(decimal d1, decimal d2);

        [Transpose.Template("{d1}.div({d2})")]
        public static extern decimal operator /(decimal d1, decimal d2);

        [Transpose.Template("{d1}.mod({d2})")]
        public static extern decimal operator %(decimal d1, decimal d2);

        [Transpose.Template("{d1}.equalsT({d2})")]
        public static extern bool operator ==(decimal d1, decimal d2);

        [Transpose.Template("{d1}.ne({d2})")]
        public static extern bool operator !=(decimal d1, decimal d2);

        [Transpose.Template("{d1}.gt({d2})")]
        public static extern bool operator >(decimal d1, decimal d2);

        [Transpose.Template("{d1}.gte({d2})")]
        public static extern bool operator >=(decimal d1, decimal d2);

        [Transpose.Template("{d1}.lt({d2})")]
        public static extern bool operator <(decimal d1, decimal d2);

        [Transpose.Template("{d1}.lte({d2})")]
        public static extern bool operator <=(decimal d1, decimal d2);

        [Transpose.Template("{d1}.add({d2})")]
        public static extern decimal Add(decimal d1, decimal d2);

        [Transpose.Template("System.Decimal.exp({d})")]
        public static extern decimal Exp(decimal d);

        [Transpose.Template("System.Decimal.ln({d})")]
        public static extern decimal Ln(decimal d);

        [Transpose.Template("System.Decimal.log({d}, {logBase})")]
        public static extern decimal Log(decimal d, decimal logBase);

        [Transpose.Template("System.Decimal.pow({d}, {exponent})")]
        public static extern decimal Pow(decimal d, decimal exponent);

        [Transpose.Template("System.Decimal.sqrt({d})")]
        public static extern decimal Sqrt(decimal d);

        [Transpose.Template("{d}.ceil()")]
        public static extern decimal Ceiling(decimal d);

        [Transpose.Template("{d1}.div({d2})")]
        public static extern decimal Divide(decimal d1, decimal d2);

        [Transpose.Template("{d}.floor()")]
        public static extern decimal Floor(decimal d);

        [Transpose.Template("{d1}.mod({d2})")]
        public static extern decimal Remainder(decimal d1, decimal d2);

        [Transpose.Template("{d1}.mul({d2})")]
        public static extern decimal Multiply(decimal d1, decimal d2);

        [Transpose.Template("System.Decimal(0).sub({d})")]
        public static extern decimal Negate(decimal d);

        [Transpose.Template("System.Decimal({s})")]
        public static extern decimal Parse(string s);

        [Transpose.Template("System.Decimal({s}, {provider})")]
        public static extern decimal Parse(string s, IFormatProvider provider);

        [Transpose.Template("System.Decimal.tryParse({s}, null, {result})")]
        public static extern bool TryParse(string s, out decimal result);

        [Transpose.Template("System.Decimal.tryParse({s}, {provider}, {result})")]
        public static extern bool TryParse(string s, IFormatProvider provider, out decimal result);

        [Transpose.Template("System.Decimal.round({d}, 6)")]
        public static extern decimal Round(decimal d);

        [Transpose.Template("System.Decimal.toDecimalPlaces({d}, {decimals}, 6)")]
        public static extern decimal Round(decimal d, int decimals);

        [Transpose.Template("System.Decimal.toDecimalPlaces({d}, {decimals}, {mode})")]
        public static extern decimal Round(decimal d, int decimals, MidpointRounding mode);

        [Transpose.Template("System.Decimal.round({d}, {mode})")]
        public static extern decimal Round(decimal d, MidpointRounding mode);

        [Transpose.Template("{d}.trunc()")]
        public static extern decimal Truncate(decimal d);

        [Transpose.Template("{d1}.sub({d2})")]
        public static extern decimal Subtract(decimal d1, decimal d2);

        [Transpose.Template("{this}.compareTo({other})")]
        public extern int CompareTo(decimal other);

        [Transpose.Template("{d1}.compareTo({d2})")]
        public static extern int Compare(decimal d1, decimal d2);

        [Transpose.Template("{d1}.equals({d2})")]
        public static extern bool Equals(decimal d1, decimal d2);

        [Transpose.Template("System.Decimal.toInt({value})")]
        public static extern byte ToByte(decimal value);

        [Transpose.Template("System.Decimal.toInt({value})")]
        
        public static extern sbyte ToSByte(decimal value);

        [Transpose.Template("System.Decimal.toInt({value})")]
        public static extern char ToChar(decimal value);

        [Transpose.Template("System.Decimal.toInt({value})")]
        public static extern short ToInt16(decimal value);

        [Transpose.Template("System.Decimal.toInt({value})")]
        
        public static extern ushort ToUInt16(decimal value);

        [Transpose.Template("System.Decimal.toInt({value})")]
        public static extern int ToInt32(decimal value);

        [Transpose.Template("System.Decimal.toInt({value})")]
        
        public static extern uint ToUInt32(decimal value);

        [Transpose.Template("System.Decimal.toInt({value}, System.Int64)")]
        public static extern long ToInt64(decimal value);

        [Transpose.Template("System.Decimal.toInt({value}, System.UInt64)")]
        
        public static extern ulong ToUInt64(decimal value);

        [Transpose.Template("System.Decimal.toFloat({value})")]
        public static extern float ToSingle(decimal value);

        [Transpose.Template("System.Decimal.toFloat({value})")]
        public static extern double ToDouble(decimal value);

        [Transpose.Template("{this}.compareTo({obj})")]
        public extern int CompareTo(object obj);

        public extern int DecimalPlaces();

        public extern decimal DividedToIntegerBy(decimal d);

        public extern decimal Exponential();

        public extern decimal Floor();

        public extern bool IsFinite();

        public extern bool IsInteger();

        public extern bool IsNaN();

        public extern bool IsNegative();

        public extern bool IsZero();

        public extern decimal Log(decimal logBase);

        public extern decimal Ln();

        public extern int Precision();

        public extern decimal Round();

        public extern decimal Sqrt();

        public extern decimal ToDecimalPlaces(int dp, MidpointRounding rm);

        public extern string ToExponential(int dp, MidpointRounding rm);

        public extern string ToFixed(int dp, MidpointRounding rm);

        public extern decimal Pow(double n);

        public extern string ToPrecision(int sd, MidpointRounding rm);

        public extern decimal ToSignificantDigits(int sd, MidpointRounding rm);

        public static extern decimal Max(params decimal[] values);

        public static extern decimal Min(params decimal[] values);

        /// <summary>
        /// Returns a new Decimal with a pseudo-random value equal to or greater than 0 and less than 1.
        /// </summary>
        /// <param name="dp">The return value will have dp decimal places (or less if trailing zeros are produced). If dp is omitted then the number of decimal places will default to the current precision setting.</param>
        /// <returns></returns>
        public static extern decimal Random(int dp);

        /// <summary>
        /// Configures the 'global' settings for this particular Decimal constructor.
        /// </summary>
        /// <param name="config"></param>
        public static extern void SetConfig(DecimalConfig config);

        public extern string ToFormat();

        public extern string ToFormat(int dp);

        public extern string ToFormat(int dp, MidpointRounding rm);

        public extern string ToFormat(int dp, MidpointRounding rm, IFormatProvider provider);

        public extern string ToFormat(int dp, MidpointRounding rm, DecimalFormatConfig config);

        [Transpose.Template("toFormat(null, null,{config})")]
        public extern string ToFormat(DecimalFormatConfig config);

#pragma warning disable 659
        public override extern bool Equals(object o);
#pragma warning restore 659

        [Transpose.Name("equalsT")]
        public extern bool Equals(decimal other);

        public override extern int GetHashCode();

        [Transpose.Template("{value}.getBytes()")]
        internal static extern byte[] GetBytes(decimal value);

        [Transpose.Template("System.Decimal.fromBytes({bytes})")]
        internal static extern decimal FromBytes(byte[] bytes);
    }

    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.Name("System.Object")]
    [Transpose.Constructor("{ }")]
    [Transpose.External]
    public class DecimalConfig
    {
        /// <summary>
        /// Default value: 20
        /// The maximum number of significant digits of the result of a calculation or base conversion.
        /// </summary>
        public int Precision;

        /// <summary>
        /// The default rounding mode used when rounding the result of a calculation or base conversion to precision significant digits, and when rounding the return value of the round, toDecimalPlaces, toExponential, toFixed, toFormat, toNearest, toPrecision and toSignificantDigits methods.
        /// </summary>
        public int Rounding;

        /// <summary>
        /// The negative exponent value at and below which toString returns exponential notation. Default value: -7
        /// </summary>
        public int ToExpNeg;

        /// <summary>
        /// The positive exponent value at and above which toString returns exponential notation. Default value: 20
        /// </summary>
        public int ToExpPos;

        /// <summary>
        /// The negative exponent limit, i.e. the exponent value below which underflow to zero occurs. Default value: -9e15
        /// </summary>
        public int MinE;

        /// <summary>
        /// The positive exponent limit, i.e. the exponent value above which overflow to Infinity occurs. Default value: 9e15
        /// </summary>
        public double MaxE;

        /// <summary>
        /// The value that determines whether Decimal Errors are thrown. If errors is false, this library will not throw errors.
        /// </summary>
        public bool Errors;

        /// <summary>
        /// The value that determines whether cryptographically-secure pseudo-random number generation is used. Default value: false
        /// </summary>
        public bool Crypto;

        /// <summary>
        /// The modulo mode used when calculating the modulus: a mod n.
        /// </summary>
        public int Modulo;

        /// <summary>
        /// The format object configures the format of the string returned by the toFormat method.
        /// </summary>
        public DecimalFormatConfig Format;
    }

    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.Name("System.Object")]
    [Transpose.Constructor("{ }")]
    [Transpose.External]
    public class DecimalFormatConfig
    {
        /// <summary>
        /// the decimal separator
        /// </summary>
        public string DecimalSeparator;

        /// <summary>
        /// the grouping separator of the integer part of the number
        /// </summary>
        public string GroupSeparator;

        /// <summary>
        /// the primary grouping size of the integer part of the number
        /// </summary>
        public int GroupSize;

        /// <summary>
        /// the secondary grouping size of the integer part of the number
        /// </summary>
        public int SecondaryGroupSize;

        /// <summary>
        /// the grouping separator of the fraction part of the number
        /// </summary>
        public string FractionGroupSeparator;

        /// <summary>
        /// the grouping size of the fraction part of the number
        /// </summary>
        public int FractionGroupSize;
    }
}