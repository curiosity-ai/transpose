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
        [Where("K", new string[] { "KeyOf" }, EnableImplicitConversion = true)]
        public class Pick<T, K> : IObject
        {
            public new extern object this[string P] { get; set; }
        }
    }
}
