using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public readonly struct SimulationActionEventIngress
    {
        public SimulationActionEventIngress(ulong actionInstanceId, string eventId)
        {
            ActionInstanceId = actionInstanceId;
            EventId = SimulationIdentity.Require(eventId, nameof(eventId));
        }

        public ulong ActionInstanceId { get; }
        public string EventId { get; }
        public bool IsValid => !string.IsNullOrEmpty(EventId);
    }

    public enum SimulationActionResultKind : byte
    {
        None = 0,
        Accepted = 1,
        Rejected = 2,
        Completed = 3,
        Cancelled = 4,
        Interrupted = 5,
        Aborted = 6,
        Corrected = 7
    }

    internal enum ActionSkillTraceSeverity : byte
    {
        Detail = 1,
        Information = 2
    }

    internal readonly struct SimulationActionActivationRequestState<TTargetSnapshot>
        : IEquatable<SimulationActionActivationRequestState<TTargetSnapshot>>
        where TTargetSnapshot : struct, IEquatable<TTargetSnapshot>
    {
        public SimulationActionActivationRequestState(
            string actionId,
            CharacterSkillId skillId,
            OperationHandle skillEntryOperation,
            CanonicalTextValue contextId,
            SimulationInputRequestState sourceInputRequest,
            ulong inputSequence,
            ulong startTick,
            CanonicalTextValue targetKey,
            TTargetSnapshot targetSnapshot,
            SimulationExecutionSource source,
            EquipmentActionContext equipmentContext = default,
            GameplayAbilitySourceContext replacementSource = default,
            string activationEntryId = "",
            bool consumeSourceInputRequest = true,
            GameplayEffectOpportunityReference opportunity = default)
        {
            ActionId = SimulationIdentity.Require(actionId, nameof(actionId));
            SkillId = skillId;
            SkillEntryOperation = skillEntryOperation;
            ContextId = SimulationIdentity.Require(contextId, nameof(contextId));
            SourceInputRequest = sourceInputRequest;
            ConsumeSourceInputRequest = consumeSourceInputRequest;
            if (inputSequence == 0 || startTick == 0 || !source.IsValid)
                throw new ArgumentException("Action activation request identity is incomplete.");
            InputSequence = inputSequence;
            StartTick = startTick;
            TargetKey = targetKey.IsNull ? CanonicalTextValue.Empty : targetKey;
            TargetSnapshot = targetSnapshot;
            Source = source;
            EquipmentContext = equipmentContext;
            ReplacementSource = replacementSource;
            ActivationEntryId = activationEntryId ?? string.Empty;
            Opportunity = opportunity;
        }

        public string ActionId { get; }
        public CharacterSkillId SkillId { get; }
        public OperationHandle SkillEntryOperation { get; }
        public CanonicalTextValue ContextId { get; }
        public SimulationInputRequestState SourceInputRequest { get; }
        public CanonicalTextValue SourceInputRequestId => SourceInputRequest.RequestId.IsNull
            ? CanonicalTextValue.Empty : SourceInputRequest.RequestId;
        public bool ConsumeSourceInputRequest { get; }
        public ulong InputSequence { get; }
        public ulong StartTick { get; }
        public CanonicalTextValue TargetKey { get; }
        public TTargetSnapshot TargetSnapshot { get; }
        public SimulationExecutionSource Source { get; }
        public EquipmentActionContext EquipmentContext { get; }
        public GameplayAbilitySourceContext ReplacementSource { get; }
        public string ActivationEntryId { get; }
        public GameplayEffectOpportunityReference Opportunity { get; }
        public bool Equals(SimulationActionActivationRequestState<TTargetSnapshot> other) =>
            string.Equals(ActionId, other.ActionId, StringComparison.Ordinal) &&
            SkillId.Equals(other.SkillId) &&
            SkillEntryOperation.Equals(other.SkillEntryOperation) &&
            ContextId.Equals(other.ContextId) &&
            SourceInputRequest.Equals(other.SourceInputRequest) &&
            ConsumeSourceInputRequest == other.ConsumeSourceInputRequest &&
            InputSequence == other.InputSequence &&
            StartTick == other.StartTick &&
            TargetKey.Equals(other.TargetKey) &&
            TargetSnapshot.Equals(other.TargetSnapshot) &&
            Source.Equals(other.Source) &&
            EquipmentContext.Equals(other.EquipmentContext) &&
            ReplacementSource.Equals(other.ReplacementSource) &&
            string.Equals(ActivationEntryId, other.ActivationEntryId, StringComparison.Ordinal) && Opportunity.Equals(other.Opportunity);
        public bool IsValid =>
            !string.IsNullOrEmpty(ActionId) &&
            !ContextId.IsEmpty &&
            InputSequence != 0 &&
            StartTick != 0 &&
            Source.IsValid &&
            (!SourceInputRequest.IsValid || !SourceInputRequest.Consumed &&
                SourceInputRequest.Sequence == InputSequence && SourceInputRequest.ExpireTick >= StartTick) &&
            (ReplacementSource.InstanceId == 0 || ReplacementSource.IsValid) &&
            (!Source.IsAbilityRule || SkillId.IsValid && SkillEntryOperation.IsValid);

        public void Retain()
        {
            ContextId.Retain();
            SourceInputRequest.Retain();
            TargetKey.Retain();
            Source.Retain();
            EquipmentContext.SlotId.Retain();
            Opportunity.Retain();
        }

        public void Release()
        {
            ContextId.Release();
            SourceInputRequest.Release();
            TargetKey.Release();
            Source.Release();
            EquipmentContext.SlotId.Release();
            Opportunity.Release();
        }
    }

    internal readonly struct AbilityExecutionContext
    {
        public AbilityExecutionContext(
            CharacterSkillId skillId,
            OperationHandle entryOperation,
            ulong actionInstanceId,
            ulong predictionKey,
            ulong generation)
        {
            SkillId = skillId;
            EntryOperation = entryOperation;
            ActionInstanceId = actionInstanceId;
            PredictionKey = predictionKey;
            Generation = generation;
        }

        public CharacterSkillId SkillId { get; }
        public OperationHandle EntryOperation { get; }
        public ulong ActionInstanceId { get; }
        public ulong PredictionKey { get; }
        public ulong Generation { get; }
        public bool IsValid => SkillId.IsValid && EntryOperation.IsValid && ActionInstanceId != 0 && PredictionKey != 0;
    }

    internal interface IGameplayAbilityExecutionStorage<TValue>
        where TValue : struct, IEquatable<TValue>
    {
        bool IsAbilityStateSlot(int slotIndex);
        bool IsValueValid(int slotIndex, TValue value);
        TValue DefaultValue(int slotIndex);
        GameplayAbilityExecutionAggregate<TValue> ReadAggregate();
        void WriteAggregate(GameplayAbilityExecutionAggregate<TValue> aggregate);
    }

    internal readonly struct ActionSkillActivationCandidate<TTargetSnapshot, TOperation>
        where TTargetSnapshot : struct
        where TOperation : class
    {
        public ActionSkillActivationCandidate(
            CharacterSkillId skillId,
            OperationHandle skillEntryOperation,
            CanonicalTextValue contextId,
            SimulationInputRequestState sourceInputRequest,
            bool consumeSourceInputRequest,
            CanonicalTextValue targetKey,
            TTargetSnapshot targetSnapshot,
            SimulationExecutionSource source,
            EquipmentActionContext equipmentContext,
            TOperation operation = null,
            GameplayAbilitySourceContext replacementSource = default,
            string activationEntryId = "",
            GameplayAbilityRuleTargetPolicy targetPolicy = GameplayAbilityRuleTargetPolicy.Grant,
            bool requiresSourceInputRequest = true,
            bool requiresOpportunity = false,
            GameplayEffectOpportunityReference opportunity = default)
        {
            if (!source.IsValid)
                throw new ArgumentException("Action activation source is invalid.", nameof(source));
            SkillId = skillId;
            SkillEntryOperation = skillEntryOperation;
            ContextId = contextId.IsNull ? CanonicalTextValue.Empty : contextId;
            SourceInputRequest = sourceInputRequest;
            RequiresSourceInputRequest = requiresSourceInputRequest;
            ConsumeSourceInputRequest = consumeSourceInputRequest;
            TargetKey = targetKey.IsNull ? CanonicalTextValue.Empty : targetKey;
            TargetSnapshot = targetSnapshot;
            Source = source;
            EquipmentContext = equipmentContext;
            Operation = operation;
            ReplacementSource = replacementSource;
            TargetPolicy = targetPolicy;
            ActivationEntryId = activationEntryId ?? string.Empty;
            RequiresOpportunity = requiresOpportunity;
            Opportunity = opportunity;
        }

        public CharacterSkillId SkillId { get; }
        public OperationHandle SkillEntryOperation { get; }
        public CanonicalTextValue ContextId { get; }
        public SimulationInputRequestState SourceInputRequest { get; }
        public CanonicalTextValue SourceInputRequestId => SourceInputRequest.RequestId.IsNull
            ? CanonicalTextValue.Empty : SourceInputRequest.RequestId;
        public bool RequiresSourceInputRequest { get; }
        public bool ConsumeSourceInputRequest { get; }
        public CanonicalTextValue TargetKey { get; }
        public TTargetSnapshot TargetSnapshot { get; }
        public SimulationExecutionSource Source { get; }
        public EquipmentActionContext EquipmentContext { get; }
        public TOperation Operation { get; }
        public GameplayAbilitySourceContext ReplacementSource { get; }
        public GameplayAbilityRuleTargetPolicy TargetPolicy { get; }
        public bool RequiresOpportunity { get; }
        public GameplayEffectOpportunityReference Opportunity { get; }
        public string ActivationEntryId { get; }

        public void Retain()
        {
            ContextId.Retain();
            SourceInputRequest.Retain();
            TargetKey.Retain();
            Source.Retain();
            EquipmentContext.SlotId.Retain();
            Opportunity.Retain();
        }

        public void Release()
        {
            ContextId.Release();
            SourceInputRequest.Release();
            TargetKey.Release();
            Source.Release();
            EquipmentContext.SlotId.Release();
            Opportunity.Release();
        }
    }

    internal readonly struct ActionSkillActivationRequest<TTargetSnapshot>
        where TTargetSnapshot : struct
    {
        public ActionSkillActivationRequest(
            string actionId,
            CharacterSkillId skillId,
            OperationHandle skillEntryOperation,
            CanonicalTextValue contextId,
            SimulationInputRequestState sourceInputRequest,
            ulong inputSequence,
            ulong startTick,
            CanonicalTextValue targetKey,
            TTargetSnapshot targetSnapshot,
            SimulationExecutionSource source,
            EquipmentActionContext equipmentContext,
            GameplayAbilitySourceContext replacementSource = default,
            string activationEntryId = "",
            bool consumeSourceInputRequest = true,
            GameplayEffectOpportunityReference opportunity = default)
        {
            ActionId = SimulationIdentity.Require(actionId, nameof(actionId));
            ContextId = SimulationIdentity.Require(contextId, nameof(contextId));
            if (inputSequence == 0 || startTick == 0 || !source.IsValid ||
                source.IsAbilityRule && (!skillId.IsValid || !skillEntryOperation.IsValid))
            {
                throw new ArgumentException("Action activation request identity is incomplete.");
            }
            SkillId = skillId;
            SkillEntryOperation = skillEntryOperation;
            SourceInputRequest = sourceInputRequest;
            ConsumeSourceInputRequest = consumeSourceInputRequest;
            InputSequence = inputSequence;
            StartTick = startTick;
            TargetKey = targetKey.IsNull ? CanonicalTextValue.Empty : targetKey;
            TargetSnapshot = targetSnapshot;
            Source = source;
            EquipmentContext = equipmentContext;
            ReplacementSource = replacementSource;
            ActivationEntryId = activationEntryId ?? string.Empty;
            Opportunity = opportunity;
        }

        public string ActionId { get; }
        public CharacterSkillId SkillId { get; }
        public OperationHandle SkillEntryOperation { get; }
        public CanonicalTextValue ContextId { get; }
        public SimulationInputRequestState SourceInputRequest { get; }
        public CanonicalTextValue SourceInputRequestId => SourceInputRequest.RequestId.IsNull
            ? CanonicalTextValue.Empty : SourceInputRequest.RequestId;
        public bool ConsumeSourceInputRequest { get; }
        public ulong InputSequence { get; }
        public ulong StartTick { get; }
        public CanonicalTextValue TargetKey { get; }
        public TTargetSnapshot TargetSnapshot { get; }
        public SimulationExecutionSource Source { get; }
        public EquipmentActionContext EquipmentContext { get; }
        public GameplayAbilitySourceContext ReplacementSource { get; }
        public string ActivationEntryId { get; }

        public GameplayEffectOpportunityReference Opportunity { get; }

        public void Retain()
        {
            ContextId.Retain();
            SourceInputRequest.Retain();
            TargetKey.Retain();
            Source.Retain();
            EquipmentContext.SlotId.Retain();
            Opportunity.Retain();
        }

        public void Release()
        {
            ContextId.Release();
            SourceInputRequest.Release();
            TargetKey.Release();
            Source.Release();
            EquipmentContext.SlotId.Release();
            Opportunity.Release();
        }
    }

    internal interface IActionSkillCommitPort<TTargetSnapshot, TActionState>
        where TTargetSnapshot : struct
        where TActionState : struct
    {
        ulong NextActionInstanceId();
        ulong NextPredictionKey();
        TActionState CreatePredictedAction(
            ActionSkillActivationRequest<TTargetSnapshot> request,
            ulong instanceId,
            ulong predictionKey);
        void WriteAction(TActionState action, in GameplayAbilitySourceContext replacementSource);
        GameplayEffectApplyResult SetActionTags(ulong actionInstanceId, ActionAdmissionProfile profile);
        bool ApplyStartupEffects(TActionState action, out GameplayEffectApplyResult failure);
        GameplayEffectApplyResult CommitActionMutation(SimulationExecutionSource source);
        void ClearRequest(ActionSkillActivationRequest<TTargetSnapshot> request);
        void EmitActionFact(SimulationExecutionSource source, TActionState action);
    }

    internal enum ActionReplacementStopStatus : byte
    {
        Waiting = 0,
        Complete = 1,
        SourceChanged = 2
    }

    internal interface IActionSkillActivationPort<TTargetSnapshot, TOperation, TSavepoint>
        where TTargetSnapshot : struct
        where TOperation : class
        where TSavepoint : struct
    {
        GameplayCommandBatchAdmission ReceiveStartupCommands(SimulationExecutionSource source);
        TSavepoint CreateMutationSavepoint(ulong replacementActionInstanceId);
        void RestoreMutation(TSavepoint savepoint);
        void ReleaseMutation(TSavepoint savepoint);
        bool MutationIsActive(TSavepoint savepoint);
        void ReportStartupFailure(GameplayEffectApplyResult failure, SimulationExecutionSource source, bool requiresCommandAdmission);
        ulong InputSequence { get; }
        ulong Tick { get; }
        bool TraceEnabled { get; }
        bool TryReadInputRequest(CanonicalTextValue requestId, out SimulationInputRequestState request);
        bool IsInputRequestPending(in SimulationInputRequestState request);
        void ClearInputRequest(CanonicalTextValue requestId);
        bool ConsumeOpportunity(GameplayEffectOpportunityReference opportunity, SimulationExecutionSource source);
        TTargetSnapshot NoneTarget { get; }
        bool TryReadReplacementSource(in GameplayAbilitySourceContext source,
            out ActionAdmissionActiveAction activeSource, out TTargetSnapshot targetSnapshot);
        CanonicalTextValue TargetId(TTargetSnapshot targetSnapshot);
        bool HasPendingRequest(string actionId);
        void StageRequest(ActionSkillActivationRequest<TTargetSnapshot> request);
        bool TryReadPendingRequest(CharacterSkillId skillId, out ActionSkillActivationRequest<TTargetSnapshot> request);
        void ClearPendingRequest(ActionSkillActivationRequest<TTargetSnapshot> request);
        bool InterruptActive(in GameplayAbilitySourceContext replacementSource, SimulationExecutionSource source, CanonicalTextValue reason);
        ActionReplacementStopStatus ReadReplacementStopStatus(in GameplayAbilitySourceContext source);
        void Trace(TOperation operation, string code, ActionSkillTraceSeverity severity, string detail);
        void Trace(SimulationExecutionSource source, string code, ActionSkillTraceSeverity severity, string detail);
        void TraceActionResult(
            TOperation operation,
            string actionId,
            CharacterSkillId skillId,
            ulong actionInstanceId,
            ulong inputSequence,
            SimulationActionResultKind result,
            CanonicalTextValue reason, in GameplayAbilitySourceContext ruleSource, ulong effectHandle = 0);
        void TraceActionResult(
            SimulationExecutionSource source,
            string actionId,
            CharacterSkillId skillId,
            ulong actionInstanceId,
            ulong inputSequence,
            SimulationActionResultKind result,
            CanonicalTextValue reason, in GameplayAbilitySourceContext ruleSource, ulong effectHandle = 0);
    }

    public enum AbilityLifecyclePhase : byte
    {
        Startup = 0,
        Active = 1,
        Recovery = 2,
        Cancel = 3,
        Ended = 4
    }

    internal enum AbilityLifecycleState : byte
    {
        Requested = 0,
        Predicted = 1,
        Confirmed = 2,
        Rejected = 3,
        Cancelled = 4,
        Interrupted = 5,
        Aborted = 6,
        Ended = 7,
        Corrected = 8
    }

    internal enum AbilityLifecycleTransition : byte
    {
        None = 0,
        Confirm = 1,
        Complete = 2,
        Cancel = 3,
        Interrupt = 4,
        Reject = 5,
        Correct = 6,
        Abort = 7
    }

    internal readonly struct AbilityLifecycleIngress
    {
        public AbilityLifecycleIngress(
            StableHash identity,
            ulong actionInstanceId,
            ulong predictionKey,
            ulong inputSequence,
            int transitionValue,
            ulong sourceTick,
            CanonicalTextValue reason)
        {
            if (actionInstanceId == 0 && predictionKey == 0 && inputSequence == 0)
                throw new ArgumentException("Action lifecycle ingress requires an instance, prediction, or input identity.");
            if (transitionValue == 0)
                throw new ArgumentOutOfRangeException(nameof(transitionValue));
            Identity = identity;
            ActionInstanceId = actionInstanceId;
            PredictionKey = predictionKey;
            InputSequence = inputSequence;
            TransitionValue = transitionValue;
            SourceTick = sourceTick;
            Reason = reason.IsNull ? CanonicalTextValue.Empty : reason;
        }

        public StableHash Identity { get; }
        public ulong ActionInstanceId { get; }
        public ulong PredictionKey { get; }
        public ulong InputSequence { get; }
        public int TransitionValue { get; }
        public ulong SourceTick { get; }
        public CanonicalTextValue Reason { get; }

        public void Retain()
        {
            Reason.Retain();
        }

        public void Release()
        {
            Reason.Release();
        }
    }

    internal readonly struct AbilityLifecycleUpdate
    {
        public AbilityLifecycleUpdate(
            AbilityLifecyclePhase phase,
            AbilityLifecycleState state,
            AbilityLifecycleTransition transition,
            ulong transitionTick,
            ulong sourceTick,
            CanonicalTextValue reason)
        {
            Phase = phase;
            State = state;
            Transition = transition;
            TransitionTick = transitionTick;
            SourceTick = sourceTick;
            Reason = reason.IsNull ? CanonicalTextValue.Empty : reason;
        }

        public AbilityLifecyclePhase Phase { get; }
        public AbilityLifecycleState State { get; }
        public AbilityLifecycleTransition Transition { get; }
        public ulong TransitionTick { get; }
        public ulong SourceTick { get; }
        public CanonicalTextValue Reason { get; }

        public void Retain()
        {
            Reason.Retain();
        }

        public void Release()
        {
            Reason.Release();
        }
    }

    internal interface IAbilityLifecyclePort<TActionState, TReconciliationCommand>
        where TActionState : struct
    {
        ulong Tick { get; }
        IReadOnlyList<TActionState> ActionStates { get; }
        bool TryFindActive(CanonicalTextValue contextId, out TActionState action);
        bool TryFindActive(CharacterSkillId skillId, out TActionState action);
        bool IsActive(TActionState action);
        string ActionId(TActionState action);
        CharacterSkillId SkillId(TActionState action);
        ulong InstanceId(TActionState action);
        ulong PredictionKey(TActionState action);
        ulong InputSequence(TActionState action);
        CanonicalTextValue Reason(TActionState action);
        SimulationExecutionSource Source(TActionState action);
        EquipmentActionContext EquipmentContext(TActionState action);
        AbilityLifecyclePhase Phase(TActionState action);
        AbilityLifecycleState State(TActionState action);
        IDisposable EnterExecution(TActionState action);
        TActionState WithLifecycle(TActionState action, AbilityLifecycleUpdate update);
        void WriteState(TActionState action);
        void EmitActionFact(SimulationExecutionSource source, TActionState action);
        GameplayEffectReconciliationResult Reconcile(TActionState action, AbilityLifecycleTransition transition,
            IReadOnlyList<TReconciliationCommand> commands, SimulationExecutionSource source, bool requiresCommandReception);
        void RemoveActionTags(TActionState action, SimulationExecutionSource source);
        ulong SourceGeneration(SimulationExecutionSource source);
        bool TraceEnabled { get; }
        void Trace(SimulationExecutionSource source, string code, ActionSkillTraceSeverity severity, string detail, ulong generation);
        void TraceActionResult(
            SimulationExecutionSource source,
            string actionId,
            CharacterSkillId skillId,
            ulong actionInstanceId,
            ulong inputSequence,
            SimulationActionResultKind result,
            CanonicalTextValue reason);
    }
}
