using static Transpose.Core.dom;

namespace System.Net.Http
{
    public class StringContent : HttpContent
    {
        private const string DefaultMediaType = "text/plain";

        public StringContent(string content) : this(content, DefaultMediaType)
        {
        }

        public StringContent(string content, string mediaType)
        {
            MediaType = mediaType;
            Content = content;

            // The media type is only useful if it reaches the wire. It used to live in the MediaType
            // property and nowhere else, so a JSON POST went out with no content type at all and any
            // server that dispatches on one rejected it. No charset is appended: the body is handed to
            // XMLHttpRequest.send as a string, and a browser encodes that as UTF-8 and says so itself.
            if (!string.IsNullOrEmpty(mediaType))
            {
                Headers.Add("Content-Type", mediaType);
            }
        }

        public string Content { get; }
        public string MediaType { get; }
    }

    public class FormContent : HttpContent
    {
        public FormContent(FormData content)
        {
            Content = content;
        }

        public FormData Content { get; }
    }

    public class EmptyContent  : HttpContent
    {

    }
}