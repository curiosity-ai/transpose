namespace Transpose
{
    /// <summary>
    /// Marks the static method a shared worker starts at, and makes the compiler emit the worker
    /// script that starts it.
    ///
    /// <para>
    /// A <c>SharedWorker</c> needs a script URL of its own, and until now a Transpose project had no
    /// way to produce one: its output is a bundle meant for a page, whose entry point runs
    /// <c>Main</c> and whose code expects a document. Marking a method makes the build write a
    /// second entry beside the bundle — <c>&lt;name&gt;.worker.js</c> — that loads the runtime, the
    /// <c>TransposeR</c> shim and every bundle this site scripts, in the same order
    /// <c>index.html</c> does, and then calls the marked method. So the worker is ordinary C# in the
    /// ordinary project, and no JavaScript is written by hand.
    /// </para>
    ///
    /// <para>
    /// <paramref name="name"/> is both the file's base name and the worker's name, and the name is
    /// part of a shared worker's identity: the browser keys one instance per (origin, script URL,
    /// name), so every page that asks for this name joins the same worker, and a different name is a
    /// different worker. Pass it to <c>SharedWorkerChannel.ConnectAsync</c> on the page side
    /// rather than repeating the file name, so the two cannot drift apart.
    /// </para>
    ///
    /// <para>
    /// The method must be <c>static</c>, take no parameters and return <c>void</c>. It runs once per
    /// worker — not once per page — so its job is to install a
    /// <c>SharedWorkerGlobalScope.onconnect</c> handler (directly, or by handing a hub to
    /// <c>SharedWorkerHost</c>) and then return; a page connecting later is a
    /// <c>connect</c> event, not another call.
    /// </para>
    ///
    /// <para>
    /// What runs in a worker has no <c>document</c> and no <c>localStorage</c>, so anything the
    /// worker needs from either has to be handed to it in a message. Reaching for a type that
    /// touches the DOM fails at run time, not at build time.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// public static class LiveWorker
    /// {
    ///     [SharedWorkerEntry("curiosity-live")]
    ///     public static void Main() =&gt; SharedWorkerHost.Run(new LiveHub());
    /// }
    /// </code>
    /// </example>
    [NonScriptable]
    [System.AttributeUsage(System.AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class SharedWorkerEntryAttribute : System.Attribute
    {
        /// <param name="name">
        /// The worker's name, and the base name of the emitted script. Must be a plain file-name
        /// fragment — no directory separators — since it becomes a file beside the bundle.
        /// </param>
        public SharedWorkerEntryAttribute(string name)
        {
            Name = name;
        }

        /// <summary>The worker's name, as given.</summary>
        public string Name { get; }
    }
}
