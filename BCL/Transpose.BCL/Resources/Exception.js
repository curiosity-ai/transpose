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
                        // errorStack holds whatever carried the frames: the Error captured by the
                        // constructor, or - for an exception wrapping something the engine threw -
                        // that value itself. A thrown non-Error has no `stack`, and a caller may
                        // have assigned anything at all, so answer null rather than throwing or
                        // handing C# an undefined.
                        var carrier = this.errorStack;

                        return (carrier && typeof carrier.stack === "string") ? carrier.stack : null;
                    }
                },

                // Generated code addresses an [External] type's members in camelCase (ex.StackTrace
                // -> ex.stackTrace, ex.HResult -> ex.hResult). message/innerException/data already
                // exist as lowercase fields; StackTrace/HResult need explicit camelCase accessors.
                stackTrace: {
                    get: function () {
                        var carrier = this.errorStack;

                        return (carrier && typeof carrier.stack === "string") ? carrier.stack : null;
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

            // .NET reports the whole chain. Without this an exception that wraps the error that
            // actually failed - a browser error behind an HttpRequestException, say - printed
            // nothing at all about it, which is the one thing the reader needs.
            var inner = this.InnerException;

            if (inner != null && inner !== this) {
                builder += " ---> " + Transpose.toString(inner) + "\n   --- End of inner exception stack trace ---\n";
            }

            if (this.StackTrace != null) {
                builder += this.StackTrace + "\n";
            }

            return builder;
        },

        statics: {
            // Wrap a value the engine handed us - something thrown across an interop boundary, a
            // rejected promise's reason, a DOM error event - in a System.Exception without losing
            // what it carried. Two things a browser gives us are the whole diagnostic and both were
            // easy to drop here: the message (which has to reach C# as a *string*, not as the Error
            // object itself) and the stack, whose frames point at the real throw site rather than at
            // this wrapper.
            create: function (error) {
                if (Transpose.is(error, System.Exception)) {
                    return error;
                }

                // An ErrorEvent (window.onerror, worker.onerror) or a PromiseRejectionEvent wraps
                // the value that was actually thrown; that value is the one carrying the stack, so
                // unwrap it and keep the event as a fallback for the message.
                var source = (error && !(error instanceof Error)) ? (error.error || error.reason || error) : error;
                var carrier = System.Exception.stackCarrier(source) || System.Exception.stackCarrier(error);
                var message = System.Exception.describe(source);

                if (message == null && source !== error) {
                    message = System.Exception.describe(error);
                }

                var ex;

                if (source instanceof TypeError) {
                    ex = new System.NullReferenceException.$ctor1(message);
                } else if (source instanceof RangeError) {
                    // (message, innerException), not (paramName): the browser's message is a
                    // sentence about what went wrong, and reporting it as a parameter name buried it
                    // inside "Specified argument was out of the range of valid values.".
                    ex = new System.ArgumentOutOfRangeException.$ctor2(message, null);
                } else if (source instanceof Error || carrier) {
                    ex = new System.SystemException.$ctor1(message);
                } else {
                    ex = new System.Exception(message);
                }

                if (carrier) {
                    // Report the browser's own frames: left alone, the only stack on the exception is
                    // the one its constructor just captured, every frame of which is inside tps.js.
                    // Conversely a thrown non-Error has no frames to take, and that captured stack -
                    // taken in the handler, close to the throw - is then the best there is, so
                    // assigning unconditionally (as this did) left it with no stack trace at all.
                    ex.errorStack = carrier;
                }

                // The value exactly as it arrived, so nothing it carried is unreachable: an
                // ErrorEvent's filename/lineno, a CloseEvent's code/reason, a DOMException's name.
                ex.errorSource = error;

                return ex;
            },

            // The value carrying a JavaScript stack, if any. Duck-typed rather than
            // `instanceof Error`, because instanceof is per-realm: an error from an iframe or a
            // worker is a real Error that fails the test.
            stackCarrier: function (value) {
                return (value && typeof value.stack === "string" && value.stack.length > 0) ? value : null;
            },

            // A description of a raw JavaScript value as a string - Exception.Message is a string in
            // C#, and handing back the Error object instead made every string operation on it
            // (Length, Contains, IsNullOrEmpty) answer for an object.
            describe: function (value) {
                if (value === null || value === undefined) {
                    return null;
                }

                if (typeof value === "string") {
                    return value;
                }

                if (typeof value.message === "string" && value.message.length > 0) {
                    return value.message;
                }

                // A DOM event - a WebSocket or XHR `error`, a failed <script> - carries no message
                // and stringifies to "[object Event]", which says nothing. Name the event and what
                // raised it instead.
                if (typeof value.type === "string" && value.type.length > 0 && "target" in value) {
                    var raisedBy = (value.target && value.target.constructor && value.target.constructor.name !== "Object") ? value.target.constructor.name : null;

                    return "A JavaScript '" + value.type + "' event was raised" + (raisedBy ? " by " + raisedBy : "") + ".";
                }

                try {
                    return String(value);
                } catch (e) {
                    return null;
                }
            }
        }
    });
