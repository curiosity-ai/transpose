namespace System.Threading
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    public struct CancellationToken
    {
        public extern CancellationToken(bool canceled);

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public static extern CancellationToken None
        {
            get;
        }

        public extern bool CanBeCanceled
        {
            [Transpose.Template("getCanBeCanceled()")]
            get;
        }

        public extern bool IsCancellationRequested
        {
            [Transpose.Template("getIsCancellationRequested()")]
            get;
        }

        public extern void ThrowIfCancellationRequested();

        public extern CancellationTokenRegistration Register(Action callback);

        [Transpose.Template("{this}.register({callback})")]
        public extern CancellationTokenRegistration Register(Action callback, bool useSynchronizationContext);

        public extern CancellationTokenRegistration Register(Action<object> callback, object state);

        [Transpose.Template("{this}.register({callback}, {state})")]
        public extern CancellationTokenRegistration Register(Action<object> callback, object state, bool useSynchronizationContext);
    }
}