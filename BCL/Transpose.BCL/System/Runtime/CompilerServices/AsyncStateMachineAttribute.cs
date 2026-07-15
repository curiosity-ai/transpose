namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class AsyncStateMachineAttribute : Attribute
    {
        public AsyncStateMachineAttribute(Type stateMachineType)
        {
        }
    }
}
