Transpose.define("System.ValueType", {
    statics: {
        methods: {
            $is: function (obj) {
                return Transpose.Reflection.isValueType(Transpose.getType(obj));
            }
        }
    }
});
