using System;

namespace System.Text.Json.Serialization
{
    /// <summary>
    /// Declares a polymorphic hierarchy at run time, for the case where
    /// <see cref="JsonDerivedTypeAttribute"/> cannot be written.
    /// </summary>
    /// <remarks>
    /// The attribute has to name the derived types from the base type's own file, which means the
    /// base has to see them at compile time. In a layered application the base often sits *below*
    /// its implementations — an interface in a shared, low-level project and the concrete types in
    /// the UI project that references it — and there is no way to write the attribute at all.
    ///
    /// This is the escape hatch: register the same pairs at startup, from a place that can see both.
    /// A registration behaves exactly as the attribute would, and the two can be mixed (a type may
    /// use the attribute in one build and this in another, which is what a project shared between a
    /// server and a Transpose front-end typically needs).
    ///
    /// Register before the first serialize or deserialize of the hierarchy. A registration is
    /// process-wide, like the attribute it stands in for.
    /// </remarks>
    /// <example>
    /// <code>
    /// JsonPolymorphicTypes.Register&lt;INotification&gt;(typeof(UserJoined), "MyApp.UserJoined");
    /// </code>
    /// </example>
    [Transpose.External]
    public static class JsonPolymorphicTypes
    {
        /// <summary>Declares <paramref name="derivedType"/> as a member of <typeparamref name="TBase"/>'s hierarchy.</summary>
        /// <param name="derivedType">The concrete type. It must be assignable to <typeparamref name="TBase"/>.</param>
        /// <param name="typeDiscriminator">The value written to, and matched against, the discriminator member.</param>
        [Transpose.Template("System.Text.Json.JsonSerializer.registerDerivedType({TBase}, {derivedType}, {typeDiscriminator}, null)")]
        public static extern void Register<TBase>(Type derivedType, string typeDiscriminator);

        /// <summary>
        /// Declares <paramref name="derivedType"/> as a member of <typeparamref name="TBase"/>'s
        /// hierarchy, naming the member that carries the discriminator.
        /// </summary>
        [Transpose.Template("System.Text.Json.JsonSerializer.registerDerivedType({TBase}, {derivedType}, {typeDiscriminator}, {discriminatorPropertyName})")]
        public static extern void Register<TBase>(Type derivedType, string typeDiscriminator, string discriminatorPropertyName);

        /// <summary>Declares <paramref name="derivedType"/> as a member of <paramref name="baseType"/>'s hierarchy.</summary>
        [Transpose.Template("System.Text.Json.JsonSerializer.registerDerivedType({baseType}, {derivedType}, {typeDiscriminator}, null)")]
        public static extern void Register(Type baseType, Type derivedType, string typeDiscriminator);
    }
}
