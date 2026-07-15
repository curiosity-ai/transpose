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
        public interface DataViewConstructor : IObject
        {
            [Template("new {this}({0})")]
            es5.DataView New(es5.ArrayBufferLike buffer);

            [Template("new {this}({0})")]
            es5.DataView New(es5.ArrayBuffer buffer);

            [Template("new {this}({0}, {1})")]
            es5.DataView New(es5.ArrayBufferLike buffer, double byteOffset);

            [Template("new {this}({0}, {1})")]
            es5.DataView New(es5.ArrayBuffer buffer, double byteOffset);

            [Template("new {this}({0}, {1}, {2})")]
            es5.DataView New(es5.ArrayBufferLike buffer, double byteOffset, double byteLength);

            [Template("new {this}({0}, {1}, {2})")]
            es5.DataView New(es5.ArrayBuffer buffer, double byteOffset, double byteLength);
        }
    }
}
