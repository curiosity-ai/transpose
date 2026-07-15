using System.Collections;

namespace System.Text.RegularExpressions
{
    /// <summary>
    /// Returns the set of captured groups in a single match.
    /// </summary>
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    public class GroupCollection : ICollection
    {
        internal extern GroupCollection();

        /// <summary>
        /// Gets an object that can be used to synchronize access to the GroupCollection.
        /// </summary>
        public extern object SyncRoot
        {
            [Transpose.Template("getSyncRoot()")]
            get;
        }

        /// <summary>
        /// Gets a value that indicates whether access to the GroupCollection is synchronized (thread-safe).
        /// </summary>
        public extern bool IsSynchronized
        {
            [Transpose.Template("getIsSynchronized()")]
            get;
        }

        /// <summary>
        /// Gets a value that indicates whether the collection is read-only.
        /// </summary>
        public extern bool IsReadOnly
        {
            [Transpose.Template("getIsReadOnly()")]
            get;
        }

        /// <summary>
        /// Returns the number of groups in the collection.
        /// </summary>
        public extern int Count
        {
            [Transpose.Template("getCount()")]
            get;
        }

        /// <summary>
        /// Enables access to a member of the collection by integer index.
        /// </summary>
        public extern Group this[int groupnum]
        {
            [Transpose.Template("get({0})")]
            get;
        }

        /// <summary>
        /// Enables access to a member of the collection by string index.
        /// </summary>
        public extern new Group this[string groupname]
        {
            [Transpose.Template("getByName({0})")]
            get;
        }

        /// <summary>
        /// Copies all the elements of the collection to the given array beginning at the given index.
        /// </summary>
        public extern void CopyTo(Array array, int arrayIndex);

        /// <summary>
        /// Provides an enumerator that iterates through the collection.
        /// </summary>
        [Transpose.Convention(Transpose.Notation.None)]
        public extern IEnumerator GetEnumerator();
    }
}