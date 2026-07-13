// H5.Translator.Roslyn — thin adapter that exposes the small set of language-level
// helpers the emitter relies on, implemented over the real h5.js runtime primitives.
// This lets emitted code interoperate with h5.js / h5.core.
(function (global) {
    var H5 = global.H5;
    var H5R = global.H5R = global.H5R || {};

    H5R.toStr = function (v) {
        if (v === null || v === undefined) { return ""; }
        if (typeof v === "boolean") { return v ? "True" : "False"; }
        try { return H5.toString(v); } catch (e) { return v.toString ? v.toString() : String(v); }
    };
    H5R.chr = function (code) { return String.fromCharCode(code); };
    H5R.is = function (v, t) { return H5.is(v, t); };
    H5R.as = function (v, t) { return H5.as ? H5.as(v, t) : (H5.is(v, t) ? v : null); };
    H5R.equals = function (a, b) { return H5.equals ? H5.equals(a, b) : a === b; };
    H5R.idiv = (H5.Int && H5.Int.div) ? function (a, b) { return H5.Int.div(a, b); } : function (a, b) { var r = a / b; return r < 0 ? Math.ceil(r) : Math.floor(r); };
    H5R.trunc = function (x) { return x < 0 ? Math.ceil(x) : Math.floor(x); };
    H5R.clone = function (o) { return (o && o.$clone) ? o.$clone() : o; };
    H5R.hash = function (v) { return H5.getHashCode ? H5.getHashCode(v) : 0; };
    H5R.getEnumerator = function (src) {
        if (H5.getEnumerator) {
            var e = H5.getEnumerator(src);
            return { moveNext: function () { return e.moveNext ? e.moveNext() : e.MoveNext(); }, get current() { return e.Current !== undefined ? e.Current : e.current; } };
        }
        var i = -1; return { moveNext: function () { i++; return i < src.length; }, get current() { return src[i]; } };
    };
    H5R.dispose = function (x) { if (x) { if (x.dispose) { x.dispose(); } else if (x.Dispose) { x.Dispose(); } } };
    H5R.array = function (n, d) { var a = new Array(n); for (var i = 0; i < n; i++) { a[i] = d; } return a; };
    H5R.formatValue = function (v, fmt) { try { return System.String.format("{0:" + fmt + "}", v); } catch (e) { return H5R.toStr(v); } };

    // Date/TimeSpan arithmetic helpers (best-effort; System types come from h5.js).
    H5R.dtSub = function (a, b) { return System.DateTime.subdd(a, b); };
    H5R.dtSubTs = function (a, b) { return System.DateTime.subdt(a, b); };
    H5R.dtAddTs = function (a, b) { return System.DateTime.adddt(a, b); };
    H5R.tsAdd = function (a, b) { return a.add(b); };
    H5R.tsSub = function (a, b) { return a.sub(b); };

    // Iterator (yield) support: a re-enumerable wrapper around a generator function.
    H5R.iter = function (genFn) {
        var iterable = {};
        iterable[Symbol.iterator] = genFn;
        iterable.GetEnumerator = function () {
            var it = genFn(), cur;
            return { moveNext: function () { var r = it.next(); cur = r.value; return !r.done; }, get current() { return cur; } };
        };
        return iterable;
    };
})(typeof globalThis !== "undefined" ? globalThis : this);
