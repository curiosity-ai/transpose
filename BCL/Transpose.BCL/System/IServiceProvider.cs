namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.NonScriptable]
    public interface IServiceProvider
    {
        object GetService(Type serviceType);
    }
}