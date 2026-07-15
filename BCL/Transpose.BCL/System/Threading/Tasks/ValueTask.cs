using System.Runtime.CompilerServices;

namespace System.Threading.Tasks
{
    [H5.Convention(Member = H5.ConventionMember.Field | H5.ConventionMember.Method, Notation = H5.Notation.CamelCase)]
    [H5.External]
    [H5.Reflectable]
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