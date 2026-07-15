namespace System.Diagnostics.Contracts
{
    [Transpose.Enum(Transpose.Emit.Name)]
    [Transpose.External]
    public enum ContractFailureKind
    {
        Precondition,
        Postcondition,
        PostconditionOnException,
        Invariant,
        Assert,
        Assume,
    }
}