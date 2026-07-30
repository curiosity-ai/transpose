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
    }
}
