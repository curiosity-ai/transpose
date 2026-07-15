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
        [ObjectLiteral]
        public class Required<T> : IObject
        {
            public extern object this[KeyOf<T> P] { get; set; }

            [Name("-")]
            public object Minus
            {
                get;
                set;
            }
        }
    }
}
