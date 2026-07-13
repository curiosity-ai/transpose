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
            if (typeof e.moveNext === "function") { return e; } // internal-style enumerator
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
        if (typeof a.Equals === "function") { return a.Equals(b); }
        if (typeof a.equals === "function") { return a.equals(b); }
        return false;
    };

    // Shallow clone preserving the prototype (for record `with` expressions).
    H5R.clone = function (o) { return Object.assign(Object.create(Object.getPrototypeOf(o)), o); };

    H5R.hash = function (v) {
        if (v === null || v === undefined) { return 0; }
        var t = typeof v;
        if (t === "number") { return v | 0; }
        if (t === "boolean") { return v ? 1 : 0; }
        if (t === "string") { var h = 0; for (var i = 0; i < v.length; i++) { h = (h * 31 + v.charCodeAt(i)) | 0; } return h; }
        if (typeof v.GetHashCode === "function") { return v.GetHashCode(); }
        return 0;
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
        var fn = function (message) { this.message = (message === undefined ? name : message); this.$typeName = "System." + name; };
        fn.prototype = Object.create(Error.prototype);
        fn.prototype.constructor = fn;
        // Base constructor + field-init hooks so user-defined exceptions can chain.
        fn.prototype.$ctorInit = function () { };
        fn.prototype.$ctor = function (message, inner) { if (message !== undefined) { this.message = message; } if (inner !== undefined) { this.innerException = inner; } };
        fn.prototype.get_Message = function () { return this.message; };
        fn.prototype.GetType = function () { return { Name: this.$typeName }; };
        fn.prototype.ToString = function () { return this.$typeName + ": " + this.message; };
        fn.prototype.toString = function () { return this.$typeName + ": " + this.message; };
        Object.defineProperty(fn.prototype, "Message", { get: function () { return this.message; }, enumerable: false, configurable: true });
        Object.defineProperty(fn.prototype, "InnerException", { get: function () { return this.innerException || null; }, enumerable: false, configurable: true });
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
    makeException("KeyNotFoundException");
    makeException("ArgumentOutOfRangeException");

    // ---- String helpers ----------------------------------------------------

    H5R.str = {
        substr: function (s, start, len) { return len === undefined ? s.substring(start) : s.substr(start, len); },
        indexOf: function (s, v, start) { return s.indexOf(typeof v === "number" ? String.fromCharCode(v) : v, start || 0); },
        lastIndexOf: function (s, v) { return s.lastIndexOf(typeof v === "number" ? String.fromCharCode(v) : v); },
        contains: function (s, v) { return s.indexOf(v) >= 0; },
        replace: function (s, a, b) {
            a = typeof a === "number" ? String.fromCharCode(a) : a;
            b = typeof b === "number" ? String.fromCharCode(b) : b;
            return s.split(a).join(b);
        },
        padLeft: function (s, total, ch) { ch = ch === undefined ? " " : String.fromCharCode(ch); while (s.length < total) { s = ch + s; } return s; },
        padRight: function (s, total, ch) { ch = ch === undefined ? " " : String.fromCharCode(ch); while (s.length < total) { s = s + ch; } return s; },
        split: function (s, seps) {
            if (seps == null) { return s.split(/\s+/); }
            var arr = Array.isArray(seps) ? seps : [seps];
            arr = arr.map(function (c) { return typeof c === "number" ? String.fromCharCode(c) : c; });
            var re = new RegExp(arr.map(function (c) { return c.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"); }).join("|"));
            return s.split(re);
        },
        charAt: function (s, i) { return s.charCodeAt(i); },
        insert: function (s, i, v) { return s.substring(0, i) + v + s.substring(i); },
        remove: function (s, i, n) { return n === undefined ? s.substring(0, i) : s.substring(0, i) + s.substring(i + n); },
        repeat: function (ch, n) { return new Array(n + 1).join(String.fromCharCode(ch)); }
    };

    H5R.strEquals = function (a, b) { return a === b; };
    H5R.strCompare = function (a, b) { return a < b ? -1 : (a > b ? 1 : 0); };

    // Augment String.prototype with C#-cased instance members so the generic
    // emit path (which uses source names for external members) resolves them.
    var defMethod = function (proto, name, fn) { Object.defineProperty(proto, name, { value: fn, enumerable: false, writable: true, configurable: true }); };
    var defGetter = function (proto, name, fn) { Object.defineProperty(proto, name, { get: fn, enumerable: false, configurable: true }); };

    var asChar = function (v) { return typeof v === "number" ? String.fromCharCode(v) : v; };

    defGetter(String.prototype, "Length", function () { return this.length; });
    defMethod(String.prototype, "Substring", function (start, len) { return len === undefined ? this.substring(start) : this.substr(start, len); });
    defMethod(String.prototype, "IndexOf", function (v, start) { return this.indexOf(asChar(v), start || 0); });
    defMethod(String.prototype, "LastIndexOf", function (v) { return this.lastIndexOf(asChar(v)); });
    defMethod(String.prototype, "Contains", function (v) { return this.indexOf(asChar(v)) >= 0; });
    defMethod(String.prototype, "StartsWith", function (v) { return this.startsWith(asChar(v)); });
    defMethod(String.prototype, "EndsWith", function (v) { return this.endsWith(asChar(v)); });
    defMethod(String.prototype, "ToUpper", function () { return this.toUpperCase(); });
    defMethod(String.prototype, "ToUpperInvariant", function () { return this.toUpperCase(); });
    defMethod(String.prototype, "ToLower", function () { return this.toLowerCase(); });
    defMethod(String.prototype, "ToLowerInvariant", function () { return this.toLowerCase(); });
    defMethod(String.prototype, "Trim", function () { return this.trim(); });
    defMethod(String.prototype, "TrimStart", function () { return this.replace(/^\s+/, ""); });
    defMethod(String.prototype, "TrimEnd", function () { return this.replace(/\s+$/, ""); });
    defMethod(String.prototype, "Replace", function (a, b) { return this.split(asChar(a)).join(asChar(b)); });
    defMethod(String.prototype, "Split", function (sep) { return H5R.str.split(this.toString(), sep); });
    defMethod(String.prototype, "PadLeft", function (t, c) { var s = this.toString(); c = c === undefined ? " " : asChar(c); while (s.length < t) { s = c + s; } return s; });
    defMethod(String.prototype, "PadRight", function (t, c) { var s = this.toString(); c = c === undefined ? " " : asChar(c); while (s.length < t) { s = s + c; } return s; });
    defMethod(String.prototype, "ToCharArray", function () { var a = []; for (var i = 0; i < this.length; i++) { a.push(this.charCodeAt(i)); } return a; });
    defMethod(String.prototype, "Insert", function (i, v) { return this.substring(0, i) + v + this.substring(i); });
    defMethod(String.prototype, "Remove", function (i, n) { return n === undefined ? this.substring(0, i) : this.substring(0, i) + this.substring(i + n); });
    defMethod(String.prototype, "Equals", function (o) { return this.toString() === o; });
    defMethod(String.prototype, "CompareTo", function (o) { return H5R.strCompare(this.toString(), o); });
    defMethod(String.prototype, "GetHashCode", function () { var h = 0, s = this.toString(); for (var i = 0; i < s.length; i++) { h = (h * 31 + s.charCodeAt(i)) | 0; } return h; });
    defMethod(String.prototype, "ToString", function () { return this.toString(); });

    // Array.prototype C#-style members.
    defGetter(Array.prototype, "Length", function () { return this.length; });
    defMethod(Array.prototype, "GetLength", function (dim) { return this.length; });
    defMethod(Array.prototype, "GetEnumerator", function () { return H5R.getEnumerator(this); });
    defMethod(Array.prototype, "Clone", function () { return this.slice(); });

    // Static System.String.
    System.String = {
        Empty: "",
        IsNullOrEmpty: function (s) { return s == null || s.length === 0; },
        IsNullOrWhiteSpace: function (s) { return s == null || s.trim().length === 0; },
        Format: function (fmt) { var args = Array.prototype.slice.call(arguments, 1); if (args.length === 1 && Array.isArray(args[0])) { args = args[0]; } return H5R.format(fmt, args); },
        Concat: function () { var r = ""; for (var i = 0; i < arguments.length; i++) { r += H5R.toStr(arguments[i]); } return r; },
        Join: function (sep, values) { sep = asChar(sep); var arr = Array.isArray(values) ? values : H5R.toArray(values); return arr.map(function (v) { return H5R.toStr(v); }).join(sep); },
        Compare: function (a, b) { return H5R.strCompare(a, b); },
        Equals: function (a, b) { return a === b; }
    };

    H5R.toArray = function (source) {
        if (Array.isArray(source)) { return source; }
        var out = [], e = H5R.getEnumerator(source);
        while (e.moveNext()) { out.push(e.current); }
        return out;
    };

    // ---- Char helpers ------------------------------------------------------

    var Char = System.Char = {
        IsDigit: function (c) { return c >= 48 && c <= 57; },
        IsLetter: function (c) { var s = String.fromCharCode(c); return s.toLowerCase() !== s.toUpperCase(); },
        IsLetterOrDigit: function (c) { return Char.IsDigit(c) || Char.IsLetter(c); },
        IsWhiteSpace: function (c) { return /\s/.test(String.fromCharCode(c)); },
        IsUpper: function (c) { var s = String.fromCharCode(c); return s !== s.toLowerCase() && s === s.toUpperCase(); },
        IsLower: function (c) { var s = String.fromCharCode(c); return s !== s.toUpperCase() && s === s.toLowerCase(); },
        ToUpper: function (c) { return String.fromCharCode(c).toUpperCase().charCodeAt(0); },
        ToLower: function (c) { return String.fromCharCode(c).toLowerCase().charCodeAt(0); },
        Parse: function (s) { return s.charCodeAt(0); }
    };

    // ---- System.Math -------------------------------------------------------

    var toEven = function (x) {
        var f = Math.floor(x);
        var diff = x - f;
        if (diff < 0.5) { return f; }
        if (diff > 0.5) { return f + 1; }
        return (f % 2 === 0) ? f : f + 1; // banker's rounding
    };

    System.Math = {
        PI: Math.PI, E: Math.E,
        Abs: Math.abs, Sqrt: Math.sqrt, Sign: Math.sign, Floor: Math.floor, Ceiling: Math.ceil,
        Sin: Math.sin, Cos: Math.cos, Tan: Math.tan, Asin: Math.asin, Acos: Math.acos, Atan: Math.atan,
        Atan2: Math.atan2, Sinh: Math.sinh, Cosh: Math.cosh, Tanh: Math.tanh,
        Exp: Math.exp, Log: function (x, b) { return b === undefined ? Math.log(x) : Math.log(x) / Math.log(b); },
        Log10: Math.log10, Log2: Math.log2, Cbrt: Math.cbrt,
        Pow: Math.pow, Max: Math.max, Min: Math.min, Truncate: Math.trunc,
        Round: function (x, digits) {
            if (digits === undefined || digits === 0) { return toEven(x); }
            var m = Math.pow(10, digits);
            return toEven(x * m) / m;
        }
    };
    System.MathF = System.Math;

    // ---- System.Convert ----------------------------------------------------

    System.Convert = {
        ToInt32: function (x) {
            if (typeof x === "string") { var n = parseInt(x, 10); if (isNaN(n)) { throw new System.FormatException("Input string was not in a correct format."); } return n; }
            if (typeof x === "boolean") { return x ? 1 : 0; }
            return toEven(x);
        },
        ToInt64: function (x) { return System.Convert.ToInt32(x); },
        ToDouble: function (x) { if (typeof x === "string") { return parseFloat(x); } return typeof x === "boolean" ? (x ? 1 : 0) : x; },
        ToSingle: function (x) { return System.Convert.ToDouble(x); },
        ToBoolean: function (x) { if (typeof x === "string") { return x.toLowerCase() === "true"; } return !!x; },
        ToString: function (x) { return H5R.toStr(x); },
        ToChar: function (x) { return typeof x === "string" ? x.charCodeAt(0) : x; }
    };

    // ---- Numeric parsing (Int32.Parse etc.) --------------------------------

    var makeIntType = function (name) {
        var t = System[name] = {
            Parse: function (s) { var n = parseInt(s, 10); if (isNaN(n)) { throw new System.FormatException("Input string was not in a correct format."); } return n; },
            TryParse: function (s, out) { var n = parseInt(s, 10); if (isNaN(n)) { out.v = 0; return false; } out.v = n; return true; },
            MaxValue: 2147483647, MinValue: -2147483648
        };
        return t;
    };
    makeIntType("Int32"); makeIntType("Int16"); makeIntType("Int64"); makeIntType("Byte");
    System.Double = {
        Parse: function (s) { var n = parseFloat(s); if (isNaN(n)) { throw new System.FormatException("Input string was not in a correct format."); } return n; },
        TryParse: function (s, out) { var n = parseFloat(s); if (isNaN(n)) { out.v = 0; return false; } out.v = n; return true; },
        IsNaN: function (x) { return x !== x; }, IsInfinity: function (x) { return x === Infinity || x === -Infinity; },
        MaxValue: Number.MAX_VALUE, MinValue: -Number.MAX_VALUE, NaN: NaN,
        PositiveInfinity: Infinity, NegativeInfinity: -Infinity, Epsilon: Number.MIN_VALUE
    };

    // ---- System.Collections.Generic.List<T> --------------------------------

    var List = H5R.List = function () { this._ = []; };
    List.prototype.Add = function (item) { this._.push(item); };
    List.prototype.AddRange = function (items) { var e = H5R.getEnumerator(items); while (e.moveNext()) { this._.push(e.current); } };
    List.prototype.Insert = function (i, item) { this._.splice(i, 0, item); };
    List.prototype.RemoveAt = function (i) { this._.splice(i, 1); };
    List.prototype.Remove = function (item) { for (var i = 0; i < this._.length; i++) { if (H5R.equals(this._[i], item)) { this._.splice(i, 1); return true; } } return false; };
    List.prototype.Contains = function (item) { for (var i = 0; i < this._.length; i++) { if (H5R.equals(this._[i], item)) { return true; } } return false; };
    List.prototype.IndexOf = function (item) { for (var i = 0; i < this._.length; i++) { if (H5R.equals(this._[i], item)) { return i; } } return -1; };
    List.prototype.Clear = function () { this._ = []; };
    List.prototype.get_Item = function (i) { return this._[i]; };
    List.prototype.set_Item = function (i, v) { this._[i] = v; };
    List.prototype.ToArray = function () { return this._.slice(); };
    List.prototype.Sort = function (cmp) { this._.sort(cmp ? function (a, b) { return cmp(a, b); } : function (a, b) { return a < b ? -1 : (a > b ? 1 : 0); }); };
    List.prototype.Reverse = function () { this._.reverse(); };
    List.prototype.ForEach = function (a) { for (var i = 0; i < this._.length; i++) { a(this._[i]); } };
    List.prototype.GetEnumerator = function () { return H5R.getEnumerator(this._); };
    List.prototype[Symbol.iterator] = function () { return this._[Symbol.iterator](); };
    Object.defineProperty(List.prototype, "Count", { get: function () { return this._.length; } });

    // ---- Dictionary<K,V> ---------------------------------------------------

    var keyOf = function (k) { return (typeof k === "object" && k !== null) ? k : (typeof k) + ":" + k; };

    var Dictionary = H5R.Dictionary = function () { this._ = new Map(); };
    Dictionary.prototype.Add = function (k, v) { var kk = keyOf(k); if (this._.has(kk)) { throw new System.ArgumentException("An item with the same key has already been added."); } this._.set(kk, { k: k, v: v }); };
    Dictionary.prototype.set_Item = function (k, v) { this._.set(keyOf(k), { k: k, v: v }); };
    Dictionary.prototype.get_Item = function (k) { var e = this._.get(keyOf(k)); if (!e) { throw new System.KeyNotFoundException("The given key was not present in the dictionary."); } return e.v; };
    Dictionary.prototype.ContainsKey = function (k) { return this._.has(keyOf(k)); };
    Dictionary.prototype.TryGetValue = function (k, out) { var e = this._.get(keyOf(k)); if (e) { out.v = e.v; return true; } out.v = null; return false; };
    Dictionary.prototype.Remove = function (k) { return this._.delete(keyOf(k)); };
    Dictionary.prototype.Clear = function () { this._ = new Map(); };
    Dictionary.prototype.GetEnumerator = function () {
        var entries = Array.from(this._.values()); var i = -1;
        return { moveNext: function () { i++; return i < entries.length; }, get current() { return { Key: entries[i].k, Value: entries[i].v }; } };
    };
    Object.defineProperty(Dictionary.prototype, "Count", { get: function () { return this._.size; } });
    Object.defineProperty(Dictionary.prototype, "Keys", { get: function () { return Array.from(this._.values()).map(function (e) { return e.k; }); } });
    Object.defineProperty(Dictionary.prototype, "Values", { get: function () { return Array.from(this._.values()).map(function (e) { return e.v; }); } });

    // ---- HashSet<T> --------------------------------------------------------

    var HashSet = H5R.HashSet = function () { this._ = new Map(); };
    HashSet.prototype.Add = function (item) { var k = keyOf(item); if (this._.has(k)) { return false; } this._.set(k, item); return true; };
    HashSet.prototype.Contains = function (item) { return this._.has(keyOf(item)); };
    HashSet.prototype.Remove = function (item) { return this._.delete(keyOf(item)); };
    HashSet.prototype.Clear = function () { this._ = new Map(); };
    HashSet.prototype.GetEnumerator = function () { return H5R.getEnumerator(Array.from(this._.values())); };
    HashSet.prototype[Symbol.iterator] = function () { return this._.values(); };
    Object.defineProperty(HashSet.prototype, "Count", { get: function () { return this._.size; } });

    // ---- StringBuilder -----------------------------------------------------

    var StringBuilder = H5R.StringBuilder = function () { this._ = ""; };
    StringBuilder.prototype.Append = function (x) { this._ += H5R.toStr(x); return this; };
    StringBuilder.prototype.AppendLine = function (x) { this._ += (x === undefined ? "" : H5R.toStr(x)) + "\n"; return this; };
    StringBuilder.prototype.Clear = function () { this._ = ""; return this; };
    StringBuilder.prototype.ToString = function () { return this._; };
    Object.defineProperty(StringBuilder.prototype, "Length", { get: function () { return this._.length; } });

    // ---- System.Threading.Tasks.Task (mapped to Promise) -------------------

    var Tasks = H5R.ns("System.Threading.Tasks");
    Tasks.Task = {
        Delay: function (ms) { return new Promise(function (resolve) { setTimeout(resolve, ms); }); },
        FromResult: function (v) { return Promise.resolve(v); },
        CompletedTask: Promise.resolve(),
        Run: function (fn) { return Promise.resolve().then(function () { return fn(); }); },
        WhenAll: function (tasks) { return Promise.all(H5R.toArray(tasks)); },
        WhenAny: function (tasks) { return Promise.race(H5R.toArray(tasks)); },
        Yield: function () { return Promise.resolve(); }
    };
    Tasks.TaskCompletionSource = function () {
        var self = this;
        this.Task = new Promise(function (resolve, reject) { self._resolve = resolve; self._reject = reject; });
    };
    Tasks.TaskCompletionSource.prototype.SetResult = function (v) { this._resolve(v); };
    Tasks.TaskCompletionSource.prototype.SetException = function (e) { this._reject(e); };
    Tasks.TaskCompletionSource.prototype.TrySetResult = function (v) { this._resolve(v); return true; };
    Tasks.TaskCompletionSource.prototype.TrySetException = function (e) { this._reject(e); return true; };

    // ---- Iterators ---------------------------------------------------------

    // Wraps a generator function so the result can be enumerated repeatedly
    // (each enumeration starts a fresh generator), matching IEnumerable<T>.
    H5R.iter = function (genFn) {
        return {
            GetEnumerator: function () {
                var it = genFn();
                var cur;
                return { moveNext: function () { var r = it.next(); cur = r.value; return !r.done; }, get current() { return cur; } };
            }
        };
    };

    // ---- System.Linq.Enumerable (LINQ to objects, materialized) ------------

    var A = H5R.toArray;

    var Linq = H5R.ns("System.Linq");
    Linq.Enumerable = {
        Where: function (src, pred) { return A(src).filter(function (x, i) { return pred(x, i); }); },
        Select: function (src, sel) { return A(src).map(function (x, i) { return sel(x, i); }); },
        SelectMany: function (src, sel) { var out = []; A(src).forEach(function (x, i) { A(sel(x, i)).forEach(function (y) { out.push(y); }); }); return out; },
        Count: function (src, pred) { return pred ? A(src).filter(function (x) { return pred(x); }).length : A(src).length; },
        LongCount: function (src, pred) { return Linq.Enumerable.Count(src, pred); },
        Sum: function (src, sel) { return A(src).reduce(function (a, x) { return a + (sel ? sel(x) : x); }, 0); },
        Average: function (src, sel) { var a = A(src); if (!a.length) { throw new System.InvalidOperationException("Sequence contains no elements"); } return a.reduce(function (s, x) { return s + (sel ? sel(x) : x); }, 0) / a.length; },
        Min: function (src, sel) { var a = A(src).map(function (x) { return sel ? sel(x) : x; }); return Math.min.apply(null, a); },
        Max: function (src, sel) { var a = A(src).map(function (x) { return sel ? sel(x) : x; }); return Math.max.apply(null, a); },
        Any: function (src, pred) { var a = A(src); return pred ? a.some(function (x) { return pred(x); }) : a.length > 0; },
        All: function (src, pred) { return A(src).every(function (x) { return pred(x); }); },
        Contains: function (src, value) { return A(src).some(function (x) { return H5R.equals(x, value); }); },
        First: function (src, pred) { var a = A(src); for (var i = 0; i < a.length; i++) { if (!pred || pred(a[i])) { return a[i]; } } throw new System.InvalidOperationException("Sequence contains no matching element"); },
        FirstOrDefault: function (src, pred) { var a = A(src); for (var i = 0; i < a.length; i++) { if (!pred || pred(a[i])) { return a[i]; } } return null; },
        Last: function (src, pred) { var a = A(src); for (var i = a.length - 1; i >= 0; i--) { if (!pred || pred(a[i])) { return a[i]; } } throw new System.InvalidOperationException("Sequence contains no matching element"); },
        LastOrDefault: function (src, pred) { var a = A(src); for (var i = a.length - 1; i >= 0; i--) { if (!pred || pred(a[i])) { return a[i]; } } return null; },
        Single: function (src, pred) { var a = A(src).filter(function (x) { return !pred || pred(x); }); if (a.length !== 1) { throw new System.InvalidOperationException("Sequence does not contain exactly one element"); } return a[0]; },
        SingleOrDefault: function (src, pred) { var a = A(src).filter(function (x) { return !pred || pred(x); }); if (a.length > 1) { throw new System.InvalidOperationException("Sequence contains more than one element"); } return a.length ? a[0] : null; },
        ElementAt: function (src, i) { return A(src)[i]; },
        ElementAtOrDefault: function (src, i) { var a = A(src); return i >= 0 && i < a.length ? a[i] : null; },
        Take: function (src, n) { return A(src).slice(0, n); },
        Skip: function (src, n) { return A(src).slice(n); },
        TakeWhile: function (src, pred) { var out = [], a = A(src); for (var i = 0; i < a.length && pred(a[i]); i++) { out.push(a[i]); } return out; },
        SkipWhile: function (src, pred) { var a = A(src), i = 0; while (i < a.length && pred(a[i])) { i++; } return a.slice(i); },
        Reverse: function (src) { return A(src).slice().reverse(); },
        Distinct: function (src) { var seen = new Map(), out = []; A(src).forEach(function (x) { var k = keyOf(x); if (!seen.has(k)) { seen.set(k, true); out.push(x); } }); return out; },
        Concat: function (src, other) { return A(src).concat(A(other)); },
        Append: function (src, item) { return A(src).concat([item]); },
        Prepend: function (src, item) { return [item].concat(A(src)); },
        DefaultIfEmpty: function (src, dflt) { var a = A(src); return a.length ? a : [dflt === undefined ? null : dflt]; },
        OrderBy: function (src, key) { return orderByImpl(A(src), key, false); },
        OrderByDescending: function (src, key) { return orderByImpl(A(src), key, true); },
        ThenBy: function (src, key) { return thenByImpl(src, key, false); },
        ThenByDescending: function (src, key) { return thenByImpl(src, key, true); },
        ToArray: function (src) { return A(src).slice(); },
        ToList: function (src) { var l = new H5R.List(); l._ = A(src).slice(); return l; },
        ToHashSet: function (src) { var s = new H5R.HashSet(); A(src).forEach(function (x) { s.Add(x); }); return s; },
        ToDictionary: function (src, keySel, valSel) { var d = new H5R.Dictionary(); A(src).forEach(function (x) { d.set_Item(keySel(x), valSel ? valSel(x) : x); }); return d; },
        Aggregate: function (src, seedOrFunc, func) {
            var a = A(src);
            if (func === undefined) { var acc = a[0]; for (var i = 1; i < a.length; i++) { acc = seedOrFunc(acc, a[i]); } return acc; }
            var res = seedOrFunc; for (var j = 0; j < a.length; j++) { res = func(res, a[j]); } return res;
        },
        GroupBy: function (src, keySel, elemSel) {
            var groups = new Map(), order = [];
            A(src).forEach(function (x) {
                var k = keySel(x), kk = keyOf(k);
                if (!groups.has(kk)) { groups.set(kk, { Key: k, _: [] }); order.push(kk); }
                groups.get(kk)._.push(elemSel ? elemSel(x) : x);
            });
            return order.map(function (kk) { var g = groups.get(kk); var arr = g._; arr.Key = g.Key; return arr; });
        },
        Range: function (start, count) { var out = []; for (var i = 0; i < count; i++) { out.push(start + i); } return out; },
        Repeat: function (value, count) { var out = []; for (var i = 0; i < count; i++) { out.push(value); } return out; },
        Empty: function () { return []; }
    };

    var cmpVals = function (a, b) { return a < b ? -1 : (a > b ? 1 : 0); };

    var orderByImpl = function (arr, key, desc) {
        var cmps = [{ key: key, desc: desc }];
        var out = arr.slice();
        stableSort(out, cmps);
        out.$cmps = cmps;
        return out;
    };

    var thenByImpl = function (src, key, desc) {
        var cmps = (src.$cmps || []).concat([{ key: key, desc: desc }]);
        var out = A(src).slice();
        stableSort(out, cmps);
        out.$cmps = cmps;
        return out;
    };

    var stableSort = function (arr, cmps) {
        var indexed = arr.map(function (v, i) { return { v: v, i: i }; });
        indexed.sort(function (x, y) {
            for (var c = 0; c < cmps.length; c++) {
                var r = cmpVals(cmps[c].key(x.v), cmps[c].key(y.v));
                if (cmps[c].desc) { r = -r; }
                if (r !== 0) { return r; }
            }
            return x.i - y.i;
        });
        for (var i = 0; i < arr.length; i++) { arr[i] = indexed[i].v; }
    };

})(typeof globalThis !== "undefined" ? globalThis : this);
