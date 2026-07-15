namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.ntype === 56")]
    public sealed class LabelExpression : Expression
    {
        [Transpose.Name("dv")]
        public extern Expression DefaultValue { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern LabelTarget Target { get; private set; }

        internal extern LabelExpression();
    }
}