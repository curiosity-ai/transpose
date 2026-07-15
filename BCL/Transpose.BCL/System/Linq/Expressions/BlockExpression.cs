using System.Collections.ObjectModel;

namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.ntype === 47")]
    public sealed class BlockExpression : Expression
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern ReadOnlyCollection<Expression> Expressions { get; private set; }

        public extern ReadOnlyCollection<ParameterExpression> Variables
        {
            [Transpose.Template("({this}.variables || Transpose.toList([]))")] get;
            private set;
        }

        public extern Expression Result
        {
            [Transpose.Template("{this}.expressions.getItem({this}.expressions.Count - 1)")]
            get;
            private set;
        }

        internal extern BlockExpression();
    }
}