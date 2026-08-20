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

Every one of these is asserted on **both** sides — the recorded translated output and what native .NET
prints for the same thing — so a note here cannot quietly rot into something else.

### The header model, by design

`HttpHeaders` is one `Dictionary<string, string>`: no multi-value store, no per-header parsers, no
strongly typed collections (`HttpRequestHeaders.Accept` and friends), no validation. A browser app does
not need them and they are a lot of code to carry into a bundle. What follows:

- **One value per header name.** A second `Add` for the same name throws instead of appending to a
  comma-separated field (`HeaderTests.AddingASecondValueForAHeaderThrows`). Write the list yourself in
  one `Add`.
- **Merging leaves an existing name alone**, since there is no list to merge into: a header set on the
  request beats the client's default, and beats the one its content contributes
  (`HeaderTests.ARequestHeaderWinsOverTheClientDefault`,
  `ContentTests.AContentTypeOnTheRequestWinsOverTheContentsOwn`).
- **A response's headers are never populated.** `response.Headers` is always a real, empty collection —
  the package does not parse a response's headers into a store
  (`HeaderTests.ResponseHeadersAreEmptyAndDoNotThrow`). Read one off the `XMLHttpRequest` if you need it.
- **No `TryGetValues`/`GetValues` and no typed accessors.** `Add`/`Contains`/`Remove`/`Clear` and
  enumeration are the whole surface.
- **`StringContent` writes the bare media type**, not `text/plain; charset=utf-8`: the body goes to
  `XMLHttpRequest.send` as a string, and a browser encodes that as UTF-8 and says so itself
  (`ContentTests.StringContentSendsItsMediaTypeAsContentType`).

### Elsewhere, by design

- **A relative request URI is appended to `BaseAddress`** on exactly one `/`, rather than resolved as a
  `Uri` (`RequestTests.ARelativeUriIsAppendedToTheBaseAddress`). .NET treats a leading `/` as
  root-relative and a base with no trailing `/` as naming a resource, and both of those rules drop path
  segments the caller wrote down.
- **`HttpRequestMessage.Version` starts null**, where .NET defaults it to 1.1. Nothing in a browser can
  act on it either way.
- **`HttpMethod`'s constructor is private**, so only the eight predefined verbs exist — a vendor verb
  (`LINK`, `PROPFIND`) cannot be expressed.
- **`EnsureSuccessStatusCode`'s message writes empty parentheses** for a status with no reason phrase,
  where .NET omits them.

## Fixed here (was broken)

Each of these had a test pinning the broken behaviour; the test now asserts the fix.

| what was wrong | now | test |
| --- | --- | --- |
| `response.Headers` was a null dereference — the response never stored the `XMLHttpRequest` it was constructed with, and the getter reached for it through `RequestMessage`, which the handler never set. **No response header could be touched at all.** | always a real, empty collection | `HeaderTests.ResponseHeadersAreEmptyAndDoNotThrow` |
| `StringContent`'s media type never became a `Content-Type` header, so a JSON POST went out with none | sent as `Content-Type` | `ContentTests.StringContentSendsItsMediaTypeAsContentType` |
| merging headers used `Dictionary.Add`, so a name on both sides threw `ArgumentException` and failed the request | existing name wins | `HeaderTests.ARequestHeaderWinsOverTheClientDefault` |
| `ResponseType` was set by the typed reads and never copied onto the `XMLHttpRequest`, so every body was decoded as text — `GetByteArrayAsync`/`GetBlobAsync` returned the response *string* | applied before `send()` | `ContentTests.EachTypedReadSetsItsResponseType` |
| content headers were merged *after* the headers were applied to the transport, i.e. after the only moment they could reach the wire | merged first | `ContentTests.StringContentSendsItsMediaTypeAsContentType` |
| a 3xx was returned to the caller: the redirect branch fell through to the ordinary `TrySetResult`, `Location` was read and never used, and the retry reused the same `XMLHttpRequest` | followed, as a loop rather than callback recursion; 303 (and a 301/302 with a body) becomes a GET | `RedirectTests.*` |
| `AllowAutoRedirect` was forwarded to the handler and never read | honoured | `RedirectTests.AllowAutoRedirectFalseHandsBackTheRedirect` |
| an already-cancelled token raised a raw JavaScript error — see the runtime note below | `TaskCanceledException`, nothing sent | `CancellationTests.AnAlreadyCancelledTokenCancels` |
| reading content with no request behind it (`EmptyContent`, i.e. any response built in code) was a null dereference | `""` | `ResponseTests.ContentWithNoRequestBehindItReadsAsEmpty` |
| a transport failure (CORS, DNS, offline) became a status-0 *response* | `HttpRequestException` | `ResponseTests.ATransportFailureThrowsHttpRequestException` |
| joining `BaseAddress` with a relative URI concatenated blindly, producing `//` or no separator at all | joined on one `/` | `RequestTests.ARelativeUriIsAppendedToTheBaseAddress` |

Two of these were **runtime** bugs rather than package bugs, found through this suite and fixed in the
BCL (with regression tests in `EmitRegressionTests`, since neither is about HTTP):

- `CancellationTokenSource.CreateLinkedTokenSource` where one token is already cancelled. Registering
  on it runs the callback synchronously, so the new source cancels while the runtime is still building
  its list of links — and cancelling cleans up, which nulled that list out from under the loop filling
  it. This is what made an already-cancelled token fail obscurely.
- A negative `TimeSpan` lost its fractional part when formatted (`TimeSpan.FromMilliseconds(-1)` →
  `-00:00:00`), which is how an infinite `HttpClient.Timeout` rendered.
