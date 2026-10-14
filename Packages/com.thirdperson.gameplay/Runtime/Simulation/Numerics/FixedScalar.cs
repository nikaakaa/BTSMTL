using System;
using System.Globalization;

namespace ThirdPersonSimulation.Fixed
{
    public readonly partial struct FixedScalar : IEquatable<FixedScalar>, IComparable<FixedScalar>
    {
        public const int FractionalBits = 32;
        public const long OneRaw = 1L << FractionalBits;

        FixedScalar(long raw)
        {
            Raw = raw;
        }

        public long Raw { get; }
        public static FixedScalar Zero => new FixedScalar(0L);
        public static FixedScalar One => new FixedScalar(OneRaw);
        public static FixedScalar MinValue => new FixedScalar(long.MinValue);
        public static FixedScalar MaxValue => new FixedScalar(long.MaxValue);

        public static FixedScalar FromRaw(long raw) => new FixedScalar(raw);

        public static FixedScalar FromInt64(long value)
        {
            return new FixedScalar(checked(value * OneRaw));
        }

        public static FixedScalar FromRatio(long numerator, long denominator)
        {
            if (denominator == 0)
                throw new DivideByZeroException();
            return DivideScaled(numerator, denominator);
        }

        public static FixedScalar FromDouble(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            return FromDecimal(checked((decimal)value));
        }

        public static FixedScalar FromDecimal(decimal value)
        {
            decimal scaled = checked(value * OneRaw);
            decimal rounded = decimal.Round(scaled, 0, MidpointRounding.ToEven);
            if (rounded < long.MinValue || rounded > long.MaxValue)
                throw new OverflowException($"Simulation Fixed value '{value.ToString(CultureInfo.InvariantCulture)}' exceeds Q32.32.");
            return new FixedScalar((long)rounded);
        }

        public static FixedScalar FromSingle(float value) => FromDouble(value);
        public double ToDouble() => Raw / (double)OneRaw;
        public float ToSingle() => (float)ToDouble();
        public int CompareTo(FixedScalar other) => Raw.CompareTo(other.Raw);
        public bool Equals(FixedScalar other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is FixedScalar other && Equals(other);
        public override int GetHashCode() => Raw.GetHashCode();
        public override string ToString() => ToDouble().ToString("R", CultureInfo.InvariantCulture);

        public static FixedScalar Abs(FixedScalar value)
        {
            if (value.Raw == long.MinValue)
                throw new OverflowException("Fixed absolute value overflowed.");
            return new FixedScalar(value.Raw < 0 ? -value.Raw : value.Raw);
        }

        public static FixedScalar Min(FixedScalar left, FixedScalar right) => left <= right ? left : right;
        public static FixedScalar Max(FixedScalar left, FixedScalar right) => left >= right ? left : right;

        public static FixedScalar Clamp(FixedScalar value, FixedScalar minimum, FixedScalar maximum)
        {
            if (minimum > maximum)
                throw new ArgumentException("Minimum exceeds maximum.");
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        public static FixedScalar Lerp(FixedScalar from, FixedScalar to, FixedScalar amount)
        {
            return from + (to - from) * Clamp(amount, Zero, One);
        }

        public static FixedScalar Sqrt(FixedScalar value)
        {
            if (value < Zero)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (value == Zero)
                return Zero;
            return new FixedScalar((long)IntegerSquareRootScaled((ulong)value.Raw));
        }

        public ulong CeilingToUInt64()
        {
            if (Raw < 0)
                throw new OverflowException("Negative Fixed value cannot convert to UInt64.");
            ulong whole = (ulong)(Raw >> FractionalBits);
            return (Raw & (OneRaw - 1)) == 0 ? whole : checked(whole + 1UL);
        }

        public int TruncateToInt32()
        {
            long whole = Raw / OneRaw;
            return checked((int)whole);
        }

        public static FixedScalar operator +(FixedScalar left, FixedScalar right) => new FixedScalar(checked(left.Raw + right.Raw));
        public static FixedScalar operator -(FixedScalar left, FixedScalar right) => new FixedScalar(checked(left.Raw - right.Raw));
        public static FixedScalar operator -(FixedScalar value)
        {
            if (value.Raw == long.MinValue)
                throw new OverflowException("Fixed negation overflowed.");
            return new FixedScalar(-value.Raw);
        }
        public static FixedScalar operator *(FixedScalar left, FixedScalar right)
        {
            return MultiplyScaled(left.Raw, right.Raw);
        }
        public static FixedScalar operator /(FixedScalar left, FixedScalar right)
        {
            if (right == Zero)
                throw new DivideByZeroException();
            return DivideScaled(left.Raw, right.Raw);
        }
        public static FixedScalar operator %(FixedScalar left, FixedScalar right)
        {
            if (right == Zero)
                throw new DivideByZeroException();
            return new FixedScalar(left.Raw % right.Raw);
        }

        public static bool operator ==(FixedScalar left, FixedScalar right) => left.Raw == right.Raw;
        public static bool operator !=(FixedScalar left, FixedScalar right) => left.Raw != right.Raw;
        public static bool operator <(FixedScalar left, FixedScalar right) => left.Raw < right.Raw;
        public static bool operator >(FixedScalar left, FixedScalar right) => left.Raw > right.Raw;
        public static bool operator <=(FixedScalar left, FixedScalar right) => left.Raw <= right.Raw;
        public static bool operator >=(FixedScalar left, FixedScalar right) => left.Raw >= right.Raw;

        static FixedScalar MultiplyScaled(long left, long right)
        {
            bool negative = left < 0 != right < 0;
            MultiplyUnsigned(AbsoluteRaw(left), AbsoluteRaw(right), out ulong high, out ulong low);
            if ((high >> FractionalBits) != 0UL)
                throw new OverflowException("Fixed arithmetic overflowed Q32.32.");

            ulong quotient = (high << FractionalBits) | (low >> FractionalBits);
            ulong remainder = low & (OneRaw - 1UL);
            return FromRoundedMagnitude(quotient, remainder, OneRaw, negative);
        }

        static FixedScalar DivideScaled(long numerator, long denominator)
        {
            ulong absoluteNumerator = AbsoluteRaw(numerator);
            ulong absoluteDenominator = AbsoluteRaw(denominator);
            bool negative = numerator < 0 != denominator < 0;
            ulong whole = absoluteNumerator / absoluteDenominator;
            if (whole > uint.MaxValue)
                throw new OverflowException("Fixed arithmetic overflowed Q32.32.");
            ulong quotient = whole << FractionalBits;
            ulong remainderHigh = 0UL;
            ulong remainderLow = absoluteNumerator % absoluteDenominator;

            for (int bitIndex = FractionalBits - 1; bitIndex >= 0; bitIndex--)
            {
                ulong carry = remainderHigh >> 63;
                remainderHigh = (remainderHigh << 1) | (remainderLow >> 63);
                remainderLow <<= 1;
                if (carry == 0UL && remainderHigh == 0UL && remainderLow < absoluteDenominator)
                    continue;

                ulong previousLow = remainderLow;
                remainderLow -= absoluteDenominator;
                if (previousLow < absoluteDenominator)
                    remainderHigh--;
                quotient |= 1UL << bitIndex;
            }

            return FromRoundedMagnitude(quotient, remainderLow, absoluteDenominator, negative);
        }

        static FixedScalar FromRoundedMagnitude(ulong quotient, ulong remainder, ulong denominator, bool negative)
        {
            ulong half = denominator >> 1;
            bool roundUp = (denominator & 1UL) == 0UL
                ? remainder > half || remainder == half && (quotient & 1UL) != 0UL
                : remainder > half;
            if (roundUp)
            {
                if (quotient == ulong.MaxValue)
                    throw new OverflowException("Fixed arithmetic overflowed Q32.32.");
                quotient++;
            }

            ulong limit = negative ? 0x8000000000000000UL : long.MaxValue;
            if (quotient > limit)
                throw new OverflowException("Fixed arithmetic overflowed Q32.32.");
            if (!negative)
                return new FixedScalar((long)quotient);
            return quotient == 0x8000000000000000UL
                ? new FixedScalar(long.MinValue)
                : new FixedScalar(-(long)quotient);
        }

        static ulong AbsoluteRaw(long value)
        {
            return value < 0
                ? unchecked((ulong)(-(value + 1))) + 1UL
                : (ulong)value;
        }

        static void MultiplyUnsigned(ulong left, ulong right, out ulong high, out ulong low)
        {
            ulong leftLow = (uint)left;
            ulong leftHigh = left >> 32;
            ulong rightLow = (uint)right;
            ulong rightHigh = right >> 32;
            ulong lowProduct = leftLow * rightLow;
            ulong middle = leftHigh * rightLow + (lowProduct >> 32);
            ulong middleLow = (uint)middle;
            ulong middleHigh = middle >> 32;
            middleLow += leftLow * rightHigh;
            high = leftHigh * rightHigh + middleHigh + (middleLow >> 32);
            low = (middleLow << 32) | (uint)lowProduct;
        }

        static ulong IntegerSquareRootScaled(ulong raw)
        {
            ulong root = 0UL;
            ulong remainder = 0UL;
            for (int pairIndex = 47; pairIndex >= 0; pairIndex--)
            {
                int lowBitIndex = pairIndex * 2;
                ulong pair = ShiftedRawBit(raw, lowBitIndex) |
                             (ShiftedRawBit(raw, lowBitIndex + 1) << 1);
                remainder = (remainder << 2) | pair;
                ulong candidate = (root << 2) | 1UL;
                if (remainder >= candidate)
                {
                    remainder -= candidate;
                    root = (root << 1) | 1UL;
                }
                else
                {
                    root <<= 1;
                }
            }
            return root;
        }

        static ulong ShiftedRawBit(ulong raw, int bitIndex)
        {
            if (bitIndex < FractionalBits || bitIndex >= FractionalBits + 64)
                return 0UL;
            return (raw >> (bitIndex - FractionalBits)) & 1UL;
        }
    }
}
