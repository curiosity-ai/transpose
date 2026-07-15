namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.ntype === 9")]
    public sealed class ConstantExpression : Expression
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern object Value { get; private set; }

        internal extern ConstantExpression();
    }
}