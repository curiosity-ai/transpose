namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.ntype === 53")]
    public sealed class GotoExpression : Expression
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern GotoExpressionKind Kind { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Expression Value { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern LabelTarget Target { get; private set; }

        internal extern GotoExpression();
    }
}