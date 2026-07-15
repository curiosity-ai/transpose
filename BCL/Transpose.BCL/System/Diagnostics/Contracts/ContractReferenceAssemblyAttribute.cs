namespace System.Diagnostics.Contracts
{
    /// <summary>
    /// Attribute that specifies that an assembly is a reference assembly with contracts.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    [Transpose.External]
    public sealed class ContractReferenceAssemblyAttribute : Attribute
    {
    }
}