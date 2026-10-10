using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal interface IFloat32ValueInputReader
    {
        Float32ValueInputLease ReadInputs<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation)
            where TTarget : struct, IOperationControlTarget<TTarget>;
    }

    internal interface IFloat32MotionContributionSink
    {
        void Submit(SimulationMotionContribution contribution);
    }

    internal interface IFloat32ActionContextReader
    {
        bool IsContextActive(CanonicalTextValue contextId);
        bool IsActivationEntry(string entryId);
        bool IsCurrentExecutionContextActive();
        bool TryGetActiveAbilityInstanceId(CharacterSkillId abilityId, out ulong instanceId);
        int FindActive(CanonicalTextValue contextId, out Float32ActionInstanceState state);
        int FindActive(CharacterSkillId skillId, out Float32ActionInstanceState state);
        Float32ActionInstanceState FindOnlyActive();
        Float32ActionInstanceState RequireActive(Float32ActionInstanceState expected);
        Float32ActionInstanceState RequireActive(Float32ActionInstanceReference reference);
        bool ContainsInstance(ulong instanceId);
        Float32ActionStateStore.SkillExecutionScope PushSkillExecution(Float32ActionInstanceState action);
        bool TryGetCurrentSkillExecution(out Float32ActionInstanceState action);
    }

    internal interface IFloat32ActionAdmissionQuery
    {
        bool IsWindowActive(SimulationOperation operation);

        ActionAdmissionDecision PreviewActivation<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation,
            in GameplayAbilitySourceContext source)
            where TTarget : struct, IOperationControlTarget<TTarget>;
    }

    internal interface IFloat32BlackboardPort
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
            Float32ActionInstanceState action);
    }

    internal interface IFloat32GameplayTagQuery
    {
        IReadOnlyList<string> OwnedTags { get; }
        bool HasTag(string tag);
        bool Matches(PortableTagQuery query);
        AbilityStateValue ReadAttribute(SimulationOperation operation, string outputPort);
    }

    internal interface IFloat32GameplayEffectActionPort
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
