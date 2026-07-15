namespace System
{
    [Transpose.External]
    [Transpose.Convention(Target = Transpose.ConventionTarget.Member, Member = Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.Reflectable]
    public interface IFormatProvider : Transpose.ITransposeClass
    {
        object GetFormat(Type formatType);
    }
}