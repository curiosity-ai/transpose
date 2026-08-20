// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http.Headers;

namespace System.Net.Http
{
    public class HttpResponseMessage : IDisposable
    {
        private const HttpStatusCode DefaultStatusCode = HttpStatusCode.OK;

        private HttpStatusCode _statusCode;
        private HttpResponseHeaders _headers;
        private string _reasonPhrase;
        private HttpRequestMessage _requestMessage;
        private HttpContent _content;
        private bool _disposed;

        // The XMLHttpRequest this response was read from, or null for a response built in code. Kept so
        // the Headers collection below has something to be backed by; it used to be taken by the
        // constructor and dropped on the floor, which made every `response.Headers` a null dereference.
        private readonly Transpose.Core.dom.XMLHttpRequest _request;

        public HttpContent Content
        {
            get
            {
                if (_content is null) _content = new EmptyContent();
                return _content;
            }
            set
            {
                CheckDisposed();
                _content = value;
            }
        }

        public HttpStatusCode StatusCode
        {
            get { return _statusCode; }
            set
            {
                if (((int)value < 0) || ((int)value > 999))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                CheckDisposed();

                _statusCode = value;
            }
        }

        internal void SetStatusCodeWithoutValidation(HttpStatusCode value) => _statusCode = value;

        public string ReasonPhrase
        {
            get
            {
                if (_reasonPhrase != null)
                {
                    return _reasonPhrase;
                }
                // Provide a default if one was not set.
                return HttpStatusDescription.Get(StatusCode);
            }
            set
            {
                if ((value != null) && ContainsNewLineCharacter(value))
                {
                    throw new FormatException("The reason phrase must not contain new-line characters.");
                }
                CheckDisposed();

                _reasonPhrase = value; // It's OK to have a 'null' reason phrase.
            }
        }

        internal void SetReasonPhraseWithoutValidation(string value) => _reasonPhrase = value;

        /// <summary>
        /// The response's header collection. Always a real collection and never throws — a response the
        /// transport produced is backed by its XMLHttpRequest, one built in code by nothing at all — but
        /// it is <b>empty</b> either way: this package does not parse a response's headers into a store
        /// (see the header-model note on <see cref="Headers.HttpHeaders"/>), so enumerating it or asking
        /// it whether it contains a header answers as it would for a response that carried none.
        /// </summary>
        public HttpResponseHeaders Headers
        {
            get
            {
                if (_headers is null) _headers = new HttpResponseHeaders(_request ?? _requestMessage?._request);
                return _headers;
            }
        }

        public HttpRequestMessage RequestMessage
        {
            get { return _requestMessage; }
            set
            {
                CheckDisposed();
                _requestMessage = value;
            }
        }

        public bool IsSuccessStatusCode
        {
            get { return ((int)_statusCode >= 200) && ((int)_statusCode <= 299); }
        }

        public HttpResponseMessage(Transpose.Core.dom.XMLHttpRequest requestObject) : this(DefaultStatusCode, requestObject)
        {
        }

        public HttpResponseMessage(HttpStatusCode statusCode, Transpose.Core.dom.XMLHttpRequest requestObject)
        {
            if (((int)statusCode < 0) || ((int)statusCode > 999))
            {
                throw new ArgumentOutOfRangeException(nameof(statusCode));
            }

            _statusCode = statusCode;
            _request = requestObject;
        }

        public HttpResponseMessage EnsureSuccessStatusCode()
        {
            if (!IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    message: $"Response status code does not indicate success: {(int)_statusCode} ({ReasonPhrase}).",
                    inner: null,
                    statusCode: _statusCode);
            }

            return this;
        }

        private bool ContainsNewLineCharacter(string value)
        {
            foreach (char character in value)
            {
                if ((character == '\r') || (character == '\n'))
                {
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            _disposed = true;

        }

        private void CheckDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(this.GetType().ToString());
            }
        }
    }
}