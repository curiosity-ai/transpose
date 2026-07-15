// Decompiled with JetBrains decompiler
// Type: Transpose.Core.StringConstructor
// Assembly: Transpose.es5, Version=2.8.2.0, Culture=neutral, PublicKeyToken=null
// MVID: EC57AC2B-0E02-4A1C-B567-F790F377783B
// Assembly location: C:\work\curiosity\tesserae\Tesserae\bin\Debug\net461\Transpose.es5.dll

using Transpose;

namespace Transpose.Core
{
    [IgnoreCast]
    [Namespace(false)]
    [Virtual]
    public interface StringConstructor : IObject
    {
        [Template("new {this}()")]
        String New();

        [Template("new {this}({0})")]
        String New(object value);

        [Template("{this}()")]
        string Self();

        [Template("{this}({0})")]
        string Self(object value);

        String prototype { get; }

        [ExpandParams]
        string fromCharCode(params double[] codes);
    }
}
