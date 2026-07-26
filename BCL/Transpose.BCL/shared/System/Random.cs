// http://referencesource.microsoft.com/#mscorlib/system/random.cs,bb77e610694e64ca

// ==++==
//
//   Copyright (c) Microsoft Corporation.  All rights reserved.
//
// ==--==
/*============================================================
**
** Class:  Random
**
**
** Purpose: A random number generator.
**
**
===========================================================*/

using System.Diagnostics.Contracts;

namespace System
{
    /// <summary>
    /// A random number generator
    /// </summary>
    public class Random
    {
        private const int MBIG = int.MaxValue;

        private const int MSEED = 161803398;
        private const int MZ = 0;

        private int inext;

        private int inextp;
        private int[] SeedArray = new int[56];

        /// <summary>
        /// Initializes a new instance of the Random class, using a default seed value.
        /// </summary>
        public Random()
          : this(GenerateSeed())
        {
        }

        /// <summary>
        /// A fresh seed for the parameterless constructor, drawn from the platform's
        /// <c>Math.random()</c>.
        /// </summary>
        /// <remarks>
        /// This replaces a <c>(int)DateTime.Now.Ticks</c> seed. Ticks are effectively
        /// millisecond-resolution in JavaScript (they come from <c>Date</c>), so every
        /// <c>new Random()</c> constructed within the same millisecond — a loop, or a handful of
        /// objects built together — got the *same* seed and therefore produced the identical
        /// sequence. <c>Math.random()</c> is independent per call.
        /// </remarks>
        private static int GenerateSeed()
        {
            // getRndInteger(0, int.MaxValue): Math.floor(Math.random() * (max - min)) + min.
            // Math.random() is in [0, 1), so the result is a non-negative int < int.MaxValue.
            return (int)(Math.Random() * int.MaxValue);
        }

        /// <summary>
        /// Initializes a new instance of the Random class, using the specified seed value.
        /// </summary>
        /// <param name="seed">A number used to calculate a starting value for the pseudo-random number sequence. If a negative number is specified, the absolute value of the number is used.</param>
        public Random(int seed)
        {
            int ii;
            int mj, mk;

            //Initialize our Seed array.
            //This algorithm comes from Numerical Recipes in C (2nd Ed.)
            int subtraction = (seed == int.MinValue) ? int.MaxValue : Math.Abs(seed);
            // The `(int)` casts on the subtractions are no-ops on .NET (the operands are already
            // int) but are required when transpiled: this algorithm relies on 32-bit integer
            // subtraction WRAPPING on overflow, which JavaScript's Number arithmetic does not do.
            // Forcing the cast makes the emitter clip the result to int32 so, e.g., a time-based
            // seed near int.MaxValue no longer corrupts SeedArray into a degenerate sequence.
            mj = (int)(MSEED - subtraction);
            SeedArray[55] = mj;
            mk = 1;
            for (int i = 1; i < 55; i++)
            {  //Apparently the range [1..55] is special (Knuth) and so we're wasting the 0'th position.
                ii = (21 * i) % 55;
                SeedArray[ii] = mk;
                mk = (int)(mj - mk);
                if (mk < 0)
                {
                    mk += MBIG;
                }
                mj = SeedArray[ii];
            }
            for (int k = 1; k < 5; k++)
            {
                for (int i = 1; i < 56; i++)
                {
                    SeedArray[i] = (int)(SeedArray[i] - SeedArray[1 + (i + 30) % 55]);
                    if (SeedArray[i] < 0)
                    {
                        SeedArray[i] += MBIG;
                    }
                }
            }
            inext = 0;
            inextp = 21;
            seed = 1;
        }

        /// <summary>
        /// Returns a random floating-point number between 0.0 and 1.0.
        /// </summary>
        protected virtual double Sample()
        {
            //Including this division at the end gives us significantly improved
            //random number distribution.
            return (InternalSample() * (1.0 / MBIG));
        }

        private int InternalSample()
        {
            int retVal;
            int locINext = inext;
            int locINextp = inextp;

            if (++locINext >= 56)
            {
                locINext = 1;
            }

            if (++locINextp >= 56)
            {
                locINextp = 1;
            }

            // (int) cast forces 32-bit wrap when transpiled (see the ctor for why); a no-op on .NET.
            retVal = (int)(SeedArray[locINext] - SeedArray[locINextp]);

            if (retVal == MBIG)
            {
                retVal--;
            }

            if (retVal < 0)
            {
                retVal += MBIG;
            }

            SeedArray[locINext] = retVal;

            inext = locINext;
            inextp = locINextp;

            return retVal;
        }

        /// <summary>
        /// Returns a non-negative random integer.
        /// </summary>
        /// <returns>A 32-bit signed integer that is greater than or equal to 0 and less than Int32.MaxValue.</returns>
        public virtual int Next()
        {
            return InternalSample();
        }

        private double GetSampleForLargeRange()
        {
            // The distribution of double value returned by Sample
            // is not distributed well enough for a large range.
            // If we use Sample for a range [Int32.MinValue..Int32.MaxValue)
            // We will end up getting even numbers only.

            int result = InternalSample();
            // Note we can't use addition here. The distribution will be bad if we do that.
            bool negative = (InternalSample() % 2 == 0) ? true : false;  // decide the sign based on second sample
            if (negative)
            {
                result = -result;
            }
            double d = result;
            d += (int.MaxValue - 1); // get a number in range [0 .. 2 * Int32MaxValue - 1)
            d /= 2 * (uint)int.MaxValue - 1;
            return d;
        }

        /// <summary>
        /// Returns a random integer that is within a specified range.
        /// </summary>
        /// <param name="minValue">The inclusive lower bound of the random number returned.</param>
        /// <param name="maxValue">The exclusive upper bound of the random number returned. maxValue must be greater than or equal to minValue.</param>
        /// <returns>A 32-bit signed integer greater than or equal to minValue and less than maxValue; that is, the range of return values includes minValue but not maxValue. If minValue equals maxValue, minValue is returned.</returns>
        public virtual int Next(int minValue, int maxValue)
        {
            if (minValue > maxValue)
            {
                throw new ArgumentOutOfRangeException("minValue", "'minValue' cannot be greater than maxValue.");
            }
            Contract.EndContractBlock();

            long range = (long)maxValue - minValue;
            if (range <= (long)int.MaxValue)
            {
                return ((int)(Sample() * range) + minValue);
            }
            else
            {
                return (int)((long)(GetSampleForLargeRange() * range) + minValue);
            }
        }

        /// <summary>
        /// Returns a non-negative random integer that is less than the specified maximum.
        /// </summary>
        /// <param name="maxValue">The exclusive upper bound of the random number to be generated. maxValue must be greater than or equal to 0</param>
        /// <returns>A 32-bit signed integer that is greater than or equal to 0, and less than maxValue; that is, the range of return values ordinarily includes 0 but not maxValue. However, if maxValue equals 0, maxValue is returned.</returns>
        public virtual int Next(int maxValue)
        {
            if (maxValue < 0)
            {
                throw new ArgumentOutOfRangeException("maxValue", "'maxValue' must be greater than zero.");
            }
            Contract.EndContractBlock();
            return (int)(Sample() * maxValue);
        }

        /// <summary>
        /// Returns a random floating-point number that is greater than or equal to 0.0, and less than 1.0.
        /// </summary>
        /// <returns>A double-precision floating point number that is greater than or equal to 0.0, and less than 1.0.</returns>
        public virtual double NextDouble()
        {
            return Sample();
        }

        /// <summary>
        /// Fills the elements of a specified array of bytes with random numbers.
        /// </summary>
        /// <param name="buffer">An array of bytes to contain random numbers.</param>
        public virtual void NextBytes(byte[] buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }
            Contract.EndContractBlock();
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (byte)(InternalSample() % (byte.MaxValue + 1));
            }
        }

        /// <summary>
        /// Produces a value in the range [0, ulong.MaxValue] by stitching together three draws of
        /// 22 + 22 + 20 bits. This is exactly how .NET composes 64 random bits out of
        /// <see cref="Next(int)"/>, and matching it is what makes a seeded sequence of
        /// <see cref="NextInt64()"/> agree with .NET value-for-value.
        /// </summary>
        private ulong NextUInt64()
        {
            return ((ulong)(uint)Next(1 << 22))
                 | (((ulong)(uint)Next(1 << 22)) << 22)
                 | (((ulong)(uint)Next(1 << 20)) << 44);
        }

        /// <summary>
        /// Returns a non-negative random integer.
        /// </summary>
        /// <returns>A 64-bit signed integer that is greater than or equal to 0 and less than Int64.MaxValue.</returns>
        public virtual long NextInt64()
        {
            while (true)
            {
                // Take the top 63 bits for a value in [0, long.MaxValue], and retry on the one
                // excluded value so the result is in [0, long.MaxValue).
                ulong result = NextUInt64() >> 1;

                if (result != long.MaxValue)
                {
                    return (long)result;
                }
            }
        }

        /// <summary>
        /// Returns a non-negative random integer that is less than the specified maximum.
        /// </summary>
        /// <param name="maxValue">The exclusive upper bound of the random number to be generated. maxValue must be greater than or equal to 0.</param>
        /// <returns>A 64-bit signed integer that is greater than or equal to 0, and less than maxValue.</returns>
        public virtual long NextInt64(long maxValue)
        {
            if (maxValue < 0)
            {
                throw new ArgumentOutOfRangeException("maxValue", "maxValue must be greater than or equal to 0");
            }

            return NextInt64(0, maxValue);
        }

        /// <summary>
        /// Returns a random 64-bit integer that is within a specified range.
        /// </summary>
        /// <param name="minValue">The inclusive lower bound of the random number returned.</param>
        /// <param name="maxValue">The exclusive upper bound of the random number returned. maxValue must be greater than or equal to minValue.</param>
        /// <returns>A 64-bit signed integer greater than or equal to minValue and less than maxValue. If minValue equals maxValue, minValue is returned.</returns>
        public virtual long NextInt64(long minValue, long maxValue)
        {
            if (minValue > maxValue)
            {
                throw new ArgumentOutOfRangeException("minValue", "minValue must be less than or equal to maxValue");
            }

            ulong exclusiveRange = (ulong)(maxValue - minValue);

            if (exclusiveRange > 1)
            {
                // Narrow to the smallest range [0, 2^bitsNeeded) that contains exclusiveRange, then
                // redraw until the value falls inside the inner range (rejection sampling — an
                // unbiased modulo would need a division on a 64-bit value).
                int bitsNeeded = BitsNeeded(exclusiveRange);

                while (true)
                {
                    ulong result = NextUInt64() >> (64 - bitsNeeded);

                    if (result < exclusiveRange)
                    {
                        return (long)result + minValue;
                    }
                }
            }

            // exclusiveRange is 0 or 1, so minValue is the only possible answer.
            return minValue;
        }

        /// <summary>The position of <paramref name="value"/>'s highest set bit, i.e.
        /// <c>64 - BitOperations.LeadingZeroCount(value)</c> (which this BCL does not have).</summary>
        private static int BitsNeeded(ulong value)
        {
            int bits = 0;

            while (value != 0)
            {
                bits++;
                value = value >> 1;
            }

            return bits;
        }

        /// <summary>
        /// Returns a random floating-point number that is greater than or equal to 0.0, and less than 1.0.
        /// </summary>
        /// <returns>A single-precision floating point number that is greater than or equal to 0.0, and less than 1.0.</returns>
        public virtual float NextSingle()
        {
            while (true)
            {
                // Narrowing a double sample to float rounds, and rounding up from just under 1.0
                // would break the exclusive upper bound — so redraw in that case, as .NET does.
                float f = (float)Sample();

                if (f < 1.0f)
                {
                    return f;
                }
            }
        }

        private static Random s_shared;

        /// <summary>
        /// Provides a shared instance for use by any code that needs random numbers but has no need
        /// for its own sequence. On .NET this instance is thread-safe; JavaScript is single-threaded,
        /// so a plain instance suffices here.
        /// </summary>
        public static Random Shared
        {
            get
            {
                if (s_shared == null)
                {
                    s_shared = new Random();
                }

                return s_shared;
            }
        }
    }
}
