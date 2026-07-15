using System;

namespace Transpose
{
    [NonScriptable]
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class ImmutableAttribute : Attribute
    {
    }
}