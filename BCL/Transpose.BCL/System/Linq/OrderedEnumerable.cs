using System.Collections.Generic;

namespace System.Linq
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.IgnoreGeneric]
    [Transpose.Convention(Target = Transpose.ConventionTarget.Member, Member = Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    public interface IOrderedEnumerable<TSource> : IEnumerable<TSource>
    {
        [Transpose.Template("thenBy({keySelector})")]
        IOrderedEnumerable<TSource> ThenBy<TKey>(Func<TSource, TKey> keySelector);

        [Transpose.Template("thenBy({keySelector}, {comparer})")]
        IOrderedEnumerable<TSource> ThenBy<TKey>(Func<TSource, TKey> keySelector, IComparer<TKey> comparer);

        [Transpose.Template("thenByDescending({keySelector})")]
        IOrderedEnumerable<TSource> ThenByDescending<TKey>(Func<TSource, TKey> keySelector);

        [Transpose.Template("thenByDescending({keySelector}, {comparer})")]
        IOrderedEnumerable<TSource> ThenByDescending<TKey>(Func<TSource, TKey> keySelector, IComparer<TKey> comparer);
    }

    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.IgnoreGeneric]
    public class OrderedEnumerable<TElement> : EnumerableInstance<TElement>, IOrderedEnumerable<TElement>
    {
        internal extern OrderedEnumerable();

        extern IOrderedEnumerable<TElement> IOrderedEnumerable<TElement>.ThenBy<TKey>(Func<TElement, TKey> keySelector);

        extern IOrderedEnumerable<TElement> IOrderedEnumerable<TElement>.ThenBy<TKey>(Func<TElement, TKey> keySelector, IComparer<TKey> comparer);

        extern IOrderedEnumerable<TElement> IOrderedEnumerable<TElement>.ThenByDescending<TKey>(Func<TElement, TKey> keySelector);

        extern IOrderedEnumerable<TElement> IOrderedEnumerable<TElement>.ThenByDescending<TKey>(Func<TElement, TKey> keySelector, IComparer<TKey> comparer);

        public extern OrderedEnumerable<TElement> ThenBy<TKey>(Func<TElement, TKey> keySelector);

        public extern OrderedEnumerable<TElement> ThenBy<TKey>(Func<TElement, TKey> keySelector, IComparer<TKey> comparer);

        public extern OrderedEnumerable<TElement> ThenByDescending<TKey>(Func<TElement, TKey> keySelector);

        public extern OrderedEnumerable<TElement> ThenByDescending<TKey>(Func<TElement, TKey> keySelector, IComparer<TKey> comparer);
    }
}