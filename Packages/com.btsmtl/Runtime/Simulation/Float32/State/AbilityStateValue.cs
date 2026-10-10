using System;
using System.IO;

namespace ThirdPersonSimulation
{
	public readonly struct AbilityStateValue : IEquatable<AbilityStateValue>
	{
		readonly BlackboardOwnerToken m_BlackboardOwnerToken;
		readonly BlackboardWriteStamp m_BlackboardWriteStamp;

		AbilityStateValue(
			ProgramStateValueKind kind,
			bool boolean,
			int int32,
			ulong uint64,
			Float32Scalar scalar,
			Float32Vector2 vector2,
			Float32Vector3 vector3,
			Float32Yaw yaw,
			CanonicalTextValue identity,
			BlackboardOwnerToken blackboardOwnerToken,
			BlackboardWriteStamp blackboardWriteStamp,
			SimulationActionTargetSnapshot actionTargetSnapshot)
		{
			Kind = kind;
			Boolean = boolean;
			Int32 = int32;
			UInt64 = uint64;
			Scalar = scalar;
			Vector2 = vector2;
			Vector3 = vector3;
			Yaw = yaw;
			Identity = kind == ProgramStateValueKind.Identity ? identity :
				identity.IsNull ? CanonicalTextValue.Empty : identity;
			m_BlackboardOwnerToken = blackboardOwnerToken;
			m_BlackboardWriteStamp = blackboardWriteStamp;
			ActionTargetSnapshot = actionTargetSnapshot;
		}

		public ProgramStateValueKind Kind { get; }
		public bool Boolean { get; }
		public int Int32 { get; }
		public ulong UInt64 { get; }
		public Float32Scalar Scalar { get; }
		public Float32Vector2 Vector2 { get; }
		public Float32Vector3 Vector3 { get; }
		public Float32Yaw Yaw { get; }
		public CanonicalTextValue Identity { get; }
		public Float32Quaternion Quaternion => new(Vector3.X, Vector3.Y, Vector3.Z, Scalar);
		public static AbilityStateValue FromQuaternion(Float32Quaternion value) =>
			Create(ProgramStateValueKind.Quaternion, scalar: value.W, vector3: new Float32Vector3(value.X, value.Y, value.Z));
		public static AbilityStateValue FromEnum(CanonicalTextValue typeIdentity, int value) =>
			Create(ProgramStateValueKind.Enum, int32: value, identity: typeIdentity);
		public BlackboardOwnerToken BlackboardOwnerToken => Require(ProgramStateValueKind.BlackboardOwnerToken, m_BlackboardOwnerToken);
		public BlackboardWriteStamp BlackboardWriteStamp => Require(ProgramStateValueKind.BlackboardWriteStamp, m_BlackboardWriteStamp);
		public SimulationActionTargetSnapshot ActionTargetSnapshot { get; }
		public void Retain() { Identity.Retain(); ActionTargetSnapshot.Retain(); }
		public void Release() { ActionTargetSnapshot.Release(); Identity.Release(); }
		public bool Equals(AbilityStateValue other) =>
			Kind == other.Kind &&
			Boolean == other.Boolean &&
			Int32 == other.Int32 &&
			UInt64 == other.UInt64 &&
			Scalar.Equals(other.Scalar) &&
			Vector2.Equals(other.Vector2) &&
			Vector3.Equals(other.Vector3) &&
			Yaw.Equals(other.Yaw) &&
			Identity.Equals(other.Identity) &&
			m_BlackboardOwnerToken.Equals(other.m_BlackboardOwnerToken) &&
			m_BlackboardWriteStamp.Equals(other.m_BlackboardWriteStamp) &&
			ActionTargetSnapshot.Equals(other.ActionTargetSnapshot);
		public static AbilityStateValue FromBoolean(bool value) => Create(ProgramStateValueKind.Boolean, boolean: value);
		public static AbilityStateValue FromInt32(int value) => Create(ProgramStateValueKind.Int32, int32: value);
		public static AbilityStateValue FromUInt64(ulong value) => Create(ProgramStateValueKind.UInt64, uint64: value);
		public static AbilityStateValue FromScalar(Float32Scalar value) => Create(ProgramStateValueKind.Scalar, scalar: value);
		public static AbilityStateValue FromVector2(Float32Vector2 value) => Create(ProgramStateValueKind.Vector2, vector2: value);
		public static AbilityStateValue FromVector3(Float32Vector3 value) => Create(ProgramStateValueKind.Vector3, vector3: value);
		public static AbilityStateValue FromYaw(Float32Yaw value) => Create(ProgramStateValueKind.Yaw, yaw: value);
		public static AbilityStateValue FromIdentity(CanonicalTextValue value) => Create(ProgramStateValueKind.Identity, identity: value);
		public static AbilityStateValue FromBlackboardOwnerToken(BlackboardOwnerToken value) => Create(ProgramStateValueKind.BlackboardOwnerToken, blackboardOwnerToken: value);
		public static AbilityStateValue FromBlackboardWriteStamp(BlackboardWriteStamp value) => Create(ProgramStateValueKind.BlackboardWriteStamp, blackboardWriteStamp: value);
		public static AbilityStateValue FromActionTargetSnapshot(SimulationActionTargetSnapshot value) => Create(ProgramStateValueKind.ActionTargetSnapshot, actionTargetSnapshot: value);
		public static AbilityStateValue Default(ProgramStateValueKind kind)
		{
			return kind switch
			{
				ProgramStateValueKind.Boolean => FromBoolean(false),
				ProgramStateValueKind.Int32 => FromInt32(0),
				ProgramStateValueKind.UInt64 => FromUInt64(0),
				ProgramStateValueKind.Scalar => FromScalar(Float32Scalar.Zero),
				ProgramStateValueKind.Vector2 => FromVector2(Float32Vector2.Zero),
				ProgramStateValueKind.Vector3 => FromVector3(Float32Vector3.Zero),
				ProgramStateValueKind.Yaw => FromYaw(Float32Yaw.Zero),
				ProgramStateValueKind.Identity => FromIdentity(string.Empty),
				ProgramStateValueKind.Quaternion => FromQuaternion(Float32Quaternion.Identity),
				ProgramStateValueKind.BlackboardOwnerToken => FromBlackboardOwnerToken(default),
				ProgramStateValueKind.BlackboardWriteStamp => FromBlackboardWriteStamp(default),
				ProgramStateValueKind.ActionTargetSnapshot => FromActionTargetSnapshot(SimulationActionTargetSnapshot.None),
				_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
			};
		}

		public static AbilityStateValue FromConstant(ProgramConstant constant, ProgramStateValueKind expectedKind)
		{
			if (constant == null)
				throw new ArgumentNullException(nameof(constant));
			AbilityStateValue value = expectedKind switch
			{
				ProgramStateValueKind.Boolean when constant.Kind == ProgramConstantKind.Boolean => FromBoolean(constant.Boolean),
				ProgramStateValueKind.Int32 when constant.Kind == ProgramConstantKind.Int32 => FromInt32(constant.Int32),
				ProgramStateValueKind.UInt64 when constant.Kind == ProgramConstantKind.UInt64 => FromUInt64(constant.UInt64),
				ProgramStateValueKind.Scalar when constant.Kind == ProgramConstantKind.Scalar => FromScalar(constant.Scalar),
				ProgramStateValueKind.Vector2 when constant.Kind == ProgramConstantKind.Vector2 => FromVector2(constant.Vector2),
				ProgramStateValueKind.Vector3 when constant.Kind == ProgramConstantKind.Vector3 => FromVector3(constant.Vector3),
				ProgramStateValueKind.Yaw when constant.Kind == ProgramConstantKind.Yaw => FromYaw(constant.Yaw),
				ProgramStateValueKind.Identity when constant.Kind == ProgramConstantKind.String => FromIdentity(constant.Text),
				ProgramStateValueKind.Quaternion when constant.Kind == ProgramConstantKind.Quaternion => FromQuaternion(constant.Quaternion),
				ProgramStateValueKind.Enum when constant.Kind == ProgramConstantKind.Enum => FromEnum(constant.Text, constant.Int32),
				ProgramStateValueKind.ActionTargetSnapshot when constant.Kind == ProgramConstantKind.Bytes =>
					FromActionTargetSnapshot(SimulationActionTargetSnapshotCodec.Read(constant.Bytes.ToArray())),
				_ => throw new InvalidDataException(
					$"Constant '{constant.Identity}' kind '{constant.Kind}' does not match state kind '{expectedKind}'.")
			};
			return value;
		}

		static AbilityStateValue Create(
			ProgramStateValueKind kind,
			bool boolean = default,
			int int32 = default,
			ulong uint64 = default,
			Float32Scalar scalar = default,
			Float32Vector2 vector2 = default,
			Float32Vector3 vector3 = default,
			Float32Yaw yaw = default,
			CanonicalTextValue identity = default,
			BlackboardOwnerToken blackboardOwnerToken = default,
			BlackboardWriteStamp blackboardWriteStamp = default,
			SimulationActionTargetSnapshot actionTargetSnapshot = default)
		{
			return new AbilityStateValue(
				kind,
				boolean,
				int32,
				uint64,
				scalar,
				vector2,
				vector3,
				yaw,
				identity,
				blackboardOwnerToken,
				blackboardWriteStamp,
				actionTargetSnapshot);
		}

		T Require<T>(ProgramStateValueKind expected, T value)
		{
			if (Kind != expected)
				throw new InvalidOperationException($"State value is '{Kind}', expected '{expected}'.");
			return value;
		}
	}
}
