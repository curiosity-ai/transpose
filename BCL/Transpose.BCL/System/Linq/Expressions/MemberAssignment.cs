namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.btype === 0")]
    public sealed class MemberAssignment : MemberBinding
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Expression Expression { get; private set; }

        internal extern MemberAssignment();
    }
}