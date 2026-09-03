using System;
using Transpose;
using Transpose.Core;

namespace Transpose.Core
{
    public static partial class dom
    {
        /// <summary>
        /// How a worker script is loaded — the <c>type</c> of <see cref="dom.WorkerOptions"/>.
        /// </summary>
        /// <remarks>
        /// A <see cref="Classic"/> worker pulls its dependencies in with
        /// <see cref="dom.WorkerGlobalScope.importScripts(string[])"/>; a <see cref="Module"/> worker
        /// is an ES module and uses <c>import</c>, which is what lets a worker share the chunk files
        /// a module build already emits. Module shared workers arrived later than shared workers
        /// themselves (Chrome 80, Firefox 114, Safari 16), so a build that has to reach an older
        /// browser wants the classic form.
        /// </remarks>
        [Enum(Emit.StringNameLowerCase)]
        public enum WorkerScriptType
        {
            /// <summary>A classic script. The default when no <c>type</c> is given.</summary>
            Classic = 0,

            /// <summary>An ES module, able to <c>import</c>.</summary>
            Module = 1,
        }

        /// <summary>
        /// The second argument of the <see cref="dom.Worker"/> and <see cref="dom.SharedWorker"/>
        /// constructors.
        /// </summary>
        /// <remarks>
        /// <paramref name="name"/> matters far more for a shared worker than for a dedicated one: a
        /// shared worker is identified by the triple (origin, script URL, name), so two pages asking
        /// for the same script under <em>different</em> names get two separate workers, and two pages
        /// asking under the same name get one. It is the only handle a page has on which instance it
        /// joins.
        /// </remarks>
        [IgnoreCast]
        [ObjectLiteral]
        [FormerInterface]
        public class WorkerOptions : IObject
        {
            /// <summary>Whether the script is a classic script or an ES module.</summary>
            public dom.WorkerScriptType type { get; set; }

            /// <summary>The credentials mode used to fetch the script — <c>omit</c>, <c>same-origin</c> or <c>include</c>.</summary>
            public string credentials { get; set; }

            /// <summary>The worker's name, and for a shared worker part of its identity.</summary>
            public string name { get; set; }
        }

        /// <summary>
        /// A worker shared by every page, iframe and worker of one origin — the way to hold a single
        /// connection, or a single piece of state, for all of a browser's tabs at once.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Unlike <see cref="dom.Worker"/> there is no <c>postMessage</c> on the worker object:
        /// communication goes over <see cref="port"/>, and each connecting page gets its own port. A
        /// port does not deliver anything until <see cref="dom.MessagePort.start"/> is called, which
        /// assigning <see cref="dom.MessagePort.onmessage"/> does implicitly — so a page that uses
        /// <c>addEventListener("message", …)</c> instead has to call <c>start()</c> itself. This is
        /// the single most common way a first shared-worker integration silently receives nothing.
        /// </para>
        /// <para>
        /// Support is broad but not universal, and the gaps are recent enough to matter: Safari only
        /// gained it in 16, and Chrome for Android (and Android WebView with it) did not support it
        /// at all until Chrome 148, so a mobile browser is the likeliest place to find it missing.
        /// Constructing one where it is unavailable throws, so probe before use rather than assuming
        /// — and note that Android may terminate a shared worker when the browser is backgrounded,
        /// which a caller has to treat as a reconnect rather than a fatal error.
        /// </para>
        /// </remarks>
        [CombinedClass]
        [FormerInterface]
        public class SharedWorker : dom.EventTarget, IObject
        {
            /// <param name="scriptURL">The worker script. Must be same-origin.</param>
            public extern SharedWorker(string scriptURL);

            /// <param name="scriptURL">The worker script. Must be same-origin.</param>
            /// <param name="name">The worker's name — part of which shared worker this joins.</param>
            public extern SharedWorker(string scriptURL, string name);

            /// <param name="scriptURL">The worker script. Must be same-origin.</param>
            /// <param name="options">The worker's name and script type.</param>
            public extern SharedWorker(string scriptURL, dom.WorkerOptions options);

            public static dom.SharedWorker prototype { get; set; }

            /// <summary>
            /// This page's end of the pipe to the worker. Nothing arrives on it until it is started,
            /// which assigning <see cref="dom.MessagePort.onmessage"/> does for you.
            /// </summary>
            public virtual dom.MessagePort port { get; }

            /// <summary>Raised when the worker script fails to load or throws while loading.</summary>
            public virtual dom.AbstractWorker.onerrorFn onerror { get; set; }

            public virtual extern void addEventListener(string type, Action<dom.Event> listener);

            public virtual extern void addEventListener(
              string type,
              Action<dom.Event> listener,
              Union<bool, dom.AddEventListenerOptions> options);

            public virtual extern void addEventListener(string type, Action<dom.Event> listener, bool options);

            public virtual extern void addEventListener(
              string type,
              Action<dom.Event> listener,
              dom.AddEventListenerOptions options);

            public virtual extern void removeEventListener(string type, Action<dom.Event> listener);

            public virtual extern void removeEventListener(
              string type,
              Action<dom.Event> listener,
              Union<bool, dom.EventListenerOptions> options);

            public virtual extern void removeEventListener(string type, Action<dom.Event> listener, bool options);

            public virtual extern void removeEventListener(
              string type,
              Action<dom.Event> listener,
              dom.EventListenerOptions options);
        }

        /// <summary>
        /// What <c>self</c> is inside any worker: the global scope shared by dedicated, shared and
        /// service workers.
        /// </summary>
        /// <remarks>
        /// A worker has no <c>document</c> and no <c>localStorage</c>, which is the practical limit
        /// on what can be moved into one — anything a worker needs from page-only storage has to be
        /// handed to it in a message. <c>fetch</c>, <c>WebSocket</c>, <c>EventSource</c>,
        /// <c>BroadcastChannel</c>, <c>IndexedDB</c> and the timers are all present.
        /// </remarks>
        [CombinedClass]
        [FormerInterface]
        public class WorkerGlobalScope : dom.EventTarget
        {
            public static dom.WorkerGlobalScope prototype { get; set; }

            /// <summary>The scope itself.</summary>
            public virtual dom.WorkerGlobalScope self { get; }

            /// <summary>The URL of the worker script.</summary>
            public virtual dom.WorkerLocation location { get; }

            /// <summary>The subset of <c>navigator</c> a worker can see.</summary>
            public virtual dom.WorkerNavigator navigator { get; }

            /// <summary>Raised for an uncaught error inside the worker.</summary>
            public virtual dom.WorkerGlobalScope.onerrorFn onerror { get; set; }

            /// <summary>
            /// Loads and runs one or more classic scripts synchronously, in order. Only available in
            /// a classic worker — in a module worker it throws, and <c>import</c> is used instead.
            /// </summary>
            [ExpandParams]
            public virtual extern void importScripts(params string[] urls);

            [Generated]
            public delegate void onerrorFn(dom.ErrorEvent evt);
        }

        /// <summary>
        /// What <c>self</c> is inside a shared worker. Reached from C# as
        /// <see cref="dom.SharedWorkerGlobalScope.Current"/>.
        /// </summary>
        /// <remarks>
        /// The whole of a shared worker's API surface is <see cref="onconnect"/>: it fires once per
        /// connecting page with that page's port in
        /// <see cref="dom.MessageEvent.ports"/>[0], and the worker keeps every port it is given for
        /// as long as it wants to talk to that page. Nothing tells the worker that a page has gone
        /// away — a closed tab's port simply stops answering — so a worker that has to know when it
        /// is idle needs the pages to say so, or a heartbeat.
        /// </remarks>
        [CombinedClass]
        [FormerInterface]
        public class SharedWorkerGlobalScope : dom.WorkerGlobalScope
        {
            public static new dom.SharedWorkerGlobalScope prototype { get; set; }

            /// <summary>
            /// The current shared worker's global scope — <c>self</c>. Reading this from a page (or a
            /// dedicated worker) gives you that scope instead, so it is only meaningful in code that
            /// runs inside a shared worker.
            /// </summary>
#pragma warning disable 649 // assigned by the template, never in C#
            [Template("self")]
            public static readonly dom.SharedWorkerGlobalScope Current;
#pragma warning restore 649

            /// <summary>The name this worker was constructed with.</summary>
            public virtual string name { get; }

            /// <summary>Raised once for every page that connects, carrying that page's port.</summary>
            public virtual dom.SharedWorkerGlobalScope.onconnectFn onconnect { get; set; }

            /// <summary>Discards the worker. Every connected port stops delivering.</summary>
            public virtual extern void close();

            [Generated]
            public delegate void onconnectFn(dom.MessageEvent evt);
        }

        /// <summary>What <c>self</c> is inside a dedicated worker (one created with <see cref="dom.Worker"/>).</summary>
        [CombinedClass]
        [FormerInterface]
        public class DedicatedWorkerGlobalScope : dom.WorkerGlobalScope
        {
            public static new dom.DedicatedWorkerGlobalScope prototype { get; set; }

            /// <summary>The current dedicated worker's global scope — <c>self</c>.</summary>
#pragma warning disable 649 // assigned by the template, never in C#
            [Template("self")]
            public static readonly dom.DedicatedWorkerGlobalScope Current;
#pragma warning restore 649

            /// <summary>The worker's name.</summary>
            public virtual string name { get; }

            /// <summary>Raised for each message the creating context posts.</summary>
            public virtual dom.DedicatedWorkerGlobalScope.onmessageFn onmessage { get; set; }

            /// <summary>Sends a message back to the context that created this worker.</summary>
            public virtual extern void postMessage(object message);

            /// <summary>Sends a message back, transferring ownership of <paramref name="transfer"/>.</summary>
            public virtual extern void postMessage(object message, object[] transfer);

            /// <summary>Discards the worker.</summary>
            public virtual extern void close();

            [Generated]
            public delegate void onmessageFn(dom.MessageEvent evt);
        }

        /// <summary>The worker equivalent of <c>location</c> — the URL the worker script was loaded from.</summary>
        [CombinedClass]
        [FormerInterface]
        public class WorkerLocation : IObject
        {
            public static dom.WorkerLocation prototype { get; set; }

            public virtual string href { get; }
            public virtual string origin { get; }
            public virtual string protocol { get; }
            public virtual string host { get; }
            public virtual string hostname { get; }
            public virtual string port { get; }
            public virtual string pathname { get; }
            public virtual string search { get; }
            public virtual string hash { get; }
        }

        /// <summary>The subset of <c>navigator</c> exposed inside a worker.</summary>
        [CombinedClass]
        [FormerInterface]
        public class WorkerNavigator : IObject
        {
            public static dom.WorkerNavigator prototype { get; set; }

            public virtual string userAgent { get; }
            public virtual string language { get; }
            public virtual es5.ReadonlyArray<string> languages { get; }
            public virtual bool onLine { get; }
            public virtual double hardwareConcurrency { get; }
        }
    }
}
