namespace System.Net.WebSockets
{
    [Transpose.External]
    [Transpose.Enum(Transpose.Emit.StringNameLowerCase)]
    public enum WebSocketMessageType
    {
        Text,
        Binary,
        Close,
    }
}