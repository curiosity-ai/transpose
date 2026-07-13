// H5.Translator.Roslyn — minimal self-contained runtime.
// Provides the small BCL surface needed by emitted code. Grows feature-by-feature.
(function (global) {
    var H5R = global.H5R = global.H5R || {};
    H5R.global = global;

    // ---- Type / namespace registration ------------------------------------

    H5R.ns = function (name) {
        var parts = name.split(".");
        var scope = global;
        for (var i = 0; i < parts.length; i++) {
            scope = scope[parts[i]] || (scope[parts[i]] = {});
        }
        return scope;
    };

    // Registers a constructor function (produced by factory) under a dotted name.
    H5R.define = function (name, factory) {
        var ctor = factory();
        var parts = name.split(".");
        var scope = global;
        for (var i = 0; i < parts.length - 1; i++) {
            scope = scope[parts[i]] || (scope[parts[i]] = {});
        }
        scope[parts[parts.length - 1]] = ctor;
        if (typeof ctor === "function") { ctor.$typeName = name; }
        return ctor;
    };

    H5R.inherit = function (derived, base) {
        derived.prototype = Object.create(base.prototype);
        derived.prototype.constructor = derived;
        derived.$base = base;
    };

    // Instantiates a type and runs the named constructor method.
    H5R.create = function (type, ctorName, args) {
        var o = new type();
        if (ctorName && o[ctorName]) {
            o[ctorName].apply(o, args || []);
        }
        return o;
    };

    // ---- Value formatting (.NET semantics) --------------------------------

    H5R.toStr = function (v) {
        if (v === null || v === undefined) { return ""; }
        var t = typeof v;
        if (t === "boolean") { return v ? "True" : "False"; }
        if (t === "number") { return H5R.numToStr(v); }
        if (t === "string") { return v; }
        // User-defined ToString override (emitted with its C# name).
        if (typeof v.ToString === "function") { return v.ToString(); }
        if (typeof v.toString === "function" && v.toString !== Object.prototype.toString) { return v.toString(); }
        return String(v);
    };

    // Char (represented as a code point) displayed as its character.
    H5R.chr = function (code) { return String.fromCharCode(code); };

    H5R.numToStr = function (n) {
        if (n === Infinity) { return "∞"; }         // ∞
        if (n === -Infinity) { return "-∞"; }
        if (n !== n) { return "NaN"; }
        return String(n);
    };

    // Composite formatting: "{0} {1:D2}" style.
    H5R.format = function (fmt, args) {
        return fmt.replace(/\{\{|\}\}|\{(\d+)(?::([^}]*))?\}/g, function (m, index, spec) {
            if (m === "{{") { return "{"; }
            if (m === "}}") { return "}"; }
            var v = args[parseInt(index, 10)];
            return H5R.formatValue(v, spec);
        });
    };

    H5R.formatValue = function (v, spec) {
        if (!spec) { return H5R.toStr(v); }
        var m = spec.match(/^([A-Za-z])(\d*)$/);
        if (m && typeof v === "number") {
            var kind = m[1].toUpperCase();
            var prec = m[2] === "" ? -1 : parseInt(m[2], 10);
            switch (kind) {
                case "D": { var s = Math.trunc(Math.abs(v)).toString(); while (s.length < prec) { s = "0" + s; } return (v < 0 ? "-" : "") + s; }
                case "X": { var h = (v >>> 0).toString(16).toUpperCase(); while (h.length < prec) { h = "0" + h; } return h; }
                case "F": return v.toFixed(prec < 0 ? 2 : prec);
                case "N": return v.toFixed(prec < 0 ? 2 : prec).replace(/\B(?=(\d{3})+(?!\d))/g, ",");
                case "P": return (v * 100).toFixed(prec < 0 ? 2 : prec) + " %";
            }
        }
        return H5R.toStr(v);
    };

    // ---- Numeric helpers ---------------------------------------------------

    H5R.idiv = function (a, b) { var r = a / b; return r < 0 ? Math.ceil(r) : Math.floor(r); };
    H5R.imod = function (a, b) { return a % b; };
    H5R.trunc = function (a) { return Math.trunc(a); };

    // ---- Arrays ------------------------------------------------------------

    H5R.array = function (length, defaultValue) {
        var a = new Array(length);
        for (var i = 0; i < length; i++) { a[i] = defaultValue; }
        return a;
    };

    // ---- Type tests --------------------------------------------------------

    H5R.is = function (obj, type) {
        if (obj === null || obj === undefined) { return false; }
        if (typeof type === "function") { return obj instanceof type || (obj.constructor === type); }
        return false;
    };

    H5R.as = function (obj, type) { return H5R.is(obj, type) ? obj : null; };

    // ---- IDisposable / IEnumerable -----------------------------------------

    H5R.dispose = function (o) {
        if (o && typeof o.Dispose === "function") { o.Dispose(); }
    };

    H5R.getEnumerator = function (source) {
        if (source === null || source === undefined) {
            throw new System.NullReferenceException("Collection is null");
        }
        if (typeof source === "string") {
            var si = -1;
            return {
                moveNext: function () { si++; return si < source.length; },
                get current() { return source.charCodeAt(si); } // chars are code points
            };
        }
        if (Array.isArray(source)) {
            var i = -1;
            return {
                moveNext: function () { i++; return i < source.length; },
                get current() { return source[i]; }
            };
        }
        if (typeof source.GetEnumerator === "function") {
            var e = source.GetEnumerator();
            return {
                moveNext: function () { return e.MoveNext(); },
                get current() { return e.Current; }
            };
        }
        if (typeof source[Symbol.iterator] === "function") {
            var it = source[Symbol.iterator]();
            var cur;
            return {
                moveNext: function () { var r = it.next(); cur = r.value; return !r.done; },
                get current() { return cur; }
            };
        }
        throw new System.NotSupportedException("Object is not enumerable");
    };

    // ---- Equality ----------------------------------------------------------

    H5R.equals = function (a, b) {
        if (a === b) { return true; }
        if (a === null || a === undefined || b === null || b === undefined) { return false; }
        if (typeof a.equals === "function") { return a.equals(b); }
        return false;
    };

    // ---- System.Console ----------------------------------------------------

    H5R._buf = "";

    var System = H5R.ns("System");

    System.Console = {
        Write: function () {
            var args = arguments;
            if (args.length > 1 && typeof args[0] === "string") {
                H5R._buf += H5R.format(args[0], Array.prototype.slice.call(args, 1));
            } else {
                H5R._buf += H5R.toStr(args[0]);
            }
        },
        WriteLine: function () {
            var args = arguments;
            var line;
            if (args.length === 0) {
                line = "";
            } else if (args.length > 1 && typeof args[0] === "string") {
                line = H5R.format(args[0], Array.prototype.slice.call(args, 1));
            } else {
                line = H5R.toStr(args[0]);
            }
            console.log(H5R._buf + line);
            H5R._buf = "";
        }
    };

    H5R.flush = function () {
        if (H5R._buf.length > 0) { console.log(H5R._buf); H5R._buf = ""; }
    };

    // ---- Exceptions --------------------------------------------------------

    var makeException = function (name) {
        var fn = function (message) { this.message = message || name; this.$typeName = "System." + name; };
        fn.prototype = Object.create(Error.prototype);
        fn.prototype.constructor = fn;
        fn.prototype.get_Message = function () { return this.message; };
        fn.prototype.toString = function () { return this.$typeName + ": " + this.message; };
        System[name] = fn;
        return fn;
    };

    makeException("Exception");
    makeException("InvalidOperationException");
    makeException("ArgumentException");
    makeException("ArgumentNullException");
    makeException("NotSupportedException");
    makeException("NotImplementedException");
    makeException("IndexOutOfRangeException");
    makeException("DivideByZeroException");
    makeException("NullReferenceException");
    makeException("FormatException");
    makeException("OverflowException");

})(typeof globalThis !== "undefined" ? globalThis : this);
