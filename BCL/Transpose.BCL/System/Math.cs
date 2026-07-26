namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Name("Math")]
    public static class Math
    {
        [Transpose.Convention]
        public const double E = 2.7182818284590452354;

        [Transpose.Convention]
        public const double PI = 3.14159265358979323846;

        public static extern int Abs(int x);

        public static extern double Abs(double x);

        [Transpose.Template("{l}.abs()")]
        public static extern long Abs(long l);

        [Transpose.Template("{l}.abs()")]
        public static extern decimal Abs(decimal l);

        /// <summary>
        /// Returns the larger of two 8-bit unsigned integers.
        /// </summary>
        /// <param name="val1">The first of two 8-bit unsigned integers to compare.</param>
        /// <param name="val2">The second of two 8-bit unsigned integers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is larger.</returns>
        public static extern byte Max(byte val1, byte val2);

        /// <summary>
        /// Returns the larger of two 8-bit signed integers.
        /// </summary>
        /// <param name="val1">The first of two 8-bit signed integers to compare.</param>
        /// <param name="val2">The second of two 8-bit signed integers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is larger.</returns>
        
        public static extern sbyte Max(sbyte val1, sbyte val2);

        /// <summary>
        /// Returns the larger of two 16-bit signed integers.
        /// </summary>
        /// <param name="val1">The first of two 16-bit signed integers to compare.</param>
        /// <param name="val2">The second of two 16-bit signed integers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is larger.</returns>
        public static extern short Max(short val1, short val2);

        /// <summary>
        /// Returns the larger of two 16-bit unsigned integers.
        /// </summary>
        /// <param name="val1">The first of two 16-bit unsigned integers to compare.</param>
        /// <param name="val2">The second of two 16-bit unsigned integers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is larger.</returns>
        
        public static extern ushort Max(ushort val1, ushort val2);

        /// <summary>
        /// Returns the larger of two single-precision floating-point numbers.
        /// </summary>
        /// <param name="val1">The first of two single-precision floating-point numbers to compare.</param>
        /// <param name="val2">The second of two single-precision floating-point numbers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is larger.</returns>
        public static extern float Max(float val1, float val2);

        /// <summary>
        /// Returns the larger of two 32-bit signed integers.
        /// </summary>
        /// <param name="val1">The first of two 32-bit signed integers to compare.</param>
        /// <param name="val2">The second of two 32-bit signed integers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is larger.</returns>
        public static extern int Max(int val1, int val2);

        /// <summary>
        /// Returns the larger of two 32-bit unsigned integers.
        /// </summary>
        /// <param name="val1">The first of two 32-bit unsigned integers to compare.</param>
        /// <param name="val2">The second of two 32-bit unsigned integers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is larger.</returns>
        
        public static extern uint Max(uint val1, uint val2);

        /// <summary>
        /// Returns the larger of two double-precision floating-point numbers.
        /// </summary>
        /// <param name="val1">The first of two double-precision floating-point numbers to compare.</param>
        /// <param name="val2">The second of two double-precision floating-point numbers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is larger.</returns>
        public static extern double Max(double val1, double val2);

        /// <summary>
        /// Returns the larger of two 64-bit signed integers.
        /// </summary>
        /// <param name="val1">The first of two 64-bit signed integers to compare.</param>
        /// <param name="val2">The second of two 64-bit signed integers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is larger.</returns>
        [Transpose.Template("System.Int64.max({val1}, {val2})")]
        public static extern long Max(long val1, long val2);

        /// <summary>
        /// Returns the larger of two 64-bit unsigned integers.
        /// </summary>
        /// <param name="val1">The first of two 64-bit unsigned integers to compare.</param>
        /// <param name="val2">The second of two 64-bit unsigned integers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is larger.</returns>
        [Transpose.Template("System.UInt64.max({val1}, {val2})")]
        
        public static extern ulong Max(ulong val1, ulong val2);

        /// <summary>
        /// Returns the larger of two decimal numbers.
        /// </summary>
        /// <param name="val1">The first of two decimal numbers to compare.</param>
        /// <param name="val2">The second of two decimal numbers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is larger.</returns>
        [Transpose.Template("System.Decimal.max({val1}, {val2})")]
        public static extern decimal Max(decimal val1, decimal val2);

        /// <summary>
        /// Returns the smaller of two 8-bit unsigned integers.
        /// </summary>
        /// <param name="val1">The first of two 8-bit unsigned integers to compare.</param>
        /// <param name="val2">The second of two 8-bit unsigned integers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is smaller.</returns>
        public static extern byte Min(byte val1, byte val2);

        /// <summary>
        /// Returns the smaller of two 8-bit signed integers.
        /// </summary>
        /// <param name="val1">The first of two 8-bit signed integers to compare.</param>
        /// <param name="val2">The second of two 8-bit signed integers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is smaller.</returns>
        
        public static extern sbyte Min(sbyte val1, sbyte val2);

        /// <summary>
        /// Returns the smaller of two 16-bit signed integers.
        /// </summary>
        /// <param name="val1">The first of two 16-bit signed integers to compare.</param>
        /// <param name="val2">The second of two 16-bit signed integers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is smaller.</returns>
        public static extern short Min(short val1, short val2);

        /// <summary>
        /// Returns the smaller of two 16-bit unsigned integers.
        /// </summary>
        /// <param name="val1">The first of two 16-bit unsigned integers to compare.</param>
        /// <param name="val2">The second of two 16-bit unsigned integers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is smaller.</returns>
        
        public static extern ushort Min(ushort val1, ushort val2);

        /// <summary>
        /// Returns the smaller of two single-precision floating-point numbers.
        /// </summary>
        /// <param name="val1">The first of two single-precision floating-point numbers to compare.</param>
        /// <param name="val2">The second of two single-precision floating-point numbers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is smaller.</returns>
        public static extern float Min(float val1, float val2);

        /// <summary>
        /// Returns the smaller of two 32-bit signed integers.
        /// </summary>
        /// <param name="val1">The first of two 32-bit signed integers to compare.</param>
        /// <param name="val2">The second of two 32-bit signed integers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is smaller.</returns>
        public static extern int Min(int val1, int val2);

        /// <summary>
        /// Returns the smaller of two 32-bit unsigned integers.
        /// </summary>
        /// <param name="val1">The first of two 32-bit unsigned integers to compare.</param>
        /// <param name="val2">The second of two 32-bit unsigned integers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is smaller.</returns>
        
        public static extern uint Min(uint val1, uint val2);

        /// <summary>
        /// Returns the smaller of two double-precision floating-point numbers.
        /// </summary>
        /// <param name="val1">The first of two double-precision floating-point numbers to compare.</param>
        /// <param name="val2">The second of two double-precision floating-point numbers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is smaller.</returns>
        public static extern double Min(double val1, double val2);

        /// <summary>
        /// Returns the smaller of two 64-bit signed integers.
        /// </summary>
        /// <param name="val1">The first of two 64-bit signed integers to compare.</param>
        /// <param name="val2">The second of two 64-bit signed integers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is smaller.</returns>
        [Transpose.Template("System.Int64.min({val1}, {val2})")]
        public static extern long Min(long val1, long val2);

        /// <summary>
        /// Returns the smaller of two 64-bit unsigned integers.
        /// </summary>
        /// <param name="val1">The first of two 64-bit unsigned integers to compare.</param>
        /// <param name="val2">The second of two 64-bit unsigned integers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is smaller.</returns>
        [Transpose.Template("System.UInt64.min({val1}, {val2})")]
        
        public static extern ulong Min(ulong val1, ulong val2);

        /// <summary>
        /// Returns the smaller of two decimal numbers.
        /// </summary>
        /// <param name="val1">The first of two decimal numbers to compare.</param>
        /// <param name="val2">The second of two decimal numbers to compare.</param>
        /// <returns>Parameter val1 or val2, whichever is smaller.</returns>
        [Transpose.Template("System.Decimal.min({val1}, {val2})")]
        public static extern decimal Min(decimal val1, decimal val2);

        public static extern double Random();

        public static extern double Sqrt(double x);

        [Transpose.Template("{d}.ceil()")]
        public static extern decimal Ceiling(decimal d);

        [Transpose.Name("ceil")]
        public static extern double Ceiling(double d);

        public static extern double Floor(double x);

        [Transpose.Template("{d}.floor()")]
        public static extern decimal Floor(decimal d);

        [Transpose.Template("System.Decimal.round({x}, 6)")]
        public static extern decimal Round(decimal x);

        [Transpose.Template("Transpose.Math.round({d}, 0, 6)")]
        public static extern double Round(double d);

        [Transpose.Template("Math.round({d})")]
        public static extern double JsRound(double d);

        [Transpose.Template("System.Decimal.toDecimalPlaces({d}, {digits}, 6)")]
        public static extern decimal Round(decimal d, int digits);

        [Transpose.Template("Transpose.Math.round({d}, {digits}, 6)")]
        public static extern double Round(double d, int digits);

        [Transpose.Template("System.Decimal.round({d}, {method})")]
        public static extern decimal Round(decimal d, MidpointRounding method);

        [Transpose.Template("Transpose.Math.round({d}, 0, {method})")]
        public static extern double Round(double d, MidpointRounding method);

        [Transpose.Template("System.Decimal.toDecimalPlaces({d}, {digits}, {method})")]
        public static extern decimal Round(decimal d, int digits, MidpointRounding method);

        [Transpose.Template("Transpose.Math.round({d}, {digits}, {method})")]
        public static extern double Round(double d, int digits, MidpointRounding method);

        [Transpose.Template("Transpose.Math.IEEERemainder({x}, {y})")]
        public static extern double IEEERemainder(double x, double y);

        public static extern double Exp(double x);

        [Transpose.Template("{x}.exponential()")]
        public static extern decimal Exp(decimal x);

        [Transpose.Template("Transpose.Math.log({x})")]
        public static extern double Log(double x);

        [Transpose.Template("Transpose.Math.logWithBase({x}, {logBase})")]
        public static extern double Log(double x, double logBase);

        [Transpose.Template("Transpose.Math.logWithBase({x}, 10.0)")]
        public static extern double Log10(double x);

        [Transpose.Template("{x}.pow({y})")]
        public static extern decimal Pow(decimal x, decimal y);

        public static extern double Pow(double x, double y);

        public static extern double Pow(int x, int y);

        // .NET has only Pow(double, double), so `Math.Pow(someLong, 2L)` resolves there. The extra
        // decimal overload above makes that call ambiguous here (long converts implicitly to both
        // double and decimal, neither better), so a long argument needs its own overload — the same
        // reason Pow(int, int) exists.
        [Transpose.Template("Math.pow(({x}).toNumber(), ({y}).toNumber())")]
        public static extern double Pow(long x, long y);

        [Transpose.Template("Math.pow(({x}).toNumber(), ({y}).toNumber())")]
        public static extern double Pow(ulong x, ulong y);

        public static extern double Acos(double x);

        public static extern double Asin(double x);

        public static extern double Atan(double x);

        public static extern double Atan2(double y, double x);

        public static extern double Cos(double x);

        public static extern double Sin(double x);

        public static extern double Tan(double x);

        [Transpose.Template("Transpose.Int.trunc({d})")]
        public static extern double Truncate(double d);

        [Transpose.Template("{d}.trunc()")]
        public static extern decimal Truncate(decimal d);

        [Transpose.Template("Transpose.Int.sign({value})")]
        public static extern int Sign(double value);

        [Transpose.Template("{value}.sign()")]
        public static extern int Sign(decimal value);

        // .NET has Sign(long)/Sign(int)/…; without them an integer argument is ambiguous between
        // Sign(double) and Sign(decimal). (There is deliberately no Sign(ulong) — .NET has none
        // either, since a ulong is never negative.)
        [Transpose.Template("({value}).sign()")]
        public static extern int Sign(long value);

        [Transpose.Template("Transpose.Int.sign({value})")]
        public static extern int Sign(int value);

        // The full 64-bit product of two 32-bit values — the point being that it does NOT wrap the
        // way `a * b` in int arithmetic would.
        [Transpose.Template("System.Int64({a}).mul(System.Int64({b}))")]
        public static extern long BigMul(int a, int b);

        [Transpose.Template("System.UInt64({a}).mul(System.UInt64({b}))")]
        public static extern ulong BigMul(uint a, uint b);

        [Transpose.Template("Transpose.Math.divRem({a}, {b}, {result})")]
        public static extern int DivRem(int a, int b, out int result);

        [Transpose.Template("System.Int64.divRem({a}, {b}, {result})")]
        public static extern long DivRem(long a, long b, out long result);

        [Transpose.Template("Transpose.Math.sinh({value})")]
        public static extern double Sinh(double value);

        [Transpose.Template("Transpose.Math.cosh({value})")]
        public static extern double Cosh(double value);

        [Transpose.Template("Transpose.Math.tanh({value})")]
        public static extern double Tanh(double value);

        [Transpose.Template("((1.0 / {y}) < 0 ? -1.0 : 1.0) * Math.abs({x})")]
        public static extern double CopySign(double x, double y);

        [Transpose.Template("Transpose.Math.logWithBase({x}, 2.0)")]
        public static extern double Log2(double x);

        [Transpose.Template("({x} * {y}) + {z}")]
        public static extern double FusedMultiplyAdd(double x, double y, double z);

        [Transpose.Template("1.0 / {x}")]
        public static extern double ReciprocalEstimate(double x);

        [Transpose.Template("1.0 / System.Math.sqrt({x})")]
        public static extern double ReciprocalSqrtEstimate(double x);

        // Clamp

        [Transpose.Template(@"(function(value, min, max) {
            if (min > max) { throw new System.ArgumentException('min > max'); }
            return (value < min) ? min : ((value > max) ? max : value);
        })({value}, {min}, {max})")]
        public static extern byte Clamp(byte value, byte min, byte max);

        [Transpose.Template(@"(function(value, min, max) {
            if (min > max) { throw new System.ArgumentException('min > max'); }
            return (value < min) ? min : ((value > max) ? max : value);
        })({value}, {min}, {max})")]
        public static extern decimal Clamp(decimal value, decimal min, decimal max);

        [Transpose.Template(@"(function(value, min, max) {
            if (min > max) { throw new System.ArgumentException('min > max'); }
            return (value < min) ? min : ((value > max) ? max : value);
        })({value}, {min}, {max})")]
        public static extern double Clamp(double value, double min, double max);

        [Transpose.Template(@"(function(value, min, max) {
            if (min > max) { throw new System.ArgumentException('min > max'); }
            return (value < min) ? min : ((value > max) ? max : value);
        })({value}, {min}, {max})")]
        public static extern float Clamp(float value, float min, float max);

        [Transpose.Template(@"(function(value, min, max) {
            if (min > max) { throw new System.ArgumentException('min > max'); }
            return (value < min) ? min : ((value > max) ? max : value);
        })({value}, {min}, {max})")]
        public static extern int Clamp(int value, int min, int max);

        [Transpose.Template(@"(function(value, min, max) {
            if (min > max) { throw new System.ArgumentException('min > max'); }
            return (value < min) ? min : ((value > max) ? max : value);
        })({value}, {min}, {max})")]
        public static extern long Clamp(long value, long min, long max);

        [Transpose.Template(@"(function(value, min, max) {
            if (min > max) { throw new System.ArgumentException('min > max'); }
            return (value < min) ? min : ((value > max) ? max : value);
        })({value}, {min}, {max})")]
        public static extern sbyte Clamp(sbyte value, sbyte min, sbyte max);

        [Transpose.Template(@"(function(value, min, max) {
            if (min > max) { throw new System.ArgumentException('min > max'); }
            return (value < min) ? min : ((value > max) ? max : value);
        })({value}, {min}, {max})")]
        public static extern short Clamp(short value, short min, short max);

        [Transpose.Template(@"(function(value, min, max) {
            if (min > max) { throw new System.ArgumentException('min > max'); }
            return (value < min) ? min : ((value > max) ? max : value);
        })({value}, {min}, {max})")]
        public static extern uint Clamp(uint value, uint min, uint max);

        [Transpose.Template(@"(function(value, min, max) {
            if (min > max) { throw new System.ArgumentException('min > max'); }
            return (value < min) ? min : ((value > max) ? max : value);
        })({value}, {min}, {max})")]
        public static extern ulong Clamp(ulong value, ulong min, ulong max);

        [Transpose.Template(@"(function(value, min, max) {
            if (min > max) { throw new System.ArgumentException('min > max'); }
            return (value < min) ? min : ((value > max) ? max : value);
        })({value}, {min}, {max})")]
        public static extern ushort Clamp(ushort value, ushort min, ushort max);


        // MaxMagnitude / MinMagnitude

        [Transpose.Template(@"(function(x, y) {
            var ax = Math.abs(x);
            var ay = Math.abs(y);

            if (ax > ay) { return x; }
            if (ax === ay) { return (x > y) ? x : y; }
            return y;
        })({x}, {y})")]
        public static extern double MaxMagnitude(double x, double y);

        [Transpose.Template(@"(function(x, y) {
            var ax = Math.abs(x);
            var ay = Math.abs(y);

            if (ax < ay) { return x; }
            if (ax === ay) { return (x < y) ? x : y; }
            return y;
        })({x}, {y})")]
        public static extern double MinMagnitude(double x, double y);


        [Transpose.Template(@"System.Int64.bitIncrement({x})")]
        public static extern double BitIncrement(double x);

        [Transpose.Template(@"System.Int64.bitDecrement({x})")]
        public static extern double BitDecrement(double x);
    }
}