using System.Collections.ObjectModel;

namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.ntype === 24")]
    public sealed class MemberInitExpression : Expression
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern NewExpression NewExpression { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern ReadOnlyCollection<MemberBinding> Bindings { get; private set; }

        internal extern MemberInitExpression();
    }
}