using System.Collections.Generic;

namespace System.Linq
{
    /// <summary>
    /// C#-implemented LINQ operators that are not part of the external, runtime-backed
    /// <see cref="Enumerable"/> binding (which maps every method onto <c>linq.js</c>). These are the
    /// .NET 6+ additions <see cref="Chunk"/>, <see cref="MinBy{TSource,TKey}(IEnumerable{TSource},Func{TSource,TKey})"/>
    /// and <see cref="MaxBy{TSource,TKey}(IEnumerable{TSource},Func{TSource,TKey})"/>. They are written
    /// as ordinary transpiled C# (no <c>[External]</c>) so they behave exactly like their BCL
    /// counterparts — including the empty-sequence and null-key rules of <c>MinBy</c>/<c>MaxBy</c>.
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
    }
}
