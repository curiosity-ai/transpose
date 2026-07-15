// Decompiled with JetBrains decompiler
// Type: Transpose.KeyOf`1
// Assembly: Transpose.Core, Version=1.6.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 9E855DC6-9E83-4420-9E6F-8D2B7A117BBD
// Assembly location: C:\work\curiosity\tesserae\Tesserae\bin\Debug\net461\Transpose.Core.dll

using Transpose;

namespace Transpose.Core
{
    [IgnoreGeneric(AllowInTypeScript = true)]
    [IgnoreCast]
    [Transpose.Name("String")]
    [ExportedAs("KeyOf")]
    public class KeyOf<T>
    {
        [Template("{this}")]
        public static readonly string Name;

        private extern KeyOf();
    }
}
