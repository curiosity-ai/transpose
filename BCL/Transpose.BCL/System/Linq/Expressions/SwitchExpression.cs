using System.Collections.ObjectModel;
using System.Reflection;

namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.ntype === 59")]
    public sealed class SwitchExpression : Expression
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Expression SwitchValue { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern ReadOnlyCollection<SwitchCase> Cases { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Expression DefaultBody { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern MethodInfo Comparison { get; private set; }

        internal extern SwitchExpression();
    }
}