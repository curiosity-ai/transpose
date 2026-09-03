using System;
using Transpose;
using Transpose.Core;

namespace Transpose.Workers
{
    /// <summary>
    /// The typed layer's serialization: the browser's own <c>JSON</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately not a JSON library. This package would otherwise have to pick one for everybody
    /// — Newtonsoft or System.Text.Json — and a transport has no business doing that. The browser's
    /// <c>JSON</c> is always there and costs nothing.
    /// </para>
    /// <para>
    /// The consequence is the limit documented on <see cref="WorkerChannel.Send{T}"/>: this
    /// round-trips <em>data</em>. A parsed object has the shape it was written with and no prototype,
    /// so reading it back as a class with methods or computed properties gives an object that answers
    /// its fields and nothing else. For plain DTOs — which is what a message payload almost always is
    /// — that is exactly right; for anything else, send a string and use your own serializer, which
    /// is what the string overloads are for.
    /// </para>
    /// </remarks>
    internal static class WorkerJson
    {
        public static string Write<T>(T value)
        {
            // A string payload is passed straight through rather than JSON-quoted, so Send<string>
            // and Send(string) put the same bytes on the wire.
            if (Script.TypeOf(value) == "string") return ((object)value).As<string>();

            return es5.JSON.stringify(value);
        }

        public static T Read<T>(string payload)
        {
            if (payload == null) return default(T);

            return es5.JSON.parse(payload).As<T>();
        }
    }
}
