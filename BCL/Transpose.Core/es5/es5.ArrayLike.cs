// Decompiled with JetBrains decompiler
// Type: Transpose.es5
// Assembly: Transpose.es5, Version=2.8.2.0, Culture=neutral, PublicKeyToken=null
// MVID: EC57AC2B-0E02-4A1C-B567-F790F377783B
// Assembly location: C:\work\curiosity\tesserae\Tesserae\bin\Debug\net461\Transpose.es5.dll

namespace Transpose.Core
{
    public  static partial class es5
    {
        [IgnoreCast]
        [IgnoreGeneric(AllowInTypeScript = true)]
        [Virtual]
        [FormerInterface]
        public abstract class ArrayLike<T> : IObject
        {
            public abstract double length { get; }

            public abstract T this[double n] { get; }

            public static extern implicit operator es5.ArrayLike<T>(T[] arr);

            public static extern implicit operator es5.ArrayLike<T>(es5.Array<T> arr);

            [Template("{0}")]
            public static extern es5.ArrayLike<T> From(T[] arr);

            [Template("{0}")]
            public static extern es5.ArrayLike<T> From(es5.Array<T> arr);
        }
    }
}
