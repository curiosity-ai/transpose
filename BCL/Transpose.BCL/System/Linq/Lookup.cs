using System.Collections;
using System.Collections.Generic;

namespace System.Linq
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.IgnoreGeneric]
    [Transpose.Convention(Target = Transpose.ConventionTarget.Member, Member = Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    public interface ILookup<TKey, TElement> : IEnumerable<Grouping<TKey, TElement>>
    {
        int Count
        {
            [Transpose.Template("count()")]
            get;
        }

        [Transpose.AccessorsIndexer]
        EnumerableInstance<TElement> this[TKey key]
        {
            [Transpose.Template("get({0})")]
            get;
        }

        [Transpose.Template("contains({key})")]
        bool Contains(TKey key);
    }

    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.IgnoreGeneric]
    public class Lookup<TKey, TElement> : ILookup<TKey, TElement>
    {
        internal extern Lookup();

        public extern int Count
        {
            [Transpose.Template("count()")]
            get;
        }

        [Transpose.AccessorsIndexer]
        public extern EnumerableInstance<TElement> this[TKey key]
        {
            [Transpose.Template("get({0})")]
            get;
        }

        public extern bool Contains(TKey key);

        [Transpose.Convention(Transpose.Notation.None)]
        public extern IEnumerator<Grouping<TKey, TElement>> GetEnumerator();

        [Transpose.Convention(Transpose.Notation.None)]
        extern IEnumerator IEnumerable.GetEnumerator();
    }
}