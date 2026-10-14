using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using SimulationMotionContribution = ThirdPersonSimulation.SimulationMotionContribution<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, ThirdPersonSimulation.QuantumNumerics.QuantumSimulationMath>;
using ThirdPersonSimulation;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    internal interface IFPValueInputReader
    {
        FPValueInputLease ReadInputs<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation)
            where TTarget : struct, IOperationControlTarget<TTarget>;
    }

    internal interface IFPMotionContributionSink
    {
        void Submit(SimulationMotionContribution contribution);
    }

    internal interface IFPActionContextReader
    {
        bool IsContextActive(CanonicalTextValue contextId);
        bool IsActivationEntry(string entryId);
        bool IsCurrentExecutionContextActive();
        bool TryGetActiveAbilityInstanceId(CharacterSkillId abilityId, out ulong instanceId);
        int FindActive(CanonicalTextValue contextId, out FPActionInstanceState state);
        int FindActive(CharacterSkillId skillId, out FPActionInstanceState state);
        FPActionInstanceState FindOnlyActive();
        bool ContainsInstance(ulong instanceId);
        bool TryGetInstance(ulong instanceId, out FPActionInstanceState state);
        FPActionStateStore.SkillExecutionScope PushSkillExecution(FPActionInstanceState action);
        bool TryGetCurrentSkillExecution(out FPActionInstanceState action);
    }

    internal interface IFPActionAdmissionQuery
    {

        ActionAdmissionDecision PreviewActivation<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation,
            in GameplayAbilitySourceContext source)
            where TTarget : struct, IOperationControlTarget<TTarget>;
    }

    internal interface IFPBlackboardPort
    {
        void WriteGraphCallParameter(int valueSlot, AbilityStateValue value);
        void ResetGraphCallParameter(int valueSlot);
        AbilityStateValue ReadGraphCallParameter(int valueSlot);

        AbilityStateValue Read<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation,
            int valueSlot)
            where TTarget : struct, IOperationControlTarget<TTarget>;

        void Write<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation,
            int valueSlot,
            AbilityStateValue value)
            where TTarget : struct, IOperationControlTarget<TTarget>;

        void WriteInputProjection(int valueSlot, AbilityStateValue value);
    }

    internal interface IFPGameplayTagQuery
    {
        IReadOnlyList<string> OwnedTags { get; }
        bool HasTag(string tag);
        bool Matches(PortableTagQuery query);
        bool HasTag(string tag, GameplayEffectQueryTarget target);
        bool Matches(PortableTagQuery query, GameplayEffectQueryTarget target);
        AbilityStateValue ReadEffect(SimulationOperation operation, string outputPort);
        bool HasTag(string tag, GameplayEffectScope scope);
        bool Matches(PortableTagQuery query, GameplayEffectScope scope);
        AbilityStateValue ReadAttribute(SimulationOperation operation, string outputPort);
    }

    internal interface IFPGameplayEffectActionPort
    {
        GameplayCommandBatchAdmission ReceiveCommandBatch(int commands, SimulationExecutionSource source);
        GameplayEffectApplyResult SetActionTags(ulong actionInstanceId, ActionAdmissionProfile profile);
        void RemoveActionTags(ulong actionInstanceId);
        GameplayEffectRemoveResult ReleaseActionEffects(ulong actionInstanceId);
        void ClearConfirmedAction(ulong actionInstanceId);
        GameplayEffectApplyResult PreviewStartupEffect(string effectId, IReadOnlyList<SimulationSetByCallerValue> values);
        GameplayEffectApplyResult ApplyStartupEffect(string effectId, IReadOnlyList<SimulationSetByCallerValue> values, SimulationGameplayEffectContext context, SimulationExecutionSource source);
        GameplayEffectReconciliationResult ReconcileAction(ulong actionInstanceId, ulong predictionKey, AbilityLifecycleTransition transition, IReadOnlyList<SimulationGameplayEffectLifecycleIngress> commands, SimulationExecutionSource source, bool requiresCommandReception);
        GameplayEffectApplyResult CommitActionMutation(SimulationExecutionSource source);
        void ReportStartupFailure(GameplayEffectApplyResult failure, SimulationExecutionSource source, bool requiresCommandAdmission);
        void CancelActionMutation();
        bool TryFindAbilityBlock(IReadOnlyList<string> candidateTags, GameplayEffectScope scope, out ulong effectHandle);
        bool TryFindOpportunity(GameplayEffectScope scope, PortableTagQuery query,
            out GameplayEffectOpportunityReference opportunity, out SimulationActionTargetSnapshot target);
        bool ConsumeOpportunity(GameplayEffectOpportunityReference opportunity, SimulationExecutionSource source);
        bool TryFindReplacementSourceRejection(IReadOnlyList<string> candidateTags,
            IReadOnlyList<string> selectedSourceTags, bool hasSource, out ulong effectHandle);
    }
}

