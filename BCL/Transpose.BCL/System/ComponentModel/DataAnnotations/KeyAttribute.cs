namespace System.ComponentModel.DataAnnotations
{
    /// <summary>
    /// Used to mark one or more entity properties that provide the entity's unique identity
    /// </summary>
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    [Transpose.External]
    [Transpose.NonScriptable]
    public sealed class KeyAttribute : Attribute { }
}
