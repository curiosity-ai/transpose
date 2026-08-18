using System;
using System.Threading.Tasks;

namespace Transpose
{
    /// <summary>
    /// The registry for types whose JavaScript lives in a module that has not been loaded.
    ///
    /// A build that splits its output into modules registers a manifest of what it deferred; every
    /// such type gets a stub standing in its place, so reflection keeps working over it —
    /// <c>Assembly.GetTypes()</c>, <c>Type.Name</c>, <c>Type.IsInterface</c>,
    /// <c>Type.IsAssignableFrom</c> and the reflection metadata all see the type while its code is
    /// still unfetched.
    ///
    /// What a stub cannot do is run. Fetching a module is asynchronous and C# construction is not,
    /// so instantiating a deferred type goes through <see cref="Activator.CreateInstanceAsync(Type)"/>
    /// or an explicit <see cref="LoadAsync(Type)"/>; a synchronous
    /// <see cref="Activator.CreateInstance(Type)"/> on a stub throws and names the module.
    /// </summary>
    [External]
    [Name("Transpose.Modules")]
    public static class Modules
    {
        /// <summary>
        /// Declares the types that live in not-yet-loaded modules. The argument is the manifest
        /// object the build emitted: type name → <c>{ m: module url, k: kind, a: assembly,
        /// i: [base type names] }</c>. Calling it more than once is safe; each assembly registers
        /// its own chunk manifest.
        /// </summary>
        [Template("Transpose.Modules.register({manifest})")]
        public static extern void Register(object manifest);

        /// <summary>
        /// Replaces how a module url is fetched. The default uses a dynamic <c>import()</c>; a host
        /// that serves its chunks another way — a bundler runtime, a non-ESM page, a test — supplies
        /// its own. The loader is handed the url and returns a task that completes once the module
        /// has been evaluated.
        /// </summary>
        [Template("Transpose.Modules.setLoader({loader})")]
        public static extern void SetLoader(Func<string, Task> loader);

        /// <summary>
        /// True when the type's code is present — either it was never deferred, or its module has
        /// been loaded. False only while it is still a stub.
        /// </summary>
        [Template("Transpose.Modules.isLoaded({type})")]
        public static extern bool IsLoaded(Type type);

        /// <summary>True while <paramref name="type"/> is a stub for an unloaded module.</summary>
        [Template("Transpose.Modules.isStub({type})")]
        public static extern bool IsStub(Type type);

        /// <summary>
        /// Fetches the module holding <paramref name="type"/> and completes with the real type.
        /// Awaiting an already-loaded type is a no-op, so this is safe to call unconditionally.
        /// </summary>
        [Template("System.Threading.Tasks.Task.fromPromise(Transpose.Modules.load({type}), 0)")]
        public static extern Task<Type> LoadAsync(Type type);

        /// <summary>
        /// Fetches the module holding the type named <paramref name="typeName"/> (its full name, as
        /// the manifest declares it) and completes with the real type, or <c>null</c> if no such
        /// type is known.
        /// </summary>
        [Template("System.Threading.Tasks.Task.fromPromise(Transpose.Modules.load({typeName}), 0)")]
        public static extern Task<Type> LoadAsync(string typeName);
    }
}
