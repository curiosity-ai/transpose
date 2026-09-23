using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

/// <summary>
/// <c>ref</c> locals and <c>ref</c> returns.
///
/// <para>
/// JavaScript has no by-reference aliases, so a <c>ref</c> is modelled the way a <c>ref</c>/<c>out</c>
/// <i>parameter</i> already is: as an object with a <c>v</c> slot (a "cell"). A parameter's cell is a
/// plain holder the call site writes back from; a <c>ref</c> local or a <c>ref</c> return needs a
/// <i>live</i> one — writing <c>r = 10</c> through <c>ref int r = ref values[0]</c> has to land in
/// <c>values[0]</c> at that moment — so these cells are accessor objects over the referenced location
/// (<c>TransposeR.ref</c>, <c>TransposeR.refElem</c> in the shim). Before this, <c>ref expr</c> simply
/// collapsed to the value of <c>expr</c>: the local held a copy and every write through it was lost,
/// silently.
/// </para>
///
/// <para>
/// The representation is decided per member, never per compilation, because a referencing assembly
/// must agree with the one that emitted the member:
/// </para>
/// <list type="bullet">
/// <item>A ref-returning <b>method</b>, local function or delegate returns the cell. A call is
/// dereferenced (<c>(M()).v</c>) everywhere except where C# itself wants the reference — a
/// <c>ref</c> local's initializer, <c>return ref</c>, a ref reassignment.</item>
/// <item>A ref-returning <b>property</b> or <b>indexer</b> keeps its ordinary read/write shape — a
/// value-typed accessor pair (<c>P</c>, <c>getItem</c>/<c>setItem</c>) that reads and writes through
/// the cell — and additionally exposes the cell itself (<c>$ref$P</c>, <c>getItem$ref</c>), which is
/// what a <c>ref</c> context binds to. That keeps every existing property/indexer read and assignment
/// path unchanged.</item>
/// <item>Members of the Transpose runtime packages (<c>Transpose</c>, <c>Transpose.*</c>) keep the old
/// value semantics. <c>Span&lt;T&gt;</c>'s indexer is a ref-returning indexer the runtime and every
/// published consumer read as a plain value; changing that would break them all.</item>
/// </list>
/// </summary>
public sealed partial class Emitter
{
    /// <summary>The invocation whose ref-returning result must be emitted as the cell itself rather than
    /// dereferenced. Keyed by node identity so it cannot leak into a nested call.</summary>
    private InvocationExpressionSyntax? _cellRequest;

    /// <summary>False while compiling a Transpose runtime package, which keeps value semantics for its
    /// own ref locals and ref returns (see the type summary).</summary>
    private bool RefCellsEnabled => !TransposeNaming.IsRuntimePackage(_compilation.Assembly);

    /// <summary>True if <paramref name="symbol"/> is a ref-returning method, delegate, property or
    /// indexer whose emitted form produces a cell.</summary>
    internal static bool ProducesRefCell(ISymbol? symbol)
    {
        switch (symbol)
        {
            case IMethodSymbol m when m.RefKind is RefKind.Ref or RefKind.RefReadOnly:
                // A property/indexer accessor is reached through the property's value view instead.
                if (m.AssociatedSymbol is not null) return false;
                return IsCellMember(m);
            case IPropertySymbol p when p.RefKind is RefKind.Ref or RefKind.RefReadOnly:
                return IsCellMember(p);
            default:
                return false;
        }
    }

    private static bool IsCellMember(ISymbol member)
    {
        if (TransposeNaming.IsRuntimePackage(member.ContainingAssembly)) return false;
        if (TransposeNaming.AssemblyHasExternalAttribute(member.ContainingAssembly)) return false;
        if (member.ContainingType is { } t && TransposeNaming.IsExternalType(t)) return false;
        var def = member.OriginalDefinition;
        if (TransposeNaming.GetTemplate(def) is not null || TransposeNaming.GetScriptBody(def) is not null) return false;
        if (def is IPropertySymbol { GetMethod: { } g }
            && (TransposeNaming.GetTemplate(g) is not null || TransposeNaming.GetScriptBody(g) is not null)) return false;
        return true;
    }

    /// <summary>True for a <c>ref</c>/<c>ref readonly</c> local, which holds a cell.</summary>
    private bool IsRefLocal(ISymbol? symbol)
        => RefCellsEnabled && symbol is ILocalSymbol { RefKind: not RefKind.None };

    /// <summary>The name of the member that exposes a ref-returning property's cell.</summary>
    internal static string RefCellPropertyName(IPropertySymbol p) => "$ref$" + TransposeNaming.MemberJsName(p);

    /// <summary>The name of the member that exposes a ref-returning indexer's cell.</summary>
    internal static string RefCellIndexerName(IPropertySymbol indexer)
        => TransposeNaming.IndexerAccessorName(indexer, isGet: true) + "$ref";

    /// <summary>
    /// Emits a cell over the storage location <paramref name="target"/> names — the operand of a
    /// <c>ref</c> expression. A location that already is a cell (a ref local, a ref/out parameter's
    /// holder, a ref-returning member) is passed through; anything else gets an accessor object.
    /// </summary>
    private void EmitRefCell(ExpressionSyntax target)
    {
        while (target is ParenthesizedExpressionSyntax p) target = p.Expression;

        switch (target)
        {
            case RefExpressionSyntax nested:
                EmitRefCell(nested.Expression);
                return;

            case ConditionalExpressionSyntax cond:
                // `ref c ? ref a : ref b` — pick the cell, never the value.
                _w.Write("(");
                EmitExpression(cond.Condition);
                _w.Write(" ? ");
                EmitRefCell(cond.WhenTrue);
                _w.Write(" : ");
                EmitRefCell(cond.WhenFalse);
                _w.Write(")");
                return;

            case InvocationExpressionSyntax inv when ProducesRefCell(_model.GetSymbolInfo(inv).Symbol):
                _cellRequest = inv;
                EmitExpression(inv);
                return;
        }

        var symbol = _model.GetSymbolInfo(target).Symbol;

        // A ref local already is a cell, and a ref/out parameter's holder has the same { v } shape.
        if (target is IdentifierNameSyntax id)
        {
            if (IsRefLocal(symbol))
            {
                _w.Write(NameMangler.JsIdentifier(id.Identifier.Text));
                return;
            }
            if (symbol is IParameterSymbol { RefKind: RefKind.Ref or RefKind.Out } param
                && (_inPrimaryCtorBody || !IsCapturedPrimaryCtorParam(param)))
            {
                _w.Write(NameMangler.JsIdentifier(param.Name));
                return;
            }
        }

        // A ref-returning property: its cell is exposed next to its value view.
        if (symbol is IPropertySymbol { IsIndexer: false } prop && ProducesRefCell(prop))
        {
            if (prop.IsStatic) _w.Write(TypeRef(prop.ContainingType) + ".");
            else EmitReceiver(target is MemberAccessExpressionSyntax ma ? ma.Expression : null);
            _w.Write(RefCellPropertyName(prop));
            return;
        }

        if (target is ElementAccessExpressionSyntax element)
        {
            // A ref-returning indexer: call the accessor that hands back the cell.
            if (symbol is IPropertySymbol { IsIndexer: true } indexer && ProducesRefCell(indexer))
            {
                EmitExpression(element.Expression);
                _w.Write("." + RefCellIndexerName(indexer) + "(");
                EmitArgumentList(element.ArgumentList);
                _w.Write(")");
                return;
            }

            // A single-dimensional array element indexed by an integer: evaluate the array and the
            // index once, when the reference is taken, as C# does.
            if (_model.GetTypeInfo(element.Expression).Type is IArrayTypeSymbol { Rank: 1 }
                && element.ArgumentList.Arguments.Count == 1
                && element.ArgumentList.Arguments[0].Expression is { } index
                && !index.IsKind(SyntaxKind.IndexExpression)
                && _model.GetTypeInfo(index).Type?.SpecialType is SpecialType.System_Int32 or SpecialType.System_UInt32
                    or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Byte or SpecialType.System_SByte)
            {
                _w.Write("TransposeR.refElem(");
                EmitExpression(element.Expression);
                _w.Write(", ");
                EmitExpression(index);
                _w.Write(")");
                return;
            }
        }

        // Anything else that C# accepts as a ref target — a local, a field, a multi-dimensional array
        // element — is read and written through a pair of arrows over the location (arrows, so `this`
        // inside them is the enclosing instance). An `await` cannot go inside a plain arrow (it would
        // make the whole bundle fail to parse), and re-running one on every access would be wrong
        // anyway, so a location computed by one is reported rather than emitted.
        if (ContainsAwait(target))
            Unsupported(target, "a ref to a location computed by an await expression");
        _w.Write("TransposeR.ref(() => ");
        EmitExpression(target);
        _w.Write(", ($v) => { ");
        // Through the ordinary assignment path, which knows each location's write form
        // (a multi-dimensional element is System.Array.set, not an assignable expression).
        EmitSimpleAssignmentTo(target, () => _w.Write("$v"));
        _w.Write("; })");
    }
}
