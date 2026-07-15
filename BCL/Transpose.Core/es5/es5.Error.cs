// Decompiled with JetBrains decompiler
// Type: Transpose.es5
// Assembly: Transpose.es5, Version=2.8.2.0, Culture=neutral, PublicKeyToken=null
// MVID: EC57AC2B-0E02-4A1C-B567-F790F377783B
// Assembly location: C:\work\curiosity\tesserae\Tesserae\bin\Debug\net461\Transpose.es5.dll

namespace Transpose.Core
{
    public  static partial class es5
    {
        [CombinedClass]
        [StaticInterface("ErrorConstructor")]
        [FormerInterface]
        public class Error : IObject
        {
            public extern Error();

            public extern Error(string message);

            public static es5.Error prototype
            {
                get;
            }

            public static extern es5.Error Self();

            public static extern es5.Error Self(string message);

            public virtual string name
            {
                get; set;
            }

            public virtual string message
            {
                get; set;
            }

            public virtual string stack
            {
                get; set;
            }
        }
    }
}
