using System;

namespace ThirdPersonSimulation
{
    public enum GameplayAbilityRulePhase : byte
    {
        BeforeAbility = 0,
        AfterAbility = 1
    }

    public enum GameplayAbilityRuleContinuation : byte
    {
        Continue = 0,
        EndPhase = 1
    }

    public enum GameplayAbilityRuleTargetPolicy : byte
    {
        Grant = 0,
        SelectedSource = 1
    }

    public readonly partial struct GameplayAbilitySourceContext : IEquatable<GameplayAbilitySourceContext>
    {
        public GameplayAbilitySourceContext(CharacterSkillId skillId, ulong instanceId,
            ulong skillExecutionGeneration, ulong segmentGeneration)
        {
            SkillId = skillId;
            InstanceId = instanceId;
            SkillExecutionGeneration = skillExecutionGeneration;
            SegmentGeneration = segmentGeneration;
        }

        public CharacterSkillId SkillId { get; }
        public ulong InstanceId { get; }
        public ulong SkillExecutionGeneration { get; }
        public ulong SegmentGeneration { get; }
        public bool IsValid => SkillId.IsValid && InstanceId != 0;
        public bool Equals(GameplayAbilitySourceContext other) => SkillId == other.SkillId &&
            InstanceId == other.InstanceId && SkillExecutionGeneration == other.SkillExecutionGeneration &&
            SegmentGeneration == other.SegmentGeneration;
        public override bool Equals(object value) => value is GameplayAbilitySourceContext other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(SkillId, InstanceId, SkillExecutionGeneration, SegmentGeneration);
    }

    public enum GameplayAbilityAvailabilityReason : byte
    {
        NoInput = 0,
        Allowed = 1,
        NoCandidate = 2,
        SourceMissing = 3,
        NotApplicable = 4,
        ConditionRejected = 5,
        AdmissionRejected = 6
    }

}
