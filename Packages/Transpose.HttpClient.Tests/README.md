# Transpose.HttpClient.Tests

End-to-end tests for the **`Transpose.HttpClient`** package — the `System.Net.Http` surface a Transpose
app compiles against, implemented in real C# on top of the browser's `XMLHttpRequest`.

## How a test works

The package is not a binding over hand-written JavaScript: it is ordinary C# that Transpose compiles
like any other library, and the only thing under it that a browser provides is `XMLHttpRequest`. So a
run is three stages, all in-process (`TranslatedHttpClientRunner`):

1. compile **`Transpose.Core`** — the DOM/ES bindings the transport is written against — into a
   reference assembly. It is `[assembly: External]`, so it emits essentially no JavaScript;
2. compile **`Transpose.HttpClient`** against that reference, keeping both its assembly (so a snippet
   can bind to `System.Net.Http.*`) and its emitted JavaScript, which is the implementation under test;
3. translate the snippet against both references and run
   `runtime + XHR stub + package JS + snippet` on Node.

Stage 1 costs about ten seconds, so the artifacts are cached on disk under a content hash of the two
projects' sources, the translator and the injected `Transpose.dll`. A second run over unchanged sources
starts in well under a second — change any of those inputs and the cache key changes with it.

### The fake transport

[`Infrastructure/xhr-stub.js`](Infrastructure/xhr-stub.js) installs an `XMLHttpRequest` (and a
`FormData`) that answers from a routing table and **records every request it was given**. No server,
no sockets, no timing flake — and both directions of the wire are assertable: the method, URL, headers,
body and `responseType` that went out, and what the package made of what came back.

A snippet drives it in C#, through the external `Xhr` class the runner prepends to every compilation
(`TranslatedHttpClientRunner.HarnessSource`):

```csharp
Xhr.Route("GET", "https://api.test/hello", 200, "hello world");   // + optional response headers
Xhr.RouteJson("GET", "https://api.test/w", 200, "{\"a\":1}");      // responseType "json" shape
Xhr.RouteNetworkError("GET", "https://api.test/dead");             // readyState 4, status 0

var body = await new HttpClient().GetStringAsync("https://api.test/hello");

Console.WriteLine(Xhr.RequestHeaders(0));           // sorted "Name: value" lines
Console.WriteLine(Xhr.RequestHeader(0, "Accept"));  // or "(absent)"
Console.WriteLine(Xhr.RequestBody(0));              // or "(none)"
```

The C# harness and the JavaScript stub are two halves of one thing — change one, change the other.

### Which oracle a test uses

| | when | how |
| --- | --- | --- |
| **the real System.Net.Http** | everything off the wire: `HttpMethod`, `HttpStatusCode`, `ReasonPhrase`, `EnsureSuccessStatusCode`, `HttpRequestOptions`, `Timeout` validation, message state | `RunAndCompare` — the same snippet also compiles against the framework copy, so "what does .NET print" *is* the specification |
| **a recorded expectation** | everything on the wire | `RunJs(code, expected)` — the transport differs by construction, so there is nothing to diff against |

`HttpResponseMessage`'s constructors take the underlying `XMLHttpRequest`, so a snippet that builds a
response by hand cannot compile against both surfaces. `RunAndCompare(code, nativeCode:)` takes an
equivalent snippet for the native side: only the *shape* may differ, what it prints must still match.

For a divergence, `RunJs(code, expected, nativePrints:, nativeCode:)` pins the JavaScript output **and**
re-asserts what native .NET prints — so a note here cannot quietly rot into something else.

```bash
dotnet test Packages/Transpose.HttpClient.Tests

# see the JavaScript a test actually ran
TPS_DUMP_JS=/tmp/out.js dotnet test Packages/Transpose.HttpClient.Tests --filter GetStringAsyncReturnsTheBody
```

## Divergences from System.Net.Http

### By design

The header model is deliberately much simpler than .NET's, and this is not a list of things to fix:
`HttpHeaders` is one `Dictionary<string, string>`, with no multi-value store, no per-header parsers, no
strongly typed collections (`HttpRequestHeaders.Accept` and friends) and no validation. A browser app
does not need them and they are a lot of code to carry into a bundle. What follows from that:

- **One value per header name.** A second `Add` for the same name throws instead of appending to a
  comma-separated field (`HeaderTests.AddingASecondValueForAHeaderThrows`). Write the list yourself in a
  single `Add`.
- **No `TryGetValues`/`GetValues` and no typed accessors.** `Add`/`Contains`/`Remove`/`Clear` and
  enumeration are the whole surface.
- A response's headers are meant to be read straight off the `XMLHttpRequest` rather than parsed into a
  store — a reasonable simplification, but the wiring is broken, so see the bug list.

Also by construction:

- **A relative request URI is appended to `BaseAddress` as a string**, not combined as a `Uri`
  (`RequestTests.ARelativeUriIsAppendedToTheBaseAddress`). Identical for the ordinary shape (base ends
  in `/`, relative does not begin with one); different as soon as either slash convention does.
- **`HttpRequestMessage.Version` starts null**, where .NET defaults it to 1.1. Nothing in a browser can
  act on it either way.
- **`HttpMethod`'s constructor is private**, so only the eight predefined verbs exist — a vendor verb
  (`LINK`, `PROPFIND`) cannot be expressed.
- **`EnsureSuccessStatusCode`'s message writes empty parentheses** for a status with no reason phrase,
  where .NET omits them.

### Bugs

Each has a test that currently passes by asserting the *wrong* behaviour, so a fix turns it red and the
test gets updated with the fix:

| what | test |
| --- | --- |
| `response.Headers` is a null dereference — the response never stores the `XMLHttpRequest` it was constructed with, and its `Headers` getter reaches for it through `RequestMessage`, which the handler never sets. `GetHeaderString` is `internal`, so there is no other way in: **a response header cannot be read at all.** | `HeaderTests.ReadingResponseHeadersThrows` |
| `StringContent`'s media type never becomes a `Content-Type` header, so a JSON POST goes out with no content type | `ContentTests.StringContentSendsNoContentTypeHeader` |
| `HttpRequestMessage.ResponseType` is set by the typed reads and never copied onto the `XMLHttpRequest`, so every body is decoded as text — `GetByteArrayAsync`/`GetBlobAsync` return the response *string* cast to `ArrayBuffer`/`Blob` | `ContentTests.TheResponseTypeIsNeverAppliedToTheRequest` |
| a 302 is returned to the caller instead of being followed: the redirect branch falls through to the ordinary `TrySetResult`, the `Location` header is read and never used, and the retry reuses the same `XMLHttpRequest` | `RedirectTests.A302IsReturnedToTheCallerInsteadOfBeingFollowed` |
| `HttpClientHandler.AllowAutoRedirect` is forwarded to the handler and never read | `RedirectTests.AllowAutoRedirectIsIgnored` |
| an already-cancelled token raises a raw JavaScript error rather than `TaskCanceledException`: the registration's callback runs synchronously inside `Register` and disposes the very source `Register` is still appending to | `CancellationTests.AnAlreadyCancelledTokenThrowsTheWrongException` |
| reading content with no request behind it (`EmptyContent`, i.e. any response the caller built) is a null dereference; .NET reads an empty body as `""` | `ResponseTests.ReadingContentWithNoRequestBehindItThrows` |
| a transport failure (CORS, DNS, offline) becomes a status-0 *response* rather than an `HttpRequestException` | `ResponseTests.ATransportFailureBecomesAStatusZeroResponse` |
