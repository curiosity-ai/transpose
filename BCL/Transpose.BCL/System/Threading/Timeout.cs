namespace System.Threading
{
    [Transpose.External]
    public static class Timeout
    {
        /// <summary>
        /// A constant used to specify an infinite waiting period, for threading methods that accept an Int32 parameter.
        /// </summary>
        [Transpose.InlineConst]
        public const int Infinite = -1;
    }
}