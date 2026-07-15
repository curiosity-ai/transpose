using System.Collections.ObjectModel;

namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    public sealed class SwitchCase
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern ReadOnlyCollection<Expression> TestValues { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Expression Body { get; private set; }

        internal extern SwitchCase();
    }
}