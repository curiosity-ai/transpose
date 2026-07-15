namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    [Transpose.Reflectable]
    public static class MathF
    {
        [Transpose.Template("Math.E")]
        public const float E = 2.71828183f;
        
        [Transpose.Template("Math.PI")]
        public const float PI = 3.14159265f;

        [Transpose.Template("Math.abs({x})")]
        public static extern float Abs(float x);

        [Transpose.Template("Math.acos({x})")]
        public static extern float Acos(float x);

        [Transpose.Template("Math.asin({x})")]
        public static extern float Asin(float x);

        [Transpose.Template("Math.atan({x})")]
        public static extern float Atan(float x);

        [Transpose.Template("Math.atan2({y}, {x})")]
        public static extern float Atan2(float y, float x);

        [Transpose.Template("Math.ceil({x})")]
        public static extern float Ceiling(float x);

        [Transpose.Template("Math.cos({x})")]
        public static extern float Cos(float x);

        [Transpose.Template("Transpose.Math.cosh({value})")]
        public static extern float Cosh(float value);

        [Transpose.Template("Math.exp({x})")]
        public static extern float Exp(float x);

        [Transpose.Template("Math.floor({x})")]
        public static extern float Floor(float x);

        [Transpose.Template("Transpose.Math.IEEERemainder({x}, {y})")]
        public static extern float IEEERemainder(float x, float y);

        [Transpose.Template("Transpose.Math.log({x})")]
        public static extern float Log(float x);

        [Transpose.Template("Transpose.Math.logWithBase({x}, {y})")]
        public static extern float Log(float x, float y);

        [Transpose.Template("Transpose.Math.logWithBase({x}, 10.0)")]
        public static extern float Log10(float x);

        [Transpose.Template("Transpose.Math.logWithBase({x}, 2.0)")]
        public static extern float Log2(float x);

        [Transpose.Template("Math.max({x}, {y})")]
        public static extern float Max(float x, float y);
        
        [Transpose.Template("Math.min({x}, {y})")]
        public static extern float Min(float x, float y);

        [Transpose.Template("Math.pow({x}, {y})")]
        public static extern float Pow(float x, float y);

        [Transpose.Template("Transpose.Math.round({x}, 0, 6)")]
        public static extern float Round(float x);

        [Transpose.Template("Transpose.Math.round({x}, {digits}, 6)")]
        public static extern float Round(float x, int digits);

        [Transpose.Template("Transpose.Math.round({x}, 0, {mode})")]
        public static extern float Round(float x, MidpointRounding mode);

        [Transpose.Template("Transpose.Math.round({x}, {digits}, {mode})")]
        public static extern float Round(float x, int digits, MidpointRounding mode);

        [Transpose.Template("Transpose.Int.sign({value})")]
        public static extern int Sign(float value);

        [Transpose.Template("Math.sin({x})")]
        public static extern float Sin(float x);

        [Transpose.Template("Transpose.Math.sinh({value})")]
        public static extern float Sinh(float value);

        [Transpose.Template("Math.sqrt({x})")]
        public static extern float Sqrt(float x);
        
        [Transpose.Template("Math.tan({x})")]
        public static extern float Tan(float x);

        [Transpose.Template("Transpose.Math.tanh({value})")]
        public static extern float Tanh(float value);

        [Transpose.Template("Transpose.Int.trunc({d})")]
        public static extern float Truncate(float d);

        [Transpose.Template("((1.0 / {y}) < 0 ? -1.0 : 1.0) * Math.abs({x})")]
        public static extern float CopySign(float x, float y);

        [Transpose.Template("(({x} * {y}) + {z})")]
        public static extern float FusedMultiplyAdd(float x, float y, float z);

        [Transpose.Template("(1.0 / {x})")]
        public static extern float ReciprocalEstimate(float x);

        [Transpose.Template("(1.0 / Math.sqrt({x}))")]
        public static extern float ReciprocalSqrtEstimate(float x);


        [Transpose.Template(@"(function(x, y) {
            var ax = Math.abs(x);
            var ay = Math.abs(y);

            if (ax > ay) { return x; }
            if (ax === ay) { return (x > y) ? x : y; }
            return y;
        })({x}, {y})")]
        public static extern float MaxMagnitude(float x, float y);

        [Transpose.Template(@"(function(x, y) {
            var ax = Math.abs(x);
            var ay = Math.abs(y);

            if (ax < ay) { return x; }
            if (ax === ay) { return (x < y) ? x : y; }
            return y;
        })({x}, {y})")]
        public static extern float MinMagnitude(float x, float y);

        [Transpose.Template(@"Transpose.Int.bitIncrement({x})")]
        public static extern float BitIncrement(float x);

        [Transpose.Template(@"Transpose.Int.bitDecrement({x})")]
        public static extern float BitDecrement(float x);
    }
}
