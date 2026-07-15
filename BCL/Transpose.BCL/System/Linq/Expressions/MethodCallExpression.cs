using System.Collections.ObjectModel;
using System.Reflection;

namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.ntype === 6")]
    public sealed class MethodCallExpression : Expression
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern MethodInfo Method { get; private set; }

        [Transpose.Name("obj")]
        public extern Expression Object { get; private set; }

        [Transpose.Name("args")]
        public extern ReadOnlyCollection<Expression> Arguments { get; private set; }

        internal extern MethodCallExpression();
    }
}