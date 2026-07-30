using System.Collections.Generic;

namespace System.Linq
{
    /// <summary>
    /// The LINQ operators added to <c>System.Linq.Enumerable</c> after the surface the external,
    /// runtime-backed <see cref="Enumerable"/> binding covers (which maps every method onto
    /// <c>linq.js</c>, whose API predates them). They are written as ordinary transpiled C# — no
    /// <c>[External]</c>, no <c>[Template]</c> — so they behave exactly like their BCL counterparts,
    /// including the argument validation, the eager-versus-deferred split (an operator validates its
    /// arguments when it is CALLED and does its work when the result is enumerated) and the
    /// key-comparer and null-key rules.
    ///
    /// By .NET version:
    /// <list type="bullet">
    /// <item><description>Core 2.0/4.7.1: <c>Append</c>, <c>Prepend</c>, <c>ToHashSet</c>,
    /// <c>SkipLast</c>, <c>TakeLast</c></description></item>
    /// <item><description>.NET 6: <c>Chunk</c>, <c>MinBy</c>, <c>MaxBy</c>, <c>DistinctBy</c>,
    /// <c>UnionBy</c>, <c>IntersectBy</c>, <c>ExceptBy</c>, <c>TryGetNonEnumeratedCount</c>, the
    /// tuple-returning <c>Zip</c> overloads, <c>ElementAt</c>/<c>ElementAtOrDefault</c> by
    /// <see cref="Index"/> and <c>Take</c> by <see cref="Range"/></description></item>
    /// <item><description>.NET 7: <c>Order</c>, <c>OrderDescending</c></description></item>
    /// <item><description>.NET 9: <c>Index</c>, <c>CountBy</c>, <c>AggregateBy</c></description></item>
    /// <item><description>.NET 10: <c>Shuffle</c>, <c>LeftJoin</c>, <c>RightJoin</c></description></item>
    /// </list>
    ///
    /// Plus the <c>FirstOrDefault</c>/<c>LastOrDefault</c>/<c>SingleOrDefault</c> overloads that take an
    /// explicit default (.NET 6). <c>EnumerableInstance</c> — what a chained query evaluates to — already
    /// binds those onto <c>linq.js</c>, so only an <c>IEnumerable&lt;T&gt;</c> receiver needed them.
    ///
    /// <para><b>One documented difference.</b> <see cref="TryGetNonEnumeratedCount"/> answers true only
    /// for a real collection. .NET also answers true for some lazy operators whose count it can work out
    /// cheaply (<c>Enumerable.Range</c>, <c>Select</c> over a list, …), which the runtime's
    /// <c>EnumerableInstance</c> cannot expose. Answering false is always <i>correct</i> — the contract
    /// is "can I have the count without enumerating?", and false only means the caller has to enumerate
    /// — but it does mean the answer can differ from .NET's for those sources.</para>
    /// </summary>
    public static class EnumerableExtras
    {
        /// <summary>
        /// Splits the elements of a sequence into chunks of size at most <paramref name="size"/>.
        /// The final chunk may contain fewer elements; an empty source yields no chunks.
        /// </summary>
        public static IEnumerable<TSource[]> Chunk<TSource>(this IEnumerable<TSource> source, int size)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (size < 1) throw new ArgumentOutOfRangeException(nameof(size));

            return ChunkIterator(source, size);
        }

        private static IEnumerable<TSource[]> ChunkIterator<TSource>(IEnumerable<TSource> source, int size)
        {
            var buffer = new List<TSource>(size);

            foreach (var item in source)
            {
                buffer.Add(item);
                if (buffer.Count == size)
                {
                    yield return buffer.ToArray();
                    buffer.Clear();
                }
            }

            if (buffer.Count > 0)
            {
                yield return buffer.ToArray();
            }
        }

        /// <summary>
        /// Returns the element of a sequence with the minimum key, using the default comparer.
        /// </summary>
        public static TSource MinBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
            => MinBy(source, keySelector, null);

        /// <summary>
        /// Returns the element of a sequence with the minimum key, using the supplied comparer
        /// (or the default when <paramref name="comparer"/> is null). Empty-sequence and null-key
        /// handling matches System.Linq: an empty source returns the default when TSource is a
        /// reference/nullable type and throws otherwise; null keys are skipped when TKey is nullable.
        /// </summary>
        public static TSource MinBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            IComparer<TKey> comparer)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
            if (comparer == null) comparer = Comparer<TKey>.Default;

            using (var e = source.GetEnumerator())
            {
                if (!e.MoveNext())
                {
                    if (default(TSource) == null) return default(TSource);
                    throw new InvalidOperationException("Sequence contains no elements");
                }

                TSource value = e.Current;
                TKey key = keySelector(value);

                if (default(TKey) == null)
                {
                    // Nullable key: null keys never win; skip a leading run of them, and if every
                    // key is null return the first element (matching System.Linq).
                    if (key == null)
                    {
                        TSource firstValue = value;
                        do
                        {
                            if (!e.MoveNext()) return firstValue;
                            value = e.Current;
                            key = keySelector(value);
                        }
                        while (key == null);
                    }

                    while (e.MoveNext())
                    {
                        TSource nextValue = e.Current;
                        TKey nextKey = keySelector(nextValue);
                        if (nextKey != null && comparer.Compare(nextKey, key) < 0)
                        {
                            key = nextKey;
                            value = nextValue;
                        }
                    }
                }
                else
                {
                    while (e.MoveNext())
                    {
                        TSource nextValue = e.Current;
                        TKey nextKey = keySelector(nextValue);
                        if (comparer.Compare(nextKey, key) < 0)
                        {
                            key = nextKey;
                            value = nextValue;
                        }
                    }
                }

                return value;
            }
        }

        /// <summary>
        /// Returns the element of a sequence with the maximum key, using the default comparer.
        /// </summary>
        public static TSource MaxBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
            => MaxBy(source, keySelector, null);

        /// <summary>
        /// Returns the element of a sequence with the maximum key, using the supplied comparer
        /// (or the default when <paramref name="comparer"/> is null). Empty-sequence and null-key
        /// handling matches System.Linq (see <see cref="MinBy{TSource,TKey}(IEnumerable{TSource},Func{TSource,TKey},IComparer{TKey})"/>).
        /// </summary>
        public static TSource MaxBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            IComparer<TKey> comparer)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
            if (comparer == null) comparer = Comparer<TKey>.Default;

            using (var e = source.GetEnumerator())
            {
                if (!e.MoveNext())
                {
                    if (default(TSource) == null) return default(TSource);
                    throw new InvalidOperationException("Sequence contains no elements");
                }

                TSource value = e.Current;
                TKey key = keySelector(value);

                if (default(TKey) == null)
                {
                    if (key == null)
                    {
                        TSource firstValue = value;
                        do
                        {
                            if (!e.MoveNext()) return firstValue;
                            value = e.Current;
                            key = keySelector(value);
                        }
                        while (key == null);
                    }

                    while (e.MoveNext())
                    {
                        TSource nextValue = e.Current;
                        TKey nextKey = keySelector(nextValue);
                        if (nextKey != null && comparer.Compare(nextKey, key) > 0)
                        {
                            key = nextKey;
                            value = nextValue;
                        }
                    }
                }
                else
                {
                    while (e.MoveNext())
                    {
                        TSource nextValue = e.Current;
                        TKey nextKey = keySelector(nextValue);
                        if (comparer.Compare(nextKey, key) > 0)
                        {
                            key = nextKey;
                            value = nextValue;
                        }
                    }
                }

                return value;
            }
        }

        // ---- Append / Prepend -------------------------------------------------------------------

        /// <summary>Returns the sequence followed by <paramref name="element"/>.</summary>
        public static IEnumerable<TSource> Append<TSource>(this IEnumerable<TSource> source, TSource element)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return AppendIterator(source, element);
        }

        private static IEnumerable<TSource> AppendIterator<TSource>(IEnumerable<TSource> source, TSource element)
        {
            foreach (var item in source) yield return item;
            yield return element;
        }

        /// <summary>Returns <paramref name="element"/> followed by the sequence.</summary>
        public static IEnumerable<TSource> Prepend<TSource>(this IEnumerable<TSource> source, TSource element)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return PrependIterator(source, element);
        }

        private static IEnumerable<TSource> PrependIterator<TSource>(IEnumerable<TSource> source, TSource element)
        {
            yield return element;
            foreach (var item in source) yield return item;
        }

        // ---- ToHashSet --------------------------------------------------------------------------

        /// <summary>Copies the sequence into a <see cref="HashSet{T}"/> using the default comparer.</summary>
        public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source)
            => ToHashSet(source, null);

        /// <summary>
        /// Copies the sequence into a <see cref="HashSet{T}"/> using <paramref name="comparer"/> (or the
        /// default when it is null). Duplicates collapse, and the set keeps first-seen order.
        /// </summary>
        public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source,
            IEqualityComparer<TSource> comparer)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return new HashSet<TSource>(source, comparer);
        }

        // ---- The *By set operators --------------------------------------------------------------

        /// <summary>Returns the elements with distinct keys, keeping the first element per key.</summary>
        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector)
            => DistinctBy(source, keySelector, null);

        /// <summary>
        /// Returns the elements with distinct keys — the FIRST element of each key, compared with
        /// <paramref name="comparer"/> (or the default when it is null).
        /// </summary>
        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

            return DistinctByIterator(source, keySelector, comparer);
        }

        private static IEnumerable<TSource> DistinctByIterator<TSource, TKey>(IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            var seen = new HashSet<TKey>(comparer);

            foreach (var item in source)
            {
                if (seen.Add(keySelector(item))) yield return item;
            }
        }

        /// <summary>Set union by key: the elements of both sequences with distinct keys.</summary>
        public static IEnumerable<TSource> UnionBy<TSource, TKey>(this IEnumerable<TSource> first,
            IEnumerable<TSource> second, Func<TSource, TKey> keySelector)
            => UnionBy(first, second, keySelector, null);

        /// <summary>
        /// Set union by key: the elements of <paramref name="first"/> then <paramref name="second"/>,
        /// keeping the first element of each distinct key.
        /// </summary>
        public static IEnumerable<TSource> UnionBy<TSource, TKey>(this IEnumerable<TSource> first,
            IEnumerable<TSource> second, Func<TSource, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

            return UnionByIterator(first, second, keySelector, comparer);
        }

        private static IEnumerable<TSource> UnionByIterator<TSource, TKey>(IEnumerable<TSource> first,
            IEnumerable<TSource> second, Func<TSource, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            var seen = new HashSet<TKey>(comparer);

            foreach (var item in first)
            {
                if (seen.Add(keySelector(item))) yield return item;
            }

            foreach (var item in second)
            {
                if (seen.Add(keySelector(item))) yield return item;
            }
        }

        /// <summary>Set intersection by key. <paramref name="second"/> is a sequence of KEYS.</summary>
        public static IEnumerable<TSource> IntersectBy<TSource, TKey>(this IEnumerable<TSource> first,
            IEnumerable<TKey> second, Func<TSource, TKey> keySelector)
            => IntersectBy(first, second, keySelector, null);

        /// <summary>
        /// The distinct elements of <paramref name="first"/> whose key appears in
        /// <paramref name="second"/>. Note that <paramref name="second"/> is a sequence of <b>keys</b>,
        /// not of elements — that is what distinguishes these overloads from
        /// <see cref="Enumerable.Intersect{TSource}(IEnumerable{TSource}, IEnumerable{TSource})"/>.
        /// </summary>
        public static IEnumerable<TSource> IntersectBy<TSource, TKey>(this IEnumerable<TSource> first,
            IEnumerable<TKey> second, Func<TSource, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

            return IntersectByIterator(first, second, keySelector, comparer);
        }

        private static IEnumerable<TSource> IntersectByIterator<TSource, TKey>(IEnumerable<TSource> first,
            IEnumerable<TKey> second, Func<TSource, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            var wanted = new HashSet<TKey>(second, comparer);

            foreach (var item in first)
            {
                // Removing on a hit is what de-duplicates the result: a key can only be yielded once.
                if (wanted.Remove(keySelector(item))) yield return item;
            }
        }

        /// <summary>Set difference by key. <paramref name="second"/> is a sequence of KEYS.</summary>
        public static IEnumerable<TSource> ExceptBy<TSource, TKey>(this IEnumerable<TSource> first,
            IEnumerable<TKey> second, Func<TSource, TKey> keySelector)
            => ExceptBy(first, second, keySelector, null);

        /// <summary>
        /// The distinct elements of <paramref name="first"/> whose key does NOT appear in
        /// <paramref name="second"/>, which is a sequence of <b>keys</b>.
        /// </summary>
        public static IEnumerable<TSource> ExceptBy<TSource, TKey>(this IEnumerable<TSource> first,
            IEnumerable<TKey> second, Func<TSource, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

            return ExceptByIterator(first, second, keySelector, comparer);
        }

        private static IEnumerable<TSource> ExceptByIterator<TSource, TKey>(IEnumerable<TSource> first,
            IEnumerable<TKey> second, Func<TSource, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            // Seeding with the excluded keys means Add both filters them out and de-duplicates the rest.
            var seen = new HashSet<TKey>(second, comparer);

            foreach (var item in first)
            {
                if (seen.Add(keySelector(item))) yield return item;
            }
        }

        // ---- SkipLast / TakeLast ----------------------------------------------------------------

        /// <summary>
        /// All but the last <paramref name="count"/> elements. A count of zero or less returns the whole
        /// sequence rather than throwing.
        /// </summary>
        public static IEnumerable<TSource> SkipLast<TSource>(this IEnumerable<TSource> source, int count)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return count <= 0 ? SkipLastIterator(source, 0) : SkipLastIterator(source, count);
        }

        private static IEnumerable<TSource> SkipLastIterator<TSource>(IEnumerable<TSource> source, int count)
        {
            if (count == 0)
            {
                foreach (var item in source) yield return item;
                yield break;
            }

            // A rolling buffer of the trailing `count` elements: an element is only yielded once a later
            // one has pushed it out of the window, so the last `count` are never yielded at all.
            var window = new Queue<TSource>();

            foreach (var item in source)
            {
                window.Enqueue(item);
                if (window.Count > count) yield return window.Dequeue();
            }
        }

        /// <summary>
        /// The last <paramref name="count"/> elements, in source order. A count of zero or less returns an
        /// empty sequence rather than throwing.
        /// </summary>
        public static IEnumerable<TSource> TakeLast<TSource>(this IEnumerable<TSource> source, int count)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return TakeLastIterator(source, count);
        }

        private static IEnumerable<TSource> TakeLastIterator<TSource>(IEnumerable<TSource> source, int count)
        {
            if (count <= 0) yield break;

            var window = new Queue<TSource>();

            foreach (var item in source)
            {
                window.Enqueue(item);
                if (window.Count > count) window.Dequeue();
            }

            while (window.Count > 0) yield return window.Dequeue();
        }

        // ---- Order / OrderDescending ------------------------------------------------------------

        /// <summary>Sorts the elements by themselves, using the default comparer.</summary>
        public static IOrderedEnumerable<TSource> Order<TSource>(this IEnumerable<TSource> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.OrderBy(Identity);
        }

        /// <summary>Sorts the elements by themselves, using <paramref name="comparer"/>.</summary>
        public static IOrderedEnumerable<TSource> Order<TSource>(this IEnumerable<TSource> source,
            IComparer<TSource> comparer)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.OrderBy(Identity, comparer);
        }

        /// <summary>Sorts the elements by themselves in descending order, using the default comparer.</summary>
        public static IOrderedEnumerable<TSource> OrderDescending<TSource>(this IEnumerable<TSource> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.OrderByDescending(Identity);
        }

        /// <summary>Sorts the elements by themselves in descending order, using <paramref name="comparer"/>.</summary>
        public static IOrderedEnumerable<TSource> OrderDescending<TSource>(this IEnumerable<TSource> source,
            IComparer<TSource> comparer)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return source.OrderByDescending(Identity, comparer);
        }

        /// <summary>The key selector <c>Order</c>/<c>OrderDescending</c> sort by: the element itself.</summary>
        private static TSource Identity<TSource>(TSource value) => value;

        // ---- Index ------------------------------------------------------------------------------

        /// <summary>
        /// Pairs every element with its zero-based position, as <c>(Index, Item)</c> — the tuple-returning
        /// form of <c>Select((item, index) =&gt; …)</c> that reads well in a <c>foreach</c>.
        /// </summary>
        public static IEnumerable<(int Index, TSource Item)> Index<TSource>(this IEnumerable<TSource> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return IndexIterator(source);
        }

        private static IEnumerable<(int Index, TSource Item)> IndexIterator<TSource>(IEnumerable<TSource> source)
        {
            int index = 0;

            foreach (var item in source)
            {
                yield return (index, item);
                index++;
            }
        }

        // ---- CountBy / AggregateBy --------------------------------------------------------------

        /// <summary>
        /// Counts the elements per key, as <c>KeyValuePair&lt;TKey, int&gt;</c>s in first-seen key order.
        /// Cheaper and more direct than <c>GroupBy(key).Select(g =&gt; …g.Count())</c>, which materializes
        /// every group.
        /// </summary>
        public static IEnumerable<KeyValuePair<TKey, int>> CountBy<TSource, TKey>(this IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector, IEqualityComparer<TKey> keyComparer = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

            return CountByIterator(source, keySelector, keyComparer);
        }

        private static IEnumerable<KeyValuePair<TKey, int>> CountByIterator<TSource, TKey>(
            IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey> keyComparer)
        {
            // The key order is the order the keys were first SEEN, which a Dictionary does not promise, so
            // it is tracked separately.
            var counts = new Dictionary<TKey, int>(keyComparer);
            var order = new List<TKey>();

            foreach (var item in source)
            {
                TKey key = keySelector(item);
                int existing;
                if (counts.TryGetValue(key, out existing))
                {
                    counts[key] = existing + 1;
                }
                else
                {
                    counts.Add(key, 1);
                    order.Add(key);
                }
            }

            foreach (var key in order) yield return new KeyValuePair<TKey, int>(key, counts[key]);
        }

        /// <summary>
        /// Aggregates the elements per key from a shared <paramref name="seed"/>, as
        /// <c>KeyValuePair&lt;TKey, TAccumulate&gt;</c>s in first-seen key order.
        /// </summary>
        public static IEnumerable<KeyValuePair<TKey, TAccumulate>> AggregateBy<TSource, TKey, TAccumulate>(
            this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, TAccumulate seed,
            Func<TAccumulate, TSource, TAccumulate> func, IEqualityComparer<TKey> keyComparer = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
            if (func == null) throw new ArgumentNullException(nameof(func));

            return AggregateByIterator(source, keySelector, ConstantSeed<TKey, TAccumulate>(seed), func, keyComparer);
        }

        /// <summary>
        /// Aggregates the elements per key, with the seed computed per key by
        /// <paramref name="seedSelector"/> — for an accumulator that must not be shared between keys
        /// (a <c>List&lt;T&gt;</c>, say).
        /// </summary>
        public static IEnumerable<KeyValuePair<TKey, TAccumulate>> AggregateBy<TSource, TKey, TAccumulate>(
            this IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            Func<TKey, TAccumulate> seedSelector, Func<TAccumulate, TSource, TAccumulate> func,
            IEqualityComparer<TKey> keyComparer = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
            if (seedSelector == null) throw new ArgumentNullException(nameof(seedSelector));
            if (func == null) throw new ArgumentNullException(nameof(func));

            return AggregateByIterator(source, keySelector, seedSelector, func, keyComparer);
        }

        /// <summary>Turns the shared-seed overload's single value into the per-key selector form.</summary>
        private static Func<TKey, TAccumulate> ConstantSeed<TKey, TAccumulate>(TAccumulate seed)
            => key => seed;

        private static IEnumerable<KeyValuePair<TKey, TAccumulate>> AggregateByIterator<TSource, TKey, TAccumulate>(
            IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TKey, TAccumulate> seedSelector,
            Func<TAccumulate, TSource, TAccumulate> func, IEqualityComparer<TKey> keyComparer)
        {
            var accumulators = new Dictionary<TKey, TAccumulate>(keyComparer);
            var order = new List<TKey>();

            foreach (var item in source)
            {
                TKey key = keySelector(item);
                TAccumulate accumulator;
                if (!accumulators.TryGetValue(key, out accumulator))
                {
                    accumulator = seedSelector(key);
                    order.Add(key);
                }

                accumulators[key] = func(accumulator, item);
            }

            foreach (var key in order) yield return new KeyValuePair<TKey, TAccumulate>(key, accumulators[key]);
        }

        // ---- TryGetNonEnumeratedCount -----------------------------------------------------------

        /// <summary>
        /// Reports the element count when it can be had without enumerating — i.e. when the source is a
        /// real collection. See the type's remarks: a lazy sequence answers false here even where .NET
        /// could work its count out cheaply, which is a permitted (if less helpful) answer.
        /// </summary>
        public static bool TryGetNonEnumeratedCount<TSource>(this IEnumerable<TSource> source, out int count)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            if (source is ICollection<TSource> generic)
            {
                count = generic.Count;
                return true;
            }

            if (source is System.Collections.ICollection collection)
            {
                count = collection.Count;
                return true;
            }

            count = 0;
            return false;
        }

        // ---- The tuple-returning Zip overloads --------------------------------------------------

        /// <summary>
        /// Pairs the two sequences positionally as tuples, stopping at the shorter one — the selectorless
        /// form of <see cref="Enumerable.Zip{TFirst, TSecond, TResult}(IEnumerable{TFirst}, IEnumerable{TSecond}, Func{TFirst, TSecond, TResult})"/>.
        /// </summary>
        public static IEnumerable<(TFirst First, TSecond Second)> Zip<TFirst, TSecond>(
            this IEnumerable<TFirst> first, IEnumerable<TSecond> second)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));

            return ZipIterator(first, second);
        }

        private static IEnumerable<(TFirst First, TSecond Second)> ZipIterator<TFirst, TSecond>(
            IEnumerable<TFirst> first, IEnumerable<TSecond> second)
        {
            using (var e1 = first.GetEnumerator())
            using (var e2 = second.GetEnumerator())
            {
                while (e1.MoveNext() && e2.MoveNext())
                {
                    yield return (e1.Current, e2.Current);
                }
            }
        }

        /// <summary>Pairs three sequences positionally as tuples, stopping at the shortest.</summary>
        public static IEnumerable<(TFirst First, TSecond Second, TThird Third)> Zip<TFirst, TSecond, TThird>(
            this IEnumerable<TFirst> first, IEnumerable<TSecond> second, IEnumerable<TThird> third)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));
            if (third == null) throw new ArgumentNullException(nameof(third));

            return ZipIterator(first, second, third);
        }

        private static IEnumerable<(TFirst First, TSecond Second, TThird Third)> ZipIterator<TFirst, TSecond, TThird>(
            IEnumerable<TFirst> first, IEnumerable<TSecond> second, IEnumerable<TThird> third)
        {
            using (var e1 = first.GetEnumerator())
            using (var e2 = second.GetEnumerator())
            using (var e3 = third.GetEnumerator())
            {
                while (e1.MoveNext() && e2.MoveNext() && e3.MoveNext())
                {
                    yield return (e1.Current, e2.Current, e3.Current);
                }
            }
        }

        // ---- The *OrDefault overloads that take an explicit default ----------------------------

        /// <summary>The first element, or <paramref name="defaultValue"/> when the sequence is empty.</summary>
        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, TSource defaultValue)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            foreach (var item in source) return item;

            return defaultValue;
        }

        /// <summary>The first matching element, or <paramref name="defaultValue"/> when none matches.</summary>
        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source,
            Func<TSource, bool> predicate, TSource defaultValue)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            foreach (var item in source)
            {
                if (predicate(item)) return item;
            }

            return defaultValue;
        }

        /// <summary>The last element, or <paramref name="defaultValue"/> when the sequence is empty.</summary>
        public static TSource LastOrDefault<TSource>(this IEnumerable<TSource> source, TSource defaultValue)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            TSource last = defaultValue;
            foreach (var item in source) last = item;

            return last;
        }

        /// <summary>The last matching element, or <paramref name="defaultValue"/> when none matches.</summary>
        public static TSource LastOrDefault<TSource>(this IEnumerable<TSource> source,
            Func<TSource, bool> predicate, TSource defaultValue)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            TSource last = defaultValue;
            foreach (var item in source)
            {
                if (predicate(item)) last = item;
            }

            return last;
        }

        /// <summary>
        /// The single element, or <paramref name="defaultValue"/> when the sequence is empty. Still throws
        /// when the sequence holds more than one element — the "OrDefault" covers emptiness, not ambiguity.
        /// </summary>
        public static TSource SingleOrDefault<TSource>(this IEnumerable<TSource> source, TSource defaultValue)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            using (var e = source.GetEnumerator())
            {
                if (!e.MoveNext()) return defaultValue;

                TSource single = e.Current;
                if (e.MoveNext()) throw new InvalidOperationException("Sequence contains more than one element");

                return single;
            }
        }

        /// <summary>
        /// The single matching element, or <paramref name="defaultValue"/> when none matches. Throws when
        /// more than one element matches.
        /// </summary>
        public static TSource SingleOrDefault<TSource>(this IEnumerable<TSource> source,
            Func<TSource, bool> predicate, TSource defaultValue)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            TSource single = defaultValue;
            bool found = false;

            foreach (var item in source)
            {
                if (!predicate(item)) continue;
                if (found) throw new InvalidOperationException("Sequence contains more than one matching element");

                single = item;
                found = true;
            }

            return single;
        }

        // ---- Shuffle ----------------------------------------------------------------------------

        /// <summary>
        /// The elements in a random order, using <see cref="Random.Shared"/>. Deferred: nothing is drawn
        /// until the result is enumerated, and each enumeration shuffles afresh.
        /// </summary>
        public static IEnumerable<TSource> Shuffle<TSource>(this IEnumerable<TSource> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return ShuffleIterator(source);
        }

        private static IEnumerable<TSource> ShuffleIterator<TSource>(IEnumerable<TSource> source)
        {
            // The whole sequence has to be in hand before the first element can be known, so it is
            // buffered and shuffled in place (Fisher-Yates) rather than streamed.
            var buffer = new List<TSource>(source);
            var random = Random.Shared;

            for (int i = buffer.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                TSource swap = buffer[i];
                buffer[i] = buffer[j];
                buffer[j] = swap;
            }

            foreach (var item in buffer) yield return item;
        }

        // ---- LeftJoin / RightJoin ---------------------------------------------------------------

        /// <summary>
        /// Correlates the two sequences on a key, keeping every element of <paramref name="outer"/>: an
        /// outer element with no match is paired with <c>default(TInner)</c>.
        /// </summary>
        public static IEnumerable<TResult> LeftJoin<TOuter, TInner, TKey, TResult>(this IEnumerable<TOuter> outer,
            IEnumerable<TInner> inner, Func<TOuter, TKey> outerKeySelector, Func<TInner, TKey> innerKeySelector,
            Func<TOuter, TInner, TResult> resultSelector)
            => LeftJoin(outer, inner, outerKeySelector, innerKeySelector, resultSelector, null);

        /// <summary>
        /// Correlates the two sequences on a key compared with <paramref name="comparer"/>, keeping every
        /// element of <paramref name="outer"/>. The result is in outer order, with each outer element's
        /// matches in inner order. A null key never matches, so an outer element with one is always paired
        /// with <c>default(TInner)</c>.
        /// </summary>
        public static IEnumerable<TResult> LeftJoin<TOuter, TInner, TKey, TResult>(this IEnumerable<TOuter> outer,
            IEnumerable<TInner> inner, Func<TOuter, TKey> outerKeySelector, Func<TInner, TKey> innerKeySelector,
            Func<TOuter, TInner, TResult> resultSelector, IEqualityComparer<TKey> comparer)
        {
            if (outer == null) throw new ArgumentNullException(nameof(outer));
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            if (outerKeySelector == null) throw new ArgumentNullException(nameof(outerKeySelector));
            if (innerKeySelector == null) throw new ArgumentNullException(nameof(innerKeySelector));
            if (resultSelector == null) throw new ArgumentNullException(nameof(resultSelector));

            return LeftJoinIterator(outer, inner, outerKeySelector, innerKeySelector, resultSelector, comparer);
        }

        private static IEnumerable<TResult> LeftJoinIterator<TOuter, TInner, TKey, TResult>(IEnumerable<TOuter> outer,
            IEnumerable<TInner> inner, Func<TOuter, TKey> outerKeySelector, Func<TInner, TKey> innerKeySelector,
            Func<TOuter, TInner, TResult> resultSelector, IEqualityComparer<TKey> comparer)
        {
            using (var e = outer.GetEnumerator())
            {
                // An empty outer means no result at all, and .NET does not even enumerate the inner
                // sequence in that case — so the lookup is built only once there is something to match.
                if (!e.MoveNext()) yield break;

                var lookup = BuildJoinLookup(inner, innerKeySelector, comparer);

                do
                {
                    TOuter item = e.Current;
                    TKey key = outerKeySelector(item);
                    List<TInner> matches;

                    if (key != null && lookup.TryGetValue(key, out matches))
                    {
                        foreach (var match in matches) yield return resultSelector(item, match);
                    }
                    else
                    {
                        yield return resultSelector(item, default(TInner));
                    }
                }
                while (e.MoveNext());
            }
        }

        /// <summary>
        /// Correlates the two sequences on a key, keeping every element of <paramref name="inner"/>: an
        /// inner element with no match is paired with <c>default(TOuter)</c>.
        /// </summary>
        public static IEnumerable<TResult> RightJoin<TOuter, TInner, TKey, TResult>(this IEnumerable<TOuter> outer,
            IEnumerable<TInner> inner, Func<TOuter, TKey> outerKeySelector, Func<TInner, TKey> innerKeySelector,
            Func<TOuter, TInner, TResult> resultSelector)
            => RightJoin(outer, inner, outerKeySelector, innerKeySelector, resultSelector, null);

        /// <summary>
        /// Correlates the two sequences on a key compared with <paramref name="comparer"/>, keeping every
        /// element of <paramref name="inner"/>. The result is in INNER order (the kept side drives it),
        /// with each inner element's matches in outer order.
        /// </summary>
        public static IEnumerable<TResult> RightJoin<TOuter, TInner, TKey, TResult>(this IEnumerable<TOuter> outer,
            IEnumerable<TInner> inner, Func<TOuter, TKey> outerKeySelector, Func<TInner, TKey> innerKeySelector,
            Func<TOuter, TInner, TResult> resultSelector, IEqualityComparer<TKey> comparer)
        {
            if (outer == null) throw new ArgumentNullException(nameof(outer));
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            if (outerKeySelector == null) throw new ArgumentNullException(nameof(outerKeySelector));
            if (innerKeySelector == null) throw new ArgumentNullException(nameof(innerKeySelector));
            if (resultSelector == null) throw new ArgumentNullException(nameof(resultSelector));

            return RightJoinIterator(outer, inner, outerKeySelector, innerKeySelector, resultSelector, comparer);
        }

        private static IEnumerable<TResult> RightJoinIterator<TOuter, TInner, TKey, TResult>(IEnumerable<TOuter> outer,
            IEnumerable<TInner> inner, Func<TOuter, TKey> outerKeySelector, Func<TInner, TKey> innerKeySelector,
            Func<TOuter, TInner, TResult> resultSelector, IEqualityComparer<TKey> comparer)
        {
            using (var e = inner.GetEnumerator())
            {
                if (!e.MoveNext()) yield break;

                var lookup = BuildJoinLookup(outer, outerKeySelector, comparer);

                do
                {
                    TInner item = e.Current;
                    TKey key = innerKeySelector(item);
                    List<TOuter> matches;

                    if (key != null && lookup.TryGetValue(key, out matches))
                    {
                        foreach (var match in matches) yield return resultSelector(match, item);
                    }
                    else
                    {
                        yield return resultSelector(default(TOuter), item);
                    }
                }
                while (e.MoveNext());
            }
        }

        /// <summary>
        /// Groups a join's matched side by key, in source order within each key. Null-keyed elements are
        /// DROPPED, which is how System.Linq's join lookup behaves — a null key never correlates.
        /// </summary>
        private static Dictionary<TKey, List<TElement>> BuildJoinLookup<TElement, TKey>(IEnumerable<TElement> source,
            Func<TElement, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            var lookup = new Dictionary<TKey, List<TElement>>(comparer);

            foreach (var item in source)
            {
                TKey key = keySelector(item);
                if (key == null) continue;

                List<TElement> bucket;
                if (!lookup.TryGetValue(key, out bucket))
                {
                    bucket = new List<TElement>();
                    lookup.Add(key, bucket);
                }

                bucket.Add(item);
            }

            return lookup;
        }

        // ---- Index/Range indexing ---------------------------------------------------------------

        /// <summary>
        /// The element at <paramref name="index"/>, which may count from the end (<c>^1</c> is the last).
        /// Throws <see cref="ArgumentOutOfRangeException"/> when the index is outside the sequence.
        /// </summary>
        public static TSource ElementAt<TSource>(this IEnumerable<TSource> source, System.Index index)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            if (!index.IsFromEnd) return source.ElementAt(index.Value);

            TSource value;
            if (!TryGetElementFromEnd(source, index.Value, out value))
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return value;
        }

        /// <summary>
        /// The element at <paramref name="index"/> (which may count from the end), or the type's default
        /// when the index is outside the sequence.
        /// </summary>
        public static TSource ElementAtOrDefault<TSource>(this IEnumerable<TSource> source, System.Index index)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            if (!index.IsFromEnd) return source.ElementAtOrDefault(index.Value);

            TSource value;
            return TryGetElementFromEnd(source, index.Value, out value) ? value : default(TSource);
        }

        /// <summary>
        /// Reads the element <paramref name="fromEnd"/> places from the end (1 being the last) with a
        /// rolling buffer, so the source is enumerated once and only that many elements are held.
        /// </summary>
        private static bool TryGetElementFromEnd<TSource>(IEnumerable<TSource> source, int fromEnd, out TSource value)
        {
            value = default(TSource);

            // ^0 is one past the last element, so it is never in range.
            if (fromEnd <= 0) return false;

            var window = new Queue<TSource>();

            foreach (var item in source)
            {
                window.Enqueue(item);
                if (window.Count > fromEnd) window.Dequeue();
            }

            if (window.Count < fromEnd) return false;

            value = window.Dequeue();
            return true;
        }

        /// <summary>
        /// The elements in <paramref name="range"/>, whose ends may each count from the start or the end
        /// (<c>1..^1</c> drops the first and the last). An empty or inverted range yields nothing rather
        /// than throwing.
        /// </summary>
        public static IEnumerable<TSource> Take<TSource>(this IEnumerable<TSource> source, Range range)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return TakeRangeIterator(source, range);
        }

        private static IEnumerable<TSource> TakeRangeIterator<TSource>(IEnumerable<TSource> source, Range range)
        {
            System.Index start = range.Start;
            System.Index end = range.End;

            if (start.IsFromEnd)
            {
                // A window that starts from the END cannot be placed until the length is known, so the
                // source is buffered. (Every other shape streams.)
                var buffered = new List<TSource>(source);
                int length = buffered.Count;
                int from = start.GetOffset(length);
                int to = end.GetOffset(length);
                if (from < 0) from = 0;
                if (to > length) to = length;

                for (int i = from; i < to; i++) yield return buffered[i];
                yield break;
            }

            int skip = start.Value;

            if (!end.IsFromEnd)
            {
                int stop = end.Value;
                if (stop <= skip) yield break;

                int index = 0;
                foreach (var item in source)
                {
                    if (index >= stop) break;
                    if (index >= skip) yield return item;
                    index++;
                }

                yield break;
            }

            int dropped = end.Value;

            if (dropped == 0)
            {
                int index = 0;
                foreach (var item in source)
                {
                    if (index >= skip) yield return item;
                    index++;
                }

                yield break;
            }

            // Both ends are known relative to the start except for the trailing `dropped` elements, so a
            // rolling buffer of that size is enough — the source still streams.
            var window = new Queue<TSource>();
            int position = 0;

            foreach (var item in source)
            {
                window.Enqueue(item);
                if (window.Count > dropped)
                {
                    TSource ready = window.Dequeue();
                    if (position >= skip) yield return ready;
                    position++;
                }
            }
        }
    }
}
