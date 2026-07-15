namespace System.Linq.Expressions
{
    [Transpose.External]
    [Transpose.Enum(Transpose.Emit.Value)]
    public enum DynamicExpressionType
    {
        MemberAccess,
        Invocation,
        Index
    }
}