namespace System.ComponentModel.DataAnnotations.Schema
{
    /// <summary>
    /// The pattern used to generate values for a property in the database.
    /// </summary>
    // [External] with no JavaScript counterpart - see DataType: Emit.Value inlines the ordinal
    // rather than emitting a path to an object the runtime never defines.
    [Transpose.External]
    [Transpose.Enum(Transpose.Emit.Value)]
    public enum DatabaseGeneratedOption
    {
        /// <summary>
        /// The database does not generate values.
        /// </summary>
        None = 0,

        /// <summary>
        /// The database generates a value when a row is inserted.
        /// </summary>
        Identity = 1,

        /// <summary>
        /// The database generates a value when a row is inserted or updated.
        /// </summary>
        Computed = 2
    }
}
