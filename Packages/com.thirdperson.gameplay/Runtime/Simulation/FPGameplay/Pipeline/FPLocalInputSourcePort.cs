using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    public enum FPInputSequenceState : byte
    {
        Idle,
        Pending,
        Running,
        Completed,
        Cancelled,
        Rejected
    }

    public enum FPInputSequenceFailure : byte
    {
        None,
        Busy,
        ReplaySource,
        UnknownAction,
        ValueKind,
        OverlappingAction
    }

    [ValueOperations]
    public readonly partial struct FPInputActionStep
    {
        public const int MaximumSteps = 64;
        public const int MaximumDurationTicks = 3600;
        public const int MaximumActionCharacters = 128;

        public FPInputActionStep(CanonicalTextValue action, int offsetTicks, int durationTicks,
            ProgramInputValueKind kind, FP x, FP y)
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
        public FP X { get; }
        public FP Y { get; }
    }

    [ValueOperations]
    public readonly partial struct FPInputSequenceStatus
    {
        public FPInputSequenceStatus(ulong commandId, FPInputSequenceState state, ulong startTick,
            ulong lastInputTick, int totalTicks, FPInputSequenceFailure failure = FPInputSequenceFailure.None,
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
        public FPInputSequenceState State { get; }
        public ulong StartTick { get; }
        public ulong LastInputTick { get; }
        public int TotalTicks { get; }
        public FPInputSequenceFailure Failure { get; }
        public int FailedStep { get; }
    }

    public interface IFPCharacterInputSequenceSource
    {
        int InputActionCount { get; }
        FPInputActionStep DescribeInputAction(int index);
        FPInputSequenceStatus InputSequenceStatus { get; }
        FPInputSequenceStatus BeginInputSequence(ulong commandId, FPInputActionStep[] steps, int count);
        FPInputSequenceStatus CancelInputSequence();
    }

    public readonly struct FPCharacterInputBuildContext
    {
        public FPCharacterInputBuildContext(
            ActorId actorId,
            SimulationTick simulationTick,
            SimulationTickSourceIdentity source,
            ulong inputSequence,
            int tickRate,
            SimulationInputFrameStorage inputStorage)
            : this(
                actorId,
                simulationTick,
                source,
                inputSequence,
                tickRate,
                null,
                inputStorage)
        {
        }

        public FPCharacterInputBuildContext(
            ActorId actorId,
            SimulationTick simulationTick,
            SimulationTickSourceIdentity source,
            ulong inputSequence,
            int tickRate,
            CommittedActorPoseSnapshot<FPVector3, FP> committedObservation,
            SimulationInputFrameStorage inputStorage)
        {
            if (!actorId.IsValid || !simulationTick.IsValid || CanonicalTextValue.IsNullOrEmpty(source.ClockId) ||
                source.Kind != SimulationTickSourceKind.LocalLogic ||
                inputSequence == 0 || tickRate <= 0)
            {
                throw new ArgumentException("FP character input build context is incomplete.");
            }
            ActorId = actorId;
            SimulationTick = simulationTick;
            Source = source;
            InputSequence = inputSequence;
            TickRate = tickRate;
            InputStorage = inputStorage;
            CommittedObservation = committedObservation;
        }

        public ActorId ActorId { get; }
        public SimulationTick SimulationTick { get; }
        public SimulationTickSourceIdentity Source { get; }
        public ulong InputSequence { get; }
        public int TickRate { get; }
        public SimulationInputFrameStorage InputStorage { get; }
        public CommittedActorPoseSnapshot<FPVector3, FP> CommittedObservation { get; }
    }

    public enum FPCharacterControlSourceStateDisposition : byte
    {
        Prepared = 1,
        Committed = 2,
        Discarded = 3,
        Restored = 4
    }

    public interface IFPCharacterControlSourceRuntime
    {
        string SourceIdentity { get; }
        SimulationInput BuildInput(FPCharacterInputBuildContext context);
        int InputValueCapacity { get; }
        IReadOnlyList<SimulationInputValue> InputValues { get; }
    }

    public interface IFPCharacterControlSourceInputPreparation
    {
        void PrepareInputState(int maximumPendingRequests);
    }

    public interface IFPCharacterControlSourceTransactionObserver
    {
        void NotifyStateDisposition(FPCharacterControlSourceStateDisposition disposition);
    }

    public interface IFPCharacterInputFrameConsumer
    {
        void ReceiveInput(SimulationInput input);
    }

    public sealed class FPLocalSimulationInputBinding
    {
        public FPLocalSimulationInputBinding(ActorId actorId, IFPCharacterControlSourceRuntime controlSource)
        {
            if (!actorId.IsValid)
                throw new ArgumentException("FP Local input binding ActorId is invalid.", nameof(actorId));
            ActorId = actorId;
            ControlSource = controlSource ?? throw new ArgumentNullException(nameof(controlSource));
        }

        public ActorId ActorId { get; }
        public IFPCharacterControlSourceRuntime ControlSource { get; }
    }

    public static class FPLocalInputSourcePortContract
    {
        public const string PortId = "simulation.source.fp-local-actor-input";
        public const string SchemaId = "fp-local-actor-input-source";
        public const int SchemaVersion = 7;

        public static SimulationPipelinePortRequirement Requirement =>
            new SimulationPipelinePortRequirement(
                SimulationPipelineBindingPortRole.Source,
                PortId,
                SchemaId,
                SchemaVersion,
                SimulationPortDirection.Input);
    }

    public readonly struct FPLocalInputFrame
    {
        public FPLocalInputFrame(FPCanonicalInputBatch canonicalInputs, FPTypedIngressBatch typedIngress)
        {
            if (!canonicalInputs.IsValid)
                throw new ArgumentException("Canonical input batch is incomplete.", nameof(canonicalInputs));
            if (!typedIngress.IsValid)
                throw new ArgumentException("FP typed ingress batch is invalid.", nameof(typedIngress));
            TypedIngress = typedIngress;
            CanonicalInputs = canonicalInputs;
        }

        public FPCanonicalInputBatch CanonicalInputs { get; }
        public FPTypedIngressBatch TypedIngress { get; }
    }

    public interface IFPAbilityInputReadPort
    {
        ulong ReadInputSequence(ActorId actorId);
        SimulationInputValue ReadInputValue(ActorId actorId, CanonicalTextValue inputId, ProgramInputValueKind kind);
        bool HasActionEvent(ActorId actorId, ulong instanceId, string eventId, CanonicalTextValue targetId);
        bool HasGameplayEvent(ActorId actorId, string eventId);
    }

    public interface IFPAbilityFrameEventCapturePort
    {
        void CaptureFrameEvents(ActorId actorId, ReadOnlySpan<SimulationIngress> ingress);
    }

    internal interface IFPCommittedInputReadPort : IFPAbilityInputReadPort, IFPAbilityFrameEventCapturePort
    {
        void PrepareAvailabilityRead(IReadOnlyList<SimulationActorBinding> roster, int inputFrameCapacity);
        void RestoreControlInput(IValueResources state);
        void RestoreControlSources(IValueResources state);
    }

    public interface IFPSessionIngressWritePort
    {
        FPValuePortInputBank ValuePorts { get; }
        ulong QueueRuntimeIngress(ActorId actor, SimulationActionEventIngress actionEvent, SimulationAttributeValueIngress attribute);
        bool TryTakeRuntimeIngressResult(ulong sequence, out ulong tick);
    }

    sealed class FPQueuedIngressBuffer : IReadOnlyList<SimulationPipelineTypedIngress<SimulationIngress>>
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
                var header = new SimulationIngressHeader(QuantumSimulationNumericProfile.Value, Actor, source.SourceTick, Sequence, Hash, kind);
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

        internal void Bind(SimulationTickSourceIdentity source, SimulationTick tick, FPTypedIngressBatch external)
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

    public interface IFPLocalInputSourcePort : ISimulationRuntimePort, IFPSessionIngressWritePort, IValueSnapshotSource
    {
        IReadOnlyList<FPLocalEnemyBinding> LocalEnemies { get; }
        void PrepareInputStorage(int frameCapacity);
        void ReleaseInputFrames();
        FPLocalInputFrame Read(
            SimulationTickSourceIdentity source,
            SimulationTick simulationTick,
            int tickRate,
            IReadOnlyList<SimulationActorBinding> roster,
            CommittedActorPoseSnapshot<FPVector3, FP> committedObservation);
        void PrepareAvailabilityRead(IReadOnlyList<SimulationActorBinding> roster);
        ulong ReadInputSequence(ActorId actorId);
        SimulationInputValue ReadInputValue(ActorId actorId, CanonicalTextValue inputId, ProgramInputValueKind kind);
        void CaptureFrameEvents(ActorId actorId, ReadOnlySpan<SimulationIngress> ingress);
        bool HasActionEvent(ActorId actorId, ulong instanceId, string eventId, CanonicalTextValue targetId);
        bool HasGameplayEvent(ActorId actorId, string eventId);

        void ReleaseRuntimeIngress();
        void ReleaseAvailabilityInputs();
        void RestoreControlSources(IValueResources state);
        void NotifyStateDisposition(FPCharacterControlSourceStateDisposition disposition);
    }

    [ValueSnapshot]
    public sealed partial class FPLocalInputSourcePort : IFPLocalInputSourcePort
    {
        [ValueSnapshot] public FPValuePortInputBank ValuePorts { get; } = new();
        readonly FPQueuedIngressBuffer m_RuntimeIngress = new();
        readonly ReadOnlyCollection<FPLocalSimulationInputBinding> m_Bindings;
        readonly SimulationPipelineActorInput<FPStepInput>[] m_InputScratch;
        SimulationInputFrameBank[] m_InputFrames;
        readonly FPLocalEnemyInput[] m_EnemyInputScratch;
        readonly WorldKinematicBodySample[] m_KinematicBodyScratch;
        readonly int m_OffensiveRequestDelayTicks;
        readonly int m_MaximumPendingRequests;
        [ValueSnapshot(RestoreMethod = nameof(RestoreControlSourceState))] readonly IValueSnapshotSource[] m_StatefulBindings;
        [ValueSnapshot] ulong m_LastReadSourceTick;
        [ValueSnapshot] ExecutionWorkspaceBuffer<SimulationInputValue>[] m_CurrentInputValues = Array.Empty<ExecutionWorkspaceBuffer<SimulationInputValue>>();
        [ValueSnapshot] AbilityInputEvent[][] m_CurrentEvents;
        [ValueSnapshot] int[] m_CurrentEventCounts;

        public FPLocalInputSourcePort(
            SimulationComponentIdentity sessionSource,
            IEnumerable<FPLocalSimulationInputBinding> bindings,
            int offensiveRequestDelayTicks,
            int maximumPendingRequests,
            IReadOnlyList<FPLocalEnemyBinding> localEnemies = null)
        {
            if (!sessionSource.IsValid || sessionSource.Role != SimulationComponentRole.SessionSource)
                throw new ArgumentException("FP Local Session Source identity is invalid.", nameof(sessionSource));
            if (offensiveRequestDelayTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(offensiveRequestDelayTicks));
            if (maximumPendingRequests <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumPendingRequests));
            var values = bindings == null
                ? new List<FPLocalSimulationInputBinding>()
                : new List<FPLocalSimulationInputBinding>(bindings);
            values.Sort((left, right) => left.ActorId.CompareTo(right.ActorId));
            if (values.Count == 0)
                throw new ArgumentException("FP Local input Source requires an Actor roster.", nameof(bindings));
            var identity = new string[values.Count + 4];
            identity[0] = "fp-local-input-source-port/" + FPLocalInputSourcePortContract.SchemaVersion;
            identity[1] = sessionSource.ToString();
            identity[2] = offensiveRequestDelayTicks.ToString();
            identity[3] = maximumPendingRequests.ToString();
            for (int i = 0; i < values.Count; i++)
            {
                FPLocalSimulationInputBinding binding = values[i] ??
                    throw new ArgumentException("FP Local input Source contains a missing binding.", nameof(bindings));
                if (i > 0 && values[i - 1].ActorId.Equals(binding.ActorId))
                    throw new ArgumentException("FP Local input Source contains duplicate ActorId.", nameof(bindings));
                identity[i + 4] = $"{binding.ActorId}:{binding.ControlSource.SourceIdentity}";
            }
            m_Bindings = values.AsReadOnly();
            m_InputScratch = new SimulationPipelineActorInput<FPStepInput>[values.Count];
            LocalEnemies = localEnemies ?? Array.Empty<FPLocalEnemyBinding>();
            m_EnemyInputScratch = new FPLocalEnemyInput[LocalEnemies.Count];
            m_KinematicBodyScratch = new WorldKinematicBodySample[LocalEnemies.Count];
            m_OffensiveRequestDelayTicks = offensiveRequestDelayTicks;
            m_MaximumPendingRequests = maximumPendingRequests;
            var statefulBindings = new List<IValueSnapshotSource>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].ControlSource is IFPCharacterControlSourceInputPreparation preparation)
                    preparation.PrepareInputState(maximumPendingRequests);
                if (values[i].ControlSource is IValueSnapshotSource stateful)
                    statefulBindings.Add(stateful);
            }
            m_StatefulBindings = statefulBindings.ToArray();
            Descriptor = new SimulationPortDescriptor(
                FPLocalInputSourcePortContract.PortId,
                FPLocalInputSourcePortContract.SchemaId,
                FPLocalInputSourcePortContract.SchemaVersion,
                SimulationPortDirection.Input,
                sessionSource.ComponentId,
                StableHash.Compute(identity));
        }

        public void PrepareAvailabilityRead(IReadOnlyList<SimulationActorBinding> roster)
        {
            m_CurrentInputValues = new ExecutionWorkspaceBuffer<SimulationInputValue>[m_Bindings.Count];
            m_CurrentEvents = new AbilityInputEvent[m_Bindings.Count][];
            m_CurrentEventCounts = new int[m_Bindings.Count];
            for (int actor = 0; actor < m_Bindings.Count; actor++)
            {
                roster[actor].PrepareInputRequestCapacity(m_MaximumPendingRequests, m_OffensiveRequestDelayTicks);
                var source = m_Bindings[actor].ControlSource;
                int ingress = roster[actor].AvailabilityIngressCapacity;
                m_CurrentEvents[actor] = new AbilityInputEvent[ingress];
                m_CurrentInputValues[actor] = new ExecutionWorkspaceBuffer<SimulationInputValue>(source.InputValueCapacity);
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
                m_CurrentEvents[actor][i].Release();
            Array.Clear(m_CurrentEvents[actor], 0, m_CurrentEventCounts[actor]);
            m_CurrentEventCounts[actor] = 0;
        }
        public void CaptureFrameEvents(ActorId actorId, ReadOnlySpan<SimulationIngress> ingress)
        {
            int actor = RequireInputActor(actorId);
            ClearFrameEvents(actor);
            for (int i = 0; i < ingress.Length; i++)
            {
                if (!FPAbilityFrameEventSemantics.TryProject(actorId, in ingress[i], out AbilityInputEvent frozen))
                    continue;
                frozen.Retain();
                m_CurrentEvents[actor][m_CurrentEventCounts[actor]++] = frozen;
            }
        }
        public bool HasActionEvent(ActorId actorId, ulong instanceId, string eventId, CanonicalTextValue targetId)
        {
            int actor = RequireInputActor(actorId);
            return FPAbilityFrameEventSemantics.HasActionEvent(
                new ReadOnlySpan<AbilityInputEvent>(m_CurrentEvents[actor], 0, m_CurrentEventCounts[actor]),
                actorId, instanceId, eventId, targetId);
        }
        public bool HasGameplayEvent(ActorId actorId, string eventId)
        {
            int actor = RequireInputActor(actorId);
            return FPAbilityFrameEventSemantics.HasGameplayEvent(
                new ReadOnlySpan<AbilityInputEvent>(m_CurrentEvents[actor], 0, m_CurrentEventCounts[actor]), actorId, eventId);
        }

        public SimulationPortDescriptor Descriptor { get; }
        public IReadOnlyList<FPLocalEnemyBinding> LocalEnemies { get; }

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

        public FPLocalInputFrame Read(
            SimulationTickSourceIdentity source,
            SimulationTick simulationTick,
            int tickRate,
            IReadOnlyList<SimulationActorBinding> roster,
            CommittedActorPoseSnapshot<FPVector3, FP> committedObservation)
        {
            if (source.Kind != SimulationTickSourceKind.LocalLogic || source.SourceTick <= m_LastReadSourceTick)
                throw new InvalidOperationException("FP Local input Source requires a new LocalLogic source Tick.");
            if (!simulationTick.IsValid || tickRate <= 0 ||
                roster == null || roster.Count != m_Bindings.Count)
            {
                throw new InvalidOperationException("FP Local input Source request does not match its locked Actor roster.");
            }
            if (committedObservation == null || committedObservation.ObservationTick != simulationTick.Value - 1)
                throw new InvalidOperationException("FP Local input Source requires the committed observation immediately preceding the requested simulation Tick.");
            if (committedObservation.Actors.Count != roster.Count + LocalEnemies.Count)
                throw new InvalidOperationException("FP Local input Source committed observation does not match its locked Actor roster.");
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i] == null || !committedObservation.TryGetActor(roster[i].ActorId, out _))
                    throw new InvalidOperationException("FP Local input Source committed observation Actor identities do not match its locked roster.");
            }
            ValuePorts.BeginTick(simulationTick.Value);
            SimulationPipelineActorInput<FPStepInput>[] inputs = m_InputScratch;
            bool prepared = false;
            try
            {
                for (int i = 0; i < roster.Count; i++)
                {
                    SimulationActorBinding actor = roster[i] ??
                        throw new InvalidOperationException("FP Local input Source roster contains a missing Actor binding.");
                    FPLocalSimulationInputBinding binding = m_Bindings[i];
                    IFPCharacterControlSourceRuntime controlSource = binding.ControlSource;
                    if (!actor.ActorId.Equals(binding.ActorId))
                    {
                        throw new InvalidOperationException($"FP Local input binding for Actor '{actor.ActorId}' is incompatible.");
                    }
                    var context = new FPCharacterInputBuildContext(
                        actor.ActorId,
                        simulationTick,
                        source,
                        source.SourceTick,
                        tickRate,
                        committedObservation,
                        m_InputFrames[i].BeginFrame());
                    SimulationInput input = FPCharacterInputTraceModule.IsSessionHistoryReplaying
                        ? default : controlSource.BuildInput(context);
                    input = FPCharacterInputTraceModule.Resolve(context, input);
                    if (!input.IsValid || input.NumericProfile != QuantumSimulationNumericProfile.Value ||
                        !input.TickSource.Equals(source) || input.Sequence != source.SourceTick)
                    {
                        throw new InvalidOperationException($"FP Control Source '{controlSource.SourceIdentity}' returned input outside the Local Source contract.");
                    }
                    CaptureInputValues(i, input.Values);
                    if (controlSource is IFPCharacterInputFrameConsumer consumer)
                        consumer.ReceiveInput(input);
                    inputs[i] = new SimulationPipelineActorInput<FPStepInput>(
                        actor.ActorId,
                        source.SourceTick,
                        new FPStepInput(input));
                }
                for (int i = 0; i < LocalEnemies.Count; i++)
                {
                    m_EnemyInputScratch[i] = LocalEnemies[i].Source.FreezeInput(simulationTick.Value);
                    FPLocalEnemyDefinition definition = LocalEnemies[i].Definition;
                    FPLocalEnemyInput frozen = m_EnemyInputScratch[i];
                    var offset = new FPVector3(definition.ReceiverCenter.X,
                        definition.ReceiverCenter.Y - definition.HalfLength - definition.Radius,
                        definition.ReceiverCenter.Z);
                    var before = FPCombatGeometryPreparation.Pose(frozen.BeforePosition, frozen.BeforeYaw, offset).Position;
                    var after = FPCombatGeometryPreparation.Pose(frozen.Position, frozen.Yaw, offset).Position;
                    m_KinematicBodyScratch[i] = new WorldKinematicBodySample(definition.ActorId,
                        new FPVector3(before.X, before.Y, before.Z), new FPVector3(after.X, after.Y, after.Z));
                }
                FPCanonicalInputBatch batch = new FPCanonicalInputBatch(source, inputs, m_EnemyInputScratch, m_KinematicBodyScratch);
                m_LastReadSourceTick = source.SourceTick;
                prepared = true;
                m_RuntimeIngress.Bind(source, simulationTick, FPCharacterInputTraceModule.ResolveIngress(source, simulationTick));
                return new FPLocalInputFrame(batch, new FPTypedIngressBatch(m_RuntimeIngress));
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

        private partial void RestoreControlSourceState(IValueResources state);

        public void RestoreControlSources(IValueResources state)
        {
            RestoreControlSourceState(state);
            for (int i = 0; i < m_Bindings.Count; i++)
                if (m_Bindings[i].ControlSource is IFPCharacterControlSourceTransactionObserver observer)
                    observer.NotifyStateDisposition(FPCharacterControlSourceStateDisposition.Restored);
        }

        public void NotifyStateDisposition(FPCharacterControlSourceStateDisposition disposition)
        {
            if (disposition is not (FPCharacterControlSourceStateDisposition.Prepared or FPCharacterControlSourceStateDisposition.Committed or
                FPCharacterControlSourceStateDisposition.Discarded or FPCharacterControlSourceStateDisposition.Restored))
                throw new ArgumentOutOfRangeException(nameof(disposition));
            for (int i = 0; i < m_Bindings.Count; i++)
                if (m_Bindings[i].ControlSource is IFPCharacterControlSourceTransactionObserver observer)
                    observer.NotifyStateDisposition(disposition);
            FPCharacterInputTraceModule.NotifyIngressDisposition(disposition);
            if (disposition == FPCharacterControlSourceStateDisposition.Committed)
                m_RuntimeIngress.Commit();
        }
    }
}
