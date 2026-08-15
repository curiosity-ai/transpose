using System;

namespace System.Text.Json.Serialization
{
    /// <summary>
    /// Positions the member in the written payload. Members carrying no order sort as <c>0</c> and
    /// keep their declaration order relative to one another.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class JsonPropertyOrderAttribute : Attribute
    {
        /// <summary>Initializes a new instance with the member's position.</summary>
        public JsonPropertyOrderAttribute(int order)
        {
            Order = order;
        }

        /// <summary>The member's position. Lower sorts earlier; negatives are allowed.</summary>
        public int Order { get; }
    }
}
