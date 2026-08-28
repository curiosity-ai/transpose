    // ---- require: fetching a script, a module or a stylesheet at run time --------------------
    //
    // One loader for every "the page needs this file now" case: a vendored bundle only the screen
    // that shows a chart wants, a stylesheet, an ES module. Every application built on Transpose
    // grew its own version of this — each one a <script> element, an onload handler and a slightly
    // different set of things it got wrong — so it lives here instead, once.
    //
    // What it does that hand-rolled injection does not:
    //
    //   * picks the element from the URL: `.css` -> <link rel=stylesheet>, `.mjs` -> a module
    //     <script>, anything else -> a classic <script>. A `.js` file that is really a module (a
    //     Transpose module entry, which keeps the bundle's name) is asked for explicitly, since
    //     nothing about the URL says so;
    //   * resolves the URL against document.baseURI first, so "assets/x.js", "./assets/x.js" and the
    //     absolute form are one entry — and a file index.html already carries is recognised rather
    //     than fetched a second time;
    //   * falls back between the `.js` / `.min.js` (and `.css` / `.min.css`) spellings of the same
    //     file. A site keeps whichever variant its own build called for, and a library published
    //     once cannot know which build is consuming it, so asking for either name has to work;
    //   * remembers what it loaded, so N callers share one fetch — and forgets what failed, so a
    //     later caller can retry rather than inheriting a poisoned result forever.
    //   * busts the browser/CDN cache: every URL it injects carries this build's token as a query
    //     ("assets/js/graph-kit.min.js?1q9v3k2abc"), so a redeployed page never serves the copy the
    //     browser kept of a file whose NAME did not change. The token is stamped into each compiled
    //     assembly's prelude (Transpose.Require.cacheBust), so it moves when a build moves and stands
    //     still when nothing was rebuilt. It is applied at injection only — everything that identifies
    //     a URL (the shared-fetch table, isLoaded, "index.html already carries this file") still works
    //     on the URL as asked for.
    //
    // Loading is sequential when several URLs are given: a plugin that extends a library has to
    // arrive after it, and that is the common case (d3 then d3.lasso). A caller that genuinely wants
    // them in parallel asks for them in separate calls.

    var require = {
        // resolved url -> the in-flight (or settled) load, so concurrent callers share one fetch
        $pending: {},
        // resolved url -> true once it is on the page
        $loaded: {},
        // this page's cache-busting token, appended to every URL that is actually fetched. Set by the
        // prelude of each compiled assembly; "" (nothing stamped it) means no busting at all.
        $bust: "",

        /// Loads one or more URLs, in order, and completes when the last one has run. `kind` is one
        /// of Transpose.Require.kinds; 0 (auto) picks the element from the URL.
        loadAsync: function (urls, kind) {
            if (urls == null) return Promise.resolve(true);
            if (typeof urls === "string") urls = [urls];

            var i = -1;
            function next() {
                i++;
                if (i >= urls.length) return Promise.resolve(true);
                return require.$one(urls[i], kind || 0).then(next);
            }
            return next();
        },

        /// True once <paramref name="url"/> is on the page — loaded through here, or scripted by
        /// index.html before anything asked for it.
        isLoaded: function (url) {
            var resolved = require.$resolve(url);
            return !!require.$loaded[resolved] || require.$find(resolved) != null;
        },

        kinds: { auto: 0, script: 1, module: 2, style: 3 },

        /// Registers a build's cache-busting token. A page can carry several compiled assemblies, each
        /// stamped when *it* was built and loading in whatever order index.html (or an import graph)
        /// puts them in, so the greatest token wins rather than the last one: the compiler mints them
        /// as a fixed-width base-36 time stamp plus a random tail, which makes "greater" mean "newer
        /// build". Assigning $bust directly overrides that, which is what a host or a test does.
        cacheBust: function (token) {
            if (typeof token === "string" && token > require.$bust) require.$bust = token;
            return require.$bust;
        },

        $one: function (url, kind) {
            if (typeof document === "undefined") {
                return Promise.reject(new System.NotSupportedException.$ctor1(
                    "Transpose.Require needs a document; there is none in this JavaScript engine."));
            }

            var resolved = require.$resolve(url);
            if (require.$pending[resolved]) return require.$pending[resolved];

            var effective = require.$kindOf(resolved, kind);

            // Already on the page: index.html scripted it, or a host loaded it its own way. Wait for
            // it rather than adding a second element that fetches the same file.
            var existing = require.$find(resolved);
            var p = existing != null
                ? require.$waitFor(existing)
                : require.$inject(resolved, effective).then(null, function (error) {
                    // The other spelling of the same file. A build keeps one of the pair, so this is
                    // the answer whenever a library asks for the variant this site did not keep — and
                    // it is a specific fallback, not a swallowed error: if there is no counterpart, or
                    // it fails too, the original failure is what the caller sees.
                    var counterpart = require.$counterpart(resolved);
                    if (counterpart == null) throw error;
                    return require.$inject(counterpart, require.$kindOf(counterpart, kind)).then(function () {
                        require.$loaded[counterpart] = true;
                        return true;
                    }, function () {
                        throw error;
                    });
                });

            p = p.then(function () {
                require.$loaded[resolved] = true;
                return true;
            }, function (error) {
                // Nothing was loaded, so leave no trace of the attempt: a fetch that failed once
                // (offline, a half-deployed site) must not make every later caller fail too.
                delete require.$pending[resolved];
                throw System.Exception.create(error);
            });

            require.$pending[resolved] = p;
            return p;
        },

        /// The URL as the browser sees it. Hand-assembling one from window.location goes wrong the
        /// moment the app is served as a file rather than a directory, or under a <base href>.
        $resolve: function (url) {
            if (typeof document === "undefined" || typeof URL === "undefined") return url;
            try {
                return new URL(url, document.baseURI).href;
            } catch (e) {
                return url;
            }
        },

        /// script / module / style, from the URL when the caller did not say.
        $kindOf: function (url, kind) {
            if (kind) return kind;
            var path = require.$path(url).toLowerCase();
            if (require.$endsWith(path, ".css")) return require.kinds.style;
            if (require.$endsWith(path, ".mjs")) return require.kinds.module;
            return require.kinds.script;
        },

        /// The path part of a URL — a cache-busting "?v=…" is part of what is fetched but says
        /// nothing about what kind of file it is.
        $path: function (url) {
            var cut = url.length;
            var q = url.indexOf("?"), h = url.indexOf("#");
            if (q >= 0 && q < cut) cut = q;
            if (h >= 0 && h < cut) cut = h;
            return url.substring(0, cut);
        },

        $endsWith: function (s, suffix) {
            return s.length >= suffix.length && s.substring(s.length - suffix.length) === suffix;
        },

        /// The other spelling of the same file: x.js <-> x.min.js, x.css <-> x.min.css. null when the
        /// URL is neither (a .mjs chunk, an extensionless endpoint), so there is nothing to retry.
        $counterpart: function (url) {
            var path = require.$path(url), tail = url.substring(path.length);
            var lower = path.toLowerCase();
            var exts = [".js", ".css"];
            for (var i = 0; i < exts.length; i++) {
                var ext = exts[i], min = ".min" + ext;
                if (require.$endsWith(lower, min)) return path.substring(0, path.length - min.length) + ext + tail;
                if (require.$endsWith(lower, ext)) return path.substring(0, path.length - ext.length) + min + tail;
            }
            return null;
        },

        /// The URL as it is fetched: the one that was asked for plus this build's token, as a query
        /// with no value ("x.js?1q9v3k2abc", "x.js?v=7&1q9v3k2abc"). Only http(s) — and a URL that was
        /// never resolved to a scheme at all — is served by something that caches; a data:/blob:
        /// payload carries its own bytes and a query would corrupt it.
        $bustUrl: function (url) {
            if (!require.$bust) return url;
            var colon = url.indexOf(":");
            if (colon > 0) {
                var scheme = url.substring(0, colon).toLowerCase();
                if (scheme !== "http" && scheme !== "https") return url;
            }
            // A fragment stays at the end, where the browser expects it.
            var hash = url.indexOf("#");
            var head = hash >= 0 ? url.substring(0, hash) : url;
            var tail = hash >= 0 ? url.substring(hash) : "";
            return head + (head.indexOf("?") >= 0 ? "&" : "?") + require.$bust + tail;
        },

        /// The element already serving this URL, if any. Compared on the resolved src/href — the
        /// attribute is whatever spelling the page was written with, the property is absolute.
        $find: function (resolved) {
            if (typeof document === "undefined") return null;
            var i, nodes = document.getElementsByTagName("script");
            for (i = 0; i < nodes.length; i++) {
                if (nodes[i].src && nodes[i].src === resolved) return nodes[i];
            }
            nodes = document.getElementsByTagName("link");
            for (i = 0; i < nodes.length; i++) {
                if (nodes[i].rel === "stylesheet" && nodes[i].href === resolved) return nodes[i];
            }
            return null;
        },

        /// Waits on an element this loader did not create. Once the document is complete every
        /// element in it has settled, so there is nothing left to wait for; before that, its own
        /// load/error is the signal. A foreign element that errors is not this caller's failure —
        /// whoever put it there owns it — so the wait ends either way.
        $waitFor: function (element) {
            if (element.$tpsLoad) return element.$tpsLoad;
            var p = new Promise(function (resolve) {
                if (document.readyState === "complete") return resolve(true);
                var done = function () { resolve(true); };
                element.addEventListener("load", done);
                element.addEventListener("error", done);
                window.addEventListener("load", done);
            });
            element.$tpsLoad = p;
            return p;
        },

        $inject: function (url, kind) {
            return new Promise(function (resolve, reject) {
                // The cache-busting token goes on here and nowhere else: $pending, $loaded, isLoaded and
                // $find all key off the URL as the caller asked for it, so a token changing between
                // builds never turns one file into two entries, and a file index.html already carries
                // (without a token) is still recognised rather than fetched a second time.
                var fetched = require.$bustUrl(url);
                var element;
                if (kind === require.kinds.style) {
                    element = document.createElement("link");
                    element.rel = "stylesheet";
                    element.href = fetched;
                } else {
                    element = document.createElement("script");
                    if (kind === require.kinds.module) element.type = "module";
                    else element.type = "text/javascript";
                    element.src = fetched;
                }

                element.addEventListener("load", function () { resolve(true); });
                element.addEventListener("error", function () {
                    // Leave nothing behind: a failed element still matches a src/href lookup, and the
                    // next attempt would then wait on an element that is never going to load.
                    if (element.parentNode) element.parentNode.removeChild(element);
                    reject(new System.Exception("Transpose.Require: failed to load '" + url + "'."));
                });

                (document.head || document.getElementsByTagName("head")[0] || document.body).appendChild(element);
            });
        }
    };

    Transpose.Require = require;
