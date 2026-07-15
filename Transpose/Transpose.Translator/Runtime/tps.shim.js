// Transpose.Translator — thin adapter that exposes the small set of language-level
// helpers the emitter relies on, implemented over the real tps.js runtime primitives.
// This lets emitted code interoperate with tps.js / tps.core.
(function (global) {
    var Transpose = global.Transpose;
    var TransposeR = global.TransposeR = global.TransposeR || {};

    TransposeR.toStr = function (v) {
        if (v === null || v === undefined) { return ""; }
        if (typeof v === "boolean") { return v ? "True" : "False"; }
        try { return Transpose.toString(v); } catch (e) { return v.toString ? v.toString() : String(v); }
    };
    TransposeR.chr = function (code) { return String.fromCharCode(code); };
    TransposeR.is = function (v, t) { return Transpose.is(v, t); };
    TransposeR.as = function (v, t) { return Transpose.as ? Transpose.as(v, t) : (Transpose.is(v, t) ? v : null); };
    TransposeR.equals = function (a, b) { return Transpose.equals ? Transpose.equals(a, b) : a === b; };
    TransposeR.idiv = (Transpose.Int && Transpose.Int.div) ? function (a, b) { return Transpose.Int.div(a, b); } : function (a, b) { var r = a / b; return r < 0 ? Math.ceil(r) : Math.floor(r); };
    TransposeR.trunc = function (x) { return x < 0 ? Math.ceil(x) : Math.floor(x); };
    TransposeR.clone = function (o) {
        if (!o) { return o; }
        if (o.$clone) { return o.$clone(); }
        return Object.assign(Object.create(Object.getPrototypeOf(o)), o);
    };
    TransposeR.hash = function (v) { return Transpose.getHashCode ? Transpose.getHashCode(v) : 0; };
    TransposeR.getEnumerator = function (src) {
        var wrap = function (e) {
            return { moveNext: function () { return e.moveNext ? e.moveNext() : e.MoveNext(); }, get current() { return e.Current !== undefined ? e.Current : e.current; } };
        };
        if (src != null) {
            // Already an enumerator (pattern-based / extension GetEnumerator result).
            if (typeof src.moveNext === "function" || typeof src.MoveNext === "function") { return wrap(src); }
            // An enumerable with its own GetEnumerator (e.g. TransposeR.iter iterables).
            if (typeof src.GetEnumerator === "function") { return wrap(src.GetEnumerator()); }
        }
        if (Transpose.getEnumerator) { return wrap(Transpose.getEnumerator(src)); }
        var i = -1; return { moveNext: function () { i++; return i < src.length; }, get current() { return src[i]; } };
    };
    TransposeR.dispose = function (x) { if (x) { if (x.dispose) { x.dispose(); } else if (x.Dispose) { x.Dispose(); } } };
    TransposeR.array = function (n, d) {
        var a = new Array(n);
        // A struct default (an object) must yield an INDEPENDENT value per slot — sharing one
        // reference would alias mutations across elements (e.g. Dictionary Entry.next forming a
        // cycle → infinite probe loop). Primitive/null fills are copied as-is. A function fill is
        // a per-element factory (matching System.Array.init(n, factory) for value types).
        if (typeof d === 'function') { for (var i = 0; i < n; i++) { a[i] = d(); } }
        else if (d && typeof d === 'object') { for (var i = 0; i < n; i++) { a[i] = TransposeR.clone(d); } }
        else { for (var i = 0; i < n; i++) { a[i] = d; } }
        return a;
    };

    // Delegate / event helpers (multicast combine + remove) over tps.js's Transpose.fn.
    TransposeR.combine = function (a, b) { return Transpose.fn.combine(a, b); };
    TransposeR.remove = function (a, b) { return Transpose.fn.remove(a, b); };

    // Async interop: adapt a native Promise (produced by an emitted `async` body) into an
    // tps.js Task, so async methods return real Tasks that compose with Task.Run/WhenAll/
    // ContinueWith and route exceptions through the Task (faulted state), matching tps.js.
    TransposeR.fromPromise = function (p) {
        var tcs = new System.Threading.Tasks.TaskCompletionSource();
        Promise.resolve(p).then(
            function (v) { tcs.setResult(v); },
            function (e) { tcs.setException(System.Exception.create(e)); }
        );
        return tcs.task;
    };

    // Spread source → JS array (arrays pass through; other enumerables are drained).
    TransposeR.spread = function (x) {
        if (x == null) { return []; }
        if (Array.isArray(x)) { return x; }
        var out = [], e = TransposeR.getEnumerator(x);
        while (e.moveNext()) { out.push(e.current); }
        return out;
    };
    TransposeR.formatValue = function (v, fmt) { try { return System.String.format("{0:" + fmt + "}", v); } catch (e) { return TransposeR.toStr(v); } };

    // Date/TimeSpan arithmetic helpers (best-effort; System types come from tps.js).
    TransposeR.dtSub = function (a, b) { return System.DateTime.subdd(a, b); };
    TransposeR.dtSubTs = function (a, b) { return System.DateTime.subdt(a, b); };
    TransposeR.dtAddTs = function (a, b) { return System.DateTime.adddt(a, b); };
    TransposeR.tsAdd = function (a, b) { return System.TimeSpan.add(a, b); };
    TransposeR.tsSub = function (a, b) { return System.TimeSpan.sub(a, b); };

    // Iterator (yield) support: a re-enumerable wrapper around a generator function.
    // Built on tps.js's own GeneratorEnumerable so the result is a real
    // System.Collections.Generic.IEnumerable<object> — recognised by Transpose.as/Transpose.getEnumerator
    // AND by System.Linq.Enumerable.from (which checks Transpose.as(_, IEnumerable) and otherwise
    // treats the source as empty). A plain {GetEnumerator} object satisfies the former but not
    // the latter, so LINQ over an iterator method would silently yield nothing.
    TransposeR.iter = function (genFn) {
        var T = System.Object;
        return new (Transpose.GeneratorEnumerable$1(T))(function () {
            var it = genFn();
            var en = new (Transpose.GeneratorEnumerator$1(T))(function () {
                var r = it.next();
                if (r.done) { return false; }
                en.current = r.value;
                return true;
            });
            return en;
        });
    };
})(typeof globalThis !== "undefined" ? globalThis : this);
