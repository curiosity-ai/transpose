using System;
using Transpose;

namespace System.Text.Json
{
    /// <summary>
    /// Converts .NET values to and from JSON.
    /// </summary>
    /// <remarks>
    /// This is the Transpose binding onto the hand-written runtime in
    /// <c>Resources/Manual/JsonSerializer.js</c>. It covers the surface a browser app actually uses —
    /// whole-document <see cref="Serialize(object)"/> / <see cref="Deserialize{TValue}(string)"/> over
    /// a <see cref="JsonSerializerOptions"/> — and deliberately not the streaming
    /// (<c>Utf8JsonReader</c> / <c>Utf8JsonWriter</c>), document (<c>JsonDocument</c> /
    /// <c>JsonNode</c>) or source-generated (<c>JsonSerializerContext</c>) APIs.
    /// </remarks>
    [External]
    public static class JsonSerializer
    {
        /// <summary>Converts the value to a JSON string.</summary>
        [Unbox(false)]
        [Template("System.Text.Json.JsonSerializer.Serialize({value}, null, false, {TValue})")]
        public static extern string Serialize<TValue>(TValue value);

        /// <summary>Converts the value to a JSON string, using the supplied options.</summary>
        [Unbox(false)]
        [Template("System.Text.Json.JsonSerializer.Serialize({value}, {options}, false, {TValue})")]
        public static extern string Serialize<TValue>(TValue value, JsonSerializerOptions options);

        /// <summary>Converts the value to a JSON string.</summary>
        [Unbox(false)]
        [Template("System.Text.Json.JsonSerializer.Serialize({value}, null, false, null)")]
        public static extern string Serialize(object value);

        /// <summary>Converts the value to a JSON string, using the supplied options.</summary>
        [Unbox(false)]
        [Template("System.Text.Json.JsonSerializer.Serialize({value}, {options}, false, null)")]
        public static extern string Serialize(object value, JsonSerializerOptions options);

        /// <summary>Converts the value to a JSON string, treating it as <paramref name="inputType"/>.</summary>
        [Unbox(false)]
        [Template("System.Text.Json.JsonSerializer.Serialize({value}, null, false, {inputType})")]
        public static extern string Serialize(object value, Type inputType);

        /// <summary>Converts the value to a JSON string, treating it as <paramref name="inputType"/>.</summary>
        [Unbox(false)]
        [Template("System.Text.Json.JsonSerializer.Serialize({value}, {options}, false, {inputType})")]
        public static extern string Serialize(object value, Type inputType, JsonSerializerOptions options);

        /// <summary>Parses the JSON string into a <typeparamref name="TValue"/>.</summary>
        [ConstructsTypeArguments]
        [Template("System.Text.Json.JsonSerializer.Deserialize({json}, {TValue}, null)")]
        public static extern TValue Deserialize<TValue>(string json);

        /// <summary>Parses the JSON string into a <typeparamref name="TValue"/>, using the supplied options.</summary>
        [ConstructsTypeArguments]
        [Template("System.Text.Json.JsonSerializer.Deserialize({json}, {TValue}, {options})")]
        public static extern TValue Deserialize<TValue>(string json, JsonSerializerOptions options);

        /// <summary>Parses the JSON string into a <paramref name="returnType"/>.</summary>
        [Template("System.Text.Json.JsonSerializer.Deserialize({json}, {returnType}, null)")]
        public static extern object Deserialize(string json, Type returnType);

        /// <summary>Parses the JSON string into a <paramref name="returnType"/>, using the supplied options.</summary>
        [Template("System.Text.Json.JsonSerializer.Deserialize({json}, {returnType}, {options})")]
        public static extern object Deserialize(string json, Type returnType, JsonSerializerOptions options);
    }
}
