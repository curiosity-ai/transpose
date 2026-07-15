namespace System.Text.RegularExpressions
{
    /// <summary>
    /// Represents the results from a single successful subexpression capture.
    /// </summary>
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    public class Capture
    {
        internal extern Capture(string text, int i, int l);

        /// <summary>
        /// The position in the original string where the first character of the captured substring is found.
        /// </summary>
        public extern int Index
        {
            [Transpose.Template("getIndex()")]
            get;
        }

        /// <summary>
        /// Gets the length of the captured substring.
        /// </summary>
        public extern int Length
        {
            [Transpose.Template("getLength()")]
            get;
        }

        /// <summary>
        /// Gets the captured substring from the input string.
        /// </summary>
        public extern string Value
        {
            [Transpose.Template("getValue()")]
            get;
        }

        /// <summary>
        /// Retrieves the captured substring from the input string by calling the Value property. (Overrides Object.ToString().)
        /// </summary>
        public extern override string ToString();
    }
}