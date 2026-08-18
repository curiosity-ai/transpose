namespace Transpose
{
    /// <summary>
    /// Says that calling this method <b>constructs</b> its type arguments at run time, through
    /// reflection, rather than merely naming them — so their JavaScript has to be present wherever
    /// the call is.
    ///
    /// This exists for the module chunker (<c>outputBy: Module</c>). A chunk is a component of the
    /// reference graph the emitter records while emitting, so a type is only kept loadable if some
    /// emitted code <em>refers</em> to it. A reflection-driven deserializer breaks that:
    /// <c>JsonConvert.DeserializeObject&lt;Order&gt;(json)</c> emits nothing that mentions
    /// <c>Order</c> beyond its <c>Type</c> object — which a deferred type's stub answers perfectly
    /// well — and then walks <c>Order</c>'s metadata and <c>new</c>s up <c>Order</c> and every member
    /// type it finds. Constructing a stub throws, and fetching its module is asynchronous, so there
    /// is nothing the runtime can do about it at that point.
    ///
    /// Marking the method closes the gap at the only place the compiler can see the edge: the call
    /// site. Each call records its type arguments — and, transitively, every type reachable from them
    /// through the fields and properties reflection describes — as real dependencies of the type
    /// being emitted, so the chunk that makes the call imports the chunks that define the DTOs. The
    /// types stay deferrable for code that does not deserialize them; they are pulled in exactly
    /// where they are needed, not made globally eager.
    ///
    /// Apply it to the deserializing entry points of a binding library (Json.NET's
    /// <c>DeserializeObject&lt;T&gt;</c>, System.Text.Json's <c>Deserialize&lt;TValue&gt;</c>), or to
    /// your own generic method that activates its type argument. It is not needed for a method that
    /// only reads a value it was handed (serialization), and it does nothing outside module output.
    ///
    /// Where the type argument is not statically known — <c>DeserializeObject(json, someType)</c>, a
    /// type argument that is itself a type parameter — there is nothing at the call site to record.
    /// Use <see cref="NeverDeferAttribute"/> on those types instead.
    ///
    /// <b>Marking somebody else's activator.</b> The assembly-level form names the type from outside
    /// it, so an application can opt in without waiting for the library to be re-released — and can
    /// do it for a third-party library it cannot edit at all:
    ///
    /// <code>
    /// [assembly: Transpose.ConstructsTypeArguments(typeof(Newtonsoft.Json.JsonConvert))]
    /// [assembly: Transpose.ConstructsTypeArguments(typeof(System.Text.Json.JsonSerializer))]
    /// </code>
    ///
    /// It applies to every generic method of that type, and only within the assembly that declares
    /// it — it is a statement about how <em>this</em> code calls the activator, so it neither
    /// travels to a consumer nor needs to.
    /// </summary>
    [NonScriptable]
    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class
                           | System.AttributeTargets.Struct | System.AttributeTargets.Assembly,
                           AllowMultiple = true, Inherited = false)]
    public sealed class ConstructsTypeArgumentsAttribute : System.Attribute
    {
        /// <summary>On a method or a type: that method, or every generic method of that type,
        /// constructs its type arguments.</summary>
        public ConstructsTypeArgumentsAttribute()
        {
        }

        /// <summary>On the assembly: every generic method of <paramref name="activator"/> constructs
        /// its type arguments, for calls made from this assembly. The form for an activator you do
        /// not own.</summary>
        public ConstructsTypeArgumentsAttribute(System.Type activator)
        {
        }
    }
}
