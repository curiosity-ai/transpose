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
        [StaticInterface("RegExpConstructor")]
        [FormerInterface]
        public class RegExp : IObject
        {

            public extern RegExp(Union<es5.RegExp, string> pattern);

            public extern RegExp(es5.RegExp pattern);

            public extern RegExp(string pattern);

            public extern RegExp(string pattern, string flags);

            public static es5.RegExp prototype
            {
                get;
            }

            [Name("$1")]
            public static string Dollar1
            {
                get; set;
            }

            [Name("$2")]
            public static string Dollar2
            {
                get; set;
            }

            [Name("$3")]
            public static string Dollar3
            {
                get; set;
            }

            [Name("$4")]
            public static string Dollar4
            {
                get; set;
            }

            [Name("$5")]
            public static string Dollar5
            {
                get; set;
            }

            [Name("$6")]
            public static string Dollar6
            {
                get; set;
            }

            [Name("$7")]
            public static string Dollar7
            {
                get; set;
            }

            [Name("$8")]
            public static string Dollar8
            {
                get; set;
            }

            [Name("$9")]
            public static string Dollar9
            {
                get; set;
            }

            public static string lastMatch
            {
                get; set;
            }

            public static extern es5.RegExp Self(Union<es5.RegExp, string> pattern);

            public static extern es5.RegExp Self(es5.RegExp pattern);

            public static extern es5.RegExp Self(string pattern);

            public static extern es5.RegExp Self(string pattern, string flags);

            /// <summary>
            /// Escapes any potential regex syntax characters in a string, so it can safely be
            /// interpolated into a RegExp pattern as a literal match.
            /// </summary>
            /// <param name="string">The string to escape.</param>
            /// <returns>A new string with regex-significant characters escaped.</returns>
            public static extern string escape(string @string);

            public virtual extern es5.RegExpExecArray exec(string @string);

            public virtual extern bool test(string @string);

            public virtual string source
            {
                get;
            }

            /// <summary>
            /// The flags string, containing the letters for every flag set on this regular expression, in the order
            /// d, g, i, m, s, u, v, y.
            /// </summary>
            public virtual string flags
            {
                get;
            }

            public virtual bool global
            {
                get;
            }

            public virtual bool ignoreCase
            {
                get;
            }

            public virtual bool multiline
            {
                get;
            }

            /// <summary>
            /// Whether the "d" flag (hasIndices) is set, requesting the start/end indices of captured substrings.
            /// </summary>
            public virtual bool hasIndices
            {
                get;
            }

            /// <summary>
            /// Whether the "s" flag (dotAll) is set, so "." also matches line terminators.
            /// </summary>
            public virtual bool dotAll
            {
                get;
            }

            /// <summary>
            /// Whether the "u" flag (unicode) is set, enabling Unicode code point semantics.
            /// </summary>
            public virtual bool unicode
            {
                get;
            }

            /// <summary>
            /// Whether the "v" flag (unicodeSets) is set, enabling the ES2024 Unicode set-notation mode.
            /// </summary>
            public virtual bool unicodeSets
            {
                get;
            }

            /// <summary>
            /// Whether the "y" flag (sticky) is set, so matching starts exactly at lastIndex.
            /// </summary>
            public virtual bool sticky
            {
                get;
            }

            public virtual double lastIndex
            {
                get; set;
            }

            public virtual extern es5.RegExp compile();
        }
    }
}
