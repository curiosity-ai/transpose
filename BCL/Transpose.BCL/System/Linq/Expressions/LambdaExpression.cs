using System.Collections.ObjectModel;

namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.ntype === 18")]
    public abstract class LambdaExpression : Expression
    {
        [Transpose.Name("p")]
        public extern ReadOnlyCollection<ParameterExpression> Parameters { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Expression Body { get; private set; }

        [Transpose.Name("rt")]
        public extern Expression ReturnType { get; private set; }

        internal extern LambdaExpression();
    }
}