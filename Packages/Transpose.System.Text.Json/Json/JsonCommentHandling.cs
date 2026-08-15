namespace System.Text.Json
{
    /// <summary>How a comment in the payload is treated on read.</summary>
    public enum JsonCommentHandling : byte
    {
        /// <summary>A comment is a syntax error. This is the default.</summary>
        Disallow = 0,

        /// <summary>A comment is skipped over.</summary>
        Skip = 1,

        /// <summary>
        /// A comment is surfaced as a token. The whole-document serializer never surfaces tokens, so
        /// this behaves as <see cref="Skip"/> here.
        /// </summary>
        Allow = 2
    }
}
