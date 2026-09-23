using Transpose;
using Transpose.Core;

namespace Transpose.Workers
{
    /// <summary>Whether this browser can run a shared worker at all.</summary>
    public static class SharedWorkers
    {
        private static bool  _probed;
        private static bool  _supported;

        /// <summary>
        /// True when <c>SharedWorker</c> exists and can be constructed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Worth probing rather than assuming, and the gaps are recent enough to matter in practice:
        /// Safari only gained shared workers in 16, and Chrome for Android — with Android WebView
        /// behind it — had none at all until Chrome 148. A mobile browser is by far the likeliest
        /// place to find this false.
        /// </para>
        /// <para>
        /// The constructor is not called here: constructing one would start a worker, and this
        /// question gets asked before anyone has decided to. Presence of the global is the test, and
        /// a construction that fails anyway is handled where it happens
        /// (<see cref="WorkerChannel.Connect"/> falls back rather than throwing).
        /// </para>
        /// </remarks>
        public static bool IsSupported
        {
            get
            {
                if (!_probed)
                {
                    _probed = true;

                    // Reading a missing global would throw, hence typeof rather than a null test.
                    _supported = Script.Write<string>("typeof SharedWorker") == "function";
                }

                return _supported;
            }
        }

        /// <summary>
        /// Forces <see cref="IsSupported"/> to a value, for a test that has to exercise the fallback
        /// on a browser that does support shared workers.
        /// </summary>
        /// <remarks>
        /// There is no other way to reach the fallback path in a test: the whole point of it is the
        /// browsers a test suite cannot easily run. Pass null to go back to probing.
        /// </remarks>
        public static void OverrideSupportForTesting(bool? supported)
        {
            if (supported.HasValue)
            {
                _probed    = true;
                _supported = supported.Value;
            }
            else
            {
                _probed = false;
            }
        }
    }
}
