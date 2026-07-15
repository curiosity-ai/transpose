    Transpose.define("System.IFormattable", {
        $kind: "interface",
        statics: {
            $is: function (obj) {
                if (Transpose.isNumber(obj) || Transpose.isDate(obj)) {
                    return true;
                }

                return Transpose.is(obj, System.IFormattable, true);
            }
        }
    });

    Transpose.define("System.IComparable", {
        $kind: "interface",

        statics: {
            $is: function (obj) {
                if (Transpose.isNumber(obj) || Transpose.isDate(obj) || Transpose.isBoolean(obj) || Transpose.isString(obj)) {
                    return true;
                }

                return Transpose.is(obj, System.IComparable, true);
            }
        }
    });

    Transpose.define("System.IFormatProvider", {
        $kind: "interface"
    });

    Transpose.define("System.ICloneable", {
        $kind: "interface"
    });

    Transpose.define("System.IComparable$1", function (T) {
        return {
            $kind: "interface",

            statics: {
                $is: function (obj) {
                    if (Transpose.isNumber(obj) && T.$number && T.$is(obj) || Transpose.isDate(obj) && (T === Date || T === System.DateTime) || Transpose.isBoolean(obj) && (T === Boolean || T === System.Boolean) || Transpose.isString(obj) && (T === String || T === System.String)) {
                        return true;
                    }

                    return Transpose.is(obj, System.IComparable$1(T), true);
                },

                isAssignableFrom: function (type) {
                    if (type === System.DateTime && T === Date) {
                        return true;
                    }

                    return Transpose.Reflection.getInterfaces(type).indexOf(System.IComparable$1(T)) >= 0;
                }
            }
        };
    });

    Transpose.define("System.IEquatable$1", function (T) {
        return {
            $kind: "interface",

            statics: {
                $is: function (obj) {
                    if (Transpose.isNumber(obj) && T.$number && T.$is(obj) || Transpose.isDate(obj) && (T === Date || T === System.DateTime) || Transpose.isBoolean(obj) && (T === Boolean || T === System.Boolean) || Transpose.isString(obj) && (T === String || T === System.String)) {
                        return true;
                    }

                    return Transpose.is(obj, System.IEquatable$1(T), true);
                },

                isAssignableFrom: function (type) {
                    if (type === System.DateTime && T === Date) {
                        return true;
                    }

                    return Transpose.Reflection.getInterfaces(type).indexOf(System.IEquatable$1(T)) >= 0;
                }
            }
        };
    });

    Transpose.define("Transpose.IPromise", {
        $kind: "interface"
    });

    Transpose.define("System.IDisposable", {
        $kind: "interface"
    });

    Transpose.define("System.IAsyncResult", {
        $kind: "interface"
    });
