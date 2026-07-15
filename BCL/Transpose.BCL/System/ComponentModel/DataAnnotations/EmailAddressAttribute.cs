namespace System.ComponentModel.DataAnnotations
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter,
        AllowMultiple = false)]
    [Transpose.External]
    [Transpose.NonScriptable]
    public sealed class EmailAddressAttribute : DataTypeAttribute
    {
        public extern EmailAddressAttribute();
    }
}
