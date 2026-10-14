namespace System.Diagnostics.Contracts
{
    // A Name mode emits every reference as a path, so the object has to exist: this one's
    // Transpose.define is hand-written in Resources/Contract.js.
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