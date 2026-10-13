using System;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation
{
    [CanonicalContract]
    [CanonicalUnion(typeof(ProgramInputValue<Float32Scalar, Float32Vector2, Float32Vector3, Float32Yaw, SimulationActionTargetSnapshot>))]
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

    public sealed class SimulationInput
    {
        readonly Float32SimulationInputFrameStorage m_Storage;
        internal SimulationInput(Float32SimulationInputFrameStorage storage) => m_Storage = storage;
        internal SimulationInputHeader Header => m_Storage.Header;
        internal SimulationInputValues CanonicalValues => m_Storage.CanonicalValues;

        public SimulationInput(
            SimulationNumericProfile numericProfile,
            SimulationTickSourceIdentity tickSource,
            CanonicalTextValue inputSourceIdentity,
            ulong sequence,
            IEnumerable<SimulationInputValue> values,
            IEnumerable<SimulationInputRequest> requests)
        {
            if (!numericProfile.IsValid || sequence == 0)
                throw new ArgumentOutOfRangeException(nameof(sequence));
            m_Storage = new Float32SimulationInputFrameStorage(numericProfile, tickSource,
                SimulationIdentity.Require(inputSourceIdentity, nameof(inputSourceIdentity)), sequence,
                SortValues(values), SortRequests(requests));
        }

        public static SimulationInput FromOwnedArrays(
            SimulationNumericProfile numericProfile,
            SimulationTickSourceIdentity tickSource,
            CanonicalTextValue inputSourceIdentity,
            ulong sequence,
            SimulationInputValue[] values,
            SimulationInputRequest[] requests)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (requests == null)
                throw new ArgumentNullException(nameof(requests));
            ValidateValues(values);
            ValidateRequests(requests);
            return new SimulationInput(
                numericProfile,
                tickSource,
                inputSourceIdentity,
                sequence,
                values,
                requests);
        }

        public SimulationNumericProfile NumericProfile => m_Storage.NumericProfile;
        public SimulationTickSourceIdentity TickSource => m_Storage.TickSource;
        public CanonicalTextValue InputSourceIdentity => m_Storage.InputSourceIdentity;
        public ulong Sequence => m_Storage.Sequence;
        public IReadOnlyList<SimulationInputValue> Values => m_Storage.Values;
        public ReadOnlySpan<SimulationInputValue> ValuesSpan => m_Storage.CanonicalValues.Values.Span;
        public IReadOnlyList<SimulationInputRequest> Requests => m_Storage.Requests;
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
        static SimulationInputValue[] SortValues(IEnumerable<SimulationInputValue> values)
        {
            SimulationInputValue[] result = Copy(values);
            Array.Sort(result, s_ValueComparer);
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
            Array.Sort(result, s_RequestComparer);
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

        static void ValidateValues(SimulationInputValue[] values)
        {
            for (int i = 1; i < values.Length; i++)
            {
                if (CanonicalTextValue.CompareOrdinal(values[i - 1].InputId, values[i].InputId) >= 0)
                    throw new ArgumentException("Simulation input owned values must be sorted by unique InputId.", nameof(values));
            }
        }

        static void ValidateRequests(SimulationInputRequest[] requests)
        {
            for (int i = 1; i < requests.Length; i++)
            {
                int bySequence = requests[i - 1].Sequence.CompareTo(requests[i].Sequence);
                if (bySequence > 0 || (bySequence == 0 && CanonicalTextValue.CompareOrdinal(
                        requests[i - 1].RequestId,
                        requests[i].RequestId) >= 0))
                {
                    throw new ArgumentException("Simulation input owned requests must be sorted by unique Sequence and RequestId.", nameof(requests));
                }
            }
        }

        SimulationInput(
            SimulationNumericProfile numericProfile,
            SimulationTickSourceIdentity tickSource,
            CanonicalTextValue inputSourceIdentity,
            ulong sequence,
            SimulationInputValue[] values,
            SimulationInputRequest[] requests)
        {
            if (!numericProfile.IsValid || sequence == 0)
                throw new ArgumentOutOfRangeException(nameof(sequence));
            m_Storage = new Float32SimulationInputFrameStorage(numericProfile, tickSource,
                SimulationIdentity.Require(inputSourceIdentity, nameof(inputSourceIdentity)), sequence, values, requests);
        }

        static readonly SimulationInputValueComparer s_ValueComparer = new();
        static readonly SimulationInputRequestComparer s_RequestComparer = new();

        sealed class SimulationInputValueComparer : IComparer<SimulationInputValue>
        {
            public int Compare(SimulationInputValue left, SimulationInputValue right) =>
                CanonicalTextValue.CompareOrdinal(left.InputId, right.InputId);
        }

        sealed class SimulationInputRequestComparer : IComparer<SimulationInputRequest>
        {
            public int Compare(SimulationInputRequest left, SimulationInputRequest right)
            {
                int bySequence = left.Sequence.CompareTo(right.Sequence);
                return bySequence != 0 ? bySequence : CanonicalTextValue.CompareOrdinal(left.RequestId, right.RequestId);
            }
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
        public SimulationActionTargetSnapshot(CanonicalTextValue targetId, Float32Vector3 position, Float32Yaw yaw)
        {
            TargetId = targetId.IsNull ? CanonicalTextValue.Empty : targetId;
            Position = position;
            Yaw = yaw;
        }

        public CanonicalTextValue TargetId { get; }
        public Float32Vector3 Position { get; }
        public Float32Yaw Yaw { get; }
        public bool HasTarget => !CanonicalTextValue.IsNullOrEmpty(TargetId);
        public void Retain() => CanonicalRetain(in this);
        public void Release() => CanonicalRelease(in this);
        public bool Equals(SimulationActionTargetSnapshot other) =>
            CanonicalTextValue.Equals(TargetId, other.TargetId, StringComparison.Ordinal) &&
            Position.Equals(other.Position) &&
            Yaw.Equals(other.Yaw);
        public static SimulationActionTargetSnapshot None => new SimulationActionTargetSnapshot(string.Empty, Float32Vector3.Zero, Float32Yaw.Zero);
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
            Reason = reason.IsNull ? CanonicalTextValue.Empty : reason;
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
            : Header.Kind == SimulationIngressKind.ActionEvent ? 0 : 1;
        public SimulationActionLifecycleIngress ActionLifecycle { get; }
        public SimulationGameplayResultIngress GameplayResult { get; }
        public SimulationGameplayEffectLifecycleIngress GameplayEffectLifecycle { get; }
        public SimulationAttributeValueIngress AttributeValue { get; }
    }

    [CanonicalContract]
    public readonly partial struct SimulationEventHeader
    {
        public SimulationEventHeader(
            SimulationNumericProfile numericProfile,
            EventId eventId,
            ActorId actorId,
            SimulationTick tick,
            ActivationId activation,
            ulong sequence,
            CanonicalTextValue channel,
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

        [CanonicalPublished]
        public SimulationNumericProfile NumericProfile { get; }
        public EventId EventId { get; }
        public ActorId ActorId { get; }
        public SimulationTick Tick { get; }
        public ActivationId Activation { get; }
        public ulong Sequence { get; }
        [CanonicalTextRules(Required = true, Trim = true)]
        public CanonicalTextValue Channel { get; }
        public AbilityTreeClipInvocation? TreeClipInvocation { get; }
        public void Retain() => CanonicalRetain(in this);

        public void Release() => CanonicalRelease(in this);

        [CanonicalValidation]
        void RequireRecord()
        {
            if (!NumericProfile.IsValid || !EventId.IsValid || !ActorId.IsValid || !Tick.IsValid || !Activation.IsValid || Sequence == 0)
                throw new ArgumentException("Simulation event header is incomplete.");
        }
    }

    [CanonicalUnionTemplate]
    public enum GameplayFactKind : byte
    {
        [CanonicalUnionCase(typeof(ActionFact))]
        Action = 1,
        [CanonicalUnionCase(typeof(GameplayEffectFact))]
        Effect = 2,
        [CanonicalUnionCase(typeof(GameplayAttributeFact))]
        Attribute = 3,
        [CanonicalUnionCase(typeof(GameplayCueFact))]
        Cue = 4,
        [CanonicalUnionCase(typeof(GameplayStateFact<Float32Scalar>))]
        Motion = 5,
        [CanonicalUnionCase(typeof(GameplayStateFact<Float32Scalar>))]
        State = 6,
        [CanonicalUnionCase(typeof(ActionWindowFact))]
        ActionWindow = 7,
        [CanonicalUnionCase(typeof(GameplayTagRuntimeChange))]
        Tag = 8,
        [CanonicalUnionCase(typeof(GameplayEffectExecutionFailure))]
        Failure = 9,
        [CanonicalUnionCase(typeof(GameplayEffectReconciliationFact<SimulationGameplayEffectContext>))]
        Reconciliation = 10,
        [CanonicalUnionCase(typeof(EquipmentLifecycleFact))]
        Equipment = 11
    }

    [CanonicalContract]
    public readonly partial struct ActionFact
    {
        public ActionFact(
            ulong actionInstanceId,
			ulong predictionKey,
			ulong inputSequence,
			CanonicalTextValue actionId,
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
            Reason = reason.IsNull ? CanonicalTextValue.Empty : reason;
            EquipmentContext = equipmentContext;
            RequireRecord();
        }

        public ulong ActionInstanceId { get; }
        public ulong PredictionKey { get; }
        public ulong InputSequence { get; }
        [CanonicalTextRules(Required = true, Trim = true)]
        public CanonicalTextValue ActionId { get; }
        public CharacterSkillId SkillId { get; }
        public SimulationActionLifecycleTransitionType TransitionType { get; }
        public SimulationActionPhase Phase { get; }
        public SimulationActionState State { get; }
        public CanonicalTextValue Reason { get; }
        public EquipmentActionContext EquipmentContext { get; }
        public bool IsValid => ActionInstanceId != 0 && PredictionKey != 0 && InputSequence != 0 && !CanonicalTextValue.IsNullOrEmpty(ActionId);

        [CanonicalValidation]
        void RequireRecord()
        {
            if (ActionInstanceId == 0 || PredictionKey == 0 || InputSequence == 0)
                throw new ArgumentException("Action fact identity is incomplete.");
        }
    }

    [CanonicalContract]
    public readonly partial struct ActionWindowFact
    {
        public ActionWindowFact(
            GameplayAbilitySourceContext sourceContext,
            CanonicalTextValue actionId,
            CanonicalTextValue windowId,
            CanonicalTextValue windowType,
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
        [CanonicalTextRules(Required = true, Trim = true)]
        public CanonicalTextValue ActionId { get; }
        [CanonicalTextRules(Required = true, Trim = true)]
        public CanonicalTextValue WindowId { get; }
        [CanonicalTextRules(Required = true, Trim = true)]
        public CanonicalTextValue WindowType { get; }
        [CanonicalTextRules(Required = true, Trim = true)]
        [CanonicalDeclaredText]
        public string WindowTag { get; }
        public ulong StartTick { get; }
        public ulong EndTick { get; }
        public ulong Digest { get; }
        public bool IsValid => ActionInstanceId != 0 && StartTick != 0;
        public void Retain() => CanonicalRetain(in this);
        public void Release() => CanonicalRelease(in this);

        [CanonicalValidation]
        void RequireRecord()
        {
            if (SourceContext.InstanceId == 0 || StartTick == 0 || EndTick < StartTick)
                throw new ArgumentException("Action Window fact identity is incomplete.");
        }
    }

    [CanonicalContract]
    [CanonicalUnion(typeof(GameplayFactKind))]
    public readonly partial struct GameplayFact
    {
        public const int SchemaVersion = 6;

        public GameplayFact(SimulationEventHeader header, GameplayFactKind kind, CanonicalTextValue subjectId, CanonicalTextValue stateId, Float32Scalar scalar) {
            var value = new GameplayStateFact<Float32Scalar>(subjectId, stateId, scalar);
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
        public Float32Scalar Scalar => Kind switch
        {
            GameplayFactKind.Attribute => Attribute.CurrentValue,
            GameplayFactKind.Motion => Motion.Scalar,
            GameplayFactKind.State => State.Scalar,
            _ => default
        };
        public SimulationGameplayEffectContext EffectContext => Reconciliation.Context;
        public CanonicalTextValue Cause { get; }
        public ulong MutationCursor { get; }

        static CanonicalTextValue ActionTransitionName(SimulationActionLifecycleTransitionType transition) => transition switch
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

        static CanonicalTextValue LifecycleName(SimulationGameplayEffectLifecycleOperation operation) => operation switch
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
            internal static readonly int MaximumTextReferences = CanonicalMaximumTextReferences;
        public void Retain() => CanonicalRetain(in this);

        public void Release() => CanonicalRelease(in this);
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
        Audio = 13
    }

    [CanonicalContract]
    public readonly partial struct PresentationCommand
    {
        public PresentationCommand(
            SimulationEventHeader header,
            PresentationCommandKind kind,
            CanonicalTextValue producerId,
            Float32Scalar sampleTime,
            Float32Scalar weight,
            ulong producerGeneration = 0,
            int cycle = 0,
            ulong sourceActionInstanceId = 0,
            Float32Scalar visualTimeScale = default,
            CanonicalTextValue domainPayload = default,
            PresentationCameraRequest cameraRequest = default,
            AbilityTimelineProgress timelineProgress = default,
            PresentationAudioRequest audioRequest = default,
            AudioPlaybackOwner audioOwner = default,
            ActorId audioTargetActor = default)
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
            DomainPayload = domainPayload.IsNull ? CanonicalTextValue.Empty : domainPayload;
            CameraRequest = cameraRequest;
            TimelineProgress = timelineProgress;
            AudioRequest = audioRequest;
            AudioOwner = audioOwner;
            AudioTargetActor = audioTargetActor;
            RequireRecord();
        }
        public SimulationEventHeader Header { get; }
        public PresentationCommandKind Kind { get; }
        [CanonicalTextRules(Required = true, Trim = true)]
        public CanonicalTextValue ProducerId { get; }
        public Float32Scalar SampleTime { get; }
        public Float32Scalar Weight { get; }
        public ulong ProducerGeneration { get; }
        public int Cycle { get; }
        public ulong SourceActionInstanceId { get; }
        public CanonicalTextValue DomainPayload { get; }
        public Float32Scalar VisualTimeScale { get; }
        public PresentationCameraRequest CameraRequest { get; }
        public AbilityTimelineProgress TimelineProgress { get; }
        public PresentationAudioRequest AudioRequest { get; }
        public AudioPlaybackOwner AudioOwner { get; }
        public ActorId AudioTargetActor { get; }

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
            internal static readonly int MaximumTextReferences = CanonicalMaximumTextReferences;
        public void Retain() => CanonicalRetain(in this);

        public void Release() => CanonicalRelease(in this);

        [CanonicalValidation]
        void RequireRecord()
        {
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
            if (IsPlaybackSample(Kind) && VisualTimeScale < Float32Scalar.Zero)
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
        public CanonicalTextValue PortId => m_Storage.Read(m_Index).PortId;
        public ProgramValuePortDirection Direction => m_Storage.Read(m_Index).Direction;
        public ref readonly AbilityStateValue Value => ref m_Storage.Read(m_Index).Value;
    }

    internal sealed class SimulationValueTraceStorage
    {
        internal readonly struct Entry
        {
            internal Entry(CanonicalTextValue portId, ProgramValuePortDirection direction, in AbilityStateValue value)
            { PortId = portId; Direction = direction; Value = value; }
            internal readonly CanonicalTextValue PortId;
            internal readonly ProgramValuePortDirection Direction;
            internal readonly AbilityStateValue Value;
        }
        Entry[] m_Values = Array.Empty<Entry>();
        int[] m_Counts = Array.Empty<int>();
        readonly int m_Invocations;
        int m_Stride;
        internal SimulationValueTraceStorage(int invocationCount) => m_Invocations = invocationCount;
        internal void PrepareCapacity(int traceRecordsPerTick)
        {
            m_Stride = checked(traceRecordsPerTick + 1);
            m_Values = new Entry[checked(m_Invocations * m_Stride)];
            m_Counts = new int[m_Invocations];
        }
        internal SimulationValueTrace Capture(int invocation, CanonicalTextValue portId,
            ProgramValuePortDirection direction, in AbilityStateValue value)
        {
            int index = checked(invocation * m_Stride + m_Counts[invocation]++);
            m_Values[index] = new Entry(portId, direction, in value);
            return new SimulationValueTrace(this, index);
        }
        internal ref readonly Entry Read(int index) => ref m_Values[index];
        internal void Clear()
        {
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
        internal const int MaximumTextReferences = 23;
        internal const int MaximumGeneratedPathReferences = 3;
        public SimulationTraceRecord(
            in SimulationTraceHeader header,
            SimulationTraceSeverity severity,
            CanonicalTextValue boundary,
            CanonicalTextValue code,
            CanonicalTextValue detail,
            ulong actionInstanceId = 0,
            CanonicalTextValue skillId = default,
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
            CanonicalTextValue actionId = default,
            ulong inputSequence = 0,
            SimulationActionResultKind actionResult = SimulationActionResultKind.None,
            int loopIteration = 0,
            SimulationBlackboardTrace? blackboardTrace = null,
            in GameplayAbilitySourceContext ruleSource = default,
            SimulationStructuredTrace structured = default,
            in CharacterControlTraceDetail controlDetail = default)
        {
            Header = header;
            Severity = severity;
            Boundary = SimulationIdentity.Require(boundary, nameof(boundary));
            Code = SimulationIdentity.Require(code, nameof(code));
            Detail = detail.IsNull ? CanonicalTextValue.Empty : detail;
            ActionInstanceId = actionInstanceId;
            SkillId = skillId.IsNull ? CanonicalTextValue.Empty : skillId;
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
            ActionId = actionId.IsNull ? CanonicalTextValue.Empty : actionId;
            InputSequence = inputSequence;
            ActionResult = actionResult;
            LoopIteration = loopIteration;
            BlackboardTrace = blackboardTrace;
            RuleSource = ruleSource;
            ControlDetail = controlDetail;
            Structured = structured;
        }
        public SimulationTraceHeader Header { get; }
        public SimulationTraceSeverity Severity { get; }
        public CanonicalTextValue Boundary { get; }
        public CanonicalTextValue Code { get; }
        public CanonicalTextValue Detail { get; }
        public ulong ActionInstanceId { get; }
        public CanonicalTextValue SkillId { get; }
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
        public CanonicalTextValue ActionId { get; }
        public ulong InputSequence { get; }
        public SimulationActionResultKind ActionResult { get; }
        public int LoopIteration { get; }
        public SimulationBlackboardTrace? BlackboardTrace { get; }
        public readonly GameplayAbilitySourceContext RuleSource;
        public readonly CharacterControlTraceDetail ControlDetail;
        public readonly SimulationStructuredTrace Structured;

        internal void Retain()
        {
            Header.Retain();
            Structured.Retain();
            Boundary.Retain(); Code.Retain(); Detail.Retain(); SkillId.Retain(); ActionId.Retain();
            if (ValueTrace.HasValue)
            {
                ValueTrace.Value.PortId.Retain();
                ValueTrace.Value.Value.Retain();
            }
            if (BlackboardTrace.HasValue)
                BlackboardTrace.Value.Value.Retain();
        }

        internal void Release()
        {
            if (BlackboardTrace.HasValue)
                BlackboardTrace.Value.Value.Release();
            if (ValueTrace.HasValue)
            {
                ValueTrace.Value.Value.Release();
                ValueTrace.Value.PortId.Release();
            }
            Structured.Release();
            ActionId.Release(); SkillId.Release(); Detail.Release(); Code.Release(); Boundary.Release();
            Header.Release();
        }
    }
}
