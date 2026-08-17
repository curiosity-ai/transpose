    var enumMethods = {
        nameEquals: function (n1, n2, ignoreCase) {
            if (ignoreCase) {
                return n1.toLowerCase() === n2.toLowerCase();
            }

            return (n1.charAt(0).toLowerCase() + n1.slice(1)) === (n2.charAt(0).toLowerCase() + n2.slice(1));
        },

        checkEnumType: function (enumType) {
            if (!enumType) {
                throw new System.ArgumentNullException.$ctor1("enumType");
            }

            if (enumType.prototype && enumType.$kind !== "enum") {
                throw new System.ArgumentException.$ctor1("", "enumType");
            }
        },

        getUnderlyingType: function (type) {
            System.Enum.checkEnumType(type);

            return type.prototype.$utype || System.Int32;
        },

        toName: function (name) {
            return name;
        },

        // The declared names of an enum, without the defensive copy getNames() hands to callers.
        // getNames() is on the hot path of toString()/getName(), and copying $names there means
        // every single conversion allocates an array as long as the enum.
        namesOf: function (enumType) {
            return enumType.$names || System.Enum.getNames(enumType);
        },

        // value -> name, built once per enum type and cached on the type object. An enum's members
        // are fixed when Transpose.define() runs, so the map can never go stale. Without it every
        // ToString() walks the whole name list looking for its value, which is O(n) in the size of
        // the *enum* — invisible on a hand-written enum, but generated ones (icon sets, error code
        // tables, unicode categories) run to thousands of members and pay it on every conversion.
        //
        // Returns null for a 64-bit enum: those values are Long objects, so they are compared with
        // .eq() rather than by identity and cannot key a Map. Callers fall back to the scan.
        valueNames: function (enumType) {
            if (enumType.$valueNames !== undefined) {
                return enumType.$valueNames;
            }

            var names = enumMethods.namesOf(enumType),
                map = new Map();

            for (var i = 0; i < names.length; i++) {
                var value = enumType[names[i]];

                if (System.Int64.is64Bit(value)) {
                    map = null;
                    break;
                }

                // First declaration wins, matching the order the linear scan resolved aliases in.
                if (!map.has(value)) {
                    map.set(value, names[i]);
                }
            }

            enumType.$valueNames = map;

            return map;
        },

        toObject: function (enumType, value) {
            value = Transpose.unbox(value, true);

            if (value == null) {
                return null;
            }

            return enumMethods.parse(enumType, value.toString(), false, true);
        },

        parse: function (enumType, s, ignoreCase, silent) {
            System.Enum.checkEnumType(enumType);

            if (s != null) {
                if (enumType === Number || enumType === System.String || enumType.$number) {
                    return s;
                }

                var intValue = {};

                if (System.Int32.tryParse(s, intValue)) {
                    return Transpose.box(intValue.v, enumType, function (obj) { return System.Enum.toString(enumType, obj); });
                }

                var names = enumMethods.namesOf(enumType),
                    values = enumType;

                if (!enumType.prototype || !enumType.prototype.$flags) {
                    for (var i = 0; i < names.length; i++) {
                        var name = names[i];

                        if (enumMethods.nameEquals(name, s, ignoreCase)) {
                            return Transpose.box(values[name], enumType, function (obj) { return System.Enum.toString(enumType, obj); });
                        }
                    }
                } else {
                    var parts = s.split(","),
                        value = 0,
                        parsed = true;

                    for (var i = parts.length - 1; i >= 0; i--) {
                        var part = parts[i].trim(),
                            found = false;

                        for (var n = 0; n < names.length; n++) {
                            var name = names[n];

                            if (enumMethods.nameEquals(name, part, ignoreCase)) {
                                value |= values[name];
                                found = true;

                                break;
                            }
                        }

                        if (!found) {
                            parsed = false;

                            break;
                        }
                    }

                    if (parsed) {
                        return Transpose.box(value, enumType, function (obj) { return System.Enum.toString(enumType, obj); });
                    }
                }
            }

            if (silent !== true) {
                throw new System.ArgumentException.$ctor3("silent", "Invalid Enumeration Value");
            }

            return null;
        },

        toStringFn: function (type) {
            return function (value) {
                return System.Enum.toString(type, value);
            };
        },

        toString: function (enumType, value, forceFlags) {
            if (arguments.length === 0) {
                return "System.Enum";
            }

            if (value && value.$boxed && enumType === System.Enum) {
                enumType = value.type;
            }

            value = Transpose.unbox(value, true);

            if (enumType === Number || enumType === System.String || enumType.$number) {
                return value.toString();
            }

            System.Enum.checkEnumType(enumType);

            var values = enumType,
                isLong = System.Int64.is64Bit(value);

            if (((!enumType.prototype || !enumType.prototype.$flags) && forceFlags !== true) || (value === 0)) {
                var map = enumMethods.valueNames(enumType);

                if (map !== null) {
                    // A Long value against a non-Long enum matches nothing, exactly as the scan below
                    // would conclude, and Map lookup agrees with === on every value an enum can hold.
                    var mapped = map.get(value);

                    return mapped !== undefined ? enumMethods.toName(mapped) : value.toString();
                }

                var names = enumMethods.namesOf(enumType);

                for (var i = 0; i < names.length; i++) {
                    var name = names[i];

                    if (isLong && System.Int64.is64Bit(values[name]) ? (values[name].eq(value)) : (values[name] === value)) {
                        return enumMethods.toName(name);
                    }
                }

                return value.toString();
            } else {
                var parts = [],
                    entries = System.Enum.getValuesAndNames(enumType),
                    index = entries.length - 1,
                    saveResult = value;

                while (index >= 0) {
                    var entry = entries[index],
                        long = isLong && System.Int64.is64Bit(entry.value);

                    if ((index == 0) && (long ? entry.value.isZero() : entry.value == 0)) {
                        break;
                    }

                    if (long ? (value.and(entry.value).eq(entry.value)) : ((value & entry.value) == entry.value)) {
                        if (long) {
                            value = value.sub(entry.value);
                        } else {
                            value -= entry.value;
                        }

                        parts.unshift(entry.name);
                    }

                    index--;
                }

                if (isLong ? !value.isZero() : value !== 0) {
                    return saveResult.toString();
                }

                if (isLong ? saveResult.isZero() : saveResult === 0) {
                    var entry = entries[0];

                    if (entry && (System.Int64.is64Bit(entry.value) ? entry.value.isZero() : (entry.value == 0))) {
                        return entry.name;
                    }

                    return "0";
                }

                return parts.join(", ");
            }
        },

        getValuesAndNames: function (enumType) {
            System.Enum.checkEnumType(enumType);

            var parts = [],
                names = enumMethods.namesOf(enumType),
                values = enumType;

            for (var i = 0; i < names.length; i++) {
                parts.push({ name: names[i], value: values[names[i]] });
            }

            return parts.sort(function (i1, i2) {
                return System.Int64.is64Bit(i1.value) ? i1.value.sub(i2.value).sign() : (i1.value - i2.value);
            });
        },

        getValues: function (enumType) {
            System.Enum.checkEnumType(enumType);

            var parts = [],
                names = enumMethods.namesOf(enumType),
                values = enumType;

            for (var i = 0; i < names.length; i++) {
                parts.push(values[names[i]]);
            }

            return parts.sort(function (i1, i2) {
                return System.Int64.is64Bit(i1) ? i1.sub(i2).sign() : (i1 - i2);
            });
        },

        format: function (enumType, value, format) {
            System.Enum.checkEnumType(enumType);

            var name;

            if (!Transpose.hasValue(value) && (name = "value") || !Transpose.hasValue(format) && (name = "format")) {
                throw new System.ArgumentNullException.$ctor1(name);
            }

            value = Transpose.unbox(value, true);

            switch (format) {
                case "G":
                case "g":
                    return System.Enum.toString(enumType, value);
                case "x":
                case "X":
                    return value.toString(16);
                case "d":
                case "D":
                    return value.toString();
                case "f":
                case "F":
                    return System.Enum.toString(enumType, value, true);
                default:
                    throw new System.FormatException();
            }
        },

        getNames: function (enumType) {
            System.Enum.checkEnumType(enumType);

            var parts = [],
                values = enumType;

            if (enumType.$names) {
                return enumType.$names.slice(0);
            }

            for (var i in values) {
                if (values.hasOwnProperty(i) && i.indexOf("$") < 0 && typeof values[i] !== "function") {
                    parts.push([enumMethods.toName(i), values[i]]);
                }
            }

            return parts.sort(function (i1, i2) {
                return System.Int64.is64Bit(i1[1]) ? i1[1].sub(i2[1]).sign() : (i1[1] - i2[1]);
            }).map(function (i) {
                return i[0];
            });
        },

        getName: function (enumType, value) {
            value = Transpose.unbox(value, true);

            if (value == null) {
                throw new System.ArgumentNullException.$ctor1("value");
            }

            var isLong = System.Int64.is64Bit(value);

            if (!isLong && !(typeof (value) === "number" && Math.floor(value, 0) === value)) {
                throw new System.ArgumentException.$ctor1("Argument must be integer", "value");
            }

            System.Enum.checkEnumType(enumType);

            // Only for a plain numeric value: this scan compares with value.eq(...) whenever the
            // *value* is a Long, which coerces the enum's number members, and Map lookup would not.
            if (!isLong) {
                var map = enumMethods.valueNames(enumType);

                if (map !== null) {
                    var mapped = map.get(value);

                    return mapped !== undefined ? mapped : null;
                }
            }

            var names = enumMethods.namesOf(enumType),
                values = enumType;

            for (var i = 0; i < names.length; i++) {
                var name = names[i];

                if (isLong ? value.eq(values[name]) : (values[name] === value)) {
                    return name;
                }
            }

            return null;
        },

        hasFlag: function (value, flag) {
            flag = Transpose.unbox(flag, true);
            var isLong = System.Int64.is64Bit(value);

            return flag === 0 || (isLong ? !value.and(flag).isZero() : !!(value & flag));
        },

        isDefined: function (enumType, value) {
            value = Transpose.unbox(value, true);

            System.Enum.checkEnumType(enumType);

            var values = enumType,
                names = enumMethods.namesOf(enumType),
                isString = Transpose.isString(value),
                isLong = System.Int64.is64Bit(value);

            for (var i = 0; i < names.length; i++) {
                var name = names[i];

                if (isString ? enumMethods.nameEquals(name, value, false) : (isLong ? value.eq(values[name]) : (values[name] === value))) {
                    return true;
                }
            }

            return false;
        },

        tryParse: function (enumType, value, result, ignoreCase) {
            result.v = Transpose.unbox(enumMethods.parse(enumType, value, ignoreCase, true), true);

            if (result.v == null) {
                result.v = 0;

                return false;
            }

            return true;
        },

        equals: function (v1, v2, T) {
            if (v2 && v2.$boxed && (v1 && v1.$boxed || T)) {
                if (v2.type !== (v1.type || T)) {
                    return false;
                }
            }

            return System.Enum.equalsT(v1, v2);
        },

        equalsT: function (v1, v2) {
            return Transpose.equals(Transpose.unbox(v1, true), Transpose.unbox(v2, true));
        }
    };

    Transpose.define("System.Enum", {
        inherits: [System.IComparable, System.IFormattable],
        statics: {
            methods: enumMethods
        }
    });
