namespace System.ComponentModel.DataAnnotations
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [AttributeUsage(
        AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Parameter,
        AllowMultiple = false)]
    [Transpose.External]
    [Transpose.NonScriptable]
    public sealed class EnumDataTypeAttribute : DataTypeAttribute
    {
        public extern EnumDataTypeAttribute(Type enumType);

        public extern Type EnumType { get; }
    }
}
