namespace System.Diagnostics.Contracts
{
    /// <summary>
    /// Types marked with this attribute specify that a separate type contains the contracts for this type.
    /// </summary>
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Conditional("CONTRACTS_FULL")]
    [Conditional("DEBUG")]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Delegate, AllowMultiple = false, Inherited = false)]
    [Transpose.External]
    public sealed class ContractClassAttribute : Attribute
    {
        public extern ContractClassAttribute(Type typeContainingContracts);

        public extern Type TypeContainingContracts
        {
            get;
            private set;
        }
    }
}