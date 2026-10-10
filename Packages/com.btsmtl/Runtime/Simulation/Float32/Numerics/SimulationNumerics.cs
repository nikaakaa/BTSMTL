using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace ThirdPersonSimulation
{
    public static class Float32SimulationNumericProfile
    {
        public const int AbiVersion = 8;

        public static SimulationNumericProfile Value { get; } = new SimulationNumericProfile(
            new NumericProfileId("float32-ieee754"),
            new TargetAbiVersion(AbiVersion),
            32,
            SimulationNumericRoundingMode.Ieee754NearestEven,
            SimulationNumericOverflowMode.RejectNonFinite,
            false);
    }

    public sealed class Float32SimulationTargetManifest
    {
        public Float32SimulationTargetManifest(
            SimulationNumericProfile profile,
            string scalarType,
            string vector2Type,
            string vector3Type,
            string yawType,
            string canonicalCodec,
            SimulationExecutionTargetManifest executionTarget)
        {
            Profile = profile;
            ScalarType = SimulationIdentity.Require(scalarType, nameof(scalarType));
            Vector2Type = SimulationIdentity.Require(vector2Type, nameof(vector2Type));
            Vector3Type = SimulationIdentity.Require(vector3Type, nameof(vector3Type));
            YawType = SimulationIdentity.Require(yawType, nameof(yawType));
            CanonicalCodec = SimulationIdentity.Require(canonicalCodec, nameof(canonicalCodec));
            ExecutionTarget = executionTarget ?? throw new ArgumentNullException(nameof(executionTarget));
            if (executionTarget.NumericProfile != profile)
                throw new ArgumentException("Numeric Target and Execution Target profiles must match.", nameof(executionTarget));
        }

        public SimulationNumericProfile Profile { get; }
        public string ScalarType { get; }
        public string Vector2Type { get; }
        public string Vector3Type { get; }
        public string YawType { get; }
        public string CanonicalCodec { get; }
        public SimulationExecutionTargetManifest ExecutionTarget { get; }
    }

    public static class Float32SimulationTarget
    {
        static readonly Float32SimulationTargetManifest s_Manifest =
            new Float32SimulationTargetManifest(
                Float32SimulationNumericProfile.Value,
                nameof(Float32Scalar),
                nameof(Float32Vector2),
                nameof(Float32Vector3),
                nameof(Float32Yaw),
                "float32-le/v1",
                new SimulationExecutionTargetManifest(
                    "character-execution/float32/v2",
                    Float32SimulationNumericProfile.Value,
                    GameplayAbilityOperationSet.Version,
                    GameplayAbilityOperationSet.Operations));

        public static Float32SimulationTargetManifest Manifest => s_Manifest;
    }

    public readonly struct Float32ScalarConversion
    {
        public Float32ScalarConversion(string sourceIdentity, double sourceValue, Float32Scalar value)
        {
            SourceIdentity = SimulationIdentity.Require(sourceIdentity, nameof(sourceIdentity));
            SourceValue = sourceValue;
            Value = value;
            AbsoluteError = Math.Abs(sourceValue - value.ToDouble());
        }

        public string SourceIdentity { get; }
        public double SourceValue { get; }
        public Float32Scalar Value { get; }
        public double AbsoluteError { get; }
        public bool WasRounded => AbsoluteError > 0d;
    }

    public sealed class SimulationNumericConversionException : Exception
    {
        public SimulationNumericConversionException(string sourceIdentity, double sourceValue, Exception innerException)
            : base($"Float32 conversion failed at '{sourceIdentity}' for value '{sourceValue.ToString("R", CultureInfo.InvariantCulture)}'.", innerException)
        {
            SourceIdentity = sourceIdentity;
            SourceValue = sourceValue;
        }

        public string SourceIdentity { get; }
        public double SourceValue { get; }
    }

    public static class Float32ScalarBoundary
    {
        public static Float32ScalarConversion LowerAuthoring(double value, string sourceIdentity)
        {
            string identity = SimulationIdentity.Require(sourceIdentity, nameof(sourceIdentity));
            try
            {
                return new Float32ScalarConversion(identity, value, Float32Scalar.FromDouble(value));
            }
            catch (Exception exception) when (exception is ArgumentOutOfRangeException || exception is OverflowException)
            {
                throw new SimulationNumericConversionException(identity, value, exception);
            }
        }

        public static Float32Scalar ConvertExternal(double value, string sourceIdentity)
        {
            string identity = SimulationIdentity.Require(sourceIdentity, nameof(sourceIdentity));
            try
            {
                return Float32Scalar.FromDouble(value);
            }
            catch (Exception exception) when (exception is ArgumentOutOfRangeException || exception is OverflowException)
            {
                throw new SimulationNumericConversionException(identity, value, exception);
            }
        }
    }

    [CanonicalContract]
    public readonly partial struct Float32Scalar : IEquatable<Float32Scalar>, IComparable<Float32Scalar>
    {
        Float32Scalar(uint bits)
        {
            Bits = bits;
        }

        public uint Bits { get; }
        [CanonicalValidation]
        void RequireCanonicalBits()
        {
            float value = Value;
            if (float.IsNaN(value) || float.IsInfinity(value) || Bits == 0x80000000u)
                throw new ArgumentOutOfRangeException(nameof(Bits));
        }
        public float Value => BitConverter.Int32BitsToSingle(unchecked((int)Bits));
        public static Float32Scalar Zero => new Float32Scalar(0);
        public static Float32Scalar One => FromSingle(1f);
        public static Float32Scalar MinValue => FromSingle(-float.MaxValue);
        public static Float32Scalar MaxValue => FromSingle(float.MaxValue);

        public static Float32Scalar FromBits(uint bits)
        {
            return FromSingle(BitConverter.Int32BitsToSingle(unchecked((int)bits)));
        }

        public static Float32Scalar FromInt64(long value) => FromDouble(value);

        public static Float32Scalar FromDouble(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            float converted = (float)value;
            if (float.IsNaN(converted) || float.IsInfinity(converted))
                throw new OverflowException($"Simulation Float32 value '{value.ToString("R", CultureInfo.InvariantCulture)}' is not finite.");
            return FromSingle(converted);
        }

        public static Float32Scalar FromSingle(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (value == 0f)
                value = 0f;
            return new Float32Scalar(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        }

        public double ToDouble() => Value;
        public float ToSingle() => Value;
        public int CompareTo(Float32Scalar other) => Value.CompareTo(other.Value);
        public bool Equals(Float32Scalar other) => Bits == other.Bits;
        public override bool Equals(object obj) => obj is Float32Scalar other && Equals(other);
        public override int GetHashCode() => Bits.GetHashCode();
        public override string ToString() => Value.ToString("R", CultureInfo.InvariantCulture);

        public static Float32Scalar Abs(Float32Scalar value) => FromSingle(Math.Abs(value.Value));
        public static Float32Scalar Min(Float32Scalar left, Float32Scalar right) => left <= right ? left : right;
        public static Float32Scalar Max(Float32Scalar left, Float32Scalar right) => left >= right ? left : right;

        public static Float32Scalar Clamp(Float32Scalar value, Float32Scalar minimum, Float32Scalar maximum)
        {
            if (minimum > maximum)
                throw new ArgumentException("Minimum exceeds maximum.");
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        public static Float32Scalar Lerp(Float32Scalar from, Float32Scalar to, Float32Scalar amount)
        {
            return from + (to - from) * Clamp(amount, Zero, One);
        }

        public static Float32Scalar Sqrt(Float32Scalar value)
        {
            if (value < Zero)
                throw new ArgumentOutOfRangeException(nameof(value));
            return FromDouble(Math.Sqrt(value.Value));
        }

        public static Float32Scalar operator +(Float32Scalar left, Float32Scalar right) => FromSingle(left.Value + right.Value);
        public static Float32Scalar operator -(Float32Scalar left, Float32Scalar right) => FromSingle(left.Value - right.Value);
        public static Float32Scalar operator -(Float32Scalar value) => FromSingle(-value.Value);
        public static Float32Scalar operator *(Float32Scalar left, Float32Scalar right) => FromSingle(left.Value * right.Value);
        public static Float32Scalar operator /(Float32Scalar left, Float32Scalar right)
        {
            if (right == Zero)
                throw new DivideByZeroException();
            return FromSingle(left.Value / right.Value);
        }

        public static bool operator ==(Float32Scalar left, Float32Scalar right) => left.Bits == right.Bits;
        public static bool operator !=(Float32Scalar left, Float32Scalar right) => left.Bits != right.Bits;
        public static bool operator <(Float32Scalar left, Float32Scalar right) => left.Value < right.Value;
        public static bool operator >(Float32Scalar left, Float32Scalar right) => left.Value > right.Value;
        public static bool operator <=(Float32Scalar left, Float32Scalar right) => left.Value <= right.Value;
        public static bool operator >=(Float32Scalar left, Float32Scalar right) => left.Value >= right.Value;
    }

    [CanonicalContract]
    public readonly partial struct Float32Vector2 : IEquatable<Float32Vector2>
    {
        public Float32Vector2(Float32Scalar x, Float32Scalar y)
        {
            X = x;
            Y = y;
        }

        public Float32Scalar X { get; }
        public Float32Scalar Y { get; }
        public static Float32Vector2 Zero => new Float32Vector2(Float32Scalar.Zero, Float32Scalar.Zero);
        public Float32Scalar SqrMagnitude => X * X + Y * Y;
        public Float32Scalar Magnitude => Float32Scalar.Sqrt(SqrMagnitude);
        public Float32Vector2 Normalized
        {
            get
            {
                Float32Scalar magnitude = Magnitude;
                return magnitude == Float32Scalar.Zero ? Zero : new Float32Vector2(X / magnitude, Y / magnitude);
            }
        }
        public bool Equals(Float32Vector2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Float32Vector2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public static Float32Vector2 operator +(Float32Vector2 left, Float32Vector2 right) => new Float32Vector2(left.X + right.X, left.Y + right.Y);
        public static Float32Vector2 operator -(Float32Vector2 left, Float32Vector2 right) => new Float32Vector2(left.X - right.X, left.Y - right.Y);
        public static Float32Vector2 operator *(Float32Vector2 value, Float32Scalar scale) => new Float32Vector2(value.X * scale, value.Y * scale);
        public static bool operator ==(Float32Vector2 left, Float32Vector2 right) => left.Equals(right);
        public static bool operator !=(Float32Vector2 left, Float32Vector2 right) => !left.Equals(right);
    }

    [CanonicalContract]
    public readonly partial struct Float32Vector3 : IEquatable<Float32Vector3>
    {
        public Float32Vector3(Float32Scalar x, Float32Scalar y, Float32Scalar z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Float32Scalar X { get; }
        public Float32Scalar Y { get; }
        public Float32Scalar Z { get; }
        public static Float32Vector3 Zero => new Float32Vector3(Float32Scalar.Zero, Float32Scalar.Zero, Float32Scalar.Zero);
        public Float32Scalar SqrMagnitude => X * X + Y * Y + Z * Z;
        public Float32Scalar Magnitude => Float32Scalar.Sqrt(SqrMagnitude);
        public Float32Vector3 Normalized
        {
            get
            {
                Float32Scalar magnitude = Magnitude;
                return magnitude == Float32Scalar.Zero ? Zero : new Float32Vector3(X / magnitude, Y / magnitude, Z / magnitude);
            }
        }
        public bool Equals(Float32Vector3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is Float32Vector3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public static Float32Vector3 operator +(Float32Vector3 left, Float32Vector3 right) => new Float32Vector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        public static Float32Vector3 operator -(Float32Vector3 left, Float32Vector3 right) => new Float32Vector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        public static Float32Vector3 operator *(Float32Vector3 value, Float32Scalar scale) => new Float32Vector3(value.X * scale, value.Y * scale, value.Z * scale);
        public static bool operator ==(Float32Vector3 left, Float32Vector3 right) => left.Equals(right);
        public static bool operator !=(Float32Vector3 left, Float32Vector3 right) => !left.Equals(right);
    }

    [CanonicalContract]
    public readonly partial struct Float32Yaw : IEquatable<Float32Yaw>
    {
        static readonly Float32Scalar FullTurn = Float32Scalar.FromInt64(360);
        static readonly Float32Scalar HalfTurn = Float32Scalar.FromInt64(180);

        public Float32Yaw(Float32Scalar degrees)
        {
            Degrees = Normalize(degrees);
        }

        public Float32Scalar Degrees { get; }
        public static Float32Yaw Zero => new Float32Yaw(Float32Scalar.Zero);
        public bool Equals(Float32Yaw other) => Degrees == other.Degrees;
        public override bool Equals(object obj) => obj is Float32Yaw other && Equals(other);
        public override int GetHashCode() => Degrees.GetHashCode();
        public static bool operator ==(Float32Yaw left, Float32Yaw right) => left.Equals(right);
        public static bool operator !=(Float32Yaw left, Float32Yaw right) => !left.Equals(right);

        [CanonicalValidation]
        void RequireCanonicalDegrees()
        {
            if (Degrees != Normalize(Degrees))
                throw new System.IO.InvalidDataException("Float32 yaw must use canonical degrees.");
        }

        static Float32Scalar Normalize(Float32Scalar value)
        {
            float normalized = value.ToSingle() % FullTurn.ToSingle();
            if (normalized >= HalfTurn.ToSingle())
                normalized -= FullTurn.ToSingle();
            if (normalized < -HalfTurn.ToSingle())
                normalized += FullTurn.ToSingle();
            return Float32Scalar.FromSingle(normalized);
        }
    }

    public static class Float32Angle
    {
        public static Float32Yaw FromPlanarDirection(Float32Vector2 direction)
        {
            return FromPlanarDirection(direction.X, direction.Y);
        }

        public static Float32Yaw FromPlanarDirection(Float32Scalar x, Float32Scalar z)
        {
            if (x == Float32Scalar.Zero && z == Float32Scalar.Zero)
                return Float32Yaw.Zero;
            return new Float32Yaw(Float32Scalar.FromDouble(Math.Atan2(x.ToDouble(), z.ToDouble()) * 180d / Math.PI));
        }

        public static Float32Scalar Delta(Float32Yaw from, Float32Yaw to)
        {
            return new Float32Yaw(to.Degrees - from.Degrees).Degrees;
        }

        public static Float32Vector3 RotatePlanar(Float32Vector3 value, Float32Yaw yaw)
        {
            SinCos(yaw, out Float32Scalar sine, out Float32Scalar cosine);
            return new Float32Vector3(
                value.X * cosine + value.Z * sine,
                value.Y,
                -value.X * sine + value.Z * cosine);
        }

        public static void SinCos(Float32Yaw yaw, out Float32Scalar sine, out Float32Scalar cosine)
        {
            double radians = yaw.Degrees.ToDouble() * Math.PI / 180d;
            sine = Float32Scalar.FromDouble(Math.Sin(radians));
            cosine = Float32Scalar.FromDouble(Math.Cos(radians));
        }
    }
}
