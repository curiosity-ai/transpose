namespace System.Threading.Tasks
{
    /// <summary>
    /// CommonJS Promise/A interface
    /// http://wiki.commonjs.org/wiki/Promises/A
    /// </summary>
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("Transpose.IPromise")]
    [Transpose.Convention(Target = Transpose.ConventionTarget.Member, Member = Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    public interface IPromise
    {
        /// <summary>
        /// Adds a fulfilledHandler, errorHandler to be called for completion of a promise.
        /// </summary>
        /// <param name="fulfilledHandler">The fulfilledHandler is called when the promise is fulfilled</param>
        /// <param name="errorHandler">The errorHandler is called when a promise fails.</param>
        /// <param name="progressHandler"></param>
        void Then(Delegate fulfilledHandler, Delegate errorHandler = null, Delegate progressHandler = null);
    }

    /// <summary>
    ///
    /// </summary>
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    public static class PromiseExtensions
    {
        /// <summary>
        ///
        /// </summary>
        /// <param name="promise"></param>
        /// <returns></returns>
        [Transpose.Template("System.Threading.Tasks.Task.fromPromise({promise})")]
        public static extern TaskAwaiter<object[]> GetAwaiter(this IPromise promise);

        /// <summary>
        /// Adapts a <see cref="Task"/> into a native JavaScript <c>Promise</c>, for handing to a
        /// JavaScript API that expects a thenable (a Monaco language provider, say). A faulted task
        /// rejects the promise and a cancelled one rejects with <c>TaskCanceledException</c>, so an
        /// exception in the C# body reaches the JavaScript caller instead of being lost.
        ///
        /// This is the same adapter the translator emits for <c>await</c>, surfaced for the reverse
        /// direction: <see cref="GetAwaiter(IPromise)"/> lets C# await a JS promise, this lets JS
        /// await a C# task.
        /// </summary>
        [Transpose.Template("Transpose.toPromise({task})")]
        public static extern IPromise ToPromise(this Task task);

        /// <summary>
        /// <see cref="ToPromise(Task)"/> for a task with a result; the result becomes the value the
        /// JavaScript <c>then</c> handler receives.
        /// </summary>
        [Transpose.Template("Transpose.toPromise({task})")]
        public static extern IPromise ToPromise<TResult>(this Task<TResult> task);
    }
}