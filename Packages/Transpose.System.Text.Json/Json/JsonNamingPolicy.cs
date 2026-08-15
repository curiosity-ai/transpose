namespace System.Text.Json
{
    /// <summary>
    /// Renames a .NET member for the JSON payload. Derive from this to supply your own policy.
    /// </summary>
    public abstract class JsonNamingPolicy
    {
        /// <summary>Initializes a new instance.</summary>
        protected JsonNamingPolicy()
        {
        }

        /// <summary>The built-in policy that converts <c>PascalCase</c> to <c>camelCase</c>.</summary>
        public static JsonNamingPolicy CamelCase { get; } = new CamelCaseNamingPolicy();

        /// <summary>The built-in policy that converts to <c>snake_case</c>.</summary>
        public static JsonNamingPolicy SnakeCaseLower { get; } = new SnakeCaseNamingPolicy("_", false);

        /// <summary>The built-in policy that converts to <c>SNAKE_CASE</c>.</summary>
        public static JsonNamingPolicy SnakeCaseUpper { get; } = new SnakeCaseNamingPolicy("_", true);

        /// <summary>The built-in policy that converts to <c>kebab-case</c>.</summary>
        public static JsonNamingPolicy KebabCaseLower { get; } = new SnakeCaseNamingPolicy("-", false);

        /// <summary>The built-in policy that converts to <c>KEBAB-CASE</c>.</summary>
        public static JsonNamingPolicy KebabCaseUpper { get; } = new SnakeCaseNamingPolicy("-", true);

        /// <summary>Returns the JSON name for <paramref name="name"/>.</summary>
        public abstract string ConvertName(string name);
    }

    /// <summary>
    /// Lower-cases the leading run of capitals rather than only the first character, so
    /// <c>ALLCAPS</c> becomes <c>allcaps</c> and <c>HTTPRequest</c> becomes <c>httpRequest</c> —
    /// what System.Text.Json itself does.
    /// </summary>
    internal sealed class CamelCaseNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
        {
            if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0])) return name;

            var chars = name.ToCharArray();

            for (int i = 0; i < chars.Length; i++)
            {
                if (i > 0 && i + 1 < chars.Length && !char.IsUpper(chars[i + 1])) break;
                if (!char.IsUpper(chars[i])) break;

                chars[i] = char.ToLower(chars[i]);
            }

            return new string(chars);
        }
    }

    /// <summary>Separates word boundaries with <c>separator</c>, optionally upper-casing the result.</summary>
    internal sealed class SnakeCaseNamingPolicy : JsonNamingPolicy
    {
        private readonly string _separator;
        private readonly bool   _upper;

        public SnakeCaseNamingPolicy(string separator, bool upper)
        {
            _separator = separator;
            _upper     = upper;
        }

        public override string ConvertName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var sb = new System.Text.StringBuilder();

            for (int i = 0; i < name.Length; i++)
            {
                var c = name[i];

                if (char.IsUpper(c))
                {
                    var previousIsLower = i > 0 && !char.IsUpper(name[i - 1]) && name[i - 1] != '_';
                    var nextIsLower     = i + 1 < name.Length && !char.IsUpper(name[i + 1]);

                    if (i > 0 && (previousIsLower || nextIsLower)) sb.Append(_separator);

                    sb.Append(_upper ? char.ToUpper(c) : char.ToLower(c));
                }
                else
                {
                    sb.Append(_upper ? char.ToUpper(c) : c);
                }
            }

            return sb.ToString();
        }
    }
}
