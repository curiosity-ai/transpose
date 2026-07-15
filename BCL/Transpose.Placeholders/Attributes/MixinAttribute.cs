// Original: H5/H5/Attributes/MixinAttribute.cs
using System;

namespace H5
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
