namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("System.Object")]
    public static class Activator
    {
        [Transpose.Template("Transpose.createInstance({type}, {arguments:array})", "Transpose.Reflection.applyConstructor({type}, {arguments:array})")]
        public static extern object CreateInstance(Type type, params object[] arguments);

        [Transpose.Template("Transpose.createInstance({T}, {arguments:array})", "Transpose.Reflection.applyConstructor({T}, {arguments:array})")]
        public static extern T CreateInstance<T>(params object[] arguments);

        [Transpose.Template("Transpose.createInstance({type})")]
        public static extern object CreateInstance(Type type);

        [Transpose.Template("Transpose.createInstance({type}, {nonPublic})")]
        public static extern object CreateInstance(Type type, bool nonPublic);

        [Transpose.Template("Transpose.createInstance({T})")]
        public static extern T CreateInstance<T>();

        // ---- asynchronous creation, for a type whose module may not be loaded -------------------
        //
        // A build that splits its output into JavaScript modules leaves the types it deferred as
        // stubs (see Transpose.Modules): reflection still sees them, but their constructors are not
        // there, and fetching a module is asynchronous. The synchronous CreateInstance above throws
        // on a stub naming the module; these overloads load it first and then construct. Awaiting
        // one for an already-loaded type just constructs, so a call site that might see either can
        // use the async form unconditionally.

        /// <summary>Creates an instance of <paramref name="type"/>, first loading its module if it
        /// has not been loaded yet.</summary>
        [Transpose.Template("System.Threading.Tasks.Task.fromPromise(Transpose.createInstanceAsync({type}), 0)")]
        public static extern System.Threading.Tasks.Task<object> CreateInstanceAsync(Type type);

        /// <summary>Creates an instance of <paramref name="type"/> with the given constructor
        /// arguments, first loading its module if it has not been loaded yet.</summary>
        [Transpose.Template("System.Threading.Tasks.Task.fromPromise(Transpose.createInstanceAsync({type}, false, {arguments:array}), 0)")]
        public static extern System.Threading.Tasks.Task<object> CreateInstanceAsync(Type type, params object[] arguments);

        /// <summary>Creates an instance of <paramref name="type"/>, optionally matching a
        /// non-public constructor, first loading its module if it has not been loaded yet.</summary>
        [Transpose.Template("System.Threading.Tasks.Task.fromPromise(Transpose.createInstanceAsync({type}, {nonPublic}), 0)")]
        public static extern System.Threading.Tasks.Task<object> CreateInstanceAsync(Type type, bool nonPublic);

        /// <summary>Creates an instance of <typeparamref name="T"/>, first loading its module if it
        /// has not been loaded yet.</summary>
        [Transpose.Template("System.Threading.Tasks.Task.fromPromise(Transpose.createInstanceAsync({T}), 0)")]
        public static extern System.Threading.Tasks.Task<T> CreateInstanceAsync<T>();
    }
}