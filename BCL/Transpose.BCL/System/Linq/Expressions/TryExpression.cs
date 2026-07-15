using System.Collections.ObjectModel;

namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("{this}.ntype === 61")]
    public sealed class TryExpression : Expression
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Expression Body { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern ReadOnlyCollection<CatchBlock> Handlers { get; private set; }

        [Transpose.Name("finallyExpr")]
        public extern Expression Finally { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Expression Fault { get; private set; }

        internal extern TryExpression();
    }
}