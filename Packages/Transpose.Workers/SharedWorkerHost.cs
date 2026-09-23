using System;
using System.Collections.Generic;
using Transpose;
using Transpose.Core;

namespace Transpose.Workers
{
    /// <summary>
    /// Starts a worker's hub. This is what a <c>[SharedWorkerEntry]</c> method calls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It runs in whichever scope it finds itself in, and that is the whole design: inside a shared
    /// worker it installs an <c>onconnect</c> handler, and inside a page — the per-tab fallback,
    /// where the browser has no <c>SharedWorker</c> — it registers the hub for
    /// <see cref="WorkerChannel"/> to connect to in-process. The hub is configured the same way and
    /// written once either way, so the fallback cannot drift from the real worker.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public static class LiveWorker
    /// {
    ///     [SharedWorkerEntry("live")]
    ///     public static void Main() =&gt; SharedWorkerHost.Run("live", hub =&gt;
    ///     {
    ///         hub.OnConnect(c =&gt; Console.WriteLine("a page joined"))
    ///            .On("hello", (c, who) =&gt; c.Tag = who)
    ///            .OnRequest("time", (c, _) =&gt; Task.FromResult(DateTime.UtcNow.ToString("O")));
    ///     });
    /// }
    /// </code>
    /// </example>
    public static class SharedWorkerHost
    {
        /// <summary>The hubs running in THIS JavaScript context, by worker name.</summary>
        /// <remarks>
        /// In a worker there is one, and looking it up costs nothing. In a page it is how
        /// <see cref="WorkerChannel.Connect"/> finds the fallback hub the same page just started —
        /// which is also why running the same entry twice must not build a second hub.
        /// </remarks>
        private static readonly Dictionary<string, WorkerHub> _hubs = new Dictionary<string, WorkerHub>();

        /// <summary>
        /// Configures and starts the hub named <paramref name="name"/>, if it is not already running.
        /// </summary>
        /// <param name="name">
        /// The worker's name — the same string as on the <c>[SharedWorkerEntry]</c> attribute and in
        /// <see cref="WorkerChannel.Connect"/>. It is what identifies the hub in the fallback, where
        /// several may run side by side in one page.
        /// </param>
        /// <param name="configure">Declares what the hub answers. Called once.</param>
        /// <returns>The hub, whether it was started now or already running.</returns>
        public static WorkerHub Run(string name, Action<WorkerHub> configure)
        {
            WorkerHub existing;

            // Idempotent on purpose. In a page, a second call is an ordinary consequence of two
            // channels asking for the same worker, and re-configuring would replace live handlers
            // and orphan the connected clients.
            if (_hubs.TryGetValue(name, out existing)) return existing;

            var hub = new WorkerHub();

            _hubs[name] = hub;

            if (configure != null) configure(hub);

            if (Script.Write<bool>("Transpose.isWorkerScope"))
            {
                // Every connecting page arrives here, one event each, carrying its own port.
                dom.SharedWorkerGlobalScope.Current.onconnect = connect =>
                {
                    var ports = connect.ports;

                    if (ports == null || ports.length == 0) return;

                    hub.Accept(new PortLink(ports[0]));
                };
            }

            return hub;
        }

        /// <summary>The hub named <paramref name="name"/> running in this context, or null.</summary>
        internal static WorkerHub Find(string name)
        {
            WorkerHub hub;

            return _hubs.TryGetValue(name, out hub) ? hub : null;
        }
    }
}
