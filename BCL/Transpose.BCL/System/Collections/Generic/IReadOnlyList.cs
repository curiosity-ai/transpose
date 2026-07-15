namespace System.Collections.Generic
{
    [Transpose.External]
    [Transpose.Reflectable]
    [Transpose.Convention(Target = Transpose.ConventionTarget.Member, Member = Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    public interface IReadOnlyList<out T> : IReadOnlyCollection<T>
    {
        T this[int index]
        {
            [Transpose.Template("System.Array.getItem({this}, {0}, {T})")]
            get;
        }
    }
}