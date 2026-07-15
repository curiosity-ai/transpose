namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class AsyncIteratorStateMachineAttribute : Attribute
    {
        public AsyncIteratorStateMachineAttribute(Type stateMachineType)
        {
        }
    }
}
