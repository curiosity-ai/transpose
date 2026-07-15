namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.ntype == 50")]
    public abstract class DynamicExpression : Expression
    {
        [Transpose.Name("dtype")]
        public extern DynamicExpressionType DynamicType { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Expression Expression { get; private set; }

        internal extern DynamicExpression();
    }
}