namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    public class RegexMatchTimeoutException : TimeoutException
    {
        public extern string Pattern
        {
            [Transpose.Template("getPattern()")]
            get;
        }

        public extern string Input
        {
            [Transpose.Template("getInput()")]
            get;
        }

        public extern TimeSpan MatchTimeout
        {
            [Transpose.Template("getMatchTimeout()")]
            get;
        }

        public extern RegexMatchTimeoutException();

        public extern RegexMatchTimeoutException(string message);

        public extern RegexMatchTimeoutException(string message, Exception innerException);

        public extern RegexMatchTimeoutException(string regexInput, string regexPattern, TimeSpan matchTimeout);
    }
}