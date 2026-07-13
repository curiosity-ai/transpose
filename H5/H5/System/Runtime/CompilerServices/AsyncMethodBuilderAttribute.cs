namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Indicates the async method builder for a task-like return type. Required by
    /// Roslyn to accept `async ValueTask` methods when building the metadata
    /// assembly; the H5 emitter lowers ValueTask-returning async methods to
    /// Task-based JS, so the builder itself is never used at runtime.
    /// </summary>
    [H5.NonScriptable]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Delegate | AttributeTargets.Enum | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class AsyncMethodBuilderAttribute : Attribute
    {
        public AsyncMethodBuilderAttribute(Type builderType)
        {
            BuilderType = builderType;
        }

        public Type BuilderType { get; }
    }
}
