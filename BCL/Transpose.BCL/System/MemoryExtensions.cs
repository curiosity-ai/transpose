using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace System
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class MemoryExtensions
    {
        public static ReadOnlySpan<char> AsSpan(this string text)
        {
            if (text == null) return default;
            return new ReadOnlySpan<char>(text.ToCharArray());
        }

        public static ReadOnlySpan<char> AsSpan(this string text, int start)
        {
            if (text == null)
            {
                if (start != 0) throw new ArgumentOutOfRangeException();
                return default;
            }

            return AsSpan(text, start, text.Length - start);
        }

        public static ReadOnlySpan<char> AsSpan(this string text, int start, int length)
        {
             if (text == null)
             {
                 if (start != 0 || length != 0) throw new ArgumentOutOfRangeException();
                 return default;
             }

             return new ReadOnlySpan<char>(text.ToCharArray(), start, length);
        }

        /// <summary>
        /// Element-wise comparison of two spans.
        /// </summary>
        /// <remarks>
        /// Mapped onto a runtime helper rather than written in C# over the span indexer. C# resolves
        /// <c>someArray.SequenceEqual(otherArray)</c> to *this* overload (the array-to-span conversion
        /// beats array-to-<c>IEnumerable</c>) rather than to
        /// <see cref="System.Linq.Enumerable.SequenceEqual{TSource}(IEnumerable{TSource}, IEnumerable{TSource})"/>,
        /// so this method has to cope with a span that is really the raw JS array — the implicit
        /// array-to-span conversion is not modelled, so it arrives unwrapped. Indexing it as a span
        /// (<c>span.getItem(i)</c>) threw "getItem is not a function"; the helper accepts either shape.
        /// </remarks>
        [Transpose.Template("TransposeR.spanSequenceEqual({span}, {other})")]
        public static extern bool SequenceEqual<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> other) where T : IEquatable<T>;

        [Transpose.Template("TransposeR.spanSequenceEqual({span}, {other})")]
        public static extern bool SequenceEqual<T>(this Span<T> span, ReadOnlySpan<T> other) where T : IEquatable<T>;

        // ---- arrays ------------------------------------------------------------------------------

        public static Span<T> AsSpan<T>(this T[] array) => new Span<T>(array);

        public static Span<T> AsSpan<T>(this T[] array, int start)
        {
            if (array == null)
            {
                if (start != 0) throw new ArgumentOutOfRangeException(nameof(start));
                return default;
            }
            return new Span<T>(array, start, array.Length - start);
        }

        public static Span<T> AsSpan<T>(this T[] array, int start, int length) => new Span<T>(array, start, length);

        // ---- searching ---------------------------------------------------------------------------
        //
        // Written over the span's own window (_array/_offset/_length) rather than its indexer: these run
        // on every element, and the indexer's bounds check is already implied by the loop bounds.

        public static int IndexOf<T>(this ReadOnlySpan<T> span, T value) where T : IEquatable<T>
        {
            var comparer = System.Collections.Generic.EqualityComparer<T>.Default;
            for (var i = 0; i < span._length; i++)
                if (comparer.Equals(span._array[span._offset + i], value)) return i;
            return -1;
        }

        public static int IndexOf<T>(this Span<T> span, T value) where T : IEquatable<T> => IndexOf((ReadOnlySpan<T>)span, value);

        public static int IndexOf<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> value) where T : IEquatable<T>
        {
            if (value._length == 0) return 0;
            for (var i = 0; i + value._length <= span._length; i++)
                if (RangeEquals(span, i, value)) return i;
            return -1;
        }

        public static int IndexOf<T>(this Span<T> span, ReadOnlySpan<T> value) where T : IEquatable<T> => IndexOf((ReadOnlySpan<T>)span, value);

        public static int LastIndexOf<T>(this ReadOnlySpan<T> span, T value) where T : IEquatable<T>
        {
            var comparer = System.Collections.Generic.EqualityComparer<T>.Default;
            for (var i = span._length - 1; i >= 0; i--)
                if (comparer.Equals(span._array[span._offset + i], value)) return i;
            return -1;
        }

        public static int LastIndexOf<T>(this Span<T> span, T value) where T : IEquatable<T> => LastIndexOf((ReadOnlySpan<T>)span, value);

        public static bool Contains<T>(this ReadOnlySpan<T> span, T value) where T : IEquatable<T> => IndexOf(span, value) >= 0;

        public static bool Contains<T>(this Span<T> span, T value) where T : IEquatable<T> => IndexOf((ReadOnlySpan<T>)span, value) >= 0;

        public static bool StartsWith<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> value) where T : IEquatable<T>
            => value._length <= span._length && RangeEquals(span, 0, value);

        public static bool StartsWith<T>(this Span<T> span, ReadOnlySpan<T> value) where T : IEquatable<T> => StartsWith((ReadOnlySpan<T>)span, value);

        public static bool EndsWith<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> value) where T : IEquatable<T>
            => value._length <= span._length && RangeEquals(span, span._length - value._length, value);

        public static bool EndsWith<T>(this Span<T> span, ReadOnlySpan<T> value) where T : IEquatable<T> => EndsWith((ReadOnlySpan<T>)span, value);

        private static bool RangeEquals<T>(ReadOnlySpan<T> span, int start, ReadOnlySpan<T> value)
        {
            var comparer = System.Collections.Generic.EqualityComparer<T>.Default;
            for (var j = 0; j < value._length; j++)
                if (!comparer.Equals(span._array[span._offset + start + j], value._array[value._offset + j])) return false;
            return true;
        }

        // ---- text --------------------------------------------------------------------------------

        public static ReadOnlySpan<char> Trim(this ReadOnlySpan<char> span) => TrimEnd(TrimStart(span));

        public static ReadOnlySpan<char> TrimStart(this ReadOnlySpan<char> span)
        {
            var start = 0;
            while (start < span._length && char.IsWhiteSpace(span._array[span._offset + start])) start++;
            return span.Slice(start);
        }

        public static ReadOnlySpan<char> TrimEnd(this ReadOnlySpan<char> span)
        {
            var end = span._length;
            while (end > 0 && char.IsWhiteSpace(span._array[span._offset + end - 1])) end--;
            return span.Slice(0, end);
        }

        public static bool IsWhiteSpace(this ReadOnlySpan<char> span)
        {
            for (var i = 0; i < span._length; i++)
                if (!char.IsWhiteSpace(span._array[span._offset + i])) return false;
            return true;
        }

        public static bool Equals(this ReadOnlySpan<char> span, ReadOnlySpan<char> other, StringComparison comparisonType)
            => string.Equals(span.ToString(), other.ToString(), comparisonType);
    }
}
