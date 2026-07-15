namespace System.Threading
{
    public sealed class Lock
    {
        public void Enter() { }
        public void Exit() { }
        public Scope EnterScope() => new Scope();

        public ref struct Scope
        {
            [H5.Name("System$IDisposable$Dispose")]
            public void Dispose() { }
        }
    }
}
