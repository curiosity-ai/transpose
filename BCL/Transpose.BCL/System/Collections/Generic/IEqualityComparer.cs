namespace System.Collections.Generic
{
    [Transpose.External]
    [Transpose.Reflectable]
    [Transpose.Convention(Target = Transpose.ConventionTarget.Member, Member = Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    public interface IEqualityComparer<in T> : Transpose.ITransposeClass
    {
        [Transpose.Name("equals2")]
        bool Equals(T x, T y);

        [Transpose.Name("getHashCode2")]
        int GetHashCode(T obj);
    }

    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    public abstract class EqualityComparer<T> : IEqualityComparer<T>, Transpose.ITransposeClass
    {
        public static extern EqualityComparer<T> Default { 
            [Transpose.Template("System.Collections.Generic.EqualityComparer$1({T}).def")]
            get; 
        }

        //private extern EqualityComparer();

        public virtual extern bool Equals(T x, T y);

        public virtual extern int GetHashCode(T obj);
    }
}