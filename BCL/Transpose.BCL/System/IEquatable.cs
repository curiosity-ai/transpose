namespace System
{
    [Transpose.External]
    [Transpose.Convention(Target = Transpose.ConventionTarget.Member, Member = Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.Reflectable]
    public interface IEquatable<T> : Transpose.ITransposeClass
    {
        [Transpose.Template("Transpose.equalsT({this}, {other}, {T})")]
        [Transpose.Name("equalsT")]
        bool Equals(T other);
    }
}