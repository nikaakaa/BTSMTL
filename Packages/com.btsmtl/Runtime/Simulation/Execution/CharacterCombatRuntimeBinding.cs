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
            string sourceId, StableHash contentHash, byte[] attackCatalogBytes, int factionId, bool neutral,
            double radius, double halfLength, double centerX, double centerY, double centerZ,
            string invulnerableTag, string guardingTag,
            string landedEvent, string hitLightEvent, string hitHeavyEvent,
            string parryLightEvent, string parryHeavyEvent,
            int attackCapacity, int resultCapacity,
            string parryLightAttackId, string parryHeavyAttackId,
            string hitLightEffectId = "", string hitHeavyEffectId = "",
            string evadingTag = "", string counterOpportunityTag = "", string perfectEvadeEvent = "",
            CharacterTimeSlowRuntimeBinding perfectEvadeTimeSlow = null)
        {
            if (radius <= 0 || halfLength < 0 || attackCapacity <= 0 || resultCapacity <= 0 || !neutral && factionId <= 0)
                throw new ArgumentException("Combat receiver geometry and storage capacities are incomplete.");
            SourceId = SimulationIdentity.Require(sourceId, nameof(sourceId));
            ContentHash = StableHash.Compute(contentHash.ToString(), SimulationCanonicalPayloadHash.Compute(attackCatalogBytes).ToString());
            FactionId = factionId;
            Neutral = neutral;
            Radius = radius;
            HalfLength = halfLength;
            CenterX = centerX;
            CenterY = centerY;
            CenterZ = centerZ;
            InvulnerableTag = string.IsNullOrEmpty(invulnerableTag) ? string.Empty : GameplayEffectRuntimeIdentity.Tag(invulnerableTag);
            GuardingTag = string.IsNullOrEmpty(guardingTag) ? string.Empty : GameplayEffectRuntimeIdentity.Tag(guardingTag);
            LandedEvent = landedEvent;
            HitLightEvent = hitLightEvent;
            HitHeavyEvent = hitHeavyEvent;
            ParryLightEvent = parryLightEvent;
            ParryHeavyEvent = parryHeavyEvent;
            AttackCapacity = attackCapacity;
            ResultCapacity = resultCapacity;
            HitLightEffectId = hitLightEffectId;
            HitHeavyEffectId = hitHeavyEffectId;
            ParryLightAttackId = parryLightAttackId;
            ParryHeavyAttackId = parryHeavyAttackId;
            EvadingTag = string.IsNullOrEmpty(evadingTag) ? string.Empty : GameplayEffectRuntimeIdentity.Tag(evadingTag);
            CounterOpportunityTag = string.IsNullOrEmpty(counterOpportunityTag) ? string.Empty : GameplayEffectRuntimeIdentity.Tag(counterOpportunityTag);
            PerfectEvadeEvent = perfectEvadeEvent;
            PerfectEvadeTimeSlow = perfectEvadeTimeSlow;
            m_AttackCatalogBytes = (byte[])attackCatalogBytes.Clone();
        }

        readonly byte[] m_AttackCatalogBytes;
        public byte[] AttackCatalogBytes => (byte[])m_AttackCatalogBytes.Clone();
        public string SourceId { get; }
        public StableHash ContentHash { get; }
        public int FactionId { get; }
        public bool Neutral { get; }
        public double Radius { get; }
        public double HalfLength { get; }
        public double CenterX { get; }
        public double CenterY { get; }
        public double CenterZ { get; }
        public string InvulnerableTag { get; }
        public string GuardingTag { get; }
        public string LandedEvent { get; }
        public string HitLightEvent { get; }
        public string HitHeavyEvent { get; }
        public string ParryLightEvent { get; }
        public string ParryHeavyEvent { get; }
        public int AttackCapacity { get; }
        public int ResultCapacity { get; }
        public string HitLightEffectId { get; }
        public string HitHeavyEffectId { get; }
        public string ParryLightAttackId { get; }
        public string ParryHeavyAttackId { get; }
        public string EvadingTag { get; }
        public string CounterOpportunityTag { get; }
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

    [CanonicalContract]
    public readonly partial struct CombatAttackIdentity : IEquatable<CombatAttackIdentity>
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

        [CanonicalPresence]
        public bool IsValid => SourceKind != 0;

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

        public void Retain() => CanonicalRetain(in this);

        public void Release() => CanonicalRelease(in this);

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
