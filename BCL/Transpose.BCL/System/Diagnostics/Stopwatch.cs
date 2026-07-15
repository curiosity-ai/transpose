namespace System.Diagnostics
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    public class Stopwatch
    {
        public static readonly long Frequency = 0;
        public static readonly bool IsHighResolution = false;

        public static extern Stopwatch StartNew();

        public extern TimeSpan Elapsed
        {
            [Transpose.Template("timeSpan()")]
            get;
        }

        public extern long ElapsedMilliseconds
        {
            [Transpose.Template("milliseconds()")]
            get;
        }

        public extern long ElapsedTicks
        {
            [Transpose.Template("ticks()")]
            get;
        }

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern bool IsRunning
        {
            get;
        }

        public extern void Reset();

        public extern void Start();

        public extern void Stop();

        public extern void Restart();

        public extern static long GetTimestamp();
    }
}