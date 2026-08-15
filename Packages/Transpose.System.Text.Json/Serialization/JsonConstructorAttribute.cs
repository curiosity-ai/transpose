using System;

namespace System.Text.Json.Serialization
{
    /// <summary>Selects the constructor the deserializer calls.</summary>
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false)]
    public sealed class JsonConstructorAttribute : Attribute
    {
    }
}
