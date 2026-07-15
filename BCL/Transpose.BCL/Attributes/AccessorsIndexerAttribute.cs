using System;

namespace Transpose
{
    [NonScriptable]
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class AccessorsIndexerAttribute : Attribute
    {
    }
}