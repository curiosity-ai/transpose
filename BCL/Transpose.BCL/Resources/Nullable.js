    var nullable = {
        hasValue: Transpose.hasValue,

        getValue: function (obj) {
            obj = Transpose.unbox(obj, true);

            if (!Transpose.hasValue(obj)) {
                throw new System.InvalidOperationException.$ctor1("Nullable instance doesn't have a value.");
            }

            return obj;
        },

        getValueOrDefault: function (obj, defValue) {
            return Transpose.hasValue(obj) ? obj : defValue;
        },

        add: function (a, b) {
            return Transpose.hasValue$1(a, b) ? a + b : null;
        },

        band: function (a, b) {
            return Transpose.hasValue$1(a, b) ? a & b : null;
        },

        bor: function (a, b) {
            return Transpose.hasValue$1(a, b) ? a | b : null;
        },

        and: function (a, b) {
            if (a === true && b === true) {
                return true;
            } else if (a === false || b === false) {
                return false;
            }

            return null;
        },

        or: function (a, b) {
            if (a === true || b === true) {
                return true;
            } else if (a === false && b === false) {
                return false;
            }

            return null;
        },

        div: function (a, b) {
            return Transpose.hasValue$1(a, b) ? a / b : null;
        },

        eq: function (a, b) {
            return !Transpose.hasValue(a) ? !Transpose.hasValue(b) : (a === b);
        },

        equals: function (a, b, fn) {
            // Both sides need the null guard, not just `a`: with a value on the left and null on the
            // right this fell through to Transpose.equals(someInt64, null), which reads `.low` off the
            // null and throws. Lifted equality is "null equals only null, otherwise compare values".
            if (!Transpose.hasValue(a) || !Transpose.hasValue(b)) {
                return Transpose.hasValue(a) === Transpose.hasValue(b);
            }

            return fn ? fn(a, b) : Transpose.equals(a, b);
        },

        // `Nullable<T>.Equals(T other)` — the strongly-typed IEquatable<T> overload. The BCL templates
        // it as equalsT (the same name a record's synthesized IEquatable<T>.Equals gets), and without
        // it every `someNullable.Equals(value)` threw "equalsT is not a function".
        equalsT: function (a, b) {
            return System.Nullable.equals(a, b);
        },

        toString: function (a, fn) {
            return !Transpose.hasValue(a) ? "" : (fn ? fn(a) : a.toString());
        },

        toStringFn: function (fn) {
            return function (v) {
                return System.Nullable.toString(v, fn);
            };
        },

        getHashCode: function (a, fn) {
            return !Transpose.hasValue(a) ? 0 : (fn ? fn(a) : Transpose.getHashCode(a));
        },

        getHashCodeFn: function (fn) {
            return function (v) {
                return System.Nullable.getHashCode(v, fn);
            };
        },

        xor: function (a, b) {
            if (Transpose.hasValue$1(a, b)) {
                if (Transpose.isBoolean(a) && Transpose.isBoolean(b)) {
                    return a != b;
                }

                return a ^ b;
            }

            return null;
        },

        gt: function (a, b) {
            return Transpose.hasValue$1(a, b) && a > b;
        },

        gte: function (a, b) {
            return Transpose.hasValue$1(a, b) && a >= b;
        },

        neq: function (a, b) {
            return !Transpose.hasValue(a) ? Transpose.hasValue(b) : (a !== b);
        },

        lt: function (a, b) {
            return Transpose.hasValue$1(a, b) && a < b;
        },

        lte: function (a, b) {
            return Transpose.hasValue$1(a, b) && a <= b;
        },

        mod: function (a, b) {
            return Transpose.hasValue$1(a, b) ? a % b : null;
        },

        mul: function (a, b) {
            return Transpose.hasValue$1(a, b) ? a * b : null;
        },

        imul: function (a, b) {
            return Transpose.hasValue$1(a, b) ? Transpose.Int.mul(a, b) : null;
        },

        sl: function (a, b) {
            return Transpose.hasValue$1(a, b) ? a << b : null;
        },

        sr: function (a, b) {
            return Transpose.hasValue$1(a, b) ? a >> b : null;
        },

        srr: function (a, b) {
            return Transpose.hasValue$1(a, b) ? a >>> b : null;
        },

        sub: function (a, b) {
            return Transpose.hasValue$1(a, b) ? a - b : null;
        },

        bnot: function (a) {
            return Transpose.hasValue(a) ? ~a : null;
        },

        neg: function (a) {
            return Transpose.hasValue(a) ? -a : null;
        },

        not: function (a) {
            return Transpose.hasValue(a) ? !a : null;
        },

        pos: function (a) {
            return Transpose.hasValue(a) ? +a : null;
        },

        lift: function () {
            for (var i = 1; i < arguments.length; i++) {
                if (!Transpose.hasValue(arguments[i])) {
                    return null;
                }
            }

            if (arguments[0] == null) {
                return null;
            }

            if (arguments[0].apply == undefined) {
                return arguments[0];
            }

            return arguments[0].apply(null, Array.prototype.slice.call(arguments, 1));
        },

        lift1: function (f, o) {
            return Transpose.hasValue(o) ? (typeof f === "function" ? f.apply(null, Array.prototype.slice.call(arguments, 1)) : o[f].apply(o, Array.prototype.slice.call(arguments, 2))) : null;
        },

        lift2: function (f, a, b) {
            return Transpose.hasValue$1(a, b) ? (typeof f === "function" ? f.apply(null, Array.prototype.slice.call(arguments, 1)) : a[f].apply(a, Array.prototype.slice.call(arguments, 2))) : null;
        },

        liftcmp: function (f, a, b) {
            return Transpose.hasValue$1(a, b) ? (typeof f === "function" ? f.apply(null, Array.prototype.slice.call(arguments, 1)) : a[f].apply(a, Array.prototype.slice.call(arguments, 2))) : false;
        },

        lifteq: function (f, a, b) {
            var va = Transpose.hasValue(a),
                vb = Transpose.hasValue(b);

            return (!va && !vb) || (va && vb && (typeof f === "function" ? f.apply(null, Array.prototype.slice.call(arguments, 1)) : a[f].apply(a, Array.prototype.slice.call(arguments, 2))));
        },

        liftne: function (f, a, b) {
            var va = Transpose.hasValue(a),
                vb = Transpose.hasValue(b);

            return (va !== vb) || (va && (typeof f === "function" ? f.apply(null, Array.prototype.slice.call(arguments, 1)) : a[f].apply(a, Array.prototype.slice.call(arguments, 2))));
        },

        getUnderlyingType: function (nullableType) {
            if (!nullableType) {
                throw new System.ArgumentNullException.$ctor1("nullableType");
            }

            if (Transpose.Reflection.isGenericType(nullableType) &&
                !Transpose.Reflection.isGenericTypeDefinition(nullableType)) {
                var genericType = Transpose.Reflection.getGenericTypeDefinition(nullableType);

                if (genericType === System.Nullable$1) {
                    return Transpose.Reflection.getGenericArguments(nullableType)[0];
                }
            }

            return null;
        },

        compare: function (n1, n2) {
            return System.Collections.Generic.Comparer$1.$default.compare(n1, n2);
        }
    };

    System.Nullable = nullable;

    Transpose.define("System.Nullable$1", function (T) {
        return {
            $kind: "struct",

            statics: {
                $nullable: true,
                $nullableType: T,
                getDefaultValue: function () {
                    return null;
                },

                $is: function (obj) {
                    return Transpose.is(obj, T);
                }
            }
        };
    });
