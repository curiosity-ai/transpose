namespace System.ComponentModel.DataAnnotations
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    [Transpose.External]
    [Transpose.NonScriptable]
    public class ScaffoldColumnAttribute : Attribute
    {
        public extern ScaffoldColumnAttribute(bool scaffold);

        public extern bool Scaffold { get; }
    }
}
