namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    public sealed class CatchBlock
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern ParameterExpression Variable { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Type Test { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Expression Body { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Expression Filter { get; private set; }

        internal extern CatchBlock();
    }
}