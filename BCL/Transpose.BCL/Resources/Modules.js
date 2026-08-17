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
        // type name -> { m: module url, k: kind, a: assembly name, i: [base/interface type names] }
        $manifest: {},
        $stubs: {},
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
                var stub = modules.$makeStub(name, info);
                modules.$stubs[name] = stub;
                modules.$place(name, stub);
                var asm = System.Reflection.Assembly.assemblies[info.a];
                if (!asm) asm = new System.Reflection.Assembly(info.a, {});
                asm.$types[name] = stub;
                added.push(name);
            }

            // Resolve the inheritance chains only once every stub exists — a stub may extend
            // another stub. IsAssignableFrom walks $$inherits, so this is what makes a
            // GetTypes().Where(t => typeof(IFoo).IsAssignableFrom(t)) scan work unloaded.
            for (var j = 0; j < added.length; j++) {
                var s = modules.$stubs[added[j]], list = [], ifaces = [];
                var bases = modules.$manifest[added[j]].i || [];
                for (var k = 0; k < bases.length; k++) {
                    var b = Transpose.unroll(bases[k]);
                    if (!b) continue;
                    list.push(b);
                    if (b.$isInterface) ifaces.push(b);
                    if (b.$interfaces) ifaces = ifaces.concat(b.$interfaces);
                    if (b.$$inherits) {
                        for (var q = 0; q < b.$$inherits.length; q++) {
                            if (b.$$inherits[q].$isInterface) ifaces.push(b.$$inherits[q]);
                        }
                    }
                }
                s.$$inherits = list;
                s.$interfaces = ifaces;
            }

            // Metadata registered before the stub existed was deferred; give it another chance.
            Transpose.init();
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

            // Every type this module owns has to give up its global slot before the module's
            // Transpose.define calls run, or define reports "Class X is already defined".
            var owned = [], saved = {};
            for (var n in modules.$manifest) {
                if (modules.$manifest[n].m === url) owned.push(n);
            }
            for (var i = 0; i < owned.length; i++) saved[owned[i]] = modules.$evict(owned[i]);

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
                // The module's types are defined now; run the queued static initializers and let
                // any metadata that was deferred while they were stubs attach to the real types.
                Transpose.init();
                for (var k = 0; k < owned.length; k++) modules.$adopt(owned[k], saved[owned[k]]);
                return true;
            }, function (err) {
                // A failed fetch must not leave the type missing: put the stubs back and let the
                // next attempt try again.
                for (var k = 0; k < owned.length; k++) modules.$restore(owned[k], saved[owned[k]]);
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

        /// Steps a stub aside so the real Transpose.define can take its place, remembering what has
        /// to be carried over. Called from Class.js at define time, which catches every route a
        /// chunk can arrive by — the loader below, or a plain static import from another chunk.
        $replaceStub: function (name) {
            var saved = modules.$evict(name);
            if (saved) modules.$adoptPending[name] = saved;
        },

        /// Hands the stub's metadata and any nested types to the real type. Called right after the
        /// define registered it.
        $stubReplaced: function (name, real) {
            if (!real || real.$stub) return;
            var saved = modules.$adoptPending[name];
            if (saved) {
                for (var k in saved.carry) {
                    if (real[k] === undefined) real[k] = saved.carry[k];
                }
                delete modules.$adoptPending[name];
            }
            // The metadata comes from the name-keyed record rather than the evicted stub: the stub
            // may have been taken out by a different route than the one replacing it now.
            if (!real.$metadata && modules.$metaFor[name]) {
                real.$metadata = modules.$metaFor[name];
                real.$getMetadata = Transpose.Reflection.getMetadata;
                delete modules.$metaFor[name];
            }
            delete modules.$manifest[name];
            delete modules.$stubs[name];
        },

        $adoptPending: {},

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
        },

        $evict: function (name) {
            var at = modules.$slot(name);
            var stub = at.scope && at.scope[at.leaf];
            if (!stub || !stub.$stub) return null;
            // Anything hung off the stub (nested types registered onto it) has to survive onto the
            // real type, and so does the metadata that attached while it stood in.
            var carry = {};
            for (var k in stub) {
                if (Object.prototype.hasOwnProperty.call(stub, k) && k.charAt(0) !== "$") carry[k] = stub[k];
            }
            delete at.scope[at.leaf];
            var asm = System.Reflection.Assembly.assemblies[modules.$manifest[name].a];
            if (asm) delete asm.$types[name];
            return { stub: stub, carry: carry, metadata: stub.$metadata, getMetadata: stub.$getMetadata };
        },

        $restore: function (name, saved) {
            if (!saved) return;
            var at = modules.$slot(name);
            if (at.scope && !at.scope[at.leaf]) at.scope[at.leaf] = saved.stub;
            var asm = System.Reflection.Assembly.assemblies[modules.$manifest[name].a];
            if (asm) asm.$types[name] = saved.stub;
        },

        $adopt: function (name, saved) {
            var real = Transpose.unroll(name);
            // Class.js may already have swapped the stub out at define time and carried everything
            // over; nothing left to do then.
            if (real && !real.$stub && !modules.$adoptPending[name] && !modules.$manifest[name]) return;
            if (!real || real.$stub) {
                // The module did not actually define it — leave the stub in place so the error the
                // caller eventually sees still names the module.
                modules.$restore(name, saved);
                return;
            }
            if (saved) {
                for (var k in saved.carry) {
                    if (real[k] === undefined) real[k] = saved.carry[k];
                }
                if (!real.$metadata && saved.metadata) {
                    real.$metadata = saved.metadata;
                    real.$getMetadata = saved.getMetadata || Transpose.Reflection.getMetadata;
                }
            }
            delete modules.$manifest[name];
            delete modules.$stubs[name];
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
