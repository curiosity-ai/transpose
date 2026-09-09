    Transpose.define("System.AggregateException", {
        inherits: [System.Exception],

        ctor: function (message, innerExceptions) {
            this.$initialize();
            this.innerExceptions = new(System.Collections.ObjectModel.ReadOnlyCollection$1(System.Exception))(Transpose.hasValue(innerExceptions) ? Transpose.toArray(innerExceptions) : []);

            // The text this was constructed with, before the inner messages are appended - .NET's
            // `base.Message`, which is what Flatten() carries over to the flattened aggregate.
            this.baseMessage = message ? message : "One or more errors occurred.";

            System.Exception.ctor.call(this, System.AggregateException.composeMessage(this.baseMessage, this.innerExceptions), this.innerExceptions.Count > 0 ? this.innerExceptions.getItem(0) : null);
        },

        handle: function (predicate) {
            if (!Transpose.hasValue(predicate)) {
                throw new System.ArgumentNullException.$ctor1("predicate");
            }

            var count = this.innerExceptions.Count,
                unhandledExceptions = [];

            for (var i = 0; i < count; i++) {
                // getItem, not get: the ReadOnlyCollection indexer is emitted as getItem, so this
                // threw "this.innerExceptions.get is not a function" for every call to Handle.
                var inner = this.innerExceptions.getItem(i);

                if (!predicate(inner)) {
                    unhandledExceptions.push(inner);
                }
            }

            if (unhandledExceptions.length > 0) {
                throw new System.AggregateException(this.Message, unhandledExceptions);
            }
        },

        getBaseException: function () {
            var back = this;
            var backAsAggregate = this;

            while (backAsAggregate != null && backAsAggregate.innerExceptions.Count === 1)
            {
                back = back.InnerException;
                backAsAggregate = Transpose.as(back, System.AggregateException);
            }

            return back;
        },

        hasTaskCanceledException: function () {
            for (var i = 0; i < this.innerExceptions.Count; i++) {
                var e = this.innerExceptions.getItem(i);
                if (Transpose.is(e, System.Threading.Tasks.TaskCanceledException) || (Transpose.is(e, System.AggregateException) && e.hasTaskCanceledException())) {
                    return true;
                }
            }
            return false;
        },

        flatten: function () {
            // Initialize a collection to contain the flattened exceptions.
            var flattenedExceptions = new(System.Collections.Generic.List$1(System.Exception))();

            // Create a list to remember all aggregates to be flattened, this will be accessed like a FIFO queue
            var exceptionsToFlatten = new(System.Collections.Generic.List$1(System.AggregateException))();
            exceptionsToFlatten.add(this);
            var nDequeueIndex = 0;

            // Continue removing and recursively flattening exceptions, until there are no more.
            while (exceptionsToFlatten.Count > nDequeueIndex) {
                // dequeue one from exceptionsToFlatten
                var currentInnerExceptions = exceptionsToFlatten.getItem(nDequeueIndex++).innerExceptions,
                    count = currentInnerExceptions.Count;

                for (var i = 0; i < count; i++) {
                    var currentInnerException = currentInnerExceptions.getItem(i);

                    if (!Transpose.hasValue(currentInnerException)) {
                        continue;
                    }

                    var currentInnerAsAggregate = Transpose.as(currentInnerException, System.AggregateException);

                    // If this exception is an aggregate, keep it around for later.  Otherwise,
                    // simply add it to the list of flattened exceptions to be returned.
                    if (Transpose.hasValue(currentInnerAsAggregate)) {
                        exceptionsToFlatten.add(currentInnerAsAggregate);
                    } else {
                        flattenedExceptions.add(currentInnerException);
                    }
                }
            }

            // base.Message, not Message: composing again over the flattened list would repeat
            // every inner message the composed text already names.
            return new System.AggregateException(this.baseMessage, flattenedExceptions);
        },

        toString: function () {
            // base.ToString() reports the type, the composed message and the FIRST inner exception
            // (which is InnerException). Every other inner exception is unreachable from it, so
            // .NET lists them afterwards - and they are exactly what the reader is looking for
            // when several tasks failed at once.
            var text = System.Exception.prototype.toString.call(this);
            var count = this.innerExceptions.Count;

            for (var i = 0; i < count; i++) {
                var inner = this.innerExceptions.getItem(i);

                if (inner === this.InnerException) {
                    continue;
                }

                text += "\n---> (Inner Exception #" + i + ") " + Transpose.toString(inner) + "<---\n";
            }

            return text;
        },

        statics: {
            // "One or more errors occurred. (first) (second)" - the message .NET reports, which is
            // the base text followed by every inner message in parentheses. Composed once, in the
            // constructor, because generated code reads Message as the `message` FIELD (through
            // TransposeR.message) rather than through a property getter it could override.
            composeMessage: function (baseMessage, innerExceptions) {
                var count = innerExceptions ? innerExceptions.Count : 0;

                if (count === 0) {
                    return baseMessage;
                }

                var text = baseMessage;

                for (var i = 0; i < count; i++) {
                    text += " (" + System.Exception.describe(innerExceptions.getItem(i)) + ")";
                }

                return text;
            }
        }
    });

