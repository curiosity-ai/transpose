    Transpose.define("System.Text.Json.JsonSerializer", {
        statics: {
            methods: {
                // ----------------------------------------------------------------------------------
                // Options
                // ----------------------------------------------------------------------------------

                // Every entry point normalises its JsonSerializerOptions into a plain object once, so
                // the recursive walk below reads flat fields instead of re-checking a C# object on
                // every member. A null options argument means "the library defaults", which is what
                // System.Text.Json's own parameterless overloads use.
                opts: function (options) {
                    if (options && options.$tps) {
                        return options;
                    }

                    var o = options || {};

                    return {
                        $tps:      true,
                        indent:    !!o.WriteIndented,
                        naming:    o.PropertyNamingPolicy || null,
                        dictNaming: o.DictionaryKeyPolicy || null,
                        ci:        !!o.PropertyNameCaseInsensitive,
                        ignore:    o.DefaultIgnoreCondition || 0,
                        numbers:   o.NumberHandling || 0,
                        commas:    !!o.AllowTrailingCommas,
                        comments:  o.ReadCommentHandling || 0,
                        fields:    !!o.IncludeFields,
                        maxDepth:  o.MaxDepth > 0 ? o.MaxDepth : 64
                    };
                },

                fail: function (message) {
                    throw new System.Text.Json.JsonException.$ctor1(message);
                },

                // A [JsonNumberHandling] on a member applies to that member only, so the walk below it
                // runs against a copy of the options carrying it. Cached on the member config, since
                // the same member is read on every element of a collection.
                forMember: function (o, cfg) {
                    if (cfg.numbers == null || cfg.numbers === o.numbers) {
                        return o;
                    }

                    if (cfg.$opts && cfg.$opts.$from === o) {
                        return cfg.$opts;
                    }

                    var copy = {};

                    for (var key in o) {
                        if (o.hasOwnProperty(key)) copy[key] = o[key];
                    }

                    copy.numbers = cfg.numbers;
                    copy.$from   = o;
                    cfg.$opts    = copy;

                    return copy;
                },

                convertName: function (policy, name) {
                    if (!policy) {
                        return name;
                    }

                    return policy.ConvertName(name);
                },

                // ----------------------------------------------------------------------------------
                // Writing
                // ----------------------------------------------------------------------------------

                // System.Text.Json escapes far more than JSON requires: everything outside the basic
                // latin alphanumerics and a short punctuation allow-list, so `+ < > & ' " \` and the
                // backtick come out as \uXXXX and any non-ASCII character does too. That is what makes
                // its output safe to drop inside an HTML <script> block, which is exactly what the
                // self-contained page exports do, so the package reproduces it rather than leaning on
                // JSON.stringify's minimal escaping.
                //
                // The allow-list, verbatim from JavaScriptEncoder.Default:
                //   space ! # $ % ( ) * , - . / : ; = ? @ [ ] ^ _ { | } ~  and  0-9 A-Z a-z
                needsEscape: function (code) {
                    if (code < 0x20 || code > 0x7E) {
                        return true;
                    }

                    // 0-9, A-Z, a-z
                    if ((code >= 0x30 && code <= 0x39) || (code >= 0x41 && code <= 0x5A) || (code >= 0x61 && code <= 0x7A)) {
                        return false;
                    }

                    switch (code) {
                        case 0x20: // space
                        case 0x21: // !
                        case 0x23: // #
                        case 0x24: // $
                        case 0x25: // %
                        case 0x28: // (
                        case 0x29: // )
                        case 0x2A: // *
                        case 0x2C: // ,
                        case 0x2D: // -
                        case 0x2E: // .
                        case 0x2F: // /
                        case 0x3A: // :
                        case 0x3B: // ;
                        case 0x3D: // =
                        case 0x3F: // ?
                        case 0x40: // @
                        case 0x5B: // [
                        case 0x5D: // ]
                        case 0x5E: // ^
                        case 0x5F: // _
                        case 0x7B: // {
                        case 0x7C: // |
                        case 0x7D: // }
                        case 0x7E: // ~
                            return false;
                        default:
                            return true;
                    }
                },

                escapeString: function (value) {
                    var out = '"';

                    for (var i = 0; i < value.length; i++) {
                        var code = value.charCodeAt(i);

                        if (!System.Text.Json.JsonSerializer.needsEscape(code)) {
                            out += value.charAt(i);
                            continue;
                        }

                        switch (code) {
                            case 0x08: out += "\\b"; break;
                            case 0x09: out += "\\t"; break;
                            case 0x0A: out += "\\n"; break;
                            case 0x0C: out += "\\f"; break;
                            case 0x0D: out += "\\r"; break;
                            case 0x5C: out += "\\\\"; break;
                            default:
                                var hex = code.toString(16).toUpperCase();
                                out += "\\u" + "0000".substring(hex.length) + hex;
                                break;
                        }
                    }

                    return out + '"';
                },

                // The raw tree the serializer builds is plain JavaScript, so this writer decides the
                // final text: two-space indentation, ": " after a member name, and an empty array or
                // object staying on one line — all as System.Text.Json writes them.
                write: function (value, indent, depth) {
                    if (value === null || value === undefined) {
                        return "null";
                    }

                    var t = typeof value;

                    if (t === "string") {
                        return System.Text.Json.JsonSerializer.escapeString(value);
                    }

                    if (t === "boolean") {
                        return value ? "true" : "false";
                    }

                    if (t === "number") {
                        if (!isFinite(value)) {
                            System.Text.Json.JsonSerializer.fail("'" + value + "' is not a valid JSON number.");
                        }

                        return String(value);
                    }

                    var pad    = indent ? new Array(depth + 2).join("  ") : "",
                        padEnd = indent ? new Array(depth + 1).join("  ") : "",
                        nl     = indent ? "\n" : "",
                        sep    = indent ? ": " : ":",
                        parts  = [],
                        i;

                    if (Object.prototype.toString.call(value) === "[object Array]") {
                        if (value.length === 0) {
                            return "[]";
                        }

                        for (i = 0; i < value.length; i++) {
                            parts.push(pad + System.Text.Json.JsonSerializer.write(value[i], indent, depth + 1));
                        }

                        return "[" + nl + parts.join("," + nl) + nl + padEnd + "]";
                    }

                    var keys = Object.keys(value);

                    if (keys.length === 0) {
                        return "{}";
                    }

                    for (i = 0; i < keys.length; i++) {
                        parts.push(pad + System.Text.Json.JsonSerializer.escapeString(keys[i]) + sep + System.Text.Json.JsonSerializer.write(value[keys[i]], indent, depth + 1));
                    }

                    return "{" + nl + parts.join("," + nl) + nl + padEnd + "}";
                },

                // ----------------------------------------------------------------------------------
                // Reading
                // ----------------------------------------------------------------------------------

                // JSON.parse is the strict reader System.Text.Json defaults to. The two read options a
                // browser app actually sets — AllowTrailingCommas and ReadCommentHandling.Skip — are
                // not expressible through it, so a payload that fails strict parsing is retried
                // through the hand-written reader below, which accepts exactly those two extensions
                // and nothing else. A conforming document therefore never pays for the fallback.
                //
                // The fallback is a real reader rather than a call into the JavaScript evaluator:
                // evaluating a payload executes whatever it contains, and a Content-Security-Policy
                // without 'unsafe-eval' blocks it outright.
                parse: function (text, o) {
                    try {
                        return JSON.parse(text);
                    } catch (e) {
                        if (!(e instanceof SyntaxError)) {
                            throw e;
                        }

                        if (!o.commas && o.comments === 0) {
                            System.Text.Json.JsonSerializer.fail(e.message);
                        }

                        try {
                            return System.Text.Json.JsonSerializer.parseRelaxed(text, o);
                        } catch (relaxedError) {
                            System.Text.Json.JsonSerializer.fail(relaxedError.message);
                        }
                    }
                },

                parseRelaxed: function (text, o) {
                    if (typeof text !== "string") {
                        throw new SyntaxError("Cannot parse a non-string value as JSON.");
                    }

                    var state = { text: text, i: 0, commas: o.commas, comments: o.comments !== 0 };

                    System.Text.Json.JsonSerializer.skipTrivia(state);
                    var value = System.Text.Json.JsonSerializer.readValue(state);
                    System.Text.Json.JsonSerializer.skipTrivia(state);

                    if (state.i < text.length) {
                        System.Text.Json.JsonSerializer.syntaxError(state, "Unexpected content after the JSON value");
                    }

                    return value;
                },

                syntaxError: function (state, message) {
                    throw new SyntaxError(message + " at position " + state.i + ".");
                },

                skipTrivia: function (state) {
                    while (state.i < state.text.length) {
                        var c = state.text.charAt(state.i);

                        if (c === " " || c === "\t" || c === "\n" || c === "\r") {
                            state.i++;
                            continue;
                        }

                        if (c === "/" && state.comments) {
                            var next = state.text.charAt(state.i + 1);

                            if (next === "/") {
                                state.i += 2;
                                while (state.i < state.text.length && state.text.charAt(state.i) !== "\n") state.i++;
                                continue;
                            }

                            if (next === "*") {
                                state.i += 2;
                                var end = state.text.indexOf("*/", state.i);
                                if (end < 0) System.Text.Json.JsonSerializer.syntaxError(state, "Unterminated comment");
                                state.i = end + 2;
                                continue;
                            }
                        }

                        break;
                    }
                },

                readValue: function (state) {
                    if (state.i >= state.text.length) {
                        System.Text.Json.JsonSerializer.syntaxError(state, "Unexpected end of input");
                    }

                    var c = state.text.charAt(state.i);

                    if (c === "{") return System.Text.Json.JsonSerializer.readObject(state);
                    if (c === "[") return System.Text.Json.JsonSerializer.readArray(state);
                    if (c === '"') return System.Text.Json.JsonSerializer.readString(state);

                    return System.Text.Json.JsonSerializer.readLiteral(state);
                },

                readObject: function (state) {
                    var result = {};
                    state.i++; // {
                    System.Text.Json.JsonSerializer.skipTrivia(state);

                    if (state.text.charAt(state.i) === "}") {
                        state.i++;
                        return result;
                    }

                    while (true) {
                        System.Text.Json.JsonSerializer.skipTrivia(state);

                        if (state.text.charAt(state.i) === "}" && state.commas) {
                            state.i++;
                            return result;
                        }

                        if (state.text.charAt(state.i) !== '"') {
                            System.Text.Json.JsonSerializer.syntaxError(state, "Expected a member name");
                        }

                        var name = System.Text.Json.JsonSerializer.readString(state);

                        System.Text.Json.JsonSerializer.skipTrivia(state);

                        if (state.text.charAt(state.i) !== ":") {
                            System.Text.Json.JsonSerializer.syntaxError(state, "Expected ':' after the member name");
                        }

                        state.i++;
                        System.Text.Json.JsonSerializer.skipTrivia(state);
                        result[name] = System.Text.Json.JsonSerializer.readValue(state);
                        System.Text.Json.JsonSerializer.skipTrivia(state);

                        var c = state.text.charAt(state.i);

                        if (c === ",") {
                            state.i++;
                            continue;
                        }

                        if (c === "}") {
                            state.i++;
                            return result;
                        }

                        System.Text.Json.JsonSerializer.syntaxError(state, "Expected ',' or '}'");
                    }
                },

                readArray: function (state) {
                    var result = [];
                    state.i++; // [
                    System.Text.Json.JsonSerializer.skipTrivia(state);

                    if (state.text.charAt(state.i) === "]") {
                        state.i++;
                        return result;
                    }

                    while (true) {
                        System.Text.Json.JsonSerializer.skipTrivia(state);

                        if (state.text.charAt(state.i) === "]" && state.commas) {
                            state.i++;
                            return result;
                        }

                        result.push(System.Text.Json.JsonSerializer.readValue(state));
                        System.Text.Json.JsonSerializer.skipTrivia(state);

                        var c = state.text.charAt(state.i);

                        if (c === ",") {
                            state.i++;
                            continue;
                        }

                        if (c === "]") {
                            state.i++;
                            return result;
                        }

                        System.Text.Json.JsonSerializer.syntaxError(state, "Expected ',' or ']'");
                    }
                },

                readString: function (state) {
                    state.i++; // opening quote
                    var out = "";

                    while (state.i < state.text.length) {
                        var c = state.text.charAt(state.i);

                        if (c === '"') {
                            state.i++;
                            return out;
                        }

                        if (c === "\\") {
                            state.i++;
                            var e = state.text.charAt(state.i);

                            if (e === '"')      out += '"';
                            else if (e === "\\") out += "\\";
                            else if (e === "/")  out += "/";
                            else if (e === "b")  out += "\b";
                            else if (e === "f")  out += "\f";
                            else if (e === "n")  out += "\n";
                            else if (e === "r")  out += "\r";
                            else if (e === "t")  out += "\t";
                            else if (e === "u") {
                                var hex = state.text.substr(state.i + 1, 4);
                                if (!/^[0-9a-fA-F]{4}$/.test(hex)) System.Text.Json.JsonSerializer.syntaxError(state, "Invalid \\u escape");
                                out += String.fromCharCode(parseInt(hex, 16));
                                state.i += 4;
                            }
                            else System.Text.Json.JsonSerializer.syntaxError(state, "Invalid escape sequence");

                            state.i++;
                            continue;
                        }

                        out += c;
                        state.i++;
                    }

                    System.Text.Json.JsonSerializer.syntaxError(state, "Unterminated string");
                },

                readLiteral: function (state) {
                    var start = state.i;

                    while (state.i < state.text.length && !/[\s,}\]]/.test(state.text.charAt(state.i))) {
                        state.i++;
                    }

                    var token = state.text.substring(start, state.i);

                    if (token === "true")  return true;
                    if (token === "false") return false;
                    if (token === "null")  return null;

                    // The strict grammar only — the relaxed reader exists for commas and comments, not
                    // to become more permissive about values than the server is.
                    if (/^-?(0|[1-9][0-9]*)([.][0-9]+)?([eE][-+]?[0-9]+)?$/.test(token)) {
                        return parseFloat(token);
                    }

                    state.i = start;
                    System.Text.Json.JsonSerializer.syntaxError(state, "Unexpected token '" + token + "'");
                },

                // ----------------------------------------------------------------------------------
                // Contract discovery
                // ----------------------------------------------------------------------------------

                getCacheByType: function (type) {
                    for (var i = 0; i < System.Text.Json.$cache.length; i++) {
                        if (System.Text.Json.$cache[i].type === type) {
                            return System.Text.Json.$cache[i];
                        }
                    }

                    var cfg = { type: type };
                    System.Text.Json.$cache.push(cfg);
                    return cfg;
                },

                validateReflectable: function (type) {
                    do {
                        var ignoreMetaData = type === System.Object || type === Object || type.$literal || type.$kind === "anonymous",
                            nometa         = !Transpose.getMetadata(type);

                        if (!ignoreMetaData && nometa) {
                            if (Transpose.$stjGuard) {
                                delete Transpose.$stjGuard;
                            }

                            throw new System.InvalidOperationException.$ctor1(Transpose.getTypeName(type) + " is not reflectable and cannot be serialized.");
                        }

                        type = ignoreMetaData ? null : Transpose.Reflection.getBaseType(type);
                    } while (!ignoreMetaData && type != null)
                },

                attr: function (member, attributeType) {
                    var found = System.Attribute.getCustomAttributes(member, attributeType);
                    return found && found.length > 0 ? found[0] : null;
                },

                // The member model System.Text.Json applies, which differs from Json.NET's in three
                // ways that matter: a public field takes part only under IncludeFields (or an explicit
                // [JsonInclude]), a non-public setter is not written to unless [JsonInclude] opts it
                // in, and [JsonPropertyOrder] rather than declaration order decides the layout.
                //
                // The cache key carries the naming policy, because the policy decides the JSON name
                // and two different option objects may walk the same type.
                getMembers: function (type, memberCode, o) {
                    var cache = System.Text.Json.JsonSerializer.getCacheByType(type),
                        key   = memberCode + "|" + (o.naming ? Transpose.getTypeName(Transpose.getType(o.naming)) : "") + "|" + (o.fields ? "f" : "");

                    if (cache[key]) {
                        return cache[key];
                    }

                    var isField = memberCode === 4,
                        members = Transpose.Reflection.getMembers(type, memberCode, 52),
                        result  = [];

                    for (var i = 0; i < members.length; i++) {
                        var m       = members[i],
                            include = System.Text.Json.JsonSerializer.attr(m, System.Text.Json.Serialization.JsonIncludeAttribute) != null,
                            ignore  = System.Text.Json.JsonSerializer.attr(m, System.Text.Json.Serialization.JsonIgnoreAttribute);

                        // A compiler-generated backing field is not a member of the contract.
                        if (m.backing) {
                            continue;
                        }

                        if (ignore && ignore.Condition === 1) {
                            continue;
                        }

                        var canRead, canWrite;

                        if (isField) {
                            if (m.a !== 2 || !(o.fields || include)) {
                                continue;
                            }

                            canRead  = true;
                            canWrite = !m.ro;
                        } else {
                            if (m.a !== 2 || m.i) {
                                continue; // non-public, or an indexer
                            }

                            canRead  = !!m.g && (m.g.a === 2 || include);
                            canWrite = !!m.s && (m.s.a === 2 || include);

                            if (!canRead && !canWrite) {
                                continue;
                            }
                        }

                        var nameAttr  = System.Text.Json.JsonSerializer.attr(m, System.Text.Json.Serialization.JsonPropertyNameAttribute),
                            orderAttr = System.Text.Json.JsonSerializer.attr(m, System.Text.Json.Serialization.JsonPropertyOrderAttribute),
                            numAttr   = System.Text.Json.JsonSerializer.attr(m, System.Text.Json.Serialization.JsonNumberHandlingAttribute);

                        result.push({
                            member:   m,
                            name:     nameAttr ? nameAttr.Name : System.Text.Json.JsonSerializer.convertName(o.naming, m.n),
                            order:    orderAttr ? orderAttr.Order : 0,
                            ignore:   ignore ? ignore.Condition : null,
                            numbers:  numAttr ? numAttr.Handling : null,
                            canRead:  canRead,
                            canWrite: canWrite,
                            isField:  isField
                        });
                    }

                    if (result.length > 1) {
                        // A stable sort: only [JsonPropertyOrder] moves a member, everything else keeps
                        // the order reflection handed us.
                        for (var j = 0; j < result.length; j++) {
                            result[j].$i = j;
                        }

                        result.sort(function (a, b) {
                            return a.order !== b.order ? a.order - b.order : a.$i - b.$i;
                        });
                    }

                    cache[key] = result;
                    return result;
                },

                // Whether a member is skipped on write. A [JsonIgnore(Condition = ...)] on the member
                // overrides the options-wide DefaultIgnoreCondition.
                skipOnWrite: function (cfg, value, o) {
                    var condition = cfg.ignore != null ? cfg.ignore : o.ignore;

                    if (condition === 0 || condition == null) {
                        return false;
                    }

                    if (condition === 3) {
                        return value == null;
                    }

                    if (condition === 2) {
                        if (value == null) {
                            return true;
                        }

                        var unboxed = Transpose.unbox(value, true),
                            def     = Transpose.getDefaultValue(cfg.member.rt);

                        return def != null && Transpose.equals(unboxed, def);
                    }

                    return false;
                },

                // ----------------------------------------------------------------------------------
                // Polymorphism
                // ----------------------------------------------------------------------------------

                // [JsonPolymorphic] / [JsonDerivedType] declare the hierarchy on the base type, so the
                // discriminator is a string the author chose rather than a CLR type name. Both are
                // resolved from the *declared* type, which is what System.Text.Json keys off too.
                // A type's attributes live in its metadata rather than on a member info, so
                // System.Attribute.getCustomAttributes (which reads `element.at`) does not reach them.
                typeAttributes: function (type, attributeType) {
                    var meta = Transpose.getMetadata(type),
                        all  = (meta && meta.at) || [];

                    return all.filter(function (a) { return Transpose.is(a, attributeType); });
                },

                // The run-time half of the hierarchy declaration (JsonPolymorphicTypes.Register), for
                // the case where [JsonDerivedType] cannot be written because the base type sits below
                // its implementations and cannot name them.
                // Only the run-time additions are recorded here; `polymorphism` overlays them onto
                // whatever [JsonDerivedType] already declares, so a hierarchy that is declared both
                // ways — a base shared with a server that carries the attributes, plus the derived
                // types only the front-end can see — keeps both halves. `discriminator` stays null
                // unless a registration names one, so it cannot silently override a
                // [JsonPolymorphic(TypeDiscriminatorPropertyName = ...)] on the base.
                registerDerivedType: function (baseType, derivedType, id, discriminatorPropertyName) {
                    var cache = System.Text.Json.JsonSerializer.getCacheByType(baseType),
                        info  = cache.$registered;

                    if (!info) {
                        info = { discriminator: null, types: [] };
                        cache.$registered = info;
                    }

                    if (discriminatorPropertyName) {
                        info.discriminator = discriminatorPropertyName;
                    }

                    for (var i = 0; i < info.types.length; i++) {
                        if (info.types[i].type === derivedType) {
                            info.types[i].id = id;
                            cache.$poly = undefined;
                            return;
                        }
                    }

                    info.types.push({ type: derivedType, id: id });

                    // A hierarchy resolved earlier would have been cached as it was then.
                    cache.$poly = undefined;
                },

                polymorphism: function (type) {
                    if (!type) {
                        return null;
                    }

                    var cache = System.Text.Json.JsonSerializer.getCacheByType(type);

                    if (cache.$poly !== undefined) {
                        return cache.$poly;
                    }

                    var registered = cache.$registered,
                        result     = null,
                        i;

                    // A type with no metadata carries no attributes, but it can still have been
                    // registered at run time — an [External] interface is exactly that case.
                    if (Transpose.getMetadata(type)) {
                        var derived = System.Text.Json.JsonSerializer.typeAttributes(type, System.Text.Json.Serialization.JsonDerivedTypeAttribute),
                            poly    = System.Text.Json.JsonSerializer.typeAttributes(type, System.Text.Json.Serialization.JsonPolymorphicAttribute);

                        if (derived && derived.length > 0) {
                            result = {
                                discriminator: poly && poly.length > 0 && poly[0].TypeDiscriminatorPropertyName ? poly[0].TypeDiscriminatorPropertyName : "$type",
                                unknown:       poly && poly.length > 0 ? poly[0].UnknownDerivedTypeHandling : 0,
                                types:         []
                            };

                            for (i = 0; i < derived.length; i++) {
                                result.types.push({ type: derived[i].DerivedType, id: derived[i].TypeDiscriminator });
                            }
                        }
                    }

                    if (registered) {
                        if (!result) {
                            result = { discriminator: "$type", unknown: 0, types: [] };
                        }

                        if (registered.discriminator) {
                            result.discriminator = registered.discriminator;
                        }

                        // A registration for a type the attribute already names replaces its
                        // discriminator rather than adding a second entry for the same type.
                        for (i = 0; i < registered.types.length; i++) {
                            var entry = registered.types[i],
                                j     = 0,
                                found = false;

                            for (; j < result.types.length; j++) {
                                if (result.types[j].type === entry.type) {
                                    result.types[j] = { type: entry.type, id: entry.id };
                                    found = true;
                                    break;
                                }
                            }

                            if (!found) result.types.push({ type: entry.type, id: entry.id });
                        }
                    }

                    cache.$poly = result;
                    return result;
                },

                discriminatorFor: function (info, runtimeType) {
                    for (var i = 0; i < info.types.length; i++) {
                        if (info.types[i].type === runtimeType) {
                            return info.types[i].id;
                        }
                    }

                    return null;
                },

                typeForDiscriminator: function (info, id) {
                    var i;

                    for (i = 0; i < info.types.length; i++) {
                        if (info.types[i].id === id) {
                            return info.types[i].type;
                        }
                    }

                    // A payload written by Json.NET's TypeNameHandling carries an assembly-qualified
                    // name ("Some.Namespace.Type, Some.Assembly") where the same hierarchy declares the
                    // bare type name. Matching the part before the comma lets a store written before a
                    // migration keep deserializing, and costs nothing for a discriminator that never
                    // contains one.
                    if (typeof id === "string" && id.indexOf(",") > 0) {
                        var bare = System.String.trim(id.substring(0, id.indexOf(",")));

                        for (i = 0; i < info.types.length; i++) {
                            if (info.types[i].id === bare) {
                                return info.types[i].type;
                            }
                        }
                    }

                    return null;
                },

                // ----------------------------------------------------------------------------------
                // Serialize
                // ----------------------------------------------------------------------------------

                Serialize: function (value, options, returnRaw, declaredType, depth) {
                    var o = System.Text.Json.JsonSerializer.opts(options);

                    if (!returnRaw) {
                        var raw = System.Text.Json.JsonSerializer.Serialize(value, o, true, declaredType, 0);
                        return System.Text.Json.JsonSerializer.write(raw, o.indent, 0);
                    }

                    depth = depth || 0;

                    if (depth > o.maxDepth) {
                        System.Text.Json.JsonSerializer.fail("A possible object cycle was detected, or the object depth is larger than the maximum allowed depth of " + o.maxDepth + ".");
                    }

                    if (value == null) {
                        return null;
                    }

                    // Reflection hands a value-typed member back boxed (the accessor metadata carries a
                    // `box` function), so unwrap before anything looks at `typeof`. The box remembers
                    // the declared type, which is the only thing that still says "char" once the value
                    // is a bare JavaScript number.
                    if (typeof value === "object" && value.$boxed) {
                        var boxedType = value.type;
                        value = Transpose.unbox(value, true);

                        if (!declaredType && boxedType) {
                            declaredType = boxedType;
                        }
                    }

                    var runtimeType = Transpose.getType(value),
                        type        = declaredType || runtimeType;

                    if (type && type.$nullable) {
                        type = type.$nullableType;
                    }

                    if (typeof value === "function") {
                        return Transpose.getTypeName(value);
                    }

                    // A primitive JavaScript models directly. The *declared* type decides the shape
                    // here and the widening below is deliberately not applied first: a char is a JS
                    // number at runtime, so widening to its runtime type would write 113 where
                    // System.Text.Json writes "q".
                    if (typeof value !== "object") {
                        if (type === System.Char) {
                            return String.fromCharCode(value);
                        }

                        return value;
                    }

                    // A declared base (or interface) holding a more derived value: the runtime type is
                    // what carries the members, so serialize as that. System.Text.Json does the same
                    // for a polymorphic hierarchy and for `object`.
                    if (runtimeType && type !== runtimeType && (type === System.Object || type.$kind === "interface" || Transpose.Reflection.isAssignableFrom(type, runtimeType))) {
                        type = runtimeType;
                    }

                    return System.Text.Json.JsonSerializer.serializeObject(value, type, declaredType, o, depth);
                },

                serializeObject: function (value, type, declaredType, o, depth) {
                    var i, arr;

                    // --- BCL values with a fixed JSON shape -----------------------------------------
                    if (type === System.Guid)                 return Transpose.toString(value);
                    if (type === System.Uri)                  return value.getAbsoluteUri();
                    if (type === System.Version)              return Transpose.toString(value);
                    if (type === System.Globalization.CultureInfo) return value.name;
                    if (type === System.TimeSpan)             return Transpose.toString(value);
                    if (type === System.Char)                 return String.fromCharCode(value);

                    // A JavaScript number cannot hold a 64-bit integer exactly, so `toJSON` writes
                    // those as JSON strings where System.Text.Json writes numbers; a decimal stays a
                    // number. This matches Transpose.Newtonsoft.Json exactly, which is what the
                    // Curiosity server's converters are written against.
                    if (type === System.Int64 || type === System.UInt64 || type === System.Decimal) {
                        return value.toJSON();
                    }

                    if (type === System.DateTime) {
                        return System.DateTime.format(value, "yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFK");
                    }

                    if (type === System.DateTimeOffset) {
                        return value.ToString$1("yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFK");
                    }

                    if (Transpose.Reflection.isEnum(type)) {
                        return value;
                    }

                    // --- Collections ---------------------------------------------------------------
                    if (Transpose.isArray(null, type)) {
                        if (type.$elementType === System.Byte) {
                            return System.Convert.toBase64String(value);
                        }

                        arr = [];

                        for (i = 0; i < value.length; i++) {
                            arr.push(System.Text.Json.JsonSerializer.Serialize(value[i], o, true, type.$elementType, depth + 1));
                        }

                        return arr;
                    }

                    if (Transpose.Reflection.isAssignableFrom(System.Collections.IDictionary, type)) {
                        var generic  = System.Collections.Generic.Dictionary$2.getTypeParameters(type),
                            keyType   = generic[0],
                            valueType = generic[1],
                            dict      = {},
                            entries   = Transpose.getEnumerator(value);

                        while (entries.moveNext()) {
                            var entry = entries.Current,
                                name  = System.Text.Json.JsonSerializer.dictionaryKey(entry.key, keyType, o);

                            dict[name] = System.Text.Json.JsonSerializer.Serialize(entry.value, o, true, valueType, depth + 1);
                        }

                        return dict;
                    }

                    if (Transpose.Reflection.isAssignableFrom(System.Collections.IEnumerable, type)) {
                        var elementType = System.Text.Json.JsonSerializer.getEnumerableElementType(type),
                            enumerator  = Transpose.getEnumerator(value, elementType);

                        arr = [];

                        while (enumerator.moveNext()) {
                            arr.push(System.Text.Json.JsonSerializer.Serialize(enumerator.Current, o, true, elementType, depth + 1));
                        }

                        return arr;
                    }

                    // --- Objects -------------------------------------------------------------------
                    return System.Text.Json.JsonSerializer.serializeMembers(value, type, declaredType, o, depth);
                },

                dictionaryKey: function (key, keyType, o) {
                    if (keyType && Transpose.Reflection.isEnum(keyType)) {
                        return System.Enum.getName(keyType, key);
                    }

                    var raw = System.Text.Json.JsonSerializer.Serialize(key, o, true, keyType, 0);

                    if (raw === null || typeof raw === "object") {
                        raw = Transpose.toString(key);
                    } else if (typeof raw !== "string") {
                        raw = String(raw);
                    }

                    return System.Text.Json.JsonSerializer.convertName(o.dictNaming, raw);
                },

                getEnumerableElementType: function (type) {
                    var interfaceType;

                    if (System.String.startsWith(type.$$name, "System.Collections.Generic.IEnumerable")) {
                        interfaceType = type;
                    } else {
                        var interfaces = Transpose.Reflection.getInterfaces(type);

                        for (var j = 0; j < interfaces.length; j++) {
                            if (System.String.startsWith(interfaces[j].$$name, "System.Collections.Generic.IEnumerable")) {
                                interfaceType = interfaces[j];
                                break;
                            }
                        }
                    }

                    return interfaceType ? Transpose.Reflection.getGenericArguments(interfaceType)[0] : null;
                },

                serializeMembers: function (value, type, declaredType, o, depth) {
                    var raw = {};

                    System.Text.Json.JsonSerializer.validateReflectable(type);

                    // The discriminator is declared on the base type and must be written first —
                    // System.Text.Json's reader requires it before any other member.
                    var info = System.Text.Json.JsonSerializer.polymorphism(declaredType) ||
                               System.Text.Json.JsonSerializer.polymorphism(type);

                    if (info) {
                        var id = System.Text.Json.JsonSerializer.discriminatorFor(info, type);

                        if (id != null) {
                            raw[info.discriminator] = id;
                        } else if (info.unknown === 0 && declaredType && declaredType !== type) {
                            System.Text.Json.JsonSerializer.fail("Runtime type '" + Transpose.getTypeName(type) + "' is not supported by polymorphic type '" + Transpose.getTypeName(declaredType) + "'.");
                        }
                    }

                    // An anonymous or [ObjectLiteral] type has no reflection contract to walk: it is a
                    // plain JavaScript object, so every own property is a member.
                    if (!Transpose.getMetadata(type) || type.$literal || type.$kind === "anonymous") {
                        if (value.toJSON) {
                            return value.toJSON();
                        }

                        for (var key in value) {
                            if (value.hasOwnProperty(key)) {
                                raw[System.Text.Json.JsonSerializer.convertName(o.naming, key)] = System.Text.Json.JsonSerializer.Serialize(value[key], o, true, null, depth + 1);
                            }
                        }

                        return raw;
                    }

                    var properties = System.Text.Json.JsonSerializer.getMembers(type, 16, o),
                        fields     = System.Text.Json.JsonSerializer.getMembers(type, 4, o),
                        i, cfg, member, current;

                    for (i = 0; i < properties.length; i++) {
                        cfg    = properties[i];
                        member = cfg.member;

                        if (!cfg.canRead) continue;

                        current = Transpose.Reflection.midel(member.g, value)();

                        if (System.Text.Json.JsonSerializer.skipOnWrite(cfg, current, o)) continue;

                        raw[cfg.name] = System.Text.Json.JsonSerializer.Serialize(current, o, true, member.rt, depth + 1);
                    }

                    for (i = 0; i < fields.length; i++) {
                        cfg    = fields[i];
                        member = cfg.member;
                        current = Transpose.Reflection.fieldAccess(member, value);

                        if (System.Text.Json.JsonSerializer.skipOnWrite(cfg, current, o)) continue;

                        raw[cfg.name] = System.Text.Json.JsonSerializer.Serialize(current, o, true, member.rt, depth + 1);
                    }

                    return raw;
                },

                // ----------------------------------------------------------------------------------
                // Deserialize
                // ----------------------------------------------------------------------------------

                Deserialize: function (json, type, options) {
                    var o = System.Text.Json.JsonSerializer.opts(options);

                    if (typeof json !== "string") {
                        System.Text.Json.JsonSerializer.fail("The input does not contain any JSON tokens.");
                    }

                    if (json.length === 0 || System.String.trim(json).length === 0) {
                        System.Text.Json.JsonSerializer.fail("The input does not contain any JSON tokens.");
                    }

                    return System.Text.Json.JsonSerializer.read(System.Text.Json.JsonSerializer.parse(json, o), type, o, null, 0);
                },

                // Reads an already-parsed JavaScript value into `type`.
                read: function (raw, type, o, instance, depth) {
                    depth = depth || 0;

                    if (depth > o.maxDepth) {
                        System.Text.Json.JsonSerializer.fail("The maximum configured depth of " + o.maxDepth + " has been exceeded.");
                    }

                    if (type.$kind === "interface") {
                        type = System.Text.Json.JsonSerializer.concreteCollectionType(type);
                    }

                    var isObjectTarget = type === Object || type === System.Object,
                        def            = Transpose.getDefaultValue(type);

                    if (raw === null || raw === undefined) {
                        if (type.$nullable || def === null || isObjectTarget) {
                            return isObjectTarget ? null : def;
                        }

                        // System.Text.Json rejects a JSON null for a non-nullable value type where
                        // Json.NET quietly left the default in place.
                        System.Text.Json.JsonSerializer.fail("The JSON value could not be converted to " + Transpose.getTypeName(type) + ".");
                    }

                    if (type.$nullable) {
                        type = type.$nullableType;
                    }

                    // Deserializing to `object` hands back the parsed JavaScript value. System.Text.Json
                    // produces a JsonElement, which this package does not model.
                    if (isObjectTarget) {
                        return raw;
                    }

                    // An [ObjectLiteral] type compiles to direct property access on a plain JavaScript
                    // object, so it is the parsed value — walking it member by member would deep-convert
                    // typed slots that have no conversion and throw.
                    if (type.$literal) {
                        return Transpose.merge(instance || Transpose.createInstance(type), raw);
                    }

                    var t = typeof raw;

                    if (t === "boolean")  return System.Text.Json.JsonSerializer.readBoolean(raw, type);
                    if (t === "number")   return System.Text.Json.JsonSerializer.readNumber(raw, type, o);
                    if (t === "string")   return System.Text.Json.JsonSerializer.readString2(raw, type, o);

                    if (Object.prototype.toString.call(raw) === "[object Array]") {
                        return System.Text.Json.JsonSerializer.readArrayInto(raw, type, o, instance, depth);
                    }

                    return System.Text.Json.JsonSerializer.readObjectInto(raw, type, o, instance, depth);
                },

                concreteCollectionType: function (type) {
                    if (type === System.Collections.IDictionary) {
                        return System.Collections.Generic.Dictionary$2(System.Object, System.Object);
                    }

                    if (Transpose.Reflection.isGenericType(type) && Transpose.Reflection.isAssignableFrom(System.Collections.Generic.IDictionary$2, Transpose.Reflection.getGenericTypeDefinition(type))) {
                        var p = System.Collections.Generic.Dictionary$2.getTypeParameters(type);
                        return System.Collections.Generic.Dictionary$2(p[0] || System.Object, p[1] || System.Object);
                    }

                    if (type === System.Collections.IList || type === System.Collections.ICollection) {
                        return System.Collections.Generic.List$1(System.Object);
                    }

                    if (Transpose.Reflection.isGenericType(type) && (
                        Transpose.Reflection.isAssignableFrom(System.Collections.Generic.IList$1, Transpose.Reflection.getGenericTypeDefinition(type)) ||
                        Transpose.Reflection.isAssignableFrom(System.Collections.Generic.ICollection$1, Transpose.Reflection.getGenericTypeDefinition(type)) ||
                        Transpose.Reflection.isAssignableFrom(System.Collections.Generic.IEnumerable$1, Transpose.Reflection.getGenericTypeDefinition(type)))) {
                        return System.Collections.Generic.List$1(System.Collections.Generic.List$1.getElementType(type) || System.Object);
                    }

                    return type;
                },

                readBoolean: function (raw, type) {
                    if (type === System.Boolean) return raw;
                    if (type === System.String)  return raw ? "true" : "false";

                    var cast = System.Text.Json.JsonSerializer.castOperator(raw, type);
                    if (cast) return cast(raw);

                    System.Text.Json.JsonSerializer.fail("The JSON value could not be converted to " + Transpose.getTypeName(type) + ".");
                },

                // An integer target only accepts a whole number inside its range. `x | 0` / `x >>> 0`
                // silently wrap instead, so a byte read from -1 became 4294967295 and a ushort read
                // from 70000 stayed 70000 — a value outside the member's own type, handed to the app
                // as if it had been valid. System.Text.Json throws for all of these, and so does this.
                integer: function (raw, type, min, max) {
                    if (typeof raw !== "number" || !isFinite(raw) || Math.floor(raw) !== raw || raw < min || raw > max) {
                        System.Text.Json.JsonSerializer.fail("The JSON value could not be converted to " + Transpose.getTypeName(type) + ".");
                    }

                    return raw;
                },

                readNumber: function (raw, type, o) {
                    if (Transpose.Reflection.isEnum(type))  return Transpose.unbox(System.Enum.parse(type, raw));
                    if (type === System.SByte)              return System.Text.Json.JsonSerializer.integer(raw, type, -128, 127);
                    if (type === System.Byte)               return System.Text.Json.JsonSerializer.integer(raw, type, 0, 255);
                    if (type === System.Int16)              return System.Text.Json.JsonSerializer.integer(raw, type, -32768, 32767);
                    if (type === System.UInt16)             return System.Text.Json.JsonSerializer.integer(raw, type, 0, 65535);
                    if (type === System.Int32)              return System.Text.Json.JsonSerializer.integer(raw, type, -2147483648, 2147483647);
                    if (type === System.UInt32)             return System.Text.Json.JsonSerializer.integer(raw, type, 0, 4294967295);
                    if (type === System.Int64)              return System.Int64(raw);
                    if (type === System.UInt64)             return System.UInt64(raw);
                    if (type === System.Single)             return raw;
                    if (type === System.Double)             return raw;
                    if (type === System.Decimal)            return System.Decimal(raw);
                    if (type === System.Char)               return System.Text.Json.JsonSerializer.integer(raw, type, 0, 65535);
                    if (type === System.TimeSpan)           return System.TimeSpan.fromTicks(raw);

                    var cast = System.Text.Json.JsonSerializer.castOperator(raw, type);
                    if (cast) return cast(raw);

                    System.Text.Json.JsonSerializer.fail("The JSON value could not be converted to " + Transpose.getTypeName(type) + ".");
                },

                readString2: function (raw, type, o) {
                    if (type === System.String)                    return raw;
                    if (type === Function || type === System.Type) return Transpose.Reflection.getType(raw);
                    if (type === System.Globalization.CultureInfo) return new System.Globalization.CultureInfo(raw);
                    if (type === System.Uri)                       return new System.Uri(raw);
                    if (type === System.Version)                   return System.Version.parse(raw);
                    if (type === System.Guid)                      return System.Guid.Parse(raw);
                    if (type === System.TimeSpan)                  return System.TimeSpan.parse(raw);
                    if (type === System.Array.type(System.Byte, 1)) return System.Convert.fromBase64String(raw);

                    if (type === System.DateTime || type === System.DateTimeOffset) {
                        var isUtc  = System.String.endsWith(raw, "Z"),
                            format = "yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFF" + (isUtc ? "'Z'" : "K"),
                            d      = System.DateTime.parseExact(raw, format, null, true, true);

                        d = d != null ? d : System.DateTime.parse(raw, undefined, true);

                        if (d == null) {
                            System.Text.Json.JsonSerializer.fail("The JSON value could not be converted to " + Transpose.getTypeName(type) + ".");
                        }

                        if (isUtc && d.kind !== 1) {
                            d = System.DateTime.specifyKind(d, 1);
                        }

                        return type === System.DateTime ? d : new System.DateTimeOffset.$ctor1(d);
                    }

                    // 64-bit integers and decimals are written as JSON strings by this package (see
                    // serializeObject), so reading them back from one is not a relaxation of
                    // NumberHandling — it is what round-tripping our own output requires.
                    if (type === System.Int64 || type === System.UInt64 || type === System.Decimal) {
                        var parsed = { };

                        if (type === System.Decimal ? !type.tryParse(raw, null, parsed) : !type.tryParse(raw, parsed)) {
                            System.Text.Json.JsonSerializer.fail("The JSON value could not be converted to " + Transpose.getTypeName(type) + ".");
                        }

                        // Named rather than called through `type`, because these constructors read
                        // `this` and lose it when invoked through a variable.
                        if (type === System.Decimal) return System.Decimal(raw);
                        if (type === System.Int64)   return System.Int64(raw);

                        return System.UInt64(raw);
                    }

                    if (Transpose.Reflection.isEnum(type)) {
                        // System.Text.Json needs a JsonStringEnumConverter to read an enum from its
                        // name. A Transpose app has no converter registry, and the Curiosity server
                        // writes enums as names, so the name form is always accepted here.
                        try {
                            return Transpose.unbox(System.Enum.parse(type, raw));
                        } catch (parseError) {
                            System.Text.Json.JsonSerializer.fail("The JSON value could not be converted to " + Transpose.getTypeName(type) + ".");
                        }
                    }

                    if (type === System.Char) {
                        return raw.length === 0 ? 0 : raw.charCodeAt(0);
                    }

                    if (type === System.Boolean) {
                        if (raw === "true")  return true;
                        if (raw === "false") return false;

                        System.Text.Json.JsonSerializer.fail("The JSON value could not be converted to System.Boolean.");
                    }

                    if (type.$number) {
                        if ((o.numbers & 1) === 0) {
                            System.Text.Json.JsonSerializer.fail("The JSON value could not be converted to " + Transpose.getTypeName(type) + ".");
                        }

                        var n = parseFloat(raw);

                        if (isNaN(n)) {
                            System.Text.Json.JsonSerializer.fail("The JSON value could not be converted to " + Transpose.getTypeName(type) + ".");
                        }

                        return System.Text.Json.JsonSerializer.readNumber(n, type, o);
                    }

                    var cast = System.Text.Json.JsonSerializer.castOperator(raw, type);
                    if (cast) return cast(raw);

                    System.Text.Json.JsonSerializer.fail("The JSON value could not be converted to " + Transpose.getTypeName(type) + ".");
                },

                // An implicit/explicit conversion operator from the JSON primitive is how a wrapper
                // type such as UID128 round-trips through its string form.
                castOperator: function (raw, type) {
                    if (raw === null) {
                        return null;
                    }

                    var candidates;

                    if (typeof raw === "boolean" || typeof raw === "string") {
                        candidates = [Transpose.getType(raw)];
                    } else if (typeof raw === "number") {
                        candidates = [System.Double, System.Int64];
                    } else {
                        return null;
                    }

                    for (var i = 0; i < candidates.length; i++) {
                        var explicitOp = Transpose.Reflection.getMembers(type, 8, 284, "op_Explicit", [candidates[i]]);
                        if (explicitOp) {
                            return function (value) { return Transpose.Reflection.midel(explicitOp, null)(value); };
                        }

                        var implicitOp = Transpose.Reflection.getMembers(type, 8, 284, "op_Implicit", [candidates[i]]);
                        if (implicitOp) {
                            return function (value) { return Transpose.Reflection.midel(implicitOp, null)(value); };
                        }
                    }

                    return null;
                },

                readArrayInto: function (raw, type, o, instance, depth) {
                    if (Transpose.isArray(null, type)) {
                        var arr = new Array();
                        System.Array.type(type.$elementType, type.$rank || 1, arr);

                        for (var i = 0; i < raw.length; i++) {
                            arr[i] = System.Text.Json.JsonSerializer.read(raw[i], type.$elementType, o, null, depth + 1);
                        }

                        return arr;
                    }

                    if (Transpose.Reflection.isAssignableFrom(System.Collections.IList, type) ||
                        (Transpose.Reflection.isGenericType(type) && Transpose.Reflection.isAssignableFrom(System.Collections.Generic.HashSet$1, Transpose.Reflection.getGenericTypeDefinition(type)))) {

                        var elementType = System.Collections.Generic.List$1.getElementType(type) ||
                                          Transpose.Reflection.getGenericArguments(type)[0] ||
                                          System.Object,
                            list        = instance || Transpose.createInstance(type);

                        for (var j = 0; j < raw.length; j++) {
                            list.add(System.Text.Json.JsonSerializer.read(raw[j], elementType, o, null, depth + 1));
                        }

                        return list;
                    }

                    // An IEnumerable<T>-only target that is a plain JavaScript array at runtime — a
                    // ReadOnlyArray<T>, or an IEnumerable<T> / IReadOnlyList<T> member. None of the
                    // branches above match it, and falling through to object deserialization would
                    // leave it null.
                    var enumerableElement = System.Text.Json.JsonSerializer.getEnumerableElementType(type);

                    if (enumerableElement != null) {
                        var materialized = new Array();

                        for (var k = 0; k < raw.length; k++) {
                            materialized[k] = System.Text.Json.JsonSerializer.read(raw[k], enumerableElement, o, null, depth + 1);
                        }

                        return materialized;
                    }

                    System.Text.Json.JsonSerializer.fail("The JSON value could not be converted to " + Transpose.getTypeName(type) + ".");
                },

                readObjectInto: function (raw, type, o, instance, depth) {
                    // Resolve the runtime type before anything else, so the contract walked below is
                    // the derived one.
                    var info = System.Text.Json.JsonSerializer.polymorphism(type);

                    if (info) {
                        var id = raw[info.discriminator];

                        if (id === undefined) {
                            if (type.$kind === "interface") {
                                System.Text.Json.JsonSerializer.fail("The JSON payload for polymorphic type '" + Transpose.getTypeName(type) + "' must specify a type discriminator.");
                            }
                        } else {
                            var resolved = System.Text.Json.JsonSerializer.typeForDiscriminator(info, id);

                            if (resolved == null) {
                                System.Text.Json.JsonSerializer.fail("Read unrecognized type discriminator id '" + id + "'.");
                            }

                            type = resolved;
                        }
                    }

                    if (Transpose.Reflection.isAssignableFrom(System.Collections.IDictionary, type)) {
                        var generic   = System.Collections.Generic.Dictionary$2.getTypeParameters(type),
                            keyType   = generic[0] || System.Object,
                            valueType = generic[1] || System.Object,
                            dict      = instance || Transpose.createInstance(type),
                            keys      = Object.keys(raw);

                        for (var d = 0; d < keys.length; d++) {
                            dict.add(System.Text.Json.JsonSerializer.readDictionaryKey(keys[d], keyType, o),
                                     System.Text.Json.JsonSerializer.read(raw[keys[d]], valueType, o, null, depth + 1));
                        }

                        return dict;
                    }

                    // A JSON object can never become a scalar or a sequence. Report that as a
                    // conversion failure rather than letting the contract walker below complain that
                    // System.Int32 carries no reflection metadata.
                    if (Transpose.isArray(null, type) ||
                        Transpose.Reflection.isAssignableFrom(System.Collections.IList, type) ||
                        System.Text.Json.JsonSerializer.isScalarType(type)) {
                        System.Text.Json.JsonSerializer.fail("The JSON value could not be converted to " + Transpose.getTypeName(type) + ".");
                    }

                    System.Text.Json.JsonSerializer.validateReflectable(type);

                    var built  = instance ? { value: instance, consumed: [] } : System.Text.Json.JsonSerializer.construct(type, raw, o, depth),
                        target = built.value;

                    if (target == null) {
                        return target;
                    }

                    var properties = System.Text.Json.JsonSerializer.getMembers(type, 16, o),
                        fields     = System.Text.Json.JsonSerializer.getMembers(type, 4, o),
                        i, cfg;

                    for (i = 0; i < properties.length; i++) {
                        cfg = properties[i];

                        if (!cfg.canWrite || built.consumed.indexOf(cfg.name) >= 0) continue;

                        var pv = System.Text.Json.JsonSerializer.lookup(raw, cfg.name, o);

                        if (pv.found) {
                            Transpose.Reflection.midel(cfg.member.s, target)(System.Text.Json.JsonSerializer.read(pv.value, cfg.member.rt, System.Text.Json.JsonSerializer.forMember(o, cfg), null, depth + 1));
                        }
                    }

                    for (i = 0; i < fields.length; i++) {
                        cfg = fields[i];

                        if (!cfg.canWrite || built.consumed.indexOf(cfg.name) >= 0) continue;

                        var fv = System.Text.Json.JsonSerializer.lookup(raw, cfg.name, o);

                        if (fv.found) {
                            Transpose.Reflection.fieldAccess(cfg.member, target, System.Text.Json.JsonSerializer.read(fv.value, cfg.member.rt, System.Text.Json.JsonSerializer.forMember(o, cfg), null, depth + 1));
                        }
                    }

                    return target;
                },

                // A JSON member name is always a string, so a non-string key type is parsed from it
                // whatever NumberHandling says — that switch is about member *values*.
                isScalarType: function (type) {
                    return !!type.$number ||
                           type === System.String ||
                           type === System.Boolean ||
                           type === System.Char ||
                           type === System.Guid ||
                           type === System.Uri ||
                           type === System.Version ||
                           type === System.DateTime ||
                           type === System.DateTimeOffset ||
                           type === System.TimeSpan ||
                           Transpose.Reflection.isEnum(type);
                },

                readDictionaryKey: function (key, keyType, o) {
                    if (keyType === System.String || keyType === System.Object) {
                        return key;
                    }

                    if (keyType.$number && keyType !== System.Int64 && keyType !== System.UInt64 && keyType !== System.Decimal) {
                        var n = parseFloat(key);

                        if (isNaN(n)) {
                            System.Text.Json.JsonSerializer.fail("The JSON value could not be converted to " + Transpose.getTypeName(keyType) + ".");
                        }

                        return System.Text.Json.JsonSerializer.readNumber(n, keyType, o);
                    }

                    return System.Text.Json.JsonSerializer.readString2(key, keyType, o);
                },

                // Member matching is case-sensitive unless PropertyNameCaseInsensitive is set — the
                // one place System.Text.Json is stricter than Json.NET by default.
                lookup: function (raw, name, o) {
                    if (Object.prototype.hasOwnProperty.call(raw, name)) {
                        return { found: true, value: raw[name] };
                    }

                    if (o.ci) {
                        var lower = name.toLowerCase(),
                            keys  = Object.keys(raw);

                        for (var i = 0; i < keys.length; i++) {
                            if (keys[i].toLowerCase() === lower) {
                                return { found: true, value: raw[keys[i]] };
                            }
                        }
                    }

                    return { found: false };
                },

                // System.Text.Json prefers a public parameterless constructor; failing that it uses the
                // single public constructor, binding each parameter to the JSON member of the same
                // name. [JsonConstructor] overrides both. A member bound through a parameter is not
                // written again afterwards, which is what `consumed` records.
                construct: function (type, raw, o, depth) {
                    var ctors     = Transpose.Reflection.getMembers(type, 1, 54) || [],
                        chosen    = null,
                        publics   = [],
                        declared  = 0,
                        hasEmpty  = false;

                    for (var i = 0; i < ctors.length; i++) {
                        var c = ctors[i];

                        // A synthetic constructor is the compiler's, not the author's — it is the
                        // parameterless one, and it is what createInstance already calls.
                        if (c.isSynthetic) continue;

                        declared++;

                        if ((c.pi || []).length === 0) {
                            hasEmpty = true;
                        }

                        if (System.Text.Json.JsonSerializer.attr(c, System.Text.Json.Serialization.JsonConstructorAttribute) != null) {
                            chosen = c;
                        }

                        if (c.a === 2) {
                            publics.push(c);
                        }
                    }

                    if (chosen == null && (hasEmpty || declared === 0)) {
                        return { value: Transpose.createInstance(type), consumed: [] };
                    }

                    if (chosen == null && publics.length === 1 && (publics[0].pi || []).length > 0) {
                        chosen = publics[0];
                    }

                    if (chosen == null) {
                        if (type.$kind === "struct") {
                            return { value: Transpose.createInstance(type), consumed: [] };
                        }

                        System.Text.Json.JsonSerializer.fail("Each parameter in the deserialization constructor on type '" + Transpose.getTypeName(type) + "' must bind to an object property or field on deserialization.");
                    }

                    var params   = chosen.pi || [],
                        args     = [],
                        consumed = [];

                    for (var p = 0; p < params.length; p++) {
                        var prm  = params[p],
                            name = System.Text.Json.JsonSerializer.parameterName(type, prm, o),
                            hit  = System.Text.Json.JsonSerializer.lookup(raw, name, o);

                        if (hit.found) {
                            args[p] = System.Text.Json.JsonSerializer.read(hit.value, prm.pt, o, null, (depth || 0) + 1);
                            consumed.push(name);
                        } else {
                            args[p] = Transpose.getDefaultValue(prm.pt);
                        }
                    }

                    return { value: Transpose.Reflection.invokeCI(chosen, args), consumed: consumed };
                },

                // A constructor parameter binds to the member of the same name, so it inherits that
                // member's JSON name — its [JsonPropertyName] and the naming policy alike.
                parameterName: function (type, parameter, o) {
                    var raw        = parameter.sn || parameter.n,
                        properties = System.Text.Json.JsonSerializer.getMembers(type, 16, o),
                        lower      = raw.toLowerCase();

                    for (var i = 0; i < properties.length; i++) {
                        if (properties[i].member.n.toLowerCase() === lower) {
                            return properties[i].name;
                        }
                    }

                    return System.Text.Json.JsonSerializer.convertName(o.naming, raw);
                }
            }
        }
    });
