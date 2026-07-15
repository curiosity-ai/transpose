namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.ntype === 58")]
    public sealed class LoopExpression : Expression
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Expression Body { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern LabelTarget BreakLabel { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern LabelTarget ContinueLabel { get; private set; }

        internal extern LoopExpression();
    }
}