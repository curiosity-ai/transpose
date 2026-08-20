// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Transpose.Core.es5;

namespace System.Net.Http
{
    public abstract class HttpContent
    {
        internal XMLHttpRequest _request;
        private HttpContentHeaders _headers;

        public HttpContentHeaders Headers 
        {
            get
            {
                if (_headers == null)
                {
                    _headers = new HttpContentHeaders(this);
                }
                return _headers;
            }
        }

        internal HttpContent()
        {
        }

        internal HttpContent(XMLHttpRequest request)
        {
            _request = request;
        }

        internal long? GetComputedOrBufferLength()
        {
            return null;
        }

        // A content with no XMLHttpRequest behind it is a response built in code rather than read off
        // the wire (HttpResponseMessage.Content defaults to an EmptyContent). Reading one used to be a
        // null dereference; it now reads as the empty body it is, which is what .NET answers.
        public string ReadAsString() => _request is object ? _request.responseText : "";
        public ArrayBuffer ReadAsArrayBuffer() => _request is object ? _request.response.As<ArrayBuffer>() : null;
        public Blob ReadAsBlob() => _request is object ? _request.response.As<Blob>() : null;
        public T ReadAsObjectLiteral<T>() => _request is object ? _request.response.As<T>() : default(T);
    }
}