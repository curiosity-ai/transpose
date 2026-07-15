namespace System
{
    [Transpose.External]
    [Transpose.Convention(Target = Transpose.ConventionTarget.Member, Member = Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.Reflectable]
    public interface IFormattable : Transpose.ITransposeClass
    {
        [Transpose.Name("format")]
        [Transpose.Template("Transpose.format({this}, {format}, {formatProvider})")]
        string ToString(string format, IFormatProvider formatProvider);
    }
}