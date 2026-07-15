namespace System.Collections
{
    [Transpose.External]
    [Transpose.Reflectable]
    public interface IEnumerable : Transpose.ITransposeClass
    {
        [Transpose.Template("Transpose.getEnumerator({this})")]
        IEnumerator GetEnumerator();
    }
}