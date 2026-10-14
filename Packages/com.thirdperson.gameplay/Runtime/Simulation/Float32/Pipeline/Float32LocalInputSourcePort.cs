using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public readonly struct SimulationInputBuildContext
    {
        public SimulationInputBuildContext(
            ActorId actorId,
            SimulationNumericProfile numericProfile,
            SimulationTick simulationTick,
            SimulationTickSourceIdentity source,
            ulong inputSequence,
            int tickRate,
            CommittedActorPoseSnapshot<Float32Vector3, Float32Yaw> committedObservation,
            Float32SimulationInputFrameStorage inputStorage)
        {
            if (!actorId.IsValid || !numericProfile.IsValid || !simulationTick.IsValid ||
                CanonicalTextValue.IsNullOrEmpty(source.ClockId) || inputSequence == 0 || tickRate <= 0)
                throw new ArgumentException("Simulation input build context is incomplete.");
            ActorId = actorId;
            NumericProfile = numericProfile;
            SimulationTick = simulationTick;
            Source = source;
            InputSequence = inputSequence;
            TickRate = tickRate;
            CommittedObservation = committedObservation ?? throw new ArgumentNullException(nameof(committedObservation));
            InputStorage = inputStorage;
        }

        public ActorId ActorId { get; }
        public SimulationNumericProfile NumericProfile { get; }
        public SimulationTick SimulationTick { get; }
        public SimulationTickSourceIdentity Source { get; }
        public ulong InputSequence { get; }
        public int TickRate { get; }
        public CommittedActorPoseSnapshot<Float32Vector3, Float32Yaw> CommittedObservation { get; }
        public Float32SimulationInputFrameStorage InputStorage { get; }
    }

    [Flags]
    public enum CharacterControlSourceCapability : byte
    {
        None = 0,
        CommittedObservation = 1,
        TransactionalState = 2
    }

    public interface ICharacterControlSourceRuntime
    {
        string SourceIdentity { get; }
        SimulationNumericProfile NumericProfile { get; }
        CharacterControlSourceCapability Capabilities { get; }
        SimulationInput BuildInput(SimulationInputBuildContext context);
        int InputValueCapacity { get; }
        int InputRequestCapacity { get; }
    }

    public interface ICharacterControlSourceInputRequestRuntime
    {
        bool TryQueueInputRequest(CanonicalTextValue requestId, out ulong requestSequence, out string error);
    }

    public enum CharacterControlSourceStateDisposition : byte
    {
        Prepared = 1,
        Committed = 2,
        Discarded = 3,
        Restored = 4
    }

    public interface ICharacterControlSourceTransactionObserver
    {
        void NotifyStateDisposition(CharacterControlSourceStateDisposition disposition);
    }

    public interface ICharacterControlSourceRosterRuntime
    {
        void ValidateRoster(
            ActorId actorId,
            IReadOnlyList<ActorId> roster,
            StableHash committedObservationCapability);
    }

    public sealed class LocalSimulationInputBinding
    {
        public LocalSimulationInputBinding(ActorId actorId, ICharacterControlSourceRuntime controlSource)
        {
            if (!actorId.IsValid)
                throw new ArgumentException("Local input binding ActorId is invalid.", nameof(actorId));
            ActorId = actorId;
            ControlSource = controlSource ?? throw new ArgumentNullException(nameof(controlSource));
        }

        public ActorId ActorId { get; }
        public ICharacterControlSourceRuntime ControlSource { get; }
    }

    public static class Float32LocalInputSourcePortContract
    {
        public const string PortId = "simulation.source.local-actor-input";
        public const string SchemaId = "float32-local-actor-input-source";
        public const int SchemaVersion = 6;

        public static SimulationPipelinePortRequirement Requirement =>
            new SimulationPipelinePortRequirement(
                SimulationPipelineBindingPortRole.Source,
                PortId,
                SchemaId,
                SchemaVersion,
                SimulationPortDirection.Input);
    }

    public readonly struct Float32LocalInputFrame
    {
        public Float32LocalInputFrame(
            Float32CanonicalInputBatch canonicalInputs,
            Float32TypedIngressBatch typedIngress)
        {
            if (!canonicalInputs.IsValid)
                throw new ArgumentException("Canonical input batch is incomplete.", nameof(canonicalInputs));
            if (!typedIngress.IsValid)
                throw new ArgumentException("Typed ingress batch is invalid.", nameof(typedIngress));
            TypedIngress = typedIngress;
            CanonicalInputs = canonicalInputs;
        }

        public Float32CanonicalInputBatch CanonicalInputs { get; }
        public Float32TypedIngressBatch TypedIngress { get; }
    }

    internal interface IFloat32CommittedInputReadPort
    {
        void PrepareAvailabilityRead(IReadOnlyList<SimulationActorBinding> roster);
        void RestoreControlInput(IValueResources state);
        void GetInputCapacity(ActorId actorId, out int values, out int requests);
        ulong ReadInputSequence(ActorId actorId);
        SimulationInputValue ReadInputValue(ActorId actorId, CanonicalTextValue inputId, ProgramInputValueKind kind);
        void CaptureFrameEvents(ActorId actorId, ReadOnlySpan<SimulationIngress> ingress);
        bool HasActionEvent(ActorId actorId, ulong instanceId, string eventId, CanonicalTextValue targetId);
        bool HasGameplayEvent(ActorId actorId, string eventId);

    }

    public interface IFloat32LocalInputSourcePort : ISimulationRuntimePort, IValueSnapshotSource
    {
        Float32LocalInputFrame Read(
            SimulationTickSourceIdentity source,
            SimulationTick simulationTick,
            SimulationNumericProfile numericProfile,
            int tickRate,
            IReadOnlyList<SimulationActorBinding> roster,
            CommittedActorPoseSnapshot<Float32Vector3, Float32Yaw> committedObservation);
        void PrepareAvailabilityRead(IReadOnlyList<SimulationActorBinding> roster);
        ulong ReadInputSequence(ActorId actorId);
        SimulationInputValue ReadInputValue(ActorId actorId, CanonicalTextValue inputId, ProgramInputValueKind kind);
        void CaptureFrameEvents(ActorId actorId, ReadOnlySpan<SimulationIngress> ingress);
        bool HasActionEvent(ActorId actorId, ulong instanceId, string eventId, CanonicalTextValue targetId);
        bool HasGameplayEvent(ActorId actorId, string eventId);

        void ReleaseInputFrames();
        void GetInputCapacity(ActorId actorId, out int values, out int requests);
        void ReleaseAvailabilityInputs();
        void NotifyStateDisposition(CharacterControlSourceStateDisposition disposition);
    }

    [ValueSnapshot]
    public sealed partial class Float32LocalInputSourcePort : IFloat32LocalInputSourcePort
    {
        readonly ReadOnlyCollection<LocalSimulationInputBinding> m_Bindings;
        readonly SimulationPipelineActorInput<Float32StepInput>[] m_InputScratch;
        [ValueSnapshot] readonly IValueSnapshotSource[] m_StatefulBindings;
        [ValueSnapshot] ulong m_LastReadSourceTick;
        [ValueSnapshot] ExecutionWorkspaceBuffer<SimulationInputValue>[] m_CurrentInputValues = Array.Empty<ExecutionWorkspaceBuffer<SimulationInputValue>>();
        [ValueSnapshot] AbilityInputEvent[][] m_CurrentEvents;
        [ValueSnapshot] int[] m_CurrentEventCounts;
        readonly Float32SimulationInputFrameStorage[] m_InputFrames;

        public Float32LocalInputSourcePort(
            SimulationComponentIdentity sessionSource,
            IEnumerable<LocalSimulationInputBinding> bindings)
        {
            if (!sessionSource.IsValid || sessionSource.Role != SimulationComponentRole.SessionSource)
                throw new ArgumentException("Session Source identity is invalid.", nameof(sessionSource));
            var values = bindings == null
                ? new List<LocalSimulationInputBinding>()
                : new List<LocalSimulationInputBinding>(bindings);
            values.Sort((left, right) => left.ActorId.CompareTo(right.ActorId));
            if (values.Count == 0)
                throw new ArgumentException("Local input Source requires explicit Actor input bindings.", nameof(bindings));
            var identityValues = new string[values.Count + 2];
            identityValues[0] = "float32-local-input-source-port/" + Float32LocalInputSourcePortContract.SchemaVersion;
            identityValues[1] = sessionSource.ToString();
            for (int i = 0; i < values.Count; i++)
            {
                LocalSimulationInputBinding binding = values[i] ??
                    throw new ArgumentException("Local input Source contains a missing binding.", nameof(bindings));
                if (i > 0 && values[i - 1].ActorId.Equals(binding.ActorId))
                    throw new ArgumentException("Local input Source contains duplicate ActorId.", nameof(bindings));
                ICharacterControlSourceRuntime source = binding.ControlSource;
                if (((source.Capabilities & CharacterControlSourceCapability.TransactionalState) != 0) !=
                    (source is IValueSnapshotSource))
                {
                    throw new ArgumentException($"Control Source '{source.SourceIdentity}' state capability and runtime contract disagree.", nameof(bindings));
                }
                identityValues[i + 2] = $"{binding.ActorId}:{source.SourceIdentity}:{source.NumericProfile}:{source.Capabilities}:{source.InputValueCapacity}:{source.InputRequestCapacity}";
            }
            m_Bindings = values.AsReadOnly();
            m_InputFrames = new Float32SimulationInputFrameStorage[m_Bindings.Count];
            for (int actor = 0; actor < m_Bindings.Count; actor++)
                m_InputFrames[actor] = new Float32SimulationInputFrameStorage(m_Bindings[actor].ControlSource.InputValueCapacity,
                    m_Bindings[actor].ControlSource.InputRequestCapacity);

            m_InputScratch = new SimulationPipelineActorInput<Float32StepInput>[values.Count];
            var statefulBindings = new List<IValueSnapshotSource>();
            for (int i = 0; i < values.Count; i++)
                if (values[i].ControlSource is IValueSnapshotSource stateful)
                    statefulBindings.Add(stateful);
            m_StatefulBindings = statefulBindings.ToArray();
            Descriptor = new SimulationPortDescriptor(
                Float32LocalInputSourcePortContract.PortId,
                Float32LocalInputSourcePortContract.SchemaId,
                Float32LocalInputSourcePortContract.SchemaVersion,
                SimulationPortDirection.Input,
                sessionSource.ComponentId,
                StableHash.Compute(identityValues));
        }

        public void PrepareAvailabilityRead(IReadOnlyList<SimulationActorBinding> roster)
        {
            m_CurrentInputValues = new ExecutionWorkspaceBuffer<SimulationInputValue>[m_Bindings.Count];
            m_CurrentEvents = new AbilityInputEvent[m_Bindings.Count][];
            m_CurrentEventCounts = new int[m_Bindings.Count];
            for (int actor = 0; actor < m_Bindings.Count; actor++)
            {
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
                ref readonly SimulationIngress value = ref ingress[i];
                AbilityInputEvent frozen;
                if (value.Header.Kind == SimulationIngressKind.ActionEvent)
                    frozen = new AbilityInputEvent(1, actorId, default, value.ActionEvent.ActionInstanceId, 0, value.ActionEvent.EventId, default, default, default, 0);
                else continue;
                frozen.Retain();
                m_CurrentEvents[actor][m_CurrentEventCounts[actor]++] = frozen;
            }
        }
        public bool HasActionEvent(ActorId actorId, ulong instanceId, string eventId, CanonicalTextValue targetId)
        {
            int actor = RequireInputActor(actorId);
            for (int i = 0; i < m_CurrentEventCounts[actor]; i++)
            {
                ref readonly AbilityInputEvent value = ref m_CurrentEvents[actor][i];
                if (value.Kind == 1 && value.SourceInstance == instanceId && value.EventId.Equals(eventId)) return true;
            }
            return false;
        }
        public bool HasGameplayEvent(ActorId actorId, string eventId)
        {
            int actor = RequireInputActor(actorId);
            for (int i = 0; i < m_CurrentEventCounts[actor]; i++)
            {
                ref readonly AbilityInputEvent value = ref m_CurrentEvents[actor][i];
                if (value.Kind == 1 && value.EventId.Equals(eventId))
                    return true;
            }
            return false;
        }

        public SimulationPortDescriptor Descriptor { get; }

        public void ReleaseInputFrames()
        {
            for (int actor = 0; actor < m_InputFrames.Length; actor++) m_InputFrames[actor].Release();
        }

        public void GetInputCapacity(ActorId actorId, out int values, out int requests)
        {
            ICharacterControlSourceRuntime source = m_Bindings[RequireInputActor(actorId)].ControlSource;
            values = source.InputValueCapacity; requests = source.InputRequestCapacity;
        }

        public Float32LocalInputFrame Read(
            SimulationTickSourceIdentity source,
            SimulationTick simulationTick,
            SimulationNumericProfile numericProfile,
            int tickRate,
            IReadOnlyList<SimulationActorBinding> roster,
            CommittedActorPoseSnapshot<Float32Vector3, Float32Yaw> committedObservation)
        {
            if (source.Kind != SimulationTickSourceKind.LocalLogic || source.SourceTick <= m_LastReadSourceTick)
                throw new InvalidOperationException("Local input Source requires a new LocalLogic source Tick.");
            if (!simulationTick.IsValid || !numericProfile.IsValid || tickRate <= 0 ||
                roster == null || roster.Count != m_Bindings.Count)
            {
                throw new InvalidOperationException("Local input Source request does not match its locked Actor bindings.");
            }
            if (committedObservation == null || committedObservation.ObservationTick != simulationTick.Value - 1)
                throw new InvalidOperationException("Local input Source requires the committed observation immediately preceding the requested simulation Tick.");
            if (committedObservation.Actors.Count != roster.Count)
                throw new InvalidOperationException("Local input Source committed observation does not match its locked Actor roster.");
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i] == null || committedObservation.Actors[i].ActorId != roster[i].ActorId)
                    throw new InvalidOperationException("Local input Source committed observation Actor order does not match its locked roster.");
            }
            SimulationPipelineActorInput<Float32StepInput>[] inputs = m_InputScratch;
            bool prepared = false;
            try
            {
                for (int i = 0; i < roster.Count; i++)
                {
                    SimulationActorBinding actor = roster[i] ??
                        throw new InvalidOperationException("Local input Source roster contains a missing Actor binding.");
                    LocalSimulationInputBinding binding = m_Bindings[i];
                    ICharacterControlSourceRuntime controlSource = binding.ControlSource;
                    if (!actor.ActorId.Equals(binding.ActorId) || controlSource.NumericProfile != numericProfile)
                        throw new InvalidOperationException($"Local input Source binding for Actor '{actor.ActorId}' is incompatible.");
                    var context = new SimulationInputBuildContext(
                        actor.ActorId,
                        numericProfile,
                        simulationTick,
                        source,
                        source.SourceTick,
                        tickRate,
                        committedObservation,
                        m_InputFrames[i].BeginFrame());
                    SimulationInput input = Float32CharacterInputTraceModule.Resolve(
                        context,
                        controlSource.BuildInput(context));
                    if (input == null || input.NumericProfile != numericProfile ||
                        !input.TickSource.Equals(source) || input.Sequence != source.SourceTick)
                    {
                        throw new InvalidOperationException($"Control Source '{controlSource.SourceIdentity}' returned input outside the Local Source contract.");
                    }
                    CaptureInputValues(i, input.Values);
                    inputs[i] = new SimulationPipelineActorInput<Float32StepInput>(
                        actor.ActorId,
                        source.SourceTick,
                        new Float32StepInput(input));
                }
                Float32CanonicalInputBatch batch = new Float32CanonicalInputBatch(source, inputs);
                m_LastReadSourceTick = source.SourceTick;
                prepared = true;
                return new Float32LocalInputFrame(batch, Float32TypedIngressBatch.Empty);
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

        public void NotifyStateDisposition(CharacterControlSourceStateDisposition disposition)
        {
            if (disposition < CharacterControlSourceStateDisposition.Prepared ||
                disposition > CharacterControlSourceStateDisposition.Restored)
                throw new ArgumentOutOfRangeException(nameof(disposition));
            for (int i = 0; i < m_Bindings.Count; i++)
            {
                if (m_Bindings[i].ControlSource is ICharacterControlSourceTransactionObserver observer)
                    observer.NotifyStateDisposition(disposition);
            }
        }
    }
}
