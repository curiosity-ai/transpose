using System.Collections.ObjectModel;
using System.Reflection;

namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.ntype === 31")]
    public sealed class NewExpression : Expression
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public new extern ConstructorInfo Constructor { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern ReadOnlyCollection<Expression> Arguments { get; private set; }

        [Transpose.Name("m")]
        public extern ReadOnlyCollection<MemberInfo> Members { get; private set; }

        internal extern NewExpression();
    }
}