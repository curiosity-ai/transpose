namespace System.Collections.Generic
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    public abstract class Comparer<T> : IComparer<T>
    {
        public static extern Comparer<T> Default
        {
            [Transpose.Template("new (System.Collections.Generic.Comparer$1({T}))(System.Collections.Generic.Comparer$1.$default.fn)")]
            get;
        }

        public abstract int Compare(T x, T y);

        [Transpose.Template("new (System.Collections.Generic.Comparer$1({T}))({comparison})")]
        public static extern Comparer<T> Create(Comparison<T> comparison);
    }
}