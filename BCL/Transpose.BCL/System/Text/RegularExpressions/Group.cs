namespace System.Text.RegularExpressions
{
    /// <summary>
    /// Represents the results from a single capturing group.
    /// </summary>
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    public class Group : Capture
    {
        internal extern Group(string text, int[] caps, int capcount);

        /// <summary>
        /// Gets a value indicating whether the match is successful.
        /// </summary>
        public extern bool Success
        {
            [Transpose.Template("getSuccess()")]
            get;
        }

        /// <summary>
        /// Gets a collection of all the captures matched by the capturing group, in innermost-leftmost-first order
        /// (or innermost-rightmost-first order if the regular expression is modified with the RegexOptions.RightToLeft option).
        /// The collection may have zero or more items.
        /// </summary>
        public extern CaptureCollection Captures
        {
            [Transpose.Template("getCaptures()")]
            get;
        }

        /// <summary>
        /// Returns a Group object equivalent to the one supplied that is safe to share between multiple threads.
        /// </summary>
        public static extern Group Synchronized(Group inner);
    }
}