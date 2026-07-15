// Original: Transpose/Transpose/Attributes/MixinAttribute.cs
using System;

namespace Transpose
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class MixinAttribute : Attribute
    {
        public MixinAttribute(string expression)
        {
        }

        public string Expression { get; private set; }
    }
}
