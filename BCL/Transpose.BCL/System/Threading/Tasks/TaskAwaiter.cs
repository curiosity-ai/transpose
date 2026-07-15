using System.Runtime.CompilerServices;

namespace System.Threading.Tasks
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    public class TaskAwaiter : INotifyCompletion
    {
        internal extern TaskAwaiter();

        public extern bool IsCompleted
        {
            [Transpose.Template("isCompleted()")]
            get;
        }

        [Transpose.Name("continueWith")]
        public extern void OnCompleted(Action continuation);

        [Transpose.Name("getAwaitedResult")]
        public extern void GetResult();
    }

    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Threading.Tasks.Task")]
    public class TaskAwaiter<TResult> : INotifyCompletion
    {
        internal extern TaskAwaiter();

        public extern bool IsCompleted
        {
            [Transpose.Template("isCompleted()")]
            get;
        }

        [Transpose.Name("continueWith")]
        public extern void OnCompleted(Action continuation);

        [Transpose.Name("getAwaitedResult")]
        public extern TResult GetResult();
    }
}