namespace Transpose
{
    /// <summary>
    /// Says that calling this method <b>loads</b> its type arguments' modules itself, asynchronously,
    /// so their JavaScript does not have to be present where the call is.
    ///
    /// This is the mirror image of <see cref="ConstructsTypeArgumentsAttribute"/>, and it exists for
    /// the same pass. The module chunker records a dependency wherever a type reference is emitted,
    /// and a generic call emits its type arguments — as a <c>{T}</c> template token, or as the
    /// leading arguments a generic method is threaded — so
    /// <c>Activator.CreateInstanceAsync&lt;HomeView&gt;()</c> pins <c>HomeView</c>'s chunk into the
    /// caller's, which is exactly what the call was written to avoid. The whole point of the
    /// asynchronous activator is that it fetches the module first; a static edge to it defeats it,
    /// silently, leaving code that looks lazy and is not.
    ///
    /// Marking the method makes its type arguments <em>soft</em> references at every call site: the
    /// name is still emitted (the runtime needs it to know what to fetch), and a deferred type's stub
    /// answers it, but the chunk holding the call no longer imports the chunk holding the type. Apply
    /// it only to a method that really does load before it touches the type — everything else about a
    /// deferred type still throws.
    ///
    /// It reads on the method or on its containing type, and works the same for your own async
    /// activator as for the BCL's:
    ///
    /// <code>
    /// [Transpose.LoadsTypeArguments]
    /// public static async Task&lt;T&gt; ActivateAsync&lt;T&gt;() where T : class
    ///     =&gt; await Activator.CreateInstanceAsync&lt;T&gt;();
    /// </code>
    ///
    /// It does nothing outside module output.
    /// </summary>
    [NonScriptable]
    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class
                           | System.AttributeTargets.Struct,
                           AllowMultiple = false, Inherited = false)]
    public sealed class LoadsTypeArgumentsAttribute : System.Attribute
    {
    }
}
