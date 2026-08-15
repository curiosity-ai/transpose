using System;

namespace System.Text.Json.Serialization
{
    /// <summary>
    /// Opts a member in that the default rules leave out — a public field when
    /// <see cref="JsonSerializerOptions.IncludeFields"/> is off, or a property whose setter is
    /// non-public.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class JsonIncludeAttribute : Attribute
    {
    }
}
