namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.ntype === 45 || {this}.ntype === 81")]
    public sealed class TypeBinaryExpression : Expression
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Expression Expression { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Type TypeOperand { get; private set; }

        internal extern TypeBinaryExpression();
    }
}