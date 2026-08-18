namespace Transpose
{
    /// <summary>
    /// Keeps a type's JavaScript in the initial payload: it is never left as a
    /// <c>Transpose.Modules</c> stub, whatever the reference graph says.
    ///
    /// With <c>outputBy: Module</c> a type nothing statically references is deferred — its chunk is
    /// fetched on demand, and until then a stub answers every reflection question but throws when
    /// constructed, because fetching a module is asynchronous and construction is not. That is the
    /// right default, and it is wrong for a type that something reaches purely through reflection:
    /// a DTO a deserializer activates from its metadata, a settings class resolved by name, a type
    /// looked up from a string.
    ///
    /// This is the blunt instrument, and deliberately so — the type joins the eager roots, exactly
    /// like the attribute classes the reflection metadata constructs, so it and everything it needs
    /// are in the entry module's imports. Prefer <see cref="ConstructsTypeArgumentsAttribute"/> on
    /// the generic method that does the activating: it records the same dependency at the call site,
    /// so the type is fetched with the code that deserializes it rather than at start-up. Reach for
    /// this one where that cannot work — the type argument is a <c>Type</c> value rather than a
    /// static type argument, or the reflective lookup is in a library you cannot annotate.
    ///
    /// It does nothing outside module output.
    /// </summary>
    [NonScriptable]
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct
                           | System.AttributeTargets.Interface | System.AttributeTargets.Enum,
                           Inherited = false)]
    public sealed class NeverDeferAttribute : System.Attribute
    {
    }
}
