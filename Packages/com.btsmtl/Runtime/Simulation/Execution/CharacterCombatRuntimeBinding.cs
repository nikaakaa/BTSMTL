using System;
using ThirdPersonSimulation.Fixed;

namespace ThirdPersonSimulation
{
    public sealed class CharacterTimeSlowRuntimeBinding
    {
        public CharacterTimeSlowRuntimeBinding(string sourceKey, int priority, FixedScalar duration,
            CharacterCombatCurve curve, int preWrapMode, int postWrapMode,
            CharacterSkillId counterSkillId, StableHash contentHash)
        {
            SourceKey = SimulationIdentity.Require(sourceKey, nameof(sourceKey));
            if (duration <= FixedScalar.Zero || curve == null || !counterSkillId.IsValid)
                throw new ArgumentException("Perfect evade time slow requires its source curve, duration and counter skill.");
            Priority = priority;
            Duration = duration;
            Curve = curve;
            PreWrapMode = preWrapMode;
            PostWrapMode = postWrapMode;
            CounterSkillId = counterSkillId;
            ContentHash = contentHash;
        }

        public string SourceKey { get; }
        public int Priority { get; }
        public FixedScalar Duration { get; }
        public CharacterCombatCurve Curve { get; }
        public int PreWrapMode { get; }
        public int PostWrapMode { get; }
        public CharacterSkillId CounterSkillId { get; }
        public StableHash ContentHash { get; }
    }

    public sealed class CharacterCombatRuntimeBinding
    {
        public CharacterCombatRuntimeBinding(
            string sourceId, StableHash contentHash, int factionId, bool neutral,
            double radius, double halfLength, double centerX, double centerY, double centerZ,
            string invulnerableWindowTag, string parryWindowTag,
            string landedEvent, string hitLightEvent, string hitHeavyEvent,
            string parryLightEvent, string parryHeavyEvent,
            int attackCapacity, int resultCapacity,
            string parryLightEffectId, string parryHeavyEffectId,
            string hitLightEffectId = "", string hitHeavyEffectId = "",
            string perfectEvadeWindowTag = "", string perfectEvadeReadyWindowTag = "", string perfectEvadeEvent = "",
            CharacterTimeSlowRuntimeBinding perfectEvadeTimeSlow = null)
        {
            if (radius <= 0 || halfLength < 0 || attackCapacity <= 0 || resultCapacity <= 0 || !neutral && factionId <= 0)
                throw new ArgumentException("Combat receiver geometry and storage capacities are incomplete.");
            SourceId = SimulationIdentity.Require(sourceId, nameof(sourceId));
            ContentHash = contentHash;
            FactionId = factionId;
            Neutral = neutral;
            Radius = radius;
            HalfLength = halfLength;
            CenterX = centerX;
            CenterY = centerY;
            CenterZ = centerZ;
            InvulnerableWindowTag = string.IsNullOrEmpty(invulnerableWindowTag) ? string.Empty : GameplayEffectRuntimeIdentity.Tag(invulnerableWindowTag);
            ParryWindowTag = string.IsNullOrEmpty(parryWindowTag) ? string.Empty : GameplayEffectRuntimeIdentity.Tag(parryWindowTag);
            LandedEvent = landedEvent;
            HitLightEvent = hitLightEvent;
            HitHeavyEvent = hitHeavyEvent;
            ParryLightEvent = parryLightEvent;
            ParryHeavyEvent = parryHeavyEvent;
            AttackCapacity = attackCapacity;
            ResultCapacity = resultCapacity;
            HitLightEffectId = hitLightEffectId;
            HitHeavyEffectId = hitHeavyEffectId;
            ParryLightEffectId = parryLightEffectId;
            ParryHeavyEffectId = parryHeavyEffectId;
            PerfectEvadeWindowTag = string.IsNullOrEmpty(perfectEvadeWindowTag) ? string.Empty : GameplayEffectRuntimeIdentity.Tag(perfectEvadeWindowTag);
            PerfectEvadeReadyWindowTag = string.IsNullOrEmpty(perfectEvadeReadyWindowTag) ? string.Empty : GameplayEffectRuntimeIdentity.Tag(perfectEvadeReadyWindowTag);
            PerfectEvadeEvent = perfectEvadeEvent;
            PerfectEvadeTimeSlow = perfectEvadeTimeSlow;
        }

        public string SourceId { get; }
        public StableHash ContentHash { get; }
        public int FactionId { get; }
        public bool Neutral { get; }
        public double Radius { get; }
        public double HalfLength { get; }
        public double CenterX { get; }
        public double CenterY { get; }
        public double CenterZ { get; }
        public string InvulnerableWindowTag { get; }
        public string ParryWindowTag { get; }
        public string LandedEvent { get; }
        public string HitLightEvent { get; }
        public string HitHeavyEvent { get; }
        public string ParryLightEvent { get; }
        public string ParryHeavyEvent { get; }
        public int AttackCapacity { get; }
        public int ResultCapacity { get; }
        public string HitLightEffectId { get; }
        public string HitHeavyEffectId { get; }
        public string ParryLightEffectId { get; }
        public string ParryHeavyEffectId { get; }
        public string PerfectEvadeWindowTag { get; }
        public string PerfectEvadeReadyWindowTag { get; }
        public string PerfectEvadeEvent { get; }
        public CharacterTimeSlowRuntimeBinding PerfectEvadeTimeSlow { get; }
    }

    public enum CombatHitDisposition : byte
    {
        Hit = 1,
        Parried = 2,
        Evaded = 3
    }

    public enum CombatAttackSourceKind : byte
    {
        CharacterAction = 1,
        LocalEnemyAttack = 2
    }

    public readonly struct CombatAttackIdentity : IEquatable<CombatAttackIdentity>
    {
        public CombatAttackIdentity(ulong actionInstanceId, ulong segmentGeneration,
            CharacterSkillId skillId, OperationHandle operation, CanonicalTextValue clipId,
            ulong playbackGeneration, ulong branchRevision, int cycle, ulong timelineRuntimeHandle)
        {
            SourceKind = CombatAttackSourceKind.CharacterAction;
            LocalActor = default;
            LocalReleaseSequence = 0;
            LocalWindowId = string.Empty;
            ActionInstanceId = actionInstanceId;
            SegmentGeneration = segmentGeneration;
            SkillId = skillId;
            Operation = operation;
            ClipId = clipId;
            PlaybackGeneration = playbackGeneration;
            BranchRevision = branchRevision;
            Cycle = cycle;
            TimelineRuntimeHandle = timelineRuntimeHandle;
        }

        public CombatAttackIdentity(ActorId localActor, ulong releaseSequence, CanonicalTextValue windowId)
        {
            this = default;
            SourceKind = CombatAttackSourceKind.LocalEnemyAttack;
            LocalActor = localActor;
            LocalReleaseSequence = releaseSequence;
            LocalWindowId = windowId;
        }

        public CombatAttackSourceKind SourceKind { get; }
        public ActorId LocalActor { get; }
        public ulong LocalReleaseSequence { get; }
        public CanonicalTextValue LocalWindowId { get; }

        public ulong ActionInstanceId { get; }
        public ulong SegmentGeneration { get; }
        public CharacterSkillId SkillId { get; }
        public OperationHandle Operation { get; }
        public CanonicalTextValue ClipId { get; }
        public ulong PlaybackGeneration { get; }
        public ulong BranchRevision { get; }
        public int Cycle { get; }
        public ulong TimelineRuntimeHandle { get; }

        public void Retain()
        {
            LocalActor.Value.Retain();
            LocalWindowId.Retain();
            ClipId.Retain();
        }

        public void Release()
        {
            LocalActor.Value.Release();
            LocalWindowId.Release();
            ClipId.Release();
        }

        public bool Equals(CombatAttackIdentity other) => SourceKind == other.SourceKind &&
            (SourceKind == CombatAttackSourceKind.LocalEnemyAttack
            ? LocalActor == other.LocalActor && LocalReleaseSequence == other.LocalReleaseSequence &&
              LocalWindowId.Equals(other.LocalWindowId)
            :
            ActionInstanceId == other.ActionInstanceId && SegmentGeneration == other.SegmentGeneration &&
            SkillId == other.SkillId && Operation.Equals(other.Operation) &&
            ClipId.Equals(other.ClipId) &&
            PlaybackGeneration == other.PlaybackGeneration && BranchRevision == other.BranchRevision &&
            Cycle == other.Cycle && TimelineRuntimeHandle == other.TimelineRuntimeHandle);

        public override bool Equals(object value) => value is CombatAttackIdentity identity && Equals(identity);
        public override int GetHashCode() => SourceKind == CombatAttackSourceKind.LocalEnemyAttack
            ? HashCode.Combine(SourceKind, LocalActor, LocalReleaseSequence, LocalWindowId)
            : HashCode.Combine(HashCode.Combine(ActionInstanceId, SegmentGeneration,
                SkillId, Operation, ClipId, PlaybackGeneration, BranchRevision, Cycle), TimelineRuntimeHandle);
    }
}
