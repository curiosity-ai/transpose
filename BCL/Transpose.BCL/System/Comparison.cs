namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.Name("Function")]
    [Transpose.External]
    public delegate int Comparison<in T>(T x, T y);
}