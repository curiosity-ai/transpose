// A fake XMLHttpRequest for the Transpose.HttpClient test suite.
//
// The package is a *browser* HTTP stack: every request it makes goes through the XMLHttpRequest
// global, and Node has no such global. Rather than start a real server (slow, and it would put the
// operating system's socket behaviour inside every assertion), the suite installs this stub before
// the program under test runs. It answers from a routing table the snippet declares up front and
// records every request it was given, so a test can assert on both directions of the wire — the
// method, URL, headers and body that went out, and what the package made of what came back.
//
// The C# side of this is the `Xhr` external class the runner prepends to each snippet
// (TranslatedHttpClientRunner.HarnessSource); the two must stay in step.
(function (global) {
    "use strict";

    var routes = [];
    var requests = [];

    // "Name: value\nName2: value2" → { Name: "value", Name2: "value2" }. The empty string is no
    // headers at all, which is the common case and must not produce a bogus "" entry.
    function parseHeaders(text) {
        var out = {};
        if (text === null || text === undefined || text === "") { return out; }
        var lines = String(text).split("\n");
        for (var i = 0; i < lines.length; i++) {
            var line = lines[i];
            if (line === "") { continue; }
            var colon = line.indexOf(":");
            if (colon < 0) { out[line.trim()] = ""; continue; }
            out[line.slice(0, colon).trim()] = line.slice(colon + 1).trim();
        }
        return out;
    }

    // Sorted so a test asserting on a whole header set does not depend on insertion order.
    function formatHeaders(map) {
        var names = Object.keys(map).sort();
        var out = [];
        for (var i = 0; i < names.length; i++) { out.push(names[i] + ": " + map[names[i]]); }
        return out.join("\n");
    }

    function match(method, url) {
        // Last route registered for a method/URL pair wins, so a test can override a broader one.
        for (var i = routes.length - 1; i >= 0; i--) {
            var r = routes[i];
            if (r.method !== "*" && r.method !== method) { continue; }
            if (r.url !== "*" && r.url !== url) { continue; }
            return r;
        }
        return null;
    }

    global.$xhr = {
        reset: function () { routes = []; requests = []; },

        route: function (method, url, status, body, headers) {
            routes.push({
                method: method,
                url: url,
                status: status,
                body: body === null || body === undefined ? "" : String(body),
                headers: parseHeaders(headers),
                json: false,
                fail: false
            });
        },

        // A route whose `response` is the parsed body rather than the text, i.e. what a browser hands
        // back for responseType "json". The package's GetObjectLiteralAsync reads exactly that.
        routeJson: function (method, url, status, body) {
            routes.push({
                method: method,
                url: url,
                status: status,
                body: body === null || body === undefined ? "" : String(body),
                headers: {},
                json: true,
                fail: false
            });
        },

        // A route that fails at the transport level — the browser's "network error", which surfaces as
        // readyState 4 with status 0 and no body (a CORS rejection, DNS failure, offline).
        routeNetworkError: function (method, url) {
            routes.push({ method: method, url: url, status: 0, body: "", headers: {}, json: false, fail: true });
        },

        requestCount: function () { return requests.length; },
        requestMethod: function (i) { return requests[i].method; },
        requestUrl: function (i) { return requests[i].url; },
        requestBody: function (i) { return requests[i].body === null ? "(none)" : String(requests[i].body); },
        requestHeaders: function (i) { return formatHeaders(requests[i].headers); },
        // What the caller asked the transport to decode the body as. "" is the XHR default, i.e.
        // "nobody set responseType" — which is how a test sees that a typed read was never wired up.
        requestResponseType: function (i) { return requests[i].responseType === "" ? "(default)" : requests[i].responseType; },
        requestHeader: function (i, name) {
            var h = requests[i].headers;
            return Object.prototype.hasOwnProperty.call(h, name) ? h[name] : "(absent)";
        },
        aborted: function (i) { return requests[i].aborted === true; }
    };

    function XMLHttpRequest() {
        this.readyState = 0;
        this.status = 0;
        this.statusText = "";
        this.responseText = "";
        this.response = null;
        this.responseType = "";
        this.responseURL = "";
        this.timeout = 0;
        this.withCredentials = false;
        this.onreadystatechange = null;
        this._requestHeaders = {};
        this._responseHeaders = {};
        this._record = null;
    }

    XMLHttpRequest.prototype.open = function (method, url) {
        this._method = method;
        this._url = url;
        // A real XHR resets its request headers on open(), which is what makes the package's
        // "apply the headers only after open()" ordering load-bearing.
        this._requestHeaders = {};
        this.readyState = 1;
    };

    XMLHttpRequest.prototype.setRequestHeader = function (name, value) {
        // Per the XHR spec a repeated header is appended to, not replaced — the same combining rule
        // the .NET header collections use.
        this._requestHeaders[name] = Object.prototype.hasOwnProperty.call(this._requestHeaders, name)
            ? this._requestHeaders[name] + ", " + value
            : String(value);
    };

    XMLHttpRequest.prototype.getResponseHeader = function (name) {
        var wanted = String(name).toLowerCase();
        var names = Object.keys(this._responseHeaders);
        for (var i = 0; i < names.length; i++) {
            if (names[i].toLowerCase() === wanted) { return this._responseHeaders[names[i]]; }
        }
        return null; // A browser answers null for a header the response does not carry.
    };

    XMLHttpRequest.prototype.getAllResponseHeaders = function () {
        var names = Object.keys(this._responseHeaders).sort();
        var out = "";
        for (var i = 0; i < names.length; i++) {
            out += names[i].toLowerCase() + ": " + this._responseHeaders[names[i]] + "\r\n";
        }
        return out;
    };

    XMLHttpRequest.prototype.abort = function () {
        if (this._record) { this._record.aborted = true; }
        this.readyState = 0;
        this.status = 0;
        if (this.onreadystatechange) { this.onreadystatechange(null); }
    };

    XMLHttpRequest.prototype.send = function (body) {
        var self = this;
        var route = match(this._method, this._url);

        this._record = {
            method: this._method,
            url: this._url,
            headers: JSON.parse(JSON.stringify(this._requestHeaders)),
            body: body === undefined ? null : body,
            responseType: String(this.responseType),
            aborted: false
        };
        requests.push(this._record);

        // Asynchronous, like the real thing: a test that awaits the task exercises the readystate
        // callback rather than a value that was already there.
        setTimeout(function () {
            if (self.readyState === 0) { return; } // aborted before the response arrived
            if (route === null) {
                self.status = 404;
                self.responseText = "";
                self.response = "";
                self._responseHeaders = {};
            } else if (route.fail) {
                self.status = 0;
                self.responseText = "";
                self.response = null;
                self._responseHeaders = {};
            } else {
                self.status = route.status === null || route.status === undefined ? 200 : route.status;
                self.responseText = route.body;
                self.response = route.json ? JSON.parse(route.body) : route.body;
                self._responseHeaders = route.headers;
            }
            self.responseURL = self._url;
            self.readyState = 4;
            if (self.onreadystatechange) { self.onreadystatechange(null); }
        }, 0);
    };

    global.XMLHttpRequest = XMLHttpRequest;

    // FormData, for the package's FormContent. Only what a test needs to see the body arrive.
    function FormData() { this._entries = []; }
    FormData.prototype.append = function (name, value) { this._entries.push(name + "=" + value); };
    FormData.prototype.toString = function () { return "FormData(" + this._entries.join("&") + ")"; };
    global.FormData = FormData;
})(typeof globalThis !== "undefined" ? globalThis : this);
