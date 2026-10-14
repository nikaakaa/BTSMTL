using System;

namespace ThirdPersonSimulation
{
    public enum SimulationExecutionSourceKind : byte
    {
        SkillOperation = 1,
        CharacterControl = 2,
        BlackboardCommand = 3,
        GameplayEffect = 4,
        AbilityRule = 5
    }

    [CanonicalContract]
    public readonly partial struct SimulationExecutionSource : IEquatable<SimulationExecutionSource>
    {
        SimulationExecutionSource(
            SimulationExecutionSourceKind kind,
            OperationHandle operation,
            CanonicalTextValue executionPath,
            CharacterControlModuleId moduleId,
            CharacterControlStateId stateId,
            CharacterControlTransitionId transitionId,
            CanonicalTextValue blackboardDeclaration = default,
            CharacterSkillId abilityId = default,
            CanonicalTextValue ruleId = default,
            GameplayAbilityRulePhase rulePhase = default)
        {
            Kind = kind;
            Operation = operation;
            ExecutionPath = executionPath.IsNull ? CanonicalTextValue.Empty : executionPath;
            ModuleId = moduleId;
            StateId = stateId;
            TransitionId = transitionId;
            BlackboardDeclaration = blackboardDeclaration.IsNull ? CanonicalTextValue.Empty : blackboardDeclaration;
            AbilityId = abilityId;
            RuleId = ruleId.IsNull ? CanonicalTextValue.Empty : ruleId;
            RulePhase = rulePhase;
            RequireStructure();
        }

        public static SimulationExecutionSource FromSkillOperation(OperationHandle operation, CanonicalTextValue executionPath) =>
            new SimulationExecutionSource(
                SimulationExecutionSourceKind.SkillOperation,
                operation,
                SimulationIdentity.Require(executionPath, nameof(executionPath)),
                default,
                default,
                default);

        public static SimulationExecutionSource FromCharacterControl(
            CharacterControlModuleId moduleId,
            CharacterControlStateId stateId,
            CharacterControlTransitionId transitionId) =>
            new SimulationExecutionSource(
                SimulationExecutionSourceKind.CharacterControl,
                OperationHandle.Invalid,
                string.Empty,
                moduleId,
                stateId,
                transitionId);

        public static SimulationExecutionSource FromBlackboardCommand(CanonicalTextValue declarationIdentity) =>
            new SimulationExecutionSource(SimulationExecutionSourceKind.BlackboardCommand,
                OperationHandle.Invalid, string.Empty, default, default, default, declarationIdentity);

        public static SimulationExecutionSource FromGameplayEffect() =>
            new SimulationExecutionSource(SimulationExecutionSourceKind.GameplayEffect,
                OperationHandle.Invalid, string.Empty, default, default, default);

        public static SimulationExecutionSource FromAbilityRule(CharacterSkillId abilityId,
            CanonicalTextValue ruleId, GameplayAbilityRulePhase phase) =>
            new SimulationExecutionSource(SimulationExecutionSourceKind.AbilityRule,
                OperationHandle.Invalid, string.Empty, default, default, default,
                abilityId: abilityId, ruleId: ruleId, rulePhase: phase);

        public SimulationExecutionSourceKind Kind { get; }
        public OperationHandle Operation { get; }
        public CanonicalTextValue ExecutionPath { get; }
        public CharacterControlModuleId ModuleId { get; }
        public CharacterControlStateId StateId { get; }
        public CharacterControlTransitionId TransitionId { get; }
        public CanonicalTextValue BlackboardDeclaration { get; }
        public CharacterSkillId AbilityId { get; }
        public CanonicalTextValue RuleId { get; }
        public GameplayAbilityRulePhase RulePhase { get; }
        public bool IsBlackboardCommand => Kind == SimulationExecutionSourceKind.BlackboardCommand;
        public bool IsGameplayEffect => Kind == SimulationExecutionSourceKind.GameplayEffect;
        public bool IsSkillOperation => Kind == SimulationExecutionSourceKind.SkillOperation;
        public bool IsCharacterControl => Kind == SimulationExecutionSourceKind.CharacterControl;
        public bool IsAbilityRule => Kind == SimulationExecutionSourceKind.AbilityRule;
        [CanonicalPresence]
        public bool IsValid => IsGameplayEffect || (IsAbilityRule
            ? AbilityId.IsValid && !CanonicalTextValue.IsNullOrEmpty(RuleId) &&
                (RulePhase == GameplayAbilityRulePhase.BeforeAbility || RulePhase == GameplayAbilityRulePhase.AfterAbility)
            : IsSkillOperation
            ? Operation.IsValid && !CanonicalTextValue.IsNullOrEmpty(ExecutionPath)
            : IsBlackboardCommand
                ? !CanonicalTextValue.IsNullOrEmpty(BlackboardDeclaration)
                : IsCharacterControl && ModuleId.IsValid && (StateId.IsValid || TransitionId.IsValid));

        public string Identity => IsAbilityRule
            ? $"ability-rule:{AbilityId}:{RuleId}:{RulePhase}"
            : IsGameplayEffect
            ? "gameplay-effect"
            : IsSkillOperation
            ? $"skill-operation:{ExecutionPath}"
            : IsBlackboardCommand
                ? $"blackboard-command:{BlackboardDeclaration}"
                : $"character-control:{ModuleId}:{StateId}:{TransitionId}";

        public bool Equals(SimulationExecutionSource other) =>
            Kind == other.Kind &&
            Operation.Equals(other.Operation) &&
            CanonicalTextValue.Equals(ExecutionPath, other.ExecutionPath, StringComparison.Ordinal) &&
            ModuleId == other.ModuleId &&
            StateId == other.StateId &&
            TransitionId == other.TransitionId &&
            CanonicalTextValue.Equals(BlackboardDeclaration, other.BlackboardDeclaration, StringComparison.Ordinal) &&
            AbilityId == other.AbilityId &&
            CanonicalTextValue.Equals(RuleId, other.RuleId, StringComparison.Ordinal) &&
            RulePhase == other.RulePhase;

        public override bool Equals(object obj) => obj is SimulationExecutionSource other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(
            HashCode.Combine(Kind, Operation, ExecutionPath, ModuleId, StateId, TransitionId, BlackboardDeclaration),
            AbilityId, RuleId, RulePhase);
        public void Retain() => CanonicalRetain(in this);
        public void Release() => CanonicalRelease(in this);
        public override string ToString() => Identity;
        public static bool operator ==(SimulationExecutionSource left, SimulationExecutionSource right) => left.Equals(right);
        public static bool operator !=(SimulationExecutionSource left, SimulationExecutionSource right) => !left.Equals(right);

        [CanonicalValidation]
        void RequireStructure()
        {
            if (Kind == SimulationExecutionSourceKind.SkillOperation &&
                (!Operation.IsValid || CanonicalTextValue.IsNullOrWhiteSpace(ExecutionPath) || ModuleId.IsValid || StateId.IsValid || TransitionId.IsValid))
            {
                throw new ArgumentException("Skill execution source is incomplete.");
            }
            if (Kind == SimulationExecutionSourceKind.CharacterControl &&
                (!ModuleId.IsValid || !StateId.IsValid && !TransitionId.IsValid || Operation.IsValid || !CanonicalTextValue.IsNullOrEmpty(ExecutionPath)))
            {
                throw new ArgumentException("Character control execution source is incomplete.");
            }
            if (Kind == SimulationExecutionSourceKind.BlackboardCommand &&
                (CanonicalTextValue.IsNullOrWhiteSpace(BlackboardDeclaration) || Operation.IsValid || !CanonicalTextValue.IsNullOrEmpty(ExecutionPath) ||
                 ModuleId.IsValid || StateId.IsValid || TransitionId.IsValid))
                throw new ArgumentException("Blackboard command execution source is incomplete.");
        }
}


}
