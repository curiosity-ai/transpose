namespace System
{
    /// <summary>
    /// Delimits a section of a one-dimensional array.
    /// </summary>
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.IgnoreGeneric]
    [Transpose.Name("System.ArraySegment")]
    [Transpose.Reflectable]
    public struct ArraySegment<T>
    {
        public extern ArraySegment(T[] array);

        public extern ArraySegment(T[] array, int offset, int count);

        /// <summary>
        /// Gets the original array containing the range of elements that the array segment delimits.
        /// </summary>
        public extern T[] Array
        {
            [Transpose.Template("getArray()")]
            get;
        }

        /// <summary>
        /// Gets the number of elements in the range delimited by the array segment.
        /// </summary>
        public extern int Count
        {
            [Transpose.Template("getCount()")]
            get;
        }

        /// <summary>
        /// Gets the position of the first element in the range delimited by the array segment,
        /// relative to the start of the original array.
        /// </summary>
        public extern int Offset
        {
            [Transpose.Template("getOffset()")]
            get;
        }
    }
}