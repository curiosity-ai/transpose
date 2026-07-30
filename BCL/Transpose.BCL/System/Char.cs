namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Constructor("Number")]
    [Transpose.Reflectable]
    public struct Char : IComparable, IComparable<char>, IEquatable<char>, IFormattable
    {
        private extern Char(int i);

        [Transpose.InlineConst]
        public const char MinValue = '\0';

        [Transpose.InlineConst]
        public const char MaxValue = '\xFFFF';

        [Transpose.Template("System.Char.format({this}, {0})")]
        public extern string Format(string format);

        [Transpose.Template("System.Char.format({this}, {0}, {1})")]
        public extern string Format(string format, IFormatProvider provider);

        [Transpose.Template("System.Char.charCodeAt({0}, 0)")]
        public static extern char Parse(string s);

        [Transpose.Template(Fn = "String.fromCharCode")]
        public override extern string ToString();

        [Transpose.Template("System.Char.format({this}, {format})")]
        public extern string ToString(string format);

        [Transpose.Template("System.Char.format({this}, {format}, {provider})")]
        public extern string ToString(string format, IFormatProvider provider);

        [Transpose.Template("Transpose.compare({this}, {0})")]
        public extern int CompareTo(char value);

        [Transpose.Template("Transpose.compare({this}, {0})")]
        public extern int CompareTo(object value);

        [Transpose.Template("{this} === {0}")]
        public extern bool Equals(char obj);

        [Transpose.Template("Transpose.isLower({0})", Fn = "Transpose.isLower")]
        public static extern bool IsLower(char s);

        [Transpose.Template("Transpose.isUpper({0})", Fn = "Transpose.isUpper")]
        public static extern bool IsUpper(char s);

        /// <summary>
        /// Indicates whether the character at the specified position in a specified string is categorized as an uppercase letter.
        /// </summary>
        /// <param name="s">A string.</param>
        /// <param name="index">The position of the character to evaluate in s.</param>
        /// <returns>true if the character at position index in s is an uppercase letter; otherwise, false.</returns>
        public extern static bool IsUpper(string s, int index);

        [Transpose.Template("String.fromCharCode({0}).toLowerCase().charCodeAt(0)")]
        public static extern char ToLower(char c);

        [Transpose.Template("String.fromCharCode({0}).toUpperCase().charCodeAt(0)")]
        public static extern char ToUpper(char c);

        public static extern bool IsLetter(char c);

        [Transpose.Template("System.Char.isLetter(({0}).charCodeAt({1}))", Fn = "function ($s, $i) { return System.Char.isLetter($s.charCodeAt($i)); }")]
        public static extern bool IsLetter(string s, int index);

        public static extern bool IsDigit(char c);

        [Transpose.Template("System.Char.isDigit(({0}).charCodeAt({1}))", Fn = "function ($s, $i) { return System.Char.isDigit($s.charCodeAt($i)); }")]
        public static extern bool IsDigit(string s, int index);

        [Transpose.Template("(System.Char.isDigit({0}) || System.Char.isLetter({0}))", Fn = "function ($c) { return System.Char.isDigit($c) || System.Char.isLetter($c); }")]
        public static extern bool IsLetterOrDigit(char c);

        [Transpose.Template("(System.Char.isDigit(({0}).charCodeAt({1})) || System.Char.isLetter(({0}).charCodeAt({1})))", Fn = "function ($s, $i) { var $c = $s.charCodeAt($i); return System.Char.isDigit($c) || System.Char.isLetter($c); }")]
        public static extern bool IsLetterOrDigit(string s, int index);

        [Transpose.Template("System.Char.isWhiteSpace(String.fromCharCode({0}))", Fn = "function ($c) { return System.Char.isWhiteSpace(String.fromCharCode($c)); }")]
        public static extern bool IsWhiteSpace(char c);

        [Transpose.Template("System.Char.isWhiteSpace(({0}).charAt({1}))", Fn = "function ($s, $i) { return System.Char.isWhiteSpace($s.charAt($i)); }")]
        public static extern bool IsWhiteSpace(string s, int index);

        public static extern bool IsHighSurrogate(char c);

        [Transpose.Template("System.Char.isHighSurrogate(({0}).charCodeAt({1}))", Fn = "function ($s, $i) { return System.Char.isHighSurrogate($s.charCodeAt($i)); }")]
        public static extern bool IsHighSurrogate(string s, int index);

        public static extern bool IsLowSurrogate(char c);

        [Transpose.Template("System.Char.isLowSurrogate(({0}).charCodeAt({1}))", Fn = "function ($s, $i) { return System.Char.isLowSurrogate($s.charCodeAt($i)); }")]
        public static extern bool IsLowSurrogate(string s, int index);

        public static extern bool IsSurrogate(char c);

        [Transpose.Template("System.Char.isSurrogate(({0}).charCodeAt({1}))", Fn = "function ($s, $i) { return System.Char.isSurrogate($s.charCodeAt($i)); }")]
        public static extern bool IsSurrogate(string s, int index);

        [Transpose.Template("(System.Char.isHighSurrogate({0}) && System.Char.isLowSurrogate({1}))", Fn = "function ($hi, $lo) { return System.Char.isHighSurrogate($hi) && System.Char.isLowSurrogate($lo); }")]
        public static extern bool IsSurrogatePair(char highSurrogate, char lowSurrogate);

        [Transpose.Template("(System.Char.isHighSurrogate(({0}).charCodeAt({1})) && System.Char.isLowSurrogate(({0}).charCodeAt({1} + 1)))", Fn = "function ($s, $i) { return System.Char.isHighSurrogate($s.charCodeAt($i)) && System.Char.isLowSurrogate($s.charCodeAt($i + 1)); }")]
        public static extern bool IsSurrogatePair(string s, int index);

        public static extern bool IsSymbol(char c);

        [Transpose.Template("System.Char.isSymbol(({0}).charCodeAt({1}))", Fn = "function ($s, $i) { return System.Char.isSymbol($s.charCodeAt($i)); }")]
        public static extern bool IsSymbol(string s, int index);

        public static extern bool IsSeparator(char c);

        [Transpose.Template("System.Char.isSeparator(({0}).charCodeAt({1}))", Fn = "function ($s, $i) { return System.Char.isSeparator($s.charCodeAt($i)); }")]
        public static extern bool IsSeparator(string s, int index);

        public static extern bool IsPunctuation(char c);

        [Transpose.Template("System.Char.isPunctuation(({0}).charCodeAt({1}))", Fn = "function ($s, $i) { return System.Char.isPunctuation($s.charCodeAt($i)); }")]
        public static extern bool IsPunctuation(string s, int index);

        public static extern bool IsNumber(char c);

        [Transpose.Template("System.Char.isNumber(({0}).charCodeAt({1}))", Fn = "function ($s, $i) { return System.Char.isNumber($s.charCodeAt($i)); }")]
        public static extern bool IsNumber(string s, int index);

        public static extern bool IsControl(char c);

        [Transpose.Template("System.Char.isControl(({0}).charCodeAt({1}))", Fn = "function ($s, $i) { return System.Char.isControl($s.charCodeAt($i)); }")]
        public static extern bool IsControl(string s, int index);

        [Transpose.Template("System.Char.equals({this}, {0})")]
        public override extern bool Equals(object obj);

        [Transpose.Template(Fn = "System.Char.getHashCode")]
        public override extern int GetHashCode();

        [Transpose.Template("String.fromCharCode({c})")]
        public static extern string ToString(char c);
    }
}