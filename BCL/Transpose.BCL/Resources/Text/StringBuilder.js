    Transpose.define("System.Text.StringBuilder", {
        ctor: function () {
            this.$initialize();
            this.buffer = [],
            this.capacity = 16;
            // The length of what the buffer holds, kept as pieces are added and taken out so that reading
            // Length never has to join them: a parser appends and asks for the length on every step.
            this.$len = 0;

            if (arguments.length === 1) {
                this.append(arguments[0]);
            } else if (arguments.length === 2) {
                this.append(arguments[0]);
                this.setCapacity(arguments[1]);
            } else if (arguments.length >= 3) {
                this.append(arguments[0], arguments[1], arguments[2]);
                if (arguments.length === 4) {
                    this.setCapacity(arguments[3]);
                }
            }
        },

        getLength: function () {
            return this.$len;
        },

        setLength: function (value) {
            if (value === 0) {
                this.clear();
            } else if (value < 0) {
                throw new System.ArgumentOutOfRangeException.$ctor4("value", "Length cannot be less than zero");
            } else {
                var l = this.getLength();

                if (value === l) {
                    return;
                }

                var delta = value - l;

                if (delta > 0) {
                    this.append("\0", delta);
                } else {
                    this.remove(l + delta, -delta);
                }
            }
        },

        getCapacity: function () {
            var length = this.getLength();

            return (this.capacity > length) ? this.capacity : length;
        },

        setCapacity: function (value) {
            var length = this.getLength();

            if (value > length) {
                this.capacity = value;
            }
        },

        toString: function () {
            var s = this.getString();

            if (arguments.length === 2) {
                var startIndex = arguments[0],
                    length = arguments[1];

                this.checkLimits(s, startIndex, length);

                return s.substr(startIndex, length);
            }

            return s;
        },

        append: function (value) {
            if (value == null) {
                return this;
            }

            if (arguments.length === 2) {
                // append a char repeated count times
                var count = arguments[1];

                if (count === 0) {
                    return this;
                } else if (count < 0) {
                    throw new System.ArgumentOutOfRangeException.$ctor4("count", "cannot be less than zero");
                }

                value = Array(count + 1).join(value).toString();
            } else if (arguments.length === 3) {
                // append a (startIndex, count) substring of value
                var startIndex = arguments[1],
                    count = arguments[2];

                if (count === 0) {
                    return this;
                }

                this.checkLimits(value, startIndex, count);
                value = value.substr(startIndex, count);
            }

            if (typeof value !== "string") {
                value = String(value);
            }

            this.buffer[this.buffer.length] = value;
            this.$len += value.length;
            this.clearString();

            return this;
        },

        appendFormat: function (format) {
            return this.append(System.String.format.apply(System.String, arguments));
        },

        clear: function () {
            this.buffer = [];
            this.$len = 0;
            this.clearString();

            return this;
        },

        appendLine: function () {
            if (arguments.length === 1) {
                this.append(arguments[0]);
            }

            return this.append("\r\n");
        },

        equals: function (sb) {
            if (sb == null) {
                return false;
            }

            if (sb === this) {
                return true;
            }

            return this.toString() === sb.toString();
        },

        remove: function (startIndex, length) {
            var s = this.getString();

            this.checkLimits(s, startIndex, length);

            if (s.length === length && startIndex === 0) {
                // Optimization.  If we are deleting everything
                return this.clear();
            }

            if (length > 0) {
                this.buffer = [];
                this.buffer[0] = s.substring(0, startIndex);
                this.buffer[1] = s.substring(startIndex + length, s.length);
                this.$len -= length;
                this.clearString();
            }

            return this;
        },

        insert: function (index, value) {
            if (value == null) {
                return this;
            }

            if (arguments.length === 3) {
                // insert value repeated count times
                var count = arguments[2];

                if (count === 0) {
                    return this;
                } else if (count < 0) {
                    throw new System.ArgumentOutOfRangeException.$ctor4("count", "cannot be less than zero");
                }

                value = Array(count + 1).join(value).toString();
            }

            if (typeof value !== "string") {
                value = String(value);
            }

            var s = this.getString();

            this.buffer = [];
            this.$len = s.length + value.length;

            if (index < 1) {
                this.buffer[0] = value;
                this.buffer[1] = s;
            } else if (index >= s.length) {
                this.buffer[0] = s;
                this.buffer[1] = value;
            } else {
                this.buffer[0] = s.substring(0, index);
                this.buffer[1] = value;
                this.buffer[2] = s.substring(index, s.length);
            }

            this.clearString();

            return this;
        },

        replace: function (oldValue, newValue) {
            var r = new RegExp(oldValue, "g"),
                s = this.buffer.join("");

            this.buffer = [];

            if (arguments.length === 4) {
                var startIndex = arguments[2],
                    count = arguments[3],
                    b = s.substr(startIndex, count);

                this.checkLimits(s, startIndex, count);

                this.buffer[0] = s.substring(0, startIndex);
                this.buffer[1] = b.replace(r, newValue);
                this.buffer[2] = s.substring(startIndex + count, s.length);
            } else {
                this.buffer[0] = s.replace(r, newValue);
            }

            this.$len = 0;

            for (var i = 0; i < this.buffer.length; i++) {
                this.$len += this.buffer[i].length;
            }

            this.clearString();
            return this;
        },

        checkLimits: function (value, startIndex, length) {
            if (length < 0) {
                throw new System.ArgumentOutOfRangeException.$ctor4("length", "must be non-negative");
            }

            if (startIndex < 0) {
                throw new System.ArgumentOutOfRangeException.$ctor4("startIndex", "startIndex cannot be less than zero");
            }

            if (length > value.length - startIndex) {
                throw new System.ArgumentOutOfRangeException.$ctor4("Index and length must refer to a location within the string");
            }
        },

        clearString: function () {
            this.$str = null;
        },

        getString: function () {
            if (!this.$str) {
                this.$str = this.buffer.join("");
                this.buffer = [];
                this.buffer[0] = this.$str;
            }

            return this.$str;
        },

        getChar: function (index) {
            var str = this.getString();

            if (index < 0 || index >= str.length) {
                throw new System.IndexOutOfRangeException();
            }

            return str.charCodeAt(index);
        },

        setChar: function (index, value) {
            var str = this.getString();

            if (index < 0 || index >= str.length) {
                throw new System.ArgumentOutOfRangeException();
            }

            value = String.fromCharCode(value);
            this.buffer = [];
            this.buffer[0] = str.substring(0, index);
            this.buffer[1] = value;
            this.buffer[2] = str.substring(index + 1, str.length);
            this.clearString();
        }
    });
