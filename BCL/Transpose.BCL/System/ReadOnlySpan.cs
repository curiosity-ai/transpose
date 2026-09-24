using System.Runtime.CompilerServices;

namespace System
{
    /// <summary>
    /// A read-only window onto a range of a JavaScript array — see <see cref="Span{T}"/>, whose
    /// representation and emitter contract (<c>$fromArray</c>, <c>getItem$ref</c>) this shares, so the
    /// runtime helpers can treat the two alike.
    /// </summary>
    public readonly ref struct ReadOnlySpan<T>
    {
        internal readonly T[] _array;
        internal readonly int _offset;
        internal readonly int _length;

        public ReadOnlySpan(T[] array)
        {
            _array = array;
            _offset = 0;
            _length = array != null ? array.Length : 0;
        }

        public ReadOnlySpan(T[] array, int start, int length)
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

        /// <summary>The factory the emitter's span conversions go through (<c>TransposeR.toSpan</c>).</summary>
        [Transpose.Name("$fromArray")]
        internal static ReadOnlySpan<T> FromArray(T[] array, int start, int length) => new ReadOnlySpan<T>(array, start, length);

        public static ReadOnlySpan<T> Empty => default;

        [Transpose.Name("length")]
        public int Length => _length;

        public bool IsEmpty => _length == 0;

        public ref readonly T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_length)
                     throw new IndexOutOfRangeException();
                return ref _array[_offset + index];
            }
        }

        /// <summary>The cell for <c>ref readonly var x = ref span[i]</c>.</summary>
        [Transpose.Name("getItem$ref")]
        [Transpose.Script(
            "if ((index >>> 0) >= (this._length >>> 0)) { throw new System.IndexOutOfRangeException(); }",
            "return TransposeR.refElem(this._array, this._offset + index);")]
        internal extern object GetItemRef(int index);

        public ReadOnlySpan<T> Slice(int start)
        {
            if ((uint)start > (uint)_length)
                throw new ArgumentOutOfRangeException(nameof(start));
            return FromArray(_array, _offset + start, _length - start);
        }

        public ReadOnlySpan<T> Slice(int start, int length)
        {
            if ((uint)start > (uint)_length || (uint)length > (uint)(_length - start))
                throw new ArgumentOutOfRangeException();
            return FromArray(_array, _offset + start, length);
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

        /// <summary>A <c>ReadOnlySpan&lt;char&gt;</c> is its text; any other span describes itself, as
        /// .NET does (<c>System.ReadOnlySpan&lt;Int32&gt;[3]</c>).</summary>
        public override string ToString()
        {
            if (typeof(T) == typeof(char))
                return _length == 0 ? "" : new string((char[])(object)_array, _offset, _length);
            return "System.ReadOnlySpan<" + typeof(T).Name + ">[" + _length + "]";
        }

        public static bool operator ==(ReadOnlySpan<T> left, ReadOnlySpan<T> right)
            => left._length == right._length && left._offset == right._offset && (object)left._array == (object)right._array;

        public static bool operator !=(ReadOnlySpan<T> left, ReadOnlySpan<T> right) => !(left == right);

        public override bool Equals(object obj) => throw new NotSupportedException("Equals() on Span and ReadOnlySpan is not supported.");

        public override int GetHashCode() => throw new NotSupportedException("GetHashCode() on Span and ReadOnlySpan is not supported.");

        public static implicit operator ReadOnlySpan<T>(T[] array) => new ReadOnlySpan<T>(array);

        public ref struct Enumerator
        {
            private readonly T[] _array;
            private readonly int _offset;
            private readonly int _length;
            private int _index;

            internal Enumerator(ReadOnlySpan<T> span)
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

            public ref readonly T Current => ref _array[_offset + _index];

            /// <summary>The cell for <c>foreach (ref readonly var x in span)</c>.</summary>
            [Transpose.Name("$ref$Current")]
            internal object RefCurrent
            {
                [Transpose.Script("return TransposeR.refElem(this._array, this._offset + this._index);")]
                get { return null; }
            }
        }
    }
}
