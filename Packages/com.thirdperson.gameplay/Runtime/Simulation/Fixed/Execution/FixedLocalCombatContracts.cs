using System;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class FixedLocalEnemyDefinition
    {
        public const int WindowCapacity = 8;
        public readonly ActorId ActorId;
        public readonly ActorId TargetActorId;
        public readonly string ContentId;
        public readonly int FactionId;
        public readonly FixedScalar Radius;
        public readonly FixedScalar HalfLength;
        public readonly FixedVector3 ReceiverCenter;
        public readonly FixedVector3 AttackCenter;
        public readonly FixedVector3 AttackSize;
        public readonly bool Heavy;
        public readonly int HitType;
        public readonly int HitStrengthType;
        public readonly int FrameHalt;
        public readonly int AttackerFrameHalt;
        public readonly uint HitEffectId;
        public readonly StableHash ContentHash;

        public FixedLocalEnemyDefinition(ActorId actorId, ActorId targetActorId, string contentId, int factionId,
            FixedScalar radius, FixedScalar halfLength, FixedVector3 receiverCenter,
            FixedVector3 attackCenter, FixedVector3 attackSize, bool heavy,
            int hitType, int hitStrengthType, int frameHalt, int attackerFrameHalt, uint hitEffectId)
        {
            ActorId = actorId;
            TargetActorId = targetActorId;
            ContentId = contentId;
            FactionId = factionId;
            Radius = radius;
            HalfLength = halfLength;
            ReceiverCenter = receiverCenter;
            AttackCenter = attackCenter;
            AttackSize = attackSize;
            Heavy = heavy;
            HitType = hitType;
            HitStrengthType = hitStrengthType;
            FrameHalt = frameHalt;
            AttackerFrameHalt = attackerFrameHalt;
            HitEffectId = hitEffectId;
            ContentHash = StableHash.Compute("local-enemy-combat/2", actorId.ToString(), targetActorId.ToString(), contentId,
                factionId.ToString(), radius.Raw.ToString(), halfLength.Raw.ToString(),
                receiverCenter.X.Raw.ToString(), receiverCenter.Y.Raw.ToString(), receiverCenter.Z.Raw.ToString(),
                attackCenter.X.Raw.ToString(), attackCenter.Y.Raw.ToString(), attackCenter.Z.Raw.ToString(),
                attackSize.X.Raw.ToString(), attackSize.Y.Raw.ToString(), attackSize.Z.Raw.ToString(),
                heavy.ToString(), hitType.ToString(), hitStrengthType.ToString(), frameHalt.ToString(),
                attackerFrameHalt.ToString(), hitEffectId.ToString());
        }
    }

    public readonly struct FixedLocalEnemyAttackWindow
    {
        public FixedLocalEnemyAttackWindow(ulong releaseSequence, FixedVector3 beforePosition, FixedYaw beforeYaw,
            FixedVector3 position, FixedYaw yaw, FixedScalar startFraction, FixedScalar endFraction)
        {
            ReleaseSequence = releaseSequence;
            BeforePosition = beforePosition;
            BeforeYaw = beforeYaw;
            Position = position;
            Yaw = yaw;
            StartFraction = startFraction;
            EndFraction = endFraction;
        }

        public ulong ReleaseSequence { get; }
        public FixedVector3 BeforePosition { get; }
        public FixedYaw BeforeYaw { get; }
        public FixedVector3 Position { get; }
        public FixedYaw Yaw { get; }
        public FixedScalar StartFraction { get; }
        public FixedScalar EndFraction { get; }
    }

    public readonly struct FixedLocalEnemyInput
    {
        public FixedLocalEnemyInput(ulong tick, FixedVector3 beforePosition, FixedYaw beforeYaw,
            FixedVector3 position, FixedYaw yaw, ulong activeReleaseSequence,
            FixedLocalEnemyAttackWindow[] windows, int windowCount)
        {
            Tick = tick;
            BeforePosition = beforePosition;
            BeforeYaw = beforeYaw;
            Position = position;
            Yaw = yaw;
            ActiveReleaseSequence = activeReleaseSequence;
            Windows = windows;
            WindowCount = windowCount;
        }

        public ulong Tick { get; }
        public FixedVector3 BeforePosition { get; }
        public FixedYaw BeforeYaw { get; }
        public FixedVector3 Position { get; }
        public FixedYaw Yaw { get; }
        public ulong ActiveReleaseSequence { get; }
        public FixedLocalEnemyAttackWindow[] Windows { get; }
        public int WindowCount { get; }
    }

    public interface IFixedLocalEnemyInputSource
    {
        CommittedActorPose<FixedVector3, FixedYaw> CommittedPose { get; }
        FixedLocalEnemyInput FreezeInput(ulong tick);
    }

    public sealed class FixedLocalEnemyBinding
    {
        public FixedLocalEnemyBinding(FixedLocalEnemyDefinition definition, IFixedLocalEnemyInputSource source, StableHash sourceIdentity)
        {
            Definition = definition;
            Source = source;
            SourceIdentity = sourceIdentity;
        }

        public FixedLocalEnemyDefinition Definition { get; }
        public IFixedLocalEnemyInputSource Source { get; }
        public StableHash SourceIdentity { get; }
    }
}
