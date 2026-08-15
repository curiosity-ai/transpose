namespace System.Text.Json
{
    /// <summary>Preset groups of <see cref="JsonSerializerOptions"/> values.</summary>
    public enum JsonSerializerDefaults
    {
        /// <summary>The library defaults: case-sensitive matching and member names as declared.</summary>
        General = 0,

        /// <summary>
        /// What ASP.NET Core uses: camel-cased member names, case-insensitive matching and numbers
        /// readable from JSON strings.
        /// </summary>
        Web = 1
    }
}
