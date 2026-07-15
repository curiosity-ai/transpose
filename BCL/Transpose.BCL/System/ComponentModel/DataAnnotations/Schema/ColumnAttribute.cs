namespace System.ComponentModel.DataAnnotations.Schema
{
    /// <summary>
    /// Specifies the database column that a property is mapped to.
    /// </summary>
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    [Transpose.External]
    [Transpose.NonScriptable]
    public class ColumnAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ColumnAttribute" /> class.
        /// </summary>
        public extern ColumnAttribute();

        /// <summary>
        /// Initializes a new instance of the <see cref="ColumnAttribute" /> class.
        /// </summary>
        /// <param name="name">The name of the column the property is mapped to.</param>
        public extern ColumnAttribute(string name);

        /// <summary>
        /// The name of the column the property is mapped to.
        /// </summary>
        public extern string Name { get; }

        /// <summary>
        /// The zero-based order of the column the property is mapped to.
        /// </summary>
        public extern int Order { get; set; }

        /// <summary>
        /// The database provider specific data type of the column the property is mapped to.
        /// </summary>
        public extern string TypeName { get; set; }
    }
}
