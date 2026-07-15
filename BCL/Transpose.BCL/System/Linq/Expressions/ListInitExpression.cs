using System.Collections.ObjectModel;

namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.ntype === 22")]
    public sealed class ListInitExpression : Expression
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern NewExpression NewExpression { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern ReadOnlyCollection<ElementInit> Initializers { get; private set; }

        internal extern ListInitExpression();
    }
}