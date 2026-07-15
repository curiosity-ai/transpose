namespace System.Diagnostics.Contracts
{
    /// <summary>
    /// Allows a field f to be used in the method contracts for a method m when f has less visibility than m.
    /// For instance, if the method is public, but the field is private.
    /// </summary>
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Conditional("CONTRACTS_FULL")]
    [AttributeUsage(AttributeTargets.Field)]
    [Transpose.External]
    public sealed class ContractPublicPropertyNameAttribute : Attribute
    {
        public extern ContractPublicPropertyNameAttribute(string name);

        public extern string Name
        {
            get;
            private set;
        }
    }
}