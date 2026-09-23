    Transpose.define("System.Exception", {
        config: {
            properties: {
                Message: {
                    get: function () {
                        return this.message;
                    }
                },

                InnerException: {
                    get: function () {
                        return this.innerException;
                    }
                },

                StackTrace: {
                    get: function () {
                        return this.errorStack.stack;
                    }
                },

                // Generated code addresses an [External] type's members in camelCase (ex.StackTrace
                // -> ex.stackTrace, ex.HResult -> ex.hResult). message/innerException/data already
                // exist as lowercase fields; StackTrace/HResult need explicit camelCase accessors.
                stackTrace: {
                    get: function () {
                        return this.errorStack.stack;
                    }
                },

                hResult: {
                    get: function () {
                        return this._HResult;
                    },
                    set: function (value) {
                        this._HResult = value;
                    }
                },

                Data: {
                    get: function () {
                        return this.data;
                    }
                },

                HResult: {
                    get: function () {
                        return this._HResult;
                    },
                    set: function (value) {
                        this._HResult = value;
                    }
                }
            }
        },

        ctor: function (message, innerException) {
            this.$initialize();
            this.message = message ? message : ("Exception of type '" + Transpose.getTypeName(this) + "' was thrown.");
            this.innerException = innerException ? innerException : null;
            this.errorStack = new Error(this.message);
            this.data = new (System.Collections.Generic.Dictionary$2(System.Object, System.Object))();
        },

        getBaseException: function () {
            var inner = this.innerException;
            var back = this;

            while (inner != null) {
                back = inner;
                inner = inner.innerException;
            }

            return back;
        },

        toString: function () {
            var builder = Transpose.getTypeName(this);

            if (this.Message != null) {
                builder += ": " + this.Message + "\n";
            } else {
                builder += "\n";
            }

            if (this.StackTrace != null) {
                builder += this.StackTrace + "\n";
            }

            return builder;
        },

        statics: {
            create: function (error) {
                if (Transpose.is(error, System.Exception)) {
                    return error;
                }

                var ex;

                if (error instanceof TypeError) {
                    ex = new System.NullReferenceException.$ctor1(error.message);
                } else if (error instanceof RangeError) {
                    // (paramName, message): $ctor1 is the paramName-only overload, which dropped the
                    // error's text in favour of the generic "out of the range" message.
                    ex = new System.ArgumentOutOfRangeException.$ctor4(null, error.message);
                } else if (error instanceof Error) {
                    ex = new System.SystemException.$ctor1(error.message);
                } else if (error && error.error && error.error.stack) {
                    ex = new System.Exception(error.error.stack);
                } else {
                    // Anything else JavaScript can throw: an object literal with a message, a string, a
                    // number, a boolean. `throw 0` and `throw ''` are values too — only null/undefined
                    // carry nothing, and they take the default message.
                    var text = null;
                    if (error !== null && error !== undefined) {
                        text = (error.message !== null && error.message !== undefined) ? String(error.message) : String(error);
                    }
                    ex = new System.Exception(text);
                }

                // The original value is the stack source (Exception.StackTrace). A thrown null or
                // undefined has none, and keeps the Error the constructor captured instead —
                // assigning it would make StackTrace (and ToString, which reads it) throw.
                if (error !== null && error !== undefined) {
                    ex.errorStack = error;
                }

                return ex;
            }
        }
    });
