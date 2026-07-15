namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    public sealed class LabelTarget
    {
        [Transpose.Name("n")]
        public extern string Name { get; }

        [Transpose.Name("t")]
        public extern Type Type { get; }

        internal extern LabelTarget();
    }
}