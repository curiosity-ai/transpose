using System.Reflection;

namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.ntype === 23")]
    public sealed class MemberExpression : Expression
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern MemberInfo Member { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Expression Expression { get; private set; }

        internal extern MemberExpression();
    }
}