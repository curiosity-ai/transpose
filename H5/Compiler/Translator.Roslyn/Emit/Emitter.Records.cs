using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace H5.Translator.Roslyn;

public sealed partial class Emitter
{
    /// <summary>
    /// Emits the synthesized members of a record: positional properties, the primary
    /// constructor, and value-based Equals/GetHashCode/ToString/Deconstruct.
    /// </summary>
    private void EmitRecordMembers(INamedTypeSymbol type, string simpleName)
    {
        static bool IsRecordCopyConstructor(IMethodSymbol c, INamedTypeSymbol type)
            => c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, type);

        var recordDecl = type.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<RecordDeclarationSyntax>()
            .FirstOrDefault();

        var positional = recordDecl?.ParameterList?.Parameters.ToList() ?? new List<ParameterSyntax>();
        var paramNames = positional.Select(p => NameMangler.JsIdentifier(p.Identifier.Text)).ToList();

        // Positional properties (auto, backed by __Name).
        foreach (var name in paramNames)
        {
            _w.Write($"Object.defineProperty({simpleName}.prototype, \"{name}\", ");
            _w.Block(() =>
            {
                _w.WriteLine($"get: function () {{ return this.__{name}; }},");
                _w.WriteLine($"set: function (value) {{ this.__{name} = value; }},");
                _w.WriteLine("enumerable: true, configurable: true");
            });
            _w.WriteLine(");");
        }

        // Primary constructor — emitted under the name overload-resolution assigns it
        // (a record also has a synthesized copy constructor).
        var primaryCtor = type.InstanceConstructors.FirstOrDefault(c => !IsRecordCopyConstructor(c, type));
        var ctorName = primaryCtor is not null ? _names.MethodName(primaryCtor) : "$ctor";

        _w.Write($"{simpleName}.prototype.{ctorName} = function ({string.Join(", ", paramNames)}) ");
        _w.Block(() =>
        {
            _w.WriteLine("this.$ctorInit();");
            var baseType = type.BaseType;
            if (baseType is not null && baseType.SpecialType != SpecialType.System_Object
                && baseType.TypeKind != TypeKind.Error)
            {
                _w.WriteLine($"{_names.TypeReference(baseType)}.prototype.$ctor.call(this);");
            }
            foreach (var name in paramNames)
            {
                _w.WriteLine($"this.__{name} = {name};");
            }
        });
        _w.WriteLine(";");

        // Value members over all readable instance properties.
        var valueProps = type.GetMembers().OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && p.GetMethod is not null && !p.IsIndexer && p.Name != "EqualityContract")
            .Select(p => _names.PropertyName(p))
            .Distinct()
            .ToList();

        // Equals
        _w.Write($"{simpleName}.prototype.Equals = function (o) ");
        _w.Block(() =>
        {
            _w.WriteLine("if (o == null || o.constructor !== this.constructor) { return false; }");
            if (valueProps.Count == 0)
            {
                _w.WriteLine("return true;");
            }
            else
            {
                _w.Write("return ");
                _w.Write(string.Join(" && ", valueProps.Select(p => $"H5R.equals(this.{p}, o.{p})")));
                _w.WriteLine(";");
            }
        });
        _w.WriteLine(";");

        // GetHashCode
        _w.Write($"{simpleName}.prototype.GetHashCode = function () ");
        _w.Block(() =>
        {
            _w.WriteLine("var h = 17;");
            foreach (var p in valueProps)
            {
                _w.WriteLine($"h = (h * 31 + H5R.hash(this.{p})) | 0;");
            }
            _w.WriteLine("return h;");
        });
        _w.WriteLine(";");

        // ToString: "TypeName { A = .., B = .. }"
        _w.Write($"{simpleName}.prototype.ToString = function () ");
        _w.Block(() =>
        {
            if (valueProps.Count == 0)
            {
                _w.WriteLine($"return \"{simpleName} {{ }}\";");
            }
            else
            {
                var parts = valueProps.Select(p => $"\"{p} = \" + H5R.toStr(this.{p})");
                _w.WriteLine($"return \"{simpleName} {{ \" + {string.Join(" + \", \" + ", parts)} + \" }}\";");
            }
        });
        _w.WriteLine(";");

        // Deconstruct for positional records.
        if (paramNames.Count > 0)
        {
            var holders = paramNames.Select((_, i) => $"$p{i}").ToList();
            _w.Write($"{simpleName}.prototype.Deconstruct = function ({string.Join(", ", holders)}) ");
            _w.Block(() =>
            {
                for (var i = 0; i < paramNames.Count; i++)
                {
                    _w.WriteLine($"{holders[i]}.v = this.{paramNames[i]};");
                }
            });
            _w.WriteLine(";");
        }
    }
}
