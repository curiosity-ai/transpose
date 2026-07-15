namespace System
{
    [Transpose.External]
    [Transpose.Convention(Target = Transpose.ConventionTarget.Member, Member = Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.Reflectable]
    public interface IComparable : Transpose.ITransposeClass
    {
        [Transpose.Template("Transpose.compare({this}, {obj})")]
        int CompareTo(object obj);
    }

    [Transpose.External]
    [Transpose.Convention(Target = Transpose.ConventionTarget.Member, Member = Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.Reflectable]
    public interface IComparable<in T> : Transpose.ITransposeClass
    {
        [Transpose.Template("Transpose.compare({this}, {other}, false, {T})")]
        int CompareTo(T other);
    }
}