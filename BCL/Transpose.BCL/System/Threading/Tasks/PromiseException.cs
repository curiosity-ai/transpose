namespace System.Threading.Tasks
{
    /// <summary>
    /// This exception is used as the exception for a task created from a promise when the underlying promise fails.
    /// </summary>
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Namespace("Transpose")]
    public class PromiseException : Exception
    {
        public extern PromiseException(object[] arguments);

        public extern PromiseException(object[] arguments, string message);

        public extern PromiseException(object[] arguments, string message, Exception innerException);

        /// <summary>
        /// Arguments supplied to the promise onError() callback.
        /// </summary>
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern object[] Arguments
        {
            get;
        }
    }
}