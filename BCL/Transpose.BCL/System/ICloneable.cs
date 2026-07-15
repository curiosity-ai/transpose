namespace System
{
    [Transpose.External]
    [Transpose.Convention(Target = Transpose.ConventionTarget.Member, Member = Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.Reflectable]
    public interface ICloneable : Transpose.ITransposeClass
    {
        [Transpose.Template("Transpose.clone({this})")]
        object Clone();
    }
}