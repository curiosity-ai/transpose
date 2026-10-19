    Transpose.define("System.DateTime", {
        inherits: function () { return [System.IComparable, System.IComparable$1(System.DateTime), System.IEquatable$1(System.DateTime), System.IFormattable]; },
        $kind: "struct",
        fields: {
            kind: 0
        },
        methods: {
            $clone: function (to) { return this; }
        },
        statics: {
            $minTicks: null,
            $maxTicks: null,
            $minOffset: null,
            $maxOffset: null,
            $default: null,
            $min: null,
            $max: null,

            TicksPerDay: System.Int64(864e9),

            DaysTo1970: 719162,
            YearDaysByMonth: [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334],

            getMinTicks: function () {
                if (this.$minTicks === null) {
                    this.$minTicks = System.Int64(0);
                }

                return this.$minTicks;
            },

            getMaxTicks: function () {
                if (this.$maxTicks === null) {
                    this.$maxTicks = System.Int64("3652059").mul(this.TicksPerDay).sub(1);
                }

                return this.$maxTicks;
            },

            // Difference in Ticks from 1-Jan-0001 to 1-Jan-1970 at UTC
            $getMinOffset: function () {
                if (this.$minOffset === null) {
                    this.$minOffset = System.Int64(621355968e9);
                }

                return this.$minOffset;
            },

            // Difference in Ticks between 1970-01-01 and 100 nanoseconds before 10000-01-01 UTC
            $getMaxOffset: function () {
                if (this.$maxOffset === null) {
                    this.$maxOffset = this.getMaxTicks().sub(this.$getMinOffset());
                }

                return this.$maxOffset;
            },

            $is: function (instance) {
                return Transpose.isDate(instance);
            },

            getDefaultValue: function () {
                if (this.$default === null) {
                    this.$default = this.getMinValue();
                }

                return this.$default;
            },

            getMinValue: function () {
                if (this.$min === null) {
                    var d = new Date(1, 0, 1, 0, 0, 0, 0);

                    d.setFullYear(1);
                    d.setSeconds(0);

                    d.kind = 0;
                    d.ticks = this.getMinTicks();

                    this.$min = d;
                }

                return this.$min;
            },

            getMaxValue: function () {
                if (this.$max === null) {
                    var d = new Date(9999, 11, 31, 23, 59, 59, 999);

                    d.kind = 0;
                    d.ticks = this.getMaxTicks();

                    this.$max = d;
                }

                return this.$max;
            },

            $getTzOffset: function (d) {
                // 60 seconds * 1000 milliseconds * 10000
                return System.Int64(d.getTimezoneOffset()).mul(6e8);
            },

            toLocalTime: function (d, throwOnOverflow) {
                var kind = (d.kind !== undefined) ? d.kind : 0,
                    ticks = this.getTicks(d),
                    d1;

                if (kind === 2) {
                    d1 = new Date(d.getTime());
                    d1.kind = 2;
                    d1.ticks = ticks;

                    return d1;
                }

                d1 = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate(), d.getUTCHours(), d.getUTCMinutes(), d.getUTCSeconds(), d.getUTCMilliseconds()));

                d1.kind = 2;
                d1.ticks = ticks.sub(this.$getTzOffset(d));

                // Check if Ticks are out of range
                if (d1.ticks.gt(this.getMaxTicks()) || d1.ticks.lt(0)) {
                    if (throwOnOverflow && throwOnOverflow === true) {
                        throw new System.ArgumentException.$ctor1("Specified argument was out of the range of valid values.");
                    } else {
                        d1 = this.create$2(ticks.add(this.$getTzOffset(d1)), 2);
                    }
                }

                return d1;
            },

            toUniversalTime: function (d) {
                var kind = (d.kind !== undefined) ? d.kind : 0,
                    ticks = this.getTicks(d),
                    d1;

                if (kind === 1) {
                    d1 = new Date(d.getTime());
                    d1.kind = 1;
                    d1.ticks = ticks;

                    return d1;
                }

                d1 = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate(), d.getUTCHours(), d.getUTCMinutes(), d.getUTCSeconds(), d.getUTCMilliseconds()));

                d1.kind = 1;
                d1.ticks = ticks.add(this.$getTzOffset(d));

                // Check if Ticks are out of range
                if (d1.ticks.gt(this.getMaxTicks()) || d1.ticks.lt(0)) {
                    d1 = this.create$2(ticks.add(this.$getTzOffset(d1)), 1);
                }

                return d1;
            },

            // Get the number of ticks since 0001-01-01T00:00:00.0000000 UTC
            getTicks: function (d) {
                if (d.ticks) {
                    return d.ticks;
                }

                var kind = (d.kind !== undefined) ? d.kind : 0;

                if (kind === 1) {
                    d.ticks = System.Int64(d.getTime()).mul(10000).add(this.$getMinOffset());
                } else {
                    d.ticks = System.Int64(d.getTime()).mul(10000).add(this.$getMinOffset()).sub(this.$getTzOffset(d));
                }

                return d.ticks;
            },

            // The clock time of d, as a Date whose UTC fields hold it. A Local/Unspecified DateTime
            // keeps a real instant (what JavaScript interop reads) but, as in .NET, its value is its
            // ticks: reading the instant's local fields instead moves a clock time the zone skips or
            // repeats around a DST switch, and every field of a date before the zone's first
            // whole-minute offset.
            $clock: function (d) {
                return new Date(this.getTicks(d).div(10000).toNumber() - 62135596800000);
            },

            // DateTime.AddUnits: a whole and a fractional part, each truncated to ticks.
            $addUnits: function (d, value, ticksPerUnit) {
                var whole = value < 0 ? Math.ceil(value) : Math.floor(value);

                return this.addTicks(d, System.Int64(whole).mul(ticksPerUnit).add(System.Int64(Math.trunc((value - whole) * ticksPerUnit))));
            },

            create: function (year, month, day, hour, minute, second, millisecond, kind) {
                year = (year !== undefined) ? year : new Date().getFullYear();
                month = (month !== undefined) ? month : new Date().getMonth() + 1;
                day = (day !== undefined) ? day : 1;
                hour = (hour !== undefined) ? hour : 0;
                minute = (minute !== undefined) ? minute : 0;
                second = (second !== undefined) ? second : 0;
                millisecond = (millisecond !== undefined) ? millisecond : 0;
                kind = (kind !== undefined) ? kind : 0;

                var d;

                // The ticks come from the clock fields, never from the local instant: a clock time
                // the zone skips (a DST gap) has no local instant of its own to read them back from.
                var clock = new Date(Date.UTC(year, month - 1, day, hour, minute, second, millisecond));
                clock.setUTCFullYear(year);

                if (kind === 1) {
                    d = clock;
                } else {
                    d = new Date(year, month - 1, day, hour, minute, second, millisecond);
                    d.setFullYear(year);
                }

                d.kind = kind;
                d.ticks = System.Int64(clock.getTime()).mul(10000).add(this.$getMinOffset());

                return d;
            },

            create$2: function (ticks, kind) {
                ticks = System.Int64.is64Bit(ticks) ? ticks : System.Int64(ticks);

                var d;

                if (ticks.lt(this.TicksPerDay)) {
                    d = new Date(0);
                    d.setMilliseconds(d.getMilliseconds() + this.$getTzOffset(d).div(10000).toNumber());
                    d.setFullYear(1);
                } else {
                    d = new Date(ticks.sub(this.$getMinOffset()).div(10000).toNumber());

                    if (kind !== 1) {
                        d.setTime(d.getTime() + (d.getTimezoneOffset() * 60000));
                    }
                }

                d.kind = (kind !== undefined) ? kind : 0;
                d.ticks = ticks;

                return d;
            },

            getToday: function () {
                var d = this.getNow()

                d.setHours(0);
                d.setMinutes(0);
                d.setSeconds(0);
                d.setMilliseconds(0);

                return d;
            },

            getNow: function () {
                var d = new Date();

                d.kind = 2;

                return d;
            },

            getUtcNow: function () {
                var d = new Date();

                d.kind = 1;

                return d;
            },

            getTimeOfDay: function (d) {
                return new System.TimeSpan(this.getTicks(d).mod(this.TicksPerDay));
            },

            getKind: function (d) {
                d.kind = (d.kind !== undefined) ? d.kind : 0

                return d.kind;
            },

            // The ticks (clock time) are kept, as in .NET; the JS instant is not. A Utc date holds its
            // clock time in the Date's UTC fields and a Local/Unspecified one in its local fields, so
            // copying the instant across kinds shifted the clock time by the local offset.
            specifyKind: function (d, kind) {
                return this.create$2(this.getTicks(d), kind);
            },

            $FileTimeOffset: System.Int64("584388").mul(System.Int64(864e9)),

            FromFileTime: function (fileTime) {
                return this.toLocalTime(this.FromFileTimeUtc(fileTime));
            },

            FromFileTimeUtc: function (fileTime) {
                fileTime = System.Int64.is64Bit(fileTime) ? fileTime : System.Int64(fileTime);

                return this.create$2(fileTime.add(this.$FileTimeOffset), 1);
            },

            ToFileTime: function (d) {
                return this.ToFileTimeUtc(this.toUniversalTime(d));
            },

            ToFileTimeUtc: function (d) {
                return (this.getKind(d) !== 0) ? this.getTicks(this.toUniversalTime(d)) : this.getTicks(d);
            },

            isUseGenitiveForm: function (format, index, tokenLen, patternToMatch) {
                var i,
                    repeat = 0;

                for (i = index - 1; i >= 0 && format[i] !== patternToMatch; i--) { }

                if (i >= 0) {
                    while (--i >= 0 && format[i] === patternToMatch) {
                        repeat++;
                    }

                    if (repeat <= 1) {
                        return true;
                    }
                }

                for (i = index + tokenLen; i < format.length && format[i] !== patternToMatch; i++) { }

                if (i < format.length) {
                    repeat = 0;

                    while (++i < format.length && format[i] === patternToMatch) {
                        repeat++;
                    }

                    if (repeat <= 1) {
                        return true;
                    }
                }

                return false;
            },

            // offset (a TimeSpan) is DateTimeOffset's: the clock time is formatted with it in place of
            // the host's zone, so "K" and "z" print the value's own offset rather than the browser's.
            format: function (d, f, p, offset) {
                var me = this,
                    kind = offset ? 2 : (d.kind || 0),
                    clock = me.$clock(d),
                    df = (p || System.Globalization.CultureInfo.getCurrentCulture()).getFormat(System.Globalization.DateTimeFormatInfo),
                    year = clock.getUTCFullYear(),
                    month = clock.getUTCMonth(),
                    dayOfMonth = clock.getUTCDate(),
                    dayOfWeek = clock.getUTCDay(),
                    hour = clock.getUTCHours(),
                    minute = clock.getUTCMinutes(),
                    second = clock.getUTCSeconds(),
                    millisecond = clock.getUTCMilliseconds(),
                    timezoneOffset = offset ? -offset.getTotalMinutes() : d.getTimezoneOffset(),
                    formats;

                f = f || "G";

                if (f.length === 1) {
                    formats = df.getAllDateTimePatterns(f, true);
                    f = formats ? formats[0] : f;
                } else if (f.length === 2 && f.charAt(0) === "%") {
                    f = f.charAt(1);
                }

                var removeDot = false;

                f = f.replace(/(\\.|'[^']*'|"[^"]*"|d{1,4}|M{1,4}|yyyy|yy|y|HH?|hh?|mm?|ss?|tt?|u|f{1,7}|F{1,7}|K|z{1,3}|\:|\/)/g,
                    function (match, group, index) {
                        var part = match;

                        switch (match) {
                            case "dddd":
                                part = df.dayNames[dayOfWeek];

                                break;
                            case "ddd":
                                part = df.abbreviatedDayNames[dayOfWeek];

                                break;
                            case "dd":
                                part = dayOfMonth < 10 ? "0" + dayOfMonth : dayOfMonth;

                                break;
                            case "d":
                                part = dayOfMonth;

                                break;
                            case "MMMM":
                                if (me.isUseGenitiveForm(f, index, 4, "d")) {
                                    part = df.monthGenitiveNames[month];
                                } else {
                                    part = df.monthNames[month];
                                }

                                break;
                            case "MMM":
                                if (me.isUseGenitiveForm(f, index, 3, "d")) {
                                    part = df.abbreviatedMonthGenitiveNames[month];
                                } else {
                                    part = df.abbreviatedMonthNames[month];
                                }

                                break;
                            case "MM":
                                part = (month + 1) < 10 ? "0" + (month + 1) : (month + 1);

                                break;
                            case "M":
                                part = month + 1;

                                break;
                            case "yyyy":
                                part = ("0000" + year).substring(year.toString().length);

                                break;
                            case "yy":
                                part = (year % 100).toString();

                                if (part.length === 1) {
                                    part = "0" + part;
                                }

                                break;
                            case "y":
                                part = year % 100;

                                break;
                            case "h":
                            case "hh":
                                part = hour % 12;

                                if (!part) {
                                    part = "12";
                                } else if (match === "hh" && part.length === 1) {
                                    part = "0" + part;
                                }

                                break;
                            case "HH":
                                part = hour.toString();

                                if (part.length === 1) {
                                    part = "0" + part;
                                }

                                break;
                            case "H":
                                part = hour;
                                break;
                            case "mm":
                                part = minute.toString();

                                if (part.length === 1) {
                                    part = "0" + part;
                                }

                                break;
                            case "m":
                                part = minute;

                                break;
                            case "ss":
                                part = second.toString();

                                if (part.length === 1) {
                                    part = "0" + part;
                                }

                                break;
                            case "s":
                                part = second;
                                break;
                            case "t":
                            case "tt":
                                part = (hour < 12) ? df.amDesignator : df.pmDesignator;

                                if (match === "t") {
                                    part = part.charAt(0);
                                }

                                break;
                            case "F":
                            case "FF":
                            case "FFF":
                            case "FFFF":
                            case "FFFFF":
                            case "FFFFFF":
                            case "FFFFFFF":
                                part = millisecond.toString();

                                if (part.length < 3) {
                                    part = Array(4 - part.length).join("0") + part;
                                }

                                part = part.substr(0, match.length);

                                var c = '0',
                                    i = part.length - 1;

                                for (; i >= 0 && part.charAt(i) === c; i--);
                                part = part.substring(0, i + 1);

                                removeDot = part.length == 0;

                                break;
                            case "f":
                            case "ff":
                            case "fff":
                            case "ffff":
                            case "fffff":
                            case "ffffff":
                            case "fffffff":
                                part = millisecond.toString();

                                if (part.length < 3) {
                                    part = Array(4 - part.length).join("0") + part;
                                }

                                var ln = match === "u" ? 7 : match.length;
                                if (part.length < ln) {
                                    part = part + Array(8 - part.length).join("0");
                                }

                                part = part.substr(0, ln);

                                break;
                            case "z":
                                part = timezoneOffset / 60;
                                part = ((part >= 0) ? "-" : "+") + Math.floor(Math.abs(part));

                                break;
                            case "K":
                            case "zz":
                            case "zzz":
                                if (kind === 0) {
                                    part = "";
                                } else if (kind === 1) {
                                    part = "Z";
                                } else {
                                    part = timezoneOffset / 60;
                                    part = ((part > 0) ? "-" : "+") + System.String.alignString(Math.floor(Math.abs(part)).toString(), 2, "0", 2);

                                    if (match === "zzz" || match === "K") {
                                        part += df.timeSeparator + System.String.alignString(Math.floor(Math.abs(timezoneOffset % 60)).toString(), 2, "0", 2);
                                    }
                                }

                                break;
                            case ":":
                                part = df.timeSeparator;

                                break;
                            case "/":
                                part = df.dateSeparator;

                                break;
                            default:
                                part = match.substr(1, match.length - 1 - (match.charAt(0) !== "\\"));

                                break;
                        }

                        return part;
                    });

                if (removeDot) {
                    if (System.String.endsWith(f, ".")) {
                        f = f.substring(0, f.length - 1);
                    } else if (System.String.endsWith(f, ".Z")) {
                        f = f.substring(0, f.length - 2) + "Z";
                    } else if (kind === 2 && f.match(/\.([+-])/g) !== null) {
                        f = f.replace(/\.([+-])/g, '$1');
                    }
                }

                return f;
            },

            parse: function (value, provider, utc, silent) {
                var d = this.parseExact(value, null, provider, utc, true);

                if (d !== null) {
                    return d;
                }

                d = Date.parse(value);

                if (!isNaN(d)) {
                    return new Date(d);
                } else if (!silent) {
                    throw new System.FormatException.$ctor1("String does not contain a valid string representation of a date and time.");
                }
            },

            parseExact: function (str, format, provider, utc, silent) {
                if (!format) {
                    format = ["G", "g", "F", "f", "D", "d", "R", "r", "s", "S", "U", "u", "O", "o", "Y", "y", "M", "m", "T", "t"];
                }

                if (Transpose.isArray(format)) {
                    var j = 0,
                        d;

                    for (j; j < format.length; j++) {
                        d = this.parseExact(str, format[j], provider, utc, true);

                        if (d != null) {
                            return d;
                        }
                    }

                    if (silent) {
                        return null;
                    }

                    throw new System.FormatException.$ctor1("String does not contain a valid string representation of a date and time.");
                } else {
                    // TODO: The code below assumes that there are no quotation marks around the UTC/Z format token (the format patterns
                    // used by Transpose appear to use quotation marks throughout (see universalSortableDateTimePattern), including
                    // in the recent Newtonsoft.Json.JsonConvert release).
                    // Until the above is sorted out, manually remove quotation marks to get UTC times parsed correctly.
                    format = format.replace("'Z'", "Z");
                }

                var now = new Date(),
                    df = (provider || System.Globalization.CultureInfo.getCurrentCulture()).getFormat(System.Globalization.DateTimeFormatInfo),
                    am = df.amDesignator,
                    pm = df.pmDesignator,
                    idx = 0,
                    index = 0,
                    i = 0,
                    c,
                    token,
                    year = now.getFullYear(),
                    month = now.getMonth() + 1,
                    date = now.getDate(),
                    hh = 0,
                    mm = 0,
                    ss = 0,
                    ff = 0,
                    tt = "",
                    zzh = 0,
                    zzm = 0,
                    zzi,
                    sign,
                    neg,
                    names,
                    name,
                    invalid = false,
                    inQuotes = false,
                    tokenMatched,
                    formats,
                    kind = 0,
                    offset = 0;

                if (str == null) {
                    throw new System.ArgumentNullException.$ctor1("str");
                }

                format = format || "G";

                if (format.length === 1) {
                    formats = df.getAllDateTimePatterns(format, true);
                    format = formats ? formats[0] : format;
                } else if (format.length === 2 && format.charAt(0) === "%") {
                    format = format.charAt(1);
                }

                while (index < format.length) {
                    c = format.charAt(index);
                    token = "";

                    if (inQuotes === "\\") {
                        token += c;
                        index++;
                    } else {
                        var nextChar = format.charAt(index + 1);
                        if (c === '.' && str.charAt(idx) !== c && (nextChar === 'F' || nextChar === 'f')) {
                            index++;
                            c = nextChar;
                        }

                        while ((format.charAt(index) === c) && (index < format.length)) {
                            token += c;
                            index++;
                        }
                    }

                    tokenMatched = true;

                    if (!inQuotes) {
                        if (token === "yyyy" || token === "yy" || token === "y") {
                            if (token === "yyyy") {
                                year = this.subparseInt(str, idx, 4, 4);
                            } else if (token === "yy") {
                                year = this.subparseInt(str, idx, 2, 2);
                            } else if (token === "y") {
                                year = this.subparseInt(str, idx, 2, 4);
                            }

                            if (year == null) {
                                invalid = true;
                                break;
                            }

                            idx += year.length;

                            if (year.length === 2) {
                                year = ~~year;
                                year = (year > 30 ? 1900 : 2000) + year;
                            }
                        } else if (token === "MMM" || token === "MMMM") {
                            month = 0;

                            if (token === "MMM") {
                                if (this.isUseGenitiveForm(format, index, 3, "d")) {
                                    names = df.abbreviatedMonthGenitiveNames;
                                } else {
                                    names = df.abbreviatedMonthNames;
                                }
                            } else {
                                if (this.isUseGenitiveForm(format, index, 4, "d")) {
                                    names = df.monthGenitiveNames;
                                } else {
                                    names = df.monthNames;
                                }
                            }

                            for (i = 0; i < names.length; i++) {
                                name = names[i];

                                if (str.substring(idx, idx + name.length).toLowerCase() === name.toLowerCase()) {
                                    month = (i % 12) + 1;
                                    idx += name.length;

                                    break;
                                }
                            }

                            if ((month < 1) || (month > 12)) {
                                invalid = true;

                                break;
                            }
                        } else if (token === "MM" || token === "M") {
                            month = this.subparseInt(str, idx, token.length, 2);

                            if (month == null || month < 1 || month > 12) {
                                invalid = true;

                                break;
                            }

                            idx += month.length;
                        } else if (token === "dddd" || token === "ddd") {
                            names = token === "ddd" ? df.abbreviatedDayNames : df.dayNames;

                            for (i = 0; i < names.length; i++) {
                                name = names[i];

                                if (str.substring(idx, idx + name.length).toLowerCase() === name.toLowerCase()) {
                                    idx += name.length;

                                    break;
                                }
                            }
                        } else if (token === "dd" || token === "d") {
                            date = this.subparseInt(str, idx, token.length, 2);

                            if (date == null || date < 1 || date > 31) {
                                invalid = true;

                                break;
                            }

                            idx += date.length;
                        } else if (token === "hh" || token === "h") {
                            hh = this.subparseInt(str, idx, token.length, 2);

                            if (hh == null || hh < 1 || hh > 12) {
                                invalid = true;

                                break;
                            }

                            idx += hh.length;
                        } else if (token === "HH" || token === "H") {
                            hh = this.subparseInt(str, idx, token.length, 2);

                            if (hh == null || hh < 0 || hh > 23) {
                                invalid = true;

                                break;
                            }

                            idx += hh.length;
                        } else if (token === "mm" || token === "m") {
                            mm = this.subparseInt(str, idx, token.length, 2);

                            if (mm == null || mm < 0 || mm > 59) {
                                return null;
                            }

                            idx += mm.length;
                        } else if (token === "ss" || token === "s") {
                            ss = this.subparseInt(str, idx, token.length, 2);

                            if (ss == null || ss < 0 || ss > 59) {
                                invalid = true;

                                break;
                            }

                            idx += ss.length;
                        } else if (token === "u") {
                            ff = this.subparseInt(str, idx, 1, 7);

                            if (ff == null) {
                                invalid = true;

                                break;
                            }

                            idx += ff.length;

                            ff = System.DateTime.fractionToMilliseconds(ff);
                        } else if (token.match(/f{1,7}/) !== null) {
                            ff = this.subparseInt(str, idx, token.length, 7);

                            if (ff == null) {
                                invalid = true;

                                break;
                            }

                            idx += ff.length;

                            ff = System.DateTime.fractionToMilliseconds(ff);
                        } else if (token.match(/F{1,7}/) !== null) {
                            ff = this.subparseInt(str, idx, 0, 7);

                            if (ff !== null) {
                                idx += ff.length;

                                ff = System.DateTime.fractionToMilliseconds(ff);
                            }
                        } else if (token === "t") {
                            if (str.substring(idx, idx + 1).toLowerCase() === am.charAt(0).toLowerCase()) {
                                tt = am;
                            } else if (str.substring(idx, idx + 1).toLowerCase() === pm.charAt(0).toLowerCase()) {
                                tt = pm;
                            } else {
                                invalid = true;

                                break;
                            }

                            idx += 1;
                        } else if (token === "tt") {
                            if (str.substring(idx, idx + 2).toLowerCase() === am.toLowerCase()) {
                                tt = am;
                            } else if (str.substring(idx, idx + 2).toLowerCase() === pm.toLowerCase()) {
                                tt = pm;
                            } else {
                                invalid = true;

                                break;
                            }

                            idx += 2;
                        } else if (token === "z" || token === "zz") {
                            sign = str.charAt(idx);

                            if (sign === "-") {
                                neg = true;
                            } else if (sign === "+") {
                                neg = false;
                            } else {
                                invalid = true;

                                break;
                            }

                            idx++;

                            zzh = this.subparseInt(str, idx, 1, 2);

                            if (zzh == null || zzh > 14) {
                                invalid = true;

                                break;
                            }

                            idx += zzh.length;

                            offset = zzh * 60 * 60 * 1000;

                            if (neg) {
                                offset = -offset;
                            }
                        } else if (token === "Z") {
                            var ch = str.substring(idx, idx + 1);
                            if (ch === "Z" || ch === "z") {
                                kind = 1;
                                idx += 1;
                            } else {
                                invalid = true;
                            }

                            break;

                        } else if (token === "zzz" || token === "K") {
                            if (str.substring(idx, idx + 1) === "Z") {
                                kind = 2;
                                idx += 1;

                                break;
                            }

                            name = str.substring(idx, idx + 6);

                            if (name === "") {
                                kind = 0;

                                break;
                            }

                            idx += name.length;

                            if (name.length !== 6 && name.length !== 5) {
                                invalid = true;

                                break;
                            }

                            sign = name.charAt(0);

                            if (sign === "-") {
                                neg = true;
                            } else if (sign === "+") {
                                neg = false;
                            } else {
                                invalid = true;

                                break;
                            }

                            zzi = 1;
                            zzh = this.subparseInt(name, zzi, 1, name.length === 6 ? 2 : 1);

                            if (zzh == null || zzh > 14) {
                                invalid = true;

                                break;
                            }

                            zzi += zzh.length;

                            if (name.charAt(zzi) !== df.timeSeparator) {
                                invalid = true;

                                break;
                            }

                            zzi++;

                            zzm = this.subparseInt(name, zzi, 1, 2);

                            if (zzm == null || zzh > 59) {
                                invalid = true;

                                break;
                            }

                            offset = zzh * 60 * 60 * 1000 + zzm * 60 * 1000;

                            if (neg) {
                                offset = -offset;
                            }

                            kind = 2;
                        } else {
                            tokenMatched = false;
                        }
                    }

                    if (inQuotes || !tokenMatched) {
                        name = str.substring(idx, idx + token.length);

                        if (!inQuotes && name === ":" && (token === df.timeSeparator || token === ":")) {

                        } else if ((!inQuotes && ((token === ":" && name !== df.timeSeparator) || (token === "/" && name !== df.dateSeparator))) || (name !== token && token !== "'" && token !== '"' && token !== "\\")) {
                            invalid = true;

                            break;
                        }

                        if (inQuotes === "\\") {
                            inQuotes = false;
                        }

                        if (token !== "'" && token !== '"' && token !== "\\") {
                            idx += token.length;
                        } else {
                            if (inQuotes === false) {
                                inQuotes = token;
                            } else {
                                if (inQuotes !== token) {
                                    invalid = true;
                                    break;
                                }

                                inQuotes = false;
                            }
                        }
                    }
                }

                if (inQuotes) {
                    invalid = true;
                }

                if (!invalid) {
                    if (idx !== str.length) {
                        invalid = true;
                    } else if (month === 2) {
                        if (((year % 4 === 0) && (year % 100 !== 0)) || (year % 400 === 0)) {
                            if (date > 29) {
                                invalid = true;
                            }
                        } else if (date > 28) {
                            invalid = true;
                        }
                    } else if ((month === 4) || (month === 6) || (month === 9) || (month === 11)) {
                        if (date > 30) {
                            invalid = true;
                        }
                    }
                }

                if (invalid) {
                    if (silent) {
                        return null;
                    }

                    throw new System.FormatException.$ctor1("String does not contain a valid string representation of a date and time.");
                }

                if (tt) {
                    if (hh < 12 && tt === pm) {
                        hh = hh - 0 + 12;
                    } else if (hh > 11 && tt === am) {
                        hh -= 12;
                    }
                }

                var d = this.create(year, month, date, hh, mm, ss, ff, kind);

                // Kind 2 here means the text carried an offset ("Z" is offset 0): the clock time read is
                // at that offset, and the result is the same instant in this zone. Testing for a
                // non-zero offset instead read "+00:00" as local time.
                if (kind === 2) {
                    d = this.toLocalTime(this.create$2(this.getTicks(d).sub(System.Int64(offset).mul(10000)), 1));
                }

                return d;
            },

            // The digits after the decimal separator are a FRACTION of a second, so they are padded
            // out to milliseconds before being truncated: ".25" is 250 ms and ".5" is 500 ms. (They
            // used to be truncated only, which read ".25" as 25 ms — a timestamp did not survive its
            // own round trip whenever the formatter trimmed trailing zeros, as "FFF" does.)
            fractionToMilliseconds: function (digits) {
                if (digits == null) {
                    return digits;
                }

                return (digits + "000").substring(0, 3);
            },

            subparseInt: function (str, index, min, max) {
                var x,
                    token;

                for (x = max; x >= min; x--) {
                    token = str.substring(index, index + x);

                    if (token.length < min) {
                        return null;
                    }

                    if (/^\d+$/.test(token)) {
                        return token;
                    }
                }

                return null;
            },

            tryParse: function (value, provider, result, utc) {
                result.v = this.parse(value, provider, utc, true);

                if (result.v == null) {
                    result.v = this.getMinValue();

                    return false;
                }

                return true;
            },

            tryParseExact: function (v, f, p, r, utc) {
                r.v = this.parseExact(v, f, p, utc, true);

                if (r.v == null) {
                    r.v = this.getMinValue();

                    return false;
                }

                return true;
            },

            isDaylightSavingTime: function (d) {
                if (d.kind !== undefined && d.kind === 1) {
                    return false;
                }

                var tmp = new Date(d.getTime());

                tmp.setMonth(0);
                tmp.setDate(1);

                return tmp.getTimezoneOffset() !== d.getTimezoneOffset();
            },

            dateAddSubTimeSpan: function (d, t, direction) {
                return this.addTicks(d, t.getTicks().mul(direction));
            },

            subdt: function (d, t) {
                return this.dateAddSubTimeSpan(d, t, -1);
            },

            adddt: function (d, t) {
                return this.dateAddSubTimeSpan(d, t, 1);
            },

            subdd: function (a, b) {
                var offset = 0,
                    ticksA = this.getTicks(a),
                    ticksB = this.getTicks(b),
                    valA = ticksA.toNumber(),
                    valB = ticksB.toNumber(),
                    spread = ticksA.sub(ticksB);

                if ((valA === 0 && valB !== 0) || (valB === 0 && valA !== 0)) {
                    offset = Math.round((spread.toNumberDivided(6e8) - (Math.round(spread.toNumberDivided(9e9)) * 15)) * 6e8);
                }

                return new System.TimeSpan(spread.sub(offset));
            },

            addYears: function (d, v) {
                return this.addMonths(d, v * 12);
            },

            // Arithmetic is on the ticks, whatever the kind, as in .NET. Adding to the instant of a
            // Local/Unspecified date instead gained or lost the hour of any DST switch in between.
            addMonths: function (d, v) {
                var ticks = this.getTicks(d),
                    dt = this.$clock(d),
                    day = dt.getUTCDate();

                dt.setUTCDate(1);
                dt.setUTCMonth(dt.getUTCMonth() + v);
                dt.setUTCDate(Math.min(day, this.getDaysInMonth(dt.getUTCFullYear(), dt.getUTCMonth() + 1)));

                return this.create$2(System.Int64(dt.getTime() + 62135596800000).mul(10000).add(ticks.mod(10000)), (d.kind !== undefined) ? d.kind : 0);
            },

            addDays: function (d, v) {
                return this.$addUnits(d, v, 864e9);
            },

            addHours: function (d, v) {
                return this.$addUnits(d, v, 36e9);
            },

            addMinutes: function (d, v) {
                return this.$addUnits(d, v, 6e8);
            },

            addSeconds: function (d, v) {
                return this.$addUnits(d, v, 1e7);
            },

            addMilliseconds: function (d, v) {
                return this.$addUnits(d, v, 1e4);
            },

            addTicks: function (d, v) {
                v = System.Int64.is64Bit(v) ? v : System.Int64(v);

                return this.create$2(this.getTicks(d).add(v), (d.kind !== undefined) ? d.kind : 0);
            },

            add: function (d, value) {
                return this.addTicks(d, value.getTicks());
            },

            subtract: function (d, value) {
                return this.addTicks(d, value.getTicks().mul(-1));
            },

            // Replaced leap year calculation for performance:
            // https://jsperf.com/leapyear-calculation/1
            getIsLeapYear: function (year) {
                if ((year & 3) != 0) {
                    return false;
                }

                return ((year % 100) != 0 || (year % 400) == 0);
            },

            getDaysInMonth: function (year, month) {
                return [31, (this.getIsLeapYear(year) ? 29 : 28), 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][month - 1];
            },

            // Optimized as per: https://jsperf.com/get-day-of-year
            getDayOfYear: function (d) {
                var dt = this.$clock(d),
                    mn = dt.getUTCMonth(),
                    dn = dt.getUTCDate(),
                    dayOfYear = this.YearDaysByMonth[mn] + dn;

                if (mn > 1 && this.getIsLeapYear(dt.getUTCFullYear())) {
                    dayOfYear++;
                }

                return dayOfYear;
            },

            getDate: function (d) {
                var ticks = this.getTicks(d);

                return this.create$2(ticks.sub(ticks.mod(this.TicksPerDay)), (d.kind !== undefined) ? d.kind : 0);
            },

            getDayOfWeek: function (d) {
                return this.$clock(d).getUTCDay();
            },

            getYear: function (d) {
                return this.$clock(d).getUTCFullYear();
            },

            getMonth: function (d) {
                return this.$clock(d).getUTCMonth() + 1;
            },

            getDay: function (d) {
                return this.$clock(d).getUTCDate();
            },

            getHour: function (d) {
                return this.$clock(d).getUTCHours();
            },

            getMinute: function (d) {
                return this.$clock(d).getUTCMinutes();
            },

            getSecond: function (d) {
                return this.$clock(d).getUTCSeconds();
            },

            getMillisecond: function (d) {
                return this.$clock(d).getUTCMilliseconds();
            },

            gt: function (a, b) {
                return (a != null && b != null) ? (this.getTicks(a).gt(this.getTicks(b))) : false;
            },

            gte: function (a, b) {
                return (a != null && b != null) ? (this.getTicks(a).gte(this.getTicks(b))) : false;
            },

            lt: function (a, b) {
                return (a != null && b != null) ? (this.getTicks(a).lt(this.getTicks(b))) : false;
            },

            lte: function (a, b) {
                return (a != null && b != null) ? (this.getTicks(a).lte(this.getTicks(b))) : false;
            }
        }
    });
