namespace System.Threading
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    public class CancellationTokenSource : IDisposable
    {
        public extern CancellationTokenSource();

        public extern CancellationTokenSource(int millisecondsDelay);

        [Transpose.Template("new System.Threading.CancellationTokenSource({delay}.ticks / 10000)")]
        public extern CancellationTokenSource(TimeSpan delay);

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern bool IsCancellationRequested
        {
            get;
            private set;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern CancellationToken Token
        {
            get;
            private set;
        }

        public extern void Cancel();

        public extern void Cancel(bool throwOnFirstException);

        public extern void CancelAfter(int millisecondsDelay);

        [Transpose.Template("{this}.cancelAfter({delay}.ticks / 10000)")]
        public extern void CancelAfter(TimeSpan delay);

        public extern void Dispose();

        [Transpose.Name("createLinked")]
        public static extern CancellationTokenSource CreateLinkedTokenSource(CancellationToken token1, CancellationToken token2);

        [Transpose.Template("System.Threading.CancellationTokenSource.createLinked({*tokens})")]
        public static extern CancellationTokenSource CreateLinkedTokenSource(params CancellationToken[] tokens);
    }
}