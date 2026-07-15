    Transpose.assemblyVersion = function (assemblyName, version) {
        System.Reflection.Assembly.versions[assemblyName || "Transpose.$Unknown"] = version;
    };

    Transpose.assembly = function (assemblyName, res, callback, restore) {
        if (!callback) {
            callback = res;
            res = {};
        }

        assemblyName = assemblyName || "Transpose.$Unknown";

        var asm = System.Reflection.Assembly.assemblies[assemblyName];

        if (!asm) {
            asm = new System.Reflection.Assembly(assemblyName, res);
        } else {
            Transpose.apply(asm.res, res || {});
        }

        var oldAssembly = Transpose.$currentAssembly;

        Transpose.$currentAssembly = asm;

        if (callback) {
            var old = Transpose.Class.staticInitAllow;
            Transpose.Class.staticInitAllow = false;

            callback.call(Transpose.global, asm, Transpose.global);

            Transpose.Class.staticInitAllow = old;
        }

        Transpose.init();

        if (restore) {
            Transpose.$currentAssembly = oldAssembly;
        }
    };

    Transpose.define("System.Reflection.Assembly", {
        statics: {
            assemblies: {},
            versions: {}
        },

        ctor: function (name, res) {
            this.$initialize();
            this.name = name;
            this.res = res || {};
            this.$types = {};
            this.$ = {};

            System.Reflection.Assembly.assemblies[name] = this;
        },

        toString: function () {
            return this.name;
        },

        getVersion: function () {
            return System.Reflection.Assembly.versions[this.name] || "";
        },

        getManifestResourceNames: function () {
            return Object.keys(this.res);
        },

        getManifestResourceDataAsBase64: function (type, name) {
            if (arguments.length === 1) {
                name = type;
                type = null;
            }

            if (type) {
                name = Transpose.Reflection.getTypeNamespace(type) + "." + name;
            }

            return this.res[name] || null;
        },

        getManifestResourceData: function (type, name) {
            if (arguments.length === 1) {
                name = type;
                type = null;
            }

            if (type) {
                name = Transpose.Reflection.getTypeNamespace(type) + '.' + name;
            }

            var r = this.res[name];

            return r ? System.Convert.fromBase64String(r) : null;
        },

        getCustomAttributes: function (attributeType) {
            if (this.attr && attributeType && !Transpose.isBoolean(attributeType)) {
                return this.attr.filter(function (a) {
                    return Transpose.is(a, attributeType);
                });
            }

            return this.attr || [];
        }
    });

    Transpose.$currentAssembly = new System.Reflection.Assembly("mscorlib");
    Transpose.SystemAssembly = Transpose.$currentAssembly;
    Transpose.SystemAssembly.$types["System.Reflection.Assembly"] = System.Reflection.Assembly;
    System.Reflection.Assembly.$assembly = Transpose.SystemAssembly;

    var $asm = Transpose.$currentAssembly;
