using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Transpose.Translator;

/// <summary>
/// <c>long</c>/<c>ulong</c> values that live in FOREIGN JavaScript.
///
/// <para>
/// tps.js models a 64-bit integer as a <c>System.Int64</c>/<c>System.UInt64</c> OBJECT — a pair of
/// 32-bit words with <c>.add</c>/<c>.gt</c>/<c>.toNumber</c> methods — because JavaScript has no
/// 64-bit number. That model holds for every value Transpose itself produces, and for the base
/// library, which is where those two types are DEFINED (<c>DateTime.Ticks</c>, <c>long.Parse</c>,
/// <c>Stopwatch.ElapsedTicks</c> … all hand back real instances).
/// </para>
///
/// <para>
/// It does NOT hold for a slot that belongs to real JavaScript. A binding library declares
/// <c>Blob.size</c> as <c>ulong</c> because that is the closest C# type for what the spec calls an
/// unsigned long long — but the browser hands back a plain JS <c>number</c>, and no amount of C#
/// typing changes that. The same is true of an <c>[ObjectLiteral]</c> type, whose instances *are*
/// plain JS objects (that is the whole point: they cross into JSON and into hand-written JS).
/// Treating those as boxed made <c>file.Size &gt; 0</c> emit <c>file.size.gt(…)</c> — a TypeError,
/// since a number has no <c>.gt</c> — and, in the other direction, passed an <c>Int64</c> object
/// into <c>blob.slice(…)</c>, which coerces it to <c>NaN</c>.
/// </para>
///
/// <para>
/// So such a slot is <i>plain</i>: reads of it, and arithmetic/comparison between plain operands,
/// stay plain JS numbers, and the conversion happens at the boundary — lifted with
/// <c>System.Int64(…)</c> when the value enters a managed <c>long</c> slot (a local, a field, a
/// non-foreign parameter, a box), and read back with <c>.toNumber()</c> when a managed value is
/// written into a foreign one.
/// </para>
///
/// <para>
/// Two edges are deliberate. <b>Bitwise and shift</b> operators keep the boxed path — JavaScript's
/// are 32-bit, so <c>size &amp; 0xF00000000</c> would silently lose the high word — and so does a
/// <b>constant outside ±2^53</b>, which keeps <c>size == long.MaxValue</c> exact. And the cost: a
/// value stored INTO a foreign slot above 2^53 rounds, because a JS number counts in ones only that
/// far. For an external slot nothing is lost — the value arrived as a number. For an
/// <c>[ObjectLiteral]</c> it is a real trade, taken because the alternative put a
/// <c>{low, high}</c> object into an object whose whole purpose is to be read by hand-written
/// JavaScript and serialized to JSON.
/// </para>
/// </summary>
public sealed partial class Emitter
{
    /// <summary>The base library's assembly name — the one assembly whose <c>long</c>/<c>ulong</c>
    /// externs really are backed by System.Int64/UInt64, because it defines them.</summary>
    private const string BaseLibraryAssemblyName = "Transpose";

    /// <summary>
    /// True if a <c>long</c>/<c>ulong</c> member holds a PLAIN JS number rather than a
    /// System.Int64/UInt64 instance, because the slot itself lives in foreign JavaScript. See the
    /// type comment for why. The member's own type is not checked here — callers pair this with
    /// <see cref="Is64BitInteger"/> on whichever type the member contributes.
    /// </summary>
    internal static bool IsForeignJsSlot(ISymbol? member)
    {
        if (member is null) return false;

        // An [ObjectLiteral] instance is a plain JS object in every assembly, the base library
        // included: its slots are read by hand-written JavaScript and serialized to JSON. Only the
        // SLOTS — an instance field or property. A method declared on such a type is ordinary
        // transpiled C# whose parameters are managed like any other; unwrapping an argument into one
        // would hand its body a bare number where it expects an Int64.
        var containing = member.ContainingType;
        if (member is IFieldSymbol { IsStatic: false } or IPropertySymbol { IsStatic: false }
            && IsObjectLiteralType(containing)) return true;

        // The base library defines System.Int64/UInt64 and its runtime primitives return real
        // instances, so its externs are boxed however they are declared.
        if (containing?.ContainingAssembly?.Name == BaseLibraryAssemblyName) return false;

        // A binding library ([assembly: External], or a [Scope]-projected DOM type), or a single
        // member bound to hand-written JS by [External]/[Template].
        if (TransposeNaming.IsExternalType(containing)) return true;
        if (TransposeNaming.HasAttr(member, TransposeNaming.ExternalAttr)) return true;
        if (TransposeNaming.GetTemplate(member.OriginalDefinition) is not null) return true;
        return member is IPropertySymbol { GetMethod: { } getter }
               && TransposeNaming.GetTemplate(getter.OriginalDefinition) is not null;
    }

    /// <summary>The value type a member contributes to an expression: a property's/field's type or a
    /// method's return type. Null for anything else (a local, a type, a namespace).</summary>
    private static ITypeSymbol? ReadValueType(ISymbol symbol) => symbol switch
    {
        IPropertySymbol p => p.Type,
        IFieldSymbol f => f.Type,
        IMethodSymbol m => m.ReturnType,
        _ => null,
    };

    /// <summary>
    /// True if this expression's C# type is <c>long</c>/<c>ulong</c> but its emitted JavaScript is a
    /// plain number — the value came out of foreign JavaScript and was never boxed. Purely
    /// syntactic: a value that reaches a managed <c>long</c> slot is lifted at that boundary
    /// (<c>EmitExpressionConverted</c>), so there is nothing to track across statements.
    /// </summary>
    private bool IsForeignJs64Value(ExpressionSyntax expr)
    {
        switch (expr)
        {
            case ParenthesizedExpressionSyntax paren:
                return IsForeignJs64Value(paren.Expression);

            // `-x` / `+x` on a plain operand stays plain (EmitPrefixUnary emits the JS operator).
            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.UnaryMinusExpression or (int)SyntaxKind.UnaryPlusExpression } unary:
                return IsForeignJs64Value(unary.Operand);

            // A ternary is never plain: each branch is emitted through the conversion path against
            // the ternary's own 64-bit type, which lifts a plain branch — so the result is a boxed
            // instance whichever branch runs. (Claiming otherwise would be worse than boxing: the
            // two halves would disagree about the representation.)
            case ConditionalExpressionSyntax:
                return false;

            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } coalesce:
                return Is64BitInteger(UnwrapNullable(_model.GetTypeInfo(coalesce).Type))
                       && IsPlain64CoalesceOperand(coalesce.Left)
                       && IsPlain64CoalesceOperand(coalesce.Right);

            case BinaryExpressionSyntax binary:
                return IsPlain64BinaryResult(binary);

            // A cast to a 64-bit type builds a real instance (`System.Int64(x)`); only the identity
            // cast — `(ulong)someForeignUlong` — is erased and stays plain.
            case CastExpressionSyntax cast:
                return SymbolEqualityComparer.Default.Equals(
                           _model.GetTypeInfo(cast.Type).Type, _model.GetTypeInfo(cast.Expression).Type)
                       && EmitsAsPlainJsNumber(cast.Expression);

            // `x?.Size` — the continuation carries the receiver's plainness.
            case ConditionalAccessExpressionSyntax conditionalAccess:
                return IsForeignJs64Value(conditionalAccess.WhenNotNull);
        }

        var symbol = _model.GetSymbolInfo(expr).Symbol;
        if (symbol is null) return false;

        // `foreignNullable.Value` / `.GetValueOrDefault()` unwrap a Nullable<long> in place, so they
        // are as plain as their receiver. (Nullable<T> itself lives in the base library.)
        if (symbol.ContainingType is { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
            && symbol.Name is "Value" or "GetValueOrDefault")
        {
            var receiver = expr switch
            {
                MemberAccessExpressionSyntax ma => ma.Expression,
                InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax ima } => ima.Expression,
                _ => null,
            };
            return receiver is not null && EmitsAsPlainJsNumber(receiver);
        }

        return Is64BitInteger(UnwrapNullable(ReadValueType(symbol))) && IsForeignJsSlot(symbol);
    }

    /// <summary>
    /// True if a binary operator over 64-bit operands yields a plain JS number: every 64-bit operand
    /// is itself plain, and the operator is one <see cref="EmitPlain64Binary"/> emits with plain JS.
    /// An operator whose operands are all NARROW but whose result C# promoted to 64-bit
    /// (<c>int + uint</c>) is excluded — that must still build a real Int64, which is what the
    /// boxed path does.
    /// </summary>
    private bool IsPlain64BinaryResult(BinaryExpressionSyntax binary)
    {
        if (!IsPlain64Operator(binary)) return false;
        if (!Is64BitInteger(UnwrapNullable(_model.GetTypeInfo(binary).Type))) return false;

        var left = UnwrapNullable(_model.GetTypeInfo(binary.Left).Type);
        var right = UnwrapNullable(_model.GetTypeInfo(binary.Right).Type);
        if (IsFloatingType(left) || IsFloatingType(right)) return false;
        if (IsDecimalType(left) || IsDecimalType(right)) return false;

        if (!IsPlain64SafeOperand(binary.Left, left) || !IsPlain64SafeOperand(binary.Right, right)) return false;

        var anyPlain64 = false;
        if (Is64BitInteger(left))
        {
            if (!EmitsAsPlainJsNumber(binary.Left)) return false;
            anyPlain64 = true;
        }
        if (Is64BitInteger(right))
        {
            if (!EmitsAsPlainJsNumber(binary.Right)) return false;
            anyPlain64 = true;
        }
        return anyPlain64;
    }

    /// <summary>
    /// The operators a plain 64-bit value keeps in plain JavaScript. Bitwise and shift are absent on
    /// purpose: JavaScript's bitwise operators work on 32 bits, so <c>size &amp; 0xF00000000</c>
    /// would silently lose the high word — those go through the boxed Int64 path, where the width is
    /// real. Comparisons are listed even though their result is a <c>bool</c>: the flag they answer
    /// is "can this be emitted with a JS operator", not "is the result 64-bit".
    /// </summary>
    private static bool IsPlain64Operator(BinaryExpressionSyntax binary) => binary.Kind() is
        SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or SyntaxKind.MultiplyExpression
        or SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression
        or SyntaxKind.LessThanExpression or SyntaxKind.GreaterThanExpression
        or SyntaxKind.LessThanOrEqualExpression or SyntaxKind.GreaterThanOrEqualExpression
        or SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression;

    /// <summary>
    /// Emits a binary operator over plain 64-bit operands with plain JavaScript. Division is the one
    /// operator JS gets wrong for integers (it produces a fraction), so it routes through the same
    /// truncating helper the 32-bit integer path uses; no 32-bit clip is applied, because the value
    /// is not 32 bits.
    /// </summary>
    private void EmitPlain64Binary(BinaryExpressionSyntax binary)
    {
        if (binary.IsKind(SyntaxKind.DivideExpression))
        {
            _w.Write("TransposeR.idiv(");
            EmitPlain64Operand(binary.Left);
            _w.Write(", ");
            EmitPlain64Operand(binary.Right);
            _w.Write(")");
            return;
        }

        var jsOp = binary.Kind() switch
        {
            SyntaxKind.EqualsExpression => "===",
            SyntaxKind.NotEqualsExpression => "!==",
            _ => binary.OperatorToken.Text,
        };

        EmitPlain64Operand(binary.Left);
        _w.Write($" {jsOp} ");
        EmitPlain64Operand(binary.Right);
    }

    /// <summary>
    /// Emits an operand in plain (unboxed) position. Only constants need the detour: a numeric
    /// literal is emitted from its CONVERTED type (see <c>EmitLiteral</c>), so the <c>1</c> in
    /// <c>file.Size + 1</c> — which C# converted to <c>ulong</c> — would come out as
    /// <c>System.UInt64("1")</c> and turn a plain addition into string concatenation.
    /// </summary>
    private void EmitPlain64Operand(ExpressionSyntax expr)
    {
        if (Plain64ConstantText(expr) is { } text) { _w.Write(text); return; }
        EmitExpression(expr);
    }

    /// <summary>
    /// The JavaScript text of an integral constant that is exactly representable as a JS number, or
    /// null if the expression is not such a constant. The range test is what keeps
    /// <c>size == long.MaxValue</c> honest: a constant JavaScript cannot hold is left to the boxed
    /// Int64 path rather than silently rounded (<see cref="IsPlain64SafeOperand"/>).
    /// </summary>
    private string? Plain64ConstantText(ExpressionSyntax expr)
    {
        if (_model.GetConstantValue(expr) is not { HasValue: true, Value: { } value }) return null;
        switch (value)
        {
            case long l when System.Math.Abs(l) <= MaxSafeJsInteger:
                return l.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case ulong u when u <= MaxSafeJsInteger:
                return u.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case int i: return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case uint ui: return ui.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case short sh: return sh.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case ushort us: return us.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case sbyte sb: return sb.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case byte b: return b.ToString(System.Globalization.CultureInfo.InvariantCulture);
            default: return null;
        }
    }

    /// <summary><c>Number.MAX_SAFE_INTEGER</c> — above it a JS number stops counting in ones.</summary>
    private const long MaxSafeJsInteger = 9007199254740991L;

    /// <summary>True unless the operand is a 64-bit constant too large for a JS number to hold
    /// exactly. Such a comparison keeps the boxed Int64 path, where the constant is exact.</summary>
    private bool IsPlain64SafeOperand(ExpressionSyntax expr, ITypeSymbol? type)
        => !Is64BitInteger(UnwrapNullable(type))
           || _model.GetConstantValue(expr) is not { HasValue: true, Value: long or ulong }
           || Plain64ConstantText(expr) is not null;

    /// <summary>Emits <paramref name="expr"/> lifted into a real System.Int64/UInt64 instance — the
    /// managed side of the boundary.</summary>
    private void EmitLifted64(ExpressionSyntax expr, ITypeSymbol? type)
    {
        _w.Write(Is64BitUnsigned(UnwrapNullable(type)) ? "System.UInt64(" : "System.Int64(");
        EmitExpression(expr);
        _w.Write(")");
    }

    /// <summary>True if one half of a 64-bit <c>??</c> can be emitted as a plain number: it already
    /// is one, or it is a constant small enough to write out (<c>x ?? 0L</c> — the common shape).</summary>
    private bool IsPlain64CoalesceOperand(ExpressionSyntax expr)
        => EmitsAsPlainJsNumber(expr) || Plain64ConstantText(expr) is not null;

    /// <summary>Emits one half of a 64-bit <c>??</c> in whichever representation the whole expression
    /// settled on, lifting or unwrapping the half that disagrees.</summary>
    private void EmitCoalesce64Operand(ExpressionSyntax expr, bool plain)
    {
        var type = _model.GetTypeInfo(expr).Type;
        if (plain)
        {
            EmitPlain64Operand(expr);
            return;
        }
        if (EmitsAsPlainJsNumber(expr) && Is64BitInteger(UnwrapNullable(type)))
        {
            // `null` must survive the lift — it is the whole point of the operator.
            var v = Capture(() => EmitExpression(expr));
            var ctor = Is64BitUnsigned(UnwrapNullable(type)) ? "System.UInt64" : "System.Int64";
            _w.Write(IsNullableValueType(type) ? $"({v} == null ? null : {ctor}({v}))" : $"{ctor}({v})");
            return;
        }
        EmitExpression(expr);
    }

    /// <summary>
    /// Emits the receiver of a reduced extension method. It is the static call's FIRST ARGUMENT, so
    /// it crosses the same 64-bit boundary an ordinary argument does — a plain foreign value has to
    /// be lifted for a managed <c>this long</c> parameter, and unwrapped for a foreign one.
    /// </summary>
    private void EmitExtensionReceiver(ExpressionSyntax receiver, IMethodSymbol reduced)
    {
        var slot = reduced.Parameters.Length > 0 ? reduced.Parameters[0].Type : null;
        if (Is64BitInteger(UnwrapNullable(_model.GetTypeInfo(receiver).Type))
            && Is64BitInteger(UnwrapNullable(slot)))
        {
            EmitExpressionConverted(receiver, slot, IsForeignJsSlot(reduced));
            return;
        }
        EmitExpression(receiver);
    }

    /// <summary>Emits <paramref name="expr"/> as the value written INTO a foreign-JS 64-bit slot: a
    /// managed System.Int64/UInt64 instance is read back out as a plain number, and a value that is
    /// already plain passes through untouched.</summary>
    private void EmitForeign64Value(ExpressionSyntax expr, ITypeSymbol? slotType)
        => EmitExpressionConverted(expr, slotType, targetIsForeignJs: true);

    /// <summary>
    /// Emits the subject of a pattern test. The pattern emitters work on an already-emitted subject
    /// string and cannot ask how it was produced, so a foreign-JS 64-bit value is lifted here: their
    /// constant and relational tests go through <c>Transpose.equals</c> and <c>.gt</c>/<c>.lte</c>,
    /// which need a real instance. One lift per switch, not one per arm.
    /// </summary>
    private void EmitPatternSubject(ExpressionSyntax expr)
    {
        var type = _model.GetTypeInfo(expr).Type;
        if (Is64BitInteger(UnwrapNullable(type)) && EmitsAsPlainJsNumber(expr))
        {
            if (IsNullableValueType(type))
            {
                var v = Capture(() => EmitExpression(expr));
                var ctor = Is64BitUnsigned(UnwrapNullable(type)) ? "System.UInt64" : "System.Int64";
                _w.Write($"({v} == null ? null : {ctor}({v}))");
                return;
            }
            EmitLifted64(expr, type);
            return;
        }
        EmitExpression(expr);
    }

    /// <summary>True if an lvalue names a foreign-JS 64-bit slot, so a compound assignment or
    /// <c>++</c> on it must use plain JS operators rather than Int64 methods.</summary>
    private bool IsForeignJs64Lvalue(ExpressionSyntax left)
        => Is64BitInteger(UnwrapNullable(_model.GetTypeInfo(left).Type))
           && _model.GetSymbolInfo(left).Symbol is { } sym && IsForeignJsSlot(sym);
}
