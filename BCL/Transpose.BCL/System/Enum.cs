namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
    public abstract class Enum : ValueType, IComparable, IFormattable
    {
        public static extern object Parse(Type enumType, string value);

        public static extern object Parse(Type enumType, string value, bool ignoreCase);

        public static extern string ToString(Type enumType, Enum value);

        public static extern Array GetValues(Type enumType);

        [Transpose.Template("Transpose.compare({this}, {target})")]
        public extern int CompareTo(object target);

        public static extern string Format(Type enumType, object value, string format);

        public static extern string GetName(Type enumType, object value);

        public static extern string[] GetNames(Type enumType);

        [Transpose.Template("System.Enum.hasFlag({this}, {flag})")]
        public extern bool HasFlag(Enum flag);

        public static extern bool IsDefined(Type enumType, object value);

        [Transpose.Template("System.Enum.tryParse({TEnum}, {value}, {result})")]
        public static extern bool TryParse<TEnum>(string value, out TEnum result) where TEnum : struct;

        [Transpose.Template("System.Enum.tryParse({TEnum}, {value}, {result}, {ignoreCase})")]
        public static extern bool TryParse<TEnum>(string value, bool ignoreCase, out TEnum result) where TEnum : struct;

        [Transpose.Template("System.Enum.parse({TEnum}, {value}, {result})")]
        public static extern TEnum Parse<TEnum>(string value) where TEnum : struct;

        [Transpose.Template("System.Enum.parse({TEnum}, {value}, {result})")]
        public static extern TEnum Parse<TEnum>(string value, bool ignoreCase) where TEnum : struct;


        [Transpose.Template("System.Enum.getValues({TEnum})")]
        public static extern TEnum[] GetValues<TEnum>() where TEnum : struct;

        [Transpose.Template("System.Enum.getNames({TEnum})")]
        public static extern string[] GetNames<TEnum>() where TEnum : struct;

        [Transpose.Template("System.Enum.isDefined({TEnum}, {value})")]
        public static extern bool IsDefined<TEnum>(TEnum value) where TEnum : struct;

        [Transpose.Template("System.Enum.toString({this:type}, {this})", Fn = "System.Enum.toStringFn({this:type})")]
        public override extern string ToString();

        [Transpose.Template("System.Enum.format({this:type}, {this}, {format})")]
        public extern string ToString(string format);

        [Transpose.Template("System.Enum.equals({this}, {other}, {this:type})")]
        public override extern bool Equals(object other);

        [Transpose.Template("System.Enum.format({this:type}, {this}, {format})")]
        public extern string ToString(string format, IFormatProvider formatProvider);

        [Transpose.Template("System.Enum.toObject({enumType}, {value})")]
        public static extern object ToObject(Type enumType, object value);
    }
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
}