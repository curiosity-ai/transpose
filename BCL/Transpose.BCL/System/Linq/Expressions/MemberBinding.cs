using System.Reflection;

namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    public abstract class MemberBinding
    {
        [Transpose.Name("btype")]
        public extern MemberBindingType BindingType { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern MemberInfo Member { get; private set; }

        internal extern MemberBinding();
    }
}