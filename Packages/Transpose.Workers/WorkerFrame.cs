using Transpose;

namespace Transpose.Workers
{
    /// <summary>
    /// One message on the wire between a page and a worker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An <see cref="ObjectLiteralAttribute"/> type, so it emits as a plain JavaScript object and
    /// <c>postMessage</c> carries it directly through the structured clone algorithm. That is why
    /// there is no text protocol here to parse and mis-parse: the browser does the framing.
    /// </para>
    /// <para>
    /// The field names are single letters because every message pays for them. A frame is posted per
    /// event, and structured clone copies the keys as well as the values.
    /// </para>
    /// </remarks>
    [ObjectLiteral]
    internal class WorkerFrame
    {
        /// <summary>What this frame is — see <see cref="FrameKind"/>.</summary>
        public string k;

        /// <summary>The topic, for everything except a reply (a reply is matched by <see cref="id"/>).</summary>
        public string t;

        /// <summary>Correlates a request with its reply. Absent on a plain message.</summary>
        public string id;

        /// <summary>The payload, as the caller supplied it: a string, or JSON for a typed send.</summary>
        public string p;

        /// <summary>Set on <see cref="FrameKind.Fail"/> instead of <see cref="p"/>.</summary>
        public string e;
    }

    /// <summary>The <see cref="WorkerFrame.k"/> values, kept in one place so both ends agree.</summary>
    internal static class FrameKind
    {
        /// <summary>Fire and forget, in either direction.</summary>
        public const string Message = "m";

        /// <summary>A request expecting exactly one <see cref="Reply"/> or <see cref="Fail"/>.</summary>
        public const string Request = "q";

        /// <summary>The answer to a request.</summary>
        public const string Reply = "r";

        /// <summary>A request the other end could not answer; <see cref="WorkerFrame.e"/> says why.</summary>
        public const string Fail = "f";

        /// <summary>A page telling the worker it is going away, so the worker can forget its client
        /// without waiting to notice. Nothing in the platform reports a closed tab.</summary>
        public const string Bye = "b";
    }
}
