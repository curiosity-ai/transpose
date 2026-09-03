using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Transpose;
using Transpose.Core;

namespace Transpose.Workers
{
    /// <summary>
    /// A page's connection to a shared worker — and, where the browser has none, to a hub running in
    /// this page instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point of a shared worker is that one of it serves every tab: one connection to your
    /// server instead of one per tab, and one copy of whatever state it holds. This is the page's
    /// end of that.
    /// </para>
    /// <para>
    /// Callers do not branch on whether the browser supports shared workers. Where it does not, the
    /// same hub runs inside this page and the same code talks to it — one connection per tab rather
    /// than per browser, which is what an application did before shared workers were an option.
    /// <see cref="IsShared"/> says which happened, for a caller that wants to report it.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var live = WorkerChannel.Connect("live", LiveWorker.Main);
    ///
    /// live.On("frame", json =&gt; Repaint(json));
    /// live.Send("hello", CurrentUser.Uid);
    ///
    /// var when = await live.RequestAsync("time", null);
    /// </code>
    /// </example>
    public sealed class WorkerChannel : IDisposable
    {
        private readonly Dictionary<string, List<Action<string>>>            _handlers = new Dictionary<string, List<Action<string>>>();
        private readonly Dictionary<string, TaskCompletionSource<string>>    _pending  = new Dictionary<string, TaskCompletionSource<string>>();

        private IWorkerLink _link;
        private int         _nextRequest = 1;
        private bool        _closed;

        private WorkerChannel(IWorkerLink link, bool isShared, string name)
        {
            _link    = link;
            IsShared = isShared;
            Name     = name;

            link.Received = Receive;

            // Nothing in the platform tells a shared worker that a page has closed, so without this
            // a worker holding per-page state accumulates dead clients for the life of the browser
            // session. pagehide rather than unload: unload does not fire on mobile Safari, and it
            // blocks the back/forward cache everywhere.
            if (!Script.Write<bool>("Transpose.isWorkerScope"))
            {
                dom.window.addEventListener("pagehide", (dom.Event e) => Dispose());
            }
        }

        /// <summary>The worker's name, as connected.</summary>
        public string Name { get; }

        /// <summary>
        /// True when this really is a shared worker — one per browser. False when the browser has
        /// none and the hub is running in this page, so every tab has its own.
        /// </summary>
        public bool IsShared { get; }

        /// <summary>
        /// Connects to the shared worker <paramref name="name"/>, falling back to a hub in this page.
        /// </summary>
        /// <param name="name">
        /// The worker's name — the same string the <c>[SharedWorkerEntry]</c> attribute carries. The
        /// script URL is derived from it the way the compiler names the file it emits
        /// (<c>&lt;name&gt;.worker.js</c>), so the two cannot drift apart.
        /// </param>
        /// <param name="entryPoint">
        /// The <c>[SharedWorkerEntry]</c> method itself, used only for the fallback: with no shared
        /// worker to run it in, this page runs it. Passing it explicitly is what keeps one
        /// implementation of the hub serving both paths. Omit it to have no fallback, in which case
        /// a browser without shared workers throws instead.
        /// </param>
        /// <param name="scriptUrl">Overrides the derived script URL, for a site that serves the
        /// worker from somewhere other than beside its bundle.</param>
        public static WorkerChannel Connect(string name, Action entryPoint = null, string scriptUrl = null)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A worker needs a name.", nameof(name));

            if (SharedWorkers.IsSupported)
            {
                var url = scriptUrl ?? (name + ".worker.js");

                try
                {
                    // The name is part of a shared worker's identity, so passing it is what makes
                    // every tab asking for this name join the same instance.
                    var worker = new dom.SharedWorker(url, name);

                    return new WorkerChannel(new PortLink(worker.port), isShared: true, name: name);
                }
                catch (Exception)
                {
                    // Construction can fail where the global exists anyway -- a blocked worker, a
                    // context that forbids one. There is a working answer available, so take it
                    // rather than failing the page.
                    if (entryPoint == null) throw;
                }
            }

            if (entryPoint == null)
            {
                throw new NotSupportedException(
                    "This browser has no SharedWorker, and Connect was given no entry point to run in "
                  + "the page instead. Pass the [SharedWorkerEntry] method as `entryPoint`.");
            }

            return InPage(name, entryPoint);
        }

        /// <summary>
        /// The per-tab fallback: run the worker's own entry point here, then connect to the hub it
        /// started without a port in between.
        /// </summary>
        private static WorkerChannel InPage(string name, Action entryPoint)
        {
            // The entry calls SharedWorkerHost.Run, which is idempotent per name -- so a second
            // channel for the same worker joins the hub the first one started rather than building
            // another.
            entryPoint();

            var hub = SharedWorkerHost.Find(name);

            if (hub == null)
            {
                throw new InvalidOperationException(
                    "The entry point for '" + name + "' did not start a hub. A [SharedWorkerEntry] "
                  + "method has to call SharedWorkerHost.Run with the same name for the in-page "
                  + "fallback to have anything to connect to.");
            }

            IWorkerLink pageEnd, hubEnd;

            LoopLink.Pair(out pageEnd, out hubEnd);

            var channel = new WorkerChannel(pageEnd, isShared: false, name: name);

            hub.Accept(hubEnd);

            return channel;
        }

        // ------------------------------------------------------------------ sending

        /// <summary>Sends a fire-and-forget message.</summary>
        public void Send(string topic, string payload)
        {
            if (_closed) return;

            _link.Post(new WorkerFrame { k = FrameKind.Message, t = topic, p = payload });
        }

        /// <summary>
        /// Sends <paramref name="payload"/> as JSON.
        /// </summary>
        /// <remarks>
        /// Serialized with the browser's own <c>JSON</c>, so this carries <em>data</em>: the worker
        /// reads back an object with the same fields and no prototype. That is what a message payload
        /// almost always is. For anything that has to come back as a real instance of a class, send a
        /// string and use your own serializer — which is what the string overloads are for.
        /// </remarks>
        public void Send<T>(string topic, T payload)
        {
            Send(topic, WorkerJson.Write(payload));
        }

        /// <summary>
        /// Asks the worker something and waits for its answer.
        /// </summary>
        /// <remarks>
        /// The task faults if the worker has no handler for the topic, if its handler throws, or if
        /// the channel is disposed before the answer arrives. It has no timeout of its own: a shared
        /// worker either answers or has gone, and inventing a deadline here would hide which.
        /// </remarks>
        public Task<string> RequestAsync(string topic, string payload)
        {
            var tcs = new TaskCompletionSource<string>();

            if (_closed)
            {
                tcs.SetException(new InvalidOperationException("The channel to '" + Name + "' is closed."));
                return tcs.Task;
            }

            var id = (_nextRequest++).ToString();

            _pending[id] = tcs;
            _link.Post(new WorkerFrame { k = FrameKind.Request, t = topic, id = id, p = payload });

            return tcs.Task;
        }

        /// <summary>Asks the worker something, with both payload and reply as JSON.</summary>
        public async Task<TReply> RequestAsync<TAsk, TReply>(string topic, TAsk payload)
        {
            var reply = await RequestAsync(topic, WorkerJson.Write(payload));

            return WorkerJson.Read<TReply>(reply);
        }

        // ------------------------------------------------------------------ receiving

        /// <summary>
        /// Handles messages the worker sends on <paramref name="topic"/>. Dispose the result to stop.
        /// </summary>
        /// <remarks>
        /// Several handlers may share a topic, and each is called. A handler is not removed when it
        /// throws — one screen's broken repaint must not silence every other subscriber.
        /// </remarks>
        public IDisposable On(string topic, Action<string> handler)
        {
            List<Action<string>> list;

            if (!_handlers.TryGetValue(topic, out list))
            {
                list = new List<Action<string>>();
                _handlers[topic] = list;
            }

            list.Add(handler);

            return new Subscription(this, topic, handler);
        }

        /// <summary>Handles messages on <paramref name="topic"/> whose payload is JSON.</summary>
        public IDisposable On<T>(string topic, Action<T> handler)
        {
            return On(topic, payload => handler(WorkerJson.Read<T>(payload)));
        }

        private void Receive(WorkerFrame frame)
        {
            if (frame == null || _closed) return;

            if (frame.k == FrameKind.Reply || frame.k == FrameKind.Fail)
            {
                TaskCompletionSource<string> tcs;

                if (frame.id == null || !_pending.TryGetValue(frame.id, out tcs)) return;

                _pending.Remove(frame.id);

                if (frame.k == FrameKind.Fail) tcs.TrySetException(new WorkerRequestException(frame.e));
                else                           tcs.TrySetResult(frame.p);

                return;
            }

            if (frame.k != FrameKind.Message || frame.t == null) return;

            List<Action<string>> handlers;

            if (!_handlers.TryGetValue(frame.t, out handlers)) return;

            // Copied: a handler may subscribe or unsubscribe while the message is being delivered.
            var snapshot = handlers.ToArray();

            foreach (var handler in snapshot) handler(frame.p);
        }

        // ------------------------------------------------------------------ closing

        /// <summary>
        /// Tells the worker this page is going, drops the port, and faults anything still awaited.
        /// </summary>
        /// <remarks>
        /// The goodbye matters: nothing in the platform tells a shared worker that a page has closed,
        /// so a worker holding per-page state would accumulate dead clients without it. It is
        /// best-effort — a tab that is killed outright sends nothing — which is why a worker that
        /// must not leak should also expire a client that has gone quiet.
        /// </remarks>
        public void Dispose()
        {
            if (_closed) return;

            _closed = true;

            try
            {
                _link.Post(new WorkerFrame { k = FrameKind.Bye });
            }
            catch (Exception)
            {
                // Already gone; there is nothing to tell it.
            }

            var portLink = _link as PortLink;

            if (portLink != null) portLink.Close();

            _handlers.Clear();

            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(new InvalidOperationException(
                    "The channel to '" + Name + "' was closed before the worker answered."));
            }

            _pending.Clear();
        }

        private sealed class Subscription : IDisposable
        {
            private readonly WorkerChannel   _channel;
            private readonly string          _topic;
            private readonly Action<string>  _handler;

            public Subscription(WorkerChannel channel, string topic, Action<string> handler)
            {
                _channel = channel;
                _topic   = topic;
                _handler = handler;
            }

            public void Dispose()
            {
                List<Action<string>> list;

                if (_channel._handlers.TryGetValue(_topic, out list)) list.Remove(_handler);
            }
        }
    }

    /// <summary>Thrown by <see cref="WorkerChannel.RequestAsync(string, string)"/> when the worker
    /// reported a failure instead of an answer.</summary>
    public sealed class WorkerRequestException : Exception
    {
        public WorkerRequestException(string message) : base(message ?? "The worker failed to answer.") { }
    }
}
