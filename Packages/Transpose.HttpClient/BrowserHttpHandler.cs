using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using static Transpose.Core.dom;
using static Transpose.Core.es5;

namespace System.Net.Http
{
    internal sealed class BrowserHttpHandler : HttpMessageHandler
    {
        public bool AllowAutoRedirect { get; set; } = true;
        public int MaxAutomaticRedirections { get; set; } = 50;

        protected internal override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            // A redirect is followed by re-sending the same request message, so this is a loop rather
            // than recursion: the old code re-sent from inside the readystate callback and then fell
            // through to the ordinary "hand the response back" path below it, so the 3xx always won the
            // race and the response it went on to fetch was computed and dropped.
            var redirectsLeft = AllowAutoRedirect ? MaxAutomaticRedirections : 0;

            while (true)
            {
                var response = await SendOnceAsync(request, cancellationToken);

                if (redirectsLeft <= 0 || !IsRedirect(response.StatusCode))
                {
                    return response;
                }

                // Note this reads the header off the transport rather than off the response: the
                // response's own header collection is deliberately empty (see HttpHeaders).
                var location = request._request.getResponseHeader("Location");
                if (string.IsNullOrEmpty(location))
                {
                    return response; // A 3xx with nowhere to go is just the response.
                }

                redirectsLeft--;
                request.MarkAsRedirected();
                request.RequestUri = ResolveLocation(request.RequestUri, location);

                // 303 always becomes a GET, and a 301/302 on a method with a body does too — that is
                // what every client and server on the web has settled on, whatever RFC 7231 says. A
                // 307/308 is the pair that exists precisely to keep the method and body.
                if (response.StatusCode == HttpStatusCode.SeeOther ||
                    ((response.StatusCode == HttpStatusCode.MovedPermanently || response.StatusCode == HttpStatusCode.Found) && request.Content is object))
                {
                    var dropped = request.Content;

                    request.Method = HttpMethod.Get;
                    request.Content = null;

                    // The headers the body contributed were merged into the request on the way out and
                    // would otherwise be sent again describing a body that no longer exists.
                    if (dropped is object)
                    {
                        foreach (var header in dropped.Headers)
                        {
                            request.Headers.Remove(header.Key);
                        }
                    }
                }
            }
        }

        private static bool IsRedirect(HttpStatusCode status)
        {
            return status == HttpStatusCode.MovedPermanently   // 301
                || status == HttpStatusCode.Found              // 302
                || status == HttpStatusCode.SeeOther           // 303
                || status == HttpStatusCode.TemporaryRedirect  // 307
                || status == HttpStatusCode.PermanentRedirect; // 308
        }

        /// <summary>
        /// Resolves a <c>Location</c> against the URI it was returned for. Absolute wins outright; a
        /// leading "/" replaces the whole path; anything else is relative to the requested directory.
        /// </summary>
        private static Uri ResolveLocation(Uri requestUri, string location)
        {
            var current = requestUri.ToString();
            var schemeEnd = current.IndexOf("://");

            if (location.IndexOf("://") > 0)
            {
                return new Uri(location);
            }

            // "//host/path" keeps the scheme it was served over rather than losing it.
            if (location.StartsWith("//"))
            {
                return new Uri((schemeEnd < 0 ? "https:" : current.Substring(0, schemeEnd + 1)) + location);
            }

            var authorityEnd = schemeEnd < 0 ? -1 : current.IndexOf('/', schemeEnd + 3);
            var origin = authorityEnd < 0 ? current : current.Substring(0, authorityEnd);

            if (location.StartsWith("/"))
            {
                return new Uri(origin + location);
            }

            var path = authorityEnd < 0 ? "/" : current.Substring(authorityEnd);
            var lastSlash = path.LastIndexOf('/');
            var directory = lastSlash < 0 ? "/" : path.Substring(0, lastSlash + 1);
            return new Uri(origin + directory + location);
        }

        private Task<HttpResponseMessage> SendOnceAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<HttpResponseMessage>();

            // A token that was already cancelled has to fail as a cancellation like any other. It used
            // to reach CancellationToken.Register, which runs its callback synchronously for an
            // already-cancelled token — and that callback disposed the very source Register was still
            // appending its registration to, so the caller got a raw JavaScript error instead.
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled();
                return tcs.Task;
            }

            var requestObject = request._request;

            requestObject.open(request.Method.Method, request.RequestUri.AbsoluteUri);

            // How the browser should decode the body. GetStringAsync/GetByteArrayAsync/GetBlobAsync/
            // GetObjectLiteralAsync each declare one, and it has to be set before send() — nothing used
            // to copy it across at all, so every body came back as text however it was asked for.
            if (request.ResponseType is object)
            {
                requestObject.responseType = request.ResponseType;
            }

            // Content headers belong to the request too (Content-Type above all), and they have to be
            // merged BEFORE the headers are applied: a real XMLHttpRequest drops anything set before
            // open() and the applying is what actually puts them on the wire, so merging afterwards —
            // which is what this used to do — sent none of them.
            if (request.Content is object)
            {
                request.Headers.AddHeaders(request.Content.Headers);
            }

            // Only after open(): a real XMLHttpRequest discards request headers set before it.
            request.Headers.ApplyHeadersToRequest(requestObject);

            var abortRegistration = cancellationToken.Register(() => requestObject.abort());

            requestObject.onreadystatechange = (e) =>
            {
                // A cancelled request is reported by abort(), which lands here having reset readyState.
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled();
                    return;
                }

                if (requestObject.readyState == 0)
                {
                    tcs.TrySetCanceled();
                    return;
                }

                if (requestObject.readyState != 4 /*AjaxReadyState.Done*/)
                {
                    return;
                }

                // Status 0 on a completed request is the browser's way of saying the request never made
                // it: a CORS rejection, a DNS failure, an offline tab. There is no response to hand back
                // — which is why this used to surface as a nonsensical status-0 HttpResponseMessage —
                // so it fails the way .NET fails a request that could not be sent.
                if (requestObject.status == 0)
                {
                    tcs.TrySetException(new HttpRequestException("An error occurred while sending the request."));
                    return;
                }

                var httpResponse = new HttpResponseMessage((HttpStatusCode)requestObject.status, requestObject);
                httpResponse.RequestMessage = request;
                httpResponse.Content = new BrowserHttpContent(requestObject);

                tcs.TrySetResult(httpResponse);
            };

            if (request.Content is object)
            {
                if (request.Content is StringContent stringContent)
                {
                    requestObject.send(stringContent.Content);
                }
                else if (request.Content is FormContent formContent)
                {
                    requestObject.send(formContent.Content);
                }
                else
                {
                    requestObject.send();
                }
            }
            else
            {
                requestObject.send();
            }

            return Finish(tcs.Task, abortRegistration);
        }

        /// <summary>Awaits the response and releases the cancellation registration either way.</summary>
        private static async Task<HttpResponseMessage> Finish(Task<HttpResponseMessage> task, CancellationTokenRegistration registration)
        {
            try
            {
                return await task;
            }
            finally
            {
                registration.Dispose();
            }
        }

        private sealed class BrowserHttpContent : HttpContent
        {
            public BrowserHttpContent(XMLHttpRequest request) : base(request)
            {
            }
        }
    }
}
