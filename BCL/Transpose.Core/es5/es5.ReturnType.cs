// Decompiled with JetBrains decompiler
// Type: Transpose.es5
// Assembly: Transpose.es5, Version=2.8.2.0, Culture=neutral, PublicKeyToken=null
// MVID: EC57AC2B-0E02-4A1C-B567-F790F377783B
// Assembly location: C:\work\curiosity\tesserae\Tesserae\bin\Debug\net461\Transpose.es5.dll

namespace Transpose.Core
{
    public  static partial class es5
    {
        [IgnoreGeneric(AllowInTypeScript = true)]
        [IgnoreCast]
        [Virtual]
        [Where("T", new string[] { "ReturnTypeFnAlias" }, EnableImplicitConversion = true)]
        public abstract class ReturnType<T> : TypeAlias<Union<object, object>>
        {
            public static extern implicit operator es5.ReturnType<T>(Union<object, object> value);

            [Template("{0}")]
            public static extern es5.ReturnType<T> Create(object value);

            [Generated]
            public delegate object ReturnTypeFn<T>(params object[] args);

            [IgnoreGeneric(AllowInTypeScript = true)]
            [IgnoreCast]
            [Virtual]
            public abstract class ReturnTypeFnAlias<T> : TypeAlias<es5.ReturnType<T>.ReturnTypeFn<T>>
            {
                public static extern implicit operator ReturnTypeFnAlias<T>(
                  es5.ReturnType<T>.ReturnTypeFn<T> value);
            }
        }
    }
}
