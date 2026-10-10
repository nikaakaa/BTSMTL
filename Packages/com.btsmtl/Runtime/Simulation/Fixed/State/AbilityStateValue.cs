using ThirdPersonSimulation;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ThirdPersonSimulation.Fixed
{
	public readonly struct AbilityStateValue : IEquatable<AbilityStateValue>
	{
		[StructLayout(LayoutKind.Explicit)]
		readonly struct Payload
		{
			[FieldOffset(0)] internal readonly bool Boolean;
			[FieldOffset(0)] internal readonly int Int32;
			[FieldOffset(0)] internal readonly ulong UInt64;
			[FieldOffset(0)] internal readonly FixedScalar Scalar;
			[FieldOffset(0)] internal readonly FixedVector2 Vector2;
			[FieldOffset(0)] internal readonly FixedVector3 Vector3;
			[FieldOffset(0)] internal readonly FixedYaw Yaw;
			[FieldOffset(0)] internal readonly BlackboardOwnerToken BlackboardOwnerToken;
			[FieldOffset(0)] internal readonly BlackboardWriteStamp BlackboardWriteStamp;

			internal Payload(bool value) : this() => Boolean = value;
			internal Payload(int value) : this() => Int32 = value;
			internal Payload(ulong value) : this() => UInt64 = value;
			internal Payload(FixedScalar value) : this() => Scalar = value;
			internal Payload(FixedVector2 value) : this() => Vector2 = value;
			internal Payload(FixedVector3 value) : this() => Vector3 = value;
			internal Payload(FixedYaw value) : this() => Yaw = value;
			internal Payload(BlackboardOwnerToken value) : this() => BlackboardOwnerToken = value;
			internal Payload(BlackboardWriteStamp value) : this() => BlackboardWriteStamp = value;
		}

		readonly Payload m_Payload;

		AbilityStateValue(
			ProgramStateValueKind kind,
			Payload payload,
			CanonicalTextValue identity = default,
			SimulationActionTargetSnapshot actionTargetSnapshot = default)
		{
			Kind = kind;
			m_Payload = payload;
			Identity = identity;
			ActionTargetSnapshot = actionTargetSnapshot;
		}

		public ProgramStateValueKind Kind { get; }
		public bool Boolean => Kind == ProgramStateValueKind.Boolean && m_Payload.Boolean;
		public int Int32 => Kind == ProgramStateValueKind.Int32 ? m_Payload.Int32 : default;
		public ulong UInt64 => Kind == ProgramStateValueKind.UInt64 ? m_Payload.UInt64 : default;
		public FixedScalar Scalar => Kind == ProgramStateValueKind.Scalar ? m_Payload.Scalar : default;
		public FixedVector2 Vector2 => Kind == ProgramStateValueKind.Vector2 ? m_Payload.Vector2 : default;
		public FixedVector3 Vector3 => Kind == ProgramStateValueKind.Vector3 ? m_Payload.Vector3 : default;
		public FixedYaw Yaw => Kind == ProgramStateValueKind.Yaw ? m_Payload.Yaw : default;
		public CanonicalTextValue Identity { get; }
		public BlackboardOwnerToken BlackboardOwnerToken => Require(ProgramStateValueKind.BlackboardOwnerToken, m_Payload.BlackboardOwnerToken);
		public BlackboardWriteStamp BlackboardWriteStamp => Require(ProgramStateValueKind.BlackboardWriteStamp, m_Payload.BlackboardWriteStamp);
		public SimulationActionTargetSnapshot ActionTargetSnapshot { get; }
		public bool Equals(AbilityStateValue other) =>
			Kind == other.Kind &&
			Identity.Equals(other.Identity) &&
			ActionTargetSnapshot.Equals(other.ActionTargetSnapshot) &&
			(Kind switch
			{
				ProgramStateValueKind.Boolean => m_Payload.Boolean == other.m_Payload.Boolean,
				ProgramStateValueKind.Int32 => m_Payload.Int32 == other.m_Payload.Int32,
				ProgramStateValueKind.UInt64 => m_Payload.UInt64 == other.m_Payload.UInt64,
				ProgramStateValueKind.Scalar => m_Payload.Scalar.Equals(other.m_Payload.Scalar),
				ProgramStateValueKind.Vector2 => m_Payload.Vector2.Equals(other.m_Payload.Vector2),
				ProgramStateValueKind.Vector3 => m_Payload.Vector3.Equals(other.m_Payload.Vector3),
				ProgramStateValueKind.Yaw => m_Payload.Yaw.Equals(other.m_Payload.Yaw),
				ProgramStateValueKind.BlackboardOwnerToken => m_Payload.BlackboardOwnerToken.Equals(other.m_Payload.BlackboardOwnerToken),
				ProgramStateValueKind.BlackboardWriteStamp => m_Payload.BlackboardWriteStamp.Equals(other.m_Payload.BlackboardWriteStamp),
				_ => m_Payload.BlackboardWriteStamp.Equals(other.m_Payload.BlackboardWriteStamp)
			});
		public void Retain()
		{
			Identity.Retain();
			ActionTargetSnapshot.Retain();
		}

		public void Release()
		{
			Identity.Release();
			ActionTargetSnapshot.Release();
		}

		public static AbilityStateValue FromBoolean(bool value) => new AbilityStateValue(ProgramStateValueKind.Boolean, new Payload(value));
		public static AbilityStateValue FromInt32(int value) => new AbilityStateValue(ProgramStateValueKind.Int32, new Payload(value));
		public static AbilityStateValue FromUInt64(ulong value) => new AbilityStateValue(ProgramStateValueKind.UInt64, new Payload(value));
		public static AbilityStateValue FromScalar(FixedScalar value) => new AbilityStateValue(ProgramStateValueKind.Scalar, new Payload(value));
		public static AbilityStateValue FromVector2(FixedVector2 value) => new AbilityStateValue(ProgramStateValueKind.Vector2, new Payload(value));
		public static AbilityStateValue FromVector3(FixedVector3 value) => new AbilityStateValue(ProgramStateValueKind.Vector3, new Payload(value));
		public static AbilityStateValue FromYaw(FixedYaw value) => new AbilityStateValue(ProgramStateValueKind.Yaw, new Payload(value));
		public static AbilityStateValue FromIdentity(CanonicalTextValue value) => new AbilityStateValue(ProgramStateValueKind.Identity, default, value);
		public static AbilityStateValue FromBlackboardOwnerToken(BlackboardOwnerToken value) => new AbilityStateValue(ProgramStateValueKind.BlackboardOwnerToken, new Payload(value));
		public static AbilityStateValue FromBlackboardWriteStamp(BlackboardWriteStamp value) => new AbilityStateValue(ProgramStateValueKind.BlackboardWriteStamp, new Payload(value));
		public static AbilityStateValue FromActionTargetSnapshot(SimulationActionTargetSnapshot value) => new AbilityStateValue(ProgramStateValueKind.ActionTargetSnapshot, default, actionTargetSnapshot: value);
		public static AbilityStateValue Default(ProgramStateValueKind kind)
		{
			return kind switch
			{
				ProgramStateValueKind.Boolean => FromBoolean(false),
				ProgramStateValueKind.Int32 => FromInt32(0),
				ProgramStateValueKind.UInt64 => FromUInt64(0),
				ProgramStateValueKind.Scalar => FromScalar(FixedScalar.Zero),
				ProgramStateValueKind.Vector2 => FromVector2(FixedVector2.Zero),
				ProgramStateValueKind.Vector3 => FromVector3(FixedVector3.Zero),
				ProgramStateValueKind.Yaw => FromYaw(FixedYaw.Zero),
				ProgramStateValueKind.Identity => FromIdentity(string.Empty),
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
				ProgramStateValueKind.ActionTargetSnapshot when constant.Kind == ProgramConstantKind.Bytes =>
					FromActionTargetSnapshot(SimulationActionTargetSnapshotCodec.Read(constant.Bytes.ToArray())),
				_ => throw new InvalidDataException(
					$"Constant '{constant.Identity}' kind '{constant.Kind}' does not match state kind '{expectedKind}'.")
			};
			return value;
		}

		T Require<T>(ProgramStateValueKind expected, T value)
		{
			if (Kind != expected)
				throw new InvalidOperationException($"State value is '{Kind}', expected '{expected}'.");
			return value;
		}
	}
}
