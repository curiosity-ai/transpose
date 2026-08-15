using System.Text.Json.Serialization;

namespace System.Text.Json
{
    /// <summary>
    /// Options that control how <see cref="JsonSerializer"/> reads and writes JSON.
    /// </summary>
    public sealed class JsonSerializerOptions
    {
        /// <summary>A read-only instance carrying the library defaults.</summary>
        public static JsonSerializerOptions Default { get; } = new JsonSerializerOptions();

        /// <summary>Initializes options with the library defaults.</summary>
        public JsonSerializerOptions()
        {
        }

        /// <summary>Initializes options with the presets of <paramref name="defaults"/>.</summary>
        public JsonSerializerOptions(JsonSerializerDefaults defaults)
        {
            if (defaults == JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true;
                PropertyNamingPolicy        = JsonNamingPolicy.CamelCase;
                NumberHandling              = JsonNumberHandling.AllowReadingFromString;
            }
        }

        /// <summary>Copies the settings of <paramref name="options"/>.</summary>
        public JsonSerializerOptions(JsonSerializerOptions options)
        {
            if (options is null) return;

            WriteIndented               = options.WriteIndented;
            PropertyNamingPolicy        = options.PropertyNamingPolicy;
            DictionaryKeyPolicy         = options.DictionaryKeyPolicy;
            PropertyNameCaseInsensitive = options.PropertyNameCaseInsensitive;
            DefaultIgnoreCondition      = options.DefaultIgnoreCondition;
            NumberHandling              = options.NumberHandling;
            AllowTrailingCommas         = options.AllowTrailingCommas;
            ReadCommentHandling         = options.ReadCommentHandling;
            IncludeFields               = options.IncludeFields;
            MaxDepth                    = options.MaxDepth;
        }

        /// <summary>Whether the written JSON is pretty-printed. Defaults to <c>false</c>.</summary>
        public bool WriteIndented { get; set; }

        /// <summary>
        /// Renames members on write and matches them on read. <c>null</c> (the default) uses the
        /// member name as declared.
        /// </summary>
        public JsonNamingPolicy PropertyNamingPolicy { get; set; }

        /// <summary>Renames dictionary keys on write. <c>null</c> (the default) leaves them as-is.</summary>
        public JsonNamingPolicy DictionaryKeyPolicy { get; set; }

        /// <summary>
        /// Whether a JSON member name matches a .NET member ignoring case. Defaults to <c>false</c> —
        /// System.Text.Json is case-sensitive where Json.NET was not.
        /// </summary>
        public bool PropertyNameCaseInsensitive { get; set; }

        /// <summary>When a member is skipped on write. Defaults to <see cref="JsonIgnoreCondition.Never"/>.</summary>
        public JsonIgnoreCondition DefaultIgnoreCondition { get; set; } = JsonIgnoreCondition.Never;

        /// <summary>
        /// Whether numbers may be read from (and written as) JSON strings. Defaults to
        /// <see cref="JsonNumberHandling.Strict"/>.
        /// </summary>
        public JsonNumberHandling NumberHandling { get; set; } = JsonNumberHandling.Strict;

        /// <summary>Whether a trailing comma is accepted on read. Defaults to <c>false</c>.</summary>
        public bool AllowTrailingCommas { get; set; }

        /// <summary>How comments are treated on read. Defaults to <see cref="JsonCommentHandling.Disallow"/>.</summary>
        public JsonCommentHandling ReadCommentHandling { get; set; } = JsonCommentHandling.Disallow;

        /// <summary>Whether public fields take part in serialization. Defaults to <c>false</c>.</summary>
        public bool IncludeFields { get; set; }

        /// <summary>The maximum nesting depth. <c>0</c> (the default) means 64.</summary>
        public int MaxDepth { get; set; }
    }
}
