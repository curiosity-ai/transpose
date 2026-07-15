using System.Reflection;

namespace System.Linq.Expressions
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    [Transpose.Cast("[4,10,11,28,29,30,34,40,44,49,54,60,62,77,78,79,80,82,83,84].indexOf({this}.ntype) >= 0")]
    public sealed class UnaryExpression : Expression
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Expression Operand { get; private set; }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern MethodInfo Method { get; private set; }

        internal extern UnaryExpression();
    }
}