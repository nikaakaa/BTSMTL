using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    internal interface IFixedValueInputReader
    {
        FixedValueInputLease ReadInputs<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation)
            where TTarget : struct, IOperationControlTarget<TTarget>;
    }

    internal interface IFixedMotionContributionSink
    {
        void Submit(SimulationMotionContribution contribution);
    }

    internal interface IFixedActionContextReader
    {
        bool IsContextActive(CanonicalTextValue contextId);
        bool IsActivationEntry(string entryId);
        bool IsCurrentExecutionContextActive();
        bool TryGetActiveAbilityInstanceId(CharacterSkillId abilityId, out ulong instanceId);
        int FindActive(CanonicalTextValue contextId, out FixedActionInstanceState state);
        int FindActive(CharacterSkillId skillId, out FixedActionInstanceState state);
        FixedActionInstanceState FindOnlyActive();
        FixedActionInstanceState RequireActive(FixedActionInstanceState expected);
        FixedActionInstanceState RequireActive(FixedActionInstanceReference reference);
        bool ContainsInstance(ulong instanceId);
        FixedActionStateStore.SkillExecutionScope PushSkillExecution(FixedActionInstanceState action);
        bool TryGetCurrentSkillExecution(out FixedActionInstanceState action);
    }

    internal interface IFixedActionAdmissionQuery
    {
        bool IsWindowActive(SimulationOperation operation);

        ActionAdmissionDecision PreviewActivation<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation,
            in GameplayAbilitySourceContext source)
            where TTarget : struct, IOperationControlTarget<TTarget>;
    }

    internal interface IFixedBlackboardPort
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

        void ClearActionInstanceScopes(ulong actionInstanceId);

        void ProjectBlackboardInput(BlackboardInputStateBinding binding, SimulationInputValue value);

        bool IsActionWindowActive(SimulationOperation operation, in GameplayAbilitySourceContext source);

        IDisposable PushTimelineContext(
            SimulationOperation timeline,
            SimulationOperation clip,
            int cycle,
            FixedActionInstanceState action);
    }

    internal interface IFixedGameplayTagQuery
    {
        IReadOnlyList<string> OwnedTags { get; }
        bool HasTag(string tag);
        bool Matches(PortableTagQuery query);
        AbilityStateValue ReadAttribute(SimulationOperation operation, string outputPort);
    }

    internal interface IFixedGameplayEffectActionPort
    {
        GameplayCommandBatchAdmission ReceiveCommandBatch(int commands, SimulationExecutionSource source);
        GameplayEffectApplyResult SetActionTags(ulong actionInstanceId, ActionAdmissionProfile profile);
        void RemoveActionTags(ulong actionInstanceId);
        void ClearConfirmedAction(ulong actionInstanceId);
        GameplayEffectApplyResult PreviewStartupEffect(string effectId, IReadOnlyList<SimulationSetByCallerValue> values);
        GameplayEffectApplyResult ApplyStartupEffect(string effectId, IReadOnlyList<SimulationSetByCallerValue> values, SimulationGameplayEffectContext context, SimulationExecutionSource source);
        GameplayEffectReconciliationResult ReconcileAction(ulong actionInstanceId, ulong predictionKey, AbilityLifecycleTransition transition, IReadOnlyList<SimulationGameplayEffectLifecycleIngress> commands, SimulationExecutionSource source, bool requiresCommandReception);
        GameplayEffectApplyResult CommitActionMutation(SimulationExecutionSource source);
        void ReportStartupFailure(GameplayEffectApplyResult failure, SimulationExecutionSource source, bool requiresCommandAdmission);
        void CancelActionMutation();
    }
}

