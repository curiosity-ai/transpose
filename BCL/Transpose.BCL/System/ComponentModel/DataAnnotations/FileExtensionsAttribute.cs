namespace System.ComponentModel.DataAnnotations
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter,
        AllowMultiple = false)]
    [Transpose.External]
    [Transpose.NonScriptable]
    public sealed class FileExtensionsAttribute : DataTypeAttribute
    {
        public extern FileExtensionsAttribute();

        public extern string Extensions { get; set; }
    }
}
