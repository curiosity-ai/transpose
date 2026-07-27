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
    // Box a char (a bare code-point number at runtime) so it stringifies / compares as its
    // character once widened to object — `object o = 'A'; o.ToString()` must be "A", not "65".
    TransposeR.boxChar = function (c) { return Transpose.box(c, System.Char, TransposeR.chr); };
    // The value→string converter for a runtime type, or null when values of that type already
    // stringify the way .NET's ToString() does. This resolves the {T:ToString} template slot when T
    // is only bound at runtime (a generic method threading its type argument), so a char/bool/enum
    // sequence renders the same inside `static string J<T>(IEnumerable<T> x) => string.Join("|", x)`
    // as it does at a concrete call site. Callers treat null as "use your own fallback".
    TransposeR.toStrFn = function (t) {
        if (t === System.Char) { return TransposeR.chr; }
        if (t === System.Boolean) { return function ($v) { return System.Boolean.toString($v); }; }
        if (t && t.$kind === "enum") { return System.Enum.toStringFn(t); }
        return null;
    };
    // Render a value whose static type was a type parameter — `T.ToString()`, `"" + t`, `$"{t}"`
    // inside a generic method — using the converter for the type argument threaded at runtime.
    TransposeR.toStrT = function (v, t) {
        if (v === null || v === undefined) { return ""; }
        var fn = TransposeR.toStrFn(t);
        return fn ? fn(v) : TransposeR.toStr(v);
    };
    // Exception.StackTrace. A value caught by `catch (Exception)` is either a real System.Exception,
    // which captured an Error into `errorStack` when it was constructed, or a raw JS error thrown by
    // interop / a rejected promise, which has a native `stack` and no `errorStack`. C# matches both,
    // so read whichever shape arrived instead of assuming errorStack (a raw error gave undefined).
    TransposeR.stackTrace = function (e) {
        if (e === null || e === undefined) { return null; }
        if (e.errorStack && e.errorStack.stack !== null && e.errorStack.stack !== undefined) { return e.errorStack.stack; }
        return (e.stack !== null && e.stack !== undefined) ? e.stack : null;
    };
    TransposeR.is = function (v, t) { return Transpose.is(v, t); };
    TransposeR.as = function (v, t) { return Transpose.as ? Transpose.as(v, t) : (Transpose.is(v, t) ? v : null); };
    TransposeR.equals = function (a, b) { return Transpose.equals ? Transpose.equals(a, b) : a === b; };
    TransposeR.idiv = (Transpose.Int && Transpose.Int.div) ? function (a, b) { return Transpose.Int.div(a, b); } : function (a, b) { var r = a / b; return r < 0 ? Math.ceil(r) : Math.floor(r); };
    TransposeR.trunc = function (x) { return x < 0 ? Math.ceil(x) : Math.floor(x); };

    // Saturating float → integer conversions (C#'s `(int)`/`(uint)`/`(long)`/`(ulong)` of a
    // float/double). The CLR saturates out-of-range values to the target's Min/Max and maps NaN
    // to 0 (unlike an integer→integer cast, which wraps). A narrower target (byte/short/…) first
    // saturates to int32 here, then the emitter masks the result to width with Transpose.Int.clip*.
    TransposeR.fclip32 = function (x) {
        if (isNaN(x)) { return 0; }
        if (x <= -2147483648) { return -2147483648; }
        if (x >= 2147483647) { return 2147483647; }
        return x < 0 ? Math.ceil(x) : Math.floor(x);
    };
    TransposeR.fclipu32 = function (x) {
        if (isNaN(x) || x <= 0) { return 0; }
        if (x >= 4294967295) { return 4294967295; }
        return Math.floor(x);
    };
    TransposeR.fclip64 = function (x) {
        if (isNaN(x)) { return System.Int64.Zero; }
        if (x >= 9223372036854775807) { return System.Int64.MaxValue; }
        if (x <= -9223372036854775808) { return System.Int64.MinValue; }
        return System.Int64(TransposeR.trunc(x));
    };
    TransposeR.fclipu64 = function (x) {
        if (isNaN(x) || x <= 0) { return System.UInt64.Zero; }
        if (x >= 18446744073709551615) { return System.UInt64.MaxValue; }
        return System.UInt64(TransposeR.trunc(x));
    };
    TransposeR.clone = function (o) {
        if (!o) { return o; }
        if (o.$clone) { return o.$clone(); }
        // A DateTime is backed by a native Date; Object.assign onto Object.create(Date.prototype)
        // would drop the internal [[DateValue]] (d.getTime() then throws). Copy it as a real Date,
        // preserving the Transpose-attached kind/ticks.
        if (o instanceof Date) {
            var d = new Date(o.getTime());
            if (o.kind !== undefined) { d.kind = o.kind; }
            if (o.ticks !== undefined) { d.ticks = o.ticks; }
            return d;
        }
        return Object.assign(Object.create(Object.getPrototypeOf(o)), o);
    };
    // Hash one member of a synthesized value-wise GetHashCode. `safe` is required: a null member
    // contributes 0 in .NET, whereas the bare runtime helper throws "HashCode cannot be calculated
    // for empty value" — which took out any struct/record with an unset reference field.
    TransposeR.hash = function (v) { return Transpose.getHashCode ? Transpose.getHashCode(v, true) : 0; };
    TransposeR.getEnumerator = function (src) {
        // foreach over a null sequence throws NullReferenceException in .NET (the implicit
        // GetEnumerator call dereferences null), not a raw JS TypeError from a later .moveNext().
        if (src == null) { throw new System.NullReferenceException(); }
        var wrap = function (e) {
            return {
                moveNext: function () { return e.moveNext ? e.moveNext() : e.MoveNext(); },
                get current() { return e.Current !== undefined ? e.Current : e.current; },
                // Forward disposal to the underlying enumerator so a foreach ending early still runs
                // an iterator's finally / IDisposable cleanup (no-op when the source isn't disposable).
                dispose: function () { if (e.dispose) { e.dispose(); } else if (e.Dispose) { e.Dispose(); } }
            };
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
            }, function () {
                // Dispose (called when a foreach ends early via break/return/throw) must run the
                // iterator's pending `finally` blocks — return() resumes the JS generator through them.
                if (it.return) { it.return(); }
            });
            return en;
        });
    };
})(typeof globalThis !== "undefined" ? globalThis : this);
