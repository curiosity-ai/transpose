using Transpose;
using System.Collections.Generic;

namespace System.Threading.Tasks
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    public class Task : IDisposable, Transpose.ITransposeClass, IAsyncResult
    {
        public extern Task(Action action);

        public extern Task(Action<object> action, object state);

        public extern AggregateException Exception
        {
            [Template("getException()")]
            get;
        }

        public extern bool IsCanceled
        {
            [Transpose.Template("isCanceled()")]
            get;
        }

        public extern bool IsCompleted
        {
            [Transpose.Template("isCompleted()")]
            get;
        }

        public extern bool IsFaulted
        {
            [Transpose.Template("isFaulted()")]
            get;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern TaskStatus Status
        {
            get;
        }

        public object AsyncState
        {
            get;
        }

        bool IAsyncResult.CompletedSynchronously
        {
            get;
        }

        public extern Task ContinueWith(Action<Task> continuationAction);

        public extern Task<TResult> ContinueWith<TResult>(Func<Task, TResult> continuationFunction);

        public extern void Start();

        public extern TaskAwaiter GetAwaiter();

        public extern void Dispose();

        public extern void Complete(object result = null);

        // Wait bound to `waitSync`, which observes an already-completed task the way .NET does -
        // throwing the AggregateException of a faulted or cancelled one - and answers false for a
        // task still running. [ToAwait] (h5's rewrite of the call into an `await`) is not
        // implemented by the Roslyn translator, so these were emitted as a plain `wait()` whose returned
        // Task nobody looked at, which threw nothing at all for a faulted task. Blocking until a
        // pending task completes remains impossible in a single-threaded runtime; observing one that
        // has already finished never needed to block.
        [Template("{this}.waitSync()")]
        public extern void Wait();

        [Name("wait")]
        public extern Task WaitTask();

        [Template("{this}.waitSync()")]
        public extern void Wait(CancellationToken cancellationToken);

        [Name("wait")]
        public extern Task WaitTask(CancellationToken cancellationToken);

        [Template("{this}.waitSync()")]
        public extern bool Wait(int millisecondsTimeout);

        [Name("waitt")]
        public extern Task<bool> WaitTask(int millisecondsTimeout);

        [Template("{this}.waitSync()")]
        public extern bool Wait(int millisecondsTimeout, CancellationToken cancellationToken);

        [Name("waitt")]
        public extern Task<bool> WaitTask(int millisecondsTimeout, CancellationToken cancellationToken);

        [Template("{this}.waitSync()")]
        public extern bool Wait(TimeSpan timeout);

        [Name("waitt")]
        public extern Task<bool> WaitTask(TimeSpan timeout);

        public static extern Task Delay(int millisecondDelay);

        public static extern Task Delay(int millisecondsDelay, CancellationToken cancellationToken);

        public static extern Task Delay(TimeSpan delay);

        public static extern Task Delay(TimeSpan delay, CancellationToken cancellationToken);

        /// <summary>
        /// Yields control back to the runtime, resuming asynchronously on the next scheduler tick.
        /// The awaited task completes after a <c>setImmediate</c> callback, so continuations run
        /// after the current job drains (the observable behaviour of <c>Task.Yield()</c>). Returns a
        /// <see cref="Task"/> rather than the BCL's <c>YieldAwaitable</c>, which is not modelled here;
        /// <c>await Task.Yield()</c> behaves identically.
        /// </summary>
        public static extern Task Yield();


        public static extern Task CompletedTask
        {
            [Transpose.Template("System.Threading.Tasks.Task.fromResult({}, null)")]
            get; 
        }

        [Transpose.Template("System.Threading.Tasks.Task.fromResult({result}, {TResult})")]
        public static extern Task<TResult> FromResult<TResult>(TResult result);

        [Transpose.Template("System.Threading.Tasks.Task.fromException({exception}, null)")]
        public static extern Task FromException(Exception exception);        

        [Transpose.Template("System.Threading.Tasks.Task.fromException({exception}, {TResult})")]
        public static extern Task<TResult> FromException<TResult>(Exception exception);        

        /// <summary>A task already in the cancelled state. The token must already be cancelled -
        /// there is nothing to cancel the task later - which is what .NET checks for too.</summary>
        public static extern Task FromCanceled(CancellationToken cancellationToken);

        [Transpose.Template("System.Threading.Tasks.Task.fromCanceled({cancellationToken}, {TResult})")]
        public static extern Task<TResult> FromCanceled<TResult>(CancellationToken cancellationToken);

        // All eight overloads bind to the one runtime `run`, the way Delay's four bind to `delay`:
        // it takes an optional token and unwraps a Task the body handed back, so Func<Task> and
        // Func<Task<TResult>> need no separate implementation. Declaring them matters anyway -
        // without them `Task.Run(async () => …)` bound to Action/Func<TResult> and its declared type
        // came out as Task<Task<TResult>>, and the CancellationToken forms did not compile at all.
        public static extern Task Run(Action action);

        public static extern Task Run(Action action, CancellationToken cancellationToken);

        public static extern Task<TResult> Run<TResult>(Func<TResult> function);

        public static extern Task<TResult> Run<TResult>(Func<TResult> function, CancellationToken cancellationToken);

        public static extern Task Run(Func<Task> function);

        public static extern Task Run(Func<Task> function, CancellationToken cancellationToken);

        public static extern Task<TResult> Run<TResult>(Func<Task<TResult>> function);

        public static extern Task<TResult> Run<TResult>(Func<Task<TResult>> function, CancellationToken cancellationToken);

        public static extern Task WhenAll(params Task[] tasks);

        public static extern Task WhenAll(IEnumerable<Task> tasks);

        public static extern Task<TResult[]> WhenAll<TResult>(params Task<TResult>[] tasks);

        public static extern Task<TResult[]> WhenAll<TResult>(IEnumerable<Task<TResult>> tasks);

        public static extern Task<Task> WhenAny(params Task[] tasks);

        public static extern Task<Task> WhenAny(IEnumerable<Task> tasks);

        public static extern Task<Task<TResult>> WhenAny<TResult>(params Task<TResult>[] tasks);

        public static extern Task<Task<TResult>> WhenAny<TResult>(IEnumerable<Task<TResult>> tasks);

        public static extern Task FromCallback(object target, string method, params object[] otherArguments);

        public static extern Task FromCallbackResult(object target, string method, Delegate resultHandler, params object[] otherArguments);

        public static extern Task<TResult> FromCallback<TResult>(object target, string method, params object[] otherArguments);

        public static extern Task<TResult> FromCallbackResult<TResult>(object target, string method, Delegate resultHandler, params object[] otherArguments);

        public static extern Task<object[]> FromPromise(IPromise promise);

        public static extern Task<TResult> FromPromise<TResult>(IPromise promise, Delegate resultHandler);

        public static extern Task<TResult> FromPromise<TResult>(IPromise promise, Delegate resultHandler, Delegate errorHandler);

        public static extern Task<TResult> FromPromise<TResult>(IPromise promise, Delegate resultHandler, Delegate errorHandler, Delegate progressHandler);
    }

    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    public class Task<TResult> : Task
    {
        public extern Task(Func<TResult> function);

        public extern Task(Func<object, TResult> function, object state);

        public extern TResult Result
        {
            [Transpose.Template("getResult()")]
            get;
        }

        public extern Task ContinueWith(Action<Task<TResult>> continuationAction);

        [Transpose.IgnoreGeneric]
        public extern Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult> continuationFunction);

        public new extern TaskAwaiter<TResult> GetAwaiter();

        public extern void SetResult(TResult result);
    }
}