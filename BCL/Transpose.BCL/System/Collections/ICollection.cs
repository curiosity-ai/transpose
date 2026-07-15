namespace System.Collections
{
    [Transpose.External]
    [Transpose.Convention(Target = Transpose.ConventionTarget.Member, Member = Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.Reflectable]
    public interface ICollection : IEnumerable, Transpose.ITransposeClass
    {
        /// <summary>
        /// Gets the number of elements contained in the ICollection.
        /// </summary>
        int Count
        {
            [Transpose.Template("System.Array.getCount({this})")]
            get;
        }

        [Transpose.Template("System.Array.copyTo({this}, {array}, {arrayIndex})")]
        void CopyTo(Array array, int arrayIndex);

        /// <summary>
        /// Gets an object that can be used to synchronize access to the System.Collections.ICollection.
        /// </summary>
        /// <returns>
        /// An object that can be used to synchronize access to the System.Collections.ICollection.
        /// </returns>
        object SyncRoot { get; }

        /// <summary>
        /// Gets a value indicating whether access to the System.Collections.ICollection
        /// is synchronized (thread safe).
        /// </summary>
        /// <returns>
        /// true if access to the System.Collections.ICollection is synchronized (thread
        /// safe); otherwise, false.
        /// </returns>
        bool IsSynchronized { get; }
    }
}