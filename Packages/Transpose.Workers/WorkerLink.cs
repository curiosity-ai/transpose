using System;
using Transpose;
using Transpose.Core;

namespace Transpose.Workers
{
    /// <summary>
    /// One end of a two-way pipe carrying <see cref="WorkerFrame"/>s.
    /// </summary>
    /// <remarks>
    /// The abstraction exists for exactly one reason: a browser without <c>SharedWorker</c> runs the
    /// worker's hub inside the page instead, and the hub must not be able to tell. With this in the
    /// way, <see cref="WorkerHub"/> and <see cref="WorkerChannel"/> are written once and work over a
    /// real <see cref="dom.MessagePort"/> or over a pair of in-process queues.
    /// </remarks>
    internal interface IWorkerLink
    {
        /// <summary>Sends one frame to the other end.</summary>
        void Post(WorkerFrame frame);

        /// <summary>Called for every frame arriving from the other end.</summary>
        Action<WorkerFrame> Received { get; set; }
    }

    /// <summary>An <see cref="IWorkerLink"/> over a real <see cref="dom.MessagePort"/>.</summary>
    internal sealed class PortLink : IWorkerLink
    {
        private readonly dom.MessagePort _port;
        private Action<WorkerFrame>      _received;

        public PortLink(dom.MessagePort port)
        {
            _port = port;

            // Assigning onmessage starts the port implicitly, but start() is called anyway: a port
            // delivers nothing until it is started, and this is the single most common way a first
            // shared-worker integration silently receives nothing.
            _port.onmessage = e =>
            {
                var handler = _received;

                if (handler != null && e.data != null) handler(e.data.As<WorkerFrame>());
            };
            _port.start();
        }

        public Action<WorkerFrame> Received
        {
            get { return _received; }
            set { _received = value; }
        }

        public void Post(WorkerFrame frame)
        {
            _port.postMessage(frame);
        }

        /// <summary>Drops the port. Only the page end does this; a worker keeps its ports.</summary>
        public void Close()
        {
            _received = null;

            try
            {
                _port.close();
            }
            catch (Exception)
            {
                // A port whose other end has already gone throws on close in some browsers, and
                // there is nothing to recover: it is closed either way.
            }
        }
    }

    /// <summary>
    /// A pair of <see cref="IWorkerLink"/>s wired to each other in one JavaScript context — the
    /// per-tab fallback, where the page is also the worker.
    /// </summary>
    /// <remarks>
    /// Delivery is deferred with <c>setTimeout(0)</c> rather than immediate. A real port is
    /// asynchronous, and code that works over one must not start working differently here just
    /// because the other end happens to be in the same context: a hub that answered a request
    /// synchronously would let a caller observe its reply before <c>RequestAsync</c> had returned its
    /// task.
    /// </remarks>
    internal sealed class LoopLink : IWorkerLink
    {
        private LoopLink            _peer;
        private Action<WorkerFrame> _received;

        public static void Pair(out IWorkerLink a, out IWorkerLink b)
        {
            var left  = new LoopLink();
            var right = new LoopLink();

            left._peer  = right;
            right._peer = left;

            a = left;
            b = right;
        }

        public Action<WorkerFrame> Received
        {
            get { return _received; }
            set { _received = value; }
        }

        public void Post(WorkerFrame frame)
        {
            var peer = _peer;

            if (peer == null) return;

            dom.window.setTimeout(_ =>
            {
                var handler = peer._received;

                if (handler != null) handler(frame);
            }, 0);
        }
    }
}
