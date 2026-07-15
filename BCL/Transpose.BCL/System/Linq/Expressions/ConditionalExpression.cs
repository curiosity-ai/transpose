namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.ntype === 8")]
    public sealed class ConditionalExpression : Expression
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Expression Test { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Expression IfTrue { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Expression IfFalse { get; private set; }

        internal extern ConditionalExpression();
    }
}