    Transpose.define("System.Threading.Tasks.Task", {
        inherits: [System.IDisposable, System.IAsyncResult],

        config: {
            alias: [
                "dispose", "System$IDisposable$Dispose",
                "AsyncState", "System$IAsyncResult$AsyncState",
                "CompletedSynchronously", "System$IAsyncResult$CompletedSynchronously",
                "IsCompleted", "System$IAsyncResult$IsCompleted"
            ],

            properties: {
                IsCompleted: {
                    get: function () {
                        return this.isCompleted();
                    }
                }
            }
        },

        ctor: function (action, state) {
            this.$initialize();
            this.action = action;
            this.state = state;
            this.AsyncState = state;
            this.CompletedSynchronously = false;
            this.exception = null;
            this.status = System.Threading.Tasks.TaskStatus.created;
            this.callbacks = [];
            this.result = null;
        },

        statics: {
            queue: [],

            runQueue: function () {
                var queue = System.Threading.Tasks.Task.queue.slice(0);
                System.Threading.Tasks.Task.queue = [];

                for (var i = 0; i < queue.length; i++) {
                    queue[i]();
                }
            },

            schedule: function (fn) {
                System.Threading.Tasks.Task.queue.push(fn);
                Transpose.setImmediate(System.Threading.Tasks.Task.runQueue);
            },

            delay: function (delay, state) {
                var tcs = new System.Threading.Tasks.TaskCompletionSource(),
                    token,
                    cancelCallback = false;

                if (Transpose.is(state, System.Threading.CancellationToken)) {
                    token = state;
                    state = undefined;
                }

                if (token) {
                    token.cancelWasRequested = function () {
                        if (!cancelCallback) {
                            cancelCallback = true;
                            clearTimeout(clear);

                            tcs.setCanceled();
                        }
                    };
                }

                var ms = delay;
                if (Transpose.is(delay, System.TimeSpan)) {
                    ms = delay.getTotalMilliseconds();
                }

                var clear = setTimeout(function () {
                    if (!cancelCallback) {
                        cancelCallback = true;
                        tcs.setResult(state);
                    }
                }, ms);

                if (token && token.getIsCancellationRequested()) {
                    Transpose.setImmediate(token.cancelWasRequested);
                }

                return tcs.task;
            },

            yield: function () {
                var tcs = new System.Threading.Tasks.TaskCompletionSource();

                Transpose.setImmediate(function () {
                    tcs.setResult(null);
                });

                return tcs.task;
            },

            fromResult: function (result, T) {
                var t = new (System.Threading.Tasks.Task$1(T || System.Object))();

                t.status = System.Threading.Tasks.TaskStatus.ranToCompletion;
                t.result = result;

                return t;
            },

            fromException: function (exception, T) {
                var t = new (System.Threading.Tasks.Task$1(T || System.Object))();

                t.status = System.Threading.Tasks.TaskStatus.faulted;
                // Task.Exception is an AggregateException in .NET, always - and it wraps whatever it
                // is given, an AggregateException included. Storing the bare exception left
                // task.Exception.InnerException null, so the fault was simply unreachable from the
                // Task, and made every reader of `.innerExceptions` a special case (WhenAll's
                // continuation died on one, silently).
                t.exception = new System.AggregateException(null, [System.Exception.create(exception)]);

                return t;
            },            

            fromCanceled: function (token, T) {
                // .NET refuses a token that is not already cancelled - the task has to BE cancelled,
                // and there is nothing to cancel it later.
                if (!token || !token.getIsCancellationRequested()) {
                    throw new System.ArgumentOutOfRangeException.$ctor1("cancellationToken");
                }

                var t = new (System.Threading.Tasks.Task$1(T || System.Object))();

                t.cancel();

                return t;
            },

            run: function (fn, token) {
                var tcs = new System.Threading.Tasks.TaskCompletionSource();

                System.Threading.Tasks.Task.schedule(function () {
                    // Task.Run(fn, token)'s token cancels the SCHEDULING, not the body: it is read
                    // when the work reaches the front of the queue, and a cancellation by then means
                    // fn is never invoked. Deliberately not short-circuited before the schedule -
                    // .NET reports IsCanceled false immediately after the call even for an
                    // already-cancelled token, because the transition happens on the scheduler.
                    if (token && token.getIsCancellationRequested()) {
                        tcs.setCanceled();

                        return;
                    }

                    try {
                        // asTask, so a body that hands back a native promise - a [Script]-bound JS
                        // async function, which is what Func<Task> often is here - is awaited rather
                        // than becoming the task's RESULT, with its rejection unobserved.
                        var result = System.Threading.Tasks.Task.asTask(fn());

                        if (Transpose.is(result, System.Threading.Tasks.Task)) {
                            result.continueWith(function () {
                                if (result.isCanceled()) {
                                    // Unwrapping keeps the KIND of the inner completion: reporting a
                                    // cancelled inner task as a fault turned "the caller changed
                                    // their mind" into an error, and the OperationCanceledException
                                    // a caller catches into an unexpected one.
                                    tcs.setCanceled();
                                } else if (result.isFaulted()) {
                                    // As in _getResult: the inner task may have been faulted with a
                                    // bare exception, which has no innerExceptions to read.
                                    tcs.setException(result.exception && result.exception.innerExceptions && result.exception.innerExceptions.Count > 0 ? result.exception.innerExceptions.getItem(0) : result.exception);
                                } else {
                                    tcs.setResult(result.getAwaitedResult());
                                }
                            });
                        } else {
                            tcs.setResult(result);
                        }
                    } catch (e) {
                        tcs.setException(System.Exception.create(e));
                    }
                });

                return tcs.task;
            },

            // Each element as a real Task. `await` accepts a native promise (Transpose.toPromise),
            // so a [Script] binding that returns one - the natural way to bind a JS async function -
            // behaves like a working Task right up until it is handed to WhenAll/WhenAny, which
            // drive their arguments through continueWith: the call died on "continueWith is not a
            // function" and, being inside a promise, took the rejection out of reach with it.
            asTasks: function (tasks) {
                var out = new Array(tasks.length),
                    i;

                for (i = 0; i < tasks.length; i++) {
                    out[i] = System.Threading.Tasks.Task.asTask(tasks[i]);
                }

                return out;
            },

            asTask: function (awaitable) {
                if (!awaitable || typeof awaitable.continueWith === "function" || typeof awaitable.then !== "function") {
                    return awaitable;
                }

                var tcs = new System.Threading.Tasks.TaskCompletionSource();

                awaitable.then(
                    function (value) { tcs.trySetResult(value); },
                    // Normalised, so an awaiter of the WhenAll sees the same System.Exception it
                    // would have seen awaiting the promise directly.
                    function (reason) { tcs.trySetException(System.Exception.create(reason)); }
                );

                return tcs.task;
            },

            whenAll: function (tasks) {
                var tcs = new System.Threading.Tasks.TaskCompletionSource(),
                    result,
                    executing,
                    cancelled = false,
                    exceptions = [],
                    i;

                if (Transpose.is(tasks, System.Collections.IEnumerable)) {
                    tasks = Transpose.toArray(tasks);
                } else if (!Transpose.isArray(tasks)) {
                    tasks = Array.prototype.slice.call(arguments, 0);
                }

                tasks = System.Threading.Tasks.Task.asTasks(tasks);

                if (tasks.length === 0) {
                    tcs.setResult([]);

                    return tcs.task;
                }

                executing = tasks.length;
                result = new Array(tasks.length);

                for (i = 0; i < tasks.length; i++) {
                    (function (i) {
                        tasks[i].continueWith(function (t) {
                            switch (t.status) {
                                case System.Threading.Tasks.TaskStatus.ranToCompletion:
                                    result[i] = t.getResult();
                                    break;
                                case System.Threading.Tasks.TaskStatus.canceled:
                                    cancelled = true;
                                    break;
                                case System.Threading.Tasks.TaskStatus.faulted:
                                    // A task can be faulted with a bare exception rather than an
                                    // AggregateException (Task.FromException does exactly that), so
                                    // there may be no innerExceptions to range over. Reading it
                                    // blindly threw from inside this continuation, which left the
                                    // WhenAll task un-completed forever: every awaiter hung and the
                                    // program simply stopped, with nothing reported anywhere.
                                    if (t.exception && t.exception.innerExceptions) {
                                        System.Array.addRange(exceptions, t.exception.innerExceptions);
                                    } else if (t.exception) {
                                        exceptions.push(t.exception);
                                    }
                                    break;
                                default:
                                    throw new System.InvalidOperationException.$ctor1("Invalid task status: " + t.status);
                            }

                            if (--executing === 0) {
                                if (exceptions.length > 0) {
                                    tcs.setException(exceptions);
                                } else if (cancelled) {
                                    tcs.setCanceled();
                                } else {
                                    tcs.setResult(result);
                                }
                            }
                        });
                    })(i);
                }

                return tcs.task;
            },

            whenAny: function (tasks) {
                if (Transpose.is(tasks, System.Collections.IEnumerable)) {
                    tasks = Transpose.toArray(tasks);
                } else if (!Transpose.isArray(tasks)) {
                    tasks = Array.prototype.slice.call(arguments, 0);
                }

                if (!tasks.length) {
                    throw new System.ArgumentException.$ctor1("At least one task is required");
                }

                tasks = System.Threading.Tasks.Task.asTasks(tasks);

                var tcs = new System.Threading.Tasks.TaskCompletionSource(),
                    i;

                for (i = 0; i < tasks.length; i++) {
                    tasks[i].continueWith(function (t) {
                        tcs.trySetResult(t);
                    });
                }

                return tcs.task;
            },

            fromCallback: function (target, method) {
                var tcs = new System.Threading.Tasks.TaskCompletionSource(),
                    args = Array.prototype.slice.call(arguments, 2),
                    callback;

                callback = function (value) {
                    tcs.setResult(value);
                };

                args.push(callback);

                target[method].apply(target, args);

                return tcs.task;
            },

            fromCallbackResult: function (target, method, resultHandler) {
                var tcs = new System.Threading.Tasks.TaskCompletionSource(),
                    args = Array.prototype.slice.call(arguments, 3),
                    callback;

                callback = function (value) {
                    tcs.setResult(value);
                };

                resultHandler(args, callback);

                target[method].apply(target, args);

                return tcs.task;
            },

            fromCallbackOptions: function (target, method, name) {
                var tcs = new System.Threading.Tasks.TaskCompletionSource(),
                    args = Array.prototype.slice.call(arguments, 3),
                    callback;

                callback = function (value) {
                    tcs.setResult(value);
                };

                args[0] = args[0] || {};
                args[0][name] = callback;

                target[method].apply(target, args);

                return tcs.task;
            },

            fromPromise: function (promise, handler, errorHandler, progressHandler) {
                var tcs = new System.Threading.Tasks.TaskCompletionSource();

                if (!promise.then) {
                    promise = promise.promise();
                }

                if (typeof (handler) === 'number') {
                    handler = (function (i) {
                        return function () {
                            return arguments[i >= 0 ? i : (arguments.length + i)];
                        };
                    })(handler);
                } else if (typeof (handler) !== 'function') {
                    handler = function () {
                        return Array.prototype.slice.call(arguments, 0);
                    };
                }

                promise.then(function () {
                    tcs.setResult(handler ? handler.apply(null, arguments) : Array.prototype.slice.call(arguments, 0));
                }, function () {
                    tcs.setException(errorHandler ? errorHandler.apply(null, arguments) : new Transpose.PromiseException(Array.prototype.slice.call(arguments, 0)));
                }, progressHandler);

                return tcs.task;
            }
        },

        getException: function () {
            return this.isCanceled() ? null : this.exception;
        },

        waitt: function (timeout, token) {
            var ms = timeout,
                tcs = new System.Threading.Tasks.TaskCompletionSource(),
                cancelCallback = false;

            if (token) {
                token.cancelWasRequested = function () {
                    if (!cancelCallback) {
                        cancelCallback = true;
                        clearTimeout(clear);
                        tcs.setException(new System.OperationCanceledException.$ctor1(token));
                    }
                };
            }

            if (Transpose.is(timeout, System.TimeSpan)) {
                ms = timeout.getTotalMilliseconds();
            }

            var clear = setTimeout(function () {
                cancelCallback = true;
                tcs.setResult(false);
            }, ms);

            this.continueWith(function () {
                clearTimeout(clear);
                if (!cancelCallback) {
                    cancelCallback = true;
                    tcs.setResult(true);
                }
            })

            return tcs.task;
        },

        wait: function (token) {
            var me = this,
                tcs = new System.Threading.Tasks.TaskCompletionSource(),
                complete = false;

            if (token) {
                token.cancelWasRequested = function () {
                    if (!complete) {
                        complete = true;
                        tcs.setException(new System.OperationCanceledException.$ctor1(token));
                    }
                };
            }

            this.continueWith(function () {
                if (!complete) {
                    complete = true;
                    if (me.isFaulted() || me.isCanceled()) {
                        tcs.setException(me.exception);
                    } else {
                        tcs.setResult();
                    }
                }
            })

            return tcs.task;
        },

        c: function (continuationAction) {
            if (this.isCompleted()) {
                System.Threading.Tasks.Task.queue.push(continuationAction);
                System.Threading.Tasks.Task.runQueue();
            } else {
                this.callbacks.push(continuationAction);
            }
        },

        continue: function (continuationAction) {
            if (this.isCompleted()) {
                System.Threading.Tasks.Task.queue.push(continuationAction);
                System.Threading.Tasks.Task.runQueue();
            } else {
                this.callbacks.push(continuationAction);
            }
        },

        continueWith: function (continuationAction, raise) {
            var tcs = new System.Threading.Tasks.TaskCompletionSource(),
                me = this,
                fn = raise ? function () {
                    tcs.setResult(continuationAction(me));
                } : function () {
                    try {
                        tcs.setResult(continuationAction(me));
                    } catch (e) {
                        tcs.setException(System.Exception.create(e));
                    }
                };

            if (this.isCompleted()) {
                //System.Threading.Tasks.Task.schedule(fn);
                System.Threading.Tasks.Task.queue.push(fn);
                System.Threading.Tasks.Task.runQueue();
            } else {
                this.callbacks.push(fn);
            }

            return tcs.task;
        },

        start: function () {
            if (this.status !== System.Threading.Tasks.TaskStatus.created) {
                throw new System.InvalidOperationException.$ctor1("Task was already started.");
            }

            var me = this;

            this.status = System.Threading.Tasks.TaskStatus.running;

            System.Threading.Tasks.Task.schedule(function () {
                try {
                    var result = me.action(me.state);

                    delete me.action;
                    delete me.state;

                    me.complete(result);
                } catch (e) {
                    me.fail(new System.AggregateException(null, [System.Exception.create(e)]));
                }
            });
        },

        runCallbacks: function () {
            var me = this;

            for (var i = 0; i < me.callbacks.length; i++) {
                me.callbacks[i](me);
            }

            delete me.callbacks;
        },

        complete: function (result) {
            if (this.isCompleted()) {
                return false;
            }

            this.result = result;
            this.status = System.Threading.Tasks.TaskStatus.ranToCompletion;
            this.runCallbacks();

            return true;
        },

        fail: function (error) {
            if (this.isCompleted()) {
                return false;
            }

            this.exception = error;
            this.status = this.exception.hasTaskCanceledException && this.exception.hasTaskCanceledException() ? System.Threading.Tasks.TaskStatus.canceled : System.Threading.Tasks.TaskStatus.faulted;
            this.runCallbacks();

            return true;
        },

        cancel: function (error) {
            if (this.isCompleted()) {
                return false;
            }

            this.exception = error || new System.AggregateException(null, [new System.Threading.Tasks.TaskCanceledException.$ctor3(this)]);
            this.status = System.Threading.Tasks.TaskStatus.canceled;
            this.runCallbacks();

            return true;
        },

        isCanceled: function () {
            return this.status === System.Threading.Tasks.TaskStatus.canceled;
        },

        isCompleted: function () {
            return this.status === System.Threading.Tasks.TaskStatus.ranToCompletion || this.status === System.Threading.Tasks.TaskStatus.canceled || this.status === System.Threading.Tasks.TaskStatus.faulted;
        },

        isC: function () {
            return this.status === System.Threading.Tasks.TaskStatus.ranToCompletion || this.status === System.Threading.Tasks.TaskStatus.canceled || this.status === System.Threading.Tasks.TaskStatus.faulted;
        },

        isFaulted: function () {
            return this.status === System.Threading.Tasks.TaskStatus.faulted;
        },

        _getResult: function (awaiting) {
            switch (this.status) {
                case System.Threading.Tasks.TaskStatus.ranToCompletion:
                    return this.result;
                case System.Threading.Tasks.TaskStatus.canceled:
                    if (this.exception && this.exception.innerExceptions) {
                        throw awaiting ? (this.exception.innerExceptions.Count > 0 ? this.exception.innerExceptions.getItem(0) : null) : this.exception;
                    }

                    var ex = new System.Threading.Tasks.TaskCanceledException.$ctor3(this);
                    throw awaiting ? ex : new System.AggregateException(null, [ex]);
                case System.Threading.Tasks.TaskStatus.faulted:
                    // A task can be faulted with a bare exception rather than an AggregateException
                    // (Task.FromException does exactly that), and an AggregateException can carry no
                    // inner exception at all. Rethrow whatever there is: reading `.innerExceptions`
                    // blindly raised a TypeError over the real fault, and the `null` this used to
                    // fall back to discarded it outright.
                    if (!awaiting) {
                        throw this.exception;
                    }

                    if (this.exception && this.exception.innerExceptions && this.exception.innerExceptions.Count > 0) {
                        throw this.exception.innerExceptions.getItem(0);
                    }

                    throw this.exception ? this.exception : new System.Exception("A task failed without reporting an exception.");
                default:
                    throw new System.InvalidOperationException.$ctor1("Task is not yet completed.");
            }
        },

        // Task.Wait(). JavaScript cannot block, so a wait on a task that has NOT completed can only
        // answer "still running" - but a wait on one that HAS must do what .NET's Wait does and
        // observe it, throwing the AggregateException for a fault or a cancellation. Emitted as a
        // bare `wait()` whose returned Task nobody ever looked at, Wait() discarded that exception
        // outright: the single thing it exists for. The bool is Wait(timeout)'s "did it complete",
        // and answering false for a pending task is right for the same reason - no time can pass
        // while a single-threaded runtime is inside this call.
        waitSync: function () {
            if (!this.isCompleted()) {
                return false;
            }

            this._getResult(false);

            return true;
        },

        getResult: function () {
            return this._getResult(false);
        },

        dispose: function () {},

        getAwaiter: function () {
            return this;
        },

        getAwaitedResult: function () {
            return this._getResult(true);
        },

        gAR: function () {
            return this._getResult(true);
        }

    });

    Transpose.define("System.Threading.Tasks.Task$1", function (T) {
        return {
            inherits: [System.Threading.Tasks.Task],
            ctor: function (action, state) {
                this.$initialize();
                System.Threading.Tasks.Task.ctor.call(this, action, state);
            }
        };
    });

    Transpose.define("System.Threading.Tasks.TaskStatus", {
        $kind: "enum",
        $statics: {
            created: 0,
            waitingForActivation: 1,
            waitingToRun: 2,
            running: 3,
            waitingForChildrenToComplete: 4,
            ranToCompletion: 5,
            canceled: 6,
            faulted: 7
        }
    });

    Transpose.define("System.Threading.Tasks.TaskCompletionSource", {
        ctor: function (state) {
            this.$initialize();
            this.task = new System.Threading.Tasks.Task(null, state);
            this.task.status = System.Threading.Tasks.TaskStatus.running;
        },

        setCanceled: function () {
            if (!this.task.cancel()) {
                throw new System.InvalidOperationException.$ctor1("Task was already completed.");
            }
        },

        sR: function (result) {
            if (!this.task.complete(result)) {
                throw new System.InvalidOperationException.$ctor1("Task was already completed.");
            }
        },

        setResult: function (result) {
            if (!this.task.complete(result)) {
                throw new System.InvalidOperationException.$ctor1("Task was already completed.");
            }
        },

        setException: function (exception) {
            if (!this.trySetException(exception)) {
                throw new System.InvalidOperationException.$ctor1("Task was already completed.");
            }
        },

        sE: function (exception) {
            if (!this.trySetException(exception)) {
                throw new System.InvalidOperationException.$ctor1("Task was already completed.");
            }
        },

        trySetCanceled: function () {
            return this.task.cancel();
        },

        trySetResult: function (result) {
            return this.task.complete(result);
        },

        trySetException: function (exception) {
            if (Transpose.is(exception, System.Exception)) {
                exception = [exception];
            } else if (Array.isArray(exception)) {
                exception = exception.map(function (item) { return System.Exception.create(item); });
            } else if (exception === null || exception === undefined || typeof exception.getEnumerator !== "function") {
                // A value that crossed from JavaScript - a rejected promise's reason, a raw browser
                // Error bound by `catch (Exception)` - is not a System.Exception, and constructing
                // the AggregateException below out of it threw "Cannot create Enumerator." from
                // inside the setter: the task then stayed un-completed forever (so every awaiter
                // hung) and the error itself was gone. Wrap whatever arrived instead.
                exception = [System.Exception.create(exception)];
            }

            exception = new System.AggregateException(null, exception);

            if (exception.hasTaskCanceledException()) {
                return this.task.cancel(exception);
            }

            return this.task.fail(exception);
        }
    });

    Transpose.define("System.Threading.CancellationTokenSource", {
        inherits: [System.IDisposable],

        config: {
            alias: [
                "dispose", "System$IDisposable$Dispose"
            ]
        },

        ctor: function (delay) {
            this.$initialize();
            this.timeout = typeof delay === "number" && delay >= 0 ? setTimeout(Transpose.fn.bind(this, this.cancel), delay, -1) : null;
            this.isCancellationRequested = false;
            this.token = new System.Threading.CancellationToken(this);
            this.handlers = [];
        },

        cancel: function (throwFirst) {
            if (this.isCancellationRequested) {
                return;
            }

            this.isCancellationRequested = true;

            var x = [],
                h = this.handlers;

            this.clean();
            this.token.cancelWasRequested();

            for (var i = 0; i < h.length; i++) {
                try {
                    h[i].f(h[i].s);
                } catch (ex) {
                    if (throwFirst && throwFirst !== -1) {
                        throw ex;
                    }

                    x.push(ex);
                }
            }

            if (x.length > 0 && throwFirst !== -1) {
                throw new System.AggregateException(null, x);
            }
        },

        cancelAfter: function (delay) {
            if (this.isCancellationRequested) {
                return;
            }

            if (this.timeout) {
                clearTimeout(this.timeout);
            }

            this.timeout = setTimeout(Transpose.fn.bind(this, this.cancel), delay, -1);
        },

        register: function (f, s) {
            if (this.isCancellationRequested) {
                f(s);

                return new System.Threading.CancellationTokenRegistration();
            } else {
                var o = {
                    f: f,
                    s: s
                };

                this.handlers.push(o);

                return new System.Threading.CancellationTokenRegistration(this, o);
            }
        },

        deregister: function (o) {
            var ix = this.handlers.indexOf(o);

            if (ix >= 0) {
                this.handlers.splice(ix, 1);
            }
        },

        dispose: function () {
            this.clean();
        },

        clean: function () {
            if (this.timeout) {
                clearTimeout(this.timeout);
            }

            this.timeout = null;
            this.handlers = [];

            if (this.links) {
                for (var i = 0; i < this.links.length; i++) {
                    this.links[i].dispose();
                }

                this.links = null;
            }
        },

        statics: {
            createLinked: function () {
                var cts = new System.Threading.CancellationTokenSource();

                cts.links = [];

                var d = Transpose.fn.bind(cts, cts.cancel);

                for (var i = 0; i < arguments.length; i++) {
                    cts.links.push(arguments[i].register(d));
                }

                return cts;
            }
        }
    });

    Transpose.define("System.Threading.CancellationToken", {
         $kind: "struct",

        ctor: function (source) {
            this.$initialize();

            if (!Transpose.is(source, System.Threading.CancellationTokenSource)) {
                source = source ? System.Threading.CancellationToken.sourceTrue : System.Threading.CancellationToken.sourceFalse;
            }

            this.source = source;
        },

        cancelWasRequested: function () {

        },

        getCanBeCanceled: function () {
            return !this.source.uncancellable;
        },

        getIsCancellationRequested: function () {
            return this.source.isCancellationRequested;
        },

        throwIfCancellationRequested: function () {
            if (this.source.isCancellationRequested) {
                throw new System.OperationCanceledException.$ctor1(this);
            }
        },

        register: function (cb, s) {
            return this.source.register(cb, s);
        },

        getHashCode: function () {
            return Transpose.getHashCode(this.source);
        },

        equals: function (other) {
            return other.source === this.source;
        },

        equalsT: function (other) {
            return other.source === this.source;
        },

        statics: {
            sourceTrue: {
                isCancellationRequested: true,
                register: function (f, s) {
                    f(s);

                    return new System.Threading.CancellationTokenRegistration();
                }
            },
            sourceFalse: {
                uncancellable: true,
                isCancellationRequested: false,
                register: function () {
                    return new System.Threading.CancellationTokenRegistration();
                }
            },
            getDefaultValue: function () {
                return new System.Threading.CancellationToken();
            }
        }
    });

    System.Threading.CancellationToken.none = new System.Threading.CancellationToken();

    Transpose.define("System.Threading.CancellationTokenRegistration", {
        inherits: function () {
            return [System.IDisposable, System.IEquatable$1(System.Threading.CancellationTokenRegistration)];
        },

        $kind: "struct",

        config: {
            alias: [
                "dispose", "System$IDisposable$Dispose"
            ]
        },

        ctor: function (cts, o) {
            this.$initialize();
            this.cts = cts;
            this.o = o;
        },

        dispose: function () {
            if (this.cts) {
                this.cts.deregister(this.o);
                this.cts = this.o = null;
            }
        },

        equalsT: function (o) {
            return this === o;
        },

        equals: function (o) {
            return this === o;
        },

        statics: {
            getDefaultValue: function () {
                return new System.Threading.CancellationTokenRegistration();
            }
        }
    });

    Transpose.toPromise = function (awaitable) {
        if (!awaitable) {
            return Promise.resolve(awaitable);
        }

        if (awaitable instanceof Promise || typeof awaitable.then === 'function') {
            // A native promise rejects with whatever JavaScript threw - an Error, a string, a DOM
            // event - and `await`ing it binds that raw value to `catch (Exception e)`: GetType()
            // answered "Error", `e is IOException` was false, and GetBaseException()/Data/ToString
            // were simply absent. Normalise it the way every other JS->C# seam already does
            // (Task.Run, TransposeR.fromPromise, ContinueWith), which also keeps the browser's
            // message and frames. A value that is already a System.Exception is returned unchanged,
            // so nothing is double-wrapped - including the exception an awaited C# Task rejected
            // with below.
            return Promise.resolve(awaitable).catch(function (reason) {
                throw System.Exception.create(reason);
            });
        }

        if (Transpose.is(awaitable, System.Threading.Tasks.Task) || (awaitable && typeof awaitable.continueWith === 'function')) {
            return new Promise(function (resolve, reject) {
                awaitable.continueWith(function (t) {
                    if (t.isFaulted()) {
                        var ex = t.exception;
                        if (ex && ex.innerExceptions && ex.innerExceptions.Count > 0) {
                             reject(ex.innerExceptions.getItem(0));
                        } else {
                             reject(ex);
                        }
                    } else if (t.isCanceled()) {
                         reject(new System.Threading.Tasks.TaskCanceledException.$ctor3(t));
                    } else {
                        resolve(t.getAwaitedResult ? t.getAwaitedResult() : t.getResult());
                    }
                });
            });
        }

        if (typeof awaitable.getAwaiter === 'function') {
             var awaiter = awaitable.getAwaiter();
             if (awaiter.isCompleted()) {
                 return Promise.resolve(awaiter.getResult());
             }
             return new Promise(function(resolve, reject) {
                 var onCompleted = awaiter.onCompleted || awaiter.continueWith;
                 if (typeof onCompleted === 'function') {
                     onCompleted.call(awaiter, function() {
                         try {
                             resolve(awaiter.getResult());
                         } catch(e) {
                             reject(e);
                         }
                     });
                 } else {
                     resolve(awaiter);
                 }
             });
        }

        return Promise.resolve(awaitable);
    };
