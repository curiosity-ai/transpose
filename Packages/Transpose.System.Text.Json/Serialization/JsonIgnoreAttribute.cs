using System;

namespace System.Text.Json.Serialization
{
    /// <summary>Keeps the member out of the JSON payload, always or under a condition.</summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class JsonIgnoreAttribute : Attribute
    {
        /// <summary>Initializes a new instance that ignores the member unconditionally.</summary>
        public JsonIgnoreAttribute()
        {
        }

        /// <summary>When the member is ignored. Defaults to <see cref="JsonIgnoreCondition.Always"/>.</summary>
        public JsonIgnoreCondition Condition { get; set; } = JsonIgnoreCondition.Always;
    }

    /// <summary>When a member is left out of the JSON payload.</summary>
    public enum JsonIgnoreCondition
    {
        /// <summary>The member is always written and always read.</summary>
        Never = 0,

        /// <summary>The member is never written and never read.</summary>
        Always = 1,

        /// <summary>The member is skipped on write when it holds its type's default value.</summary>
        WhenWritingDefault = 2,

        /// <summary>The member is skipped on write when it is <c>null</c>.</summary>
        WhenWritingNull = 3
    }
}
