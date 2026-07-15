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
    }
}