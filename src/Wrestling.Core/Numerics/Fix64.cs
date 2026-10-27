using System;
using System.Globalization;

namespace Wrestling.Core.Numerics
{
    /// <summary>
    /// Signed Q32.32 fixed-point number. All simulation math uses this type, so a bout
    /// gives identical results on every platform. Range is about ±2.1 billion with a
    /// resolution of 2^-32. Arithmetic throws <see cref="OverflowException"/> instead of
    /// wrapping, and multiplication and division round half away from zero.
    /// </summary>
    public readonly struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>
    {
        public const int FractionalBits = 32;

        private const long OneRaw = 1L << FractionalBits;
        private const long FractionMask = OneRaw - 1;
        private const decimal OneDecimal = 4294967296m;

        public static readonly Fix64 Zero = new Fix64(0);
        public static readonly Fix64 One = new Fix64(OneRaw);
        public static readonly Fix64 Half = new Fix64(OneRaw / 2);
        public static readonly Fix64 MaxValue = new Fix64(long.MaxValue);
        public static readonly Fix64 MinValue = new Fix64(long.MinValue);

        /// <summary>The smallest positive value, 2^-32.</summary>
        public static readonly Fix64 Epsilon = new Fix64(1);

        private Fix64(long raw)
        {
            Raw = raw;
        }

        /// <summary>The underlying Q32.32 integer.</summary>
        public long Raw { get; }

        public static Fix64 FromRaw(long raw) => new Fix64(raw);

        public static Fix64 FromInt(int value) => new Fix64((long)value << FractionalBits);

        /// <summary>Exact ratio <paramref name="numerator"/> / <paramref name="denominator"/>, rounded to the nearest representable value.</summary>
        public static Fix64 FromRatio(long numerator, long denominator) => new Fix64(DivideRaw(numerator, denominator));

        /// <summary>
        /// Parses an invariant-culture decimal string (as used in content files). Parsing goes
        /// through <see cref="decimal"/>, which is computed in software and is deterministic.
        /// </summary>
        public static Fix64 Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            decimal value = decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            return FromDecimal(value);
        }

        public static Fix64 FromDecimal(decimal value)
        {
            decimal scaled = decimal.Round(value * OneDecimal, 0, MidpointRounding.AwayFromZero);
            if (scaled > long.MaxValue || scaled < long.MinValue)
            {
                throw new OverflowException($"Value {value} is outside the Fix64 range.");
            }

            return new Fix64((long)scaled);
        }

        public static Fix64 operator +(Fix64 a, Fix64 b) => new Fix64(checked(a.Raw + b.Raw));

        public static Fix64 operator -(Fix64 a, Fix64 b) => new Fix64(checked(a.Raw - b.Raw));

        public static Fix64 operator -(Fix64 value) => new Fix64(checked(-value.Raw));

        public static Fix64 operator *(Fix64 a, Fix64 b)
        {
            bool negative = (a.Raw < 0) != (b.Raw < 0);
            MultiplyUnsigned(UnsignedAbs(a.Raw), UnsignedAbs(b.Raw), out ulong high, out ulong low);

            // Divide the 128-bit product by 2^32, rounding half away from zero.
            ulong roundedLow = unchecked(low + (1UL << (FractionalBits - 1)));
            if (roundedLow < low)
            {
                high++;
            }

            if ((high >> FractionalBits) != 0)
            {
                throw new OverflowException("Fix64 multiplication overflow.");
            }

            ulong magnitude = (high << FractionalBits) | (roundedLow >> FractionalBits);
            return new Fix64(ApplySign(magnitude, negative));
        }

        public static Fix64 operator /(Fix64 a, Fix64 b) => new Fix64(DivideRaw(a.Raw, b.Raw));

        public static bool operator ==(Fix64 a, Fix64 b) => a.Raw == b.Raw;

        public static bool operator !=(Fix64 a, Fix64 b) => a.Raw != b.Raw;

        public static bool operator <(Fix64 a, Fix64 b) => a.Raw < b.Raw;

        public static bool operator >(Fix64 a, Fix64 b) => a.Raw > b.Raw;

        public static bool operator <=(Fix64 a, Fix64 b) => a.Raw <= b.Raw;

        public static bool operator >=(Fix64 a, Fix64 b) => a.Raw >= b.Raw;

        public static Fix64 Abs(Fix64 value) => value.Raw < 0 ? -value : value;

        public static Fix64 Min(Fix64 a, Fix64 b) => a.Raw <= b.Raw ? a : b;

        public static Fix64 Max(Fix64 a, Fix64 b) => a.Raw >= b.Raw ? a : b;

        public static Fix64 Clamp(Fix64 value, Fix64 min, Fix64 max)
        {
            if (min > max)
            {
                throw new ArgumentException("min must not be greater than max.");
            }

            return value < min ? min : value > max ? max : value;
        }

        /// <summary>Square root, rounded to the nearest representable value. Throws for negative input.</summary>
        public static Fix64 Sqrt(Fix64 value)
        {
            if (value.Raw < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Square root of a negative number.");
            }

            // sqrt(raw / 2^32) * 2^32 = sqrt(raw * 2^32): integer square root of a 96-bit number,
            // two bits at a time (digit-by-digit method).
            ulong raw = (ulong)value.Raw;
            ulong high = raw >> FractionalBits;
            ulong low = raw << FractionalBits;
            ulong remainder = 0;
            ulong root = 0;
            for (int pair = 63; pair >= 0; pair--)
            {
                ulong bits = pair >= 32 ? (high >> ((pair - 32) * 2)) & 3UL : (low >> (pair * 2)) & 3UL;
                remainder = (remainder << 2) | bits;
                root <<= 1;
                ulong test = (root << 1) | 1UL;
                if (remainder >= test)
                {
                    remainder -= test;
                    root |= 1UL;
                }
            }

            // remainder = n - root^2; round up when n is past (root + 0.5)^2.
            if (remainder > root)
            {
                root++;
            }

            return new Fix64((long)root);
        }

        /// <summary>Largest whole number less than or equal to the value.</summary>
        public static Fix64 Floor(Fix64 value) => new Fix64(value.Raw & ~FractionMask);

        /// <summary>Smallest whole number greater than or equal to the value.</summary>
        public static Fix64 Ceiling(Fix64 value)
        {
            Fix64 floor = Floor(value);
            return floor.Raw == value.Raw ? floor : floor + One;
        }

        /// <summary>Whole part, rounded toward negative infinity.</summary>
        public int FloorToInt() => checked((int)(Raw >> FractionalBits));

        /// <summary>For display and rendering only. Never feed the result back into the simulation.</summary>
        public double ToDouble() => Raw / (double)OneRaw;

        /// <summary>For display and rendering only. Never feed the result back into the simulation.</summary>
        public float ToFloat() => (float)ToDouble();

        public decimal ToDecimal() => Raw / OneDecimal;

        public bool Equals(Fix64 other) => Raw == other.Raw;

        public override bool Equals(object? obj) => obj is Fix64 other && Raw == other.Raw;

        public override int GetHashCode() => Raw.GetHashCode();

        public int CompareTo(Fix64 other) => Raw.CompareTo(other.Raw);

        public override string ToString() => ToDecimal().ToString(CultureInfo.InvariantCulture);

        /// <summary>Computes round(numerator * 2^32 / denominator) with a 128-bit intermediate.</summary>
        private static long DivideRaw(long numerator, long denominator)
        {
            if (denominator == 0)
            {
                throw new DivideByZeroException();
            }

            bool negative = (numerator < 0) != (denominator < 0);
            ulong n = UnsignedAbs(numerator);
            ulong d = UnsignedAbs(denominator);

            // 128-bit dividend n * 2^32 split into high and low words.
            ulong high = n >> FractionalBits;
            ulong low = n << FractionalBits;
            if (high >= d)
            {
                throw new OverflowException("Fix64 division overflow.");
            }

            // Binary long division of the low word; the quotient fits in 64 bits because high < d.
            ulong remainder = high;
            ulong quotient = 0;
            for (int bit = 63; bit >= 0; bit--)
            {
                ulong carry = remainder >> 63;
                remainder = (remainder << 1) | ((low >> bit) & 1UL);
                quotient <<= 1;
                if (carry != 0 || remainder >= d)
                {
                    remainder = unchecked(remainder - d);
                    quotient |= 1UL;
                }
            }

            // Round half away from zero: 2 * remainder >= d, written without overflow.
            if (remainder >= d - remainder)
            {
                quotient = checked(quotient + 1);
            }

            return ApplySign(quotient, negative);
        }

        private static void MultiplyUnsigned(ulong a, ulong b, out ulong high, out ulong low)
        {
            ulong aLow = a & 0xFFFFFFFFUL;
            ulong aHigh = a >> 32;
            ulong bLow = b & 0xFFFFFFFFUL;
            ulong bHigh = b >> 32;

            ulong lowLow = aLow * bLow;
            ulong lowHigh = aLow * bHigh;
            ulong highLow = aHigh * bLow;
            ulong highHigh = aHigh * bHigh;

            ulong middle = (lowLow >> 32) + (lowHigh & 0xFFFFFFFFUL) + (highLow & 0xFFFFFFFFUL);
            low = (middle << 32) | (lowLow & 0xFFFFFFFFUL);
            high = highHigh + (lowHigh >> 32) + (highLow >> 32) + (middle >> 32);
        }

        private static ulong UnsignedAbs(long value) => value < 0 ? unchecked((ulong)(-value)) : (ulong)value;

        private static long ApplySign(ulong magnitude, bool negative)
        {
            if (negative)
            {
                if (magnitude > 1UL << 63)
                {
                    throw new OverflowException("Fix64 result out of range.");
                }

                return unchecked(-(long)magnitude);
            }

            if (magnitude > long.MaxValue)
            {
                throw new OverflowException("Fix64 result out of range.");
            }

            return (long)magnitude;
        }
    }
}
