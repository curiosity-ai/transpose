using System;

namespace System.Text.Json.Serialization
{
    /// <summary>Gives the member a fixed JSON name, overriding any naming policy.</summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class JsonPropertyNameAttribute : Attribute
    {
        /// <summary>Initializes a new instance with the JSON name to use.</summary>
        public JsonPropertyNameAttribute(string name)
        {
            Name = name;
        }

        /// <summary>The JSON name of the member.</summary>
        public string Name { get; }
    }
}
