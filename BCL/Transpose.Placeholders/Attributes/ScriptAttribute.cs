// Original: H5/H5/Attributes/ScriptAttribute.cs
using System;

namespace H5
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
    public sealed class ScriptAttribute : Attribute
    {
        public ScriptAttribute(params string[] lines)
        {
        }
    }
}
