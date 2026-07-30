
    Transpose.define("Transpose.Int", {
        inherits: [System.IComparable, System.IFormattable],
        statics: {
            $number: true,

            MAX_SAFE_INTEGER: Number.MAX_SAFE_INTEGER || Math.pow(2, 53) - 1,
            MIN_SAFE_INTEGER: Number.MIN_SAFE_INTEGER || -(Math.pow(2, 53) - 1),

            $is: function (instance) {
                return typeof (instance) === "number" && isFinite(instance) && Math.floor(instance, 0) === instance;
            },

            getDefaultValue: function () {
                return 0;
            },

            /// The number of significant digits in the SHORTEST decimal string that round-trips back to
            /// `value` — what .NET Core 3.0+ uses for `double.ToString()` / `float.ToString()` / the "G"
            /// and "R" formats. (.NET Framework used a fixed 15 for double and 7 for float, which is
            /// what `System.Double.precision` still carries for an explicit "G15".)
            ///
            /// For a double, JS's own `toExponential()` with no argument is defined to use "as many
            /// digits as necessary to uniquely specify the number", i.e. exactly the shortest
            /// round-trippable form. A float has to be probed instead: the value is held in a double, so
            /// the shortest form is the fewest digits that still survive a round-trip through
            /// `Math.fround` (1f/3f prints "0.33333334", not "0.3333333432674408").
            shortestDigits: function (value, isSingle) {
                if (!isFinite(value) || value === 0) {
                    return 1;
                }

                var v = Math.abs(value);

                if (isSingle) {
                    for (var p = 1; p < 9; p++) {
                        if (Math.fround(parseFloat(v.toPrecision(p))) === v) {
                            return p;
                        }
                    }

                    return 9;
                }

                var mantissa = v.toExponential(),
                    dot = mantissa.indexOf("."),
                    exp = mantissa.indexOf("e");

                return dot < 0 ? 1 : exp - dot;
            },

            /// The default `ToString()` (and bare "G"/"R") of a double or float on .NET Core 3.0+: the
            /// SHORTEST decimal string that round-trips back to the value. The digit count comes from
            /// `shortestDigits`; the notation rule is .NET's own — scientific once the value's scale (the
            /// count of digits before the decimal point) exceeds the type's full precision, 17 for a
            /// double and 9 for a float, or once the value drops below 1e-4.
            shortestFormat: function (number, isSingle, nf) {
                var isNeg = number < 0 || (number === 0 && (1 / number) < 0),
                    magnitude = Math.abs(number),
                    digits = this.shortestDigits(magnitude, isSingle),
                    // Rounding the ORIGINAL value to `digits` significant digits reproduces the shortest
                    // form exactly; rounding a separately computed coefficient does not (its own binary
                    // value is a hair below, and 1.2345678901234568E+17 came out …4567E+17).
                    exponential = magnitude.toExponential(digits - 1),
                    eIndex = exponential.indexOf("e"),
                    exponent = parseInt(exponential.substring(eIndex + 1), 10),
                    scale = exponent + 1,
                    text;

                if (scale > (isSingle ? 9 : 17) || scale < -3) {
                    // The mantissa already carries exactly `digits` significant digits, so it is used as
                    // the STRING it is: re-parsing it and re-rendering through defaultFormat would round
                    // the mantissa's own binary value and drop the last digit.
                    text = exponential.substring(0, eIndex).replace(".", nf.numberDecimalSeparator)
                        + "E" + (exponent < 0 ? nf.negativeSign : nf.positiveSign)
                        + this.defaultFormat(Math.abs(exponent), 2, 0, 0, nf, true);
                } else {
                    text = this.defaultFormat(parseFloat(exponential), 1, 0, Math.max(0, Math.min(100, digits - scale)), nf, true);
                }

                return (isNeg ? nf.negativeSign : "") + text;
            },

            format: function (number, format, provider, T, toUnsign) {
                // A float is carried in a JS number (a double), and arithmetic on it is done at double
                // precision, so the value can hold bits a real `float` never had: 1f/3f sits here as
                // 0.3333333333333333 rather than 0.3333333432674408. Rounding to single precision before
                // rendering is what makes float.ToString() agree with .NET ("0.33333334").
                if (T === System.Single && typeof number === "number" && isFinite(number) && Math.fround) {
                    number = Math.fround(number);
                }

                var nf = (provider || System.Globalization.CultureInfo.getCurrentCulture()).getFormat(System.Globalization.NumberFormatInfo),
                    decimalSeparator = nf.numberDecimalSeparator,
                    groupSeparator = nf.numberGroupSeparator,
                    isDecimal = number instanceof System.Decimal,
                    isLong = number instanceof System.Int64 || number instanceof System.UInt64,
                    isNeg = isDecimal || isLong ? (number.isZero() ? false : number.isNegative())
                        : (number < 0 || (number === 0 && (1 / number) < 0)),
                    match,
                    precision,
                    groups,
                    fs;

                if (!isLong && (isDecimal ? !number.isFinite() : !isFinite(number))) {
                    return Number.NEGATIVE_INFINITY === number || (isDecimal && isNeg) ? nf.negativeInfinitySymbol : (isNaN(number) ? nf.nanSymbol : nf.positiveInfinitySymbol);
                }

                if (!format) {
                    format = "G";
                }

                match = format.match(/^([a-zA-Z])(\d*)$/);

                if (match) {
                    fs = match[1].toUpperCase();
                    precision = parseInt(match[2], 10);
                    //precision = precision > 15 ? 15 : precision;

                    // The default ToString of a double/float — and a bare "G" — is the shortest
                    // round-trippable form since .NET Core 3.0, not a fixed 15/7 significant digits. An
                    // explicit "G15"/"G7" still means what it says and falls through below.
                    if (fs === "G" && isNaN(precision) && (T === System.Double || T === System.Single)) {
                        return this.shortestFormat(number, T === System.Single, nf);
                    }

                    switch (fs) {
                        case "D":
                            return this.defaultFormat(number, isNaN(precision) ? 1 : precision, 0, 0, nf, true);
                        case "F":
                        case "N":
                            if (isNaN(precision)) {
                                precision = nf.numberDecimalDigits;
                            }

                            return this.defaultFormat(number, 1, precision, precision, nf, fs === "F");
                        case "G":
                        case "E":
                            var exponent = 0,
                                coefficient = isDecimal || isLong ? (isLong && number.eq(System.Int64.MinValue) ? System.Int64(number.value.toUnsigned()) : number.abs()) : Math.abs(number),
                                exponentPrefix = match[1],
                                exponentPrecision = 3,
                                minDecimals,
                                maxDecimals;

                            if (isDecimal || isLong) {
                                while (coefficient.gte(10)) {
                                    coefficient = coefficient.div(10);
                                    exponent++;
                                }

                                while (coefficient.ne(0) && coefficient.lt(1)) {
                                    coefficient = coefficient.mul(10);
                                    exponent--;
                                }
                            } else if (coefficient !== 0) {
                                // Normalising by repeated *10 / /10 accumulates error at the extremes of
                                // the double range (1e-320 ended up with a coefficient of 10, printing
                                // "10E-321"); toExponential gives the coefficient and exponent exactly.
                                var normalized = coefficient.toExponential(),
                                    eIndex = normalized.indexOf("e");

                                coefficient = parseFloat(normalized.substring(0, eIndex));
                                exponent = parseInt(normalized.substring(eIndex + 1), 10);
                            }

                            if (fs === "G") {
                                var noPrecision = isNaN(precision);

                                // A "G" with no explicit precision is the DEFAULT ToString of a double or
                                // float, and since .NET Core 3.0 that is the shortest round-trippable form
                                // rather than a fixed 15/7 significant digits — so `(7.0/3).ToString()` is
                                // "2.3333333333333335", not "2.33333333333333". An explicit "G15"/"G7"
                                // keeps the requested precision, and decimal/long keep their own widths.
                                if (noPrecision) {
                                    if (isDecimal) {
                                        precision = 29;
                                    } else if (isLong) {
                                        precision = number instanceof System.Int64 ? 19 : 20;
                                    } else if (T && T.precision) {
                                        precision = T.precision;
                                    } else {
                                        precision = 15;
                                    }
                                }

                                if ((exponent > -5 && exponent < precision) || isDecimal && noPrecision) {
                                    minDecimals = 0;
                                    // `precision` counts SIGNIFICANT digits, so the decimals available are
                                    // those left after the integer part. The `exponent > 0 ? … : 1` form
                                    // below is the historical approximation: it is one digit short for a
                                    // value below 1 (exponent < 0), which the shortest form cannot absorb.
                                    maxDecimals = isDecimal
                                        ? precision - (exponent > 0 ? exponent + 1 : 1)
                                        : Math.max(0, Math.min(100, precision - exponent - 1));

                                    return this.defaultFormat(number, 1, isDecimal ? Math.min(27, Math.max(minDecimals, number.$precision)) : minDecimals, maxDecimals, nf, true);
                                }

                                exponentPrefix = exponentPrefix === "G" ? "E" : "e";
                                exponentPrecision = 2;
                                minDecimals = 0;
                                maxDecimals = (precision || 15) - 1;
                            } else {
                                minDecimals = maxDecimals = isNaN(precision) ? 6 : precision;
                            }

                            if (exponent >= 0) {
                                exponentPrefix += nf.positiveSign;
                            } else {
                                exponentPrefix += nf.negativeSign;
                                exponent = -exponent;
                            }

                            if (isNeg) {
                                if (isDecimal || isLong) {
                                    coefficient = coefficient.mul(-1);
                                } else {
                                    coefficient *= -1;
                                }
                            }

                            return this.defaultFormat(coefficient, 1, isDecimal ? Math.min(27, Math.max(minDecimals, number.$precision)) : minDecimals, maxDecimals, nf) + exponentPrefix + this.defaultFormat(exponent, exponentPrecision, 0, 0, nf, true);
                        case "P":
                            if (isNaN(precision)) {
                                precision = nf.percentDecimalDigits;
                            }

                            return this.defaultFormat(number * 100, 1, precision, precision, nf, false, "percent");
                        case "X":
                            var result;

                            if (isDecimal) {
                                result = number.round().value.toHex().substr(2);
                            } else if (isLong) {
                                var uvalue = toUnsign ? toUnsign(number) : number;
                                result = uvalue.toString(16);
                            } else {
                                var uvalue = toUnsign ? toUnsign(Math.round(number)) : Math.round(number) >>> 0;
                                result = uvalue.toString(16);
                            }

                            if (match[1] === "X") {
                                result = result.toUpperCase();
                            }

                            precision -= result.length;

                            while (precision-- > 0) {
                                result = "0" + result;
                            }

                            return result;
                        case "C":
                            if (isNaN(precision)) {
                                precision = nf.currencyDecimalDigits;
                            }

                            return this.defaultFormat(number, 1, precision, precision, nf, false, "currency");
                        case "R":
                            var r_result = isDecimal || isLong ? (number.toString()) : ("" + number);

                            if (decimalSeparator !== ".") {
                                r_result = r_result.replace(".", decimalSeparator);
                            }

                            r_result = r_result.replace("e", "E");

                            return r_result;
                    }
                }

                if (format.indexOf(",.") !== -1 || System.String.endsWith(format, ",")) {
                    var count = 0,
                        index = format.indexOf(",.");

                    if (index === -1) {
                        index = format.length - 1;
                    }

                    while (index > -1 && format.charAt(index) === ",") {
                        count++;
                        index--;
                    }

                    if (isDecimal || isLong) {
                        number = number.div(Math.pow(1000, count));
                    } else {
                        number /= Math.pow(1000, count);
                    }
                }

                if (format.indexOf("%") !== -1) {
                    if (isDecimal || isLong) {
                        number = number.mul(100);
                    } else {
                        number *= 100;
                    }
                }

                if (format.indexOf("‰") !== -1) {
                    if (isDecimal || isLong) {
                        number = number.mul(1000);
                    } else {
                        number *= 1000;
                    }
                }

                groups = format.split(";");

                if ((isDecimal || isLong ? number.lt(0) : (number < 0)) && groups.length > 1) {
                    if (isDecimal || isLong) {
                        number = number.mul(-1);
                    } else {
                        number *= -1;
                    }

                    format = groups[1];
                } else {
                    format = groups[(isDecimal || isLong ? number.ne(0) : !number) && groups.length > 2 ? 2 : 0];
                }

                return this.customFormat(number, format, nf, !format.match(/^[^\.]*[0#],[0#]/));
            },

            defaultFormat: function (number, minIntLen, minDecLen, maxDecLen, provider, noGroup, name) {
                name = name || "number";

                var nf = (provider || System.Globalization.CultureInfo.getCurrentCulture()).getFormat(System.Globalization.NumberFormatInfo),
                    str,
                    decimalIndex,
                    pattern,
                    roundingFactor,
                    groupIndex,
                    groupSize,
                    groups = nf[name + "GroupSizes"],
                    decimalPart,
                    index,
                    done,
                    startIndex,
                    length,
                    part,
                    sep,
                    buffer = "",
                    isDecimal = number instanceof System.Decimal,
                    isLong = number instanceof System.Int64 || number instanceof System.UInt64,
                    isNeg = isDecimal || isLong ? (number.isZero() ? false : number.isNegative()) : number < 0,
                    isZero = false;

                roundingFactor = Math.pow(10, maxDecLen);

                if (isDecimal) {
                    str = number.abs().toDecimalPlaces(maxDecLen).toFixed();
                } else if (isLong) {
                    str = number.eq(System.Int64.MinValue) ? number.value.toUnsigned().toString() : number.abs().toString();
                } else {
                    str = Math.abs(number).toFixed(maxDecLen);

                    if (str.indexOf("e") >= 0) {
                        // toFixed falls back to exponential notation at 1e21 and beyond; leave that shape
                        // to the numeric round-trip, as before.
                        str = "" + (+str);
                    } else if (str.indexOf(".") >= 0) {
                        // Drop the trailing fraction zeros as a STRING. The old form (`+str`) round-tripped
                        // through a double, which silently collapsed every digit beyond the shortest
                        // round-trippable form — "G17" of 2.0/3 came back with 16 digits instead of 17.
                        str = str.replace(/0+$/, "").replace(/\.$/, "");
                    }
                }

                isZero = str.split("").every(function (s) { return s === "0" || s === "."; });

                decimalIndex = str.indexOf(".");

                if (decimalIndex > 0) {
                    decimalPart = nf[name + "DecimalSeparator"] + str.substr(decimalIndex + 1);
                    str = str.substr(0, decimalIndex);
                }

                if (str.length < minIntLen) {
                    str = Array(minIntLen - str.length + 1).join("0") + str;
                }

                if (decimalPart) {
                    if ((decimalPart.length - 1) < minDecLen) {
                        decimalPart += Array(minDecLen - decimalPart.length + 2).join("0");
                    }

                    if (maxDecLen === 0) {
                        decimalPart = null;
                    } else if ((decimalPart.length - 1) > maxDecLen) {
                        decimalPart = decimalPart.substr(0, maxDecLen + 1);
                    }
                } else if (minDecLen > 0) {
                    decimalPart = nf[name + "DecimalSeparator"] + Array(minDecLen + 1).join("0");
                }

                groupIndex = 0;
                groupSize = groups[groupIndex];

                if (str.length < groupSize) {
                    buffer = str;

                    if (decimalPart) {
                        buffer += decimalPart;
                    }
                } else {
                    index = str.length;
                    done = false;
                    sep = noGroup ? "" : nf[name + "GroupSeparator"];

                    while (!done) {
                        length = groupSize;
                        startIndex = index - length;

                        if (startIndex < 0) {
                            groupSize += startIndex;
                            length += startIndex;
                            startIndex = 0;
                            done = true;
                        }

                        if (!length) {
                            break;
                        }

                        part = str.substr(startIndex, length);

                        if (buffer.length) {
                            buffer = part + sep + buffer;
                        } else {
                            buffer = part;
                        }

                        index -= length;

                        if (groupIndex < groups.length - 1) {
                            groupIndex++;
                            groupSize = groups[groupIndex];
                        }
                    }

                    if (decimalPart) {
                        buffer += decimalPart;
                    }
                }

                if (isNeg && !isZero) {
                    pattern = System.Globalization.NumberFormatInfo[name + "NegativePatterns"][nf[name + "NegativePattern"]];

                    return pattern.replace("-", nf.negativeSign).replace("%", nf.percentSymbol).replace("$", nf.currencySymbol).replace("n", buffer);
                } else if (System.Globalization.NumberFormatInfo[name + "PositivePatterns"]) {
                    pattern = System.Globalization.NumberFormatInfo[name + "PositivePatterns"][nf[name + "PositivePattern"]];

                    return pattern.replace("%", nf.percentSymbol).replace("$", nf.currencySymbol).replace("n", buffer);
                }

                return buffer;
            },

            customFormat: function (number, format, nf, noGroup) {
                var digits = 0,
                    forcedDigits = -1,
                    integralDigits = -1,
                    decimals = 0,
                    forcedDecimals = -1,
                    atDecimals = 0,
                    unused = 1,
                    c, i, f,
                    endIndex,
                    roundingFactor,
                    decimalIndex,
                    isNegative = false,
                    isZero = false,
                    name,
                    groupCfg,
                    buffer = "",
                    isZeroInt = false,
                    wasSeparator = false,
                    wasIntPart = false,
                    isDecimal = number instanceof System.Decimal,
                    isLong = number instanceof System.Int64 || number instanceof System.UInt64,
                    isNeg = isDecimal || isLong ? (number.isZero() ? false : number.isNegative()) : number < 0;

                name = "number";

                if (format.indexOf("%") !== -1) {
                    name = "percent";
                } else if (format.indexOf("$") !== -1) {
                    name = "currency";
                }

                for (i = 0; i < format.length; i++) {
                    c = format.charAt(i);

                    if (c === "'" || c === '"') {
                        i = format.indexOf(c, i + 1);

                        if (i < 0) {
                            break;
                        }
                    } else if (c === "\\") {
                        i++;
                    } else {
                        if (c === "0" || c === "#") {
                            decimals += atDecimals;

                            if (c === "0") {
                                if (atDecimals) {
                                    forcedDecimals = decimals;
                                } else if (forcedDigits < 0) {
                                    forcedDigits = digits;
                                }
                            }

                            digits += !atDecimals;
                        }

                        atDecimals = atDecimals || c === ".";
                    }
                }
                forcedDigits = forcedDigits < 0 ? 1 : digits - forcedDigits;

                if (isNeg) {
                    isNegative = true;
                }

                roundingFactor = Math.pow(10, decimals);

                if (isDecimal) {
                    number = System.Decimal.round(number.abs().mul(roundingFactor), 4).div(roundingFactor).toString();
                } else if (isLong) {
                    number = (number.eq(System.Int64.MinValue) ? System.Int64(number.value.toUnsigned()) : number.abs()).mul(roundingFactor).div(roundingFactor).toString();
                } else {
                    number = "" + (Math.round(Math.abs(number) * roundingFactor) / roundingFactor);
                }

                isZero = number.split("").every(function (s) { return s === "0" || s === "."; });

                decimalIndex = number.indexOf(".");
                integralDigits = decimalIndex < 0 ? number.length : decimalIndex;
                i = integralDigits - digits;

                groupCfg = {
                    groupIndex: Math.max(integralDigits, forcedDigits),
                    sep: noGroup ? "" : nf[name + "GroupSeparator"]
                };

                if (integralDigits === 1 && number.charAt(0) === "0") {
                    isZeroInt = true;
                }

                for (f = 0; f < format.length; f++) {
                    c = format.charAt(f);

                    if (c === "'" || c === '"') {
                        endIndex = format.indexOf(c, f + 1);

                        buffer += format.substring(f + 1, endIndex < 0 ? format.length : endIndex);

                        if (endIndex < 0) {
                            break;
                        }

                        f = endIndex;
                    } else if (c === "\\") {
                        buffer += format.charAt(f + 1);
                        f++;
                    } else if (c === "#" || c === "0") {
                        wasIntPart = true;

                        if (!wasSeparator && isZeroInt && c === "#") {
                            i++;
                        } else {
                            groupCfg.buffer = buffer;

                            if (i < integralDigits) {
                                if (i >= 0) {
                                    if (unused) {
                                        this.addGroup(number.substr(0, i), groupCfg);
                                    }

                                    this.addGroup(number.charAt(i), groupCfg);
                                } else if (i >= integralDigits - forcedDigits) {
                                    this.addGroup("0", groupCfg);
                                }

                                unused = 0;
                            } else if (forcedDecimals-- > 0 || i < number.length) {
                                this.addGroup(i >= number.length ? "0" : number.charAt(i), groupCfg);
                            }

                            buffer = groupCfg.buffer;

                            i++;
                        }
                    } else if (c === ".") {
                        if (!wasIntPart && !isZeroInt) {
                            buffer += number.substr(0, integralDigits);
                            wasIntPart = true;
                        }

                        if (number.length > ++i || forcedDecimals > 0) {
                            wasSeparator = true;
                            buffer += nf[name + "DecimalSeparator"];
                        }
                    } else if (c !== ",") {
                        buffer += c;
                    }
                }

                if (isNegative && !isZero) {
                    buffer = "-" + buffer;
                }

                return buffer;
            },

            addGroup: function (value, cfg) {
                var buffer = cfg.buffer,
                    sep = cfg.sep,
                    groupIndex = cfg.groupIndex;

                for (var i = 0, length = value.length; i < length; i++) {
                    buffer += value.charAt(i);

                    if (sep && groupIndex > 1 && groupIndex-- % 3 === 1) {
                        buffer += sep;
                    }
                }

                cfg.buffer = buffer;
                cfg.groupIndex = groupIndex;
            },

            parseFloat: function (s, provider) {
                var res = { };

                Transpose.Int.tryParseFloat(s, provider, res, false);

                return res.v;
            },

            tryParseFloat: function (s, provider, result, safe) {
                result.v = 0;

                if (safe == null) {
                    safe = true;
                }

                if (s == null) {
                    if (safe) {
                        return false;
                    }

                    throw new System.ArgumentNullException.$ctor1("s");
                }

                s = s.trim();

                var nfInfo = (provider || System.Globalization.CultureInfo.getCurrentCulture()).getFormat(System.Globalization.NumberFormatInfo),
                    point = nfInfo.numberDecimalSeparator,
                    thousands = nfInfo.numberGroupSeparator;

                var errMsg = "Input string was not in a correct format.";

                var pointIndex = s.indexOf(point);
                var thousandIndex = thousands ? s.indexOf(thousands) : -1;

                if (pointIndex > -1) {
                    // point before thousands is not allowed
                    // "10.2,5" -> FormatException
                    // "1,0.2,5" -> FormatException
                    if (((pointIndex < thousandIndex) || ((thousandIndex > -1) && (pointIndex < s.indexOf(thousands, pointIndex))))
                        // only one point is allowed
                        || (s.indexOf(point, pointIndex + 1) > -1)) {
                        if (safe) {
                            return false;
                        }

                        throw new System.FormatException.$ctor1(errMsg);
                    }
                }

                if ((point !== ".") && (thousands !== ".") && (s.indexOf(".") > -1)) {
                    if (safe) {
                        return false;
                    }

                    throw new System.FormatException.$ctor1(errMsg);
                }

                if (thousandIndex > -1) {
                    // mutiple thousands are allowed, so we remove them before going further
                    var tmpStr = "";

                    for (var i = 0; i < s.length; i++) {
                        if (s[i] !== thousands) {
                            tmpStr += s[i];
                        }
                    }

                    s = tmpStr;
                }

                if (s === nfInfo.negativeInfinitySymbol) {
                    result.v = Number.NEGATIVE_INFINITY;

                    return true;
                } else if (s === nfInfo.positiveInfinitySymbol) {
                    result.v = Number.POSITIVE_INFINITY;

                    return true;
                } else if (s === nfInfo.nanSymbol) {
                    result.v = Number.NaN;

                    return true;
                }

                var countExp = 0,
                    ePrev = false;

                for (var i = 0; i < s.length; i++) {
                    if (!System.Char.isNumber(s[i].charCodeAt(0)) &&
                        s[i] !== "." &&
                        s[i] !== "," &&
                        (s[i] !== nfInfo.positiveSign || i !== 0 && !ePrev) &&
                        (s[i] !== nfInfo.negativeSign || i !== 0 && !ePrev) &&
                        s[i] !== point &&
                        s[i] !== thousands) {
                        if (s[i].toLowerCase() === "e") {
                            ePrev = true;
                            countExp++;

                            if (countExp > 1) {
                                if (safe) {
                                    return false;
                                }

                                throw new System.FormatException.$ctor1(errMsg);
                            }
                        } else {
                            ePrev = false;
                            if (safe) {
                                return false;
                            }

                            throw new System.FormatException.$ctor1(errMsg);
                        }
                    } else {
                        ePrev = false;
                    }
                }

                var r = parseFloat(s.replace(point, "."));

                if (isNaN(r)) {
                    if (safe) {
                        return false;
                    }

                    throw new System.FormatException.$ctor1(errMsg);
                }

                result.v = r;

                return true;
            },

            parseInt: function (str, min, max, radix) {
                radix = radix || 10;

                if (str == null) {
                    throw new System.ArgumentNullException.$ctor1("str");
                }

                str = str.trim();

                if ((radix <= 10 && !/^[+-]?[0-9]+$/.test(str))
                    || (radix == 16 && !/^[+-]?[0-9A-F]+$/gi.test(str))) {
                    throw new System.FormatException.$ctor1("Input string was not in a correct format.");
                }

                var result = parseInt(str, radix);

                if (isNaN(result)) {
                    throw new System.FormatException.$ctor1("Input string was not in a correct format.");
                }

                if (result < min || result > max) {
                    throw new System.OverflowException();
                }

                return result;
            },

            tryParseInt: function (str, result, min, max, radix) {
                result.v = 0;
                radix = radix || 10;

                if (str != null && str.trim === "".trim) {
                    str = str.trim();
                }

                if ((radix <= 10 && !/^[+-]?[0-9]+$/.test(str))
                    || (radix == 16 && !/^[+-]?[0-9A-F]+$/gi.test(str))) {
                    return false;
                }

                result.v = parseInt(str, radix);

                if (result.v < min || result.v > max) {
                    result.v = 0;

                    return false;
                }

                return true;
            },

            isInfinite: function (x) {
                return x === Number.POSITIVE_INFINITY || x === Number.NEGATIVE_INFINITY;
            },

            trunc: function (num) {
                if (!Transpose.isNumber(num)) {
                    return Transpose.Int.isInfinite(num) ? num : null;
                }

                return num > 0 ? Math.floor(num) : Math.ceil(num);
            },

            div: function (x, y) {
                if (x == null || y == null) {
                    return null;
                }

                if (y === 0) {
                    throw new System.DivideByZeroException();
                }

                return this.trunc(x / y);
            },

            mod: function (x, y) {
                if (x == null || y == null) {
                    return null;
                }

                if (y === 0) {
                    throw new System.DivideByZeroException();
                }

                return x % y;
            },

            check: function (x, type) {
                if (System.Int64.is64Bit(x)) {
                    return System.Int64.check(x, type);
                } else if (x instanceof System.Decimal) {
                    return System.Decimal.toInt(x, type);
                }

                if (Transpose.isNumber(x)) {
                    if (System.Int64.is64BitType(type)) {
                        if (type === System.UInt64 && x < 0) {
                            throw new System.OverflowException();
                        }

                        return type === System.Int64 ? System.Int64(x) : System.UInt64(x);
                    } else if (!type.$is(x)) {
                        throw new System.OverflowException();
                    }
                }

                if (Transpose.Int.isInfinite(x) || isNaN(x)) {
                    if (System.Int64.is64BitType(type)) {
                        return type.MinValue;
                    }

                    return type.min;
                }

                return x;
            },

            sxb: function (x) {
                return Transpose.isNumber(x) ? (x | (x & 0x80 ? 0xffffff00 : 0)) : ((Transpose.Int.isInfinite(x) || isNaN(x)) ? System.SByte.min : null);
            },

            sxs: function (x) {
                return Transpose.isNumber(x) ? (x | (x & 0x8000 ? 0xffff0000 : 0)) : ((Transpose.Int.isInfinite(x) || isNaN(x)) ? System.Int16.min : null);
            },

            clip8: function (x) {
                return Transpose.isNumber(x) ? Transpose.Int.sxb(x & 0xff) : ((Transpose.Int.isInfinite(x) || isNaN(x)) ? System.SByte.min : null);
            },

            clipu8: function (x) {
                return Transpose.isNumber(x) ? x & 0xff : ((Transpose.Int.isInfinite(x) || isNaN(x)) ? System.Byte.min : null);
            },

            clip16: function (x) {
                return Transpose.isNumber(x) ? Transpose.Int.sxs(x & 0xffff) : ((Transpose.Int.isInfinite(x) || isNaN(x)) ? System.Int16.min : null);
            },

            clipu16: function (x) {
                return Transpose.isNumber(x) ? x & 0xffff : ((Transpose.Int.isInfinite(x) || isNaN(x)) ? System.UInt16.min : null);
            },

            clip32: function (x) {
                return Transpose.isNumber(x) ? x | 0 : ((Transpose.Int.isInfinite(x) || isNaN(x)) ? System.Int32.min : null);
            },

            clipu32: function (x) {
                return Transpose.isNumber(x) ? x >>> 0 : ((Transpose.Int.isInfinite(x) || isNaN(x)) ? System.UInt32.min : null);
            },

            clip64: function (x) {
                return Transpose.isNumber(x) ? System.Int64(Transpose.Int.trunc(x)) : ((Transpose.Int.isInfinite(x) || isNaN(x)) ? System.Int64.MinValue : null);
            },

            clipu64: function (x) {
                return Transpose.isNumber(x) ? System.UInt64(Transpose.Int.trunc(x)) : ((Transpose.Int.isInfinite(x) || isNaN(x)) ? System.UInt64.MinValue : null);
            },

            sign: function (x) {
                if (x === Number.POSITIVE_INFINITY) {
                    return 1;
                }

                if (x === Number.NEGATIVE_INFINITY) {
                    return -1;
                }

                return Transpose.isNumber(x) ? (x === 0 ? 0 : (x < 0 ? -1 : 1)) : null;
            },

            $mul: Math.imul || function (a, b) {
                var ah = (a >>> 16) & 0xffff,
                    al = a & 0xffff,
                    bh = (b >>> 16) & 0xffff,
                    bl = b & 0xffff;

                return ((al * bl) + (((ah * bl + al * bh) << 16) >>> 0) | 0);
            },

            mul: function (a, b, overflow) {
                if (a == null || b == null) {
                    return null;
                }

                if (overflow) {
                    Transpose.Int.check(a * b, System.Int32)
                }

                return Transpose.Int.$mul(a, b);
            },

            umul: function (a, b, overflow) {
                if (a == null || b == null) {
                    return null;
                }

                if (overflow) {
                    Transpose.Int.check(a * b, System.UInt32)
                }

                return Transpose.Int.$mul(a, b) >>> 0;
            },

            bitIncrement: function (x) {
                if (isNaN(x) || x === Number.POSITIVE_INFINITY) { return x; }
                if (x === Number.NEGATIVE_INFINITY) { return -3.40282346638528859e+38; }
                if (x === 0.0) { return 1.4e-45; }

                var bits = System.BitConverter.singleToInt32Bits(x);

                if (bits < 0) { bits = bits - 1; }
                else { bits = bits + 1; }

                return System.BitConverter.int32BitsToSingle(bits);
            },

            bitDecrement: function (x) { return -Transpose.Int.bitIncrement(-x); },
        }
    });

    Transpose.Int.$kind = "";
    Transpose.Class.addExtend(Transpose.Int, [System.IComparable$1(Transpose.Int), System.IEquatable$1(Transpose.Int)]);

    (function () {
        var createIntType = function (name, min, max, precision, toUnsign) {
            var type = Transpose.define(name, {
                inherits: [System.IComparable, System.IFormattable],

                statics: {
                    $number: true,
                    toUnsign: toUnsign,
                    min: min,
                    max: max,
                    precision: precision,

                    $is: function (instance) {
                        return typeof (instance) === "number" && Math.floor(instance, 0) === instance && instance >= min && instance <= max;
                    },
                    getDefaultValue: function () {
                        return 0;
                    },
                    parse: function (s, radix) {
                        return Transpose.Int.parseInt(s, min, max, radix);
                    },
                    tryParse: function (s, result, radix) {
                        return Transpose.Int.tryParseInt(s, result, min, max, radix);
                    },
                    format: function (number, format, provider) {
                        return Transpose.Int.format(number, format, provider, type, toUnsign);
                    },
                    equals: function (v1, v2) {
                        if (Transpose.is(v1, type) && Transpose.is(v2, type)) {
                            return Transpose.unbox(v1, true) === Transpose.unbox(v2, true);
                        }

                        return false;
                    },
                    equalsT: function (v1, v2) {
                        return Transpose.unbox(v1, true) === Transpose.unbox(v2, true);
                    }
                }
            });

            type.$kind = "";
            Transpose.Class.addExtend(type, [System.IComparable$1(type), System.IEquatable$1(type)]);
        };

        createIntType("System.Byte", 0, 255, 3);
        createIntType("System.SByte", -128, 127, 3, Transpose.Int.clipu8);
        createIntType("System.Int16", -32768, 32767, 5, Transpose.Int.clipu16);
        createIntType("System.UInt16", 0, 65535, 5);
        createIntType("System.Int32", -2147483648, 2147483647, 10, Transpose.Int.clipu32);
        createIntType("System.UInt32", 0, 4294967295, 10);
    })();
