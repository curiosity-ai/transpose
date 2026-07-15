namespace System.Diagnostics.Contracts
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    public sealed class ContractException : Exception
    {
        public extern ContractFailureKind Kind
        {
            get;
        }

        public extern string Failure
        {
            get;
        }

        public extern string UserMessage
        {
            get;
        }

        public extern string Condition
        {
            get;
        }

        public extern ContractException(ContractFailureKind kind, string failure, string userMessage, string condition, Exception innerException);
    }
}