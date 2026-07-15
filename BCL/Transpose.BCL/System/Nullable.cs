namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    public struct Nullable<T> where T : struct
    {
        [Transpose.Template("{0}")]
        public extern Nullable(T value);

        public extern bool HasValue
        {
            [Transpose.Template("System.Nullable.hasValue({this})")]
            get;
        }

        public extern T Value
        {
            [Transpose.Template("System.Nullable.getValue({this})")]
            get;
        }

        [Transpose.Template("System.Nullable.getValueOrDefault({this}, {T:default})")]
        public extern T GetValueOrDefault();

        [Transpose.Template("System.Nullable.getValueOrDefault({this}, {0})")]
        public extern T GetValueOrDefault(T defaultValue);

        public static extern implicit operator T? (T value);

        [Transpose.Template("System.Nullable.getValue({this})")]
        public static extern explicit operator T(T? value);

        [Transpose.Template("System.Nullable.equalsT({this}, {other})")]
        public override extern bool Equals(object other);

        [Transpose.Template("System.Nullable.getHashCode({this}, {T:GetHashCode})", Fn = "System.Nullable.getHashCodeFn({T:GetHashCode})")]
        public override extern int GetHashCode();

        [Transpose.Template("System.Nullable.toString({this}, {T:ToString})", Fn = "System.Nullable.toStringFn({T:ToString})")]
        public override extern string ToString();
    }

    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    public static class Nullable
    {
        public static extern int Compare<T>(Nullable<T> n1, Nullable<T> n2) where T : struct;

        public static extern bool Equals<T>(Nullable<T> n1, Nullable<T> n2) where T : struct;

        public static extern Type GetUnderlyingType(Type nullableType);
    }
}