using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Transpose;

namespace Transpose.Workers
{
    /// <summary>
    /// The worker's side of the conversation: what it answers, and who is connected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A hub is configured once, when the worker starts, and then serves every page that connects —
    /// so a handler must be written for a shared worker rather than for one page. It runs once per
    /// browser, not once per tab, and outlives any page that talks to it.
    /// </para>
    /// <para>
    /// The same hub also serves the per-tab fallback, where it runs inside the page instead of a
    /// worker (see <see cref="SharedWorkerHost.Run"/>). That is the point of routing everything
    /// through <see cref="IWorkerLink"/>: there is one implementation of the hub, so the fallback
    /// cannot answer differently from the real worker.
    /// </para>
    /// </remarks>
    public sealed class WorkerHub
    {
        private readonly Dictionary<string, Action<WorkerClient, string>>       _messages = new Dictionary<string, Action<WorkerClient, string>>();
        private readonly Dictionary<string, Func<WorkerClient, string, Task<string>>> _requests = new Dictionary<string, Func<WorkerClient, string, Task<string>>>();
        private readonly List<WorkerClient>                                    _clients  = new List<WorkerClient>();

        private Action<WorkerClient> _connected;
        private Action<WorkerClient> _gone;
        private int                  _nextId = 1;

        internal WorkerHub() { }

        /// <summary>Every page currently connected, in the order they arrived.</summary>
        /// <remarks>A snapshot: a page may go away while it is being walked, and a page that has
        /// gone is dropped from the hub's own list rather than from this copy.</remarks>
        public IReadOnlyList<WorkerClient> Clients { get { return _clients.ToArray(); } }

        /// <summary>Handles a fire-and-forget message on <paramref name="topic"/>.</summary>
        public WorkerHub On(string topic, Action<WorkerClient, string> handler)
        {
            _messages[topic] = handler;
            return this;
        }

        /// <summary>Handles a message whose payload is JSON.</summary>
        public WorkerHub On<T>(string topic, Action<WorkerClient, T> handler)
        {
            _messages[topic] = (client, payload) => handler(client, WorkerJson.Read<T>(payload));
            return this;
        }

        /// <summary>
        /// Answers a request on <paramref name="topic"/>. Whatever the task completes with is sent
        /// back to the page that asked; if it faults, the page's <c>RequestAsync</c> faults with the
        /// same message.
        /// </summary>
        public WorkerHub OnRequest(string topic, Func<WorkerClient, string, Task<string>> handler)
        {
            _requests[topic] = handler;
            return this;
        }

        /// <summary>Answers a request whose payload and reply are both JSON.</summary>
        public WorkerHub OnRequest<TAsk, TReply>(string topic, Func<WorkerClient, TAsk, Task<TReply>> handler)
        {
            _requests[topic] = async (client, payload) =>
            {
                var reply = await handler(client, WorkerJson.Read<TAsk>(payload));
                return WorkerJson.Write(reply);
            };
            return this;
        }

        /// <summary>Called for each page that connects.</summary>
        public WorkerHub OnConnect(Action<WorkerClient> handler)
        {
            _connected = handler;
            return this;
        }

        /// <summary>
        /// Called when a page goes away — but only when it said so.
        /// </summary>
        /// <remarks>
        /// Nothing in the platform reports a closed tab to a shared worker: a gone page's port simply
        /// stops answering. <see cref="WorkerChannel"/> therefore sends a goodbye on <c>pagehide</c>,
        /// which is what makes this fire in practice. A tab killed outright (a crash, a force quit)
        /// sends nothing, so a worker that must not leak per-page state should also treat a long
        /// silence as gone.
        /// </remarks>
        public WorkerHub OnDisconnect(Action<WorkerClient> handler)
        {
            _gone = handler;
            return this;
        }

        /// <summary>Sends a message to every connected page.</summary>
        /// <remarks>
        /// Every page of the origin, which on a shared worker can mean pages signed in as different
        /// people. Where that matters, walk <see cref="Clients"/> and check each one's
        /// <see cref="WorkerClient.Tag"/> instead of broadcasting.
        /// </remarks>
        public void Broadcast(string topic, string payload)
        {
            // Copied first: a handler reached from here may drop a client, and the list is the hub's.
            var clients = _clients.ToArray();

            foreach (var client in clients) client.Send(topic, payload);
        }

        /// <summary>Sends <paramref name="payload"/> as JSON to every connected page.</summary>
        public void Broadcast<T>(string topic, T payload)
        {
            Broadcast(topic, WorkerJson.Write(payload));
        }

        /// <summary>Sends a message to every connected page whose <see cref="WorkerClient.Tag"/>
        /// matches <paramref name="predicate"/> — the per-user fan-out.</summary>
        public void BroadcastTo(Func<WorkerClient, bool> predicate, string topic, string payload)
        {
            var clients = _clients.ToArray();

            foreach (var client in clients)
            {
                if (predicate(client)) client.Send(topic, payload);
            }
        }

        // ------------------------------------------------------------------ plumbing

        /// <summary>Takes on a newly connected page.</summary>
        internal WorkerClient Accept(IWorkerLink link)
        {
            var client = new WorkerClient(this, link, _nextId++);

            _clients.Add(client);

            link.Received = frame => Dispatch(client, frame);

            if (_connected != null) _connected(client);

            return client;
        }

        private void Dispatch(WorkerClient client, WorkerFrame frame)
        {
            if (frame == null || !client.IsOpen) return;

            if (frame.k == FrameKind.Bye)
            {
                Drop(client);
                return;
            }

            if (frame.k == FrameKind.Message)
            {
                Action<WorkerClient, string> handler;

                // An unhandled topic is ignored rather than an error: a page built against a newer
                // version of the worker will send topics this one has never heard of, and a shared
                // worker outlives the page that started it -- so the old worker is the one still
                // running after a deploy, until every tab has gone.
                if (_messages.TryGetValue(frame.t, out handler)) handler(client, frame.p);

                return;
            }

            if (frame.k == FrameKind.Request)
            {
                Answer(client, frame);
                return;
            }
        }

        private void Answer(WorkerClient client, WorkerFrame frame)
        {
            Func<WorkerClient, string, Task<string>> handler;

            if (!_requests.TryGetValue(frame.t, out handler))
            {
                client.Link.Post(new WorkerFrame
                {
                    k  = FrameKind.Fail,
                    id = frame.id,
                    e  = "The worker has no handler for the request '" + frame.t + "'.",
                });
                return;
            }

            // A request must always be answered, or the page's task never completes: a handler that
            // throws synchronously is as much an answer as one whose task faults.
            Task<string> work;

            try
            {
                work = handler(client, frame.p);
            }
            catch (Exception ex)
            {
                Fail(client, frame.id, ex);
                return;
            }

            if (work == null)
            {
                client.Link.Post(new WorkerFrame { k = FrameKind.Reply, id = frame.id, p = null });
                return;
            }

            work.ContinueWith(t =>
            {
                if (!client.IsOpen) return;

                if (t.IsFaulted) Fail(client, frame.id, t.Exception);
                else             client.Link.Post(new WorkerFrame { k = FrameKind.Reply, id = frame.id, p = t.Result });
            });
        }

        private static void Fail(WorkerClient client, string id, Exception error)
        {
            var message = error == null ? "The worker failed to answer." : error.Message;

            client.Link.Post(new WorkerFrame { k = FrameKind.Fail, id = id, e = message });
        }

        private void Drop(WorkerClient client)
        {
            if (!client.IsOpen) return;

            client.IsOpen = false;
            _clients.Remove(client);
            client.Link.Received = null;

            if (_gone != null) _gone(client);
        }
    }
}
