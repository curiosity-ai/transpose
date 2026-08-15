using System;

namespace System.Text.Json.Serialization
{
    /// <summary>Configures how a hierarchy rooted at this type is written and read.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false)]
    public sealed class JsonPolymorphicAttribute : Attribute
    {
        /// <summary>
        /// The member carrying the discriminator. Defaults to <c>$type</c>, which is what Json.NET
        /// used for <c>TypeNameHandling</c> as well.
        /// </summary>
        public string TypeDiscriminatorPropertyName { get; set; } = "$type";

        /// <summary>What to do with a runtime type the hierarchy does not declare.</summary>
        public JsonUnknownDerivedTypeHandling UnknownDerivedTypeHandling { get; set; } = JsonUnknownDerivedTypeHandling.FailSerialization;

        /// <summary>Whether an unrecognised JSON member is allowed to reach the extension data.</summary>
        public bool IgnoreUnrecognizedTypeDiscriminators { get; set; }
    }

    /// <summary>What the serializer does with a runtime type the hierarchy does not declare.</summary>
    public enum JsonUnknownDerivedTypeHandling
    {
        /// <summary>Throw. This is the default.</summary>
        FailSerialization = 0,

        /// <summary>Write it as its nearest declared ancestor.</summary>
        FallBackToBaseType = 1,

        /// <summary>Write it as the nearest declared ancestor, without a discriminator.</summary>
        FallBackToNearestAncestor = 2
    }

    /// <summary>Declares one member of a polymorphic hierarchy and the discriminator that names it.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true)]
    public sealed class JsonDerivedTypeAttribute : Attribute
    {
        /// <summary>Declares a derived type that carries no discriminator.</summary>
        public JsonDerivedTypeAttribute(Type derivedType)
        {
            DerivedType = derivedType;
        }

        /// <summary>Declares a derived type identified by a string discriminator.</summary>
        public JsonDerivedTypeAttribute(Type derivedType, string typeDiscriminator)
        {
            DerivedType       = derivedType;
            TypeDiscriminator = typeDiscriminator;
        }

        /// <summary>Declares a derived type identified by an integer discriminator.</summary>
        public JsonDerivedTypeAttribute(Type derivedType, int typeDiscriminator)
        {
            DerivedType       = derivedType;
            TypeDiscriminator = typeDiscriminator;
        }

        /// <summary>The derived type.</summary>
        public Type DerivedType { get; }

        /// <summary>The discriminator written for, and matched against, <see cref="DerivedType"/>.</summary>
        public object TypeDiscriminator { get; }
    }
}
