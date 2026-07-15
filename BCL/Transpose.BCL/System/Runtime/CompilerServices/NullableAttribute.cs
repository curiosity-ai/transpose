namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Parameter | AttributeTargets.ReturnValue | AttributeTargets.GenericParameter, Inherited = false, AllowMultiple = false)]
    public sealed class NullableAttribute : Attribute
    {
        public NullableAttribute(byte flag)
        {
        }

        public NullableAttribute(byte[] flags)
        {
        }
    }
}
