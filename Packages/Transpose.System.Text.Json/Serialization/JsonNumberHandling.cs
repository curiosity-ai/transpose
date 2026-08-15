using System;

namespace System.Text.Json.Serialization
{
    /// <summary>How numbers are read from and written to JSON.</summary>
    [Flags]
    public enum JsonNumberHandling
    {
        /// <summary>Numbers are read from and written as JSON numbers only. This is the default.</summary>
        Strict = 0,

        /// <summary>A number may also be read from a JSON string.</summary>
        AllowReadingFromString = 1,

        /// <summary>Numbers are written as JSON strings.</summary>
        WriteAsString = 2,

        /// <summary><c>NaN</c>, <c>Infinity</c> and <c>-Infinity</c> may be read from and written as strings.</summary>
        AllowNamedFloatingPointLiterals = 4
    }

    /// <summary>Sets <see cref="JsonNumberHandling"/> for one member, type or assembly.</summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class JsonNumberHandlingAttribute : Attribute
    {
        /// <summary>Initializes a new instance with the handling to apply.</summary>
        public JsonNumberHandlingAttribute(JsonNumberHandling handling)
        {
            Handling = handling;
        }

        /// <summary>The handling to apply.</summary>
        public JsonNumberHandling Handling { get; }
    }
}
