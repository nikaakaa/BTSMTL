using ThirdPersonSimulation;
using System;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation.Fixed
{
    [CanonicalContract]
    [CanonicalUnion(typeof(ProgramInputValue<FixedScalar, FixedVector2, FixedVector3, FixedYaw, SimulationActionTargetSnapshot>))]
    public readonly partial struct SimulationInputValue
    {
        public void Retain() => CanonicalRetain(in this);
        public void Release() => CanonicalRelease(in this);

        [CanonicalValidation]
        void RequireIdentity() => SimulationIdentity.Require(InputId, nameof(InputId));
    }

    [CanonicalContract]
    public readonly partial struct SimulationInputRequest
    {
        public SimulationInputRequest(CanonicalTextValue requestId, ulong sequence, ulong sourceTick, ulong expireSimulationTick, int priority)
        {
            RequestId = requestId;
            Sequence = sequence;
            SourceTick = sourceTick;
            ExpireSimulationTick = expireSimulationTick;
            Priority = priority;
            RequireValid();
        }

        public CanonicalTextValue RequestId { get; }
        public ulong Sequence { get; }
        public ulong SourceTick { get; }
        public ulong ExpireSimulationTick { get; }
        public int Priority { get; }

        [CanonicalValidation]
        void RequireValid()
        {
            SimulationIdentity.Require(RequestId, nameof(RequestId));
            if (Sequence == 0)
                throw new ArgumentOutOfRangeException(nameof(Sequence));
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
            SimulationInputHeader.CanonicalRetain(Header);
            SimulationInputValues.CanonicalRetain(CanonicalValues);
        }

        public void ReleaseText()
        {
            SimulationInputValues.CanonicalRelease(CanonicalValues);
            SimulationInputHeader.CanonicalRelease(Header);
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
                throw new InvalidOperationException("Fixed character input request predates the replay clock.");
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

    [CanonicalContract]
    public readonly partial struct SimulationActionTargetSnapshot : IEquatable<SimulationActionTargetSnapshot>
    {
        public SimulationActionTargetSnapshot(CanonicalTextValue targetId, FixedVector3 position, FixedYaw yaw)
        {
            TargetId = targetId;
            Position = position;
            Yaw = yaw;
        }

        public CanonicalTextValue TargetId { get; }
        public FixedVector3 Position { get; }
        public FixedYaw Yaw { get; }
        public bool HasTarget => !TargetId.IsEmpty;
        public void Retain() => CanonicalRetain(in this);
        public void Release() => CanonicalRelease(in this);
        public bool Equals(SimulationActionTargetSnapshot other) =>
            TargetId.Equals(other.TargetId) &&
            Position.Equals(other.Position) &&
            Yaw.Equals(other.Yaw);
        public static SimulationActionTargetSnapshot None => new SimulationActionTargetSnapshot(string.Empty, FixedVector3.Zero, FixedYaw.Zero);
    }

    public static class SimulationActionTargetSnapshotCodec
    {
        const uint Magic = 0x504E5354;
        const int Version = 2;

        public static byte[] Write(SimulationActionTargetSnapshot value)
        {
            using var writer = new CanonicalWriter();
            writer.WriteUInt32(Magic);
            writer.WriteInt32(Version);
            writer.WriteText(value.TargetId);
            writer.WriteVector3(value.Position);
            writer.WriteYaw(value.Yaw);
            return writer.ToArray();
        }

        public static SimulationActionTargetSnapshot Read(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return SimulationActionTargetSnapshot.None;
            var reader = new CanonicalReader(bytes);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("Action target snapshot header is invalid.");
            var value = new SimulationActionTargetSnapshot(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), reader.ReadVector3(), reader.ReadYaw());
            reader.RequireComplete();
            return value;
        }
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

    public readonly struct SimulationEventHeader
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
            if (!numericProfile.IsValid || !eventId.IsValid || !actorId.IsValid || !tick.IsValid || !activation.IsValid || sequence == 0)
                throw new ArgumentException("Simulation event header is incomplete.");
            NumericProfile = numericProfile;
            EventId = eventId;
            ActorId = actorId;
            Tick = tick;
            Activation = activation;
            Sequence = sequence;
            Channel = SimulationIdentity.Require(channel, nameof(channel));
            TreeClipInvocation = treeClipInvocation;
        }

        public SimulationNumericProfile NumericProfile { get; }
        public EventId EventId { get; }
        public ActorId ActorId { get; }
        public SimulationTick Tick { get; }
        public ActivationId Activation { get; }
        public ulong Sequence { get; }
        public string Channel { get; }
        public AbilityTreeClipInvocation? TreeClipInvocation { get; }
        public void Retain()
        {
            ActorId.Value.Retain();
            Activation.Source.Retain();
            if (TreeClipInvocation.HasValue)
            {
                AbilityTreeClipInvocation invocation = TreeClipInvocation.Value;
                invocation.ClipAuthoringId.Retain();
                invocation.TreeGraphId.Retain();
                invocation.TreeGraphRevision.Retain();
                invocation.NodeAuthoringId.Retain();
            }
        }
        public void Release()
        {
            ActorId.Value.Release();
            Activation.Source.Release();
            if (TreeClipInvocation.HasValue)
            {
                AbilityTreeClipInvocation invocation = TreeClipInvocation.Value;
                invocation.ClipAuthoringId.Release();
                invocation.TreeGraphId.Release();
                invocation.TreeGraphRevision.Release();
                invocation.NodeAuthoringId.Release();
            }
        }
    }

    public enum GameplayFactKind : byte
    {
        Action = 1,
        Effect = 2,
        Attribute = 3,
        Cue = 4,
        Motion = 5,
        State = 6,
        ActionWindow = 7,
        Tag = 8,
        Failure = 9,
        Reconciliation = 10,
        Equipment = 11
    }

    public readonly struct ActionFact
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
            if (actionInstanceId == 0 || predictionKey == 0 || inputSequence == 0)
                throw new ArgumentException("Action fact identity is incomplete.");
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
        }

        public ulong ActionInstanceId { get; }
        public ulong PredictionKey { get; }
        public ulong InputSequence { get; }
        public string ActionId { get; }
        public CharacterSkillId SkillId { get; }
        public SimulationActionLifecycleTransitionType TransitionType { get; }
        public SimulationActionPhase Phase { get; }
        public SimulationActionState State { get; }
        public CanonicalTextValue Reason { get; }
        public EquipmentActionContext EquipmentContext { get; }
        public bool IsValid => ActionInstanceId != 0 && PredictionKey != 0 && InputSequence != 0 && !CanonicalTextValue.IsNullOrEmpty(ActionId);
    }

    public readonly struct ActionWindowFact
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
            if (sourceContext.InstanceId == 0 || startTick == 0 || endTick < startTick)
                throw new ArgumentException("Action Window fact identity is incomplete.");
            SourceContext = sourceContext;
            ActionId = SimulationIdentity.Require(actionId, nameof(actionId));
            WindowId = SimulationIdentity.Require(windowId, nameof(windowId));
            WindowType = SimulationIdentity.Require(windowType, nameof(windowType));
            WindowTag = SimulationIdentity.Require(windowTag, nameof(windowTag));
            StartTick = startTick;
            EndTick = endTick;
            Digest = digest;
        }

        public GameplayAbilitySourceContext SourceContext { get; }
        public ulong ActionInstanceId => SourceContext.InstanceId;
        public string ActionId { get; }
        public string WindowId { get; }
        public string WindowType { get; }
        public string WindowTag { get; }
        public ulong StartTick { get; }
        public ulong EndTick { get; }
        public ulong Digest { get; }
        public bool IsValid => ActionInstanceId != 0 && StartTick != 0;
    }

    public readonly struct GameplayFact
    {
        public const int SchemaVersion = 4;

        public GameplayFact(SimulationEventHeader header, GameplayFactKind kind, CanonicalTextValue subjectId, CanonicalTextValue stateId, FixedScalar scalar) : this()
        {
            if (kind == GameplayFactKind.Action || kind == GameplayFactKind.ActionWindow ||
                kind == GameplayFactKind.Effect || kind == GameplayFactKind.Attribute || kind == GameplayFactKind.Cue ||
                kind == GameplayFactKind.Tag || kind == GameplayFactKind.Failure || kind == GameplayFactKind.Reconciliation ||
                kind == GameplayFactKind.Equipment)
                throw new ArgumentException($"'{kind}' facts require a typed payload.", nameof(kind));
            Header = header;
            Kind = kind;
            SubjectId = SimulationIdentity.Require(subjectId, nameof(subjectId));
            StateId = stateId;
            Scalar = scalar;
        }

        public GameplayFact(SimulationEventHeader header, ActionFact action) : this()
        {
            if (!action.IsValid)
                throw new ArgumentException("Action fact payload is invalid.", nameof(action));
            Header = header;
            Kind = GameplayFactKind.Action;
            SubjectId = action.ActionId;
            StateId = ActionTransitionName(action.TransitionType);
            Scalar = FixedScalar.Zero;
            Action = action;
        }

        public GameplayFact(SimulationEventHeader header, ActionWindowFact actionWindow) : this()
        {
            if (!actionWindow.IsValid)
                throw new ArgumentException("Action Window fact payload is invalid.", nameof(actionWindow));
            Header = header;
            Kind = GameplayFactKind.ActionWindow;
            SubjectId = actionWindow.WindowId;
            StateId = actionWindow.WindowType;
            Scalar = FixedScalar.Zero;
            ActionWindow = actionWindow;
        }

        public GameplayFact(SimulationEventHeader header, GameplayEffectFact effect, CanonicalTextValue cause = default,
            ulong mutationCursor = 0) : this()
        {
            if (!effect.IsValid)
                throw new ArgumentException("Gameplay Effect fact payload is invalid.", nameof(effect));
            Header = header;
            Kind = GameplayFactKind.Effect;
            SubjectId = effect.EffectId;
            StateId = LifecycleName(effect.Operation);
            Scalar = FixedScalar.Zero;
            Effect = effect;
            Cause = cause;
            MutationCursor = mutationCursor;
        }

        public GameplayFact(SimulationEventHeader header, GameplayAttributeFact attribute, CanonicalTextValue cause = default,
            ulong mutationCursor = 0) : this()
        {
            if (!attribute.IsValid)
                throw new ArgumentException("Gameplay Attribute fact payload is invalid.", nameof(attribute));
            Header = header;
            Kind = GameplayFactKind.Attribute;
            SubjectId = attribute.AttributeId;
            StateId = "Changed";
            Scalar = attribute.CurrentValue;
            Attribute = attribute;
            Cause = cause;
            MutationCursor = mutationCursor;
        }

        public GameplayFact(SimulationEventHeader header, GameplayCueFact cue, CanonicalTextValue cause = default,
            ulong mutationCursor = 0) : this()
        {
            if (!cue.IsValid)
                throw new ArgumentException("Gameplay Cue fact payload is invalid.", nameof(cue));
            Header = header;
            Kind = GameplayFactKind.Cue;
            SubjectId = cue.CueId;
            StateId = cue.TriggerId;
            Scalar = FixedScalar.Zero;
            Cue = cue;
            Cause = cause;
            MutationCursor = mutationCursor;
        }

        public GameplayFact(SimulationEventHeader header, GameplayTagRuntimeChange tag, CanonicalTextValue cause,
            ulong mutationCursor = 0) : this()
        {
            Header = header;
            Kind = GameplayFactKind.Tag;
            SubjectId = tag.TagId;
            StateId = "Changed";
            Tag = tag;
            Cause = cause;
            MutationCursor = mutationCursor;
        }

        public GameplayFact(SimulationEventHeader header, GameplayEffectExecutionFailure failure, CanonicalTextValue cause,
            ulong mutationCursor = 0) : this()
        {
            Header = header;
            Kind = GameplayFactKind.Failure;
            SubjectId = failure.RequestedEffectId;
            StateId = "Failed";
            Failure = failure;
            Cause = cause;
            MutationCursor = mutationCursor;
        }

        public GameplayFact(SimulationEventHeader header, GameplayEffectReconciliationResult reconciliation,
            SimulationGameplayEffectContext context, CanonicalTextValue cause, ulong mutationCursor = 0) : this()
        {
            Header = header;
            Kind = GameplayFactKind.Reconciliation;
            SubjectId = reconciliation.Scope.EffectId;
            StateId = "Reconciled";
            Reconciliation = reconciliation;
            EffectContext = context;
            Cause = cause;
            MutationCursor = mutationCursor;
        }
        public GameplayFact(SimulationEventHeader header, EquipmentLifecycleFact equipment) : this()
        {
            Header = header;
            Kind = GameplayFactKind.Equipment;
            SubjectId = equipment.SlotId.Value;
            StateId = equipment.StateName;
            Equipment = equipment;
        }

        public SimulationEventHeader Header { get; }
        public GameplayFactKind Kind { get; }
        public CanonicalTextValue SubjectId { get; }
        public CanonicalTextValue StateId { get; }
        public FixedScalar Scalar { get; }
        public ActionFact Action { get; }
        public ActionWindowFact ActionWindow { get; }
        public GameplayEffectFact Effect { get; }
        public GameplayAttributeFact Attribute { get; }
        public GameplayCueFact Cue { get; }
        public EquipmentLifecycleFact Equipment { get; }
        public GameplayTagRuntimeChange Tag { get; }
        public GameplayEffectExecutionFailure Failure { get; }
        public GameplayEffectReconciliationResult Reconciliation { get; }
        public SimulationGameplayEffectContext EffectContext { get; }
        public CanonicalTextValue Cause { get; }
        public ulong MutationCursor { get; }

        internal void Retain()
        {
            Header.Retain();
            SubjectId.Retain(); StateId.Retain(); Cause.Retain();
            Action.Reason.Retain();
            Effect.Context.Retain(); Effect.EffectId.Retain();
            Attribute.AttributeId.Retain(); Attribute.CauseEffectId.Retain(); Attribute.CauseContext.Retain();
            Cue.CueId.Retain(); Cue.TriggerId.Retain(); Cue.SourceId.Retain(); Cue.EffectContext.Retain();
            Equipment.SlotId.Value.Retain();
            Tag.Retain(); Failure.Retain(); Reconciliation.Retain(); EffectContext.Retain();
        }
        internal void Release()
        {
            Header.Release();
            SubjectId.Release(); StateId.Release(); Cause.Release();
            Action.Reason.Release();
            Effect.Context.Release(); Effect.EffectId.Release();
            Attribute.AttributeId.Release(); Attribute.CauseEffectId.Release(); Attribute.CauseContext.Release();
            Cue.CueId.Release(); Cue.TriggerId.Release(); Cue.SourceId.Release(); Cue.EffectContext.Release();
            Equipment.SlotId.Value.Release();
            Tag.Release(); Failure.Release(); Reconciliation.Release(); EffectContext.Release();
        }

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

    public readonly struct PresentationCommand
    {
        public PresentationCommand(
            SimulationEventHeader header,
            PresentationCommandKind kind,
            string producerId,
            FixedScalar sampleTime,
            FixedScalar weight,
            ulong producerGeneration = 0,
            int cycle = 0,
            ulong sourceActionInstanceId = 0,
            FixedScalar visualTimeScale = default,
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
            if ((kind == PresentationCommandKind.CombatHit) != (combatHit != null))
                throw new ArgumentException("Combat feedback commands require their accepted hit result.", nameof(combatHit));
            if ((kind == PresentationCommandKind.TimelineProgress) != timelineProgress.IsValid ||
                timelineProgress.IsValid && (sourceActionInstanceId == 0 || timelineProgress.LogicTick != header.Tick.Value))
                throw new ArgumentException("Timeline progress requires its exact logic tick and Action identity.", nameof(timelineProgress));
            if (RequiresProducerGeneration(kind) && producerGeneration == 0)
                throw new ArgumentOutOfRangeException(nameof(producerGeneration));
            if (RequiresProducerGeneration(kind) && producerGeneration != header.Activation.Generation)
                throw new ArgumentException("Presentation producer generation does not match the event activation.", nameof(producerGeneration));
            if (IsPlaybackCommand(kind) && sourceActionInstanceId == 0)
                throw new ArgumentOutOfRangeException(nameof(sourceActionInstanceId));
            if (cycle < 0)
                throw new ArgumentOutOfRangeException(nameof(cycle));
            if (kind == PresentationCommandKind.DomainEvent &&
                (sourceActionInstanceId == 0 || CanonicalTextValue.IsNullOrWhiteSpace(domainPayload)))
                throw new ArgumentException("Domain event command requires an Action instance and payload.", nameof(domainPayload));
            if (kind != PresentationCommandKind.DomainEvent && !CanonicalTextValue.IsNullOrEmpty(domainPayload))
                throw new ArgumentException("Domain payload is only valid on Domain event commands.", nameof(domainPayload));
            if (kind == PresentationCommandKind.Camera && !cameraRequest.IsValid)
                throw new ArgumentException("Camera commands require a typed camera request.", nameof(cameraRequest));
            if (kind != PresentationCommandKind.Camera && cameraRequest.IsValid)
                throw new ArgumentException("Typed camera requests are only valid on Camera commands.", nameof(cameraRequest));
            if (IsPlaybackSample(kind) && visualTimeScale < FixedScalar.Zero)
                throw new ArgumentOutOfRangeException(nameof(visualTimeScale));
        }
        public SimulationEventHeader Header { get; }
        public PresentationCommandKind Kind { get; }
        public string ProducerId { get; }
        public FixedScalar SampleTime { get; }
        public FixedScalar Weight { get; }
        public ulong ProducerGeneration { get; }
        public int Cycle { get; }
        public ulong SourceActionInstanceId { get; }
        public CanonicalTextValue DomainPayload { get; }
        public FixedScalar VisualTimeScale { get; }
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

        internal PresentationCommand WithCombatHit(CombatHitResult combatHit) => new PresentationCommand(in this, combatHit);

        internal void Retain()
        {
            Header.Retain(); DomainPayload.Retain(); CameraRequest.Retain(); TimelineProgress.Retain(); AudioTargetActor.Value.Retain();
        }
        internal void Release()
        {
            Header.Release(); DomainPayload.Release(); CameraRequest.Release(); TimelineProgress.Release(); AudioTargetActor.Value.Release();
        }

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
        internal const int MaximumTextReferences = 13;
        internal const int MaximumGeneratedPathReferences = 3;
        public SimulationTraceRecord(
            SimulationEventHeader header,
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
        public SimulationEventHeader Header { get; }
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

