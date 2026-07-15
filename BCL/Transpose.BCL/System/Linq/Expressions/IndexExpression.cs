using System.Collections.ObjectModel;
using System.Reflection;

namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.ntype === 55")]
    public sealed class IndexExpression : Expression
    {
        [Transpose.Name("obj")]
        public extern Expression Object { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern PropertyInfo Indexer { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern ReadOnlyCollection<Expression> Arguments { get; private set; }

        internal extern IndexExpression();
    }
}