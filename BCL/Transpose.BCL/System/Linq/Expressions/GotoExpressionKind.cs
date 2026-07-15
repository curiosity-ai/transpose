namespace System.Linq.Expressions
{
    [Transpose.External]
    [Transpose.Enum(Transpose.Emit.Value)]
    public enum GotoExpressionKind
    {
        Goto,
        Return,
        Break,
        Continue,
    }
}