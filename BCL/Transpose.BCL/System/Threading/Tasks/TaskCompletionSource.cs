using System.Collections.Generic;

namespace System.Threading.Tasks
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.IgnoreGeneric]
    [Transpose.Name("System.Threading.Tasks.TaskCompletionSource")]
    [Transpose.Reflectable]
    public class TaskCompletionSource<TResult>
    {
        public extern TaskCompletionSource();
        public extern TaskCompletionSource(object state);

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern Task<TResult> Task
        {
            get;
        }

        public extern void SetCanceled();

        public extern void SetException(IEnumerable<Exception> exceptions);

        public extern void SetException(Exception exception);

        public extern void SetResult(TResult result);

        public extern bool TrySetCanceled();

        public extern bool TrySetException(IEnumerable<Exception> exceptions);

        public extern bool TrySetException(Exception exception);

        public extern bool TrySetResult(TResult result);
    }
}