namespace Transpose
{
    /// <summary>
    /// Makes the method to be called once the page is loaded. If using jQuery2, triggers jQuery's event,
    /// otherwise, uses DOMContentReady event from HTML5.
    /// </summary>
    [NonScriptable]
    public class ReadyAttribute : AdapterAttribute
    {
        public const string Format = "Transpose.ready(this.{2});";
        public const string FormatScope = "Transpose.ready(this.{2}, this);";
        public const string Event = "ready";
        public const bool StaticOnly = true;

        public ReadyAttribute()
        {
        }
    }
}