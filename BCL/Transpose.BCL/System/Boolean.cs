using System.Runtime.CompilerServices;

namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    public struct Boolean : IComparable, IComparable<bool>, IEquatable<bool>
    {
        [Transpose.InlineConst]
        internal const int True = 1;

        [Transpose.InlineConst]
        internal const int False = 0;

        [Transpose.Template("System.Boolean.trueString")]
        public static readonly string TrueString = "True";

        [Transpose.Template("System.Boolean.falseString")]
        public static readonly string FalseString = "False";

        [Transpose.Template("false")]
        private extern Boolean(DummyTypeUsedToAddAttributeToDefaultValueTypeConstructor _);

        [Transpose.Template("Transpose.compare({this}, {other})")]
        public extern int CompareTo(bool other);

        [Transpose.Template("{this} === {other}")]
        public extern bool Equals(bool other);

        [Transpose.Template("System.Boolean.parse({value})")]
        public static extern bool Parse(string value);

        [Transpose.Template("System.Boolean.tryParse({value}, {result})")]
        public static extern bool TryParse(string value, out bool result);

        [Transpose.Template("Transpose.compare({this}, {other})")]
        public extern int CompareTo(object obj);

        [Transpose.Template(Fn = "System.Boolean.toString")]
        public override extern string ToString();
    }
}