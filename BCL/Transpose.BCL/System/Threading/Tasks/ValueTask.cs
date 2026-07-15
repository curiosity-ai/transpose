using System.Runtime.CompilerServices;

namespace System.Threading.Tasks
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
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

    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
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