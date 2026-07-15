using System.Collections.ObjectModel;
using System.Reflection;

namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    public sealed class ElementInit
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern MethodInfo AddMethod { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern ReadOnlyCollection<Expression> Arguments { get; private set; }

        internal extern ElementInit();
    }
}