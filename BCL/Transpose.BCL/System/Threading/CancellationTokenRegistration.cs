namespace System.Threading
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    public struct CancellationTokenRegistration : IEquatable<CancellationTokenRegistration>, IDisposable
    {
        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern bool Equals(CancellationTokenRegistration other);

        public extern void Dispose();

        [Transpose.Template("Transpose.equals({left}, {right})")]
        public static extern bool operator ==(CancellationTokenRegistration left, CancellationTokenRegistration right);

        [Transpose.Template("!Transpose.equals({left}, {right})")]
        public static extern bool operator !=(CancellationTokenRegistration left, CancellationTokenRegistration right);
    }
}