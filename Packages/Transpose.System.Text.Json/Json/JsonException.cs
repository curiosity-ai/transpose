using System;

namespace System.Text.Json
{
    /// <summary>The exception thrown when JSON is invalid or cannot be mapped onto a .NET type.</summary>
    public class JsonException : Exception
    {
        /// <summary>Initializes a new instance.</summary>
        public JsonException()
        {
        }

        /// <summary>Initializes a new instance with the given message.</summary>
        public JsonException(string message)
            : base(message)
        {
        }

        /// <summary>Initializes a new instance with the given message and inner exception.</summary>
        public JsonException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>The path to the member where the error occurred, when one is known.</summary>
        public string Path { get; internal set; }
    }
}
