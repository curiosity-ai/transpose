namespace System.Collections.Generic
{
    [Transpose.External]
    [Transpose.Reflectable]
    public interface IEnumerable<out T> : IEnumerable, Transpose.ITransposeClass
    {
        [Transpose.Template("Transpose.getEnumerator({this}, {T})")]
        new IEnumerator<T> GetEnumerator();
    }
}