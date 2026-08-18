    // ---- lazily-loaded modules -------------------------------------------------------------
    //
    // A build that emits its types as separate JavaScript modules registers a manifest here. Every
    // type it did not load up front gets a *stub* standing in its place, at the same global path
    // and in the same assembly $types map a real Transpose.define would use. That keeps reflection
    // whole while the code stays unfetched: Assembly.GetTypes(), Type.Name, IsInterface,
    // IsAssignableFrom and the eagerly-emitted reflection metadata all see the type.
    //
    // Actually *using* the type is what forces the fetch, and fetching a module is asynchronous, so
    // it is only reachable through the async API — Transpose.Modules.load /
    // Transpose.createInstanceAsync, surfaced in C# as Transpose.Modules and
    // Activator.CreateInstanceAsync. A synchronous Activator.CreateInstance on a stub throws naming
    // the module rather than failing obscurely somewhere inside the constructor.

    var modules = {
        // type name -> { m: module url, k: kind, a: assembly name, i: [base/interface specs] }
        $manifest: {},
        $stubs: {},
        // type name -> the `i` specs, kept so a stub's bases can be resolved on demand rather than
        // at registration (see $defineLazyBases)
        $baseSpecs: {},
        // module url -> the in-flight (or settled) load, so concurrent loads share one fetch
        $pending: {},
        $loader: null,

        /// Replaces how a module url is fetched. The default uses a dynamic import(); a host that
        /// serves its chunks another way (a test, a bundler runtime, a non-ESM page) sets its own.
        setLoader: function (loader) {
            modules.$loader = loader;
        },

        /// Declares the types that live in not-yet-loaded modules. Safe to call more than once —
        /// each assembly's chunk manifest registers itself as it is read.
        register: function (manifest) {
            // Outermost first: a nested type is placed *onto* its container, so the container's
            // stub has to exist before it. Placing the nested one first would create a plain object
            // at the container's path that the container's own stub then overwrites, silently
            // losing the nested type.
            var names = [];
            for (var n in manifest) {
                if (Object.prototype.hasOwnProperty.call(manifest, n)) names.push(n);
            }
            names.sort(function (a, b) { return a.split(".").length - b.split(".").length; });

            var added = [];
            for (var i = 0; i < names.length; i++) {
                var name = names[i];
                if (modules.$stubs[name] || Transpose.unroll(name)) continue;   // already real, or already stubbed
                var info = manifest[name];
                modules.$manifest[name] = info;
                modules.$baseSpecs[name] = info.i || [];
                var stub = modules.$makeStub(name, info);
                modules.$stubs[name] = stub;
                modules.$place(name, stub);
                var asm = System.Reflection.Assembly.assemblies[info.a];
                if (!asm) asm = new System.Reflection.Assembly(info.a, {});
                asm.$types[name] = stub;
                added.push(name);
            }

            // The inheritance chains resolve on FIRST READ rather than here. Two reasons: a base may
            // be another stub whose own bases are not registered yet (this loop used to run second
            // to work around that), and a CONSTRUCTED generic base cannot be built at all until its
            // definition's module has arrived — applying a stub throws. IsAssignableFrom walks
            // $$inherits, so deferring the resolution is what lets a
            // GetTypes().Where(t => typeof(IFoo<Bar>).IsAssignableFrom(t)) scan answer correctly.
            for (var j = 0; j < added.length; j++) {
                modules.$defineLazyBases(modules.$stubs[added[j]], added[j]);
            }

            // Metadata registered before the stub existed was deferred; give it another chance.
            Transpose.init();
        },

        /// Gives a stub its $$inherits / $interfaces / $allInterfaces, computed the first time
        /// anything reads one and frozen only once every base resolved. A partial answer is never
        /// cached: a base whose module has not arrived resolves to nothing now and to the real type
        /// later, so the next question has to recompute rather than see a stale list.
        ///
        /// Non-enumerable on purpose. Class.set walks `for (key in exists)` when a real define takes
        /// a retired stub's slot, and an enumerable accessor would be invoked by that walk — forcing
        /// the resolution at exactly the moment the type is mid-definition.
        $defineLazyBases: function (stub, name) {
            var resolve = function (wantInterfaces) {
                var r = modules.$resolveBases(name);
                if (r.complete) {
                    modules.$freeze(stub, "$$inherits", r.inherits);
                    modules.$freeze(stub, "$interfaces", r.interfaces);
                    modules.$freeze(stub, "$allInterfaces", r.interfaces);
                }
                return wantInterfaces ? r.interfaces : r.inherits;
            };
            modules.$lazy(stub, "$$inherits", function () { return resolve(false); });
            modules.$lazy(stub, "$interfaces", function () { return resolve(true); });
            // getInterfaces() reads $allInterfaces and answers [] without it, so Type.GetInterfaces()
            // on a deferred type reported nothing at all before this.
            modules.$lazy(stub, "$allInterfaces", function () { return resolve(true); });
        },

        $lazy: function (obj, prop, get) {
            Object.defineProperty(obj, prop, { get: get, configurable: true, enumerable: false });
        },

        $freeze: function (obj, prop, value) {
            Object.defineProperty(obj, prop, { value: value, configurable: true, enumerable: false });
        },

        /// The base class and interfaces a stub reports, resolved from its manifest specs.
        /// <c>complete</c> is false when any of them could not be resolved yet.
        $resolveBases: function (name) {
            var specs = modules.$baseSpecs[name] || [], inherits = [], ifaces = [], complete = true;

            var push = function (list, t) { if (t && list.indexOf(t) < 0) list.push(t); };

            for (var i = 0; i < specs.length; i++) {
                var b = modules.$resolveType(specs[i], true);
                if (!b) { complete = false; continue; }
                push(inherits, b);
                if (b.$isInterface) push(ifaces, b);
                // A base's own interfaces come along — reading them may resolve ANOTHER stub's
                // bases, which terminates because an inheritance graph has no cycles.
                var bi = b.$interfaces || [];
                for (var k = 0; k < bi.length; k++) push(ifaces, bi[k]);
                var bh = b.$$inherits || [];
                for (var q = 0; q < bh.length; q++) {
                    if (bh[q].$isInterface) push(ifaces, bh[q]);
                }
            }
            return { inherits: inherits, interfaces: ifaces, complete: complete };
        },

        /// Resolves one manifest spec: a dotted name, or [definition, ...arguments] for a
        /// constructed generic. Null when it cannot be resolved *yet*, which the caller treats as
        /// "ask again later" rather than "no such type".
        $resolveType: function (spec, basePosition) {
            if (typeof spec === "string") {
                var named = Transpose.unroll(spec) || null;
                // A generic base named WITHOUT arguments is an OPEN one — `class Relay<T> :
                // IHandler<T>`, where there is no T to write down until the definition is applied,
                // so the manifest can only carry the definition's name. The loaded form is not the
                // definition either: $staticInit applies it to placeholder type parameters, so a
                // stub has to do the same or it answers differently from the type it stands in for
                // (IsAssignableFrom(IFoo<>, ...) would match on the bare definition, and
                // GetInterfaces() would skip it — a definition object carries $kind "class"
                // whether or not it defines an interface).
                if (basePosition && named && !named.$stub && named.$isGenericTypeDefinition) {
                    return modules.$applyOpen(named) || named;
                }
                return named;
            }
            if (!spec || !spec.length) return null;

            var def = Transpose.unroll(spec[0]);
            // A constructed generic is built by APPLYING its definition, and a stub throws when
            // applied — so this one stays unresolved until that definition's module arrives. (A
            // caller able to ask about the instantiation at all has already loaded it: writing
            // typeof(IFoo<Bar>) emits the same application.)
            if (!def || def.$stub || !Transpose.isFunction(def)) return null;
            if (spec.length === 1) return def;
            if (!def.$isGenericTypeDefinition) return def;

            var args = [];
            for (var i = 1; i < spec.length; i++) {
                var a = modules.$resolveType(spec[i]);
                if (!a) return null;
                args.push(a);
            }
            return def.apply(null, args);
        },

        /// Applies a generic definition to its own placeholder type parameters, which is the shape
        /// a loaded type records for an open base. $typeArguments is what $staticInit builds (from
        /// Reflection.createTypeParams, reading the definition function's parameter names); reading
        /// the definition through Transpose.unroll normally triggers that, so this only forces it
        /// when something reached the definition another way. Null when it cannot be produced, and
        /// the caller then falls back to the bare definition rather than dropping the base.
        $applyOpen: function (def) {
            if (!def.$typeArguments && def.$staticInit) def.$staticInit();
            var params = def.$typeArguments;
            if (!params || !params.length) return null;
            return def.apply(null, params);
        },

        isStub: function (type) {
            return !!(type && type.$stub);
        },

        /// True when the type's code is present — either it was never lazy, or its module has been
        /// loaded. False only for a stub.
        isLoaded: function (type) {
            if (typeof type === "string") type = Transpose.unroll(type);
            return !!type && !type.$stub;
        },

        /// Fetches the module holding <c>type</c> if it is still a stub, and resolves with the real
        /// type. Resolving with an already-loaded type is a no-op, so this is safe to await
        /// unconditionally.
        load: function (type) {
            var name = typeof type === "string" ? type : (type && type.$$name);
            var info = name && modules.$manifest[name];
            if (!info) {
                // Either never deferred, or its module has already been loaded. Resolve by NAME so
                // a caller still holding the stub it got from Type.GetType() before the load is
                // handed the live type, rather than its own stale stub back.
                var already = name ? Transpose.unroll(name) : null;
                return Promise.resolve(already || (typeof type === "string" ? null : type));
            }
            return modules.$loadModule(info.m).then(function () {
                return Transpose.unroll(name);
            });
        },

        $loadModule: function (url) {
            if (modules.$pending[url]) return modules.$pending[url];

            var loader = modules.$loader || modules.$defaultLoader;
            var p;
            try {
                // toPromise takes whatever the loader hands back — a native Promise, a C# Task, or
                // nothing at all for a loader that resolved synchronously.
                p = Promise.resolve(Transpose.toPromise(loader(url)));
            } catch (e) {
                p = Promise.reject(e);
            }

            p = p.then(function () {
                // The module's types are defined now — each retired its own stub as it was defined.
                // Run the queued static initializers and let any metadata that was deferred while
                // they were stubs attach to the real types.
                Transpose.init();
                return true;
            }, function (err) {
                // Nothing was evicted, so a failed fetch leaves the stubs exactly as they were and
                // the type stays visible; just allow another attempt.
                delete modules.$pending[url];
                throw err;
            });

            modules.$pending[url] = p;
            return p;
        },

        $defaultLoader: function (url) {
            // Built with new Function so the dynamic import() is only ever compiled in an engine
            // that is actually asked to load a module — tps.js itself stays parseable everywhere.
            if (!modules.$import) {
                try {
                    modules.$import = new Function("u", "return import(u);");
                } catch (e) {
                    modules.$import = function () {
                        return Promise.reject(new System.NotSupportedException.$ctor1(
                            "This JavaScript engine cannot import() a module; set Transpose.Modules.setLoader."));
                    };
                }
            }
            return modules.$import(url);
        },

        /// Retires the stub occupying <c>name</c> so the real Transpose.define can take the slot.
        ///
        /// The stub object is left in place rather than deleted: Class.set copies the members of
        /// whatever previously occupied the slot onto the new class, which is how a nested type
        /// registered onto the stub survives — and it has to survive *before* the define resolves
        /// `inherits`, which a delete-then-restore-later could not manage (a type whose own base
        /// mentions its nested type, e.g. Nav : ...&lt;Nav.NavLink&gt;, would see it undefined).
        /// Only the identity markers are cleared, so the "already defined" check does not fire.
        $replaceStub: function (name) {
            var at = modules.$slot(name);
            var stub = at.scope && at.scope[at.leaf];
            if (!stub || !stub.$stub) return;
            // $$name is deliberately kept: a caller that grabbed this stub from Type.GetType() before
            // the load still has to be resolvable by name afterwards (Modules.load hands them the live
            // type). Only the stub markers are cleared, plus a flag telling Class.set to step aside.
            stub.$stub = false;
            stub.$retiredStub = true;
            var info = modules.$manifest[name];
            var asm = info && System.Reflection.Assembly.assemblies[info.a];
            if (asm) delete asm.$types[name];
        },

        /// Hands the metadata the stub was holding to the real type, once the define registered it.
        $stubReplaced: function (name, real) {
            if (!real || real.$stub) return;
            // Keyed by NAME rather than taken off the stub: the stub may have been retired by a
            // different route than the one replacing it now (the loader, or a plain static import
            // of the chunk from another chunk).
            if (!real.$metadata && modules.$metaFor[name]) {
                real.$metadata = modules.$metaFor[name];
                real.$getMetadata = Transpose.Reflection.getMetadata;
                delete modules.$metaFor[name];
            }
            delete modules.$manifest[name];
            delete modules.$stubs[name];
            delete modules.$baseSpecs[name];
        },

        /// Metadata captured while a type was a stub, keyed by type name (see Reflection.setMetadata).
        $metaFor: {},

        $makeStub: function (name, info) {
            var fn = function () {
                throw new System.InvalidOperationException.$ctor1(
                    "Type '" + name + "' lives in module '" + info.m + "', which has not been loaded. " +
                    "Await Transpose.Modules.Load(type) or Activator.CreateInstanceAsync(type) first.");
            };
            fn.$$name = name;
            fn.$stub = true;
            fn.$module = info.m;
            fn.$kind = info.k || "class";
            if (fn.$kind === "interface") fn.$isInterface = true;
            fn.prototype = { constructor: fn };
            fn.$assembly = System.Reflection.Assembly.assemblies[info.a];
            return fn;
        },

        $place: function (name, value) {
            var parts = name.split("."), scope = Transpose.global;
            for (var i = 0; i < parts.length - 1; i++) {
                scope = scope[parts[i]] || (scope[parts[i]] = {});
            }
            var leaf = parts[parts.length - 1];
            var existing = scope[leaf];
            if (typeof existing === "function") return;             // a real type already lives here
            if (existing) {                                          // carry over nested types placed earlier
                for (var k in existing) {
                    if (Object.prototype.hasOwnProperty.call(existing, k)) value[k] = existing[k];
                }
            }
            scope[leaf] = value;
        },

        $slot: function (name) {
            var parts = name.split("."), scope = Transpose.global;
            for (var i = 0; i < parts.length - 1 && scope; i++) scope = scope[parts[i]];
            return { scope: scope, leaf: parts[parts.length - 1] };
        }
    };

    Transpose.Modules = modules;

    /// Names the assembly that a bare Transpose.define registers into. A single-bundle build gets
    /// this from the Transpose.assembly(...) wrapper it is emitted inside; a per-chunk module has no
    /// wrapper, so it names its assembly itself before defining anything.
    Transpose.$useAssembly = function (name) {
        var asm = System.Reflection.Assembly.assemblies[name];
        if (!asm) asm = new System.Reflection.Assembly(name, {});
        Transpose.$currentAssembly = asm;
        return asm;
    };

    /// Activator.CreateInstance's asynchronous form: loads the type's module if it is still a stub,
    /// then constructs. This is the only way to instantiate a type a build deferred, because
    /// fetching a module cannot be made synchronous.
    Transpose.createInstanceAsync = function (type, nonPublic, args) {
        return modules.load(type).then(function (real) {
            return Transpose.createInstance(real || type, nonPublic, args);
        });
    };
