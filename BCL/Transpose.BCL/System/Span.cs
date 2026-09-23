using System.Runtime.CompilerServices;

namespace System
{
    /// <summary>
    /// A window onto a range of a JavaScript array: the array, an offset into it, and a length.
    /// <para>
    /// The emitter builds one for every span conversion C# 14 performs (array → span, span → read-only
    /// span, string → <c>ReadOnlySpan&lt;char&gt;</c>), for a collection expression or <c>params</c>
    /// argument whose target is a span, and for <c>stackalloc</c> — all through
    /// <c>TransposeR.toSpan</c>, which calls <see cref="FromArray"/>. The members with fixed JavaScript
    /// names (<c>$fromArray</c>, <c>setItem</c>, <c>getItem$ref</c>) are that contract; everything else
    /// is ordinary transpiled C#.
    /// </para>
    /// </summary>
    public readonly ref struct Span<T>
    {
        internal readonly T[] _array;
        internal readonly int _offset;
        internal readonly int _length;

        public Span(T[] array)
        {
            _array = array;
            _offset = 0;
            _length = array != null ? array.Length : 0;
        }

        public Span(T[] array, int start, int length)
        {
            if (array == null)
            {
                if (start != 0 || length != 0) throw new ArgumentOutOfRangeException();
                _array = null;
                _offset = 0;
                _length = 0;
                return;
            }
            if ((uint)start > (uint)array.Length || (uint)length > (uint)(array.Length - start))
                throw new ArgumentOutOfRangeException();
            _array = array;
            _offset = start;
            _length = length;
        }

        /// <summary>The factory the emitter's span conversions go through (<c>TransposeR.toSpan</c>).
        /// Bounds-checked, like the public constructor it calls.</summary>
        [Transpose.Name("$fromArray")]
        internal static Span<T> FromArray(T[] array, int start, int length) => new Span<T>(array, start, length);

        public static Span<T> Empty => default;

        // Emitted as the native-looking `length`, which TransposeR.spanArray and the other runtime
        // helpers read without knowing which of the two span types they were handed.
        [Transpose.Name("length")]
        public int Length => _length;

        public bool IsEmpty => _length == 0;

        public ref T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_length)
                     throw new IndexOutOfRangeException();
                return ref _array[_offset + index];
            }
        }

        /// <summary><c>span[i] = v</c>. The indexer returns a reference, which C# assigns through; a
        /// runtime-package member keeps value semantics (Emitter.RefCells.cs), so the write is a call.</summary>
        [Transpose.Name("setItem")]
        internal void SetItem(int index, T value)
        {
            if ((uint)index >= (uint)_length)
                throw new IndexOutOfRangeException();
            _array[_offset + index] = value;
        }

        /// <summary>The cell for <c>ref span[i]</c> — a <c>ref</c> local or a <c>ref</c> argument that
        /// has to write back into the span.</summary>
        [Transpose.Name("getItem$ref")]
        [Transpose.Script(
            "if ((index >>> 0) >= (this._length >>> 0)) { throw new System.IndexOutOfRangeException(); }",
            "return TransposeR.refElem(this._array, this._offset + index);")]
        internal extern object GetItemRef(int index);

        public Span<T> Slice(int start)
        {
            if ((uint)start > (uint)_length)
                throw new ArgumentOutOfRangeException(nameof(start));
            return FromArray(_array, _offset + start, _length - start);
        }

        public Span<T> Slice(int start, int length)
        {
            if ((uint)start > (uint)_length || (uint)length > (uint)(_length - start))
                throw new ArgumentOutOfRangeException();
            return FromArray(_array, _offset + start, length);
        }

        public void Clear()
        {
            for (var i = 0; i < _length; i++) _array[_offset + i] = default;
        }

        public void Fill(T value)
        {
            for (var i = 0; i < _length; i++) _array[_offset + i] = value;
        }

        public void CopyTo(Span<T> destination)
        {
            if (!TryCopyTo(destination))
                throw new ArgumentException("Destination is too short.", nameof(destination));
        }

        public bool TryCopyTo(Span<T> destination)
        {
            if (_length > destination._length) return false;
            if (_length > 0) Array.Copy(_array, _offset, destination._array, destination._offset, _length);
            return true;
        }

        public T[] ToArray()
        {
            if (_length == 0) return Array.Empty<T>();
            var destination = new T[_length];
            Array.Copy(_array, _offset, destination, 0, _length);
            return destination;
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        /// <summary>A <c>Span&lt;char&gt;</c> is its text; any other span describes itself, as .NET does
        /// (<c>System.Span&lt;Int32&gt;[3]</c>).</summary>
        public override string ToString()
        {
            if (typeof(T) == typeof(char))
                return _length == 0 ? "" : new string((char[])(object)_array, _offset, _length);
            return "System.Span<" + typeof(T).Name + ">[" + _length + "]";
        }

        /// <summary>Two spans are equal when they are the same window onto the same array.</summary>
        public static bool operator ==(Span<T> left, Span<T> right)
            => left._length == right._length && left._offset == right._offset && (object)left._array == (object)right._array;

        public static bool operator !=(Span<T> left, Span<T> right) => !(left == right);

        /// <summary>Not supported on a span, as in .NET: use <c>==</c> or <c>SequenceEqual</c>.</summary>
        public override bool Equals(object obj) => throw new NotSupportedException("Equals() on Span and ReadOnlySpan is not supported.");

        public override int GetHashCode() => throw new NotSupportedException("GetHashCode() on Span and ReadOnlySpan is not supported.");

        public static implicit operator Span<T>(T[] array) => new Span<T>(array);

        public static implicit operator ReadOnlySpan<T>(Span<T> span) => ReadOnlySpan<T>.FromArray(span._array, span._offset, span._length);

        public ref struct Enumerator
        {
            private readonly T[] _array;
            private readonly int _offset;
            private readonly int _length;
            private int _index;

            internal Enumerator(Span<T> span)
            {
                _array = span._array;
                _offset = span._offset;
                _length = span._length;
                _index = -1;
            }

            public bool MoveNext()
            {
                var index = _index + 1;
                if (index < _length)
                {
                    _index = index;
                    return true;
                }
                return false;
            }

            public ref T Current => ref _array[_offset + _index];

            /// <summary>The cell for <c>foreach (ref var x in span)</c> (see <c>TransposeR.refCurrent</c>).</summary>
            [Transpose.Name("$ref$Current")]
            internal object RefCurrent
            {
                [Transpose.Script("return TransposeR.refElem(this._array, this._offset + this._index);")]
                get { return null; }
            }
        }
    }
}
