// Decompiled with JetBrains decompiler
// Type: Transpose.es5
// Assembly: Transpose.es5, Version=2.8.2.0, Culture=neutral, PublicKeyToken=null
// MVID: EC57AC2B-0E02-4A1C-B567-F790F377783B
// Assembly location: C:\work\curiosity\tesserae\Tesserae\bin\Debug\net461\Transpose.es5.dll

namespace Transpose.Core
{
    public  static partial class es5
    {
        public abstract class ClassDecorator : IObject
        {
            [Template("{this}({0})")]
            [Where("TFunction", typeof(es5.Function), EnableImplicitConversion = true)]
            public extern Union<TFunction, Transpose.Core.Void> Invoke<TFunction>(
              TFunction target);
        }
    }
}
