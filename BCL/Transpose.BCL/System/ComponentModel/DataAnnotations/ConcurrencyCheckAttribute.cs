namespace System.ComponentModel.DataAnnotations
{
    /// <summary>
    /// This attribute is used to mark the members of a Type that participate in optimistic concurrency checks.
    /// </summary>
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    [Transpose.External]
    [Transpose.NonScriptable]
    public sealed class ConcurrencyCheckAttribute : Attribute { }
}
