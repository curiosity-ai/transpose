// Original: H5/H5/Attributes/InitAttribute.cs
using System;

namespace H5
{
    [AttributeUsage(AttributeTargets.Method)]
    public class InitAttribute : Attribute
    {
        public InitAttribute()
        {
        }

        public InitAttribute(InitPosition position)
        {
        }
    }

    public enum InitPosition
    {
        After = 0,
        Before = 1,
        Top = 2,
        Bottom = 3
    }
}
