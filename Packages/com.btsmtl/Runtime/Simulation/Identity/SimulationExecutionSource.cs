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

    public readonly struct SimulationExecutionSource : IEquatable<SimulationExecutionSource>
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
            if (kind != SimulationExecutionSourceKind.SkillOperation &&
                kind != SimulationExecutionSourceKind.CharacterControl &&
                kind != SimulationExecutionSourceKind.BlackboardCommand &&
                kind != SimulationExecutionSourceKind.GameplayEffect &&
                kind != SimulationExecutionSourceKind.AbilityRule)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (kind == SimulationExecutionSourceKind.SkillOperation &&
                (!operation.IsValid || CanonicalTextValue.IsNullOrWhiteSpace(executionPath) || moduleId.IsValid || stateId.IsValid || transitionId.IsValid))
            {
                throw new ArgumentException("Skill execution source is incomplete.");
            }
            if (kind == SimulationExecutionSourceKind.CharacterControl &&
                (!moduleId.IsValid || !stateId.IsValid && !transitionId.IsValid || operation.IsValid || !CanonicalTextValue.IsNullOrEmpty(executionPath)))
            {
                throw new ArgumentException("Character control execution source is incomplete.");
            }
            if (kind == SimulationExecutionSourceKind.BlackboardCommand &&
                (CanonicalTextValue.IsNullOrWhiteSpace(blackboardDeclaration) || operation.IsValid || !CanonicalTextValue.IsNullOrEmpty(executionPath) ||
                 moduleId.IsValid || stateId.IsValid || transitionId.IsValid))
                throw new ArgumentException("Blackboard command execution source is incomplete.");
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
        public void Retain() { ExecutionPath.Retain(); BlackboardDeclaration.Retain(); RuleId.Retain(); }
        public void Release() { ExecutionPath.Release(); BlackboardDeclaration.Release(); RuleId.Release(); }
        public override string ToString() => Identity;
        public static bool operator ==(SimulationExecutionSource left, SimulationExecutionSource right) => left.Equals(right);
        public static bool operator !=(SimulationExecutionSource left, SimulationExecutionSource right) => !left.Equals(right);
    }

    public static class SimulationExecutionSourceCodec
    {
        public static void Write(CanonicalWriter writer, SimulationExecutionSource source)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (!source.IsValid)
                throw new ArgumentException("Execution source is invalid.", nameof(source));
            writer.WriteByte((byte)source.Kind);
            if (source.IsAbilityRule)
            {
                writer.WriteText(source.AbilityId.Value);
                writer.WriteText(source.RuleId);
                writer.WriteByte((byte)source.RulePhase);
                return;
            }
            if (source.IsGameplayEffect)
                return;
            if (source.IsSkillOperation)
            {
                writer.WriteInt32(source.Operation.Value);
                writer.WriteText(source.ExecutionPath);
                return;
            }
            if (source.IsBlackboardCommand)
            {
                writer.WriteText(source.BlackboardDeclaration);
                return;
            }
            writer.WriteText(source.ModuleId.Value);
            writer.WriteText(source.StateId.Value);
            writer.WriteText(source.TransitionId.Value);
        }

        public static SimulationExecutionSource Read(CanonicalReader reader, Func<CanonicalReader, string> readDeclaredIdentity)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            SimulationExecutionSourceKind kind = (SimulationExecutionSourceKind)reader.ReadByte();
            return kind switch
            {
                SimulationExecutionSourceKind.AbilityRule =>
                    SimulationExecutionSource.FromAbilityRule(new CharacterSkillId(readDeclaredIdentity(reader)),
                        reader.ReadText(), (GameplayAbilityRulePhase)reader.ReadByte()),
                SimulationExecutionSourceKind.GameplayEffect => SimulationExecutionSource.FromGameplayEffect(),
                SimulationExecutionSourceKind.SkillOperation =>
                    SimulationExecutionSource.FromSkillOperation(
                        new OperationHandle(reader.ReadInt32()),
                        reader.ReadText()),
                SimulationExecutionSourceKind.BlackboardCommand =>
                    SimulationExecutionSource.FromBlackboardCommand(reader.ReadText()),
                SimulationExecutionSourceKind.CharacterControl =>
                    SimulationExecutionSource.FromCharacterControl(
                        new CharacterControlModuleId(readDeclaredIdentity(reader)),
                        ReadOptionalState(reader, readDeclaredIdentity),
                        ReadOptionalTransition(reader, readDeclaredIdentity)),
                _ => throw new InvalidOperationException($"Execution source kind '{kind}' is invalid.")
            };
        }

        static CharacterControlStateId ReadOptionalState(CanonicalReader reader, Func<CanonicalReader, string> readDeclaredIdentity)
        {
            string value = readDeclaredIdentity(reader);
            return string.IsNullOrEmpty(value) ? default : new CharacterControlStateId(value);
        }

        static CharacterControlTransitionId ReadOptionalTransition(CanonicalReader reader, Func<CanonicalReader, string> readDeclaredIdentity)
        {
            string value = readDeclaredIdentity(reader);
            return string.IsNullOrEmpty(value) ? default : new CharacterControlTransitionId(value);
        }
    }
}
