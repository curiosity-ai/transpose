    Transpose.define("System.Collections.Generic.EqualityComparer$1", function (T) {
        return {
            inherits: [System.Collections.Generic.IEqualityComparer$1(T)],

            statics: {
                config: {
                    init: function () {
                        this.def = new (System.Collections.Generic.EqualityComparer$1(T))();
                    }
                }
            },

            config: {
                alias: [
                    "equals2", ["System$Collections$Generic$IEqualityComparer$1$" + Transpose.getTypeAlias(T) + "$equals2", "System$Collections$Generic$IEqualityComparer$1$equals2"],
                    "getHashCode2", ["System$Collections$Generic$IEqualityComparer$1$" + Transpose.getTypeAlias(T) + "$getHashCode2", "System$Collections$Generic$IEqualityComparer$1$getHashCode2"]
                ]
            },

            equals2: function (x, y) {
                if (!Transpose.isDefined(x, true)) {
                    return !Transpose.isDefined(y, true);
                } else if (Transpose.isDefined(y, true)) {
                    var isH5 = x && x.$$name;

                    if (Transpose.isFunction(x) && Transpose.isFunction(y)) {
                        return Transpose.fn.equals.call(x, y);
                    } else if (!isH5 || x && x.$boxed || y && y.$boxed) {
                        return Transpose.equals(x, y);
                    } else if (Transpose.isFunction(x.equalsT)) {
                        return Transpose.equalsT(x, y);
                    } else if (Transpose.isFunction(x.equals)) {
                        return Transpose.equals(x, y);
                    }

                    return x === y;
                }

                return false;
            },

            getHashCode2: function (obj) {
                return Transpose.isDefined(obj, true) ? Transpose.getHashCode(obj) : 0;
            }
        };
    });

    System.Collections.Generic.EqualityComparer$1.$default = new (System.Collections.Generic.EqualityComparer$1(System.Object))();
