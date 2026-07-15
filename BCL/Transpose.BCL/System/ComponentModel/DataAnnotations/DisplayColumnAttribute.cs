namespace System.ComponentModel.DataAnnotations
{
    /// <summary>
    /// Sets the display column, the sort column, and the sort order for when a table is used as a parent table in FK
    /// relationships.
    /// </summary>
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    [Transpose.External]
    [Transpose.NonScriptable]
    public class DisplayColumnAttribute : Attribute
    {
        public extern DisplayColumnAttribute(string displayColumn);

        public extern DisplayColumnAttribute(string displayColumn, string sortColumn);

        public extern DisplayColumnAttribute(string displayColumn, string sortColumn, bool sortDescending);

        public extern string DisplayColumn { get; }

        public extern string SortColumn { get; }

        public extern bool SortDescending { get; }
    }
}
