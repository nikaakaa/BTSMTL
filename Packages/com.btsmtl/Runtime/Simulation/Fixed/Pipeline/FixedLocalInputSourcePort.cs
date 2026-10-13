using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    public enum FixedInputSequenceState : byte
    {
        Idle,
        Pending,
        Running,
        Completed,
        Cancelled,
        Rejected
    }

    public enum FixedInputSequenceFailure : byte
    {
        None,
        Busy,
        ReplaySource,
        UnknownAction,
        ValueKind,
        OverlappingAction
    }

    [CanonicalContract]
    public readonly partial struct FixedInputActionStep
    {
        public const int MaximumSteps = 64;
        public const int MaximumDurationTicks = 3600;
        public const int MaximumActionCharacters = 128;

        public FixedInputActionStep(CanonicalTextValue action, int offsetTicks, int durationTicks,
            ProgramInputValueKind kind, FixedScalar x, FixedScalar y)
        {
            Action = action;
            OffsetTicks = offsetTicks;
            DurationTicks = durationTicks;
            Kind = kind;
            X = x;
            Y = y;
        }

        public CanonicalTextValue Action { get; }
        public int OffsetTicks { get; }
        public int DurationTicks { get; }
        public ProgramInputValueKind Kind { get; }
        public FixedScalar X { get; }
        public FixedScalar Y { get; }

        [CanonicalValidation]
        void RequireRecord()
        {
            if (Action.Length == 0 || Action.Length > MaximumActionCharacters)
                throw new InvalidDataException("Rollback input action identity exceeds its declared capacity.");
        }
    }

    [CanonicalContract]
    public readonly partial struct FixedInputSequenceStatus
    {
        public FixedInputSequenceStatus(ulong commandId, FixedInputSequenceState state, ulong startTick,
            ulong lastInputTick, int totalTicks, FixedInputSequenceFailure failure = FixedInputSequenceFailure.None,
            int failedStep = -1)
        {
            CommandId = commandId;
            State = state;
            StartTick = startTick;
            LastInputTick = lastInputTick;
            TotalTicks = totalTicks;
            Failure = failure;
            FailedStep = failedStep;
        }

        public ulong CommandId { get; }
        public FixedInputSequenceState State { get; }
        public ulong StartTick { get; }
        public ulong LastInputTick { get; }
        public int TotalTicks { get; }
        public FixedInputSequenceFailure Failure { get; }
        public int FailedStep { get; }
    }

    public interface IFixedCharacterInputSequenceSource
    {
        int InputActionCount { get; }
        FixedInputActionStep DescribeInputAction(int index);
        FixedInputSequenceStatus InputSequenceStatus { get; }
        FixedInputSequenceStatus BeginInputSequence(ulong commandId, FixedInputActionStep[] steps, int count);
        FixedInputSequenceStatus CancelInputSequence();
    }

    public readonly struct FixedCharacterInputBuildContext
    {
        public FixedCharacterInputBuildContext(
            ActorId actorId,
            SimulationTick simulationTick,
            SimulationTickSourceIdentity source,
            ulong inputSequence,
            int tickRate,
            int offensiveRequestDelayTicks,
            int maximumPendingRequests,
            SimulationInputFrameStorage inputStorage)
            : this(
                actorId,
                simulationTick,
                source,
                inputSequence,
                tickRate,
                offensiveRequestDelayTicks,
                maximumPendingRequests,
                null,
                inputStorage)
        {
        }

        public FixedCharacterInputBuildContext(
            ActorId actorId,
            SimulationTick simulationTick,
            SimulationTickSourceIdentity source,
            ulong inputSequence,
            int tickRate,
            int offensiveRequestDelayTicks,
            int maximumPendingRequests,
            CommittedActorPoseSnapshot<FixedVector3, FixedYaw> committedObservation,
            SimulationInputFrameStorage inputStorage)
        {
            if (!actorId.IsValid || !simulationTick.IsValid || CanonicalTextValue.IsNullOrEmpty(source.ClockId) ||
                source.Kind != SimulationTickSourceKind.LocalLogic ||
                inputSequence == 0 || tickRate <= 0 || offensiveRequestDelayTicks < 0 || maximumPendingRequests <= 0)
            {
                throw new ArgumentException("Fixed character input build context is incomplete.");
            }
            ActorId = actorId;
            SimulationTick = simulationTick;
            Source = source;
            InputSequence = inputSequence;
            TickRate = tickRate;
            OffensiveRequestDelayTicks = offensiveRequestDelayTicks;
            MaximumPendingRequests = maximumPendingRequests;
            InputStorage = inputStorage;
            CommittedObservation = committedObservation;
        }

        public ActorId ActorId { get; }
        public SimulationTick SimulationTick { get; }
        public SimulationTickSourceIdentity Source { get; }
        public ulong InputSequence { get; }
        public int TickRate { get; }
        public int OffensiveRequestDelayTicks { get; }
        public int MaximumPendingRequests { get; }
        public SimulationInputFrameStorage InputStorage { get; }
        public CommittedActorPoseSnapshot<FixedVector3, FixedYaw> CommittedObservation { get; }
    }

    public enum FixedCharacterControlSourceStateDisposition : byte
    {
        Prepared = 1,
        Committed = 2,
        Discarded = 3,
        Restored = 4
    }

    public readonly struct FixedCharacterControlSourceDiagnosticsSnapshot
    {
        public FixedCharacterControlSourceDiagnosticsSnapshot(
            int pendingOffensiveRequestCount,
            ulong oldestCaptureTick,
            ulong oldestEligibleTick)
        {
            if (pendingOffensiveRequestCount < 0)
                throw new ArgumentOutOfRangeException(nameof(pendingOffensiveRequestCount));
            PendingOffensiveRequestCount = pendingOffensiveRequestCount;
            OldestCaptureTick = oldestCaptureTick;
            OldestEligibleTick = oldestEligibleTick;
        }

        public int PendingOffensiveRequestCount { get; }
        public ulong OldestCaptureTick { get; }
        public ulong OldestEligibleTick { get; }
    }

    public interface IFixedCharacterControlSourceRuntime
    {
        string SourceIdentity { get; }
        SimulationInput BuildInput(FixedCharacterInputBuildContext context);
        int InputValueCapacity { get; }
        IReadOnlyList<SimulationInputValue> InputValues { get; }
        int StateTextCapacity { get; }
        int PrepareStateStorage(int maximumPendingRequests);
        void WriteState(CanonicalWriter writer);
        void RestoreState(ArraySegment<byte> state, ICanonicalTextResolver resolver);
        void NotifyStateDisposition(FixedCharacterControlSourceStateDisposition disposition);
        FixedCharacterControlSourceDiagnosticsSnapshot CaptureDiagnostics();
    }

    public interface IFixedCharacterInputFrameConsumer
    {
        void ReceiveInput(SimulationInput input);
    }

    public sealed class FixedLocalSimulationInputBinding
    {
        public FixedLocalSimulationInputBinding(ActorId actorId, IFixedCharacterControlSourceRuntime controlSource)
        {
            if (!actorId.IsValid)
                throw new ArgumentException("Fixed Local input binding ActorId is invalid.", nameof(actorId));
            ActorId = actorId;
            ControlSource = controlSource ?? throw new ArgumentNullException(nameof(controlSource));
        }

        public ActorId ActorId { get; }
        public IFixedCharacterControlSourceRuntime ControlSource { get; }
    }

    public static class FixedLocalInputSourcePortContract
    {
        public const string PortId = "simulation.source.fixed-local-actor-input";
        public const string SchemaId = "fixed-local-actor-input-source";
        public const int SchemaVersion = 6;
        internal const string StateSchema = SimulationInputValue.CanonicalSchema + ":" + AbilityInputEvent.CanonicalSchema;
        internal static readonly StableHash StateSchemaHash =
            new StableHash(CanonicalContractProtocol.ComposeSchema(StateSchema));

        public static SimulationPipelinePortRequirement Requirement =>
            new SimulationPipelinePortRequirement(
                SimulationPipelineBindingPortRole.Source,
                PortId,
                SchemaId,
                SchemaVersion,
                SimulationPortDirection.Input);
    }

    public readonly struct FixedLocalInputFrame
    {
        public FixedLocalInputFrame(FixedCanonicalInputBatch canonicalInputs, FixedTypedIngressBatch typedIngress)
        {
            if (!canonicalInputs.IsValid)
                throw new ArgumentException("Canonical input batch is incomplete.", nameof(canonicalInputs));
            if (!typedIngress.IsValid)
                throw new ArgumentException("Fixed typed ingress batch is invalid.", nameof(typedIngress));
            TypedIngress = typedIngress;
            CanonicalInputs = canonicalInputs;
        }

        public FixedCanonicalInputBatch CanonicalInputs { get; }
        public FixedTypedIngressBatch TypedIngress { get; }
    }

    public interface IFixedAbilityInputReadPort
    {
        ulong ReadInputSequence(ActorId actorId);
        SimulationInputValue ReadInputValue(ActorId actorId, CanonicalTextValue inputId, ProgramInputValueKind kind);
        bool HasActionEvent(ActorId actorId, ulong instanceId, string eventId, CanonicalTextValue targetId);
        bool HasGameplayEvent(ActorId actorId, string eventId);
    }

    public interface IFixedAbilityFrameEventCapturePort
    {
        void CaptureFrameEvents(ActorId actorId, ReadOnlySpan<SimulationIngress> ingress);
    }

    internal interface IFixedCommittedInputReadPort : IFixedAbilityInputReadPort, IFixedAbilityFrameEventCapturePort
    {
        void PrepareAvailabilityRead(int maximumPayloadBytes, IReadOnlyList<SimulationActorBinding> roster);
        void RestoreControlInput(ArraySegment<byte> bytes);
        void RestoreControlSources(ArraySegment<byte> bytes);
    }

    public interface IFixedSessionIngressWritePort
    {
        FixedValuePortInputBank ValuePorts { get; }
        ulong QueueRuntimeIngress(ActorId actor, SimulationActionEventIngress actionEvent, SimulationAttributeValueIngress attribute);
        bool TryTakeRuntimeIngressResult(ulong sequence, out ulong tick);
    }

    sealed class FixedQueuedIngressBuffer : IReadOnlyList<SimulationPipelineTypedIngress<SimulationIngress>>
    {
        readonly struct Pending
        {
            internal Pending(ActorId actor, ulong sequence, SimulationActionEventIngress actionEvent, SimulationAttributeValueIngress attribute)
            {
                Actor = actor;
                Sequence = sequence;
                ActionEvent = actionEvent;
                Attribute = attribute;
                Hash = StableHash.Compute("RuntimeValue/" + actor.Value + "/" + sequence);
                FactIdentity = Hash.Value;
                Actor.Retain();
                Attribute.AttributeId.Retain();
                Attribute.CauseEffectId.Retain();
                Attribute.CauseContext.Retain();
            }
            internal readonly ActorId Actor;
            internal readonly ulong Sequence;
            internal readonly StableHash Hash;
            internal readonly string FactIdentity;
            internal readonly SimulationActionEventIngress ActionEvent;
            internal readonly SimulationAttributeValueIngress Attribute;
            internal SimulationPipelineTypedIngress<SimulationIngress> Bind(SimulationTickSourceIdentity source)
            {
                var kind = ActionEvent.IsValid ? SimulationIngressKind.ActionEvent : SimulationIngressKind.AttributeValue;
                var header = new SimulationIngressHeader(FixedSimulationNumericProfile.Value, Actor, source.SourceTick, Sequence, Hash, kind);
                var value = kind == SimulationIngressKind.ActionEvent ? new SimulationIngress(header, ActionEvent)
                    : new SimulationIngress(header, Attribute);
                return new SimulationPipelineTypedIngress<SimulationIngress>(Actor, source, Sequence, FactIdentity, value);
            }
            internal void Release()
            {
                Attribute.CauseContext.Release();
                Attribute.CauseEffectId.Release();
                Attribute.AttributeId.Release();
                Actor.Release();
            }
        }

        static readonly Comparison<Pending> s_Order = (left, right) =>
        {
            int actor = left.Actor.CompareTo(right.Actor);
            return actor != 0 ? actor : left.Sequence.CompareTo(right.Sequence);
        };
        readonly List<Pending> m_Pending = new();
        readonly List<(ulong Sequence, ulong Tick)> m_Results = new();
        SimulationPipelineTypedIngress<SimulationIngress>[] m_Bound = Array.Empty<SimulationPipelineTypedIngress<SimulationIngress>>();
        int[] m_Positions = Array.Empty<int>();
        IReadOnlyList<SimulationPipelineTypedIngress<SimulationIngress>> m_External = Array.Empty<SimulationPipelineTypedIngress<SimulationIngress>>();
        int m_PreparedCount;
        ulong m_Sequence;
        ulong m_Tick;

        internal ulong Queue(ActorId actor, SimulationActionEventIngress actionEvent, SimulationAttributeValueIngress attribute)
        {
            ulong sequence = ++m_Sequence;
            m_Pending.Add(new Pending(actor, sequence, actionEvent, attribute));
            m_Pending.Sort(s_Order);
            if (m_Bound.Length < m_Pending.Count)
            {
                Array.Resize(ref m_Bound, m_Pending.Capacity);
                Array.Resize(ref m_Positions, m_Pending.Capacity);
            }
            m_Results.Capacity = Math.Max(m_Results.Capacity, m_Results.Count + m_Pending.Count);
            return sequence;
        }

        internal void Bind(SimulationTickSourceIdentity source, SimulationTick tick, FixedTypedIngressBatch external)
        {
            m_External = external.Ingress;
            m_PreparedCount = m_Pending.Count;
            m_Tick = tick.Value;
            for (int i = 0; i < m_PreparedCount; i++)
            {
                var value = m_Pending[i].Bind(source);
                m_Bound[i] = value;
                int low = 0, high = m_External.Count;
                while (low < high)
                {
                    int middle = (low + high) / 2;
                    var other = m_External[middle];
                    int order = value.ActorId.CompareTo(other.ActorId);
                    if (order == 0) order = value.Source.SourceTick.CompareTo(other.Source.SourceTick);
                    if (order == 0) order = value.Sequence.CompareTo(other.Sequence);
                    if (order == 0) order = string.CompareOrdinal(value.FactIdentity, other.FactIdentity);
                    if (order > 0) low = middle + 1;
                    else high = middle;
                }
                m_Positions[i] = low + i;
            }
        }

        public int Count => m_External.Count + m_PreparedCount;
        public SimulationPipelineTypedIngress<SimulationIngress> this[int index]
        {
            get
            {
                int low = 0, high = m_PreparedCount;
                while (low < high)
                {
                    int middle = (low + high) / 2;
                    if (m_Positions[middle] < index) low = middle + 1;
                    else high = middle;
                }
                return low < m_PreparedCount && m_Positions[low] == index
                    ? m_Bound[low] : m_External[index - low];
            }
        }
        public IEnumerator<SimulationPipelineTypedIngress<SimulationIngress>> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return this[i];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        internal void Commit()
        {
            for (int i = 0; i < m_PreparedCount; i++)
            {
                Pending value = m_Pending[i];
                m_Results.Add((value.Sequence, m_Tick));
                value.Release();
                m_Bound[i] = default;
            }
            m_Pending.RemoveRange(0, m_PreparedCount);
            m_PreparedCount = 0;
        }

        internal bool TryTakeResult(ulong sequence, out ulong tick)
        {
            for (int i = 0; i < m_Results.Count; i++)
                if (m_Results[i].Sequence == sequence)
                {
                    tick = m_Results[i].Tick;
                    m_Results.RemoveAt(i);
                    return true;
                }
            tick = 0;
            return false;
        }

        internal void Release()
        {
            foreach (Pending value in m_Pending) value.Release();
            m_Pending.Clear();
            m_Results.Clear();
            Array.Clear(m_Bound, 0, m_Bound.Length);
            m_PreparedCount = 0;
        }
    }

    public interface IFixedLocalInputSourcePort : ISimulationRuntimePort, IFixedSessionIngressWritePort
    {
        IReadOnlyList<FixedLocalEnemyBinding> LocalEnemies { get; }
        void PrepareInputStorage(int frameCapacity);
        void ReleaseInputFrames();
        FixedLocalInputFrame Read(
            SimulationTickSourceIdentity source,
            SimulationTick simulationTick,
            int tickRate,
            IReadOnlyList<SimulationActorBinding> roster,
            CommittedActorPoseSnapshot<FixedVector3, FixedYaw> committedObservation);
        int StatePayloadCapacity { get; }
        int StateTextCapacity { get; }
        void PrepareAvailabilityRead(int maximumPayloadBytes, IReadOnlyList<SimulationActorBinding> roster);
        ulong ReadInputSequence(ActorId actorId);
        SimulationInputValue ReadInputValue(ActorId actorId, CanonicalTextValue inputId, ProgramInputValueKind kind);
        void CaptureFrameEvents(ActorId actorId, ReadOnlySpan<SimulationIngress> ingress);
        bool HasActionEvent(ActorId actorId, ulong instanceId, string eventId, CanonicalTextValue targetId);
        bool HasGameplayEvent(ActorId actorId, string eventId);

        void ReleaseRuntimeIngress();
        void ReleaseAvailabilityInputs();
        void WriteState(CanonicalWriter writer);
        void RestoreState(ArraySegment<byte> state, ICanonicalTextResolver resolver);
        void RestoreControlSources(ArraySegment<byte> state, ICanonicalTextResolver resolver);
        void NotifyStateDisposition(FixedCharacterControlSourceStateDisposition disposition);
    }

    public sealed class FixedLocalInputSourcePort : IFixedLocalInputSourcePort
    {
        public FixedValuePortInputBank ValuePorts { get; } = new();
        readonly FixedQueuedIngressBuffer m_RuntimeIngress = new();
        readonly ReadOnlyCollection<FixedLocalSimulationInputBinding> m_Bindings;
        readonly SimulationPipelineActorInput<FixedStepInput>[] m_InputScratch;
        SimulationInputFrameBank[] m_InputFrames;
        readonly FixedLocalEnemyInput[] m_EnemyInputScratch;
        readonly WorldKinematicBodySample[] m_KinematicBodyScratch;
        readonly int m_OffensiveRequestDelayTicks;
        readonly int m_MaximumPendingRequests;
        ulong m_LastReadSourceTick;
        ExecutionWorkspaceBuffer<SimulationInputValue>[] m_CurrentInputValues = Array.Empty<ExecutionWorkspaceBuffer<SimulationInputValue>>();
        CanonicalTextValue[][] m_DeclaredInputIds;
        AbilityInputEvent[][] m_CurrentEvents;
        int[] m_CurrentEventCounts;


        public FixedLocalInputSourcePort(
            SimulationComponentIdentity sessionSource,
            IEnumerable<FixedLocalSimulationInputBinding> bindings,
            int offensiveRequestDelayTicks,
            int maximumPendingRequests,
            IReadOnlyList<FixedLocalEnemyBinding> localEnemies = null)
        {
            if (!sessionSource.IsValid || sessionSource.Role != SimulationComponentRole.SessionSource)
                throw new ArgumentException("Fixed Local Session Source identity is invalid.", nameof(sessionSource));
            if (offensiveRequestDelayTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(offensiveRequestDelayTicks));
            if (maximumPendingRequests <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumPendingRequests));
            var values = bindings == null
                ? new List<FixedLocalSimulationInputBinding>()
                : new List<FixedLocalSimulationInputBinding>(bindings);
            values.Sort((left, right) => left.ActorId.CompareTo(right.ActorId));
            if (values.Count == 0)
                throw new ArgumentException("Fixed Local input Source requires an Actor roster.", nameof(bindings));
            var identity = new string[values.Count + 4];
            identity[0] = "fixed-local-input-source-port/3:" + FixedLocalInputSourcePortContract.StateSchema;
            identity[1] = sessionSource.ToString();
            identity[2] = offensiveRequestDelayTicks.ToString();
            identity[3] = maximumPendingRequests.ToString();
            for (int i = 0; i < values.Count; i++)
            {
                FixedLocalSimulationInputBinding binding = values[i] ??
                    throw new ArgumentException("Fixed Local input Source contains a missing binding.", nameof(bindings));
                if (i > 0 && values[i - 1].ActorId.Equals(binding.ActorId))
                    throw new ArgumentException("Fixed Local input Source contains duplicate ActorId.", nameof(bindings));
                identity[i + 4] = $"{binding.ActorId}:{binding.ControlSource.SourceIdentity}";
            }
            m_Bindings = values.AsReadOnly();
            m_InputScratch = new SimulationPipelineActorInput<FixedStepInput>[values.Count];
            LocalEnemies = localEnemies ?? Array.Empty<FixedLocalEnemyBinding>();
            m_EnemyInputScratch = new FixedLocalEnemyInput[LocalEnemies.Count];
            m_KinematicBodyScratch = new WorldKinematicBodySample[LocalEnemies.Count];
            m_OffensiveRequestDelayTicks = offensiveRequestDelayTicks;
            m_MaximumPendingRequests = maximumPendingRequests;
            int payloadCapacity = 20 + sizeof(int) + 64;
            for (int i = 0; i < values.Count; i++)
            {
                FixedLocalSimulationInputBinding binding = values[i];
                payloadCapacity = checked(payloadCapacity + 12 +
                    CanonicalTextEncoding.ByteCount(binding.ActorId.Value.Span) +
                    CanonicalTextEncoding.ByteCount(binding.ControlSource.SourceIdentity.AsSpan()) +
                    binding.ControlSource.PrepareStateStorage(maximumPendingRequests));
            }
            StatePayloadCapacity = payloadCapacity;
            int textCapacity = checked(values.Count * 2);
            for (int i = 0; i < values.Count; i++)
                textCapacity = checked(textCapacity + values[i].ControlSource.StateTextCapacity);
            StateTextCapacity = textCapacity;
            Descriptor = new SimulationPortDescriptor(
                FixedLocalInputSourcePortContract.PortId,
                FixedLocalInputSourcePortContract.SchemaId,
                FixedLocalInputSourcePortContract.SchemaVersion,
                SimulationPortDirection.Input,
                sessionSource.ComponentId,
                StableHash.Compute(identity));
        }

        public void PrepareAvailabilityRead(int maximumPayloadBytes, IReadOnlyList<SimulationActorBinding> roster)
        {
            if (maximumPayloadBytes < StatePayloadCapacity)
                throw new InvalidOperationException("Local input state exceeds the Session checkpoint payload declaration.");
            StatePayloadCapacity = maximumPayloadBytes;
            StateTextCapacity += ValuePorts.TextCapacity;
            m_CurrentInputValues = new ExecutionWorkspaceBuffer<SimulationInputValue>[m_Bindings.Count];
            m_DeclaredInputIds = new CanonicalTextValue[m_Bindings.Count][];
            m_CurrentEvents = new AbilityInputEvent[m_Bindings.Count][];
            m_CurrentEventCounts = new int[m_Bindings.Count];
            for (int actor = 0; actor < m_Bindings.Count; actor++)
            {
                var source = m_Bindings[actor].ControlSource;
                int ingress = roster[actor].AvailabilityIngressCapacity;
                m_CurrentEvents[actor] = new AbilityInputEvent[ingress];
                StateTextCapacity = checked(StateTextCapacity + AbilityInputEvent.CanonicalMaximumTextReferences * ingress);
                m_CurrentInputValues[actor] = new ExecutionWorkspaceBuffer<SimulationInputValue>(source.InputValueCapacity);
                var ids = new List<CanonicalTextValue>(source.InputValues.Count + 7);
                for (int i = 0; i < source.InputValues.Count; i++) ids.Add(source.InputValues[i].InputId);
                ids.Add(CameraProgramOperationSchema.BasisValidInputId);
                ids.Add(CameraProgramOperationSchema.BasisPlanarForwardInputId);
                ids.Add(CameraProgramOperationSchema.BasisPlanarRightInputId);
                ids.Add(CameraProgramOperationSchema.BasisLookDirectionInputId);
                ids.Add(CameraProgramOperationSchema.BasisAimPointInputId);
                ids.Add(CameraProgramOperationSchema.BasisYawInputId);
                ids.Add(CameraProgramOperationSchema.BasisPitchInputId);
                m_DeclaredInputIds[actor] = ids.ToArray();
                StateTextCapacity = checked(StateTextCapacity + SimulationInputValue.CanonicalMaximumTextReferences * source.InputValueCapacity);
            }
        }

        int RequireInputActor(ActorId actorId)
        {
            for (int actor = 0; actor < m_Bindings.Count; actor++)
                if (m_Bindings[actor].ActorId == actorId) return actor;
            throw new InvalidOperationException("Availability input actor is absent from the Local source roster.");
        }
        public ulong ReadInputSequence(ActorId actorId)
        {
            RequireInputActor(actorId);
            return m_LastReadSourceTick;
        }
        public SimulationInputValue ReadInputValue(ActorId actorId, CanonicalTextValue inputId, ProgramInputValueKind kind)
        {
            var values = m_CurrentInputValues[RequireInputActor(actorId)];
            for (int i = 0; i < values.Count; i++)
                if (inputId.Equals(values[i].InputId))
                {
                    if (values[i].Kind != kind)
                        throw new InvalidOperationException("Availability input kind differs from its declared value.");
                    return values[i];
                }
            throw new InvalidOperationException("Availability input has no retained value for the requested input identity.");
        }
        void ClearInputValues(int actor)
        {
            var values = m_CurrentInputValues[actor];
            for (int i = 0; i < values.Count; i++) values[i].Release();
            values.Clear();
        }
        public void ReleaseAvailabilityInputs()
        {
            for (int actor = 0; actor < m_CurrentInputValues.Length; actor++) { ClearInputValues(actor); ClearFrameEvents(actor); }
        }
        void CaptureInputValues(int actor, IReadOnlyList<SimulationInputValue> input)
        {
            ClearInputValues(actor);
            var values = m_CurrentInputValues[actor];
            if (input.Count > values.Capacity)
                throw new InvalidOperationException("Retained input exceeds its source value capacity.");
            for (int i = 0; i < input.Count; i++)
            {
                input[i].Retain();
                values.Add(input[i]);
            }
        }
        void ClearFrameEvents(int actor)
        {
            for (int i = 0; i < m_CurrentEventCounts[actor]; i++)
                AbilityInputEvent.CanonicalRelease(in m_CurrentEvents[actor][i]);
            Array.Clear(m_CurrentEvents[actor], 0, m_CurrentEventCounts[actor]);
            m_CurrentEventCounts[actor] = 0;
        }
        public void CaptureFrameEvents(ActorId actorId, ReadOnlySpan<SimulationIngress> ingress)
        {
            int actor = RequireInputActor(actorId);
            ClearFrameEvents(actor);
            for (int i = 0; i < ingress.Length; i++)
            {
                if (!FixedAbilityFrameEventSemantics.TryProject(actorId, in ingress[i], out AbilityInputEvent frozen))
                    continue;
                AbilityInputEvent.CanonicalRetain(in frozen);
                m_CurrentEvents[actor][m_CurrentEventCounts[actor]++] = frozen;
            }
        }
        public bool HasActionEvent(ActorId actorId, ulong instanceId, string eventId, CanonicalTextValue targetId)
        {
            int actor = RequireInputActor(actorId);
            return FixedAbilityFrameEventSemantics.HasActionEvent(
                new ReadOnlySpan<AbilityInputEvent>(m_CurrentEvents[actor], 0, m_CurrentEventCounts[actor]),
                actorId, instanceId, eventId, targetId);
        }
        public bool HasGameplayEvent(ActorId actorId, string eventId)
        {
            int actor = RequireInputActor(actorId);
            return FixedAbilityFrameEventSemantics.HasGameplayEvent(
                new ReadOnlySpan<AbilityInputEvent>(m_CurrentEvents[actor], 0, m_CurrentEventCounts[actor]), actorId, eventId);
        }

        void WriteRetainedEvents(CanonicalWriter writer)
        {
            for (int actor = 0; actor < m_Bindings.Count; actor++)
            {
                writer.WriteInt32(m_CurrentEventCounts[actor]);
                for (int i = 0; i < m_CurrentEventCounts[actor]; i++)
                {
                    ref readonly AbilityInputEvent value = ref m_CurrentEvents[actor][i];
                    AbilityInputEvent.CanonicalWrite(writer, in value);
                }
            }
        }
        void ReadRetainedEvents(CanonicalReader reader)
        {
            for (int actor = 0; actor < m_Bindings.Count; actor++)
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > m_CurrentEvents[actor].Length) throw new System.IO.InvalidDataException("Retained frame events exceed the published ingress geometry.");
                ClearFrameEvents(actor);
                for (int i = 0; i < count; i++)
                {
                    ref AbilityInputEvent value = ref m_CurrentEvents[actor][m_CurrentEventCounts[actor]];
                    AbilityInputEvent.CanonicalReadInto(reader, ref value);
                    AbilityInputEvent.CanonicalRetain(in value);
                    m_CurrentEventCounts[actor]++;
                }
            }
        }

        void WriteRetainedInputs(CanonicalWriter writer)
        {
            writer.WriteInt32(m_Bindings.Count);
            for (int actor = 0; actor < m_Bindings.Count; actor++)
            {
                var values = m_CurrentInputValues[actor];
                writer.WriteInt32(values.Count);
                for (int i = 0; i < values.Count; i++) SimulationInputValue.CanonicalWrite(writer, values[i]);
            }
        }
        void ReadRetainedInputs(CanonicalReader reader)
        {
            int actors = reader.ReadInt32();
            if (actors != m_Bindings.Count) throw new System.IO.InvalidDataException("Retained input roster differs from the Local source roster.");
            for (int actor = 0; actor < actors; actor++)
            {
                int count = reader.ReadInt32();
                var values = m_CurrentInputValues[actor];
                if (count < 0 || count > values.Capacity) throw new System.IO.InvalidDataException("Retained input value count exceeds the declared source shape.");
                ClearInputValues(actor);
                for (int i = 0; i < count; i++)
                {
                    CanonicalTextValue inputId = ReadDeclaredInputId(reader, actor);
                    SimulationInputValue value = SimulationInputValue.CanonicalReadPayload(reader, inputId);
                    value.Retain();
                    values.Add(value);
                }
            }
        }
        CanonicalTextValue ReadDeclaredInputId(CanonicalReader reader, int actor)
        {
            var encoded = reader.ReadTextSegment();
            foreach (CanonicalTextValue id in m_DeclaredInputIds[actor])
                if (CanonicalTextEncoding.EqualsEncoded(encoded.AsSpan(), id.Span)) return id;
            throw new System.IO.InvalidDataException("Retained Fixed input is absent from the published source input directory.");
        }

        public SimulationPortDescriptor Descriptor { get; }
        public int StatePayloadCapacity { get; private set; }
        public int StateTextCapacity { get; private set; }
        public IReadOnlyList<FixedLocalEnemyBinding> LocalEnemies { get; }

        public ulong QueueRuntimeIngress(ActorId actor, SimulationActionEventIngress actionEvent, SimulationAttributeValueIngress attribute) =>
            m_RuntimeIngress.Queue(actor, actionEvent, attribute);
        public bool TryTakeRuntimeIngressResult(ulong sequence, out ulong tick) => m_RuntimeIngress.TryTakeResult(sequence, out tick);
        public void ReleaseRuntimeIngress()
        {
            m_RuntimeIngress.Release();
            ValuePorts.Clear();
        }

        public void PrepareInputStorage(int frameCapacity)
        {
            m_InputFrames = new SimulationInputFrameBank[m_Bindings.Count];
            for (int index = 0; index < m_Bindings.Count; index++)
                m_InputFrames[index] = new SimulationInputFrameBank(
                    m_Bindings[index].ControlSource.InputValueCapacity, m_MaximumPendingRequests, frameCapacity);
        }

        public void ReleaseInputFrames()
        {
            for (int index = 0; index < m_InputFrames.Length; index++)
                m_InputFrames[index].ReleaseFrames();
        }

        public FixedLocalInputFrame Read(
            SimulationTickSourceIdentity source,
            SimulationTick simulationTick,
            int tickRate,
            IReadOnlyList<SimulationActorBinding> roster,
            CommittedActorPoseSnapshot<FixedVector3, FixedYaw> committedObservation)
        {
            if (source.Kind != SimulationTickSourceKind.LocalLogic || source.SourceTick <= m_LastReadSourceTick)
                throw new InvalidOperationException("Fixed Local input Source requires a new LocalLogic source Tick.");
            if (!simulationTick.IsValid || tickRate <= 0 ||
                roster == null || roster.Count != m_Bindings.Count)
            {
                throw new InvalidOperationException("Fixed Local input Source request does not match its locked Actor roster.");
            }
            if (committedObservation == null || committedObservation.ObservationTick != simulationTick.Value - 1)
                throw new InvalidOperationException("Fixed Local input Source requires the committed observation immediately preceding the requested simulation Tick.");
            if (committedObservation.Actors.Count != roster.Count + LocalEnemies.Count)
                throw new InvalidOperationException("Fixed Local input Source committed observation does not match its locked Actor roster.");
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i] == null || !committedObservation.TryGetActor(roster[i].ActorId, out _))
                    throw new InvalidOperationException("Fixed Local input Source committed observation Actor identities do not match its locked roster.");
            }
            ValuePorts.BeginTick(simulationTick.Value);
            SimulationPipelineActorInput<FixedStepInput>[] inputs = m_InputScratch;
            bool prepared = false;
            try
            {
                for (int i = 0; i < roster.Count; i++)
                {
                    SimulationActorBinding actor = roster[i] ??
                        throw new InvalidOperationException("Fixed Local input Source roster contains a missing Actor binding.");
                    FixedLocalSimulationInputBinding binding = m_Bindings[i];
                    IFixedCharacterControlSourceRuntime controlSource = binding.ControlSource;
                    if (!actor.ActorId.Equals(binding.ActorId))
                    {
                        throw new InvalidOperationException($"Fixed Local input binding for Actor '{actor.ActorId}' is incompatible.");
                    }
                    var context = new FixedCharacterInputBuildContext(
                        actor.ActorId,
                        simulationTick,
                        source,
                        source.SourceTick,
                        tickRate,
                        m_OffensiveRequestDelayTicks,
                        m_MaximumPendingRequests,
                        committedObservation,
                        m_InputFrames[i].BeginFrame());
                    SimulationInput input = FixedCharacterInputTraceModule.IsSessionHistoryReplaying
                        ? default : controlSource.BuildInput(context);
                    input = FixedCharacterInputTraceModule.Resolve(context, input);
                    if (!input.IsValid || input.NumericProfile != FixedSimulationNumericProfile.Value ||
                        !input.TickSource.Equals(source) || input.Sequence != source.SourceTick)
                    {
                        throw new InvalidOperationException($"Fixed Control Source '{controlSource.SourceIdentity}' returned input outside the Local Source contract.");
                    }
                    CaptureInputValues(i, input.Values);
                    if (controlSource is IFixedCharacterInputFrameConsumer consumer)
                        consumer.ReceiveInput(input);
                    inputs[i] = new SimulationPipelineActorInput<FixedStepInput>(
                        actor.ActorId,
                        source.SourceTick,
                        new FixedStepInput(input));
                }
                for (int i = 0; i < LocalEnemies.Count; i++)
                {
                    m_EnemyInputScratch[i] = LocalEnemies[i].Source.FreezeInput(simulationTick.Value);
                    FixedLocalEnemyDefinition definition = LocalEnemies[i].Definition;
                    FixedLocalEnemyInput frozen = m_EnemyInputScratch[i];
                    var offset = new FixedVector3(definition.ReceiverCenter.X,
                        definition.ReceiverCenter.Y - definition.HalfLength - definition.Radius,
                        definition.ReceiverCenter.Z);
                    var before = FixedCombatGeometryPreparation.Pose(frozen.BeforePosition, frozen.BeforeYaw, offset).Position;
                    var after = FixedCombatGeometryPreparation.Pose(frozen.Position, frozen.Yaw, offset).Position;
                    m_KinematicBodyScratch[i] = new WorldKinematicBodySample(definition.ActorId,
                        new FixedVector3(before.X, before.Y, before.Z), new FixedVector3(after.X, after.Y, after.Z));
                }
                FixedCanonicalInputBatch batch = new FixedCanonicalInputBatch(source, inputs, m_EnemyInputScratch, m_KinematicBodyScratch);
                m_LastReadSourceTick = source.SourceTick;
                prepared = true;
                m_RuntimeIngress.Bind(source, simulationTick, FixedCharacterInputTraceModule.ResolveIngress(source, simulationTick));
                return new FixedLocalInputFrame(batch, new FixedTypedIngressBatch(m_RuntimeIngress));
            }
            finally
            {
                if (!prepared)
                {
                    for (int i = 0; i < inputs.Length; i++)
                        inputs[i] = default;
                }
            }
        }

        public void WriteState(CanonicalWriter writer)
        {
            writer.WriteUInt32(0x53434c58);
            writer.WriteInt32(7);
            writer.WriteHash(FixedLocalInputSourcePortContract.StateSchemaHash);
            writer.WriteUInt64(m_LastReadSourceTick);
            writer.WriteInt32(m_Bindings.Count);
            for (int i = 0; i < m_Bindings.Count; i++)
            {
                FixedLocalSimulationInputBinding binding = m_Bindings[i];
                writer.WriteText(binding.ActorId.Value);
                writer.WriteText(binding.ControlSource.SourceIdentity);
                long prefix = writer.BeginLengthPrefixedBlock();
                binding.ControlSource.WriteState(writer);
                writer.EndLengthPrefixedBlock(prefix);
            }
            WriteRetainedInputs(writer);
            WriteRetainedEvents(writer);
            ValuePorts.Write(writer);
        }

        readonly CanonicalReader m_StateReader = new CanonicalReader(Array.Empty<byte>());

        public void RestoreState(ArraySegment<byte> state, ICanonicalTextResolver resolver)
        {
            CanonicalReader reader = m_StateReader;
            reader.Reset(state);
            reader.SetTextResolver(resolver);
            ulong lastReadSourceTick = RestoreControlSourceBlocks(reader, resolver);
            ReadRetainedInputs(reader);
            ReadRetainedEvents(reader);
            ValuePorts.Read(reader);
            reader.RequireComplete();
            m_LastReadSourceTick = lastReadSourceTick;
        }

        public void RestoreControlSources(ArraySegment<byte> state, ICanonicalTextResolver resolver)
        {
            CanonicalReader reader = m_StateReader;
            reader.Reset(state);
            reader.SetTextResolver(resolver);
            RestoreControlSourceBlocks(reader, resolver);
            for (int i = 0; i < m_Bindings.Count; i++)
                m_Bindings[i].ControlSource.NotifyStateDisposition(FixedCharacterControlSourceStateDisposition.Restored);
        }

        ulong RestoreControlSourceBlocks(CanonicalReader reader, ICanonicalTextResolver resolver)
        {
            if (reader.ReadUInt32() != 0x53434c58 || reader.ReadInt32() != 7 ||
                reader.ReadHash() != FixedLocalInputSourcePortContract.StateSchemaHash)
                throw new InvalidDataException("Fixed Local Control Source state header is invalid.");
            ulong lastReadSourceTick = reader.ReadUInt64();
            int count = reader.ReadInt32();
            if (count != m_Bindings.Count)
                throw new InvalidDataException("Fixed Local Control Source state roster does not match its locked bindings.");
            for (int i = 0; i < count; i++)
            {
                FixedLocalSimulationInputBinding binding = m_Bindings[i];
                reader.RequireText(binding.ActorId.Value);
                reader.RequireText(binding.ControlSource.SourceIdentity);
                ArraySegment<byte> payload = reader.ReadBytesSegment();
                binding.ControlSource.RestoreState(payload, resolver);
            }
            return lastReadSourceTick;
        }

        public void NotifyStateDisposition(FixedCharacterControlSourceStateDisposition disposition)
        {
            if (disposition is not (FixedCharacterControlSourceStateDisposition.Prepared or FixedCharacterControlSourceStateDisposition.Committed or
                FixedCharacterControlSourceStateDisposition.Discarded or FixedCharacterControlSourceStateDisposition.Restored))
                throw new ArgumentOutOfRangeException(nameof(disposition));
            for (int i = 0; i < m_Bindings.Count; i++)
                m_Bindings[i].ControlSource.NotifyStateDisposition(disposition);
            FixedCharacterInputTraceModule.NotifyIngressDisposition(disposition);
            if (disposition == FixedCharacterControlSourceStateDisposition.Committed)
                m_RuntimeIngress.Commit();
        }
    }
}
