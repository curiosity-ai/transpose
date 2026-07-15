namespace System.Collections.Generic
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    public abstract class Comparer : IComparer
    {
        public static extern Comparer Default
        {
            [Transpose.Template("new (System.Collections.Generic.Comparer$1(Object))(System.Collections.Generic.Comparer$1.$default.fn)")]
            get;
        }

        public abstract int Compare(object x, object y);
    }
}