using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;
using ThirdPersonCharacter.Pipeline.Input;
using System;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation.FPGameplay
{
    [ValueUnion(typeof(ProgramInputValue<FP, FPVector2, FPVector3, FP, SimulationActionTargetSnapshot>))]
    public readonly partial struct SimulationInputValue
    {

        [ValueValidation]
        void RequireIdentity() => SimulationIdentity.Require(InputId, nameof(InputId));
    }

    [ValueOperations]
    public readonly partial struct SimulationInputRequest
    {
        public SimulationInputRequest(CanonicalTextValue requestId, ulong sequence, ulong sourceTick,
            ulong expireSimulationTick, int priority, CharacterActionRequestTimingClass timingClass)
        {
            RequestId = requestId;
            Sequence = sequence;
            SourceTick = sourceTick;
            ExpireSimulationTick = expireSimulationTick;
            Priority = priority;
            TimingClass = timingClass;
            RequireValid();
        }

        public CanonicalTextValue RequestId { get; }
        public ulong Sequence { get; }
        public ulong SourceTick { get; }
        public ulong ExpireSimulationTick { get; }
        public int Priority { get; }
        public CharacterActionRequestTimingClass TimingClass { get; }

        [ValueUpdate]
        internal partial SimulationInputRequest WithTicks(ulong sourceTick, ulong expireSimulationTick);

        [ValueValidation]
        void RequireValid()
        {
            SimulationIdentity.Require(RequestId, nameof(RequestId));
            if (Sequence == 0)
                throw new ArgumentOutOfRangeException(nameof(Sequence));
            if (TimingClass != CharacterActionRequestTimingClass.Immediate &&
                TimingClass != CharacterActionRequestTimingClass.Offensive)
                throw new ArgumentOutOfRangeException(nameof(TimingClass));
        }
    }

    public readonly struct SimulationInput
    {
        readonly SimulationInputFrameStorage m_Storage;
        readonly uint m_Generation;
        readonly SimulationTickSourceIdentity m_StepSource;
        readonly bool m_HasStepSource;

        public SimulationInput(SimulationNumericProfile numericProfile,
            SimulationTickSourceIdentity tickSource, CanonicalTextValue inputSourceIdentity, ulong sequence,
            IEnumerable<SimulationInputValue> values, IEnumerable<SimulationInputRequest> requests)
        {
            if (!numericProfile.IsValid || sequence == 0)
                throw new ArgumentOutOfRangeException(nameof(sequence));
            m_Storage = new SimulationInputFrameStorage(numericProfile, tickSource,
                SimulationIdentity.Require(inputSourceIdentity, nameof(inputSourceIdentity)), sequence,
                SortValues(values), SortRequests(requests));
            m_Generation = 0;
            m_StepSource = default;
            m_HasStepSource = false;
        }

        internal SimulationInput(SimulationInputFrameStorage storage, uint generation)
        {
            m_Storage = storage;
            m_Generation = generation;
            m_StepSource = default;
            m_HasStepSource = false;
        }

        SimulationInput(SimulationInput input, SimulationTickSourceIdentity stepSource)
        {
            m_Storage = input.m_Storage;
            m_Generation = input.m_Generation;
            m_StepSource = stepSource;
            m_HasStepSource = true;
        }

        public SimulationInput BindStepSource(SimulationTickSourceIdentity stepSource) =>
            new SimulationInput(this, stepSource);

        SimulationInputFrameStorage Data => m_Storage.Read(m_Generation);
        internal SimulationInputHeader Header => m_HasStepSource ? Data.Header.BindStepSource(m_StepSource) : Data.Header;
        internal SimulationInputValues CanonicalValues => Data.CanonicalValues;
        public bool IsValid => m_Storage != null;
        public SimulationNumericProfile NumericProfile => Data.NumericProfile;
        public SimulationTickSourceIdentity TickSource => m_HasStepSource ? m_StepSource : Data.TickSource;
        public CanonicalTextValue InputSourceIdentity => Data.InputSourceIdentity;
        public ulong Sequence => Data.Sequence;
        public IReadOnlyList<SimulationInputValue> Values => Data.Values;
        public IReadOnlyList<SimulationInputRequest> Requests => Data.Requests;

        public void RetainText()
        {
            Header.Retain();
            CanonicalValues.Retain();
        }

        public void ReleaseText()
        {
            CanonicalValues.Release();
            Header.Release();
        }

        public SimulationInput CaptureOwned() => Data.IsOwned ? this :
            new SimulationInput(new SimulationInputFrameStorage(NumericProfile, TickSource,
                InputSourceIdentity, Sequence, Copy(Values), Copy(Requests)), 0);

        public SimulationInput RebindSource(SimulationTickSourceIdentity tickSource) =>
            RebindSource(tickSource, InputSourceIdentity);

        public SimulationInput RebindSource(SimulationTickSourceIdentity tickSource, CanonicalTextValue inputSourceIdentity)
        {
            SimulationInput source = CaptureOwned();
            return new SimulationInput(new SimulationInputFrameStorage(NumericProfile, tickSource,
                SimulationIdentity.Require(inputSourceIdentity, nameof(inputSourceIdentity)), Sequence,
                source.Values, source.Requests), 0);
        }

        internal static ulong RemapTick(ulong recordedTick, ulong recordedFrameTick, ulong replayFrameTick)
        {
            if (recordedTick == 0)
                return 0;
            if (recordedTick >= recordedFrameTick)
                return checked(replayFrameTick + recordedTick - recordedFrameTick);
            ulong age = recordedFrameTick - recordedTick;
            if (age >= replayFrameTick)
                throw new InvalidOperationException("FP character input request predates the replay clock.");
            return replayFrameTick - age;
        }

        static SimulationInputValue[] SortValues(IEnumerable<SimulationInputValue> values)
        {
            SimulationInputValue[] result = Copy(values);
            Array.Sort(result, (left, right) => CanonicalTextValue.CompareOrdinal(left.InputId, right.InputId));
            for (int i = 1; i < result.Length; i++)
            {
                if (CanonicalTextValue.Equals(result[i - 1].InputId, result[i].InputId, StringComparison.Ordinal))
                    throw new ArgumentException($"Duplicate simulation input value '{result[i].InputId}'.", nameof(values));
            }
            return result;
        }

        static SimulationInputRequest[] SortRequests(IEnumerable<SimulationInputRequest> requests)
        {
            SimulationInputRequest[] result = Copy(requests);
            Array.Sort(result, (left, right) =>
            {
                int bySequence = left.Sequence.CompareTo(right.Sequence);
                return bySequence != 0 ? bySequence : CanonicalTextValue.CompareOrdinal(left.RequestId, right.RequestId);
            });
            for (int i = 1; i < result.Length; i++)
            {
                if (result[i - 1].Sequence == result[i].Sequence)
                    throw new ArgumentException($"Duplicate simulation input request sequence '{result[i].Sequence}'.", nameof(requests));
            }
            return result;
        }

        static T[] Copy<T>(IEnumerable<T> source)
        {
            if (source == null)
                return Array.Empty<T>();
            if (source is ICollection<T> collection)
            {
                if (collection.Count == 0)
                    return Array.Empty<T>();
                var result = new T[collection.Count];
                collection.CopyTo(result, 0);
                return result;
            }
            return new List<T>(source).ToArray();
        }
    }

    public enum SimulationIngressKind : byte
    {
        ActionLifecycle = 1,
        GameplayResult = 2,
        GameplayEffectLifecycle = 3,
        AttributeValue = 4,
        ActionEvent = 5
    }

    public enum SimulationActionPhase : byte
    {
        Startup = 0,
        Active = 1,
        Recovery = 2,
        Cancel = 3,
        Ended = 4
    }

    public enum SimulationActionState : byte
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

    public enum SimulationActionLifecycleTransitionType : byte
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

    [ValueOperations]
    public readonly partial struct SimulationActionTargetSnapshot : IEquatable<SimulationActionTargetSnapshot>, IValueResources
    {
        public SimulationActionTargetSnapshot(CanonicalTextValue targetId, FPVector3 position, FP yaw)
        {
            TargetId = targetId;
            Position = position;
            Yaw = yaw;
        }

        public CanonicalTextValue TargetId { get; }
        public FPVector3 Position { get; }
        public FP Yaw { get; }
        public bool HasTarget => !TargetId.IsEmpty;
        public bool Equals(SimulationActionTargetSnapshot other) =>
            TargetId.Equals(other.TargetId) &&
            Position.Equals(other.Position) &&
            Yaw.Equals(other.Yaw);
        public static SimulationActionTargetSnapshot None => new SimulationActionTargetSnapshot(string.Empty, FPVector3.Zero, FP._0);
    }

    public readonly struct SimulationActionLifecycleIngress
    {
        public SimulationActionLifecycleIngress(
            ulong actionInstanceId,
            ulong predictionKey,
            ulong inputSequence,
            SimulationActionLifecycleTransitionType transitionType,
            CanonicalTextValue reason,
            IReadOnlyList<SimulationGameplayEffectLifecycleIngress> gameplayEffectCommands)
        {
            if (actionInstanceId == 0 && predictionKey == 0 && inputSequence == 0)
                throw new ArgumentException("Action lifecycle ingress requires an instance, prediction, or input identity.");
            if (transitionType == SimulationActionLifecycleTransitionType.None)
                throw new ArgumentOutOfRangeException(nameof(transitionType));
            ActionInstanceId = actionInstanceId;
            PredictionKey = predictionKey;
            InputSequence = inputSequence;
            TransitionType = transitionType;
            Reason = reason;
            GameplayEffectCommands = gameplayEffectCommands ?? throw new ArgumentNullException(nameof(gameplayEffectCommands));
        }

        public ulong ActionInstanceId { get; }
        public ulong PredictionKey { get; }
        public ulong InputSequence { get; }
        public SimulationActionLifecycleTransitionType TransitionType { get; }
        public CanonicalTextValue Reason { get; }
        public IReadOnlyList<SimulationGameplayEffectLifecycleIngress> GameplayEffectCommands { get; }
        public bool IsValid =>
            (ActionInstanceId != 0 || PredictionKey != 0 || InputSequence != 0) &&
            TransitionType != SimulationActionLifecycleTransitionType.None;
    }

    public readonly struct SimulationIngressHeader
    {
        public SimulationIngressHeader(
            SimulationNumericProfile numericProfile,
            ActorId actorId,
            ulong sourceTick,
            ulong sequence,
            StableHash factIdentity,
            SimulationIngressKind kind)
        {
            if (!numericProfile.IsValid || !actorId.IsValid || sourceTick == 0 || sequence == 0 || !factIdentity.IsValid)
                throw new ArgumentException("Simulation ingress header is incomplete.");
            NumericProfile = numericProfile;
            ActorId = actorId;
            SourceTick = sourceTick;
            Sequence = sequence;
            FactIdentity = factIdentity;
            Kind = kind;
        }

        public SimulationNumericProfile NumericProfile { get; }
        public ActorId ActorId { get; }
        public ulong SourceTick { get; }
        public ulong Sequence { get; }
        public StableHash FactIdentity { get; }
        public SimulationIngressKind Kind { get; }
    }

    public readonly struct SimulationIngress
    {
        public SimulationIngress(SimulationIngressHeader header, SimulationActionLifecycleIngress actionLifecycle)
        {
            if (header.Kind != SimulationIngressKind.ActionLifecycle || !actionLifecycle.IsValid)
                throw new ArgumentException("Action lifecycle ingress header and payload do not match.");
            Header = header;
            ActionEvent = default;
            ActionLifecycle = actionLifecycle;
            GameplayResult = default;
            GameplayEffectLifecycle = null;
            AttributeValue = default;
        }

        public SimulationIngress(SimulationIngressHeader header, SimulationGameplayResultIngress gameplayResult)
        {
            if (header.Kind != SimulationIngressKind.GameplayResult || !gameplayResult.IsValid)
                throw new ArgumentException("Gameplay Result ingress header and payload do not match.");
            Header = header;
            ActionEvent = default;
            ActionLifecycle = default;
            GameplayResult = gameplayResult;
            GameplayEffectLifecycle = null;
            AttributeValue = default;
        }

        public SimulationIngress(SimulationIngressHeader header, SimulationGameplayEffectLifecycleIngress gameplayEffectLifecycle)
        {
            if (header.Kind != SimulationIngressKind.GameplayEffectLifecycle || gameplayEffectLifecycle == null || !gameplayEffectLifecycle.IsValid)
                throw new ArgumentException("Gameplay Effect lifecycle ingress header and payload do not match.");
            Header = header;
            ActionEvent = default;
            ActionLifecycle = default;
            GameplayResult = default;
            GameplayEffectLifecycle = gameplayEffectLifecycle;
            AttributeValue = default;
        }

        public SimulationIngress(SimulationIngressHeader header, SimulationAttributeValueIngress attributeValue)
        {
            if (header.Kind != SimulationIngressKind.AttributeValue || !attributeValue.IsValid)
                throw new ArgumentException("Attribute value ingress header and payload do not match.");
            Header = header;
            ActionEvent = default;
            ActionLifecycle = default;
            GameplayResult = default;
            GameplayEffectLifecycle = null;
            AttributeValue = attributeValue;
        }

        public SimulationIngress(SimulationIngressHeader header, SimulationActionEventIngress actionEvent)
        {
            if (header.Kind != SimulationIngressKind.ActionEvent || !actionEvent.IsValid)
                throw new ArgumentException("Action event ingress header and payload do not match.");
            Header = header;
            ActionEvent = actionEvent;
            ActionLifecycle = default;
            GameplayResult = default;
            GameplayEffectLifecycle = null;
            AttributeValue = default;
        }

        public SimulationActionEventIngress ActionEvent { get; }

        public SimulationIngressHeader Header { get; }
        internal int GameplayCommandCount => Header.Kind == SimulationIngressKind.ActionLifecycle
            ? Math.Max(1, ActionLifecycle.GameplayEffectCommands.Count)
            : Header.Kind == SimulationIngressKind.GameplayResult && !GameplayResult.HasEffect ||
                Header.Kind == SimulationIngressKind.ActionEvent ? 0 : 1;
        public SimulationActionLifecycleIngress ActionLifecycle { get; }
        public SimulationGameplayResultIngress GameplayResult { get; }
        public SimulationGameplayEffectLifecycleIngress GameplayEffectLifecycle { get; }
        public SimulationAttributeValueIngress AttributeValue { get; }
    }

    [ValueOperations]
    public readonly partial struct SimulationEventHeader
    {
        public SimulationEventHeader(
            SimulationNumericProfile numericProfile,
            EventId eventId,
            ActorId actorId,
            SimulationTick tick,
            ActivationId activation,
            ulong sequence,
            string channel,
            AbilityTreeClipInvocation? treeClipInvocation = null)
        {
            NumericProfile = numericProfile;
            EventId = eventId;
            ActorId = actorId;
            Tick = tick;
            Activation = activation;
            Sequence = sequence;
            Channel = SimulationIdentity.Require(channel, nameof(channel));
            TreeClipInvocation = treeClipInvocation;
            RequireRecord();
        }

        public SimulationNumericProfile NumericProfile { get; }
        public EventId EventId { get; }
        public ActorId ActorId { get; }
        public SimulationTick Tick { get; }
        public ActivationId Activation { get; }
        public ulong Sequence { get; }
        [ValueTextRules(Required = true, Trim = true)]
        public string Channel { get; }
        public AbilityTreeClipInvocation? TreeClipInvocation { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (!NumericProfile.IsValid || !EventId.IsValid || !ActorId.IsValid || !Tick.IsValid || !Activation.IsValid || Sequence == 0)
                throw new ArgumentException("Simulation event header is incomplete.");
        }
    }

    [ValueUnionTemplate]
    public enum GameplayFactKind : byte
    {
        [ValueUnionCase(typeof(ActionFact))]
        Action = 1,
        [ValueUnionCase(typeof(GameplayEffectFact))]
        Effect = 2,
        [ValueUnionCase(typeof(GameplayAttributeFact))]
        Attribute = 3,
        [ValueUnionCase(typeof(GameplayCueFact))]
        Cue = 4,
        [ValueUnionCase(typeof(GameplayStateFact<FP>))]
        Motion = 5,
        [ValueUnionCase(typeof(GameplayStateFact<FP>))]
        State = 6,
        [ValueUnionCase(typeof(ActionWindowFact))]
        ActionWindow = 7,
        [ValueUnionCase(typeof(GameplayTagRuntimeChange))]
        Tag = 8,
        [ValueUnionCase(typeof(GameplayEffectExecutionFailure))]
        Failure = 9,
        [ValueUnionCase(typeof(GameplayEffectReconciliationFact<SimulationGameplayEffectContext>))]
        Reconciliation = 10,
        [ValueUnionCase(typeof(EquipmentLifecycleFact))]
        Equipment = 11
    }

    public readonly partial struct ActionFact
    {
        public ActionFact(
            ulong actionInstanceId,
            ulong predictionKey,
            ulong inputSequence,
            string actionId,
            CharacterSkillId skillId,
            SimulationActionLifecycleTransitionType transitionType,
            SimulationActionPhase phase,
            SimulationActionState state,
            CanonicalTextValue reason,
            EquipmentActionContext equipmentContext = default)
        {
            ActionInstanceId = actionInstanceId;
            PredictionKey = predictionKey;
            InputSequence = inputSequence;
            ActionId = SimulationIdentity.Require(actionId, nameof(actionId));
            SkillId = skillId;
            TransitionType = transitionType;
            Phase = phase;
            State = state;
            Reason = reason;
            EquipmentContext = equipmentContext;
            RequireRecord();
        }

        public ulong ActionInstanceId { get; }
        public ulong PredictionKey { get; }
        public ulong InputSequence { get; }
        [ValueTextRules(Required = true, Trim = true)]
        public string ActionId { get; }
        public CharacterSkillId SkillId { get; }
        public SimulationActionLifecycleTransitionType TransitionType { get; }
        public SimulationActionPhase Phase { get; }
        public SimulationActionState State { get; }
        public CanonicalTextValue Reason { get; }
        public EquipmentActionContext EquipmentContext { get; }
        public bool IsValid => ActionInstanceId != 0 && PredictionKey != 0 && InputSequence != 0 && !CanonicalTextValue.IsNullOrEmpty(ActionId);

        [ValueValidation]
        void RequireRecord()
        {
            if (ActionInstanceId == 0 || PredictionKey == 0 || InputSequence == 0)
                throw new ArgumentException("Action fact identity is incomplete.");
        }
    }

    public readonly partial struct ActionWindowFact
    {
        public ActionWindowFact(
            GameplayAbilitySourceContext sourceContext,
            string actionId,
            string windowId,
            string windowType,
            string windowTag,
            ulong startTick,
            ulong endTick,
            ulong digest)
        {
            SourceContext = sourceContext;
            ActionId = SimulationIdentity.Require(actionId, nameof(actionId));
            WindowId = SimulationIdentity.Require(windowId, nameof(windowId));
            WindowType = SimulationIdentity.Require(windowType, nameof(windowType));
            WindowTag = SimulationIdentity.Require(windowTag, nameof(windowTag));
            StartTick = startTick;
            EndTick = endTick;
            Digest = digest;
            RequireRecord();
        }

        public GameplayAbilitySourceContext SourceContext { get; }
        public ulong ActionInstanceId => SourceContext.InstanceId;
        [ValueTextRules(Required = true, Trim = true)]
        public string ActionId { get; }
        [ValueTextRules(Required = true, Trim = true)]
        public string WindowId { get; }
        [ValueTextRules(Required = true, Trim = true)]
        public string WindowType { get; }
        [ValueTextRules(Required = true, Trim = true, Declared = true)]
        public string WindowTag { get; }
        public ulong StartTick { get; }
        public ulong EndTick { get; }
        public ulong Digest { get; }
        public bool IsValid => ActionInstanceId != 0 && StartTick != 0;

        [ValueValidation]
        void RequireRecord()
        {
            if (SourceContext.InstanceId == 0 || StartTick == 0 || EndTick < StartTick)
                throw new ArgumentException("Action Window fact identity is incomplete.");
        }
    }

    [ValueUnion(typeof(GameplayFactKind))]
    [ValueOperations]
    public readonly partial struct GameplayFact
    {
        public const int SchemaVersion = 5;

        public GameplayFact(SimulationEventHeader header, GameplayFactKind kind, CanonicalTextValue subjectId, CanonicalTextValue stateId, FP scalar) {
            var value = new GameplayStateFact<FP>(subjectId, stateId, scalar);
            this = kind switch
            {
                GameplayFactKind.Motion => FromMotion(default, header, 0, value),
                GameplayFactKind.State => FromState(default, header, 0, value),
                _ => throw new ArgumentException($"'{kind}' facts require a typed payload.", nameof(kind))
            };
        }

        public GameplayFact(SimulationEventHeader header, ActionFact action) {
            if (!action.IsValid)
                throw new ArgumentException("Action fact payload is invalid.", nameof(action));
            this = FromAction(default, header, 0, action);
        }

        public GameplayFact(SimulationEventHeader header, ActionWindowFact actionWindow) {
            if (!actionWindow.IsValid)
                throw new ArgumentException("Action Window fact payload is invalid.", nameof(actionWindow));
            this = FromActionWindow(default, header, 0, actionWindow);
        }

        public GameplayFact(SimulationEventHeader header, GameplayEffectFact effect, CanonicalTextValue cause = default,
            ulong mutationCursor = 0) {
            if (!effect.IsValid)
                throw new ArgumentException("Gameplay Effect fact payload is invalid.", nameof(effect));
            this = FromEffect(cause, header, mutationCursor, effect);
        }

        public GameplayFact(SimulationEventHeader header, GameplayAttributeFact attribute, CanonicalTextValue cause = default,
            ulong mutationCursor = 0) {
            if (!attribute.IsValid)
                throw new ArgumentException("Gameplay Attribute fact payload is invalid.", nameof(attribute));
            this = FromAttribute(cause, header, mutationCursor, attribute);
        }

        public GameplayFact(SimulationEventHeader header, GameplayCueFact cue, CanonicalTextValue cause = default,
            ulong mutationCursor = 0) {
            if (!cue.IsValid)
                throw new ArgumentException("Gameplay Cue fact payload is invalid.", nameof(cue));
            this = FromCue(cause, header, mutationCursor, cue);
        }

        public GameplayFact(SimulationEventHeader header, GameplayTagRuntimeChange tag, CanonicalTextValue cause,
            ulong mutationCursor = 0) {
            this = FromTag(cause, header, mutationCursor, tag);
        }

        public GameplayFact(SimulationEventHeader header, GameplayEffectExecutionFailure failure, CanonicalTextValue cause,
            ulong mutationCursor = 0) {
            this = FromFailure(cause, header, mutationCursor, failure);
        }

        public GameplayFact(SimulationEventHeader header, GameplayEffectReconciliationResult reconciliation,
            SimulationGameplayEffectContext context, CanonicalTextValue cause, ulong mutationCursor = 0) {
            this = FromReconciliation(cause, header, mutationCursor, new GameplayEffectReconciliationFact<SimulationGameplayEffectContext>(reconciliation, context));
        }
        public GameplayFact(SimulationEventHeader header, EquipmentLifecycleFact equipment) {
            this = FromEquipment(default, header, 0, equipment);
        }

        public SimulationEventHeader Header { get; }
        public CanonicalTextValue SubjectId => Kind switch
        {
            GameplayFactKind.Action => Action.ActionId,
            GameplayFactKind.ActionWindow => ActionWindow.WindowId,
            GameplayFactKind.Effect => Effect.EffectId,
            GameplayFactKind.Attribute => Attribute.AttributeId,
            GameplayFactKind.Cue => Cue.CueId,
            GameplayFactKind.Tag => Tag.TagId,
            GameplayFactKind.Failure => Failure.RequestedEffectId,
            GameplayFactKind.Reconciliation => Reconciliation.Result.Scope.EffectId,
            GameplayFactKind.Equipment => Equipment.SlotId.Value,
            GameplayFactKind.Motion => Motion.SubjectId,
            GameplayFactKind.State => State.SubjectId,
            _ => default
        };
        public CanonicalTextValue StateId => Kind switch
        {
            GameplayFactKind.Action => ActionTransitionName(Action.TransitionType),
            GameplayFactKind.ActionWindow => ActionWindow.WindowType,
            GameplayFactKind.Effect => LifecycleName(Effect.Operation),
            GameplayFactKind.Attribute or GameplayFactKind.Tag => "Changed",
            GameplayFactKind.Cue => Cue.TriggerId,
            GameplayFactKind.Failure => "Failed",
            GameplayFactKind.Reconciliation => "Reconciled",
            GameplayFactKind.Equipment => Equipment.StateName,
            GameplayFactKind.Motion => Motion.StateId,
            GameplayFactKind.State => State.StateId,
            _ => default
        };
        public FP Scalar => Kind switch
        {
            GameplayFactKind.Attribute => Attribute.CurrentValue,
            GameplayFactKind.Motion => Motion.Scalar,
            GameplayFactKind.State => State.Scalar,
            _ => default
        };
        public SimulationGameplayEffectContext EffectContext => Reconciliation.Context;
        public CanonicalTextValue Cause { get; }
        public ulong MutationCursor { get; }


        static string ActionTransitionName(SimulationActionLifecycleTransitionType transition) => transition switch
        {
            SimulationActionLifecycleTransitionType.None => "Activated",
            SimulationActionLifecycleTransitionType.Confirm => nameof(SimulationActionLifecycleTransitionType.Confirm),
            SimulationActionLifecycleTransitionType.Complete => nameof(SimulationActionLifecycleTransitionType.Complete),
            SimulationActionLifecycleTransitionType.Cancel => nameof(SimulationActionLifecycleTransitionType.Cancel),
            SimulationActionLifecycleTransitionType.Interrupt => nameof(SimulationActionLifecycleTransitionType.Interrupt),
            SimulationActionLifecycleTransitionType.Reject => nameof(SimulationActionLifecycleTransitionType.Reject),
            SimulationActionLifecycleTransitionType.Correct => nameof(SimulationActionLifecycleTransitionType.Correct),
            SimulationActionLifecycleTransitionType.Abort => nameof(SimulationActionLifecycleTransitionType.Abort),
            _ => throw new ArgumentOutOfRangeException(nameof(transition))
        };

        static string LifecycleName(SimulationGameplayEffectLifecycleOperation operation) => operation switch
        {
            SimulationGameplayEffectLifecycleOperation.Applied => nameof(SimulationGameplayEffectLifecycleOperation.Applied),
            SimulationGameplayEffectLifecycleOperation.Confirmed => nameof(SimulationGameplayEffectLifecycleOperation.Confirmed),
            SimulationGameplayEffectLifecycleOperation.Rejected => nameof(SimulationGameplayEffectLifecycleOperation.Rejected),
            SimulationGameplayEffectLifecycleOperation.StackChanged => nameof(SimulationGameplayEffectLifecycleOperation.StackChanged),
            SimulationGameplayEffectLifecycleOperation.Inhibited => nameof(SimulationGameplayEffectLifecycleOperation.Inhibited),
            SimulationGameplayEffectLifecycleOperation.Resumed => nameof(SimulationGameplayEffectLifecycleOperation.Resumed),
            SimulationGameplayEffectLifecycleOperation.PeriodExecuted => nameof(SimulationGameplayEffectLifecycleOperation.PeriodExecuted),
            SimulationGameplayEffectLifecycleOperation.Removed => nameof(SimulationGameplayEffectLifecycleOperation.Removed),
            SimulationGameplayEffectLifecycleOperation.Expired => nameof(SimulationGameplayEffectLifecycleOperation.Expired),
            SimulationGameplayEffectLifecycleOperation.Corrected => nameof(SimulationGameplayEffectLifecycleOperation.Corrected),
            SimulationGameplayEffectLifecycleOperation.Overflow => nameof(SimulationGameplayEffectLifecycleOperation.Overflow),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    public enum PresentationCommandKind : byte
    {
        SelectProducer = 1,
        SampleProducer = 2,
        CompleteProducer = 3,
        ReleaseProducer = 4,
        Camera = 5,
        Cue = 6,
        Vfx = 7,
        Ui = 8,
        ForceProducer = 9,
        DomainEvent = 10,
        ForceReleaseProducer = 11,
        TimelineProgress = 12,
        Audio = 13,
        CombatHit = 14
    }

    [ValueOperations]
    public readonly partial struct PresentationCommand
    {
        public PresentationCommand(
            SimulationEventHeader header,
            PresentationCommandKind kind,
            string producerId,
            FP sampleTime,
            FP weight,
            ulong producerGeneration = 0,
            int cycle = 0,
            ulong sourceActionInstanceId = 0,
            FP visualTimeScale = default,
            CanonicalTextValue domainPayload = default,
            PresentationCameraRequest cameraRequest = default,
            AbilityTimelineProgress timelineProgress = default,
            PresentationAudioRequest audioRequest = default,
            AudioPlaybackOwner audioOwner = default,
            ActorId audioTargetActor = default,
            CombatHitResult combatHit = null)
        {
            Header = header;
            Kind = kind;
            ProducerId = SimulationIdentity.Require(producerId, nameof(producerId));
            SampleTime = sampleTime;
            Weight = weight;
            ProducerGeneration = producerGeneration;
            Cycle = cycle;
            SourceActionInstanceId = sourceActionInstanceId;
            VisualTimeScale = visualTimeScale;
            DomainPayload = domainPayload;
            CameraRequest = cameraRequest;
            TimelineProgress = timelineProgress;
            AudioRequest = audioRequest;
            AudioOwner = audioOwner;
            AudioTargetActor = audioTargetActor;
            CombatHit = combatHit;
            RequireRecord();
        }
        public SimulationEventHeader Header { get; }
        public PresentationCommandKind Kind { get; }
        [ValueTextRules(Required = true, Trim = true)]
        public string ProducerId { get; }
        public FP SampleTime { get; }
        public FP Weight { get; }
        public ulong ProducerGeneration { get; }
        public int Cycle { get; }
        public ulong SourceActionInstanceId { get; }
        public CanonicalTextValue DomainPayload { get; }
        public FP VisualTimeScale { get; }
        public PresentationCameraRequest CameraRequest { get; }
        public AbilityTimelineProgress TimelineProgress { get; }
        public PresentationAudioRequest AudioRequest { get; }
        public AudioPlaybackOwner AudioOwner { get; }
        public ActorId AudioTargetActor { get; }
        public CombatHitResult CombatHit { get; }

        PresentationCommand(in PresentationCommand command, CombatHitResult combatHit)
        {
            Header = command.Header;
            Kind = command.Kind;
            ProducerId = command.ProducerId;
            SampleTime = command.SampleTime;
            Weight = command.Weight;
            ProducerGeneration = command.ProducerGeneration;
            Cycle = command.Cycle;
            SourceActionInstanceId = command.SourceActionInstanceId;
            VisualTimeScale = command.VisualTimeScale;
            DomainPayload = command.DomainPayload;
            CameraRequest = command.CameraRequest;
            TimelineProgress = command.TimelineProgress;
            AudioRequest = command.AudioRequest;
            AudioOwner = command.AudioOwner;
            AudioTargetActor = command.AudioTargetActor;
            CombatHit = combatHit;
        }

        public PresentationCommand WithCombatHit(CombatHitResult combatHit) => new PresentationCommand(in this, combatHit);


        static bool IsPlaybackCommand(PresentationCommandKind kind)
        {
            return kind == PresentationCommandKind.SelectProducer ||
                   kind == PresentationCommandKind.SampleProducer ||
                   kind == PresentationCommandKind.CompleteProducer ||
                   kind == PresentationCommandKind.ReleaseProducer;
        }

        static bool RequiresProducerGeneration(PresentationCommandKind kind) =>
            IsPlaybackCommand(kind) ||
            kind == PresentationCommandKind.Camera ||
            kind == PresentationCommandKind.Cue ||
            kind == PresentationCommandKind.ForceProducer;

        static bool IsPlaybackSample(PresentationCommandKind kind) =>
            kind == PresentationCommandKind.SampleProducer;

        [ValueValidation]
        void RequireRecord()
        {
            if ((Kind == PresentationCommandKind.CombatHit) != (CombatHit != null))
                throw new ArgumentException("Combat feedback commands require their accepted hit result.", "CombatHit");
            if ((Kind == PresentationCommandKind.TimelineProgress) != TimelineProgress.IsValid ||
                TimelineProgress.IsValid && (SourceActionInstanceId == 0 || TimelineProgress.LogicTick != Header.Tick.Value))
                throw new ArgumentException("Timeline progress requires its exact logic tick and Action identity.", "TimelineProgress");
            if (RequiresProducerGeneration(Kind) && ProducerGeneration == 0)
                throw new ArgumentOutOfRangeException("ProducerGeneration");
            if (RequiresProducerGeneration(Kind) && ProducerGeneration != Header.Activation.Generation)
                throw new ArgumentException("Presentation producer generation does not match the event activation.", "ProducerGeneration");
            if (IsPlaybackCommand(Kind) && SourceActionInstanceId == 0)
                throw new ArgumentOutOfRangeException("SourceActionInstanceId");
            if (Cycle < 0)
                throw new ArgumentOutOfRangeException("Cycle");
            if (Kind == PresentationCommandKind.DomainEvent &&
                (SourceActionInstanceId == 0 || CanonicalTextValue.IsNullOrWhiteSpace(DomainPayload)))
                throw new ArgumentException("Domain event command requires an Action instance and payload.", "DomainPayload");
            if (Kind != PresentationCommandKind.DomainEvent && !CanonicalTextValue.IsNullOrEmpty(DomainPayload))
                throw new ArgumentException("Domain payload is only valid on Domain event commands.", "DomainPayload");
            if (Kind == PresentationCommandKind.Camera && !CameraRequest.IsValid)
                throw new ArgumentException("Camera commands require a typed camera request.", "CameraRequest");
            if (Kind != PresentationCommandKind.Camera && CameraRequest.IsValid)
                throw new ArgumentException("Typed camera requests are only valid on Camera commands.", "CameraRequest");
            if (IsPlaybackSample(Kind) && VisualTimeScale < FP._0)
                throw new ArgumentOutOfRangeException("VisualTimeScale");
        }
    }

    public enum SimulationTraceSeverity : byte
    {
        Detail = 1,
        Information = 2,
        Warning = 3,
        Error = 4
    }

    public readonly struct SimulationValueTrace
    {
        readonly SimulationValueTraceStorage m_Storage;
        readonly int m_Index;
        internal SimulationValueTrace(SimulationValueTraceStorage storage, int index)
        { m_Storage = storage; m_Index = index; }
        public string PortId => m_Storage.Read(m_Index).PortId;
        public ProgramValuePortDirection Direction => m_Storage.Read(m_Index).Direction;
        public ref readonly AbilityStateValue Value => ref m_Storage.Read(m_Index).Value;
    }

    internal sealed class SimulationValueTraceStorage
    {
        internal readonly struct Entry
        {
            internal Entry(string portId, ProgramValuePortDirection direction, in AbilityStateValue value)
            { PortId = portId; Direction = direction; Value = value; }
            internal readonly string PortId;
            internal readonly ProgramValuePortDirection Direction;
            internal readonly AbilityStateValue Value;
        }
        Entry[] m_Values = Array.Empty<Entry>();
        int[] m_Counts = Array.Empty<int>();
        readonly int m_Invocations;
        int m_Stride;
#if UNITY_EDITOR
        internal long ObservationElapsedStopwatchTicks;
        internal int ObservationCalls;
#endif
        internal SimulationValueTraceStorage(int invocationCount) => m_Invocations = invocationCount;
        internal void PrepareCapacity(int traceRecordsPerTick)
        {
            m_Stride = checked(traceRecordsPerTick + 1);
            m_Values = new Entry[checked(m_Invocations * m_Stride)];
            m_Counts = new int[m_Invocations];
        }
        internal SimulationValueTrace Capture(int invocation, string portId,
            ProgramValuePortDirection direction, in AbilityStateValue value)
        {
            int index = checked(invocation * m_Stride + m_Counts[invocation]++);
            m_Values[index] = new Entry(portId, direction, in value);
            return new SimulationValueTrace(this, index);
        }
        internal ref readonly Entry Read(int index) => ref m_Values[index];
        internal void Clear()
        {
#if UNITY_EDITOR
            ObservationElapsedStopwatchTicks = 0;
            ObservationCalls = 0;
#endif
            for (int invocation = 0; invocation < m_Counts.Length; invocation++)
            {
                Array.Clear(m_Values, invocation * m_Stride, m_Counts[invocation]);
                m_Counts[invocation] = 0;
            }
        }
    }

    public readonly struct SimulationBlackboardTrace
    {
        public SimulationBlackboardTrace(int stateSlot, AbilityStateValue value, BlackboardOwnerToken owner, ProgramBlackboardLifetime lifetime)
        {
            StateSlot = stateSlot;
            Value = value;
            Owner = owner;
            Lifetime = lifetime;
        }

        public int StateSlot { get; }
        public AbilityStateValue Value { get; }
        public BlackboardOwnerToken Owner { get; }
        public ProgramBlackboardLifetime Lifetime { get; }
    }

    public readonly struct SimulationTraceRecord
    {
        internal const int MaximumGeneratedPathReferences = 3;
        public SimulationTraceRecord(
            in SimulationTraceHeader header,
            SimulationTraceSeverity severity,
            string boundary,
            string code,
            CanonicalTextValue detail,
            ulong actionInstanceId = 0,
            string skillId = "",
            ulong skillExecutionGeneration = 0,
            SimulationValueTrace? valueTrace = null,
            ProgramControlFlowEdge controlFlow = null,
            bool controlFlowSelected = false,
            bool controlFlowPassed = false,
            ulong graphInvocationGeneration = 0,
            ulong parentInvocationGeneration = 0,
            OperationHandle timelineOperation = default,
            ulong timelinePlaybackGeneration = 0,
            float timelineTime = 0f,
            int timelineCycle = 0,
            string actionId = "",
            ulong inputSequence = 0,
            SimulationActionResultKind actionResult = SimulationActionResultKind.None,
            int loopIteration = 0,
            SimulationBlackboardTrace? blackboardTrace = null,
            in SimulationMotionTraceDetail motionDetail = default,
            in GameplayAbilitySourceContext ruleSource = default,
            in CharacterControlTraceDetail controlDetail = default)
        {
            Header = header;
            Severity = severity;
            Boundary = SimulationIdentity.Require(boundary, nameof(boundary));
            Code = SimulationIdentity.Require(code, nameof(code));
            Detail = detail;
            ActionInstanceId = actionInstanceId;
            SkillId = skillId ?? string.Empty;
            SkillExecutionGeneration = skillExecutionGeneration;
            ValueTrace = valueTrace;
            ControlFlow = controlFlow;
            ControlFlowSelected = controlFlowSelected;
            ControlFlowPassed = controlFlowPassed;
            GraphInvocationGeneration = graphInvocationGeneration;
            ParentInvocationGeneration = parentInvocationGeneration;
            TimelineOperation = timelineOperation;
            TimelinePlaybackGeneration = timelinePlaybackGeneration;
            TimelineTime = timelineTime;
            TimelineCycle = timelineCycle;
            ActionId = actionId ?? string.Empty;
            InputSequence = inputSequence;
            ActionResult = actionResult;
            LoopIteration = loopIteration;
            BlackboardTrace = blackboardTrace;
            MotionDetail = motionDetail;
            RuleSource = ruleSource;
            ControlDetail = controlDetail;
        }
        public SimulationTraceHeader Header { get; }
        public SimulationTraceSeverity Severity { get; }
        public string Boundary { get; }
        public string Code { get; }
        public CanonicalTextValue Detail { get; }
        public ulong ActionInstanceId { get; }
        public string SkillId { get; }
        public ulong SkillExecutionGeneration { get; }
        public SimulationValueTrace? ValueTrace { get; }
        public ProgramControlFlowEdge ControlFlow { get; }
        public bool ControlFlowSelected { get; }
        public bool ControlFlowPassed { get; }
        public ulong GraphInvocationGeneration { get; }
        public ulong ParentInvocationGeneration { get; }
        public OperationHandle TimelineOperation { get; }
        public ulong TimelinePlaybackGeneration { get; }
        public float TimelineTime { get; }
        public int TimelineCycle { get; }
        public string ActionId { get; }
        public ulong InputSequence { get; }
        public SimulationActionResultKind ActionResult { get; }
        public int LoopIteration { get; }
        public SimulationBlackboardTrace? BlackboardTrace { get; }
        public readonly SimulationMotionTraceDetail MotionDetail;
        public readonly GameplayAbilitySourceContext RuleSource;
        public readonly CharacterControlTraceDetail ControlDetail;

        internal void Retain()
        {
            Header.Retain();
            Detail.Retain();
            if (ValueTrace.HasValue)
                ValueTrace.GetValueOrDefault().Value.Retain();
            if (BlackboardTrace.HasValue)
                BlackboardTrace.Value.Value.Retain();
        }

        internal void Release()
        {
            Header.Release();
            Detail.Release();
            if (ValueTrace.HasValue)
                ValueTrace.GetValueOrDefault().Value.Release();
            if (BlackboardTrace.HasValue)
                BlackboardTrace.Value.Value.Release();
        }
    }
}

