using System;

namespace Transpose
{
    [NonScriptable]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class ConstructorAttribute : Attribute
    {
        public ConstructorAttribute(string value)
        {
        }
    }
}