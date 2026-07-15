// Original: Transpose/Transpose/Attributes/ScriptAttribute.cs
using System;

namespace Transpose
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
    public sealed class ScriptAttribute : Attribute
    {
        public ScriptAttribute(params string[] lines)
        {
        }
    }
}
