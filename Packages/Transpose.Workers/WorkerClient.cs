using System;
using Transpose;

namespace Transpose.Workers
{
    /// <summary>
    /// One connected page, as the worker sees it.
    /// </summary>
    /// <remarks>
    /// A shared worker outlives any single page, so <see cref="Tag"/> is what makes it usable: the
    /// worker almost always has to know something about who is asking — which user is signed in on
    /// that tab, which document it is showing — before it can answer, and nothing about the
    /// connection itself tells it. Set it on the first message a page sends and every later handler
    /// has it.
    /// </remarks>
    public sealed class WorkerClient
    {
        private readonly IWorkerLink _link;
        private readonly WorkerHub   _hub;

        internal WorkerClient(WorkerHub hub, IWorkerLink link, int id)
        {
            _hub  = hub;
            _link = link;
            Id    = id;
        }

        /// <summary>A number identifying this connection for as long as it lasts. Not stable across
        /// reloads — a reloaded page is a new client.</summary>
        public int Id { get; }

        /// <summary>
        /// Whatever the worker wants to remember about this page. Untouched by the transport.
        /// </summary>
        /// <remarks>
        /// This is the hook the cross-user rule hangs off: a shared worker is shared by every page of
        /// an origin, including pages signed in as different people, so a worker holding per-user
        /// state must record whose page this is here and check it before sending anything back.
        /// Nothing else can do it — the browser keys a shared worker by origin, script and name, and
        /// knows nothing about your users.
        /// </remarks>
        public object Tag { get; set; }

        /// <summary>True until the page said goodbye or the worker dropped it.</summary>
        public bool IsOpen { get; internal set; } = true;

        /// <summary>Sends a message to this page alone.</summary>
        public void Send(string topic, string payload)
        {
            if (!IsOpen) return;

            _link.Post(new WorkerFrame { k = FrameKind.Message, t = topic, p = payload });
        }

        /// <summary>Sends <paramref name="payload"/> as JSON to this page alone. See
        /// <see cref="WorkerChannel.Send{T}"/> for what may be sent this way.</summary>
        public void Send<T>(string topic, T payload)
        {
            Send(topic, WorkerJson.Write(payload));
        }

        internal IWorkerLink Link { get { return _link; } }

        internal WorkerHub Hub { get { return _hub; } }
    }
}
