namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.ntype === 38")]
    public sealed class ParameterExpression : Expression
    {
        [Transpose.Name("n")]
        public extern string Name { get; private set; }

        internal extern ParameterExpression();
    }
}