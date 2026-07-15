namespace System.Collections.Generic
{
    [Transpose.External]
    [Transpose.Reflectable]
    [Transpose.Convention(Target = Transpose.ConventionTarget.Member, Member = Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    public interface IList<T> : ICollection<T>
    {
        T this[int index]
        {
            [Transpose.Template("System.Array.getItem({this}, {index}, {T})")]
            get;
            [Transpose.Template("System.Array.setItem({this}, {index}, {value}, {T})")]
            set;
        }

        [Transpose.Template("System.Array.indexOf({this}, {item}, 0, null, {T})")]
        int IndexOf(T item);

        [Transpose.Template("System.Array.insert({this}, {index}, {item}, {T})")]
        void Insert(int index, T item);

        [Transpose.Template("System.Array.removeAt({this}, {index}, {T})")]
        void RemoveAt(int index);
    }
}