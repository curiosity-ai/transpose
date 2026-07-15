namespace System
{
    [H5.Convention(Member = H5.ConventionMember.Field | H5.ConventionMember.Method, Notation = H5.Notation.CamelCase)]
    [H5.External]
    [H5.Reflectable]
    public static class MathF
    {
        [H5.Template("Math.E")]
        public const float E = 2.71828183f;
        
        [H5.Template("Math.PI")]
        public const float PI = 3.14159265f;

        [H5.Template("Math.abs({x})")]
        public static extern float Abs(float x);

        [H5.Template("Math.acos({x})")]
        public static extern float Acos(float x);

        [H5.Template("Math.asin({x})")]
        public static extern float Asin(float x);

        [H5.Template("Math.atan({x})")]
        public static extern float Atan(float x);

        [H5.Template("Math.atan2({y}, {x})")]
        public static extern float Atan2(float y, float x);

        [H5.Template("Math.ceil({x})")]
        public static extern float Ceiling(float x);

        [H5.Template("Math.cos({x})")]
        public static extern float Cos(float x);

        [H5.Template("H5.Math.cosh({value})")]
        public static extern float Cosh(float value);

        [H5.Template("Math.exp({x})")]
        public static extern float Exp(float x);

        [H5.Template("Math.floor({x})")]
        public static extern float Floor(float x);

        [H5.Template("H5.Math.IEEERemainder({x}, {y})")]
        public static extern float IEEERemainder(float x, float y);

        [H5.Template("H5.Math.log({x})")]
        public static extern float Log(float x);

        [H5.Template("H5.Math.logWithBase({x}, {y})")]
        public static extern float Log(float x, float y);

        [H5.Template("H5.Math.logWithBase({x}, 10.0)")]
        public static extern float Log10(float x);

        [H5.Template("H5.Math.logWithBase({x}, 2.0)")]
        public static extern float Log2(float x);

        [H5.Template("Math.max({x}, {y})")]
        public static extern float Max(float x, float y);
        
        [H5.Template("Math.min({x}, {y})")]
        public static extern float Min(float x, float y);

        [H5.Template("Math.pow({x}, {y})")]
        public static extern float Pow(float x, float y);

        [H5.Template("H5.Math.round({x}, 0, 6)")]
        public static extern float Round(float x);

        [H5.Template("H5.Math.round({x}, {digits}, 6)")]
        public static extern float Round(float x, int digits);

        [H5.Template("H5.Math.round({x}, 0, {mode})")]
        public static extern float Round(float x, MidpointRounding mode);

        [H5.Template("H5.Math.round({x}, {digits}, {mode})")]
        public static extern float Round(float x, int digits, MidpointRounding mode);

        [H5.Template("H5.Int.sign({value})")]
        public static extern int Sign(float value);

        [H5.Template("Math.sin({x})")]
        public static extern float Sin(float x);

        [H5.Template("H5.Math.sinh({value})")]
        public static extern float Sinh(float value);

        [H5.Template("Math.sqrt({x})")]
        public static extern float Sqrt(float x);
        
        [H5.Template("Math.tan({x})")]
        public static extern float Tan(float x);

        [H5.Template("H5.Math.tanh({value})")]
        public static extern float Tanh(float value);

        [H5.Template("H5.Int.trunc({d})")]
        public static extern float Truncate(float d);

        [H5.Template("((1.0 / {y}) < 0 ? -1.0 : 1.0) * Math.abs({x})")]
        public static extern float CopySign(float x, float y);

        [H5.Template("(({x} * {y}) + {z})")]
        public static extern float FusedMultiplyAdd(float x, float y, float z);

        [H5.Template("(1.0 / {x})")]
        public static extern float ReciprocalEstimate(float x);

        [H5.Template("(1.0 / Math.sqrt({x}))")]
        public static extern float ReciprocalSqrtEstimate(float x);


        [H5.Template(@"(function(x, y) {
            var ax = Math.abs(x);
            var ay = Math.abs(y);

            if (ax > ay) { return x; }
            if (ax === ay) { return (x > y) ? x : y; }
            return y;
        })({x}, {y})")]
        public static extern float MaxMagnitude(float x, float y);

        [H5.Template(@"(function(x, y) {
            var ax = Math.abs(x);
            var ay = Math.abs(y);

            if (ax < ay) { return x; }
            if (ax === ay) { return (x < y) ? x : y; }
            return y;
        })({x}, {y})")]
        public static extern float MinMagnitude(float x, float y);

        [H5.Template(@"H5.Int.bitIncrement({x})")]
        public static extern float BitIncrement(float x);

        [H5.Template(@"H5.Int.bitDecrement({x})")]
        public static extern float BitDecrement(float x);
    }
}
