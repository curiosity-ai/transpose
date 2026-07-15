    Transpose.define("System.ArraySegment", {
        $kind: "struct",

        statics: {
            getDefaultValue: function () {
                return new System.ArraySegment();
            }
        },

        ctor: function (array, offset, count) {
            this.$initialize();

            if (arguments.length === 0) {
                this.array = null;
                this.offset = 0;
                this.count = 0;

                return;
            }

            if (array == null) {
                throw new System.ArgumentNullException.$ctor1("array");
            }

            this.array = array;

            if (Transpose.isNumber(offset)) {
                if (offset < 0) {
                    throw new System.ArgumentOutOfRangeException.$ctor1("offset");
                }

                this.offset = offset;
            } else {
                this.offset = 0;
            }

            if (Transpose.isNumber(count)) {
                if (count < 0) {
                    throw new System.ArgumentOutOfRangeException.$ctor1("count");
                }

                this.count = count;
            } else {
                this.count = array.length;
            }

            if (array.length - this.offset < this.count) {
                throw new ArgumentException();
            }
        },

        getArray: function () {
            return this.array;
        },

        getCount: function () {
            return this.count;
        },

        getOffset: function () {
            return this.offset;
        },

        getHashCode: function () {
            var h = Transpose.addHash([5322976039, this.array, this.count, this.offset]);

            return h;
        },

        equals: function (o) {
            if (!Transpose.is(o, System.ArraySegment)) {
                return false;
            }

            return Transpose.equals(this.array, o.array) && Transpose.equals(this.count, o.count) && Transpose.equals(this.offset, o.offset);
        },

        $clone: function (to) { return this; }
    });
