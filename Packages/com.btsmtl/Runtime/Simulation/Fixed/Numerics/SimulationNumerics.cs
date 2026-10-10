using System;
using System.Collections.Generic;
using System.Globalization;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    public static class FixedSimulationNumericProfile
    {
        public const int AbiVersion = 7;
        public const int FractionalBits = FixedScalar.FractionalBits;

        public static SimulationNumericProfile Value { get; } = new SimulationNumericProfile(
            new NumericProfileId("fixed-q32.32"),
            new TargetAbiVersion(AbiVersion),
            64,
            SimulationNumericRoundingMode.FixedNearestEven,
            SimulationNumericOverflowMode.RejectOverflow,
            true);
    }

    public sealed class FixedSimulationTargetManifest
    {
        public FixedSimulationTargetManifest(
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

    public static class FixedSimulationTarget
    {
        static readonly FixedSimulationTargetManifest s_Manifest = new FixedSimulationTargetManifest(
            FixedSimulationNumericProfile.Value,
            nameof(FixedScalar),
            nameof(FixedVector2),
            nameof(FixedVector3),
            nameof(FixedYaw),
            "fixed-q32.32-le/v1",
            new SimulationExecutionTargetManifest(
                "character-execution/fixed/v2",
                FixedSimulationNumericProfile.Value,
                GameplayAbilityOperationSet.Version,
                GameplayAbilityOperationSet.Operations));

        public static FixedSimulationTargetManifest Manifest => s_Manifest;
    }

    public readonly struct FixedScalarConversion
    {
        public FixedScalarConversion(string sourceIdentity, double sourceValue, FixedScalar value)
        {
            SourceIdentity = SimulationIdentity.Require(sourceIdentity, nameof(sourceIdentity));
            SourceValue = sourceValue;
            Value = value;
            AbsoluteError = Math.Abs(sourceValue - value.ToDouble());
        }

        public string SourceIdentity { get; }
        public double SourceValue { get; }
        public FixedScalar Value { get; }
        public double AbsoluteError { get; }
        public bool WasRounded => AbsoluteError > 0d;
    }

    public sealed class SimulationNumericConversionException : Exception
    {
        public SimulationNumericConversionException(string sourceIdentity, double sourceValue, Exception innerException)
            : base($"Fixed conversion failed at '{sourceIdentity}' for value '{sourceValue.ToString("R", CultureInfo.InvariantCulture)}'.", innerException)
        {
            SourceIdentity = sourceIdentity;
            SourceValue = sourceValue;
        }

        public string SourceIdentity { get; }
        public double SourceValue { get; }
    }

    public static class FixedScalarBoundary
    {
        public static FixedScalarConversion LowerAuthoring(double value, string sourceIdentity)
        {
            string identity = SimulationIdentity.Require(sourceIdentity, nameof(sourceIdentity));
            try
            {
                return new FixedScalarConversion(identity, value, FixedScalar.FromDouble(value));
            }
            catch (Exception exception) when (exception is ArgumentOutOfRangeException || exception is OverflowException)
            {
                throw new SimulationNumericConversionException(identity, value, exception);
            }
        }

        public static FixedScalar ConvertExternal(double value, string sourceIdentity)
        {
            string identity = SimulationIdentity.Require(sourceIdentity, nameof(sourceIdentity));
            try
            {
                return FixedScalar.FromDouble(value);
            }
            catch (Exception exception) when (exception is ArgumentOutOfRangeException || exception is OverflowException)
            {
                throw new SimulationNumericConversionException(identity, value, exception);
            }
        }
    }

    [CanonicalContract]
    public readonly partial struct FixedVector2 : IEquatable<FixedVector2>
    {
        public FixedVector2(FixedScalar x, FixedScalar y)
        {
            X = x;
            Y = y;
        }

        public FixedScalar X { get; }
        public FixedScalar Y { get; }
        public static FixedVector2 Zero => new FixedVector2(FixedScalar.Zero, FixedScalar.Zero);
        public FixedScalar SqrMagnitude => X * X + Y * Y;
        public FixedScalar Magnitude => FixedScalar.Sqrt(SqrMagnitude);
        public FixedVector2 Normalized
        {
            get
            {
                FixedScalar magnitude = Magnitude;
                return magnitude == FixedScalar.Zero ? Zero : new FixedVector2(X / magnitude, Y / magnitude);
            }
        }
        public static FixedScalar Dot(FixedVector2 left, FixedVector2 right) => left.X * right.X + left.Y * right.Y;
        public bool Equals(FixedVector2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is FixedVector2 other && Equals(other);
        public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode());
        public override string ToString() => $"({X},{Y})";
        public static FixedVector2 operator +(FixedVector2 left, FixedVector2 right) => new FixedVector2(left.X + right.X, left.Y + right.Y);
        public static FixedVector2 operator -(FixedVector2 left, FixedVector2 right) => new FixedVector2(left.X - right.X, left.Y - right.Y);
        public static FixedVector2 operator -(FixedVector2 value) => new FixedVector2(-value.X, -value.Y);
        public static FixedVector2 operator *(FixedVector2 value, FixedScalar scale) => new FixedVector2(value.X * scale, value.Y * scale);
        public static bool operator ==(FixedVector2 left, FixedVector2 right) => left.Equals(right);
        public static bool operator !=(FixedVector2 left, FixedVector2 right) => !left.Equals(right);
    }

    [CanonicalContract]
    public readonly partial struct FixedVector3 : IEquatable<FixedVector3>
    {
        public FixedVector3(FixedScalar x, FixedScalar y, FixedScalar z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public FixedScalar X { get; }
        public FixedScalar Y { get; }
        public FixedScalar Z { get; }
        public static FixedVector3 Zero => new FixedVector3(FixedScalar.Zero, FixedScalar.Zero, FixedScalar.Zero);
        public FixedScalar SqrMagnitude => X * X + Y * Y + Z * Z;
        public FixedScalar Magnitude => FixedScalar.Sqrt(SqrMagnitude);
        public FixedVector3 Normalized
        {
            get
            {
                FixedScalar magnitude = Magnitude;
                return magnitude == FixedScalar.Zero ? Zero : new FixedVector3(X / magnitude, Y / magnitude, Z / magnitude);
            }
        }
        public static FixedScalar Dot(FixedVector3 left, FixedVector3 right) => left.X * right.X + left.Y * right.Y + left.Z * right.Z;
        public static FixedVector3 Cross(FixedVector3 left, FixedVector3 right) => new FixedVector3(
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);
        public bool Equals(FixedVector3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is FixedVector3 other && Equals(other);
        public override int GetHashCode() => unchecked((X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode());
        public override string ToString() => $"({X},{Y},{Z})";
        public static FixedVector3 operator +(FixedVector3 left, FixedVector3 right) => new FixedVector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        public static FixedVector3 operator -(FixedVector3 left, FixedVector3 right) => new FixedVector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        public static FixedVector3 operator -(FixedVector3 value) => new FixedVector3(-value.X, -value.Y, -value.Z);
        public static FixedVector3 operator *(FixedVector3 value, FixedScalar scale) => new FixedVector3(value.X * scale, value.Y * scale, value.Z * scale);
        public static bool operator ==(FixedVector3 left, FixedVector3 right) => left.Equals(right);
        public static bool operator !=(FixedVector3 left, FixedVector3 right) => !left.Equals(right);
    }

    [CanonicalContract]
    public readonly partial struct FixedYaw : IEquatable<FixedYaw>
    {
        static readonly FixedScalar FullTurn = FixedScalar.FromInt64(360);
        static readonly FixedScalar HalfTurn = FixedScalar.FromInt64(180);

        public FixedYaw(FixedScalar degrees)
        {
            Degrees = Normalize(degrees);
        }

        public FixedScalar Degrees { get; }
        public static FixedYaw Zero => new FixedYaw(FixedScalar.Zero);
        public bool Equals(FixedYaw other) => Degrees == other.Degrees;
        public override bool Equals(object obj) => obj is FixedYaw other && Equals(other);
        public override int GetHashCode() => Degrees.GetHashCode();
        public override string ToString() => Degrees.ToString();
        public static bool operator ==(FixedYaw left, FixedYaw right) => left.Equals(right);
        public static bool operator !=(FixedYaw left, FixedYaw right) => !left.Equals(right);

        [CanonicalValidation]
        void RequireCanonicalDegrees()
        {
            if (Degrees != Normalize(Degrees))
                throw new System.IO.InvalidDataException("Fixed yaw must use canonical degrees.");
        }

        static FixedScalar Normalize(FixedScalar value)
        {
            FixedScalar normalized = value % FullTurn;
            if (normalized >= HalfTurn)
                normalized -= FullTurn;
            if (normalized < -HalfTurn)
                normalized += FullTurn;
            return normalized;
        }
    }

    public static class FixedAngle
    {
        static readonly long[] AtanRadians =
        {
            3373259426L, 1991351318L, 1052175346L, 534100635L,
            268086748L, 134174063L, 67103403L, 33553749L,
            16777131L, 8388597L, 4194303L, 2097152L,
            1048576L, 524288L, 262144L, 131072L,
            65536L, 32768L, 16384L, 8192L,
            4096L, 2048L, 1024L, 512L,
            256L, 128L, 64L, 32L,
            16L, 8L, 4L, 2L
        };

        const long PiRaw = 13493037705L;
        const long HalfPiRaw = 6746518852L;
        const long CordicGainRaw = 2608131496L;
        static readonly FixedScalar DegreesToRadians = FixedScalar.FromRaw(74961321L);
        static readonly FixedScalar RadiansToDegrees = FixedScalar.FromRaw(246083499208L);

        public static FixedYaw FromPlanarDirection(FixedVector2 direction) => FromPlanarDirection(direction.X, direction.Y);

        public static FixedYaw FromPlanarDirection(FixedScalar x, FixedScalar z)
        {
            if (x == FixedScalar.Zero && z == FixedScalar.Zero)
                return FixedYaw.Zero;
            long vectorX = z.Raw;
            long vectorY = x.Raw;
            long angle = 0L;
            if (vectorX < 0)
            {
                bool positiveY = vectorY >= 0;
                vectorX = checked(-vectorX);
                vectorY = checked(-vectorY);
                angle = positiveY ? PiRaw : -PiRaw;
            }
            for (int i = 0; i < AtanRadians.Length; i++)
            {
                long previousX = vectorX;
                if (vectorY > 0)
                {
                    vectorX = checked(vectorX + (vectorY >> i));
                    vectorY = checked(vectorY - (previousX >> i));
                    angle = checked(angle + AtanRadians[i]);
                }
                else
                {
                    vectorX = checked(vectorX - (vectorY >> i));
                    vectorY = checked(vectorY + (previousX >> i));
                    angle = checked(angle - AtanRadians[i]);
                }
            }
            return new FixedYaw(FixedScalar.FromRaw(angle) * RadiansToDegrees);
        }

        public static FixedScalar Delta(FixedYaw from, FixedYaw to) => new FixedYaw(to.Degrees - from.Degrees).Degrees;

        public static FixedVector3 RotatePlanar(FixedVector3 value, FixedYaw yaw)
        {
            SinCos(yaw, out FixedScalar sine, out FixedScalar cosine);
            return new FixedVector3(
                value.X * cosine + value.Z * sine,
                value.Y,
                -value.X * sine + value.Z * cosine);
        }

        public static void SinCos(FixedYaw yaw, out FixedScalar sine, out FixedScalar cosine)
        {
            long angle = (yaw.Degrees * DegreesToRadians).Raw;
            int sign = 1;
            if (angle > HalfPiRaw)
            {
                angle -= PiRaw;
                sign = -1;
            }
            else if (angle < -HalfPiRaw)
            {
                angle += PiRaw;
                sign = -1;
            }

            long x = CordicGainRaw;
            long y = 0L;
            for (int i = 0; i < AtanRadians.Length; i++)
            {
                long previousX = x;
                if (angle >= 0)
                {
                    x = checked(x - (y >> i));
                    y = checked(y + (previousX >> i));
                    angle -= AtanRadians[i];
                }
                else
                {
                    x = checked(x + (y >> i));
                    y = checked(y - (previousX >> i));
                    angle += AtanRadians[i];
                }
            }
            if (sign < 0)
            {
                x = -x;
                y = -y;
            }
            sine = FixedScalar.FromRaw(y);
            cosine = FixedScalar.FromRaw(x);
        }
    }
}
