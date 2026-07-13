using System.Runtime.CompilerServices;

namespace System.Threading.Tasks
{
    [H5.Convention(Member = H5.ConventionMember.Field | H5.ConventionMember.Method, Notation = H5.Notation.CamelCase)]
    [H5.External]
    [H5.Reflectable]
    [AsyncMethodBuilder(typeof(AsyncValueTaskMethodBuilder))]
    public readonly struct ValueTask
    {
        public ValueTask(bool completedSynchronously)
        {
            throw new NotImplementedException();
        }

        public ValueTask(Task task)
        {
            throw new NotImplementedException();
        }

        public static ValueTask Completed() => throw new NotImplementedException();

        public TaskAwaiter GetAwaiter() => throw new NotImplementedException();
    }

    [H5.Convention(Member = H5.ConventionMember.Field | H5.ConventionMember.Method, Notation = H5.Notation.CamelCase)]
    [H5.External]
    [H5.Reflectable]
    [AsyncMethodBuilder(typeof(AsyncValueTaskMethodBuilder<>))]
    public readonly struct ValueTask<TResult>
    {
        public ValueTask(TResult result)
        {
            throw new NotImplementedException();
        }

        public ValueTask(Task<TResult> task)
        {
            throw new NotImplementedException();
        }

        public TaskAwaiter<TResult> GetAwaiter() => throw new NotImplementedException();
    }
}

namespace System.Runtime.CompilerServices
{
    using System.Threading.Tasks;

    // Async method builders for ValueTask: required by Roslyn to accept
    // `async ValueTask` methods when compiling the metadata assembly. The H5
    // emitter lowers ValueTask-returning async methods to Task-based JS, so
    // these builders are never used at runtime.
    [H5.Convention(Member = H5.ConventionMember.Field | H5.ConventionMember.Method, Notation = H5.Notation.CamelCase)]
    [H5.External]
    [H5.NonScriptable]
    public struct AsyncValueTaskMethodBuilder
    {
        public extern ValueTask Task
        {
            get;
        }

        public static extern AsyncValueTaskMethodBuilder Create();

        public extern void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine;

        public extern void SetStateMachine(IAsyncStateMachine stateMachine);

        public extern void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine;

        public extern void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine;

        public extern void SetResult();

        public extern void SetException(Exception exception);
    }

    [H5.Convention(Member = H5.ConventionMember.Field | H5.ConventionMember.Method, Notation = H5.Notation.CamelCase)]
    [H5.External]
    [H5.NonScriptable]
    public struct AsyncValueTaskMethodBuilder<TResult>
    {
        public extern ValueTask<TResult> Task
        {
            get;
        }

        public static extern AsyncValueTaskMethodBuilder<TResult> Create();

        public extern void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine;

        public extern void SetStateMachine(IAsyncStateMachine stateMachine);

        public extern void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine;

        public extern void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine;

        public extern void SetResult(TResult result);

        public extern void SetException(Exception exception);
    }
}