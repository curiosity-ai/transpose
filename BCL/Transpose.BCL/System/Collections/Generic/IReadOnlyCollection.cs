using Transpose;

namespace System.Collections.Generic
{
    [Transpose.External]
    [Transpose.Reflectable]
    [Transpose.Convention(Target = Transpose.ConventionTarget.Member, Member = Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    public interface IReadOnlyCollection<out T> : IEnumerable<T>
    {
        int Count
        {
            [Transpose.Template("System.Array.getCount({this}, {T})")]
            get;
        }
    }
}