using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;

namespace ThirdPersonSimulation.FPGameplay
{
    public sealed class FPLocalEnemyDefinition
    {
        public const int WindowCapacity = 8;
        public readonly ActorId ActorId;
        public readonly ActorId TargetActorId;
        public readonly string ContentId;
        public readonly int FactionId;
        public readonly FP Radius;
        public readonly FP HalfLength;
        public readonly FPVector3 ReceiverCenter;
        public readonly FPVector3 AttackCenter;
        public readonly FPVector3 AttackSize;
        public readonly bool Heavy;
        public readonly int HitType;
        public readonly int HitStrengthType;
        public readonly int FrameHalt;
        public readonly int AttackerFrameHalt;
        public readonly uint HitEffectId;
        public readonly StableHash ContentHash;

        public FPLocalEnemyDefinition(ActorId actorId, ActorId targetActorId, string contentId, int factionId,
            FP radius, FP halfLength, FPVector3 receiverCenter,
            FPVector3 attackCenter, FPVector3 attackSize, bool heavy,
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
                factionId.ToString(), radius.RawValue.ToString(), halfLength.RawValue.ToString(),
                receiverCenter.X.RawValue.ToString(), receiverCenter.Y.RawValue.ToString(), receiverCenter.Z.RawValue.ToString(),
                attackCenter.X.RawValue.ToString(), attackCenter.Y.RawValue.ToString(), attackCenter.Z.RawValue.ToString(),
                attackSize.X.RawValue.ToString(), attackSize.Y.RawValue.ToString(), attackSize.Z.RawValue.ToString(),
                heavy.ToString(), hitType.ToString(), hitStrengthType.ToString(), frameHalt.ToString(),
                attackerFrameHalt.ToString(), hitEffectId.ToString());
        }
    }

    public readonly struct FPLocalEnemyAttackWindow
    {
        public FPLocalEnemyAttackWindow(ulong releaseSequence, FPVector3 beforePosition, FP beforeYaw,
            FPVector3 position, FP yaw, FP startFraction, FP endFraction)
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
        public FPVector3 BeforePosition { get; }
        public FP BeforeYaw { get; }
        public FPVector3 Position { get; }
        public FP Yaw { get; }
        public FP StartFraction { get; }
        public FP EndFraction { get; }
    }

    public readonly struct FPLocalEnemyInput
    {
        public FPLocalEnemyInput(ulong tick, FPVector3 beforePosition, FP beforeYaw,
            FPVector3 position, FP yaw, ulong activeReleaseSequence,
            FPLocalEnemyAttackWindow[] windows, int windowCount)
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
        public FPVector3 BeforePosition { get; }
        public FP BeforeYaw { get; }
        public FPVector3 Position { get; }
        public FP Yaw { get; }
        public ulong ActiveReleaseSequence { get; }
        public FPLocalEnemyAttackWindow[] Windows { get; }
        public int WindowCount { get; }
    }

    public interface IFPLocalEnemyInputSource
    {
        CommittedActorPose<FPVector3, FP> CommittedPose { get; }
        FPLocalEnemyInput FreezeInput(ulong tick);
    }

    public sealed class FPLocalEnemyBinding
    {
        public FPLocalEnemyBinding(FPLocalEnemyDefinition definition, IFPLocalEnemyInputSource source, StableHash sourceIdentity)
        {
            Definition = definition;
            Source = source;
            SourceIdentity = sourceIdentity;
        }

        public FPLocalEnemyDefinition Definition { get; }
        public IFPLocalEnemyInputSource Source { get; }
        public StableHash SourceIdentity { get; }
    }
}
