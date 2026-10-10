using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

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

    public interface ICharacterControlSourceStateRuntime
    {
        string StateSchemaId { get; }
        int StateSchemaVersion { get; }
        int MinimumStatePayloadBytes { get; }
        int StateTextCapacity { get; }
        void WriteState(CanonicalWriter writer);
        void RestoreState(ReadOnlyMemory<byte> state, ICanonicalTextResolver resolver);
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
        public const int SchemaVersion = 4;

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

    internal readonly struct Float32AbilityInputEvent
    {
        internal Float32AbilityInputEvent(byte kind, ActorId sourceActor, ActorId targetActor, ulong sourceInstance,
            ulong targetInstance, CanonicalTextValue eventId, CanonicalTextValue attackEvent,
            CanonicalTextValue sourceFeedback, CanonicalTextValue targetFeedback, byte disposition)
        {
            Kind = kind; SourceActor = sourceActor; TargetActor = targetActor;
            SourceInstance = sourceInstance; TargetInstance = targetInstance;
            EventId = eventId; AttackEvent = attackEvent; SourceFeedback = sourceFeedback;
            TargetFeedback = targetFeedback; Disposition = disposition;
        }
        internal readonly byte Kind, Disposition;
        internal readonly ActorId SourceActor, TargetActor;
        internal readonly ulong SourceInstance, TargetInstance;
        internal readonly CanonicalTextValue EventId, AttackEvent, SourceFeedback, TargetFeedback;
        internal void Retain() { SourceActor.Value.Retain(); TargetActor.Value.Retain(); EventId.Retain(); AttackEvent.Retain(); SourceFeedback.Retain(); TargetFeedback.Retain(); }
        internal void Release() { SourceActor.Value.Release(); TargetActor.Value.Release(); EventId.Release(); AttackEvent.Release(); SourceFeedback.Release(); TargetFeedback.Release(); }
    }

    internal interface IFloat32CommittedInputReadPort
    {
        void PrepareAvailabilityRead(int maximumPayloadBytes, IReadOnlyList<SimulationActorBinding> roster);
        void RestoreControlInput(ArraySegment<byte> bytes);
        void GetInputCapacity(ActorId actorId, out int values, out int requests);
        ulong ReadInputSequence(ActorId actorId);
        SimulationInputValue ReadInputValue(ActorId actorId, CanonicalTextValue inputId, ProgramInputValueKind kind);
        void CaptureFrameEvents(ActorId actorId, ReadOnlySpan<SimulationIngress> ingress);
        bool HasActionEvent(ActorId actorId, ulong instanceId, string eventId, CanonicalTextValue targetId);
        bool HasGameplayEvent(ActorId actorId, string eventId);

    }

    public interface IFloat32LocalInputSourcePort : ISimulationRuntimePort
    {
        Float32LocalInputFrame Read(
            SimulationTickSourceIdentity source,
            SimulationTick simulationTick,
            SimulationNumericProfile numericProfile,
            int tickRate,
            IReadOnlyList<SimulationActorBinding> roster,
            CommittedActorPoseSnapshot<Float32Vector3, Float32Yaw> committedObservation);
        int StatePayloadCapacity { get; }
        int StateTextCapacity { get; }
        void PrepareAvailabilityRead(int maximumPayloadBytes, IReadOnlyList<SimulationActorBinding> roster);
        ulong ReadInputSequence(ActorId actorId);
        SimulationInputValue ReadInputValue(ActorId actorId, CanonicalTextValue inputId, ProgramInputValueKind kind);
        void CaptureFrameEvents(ActorId actorId, ReadOnlySpan<SimulationIngress> ingress);
        bool HasActionEvent(ActorId actorId, ulong instanceId, string eventId, CanonicalTextValue targetId);
        bool HasGameplayEvent(ActorId actorId, string eventId);

        void ReleaseInputFrames();
        void GetInputCapacity(ActorId actorId, out int values, out int requests);
        void ReleaseAvailabilityInputs();
        void WriteState(CanonicalWriter writer);
        void RestoreState(ReadOnlyMemory<byte> state, ICanonicalTextResolver resolver);
        void NotifyStateDisposition(CharacterControlSourceStateDisposition disposition);
    }

    public sealed class Float32LocalInputSourcePort : IFloat32LocalInputSourcePort
    {
        readonly ReadOnlyCollection<LocalSimulationInputBinding> m_Bindings;
        readonly SimulationPipelineActorInput<Float32StepInput>[] m_InputScratch;
        readonly StateBinding[] m_StatefulBindings;
        readonly CanonicalReader m_StateReader;
        ulong m_LastReadSourceTick;
        ExecutionWorkspaceBuffer<SimulationInputValue>[] m_CurrentInputValues = Array.Empty<ExecutionWorkspaceBuffer<SimulationInputValue>>();
        string[][] m_DeclaredInputIds;
        Float32AbilityInputEvent[][] m_CurrentEvents;
        int[] m_CurrentEventCounts;
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
            identityValues[0] = "float32-local-input-source-port/4";
            identityValues[1] = sessionSource.ToString();
            for (int i = 0; i < values.Count; i++)
            {
                LocalSimulationInputBinding binding = values[i] ??
                    throw new ArgumentException("Local input Source contains a missing binding.", nameof(bindings));
                if (i > 0 && values[i - 1].ActorId.Equals(binding.ActorId))
                    throw new ArgumentException("Local input Source contains duplicate ActorId.", nameof(bindings));
                ICharacterControlSourceRuntime source = binding.ControlSource;
                if (((source.Capabilities & CharacterControlSourceCapability.TransactionalState) != 0) !=
                    (source is ICharacterControlSourceStateRuntime))
                {
                    throw new ArgumentException($"Control Source '{source.SourceIdentity}' state capability and runtime contract disagree.", nameof(bindings));
                }
                string stateIdentity = source is ICharacterControlSourceStateRuntime stateful
                    ? $"{stateful.StateSchemaId}:{stateful.StateSchemaVersion}:{stateful.MinimumStatePayloadBytes}"
                    : "stateless";
                identityValues[i + 2] = $"{binding.ActorId}:{source.SourceIdentity}:{source.NumericProfile}:{source.Capabilities}:{stateIdentity}:{source.InputValueCapacity}:{source.InputRequestCapacity}";
            }
            m_Bindings = values.AsReadOnly();
            m_InputFrames = new Float32SimulationInputFrameStorage[m_Bindings.Count];
            for (int actor = 0; actor < m_Bindings.Count; actor++)
                m_InputFrames[actor] = new Float32SimulationInputFrameStorage(m_Bindings[actor].ControlSource.InputValueCapacity,
                    m_Bindings[actor].ControlSource.InputRequestCapacity);

            m_InputScratch = new SimulationPipelineActorInput<Float32StepInput>[values.Count];
            int statefulCount = 0;
            int payloadCapacity = 20;
            int textCapacity = 0;
            for (int i = 0; i < values.Count; i++)
            {
                LocalSimulationInputBinding binding = values[i];
                if (binding.ControlSource is not ICharacterControlSourceStateRuntime stateful)
                    continue;
                if (stateful.MinimumStatePayloadBytes < 0)
                    throw new ArgumentException("Control Source state payload capacity is missing.", nameof(bindings));
                statefulCount++;
                textCapacity = checked(textCapacity + 3 + stateful.StateTextCapacity);
                payloadCapacity = checked(payloadCapacity + 20 +
                    CanonicalTextEncoding.ByteCount(binding.ActorId.Value.AsSpan()) +
                    CanonicalTextEncoding.ByteCount(binding.ControlSource.SourceIdentity.AsSpan()) +
                    CanonicalTextEncoding.ByteCount(stateful.StateSchemaId.AsSpan()) +
                    stateful.MinimumStatePayloadBytes);
            }
            m_StatefulBindings = new StateBinding[statefulCount];
            int statefulIndex = 0;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].ControlSource is ICharacterControlSourceStateRuntime stateful)
                    m_StatefulBindings[statefulIndex++] = new StateBinding(values[i], stateful);
            }
            m_StateReader = new CanonicalReader(Array.Empty<byte>());
            StatePayloadCapacity = payloadCapacity;
            StateTextCapacity = textCapacity;
            Descriptor = new SimulationPortDescriptor(
                Float32LocalInputSourcePortContract.PortId,
                Float32LocalInputSourcePortContract.SchemaId,
                Float32LocalInputSourcePortContract.SchemaVersion,
                SimulationPortDirection.Input,
                sessionSource.ComponentId,
                StableHash.Compute(identityValues));
        }

        public void PrepareAvailabilityRead(int maximumPayloadBytes, IReadOnlyList<SimulationActorBinding> roster)
        {
            if (maximumPayloadBytes < StatePayloadCapacity)
                throw new InvalidOperationException("Local input state exceeds the Session checkpoint payload declaration.");
            StatePayloadCapacity = maximumPayloadBytes;
            m_CurrentInputValues = new ExecutionWorkspaceBuffer<SimulationInputValue>[m_Bindings.Count];
            m_DeclaredInputIds = new string[m_Bindings.Count][];
            m_CurrentEvents = new Float32AbilityInputEvent[m_Bindings.Count][];
            m_CurrentEventCounts = new int[m_Bindings.Count];
            for (int actor = 0; actor < m_Bindings.Count; actor++)
            {
                var source = m_Bindings[actor].ControlSource;
                int ingress = roster[actor].AvailabilityIngressCapacity;
                m_CurrentEvents[actor] = new Float32AbilityInputEvent[ingress];
                StateTextCapacity = checked(StateTextCapacity + 6 * ingress);
                m_CurrentInputValues[actor] = new ExecutionWorkspaceBuffer<SimulationInputValue>(source.InputValueCapacity);

                StateTextCapacity = checked(StateTextCapacity + 2 * source.InputValueCapacity);
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
            for (int i = 0; i < m_CurrentEventCounts[actor]; i++) m_CurrentEvents[actor][i].Release();
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
                Float32AbilityInputEvent frozen;
                if (value.Header.Kind == SimulationIngressKind.ActionEvent)
                    frozen = new Float32AbilityInputEvent(1, actorId, default, value.ActionEvent.ActionInstanceId, 0, value.ActionEvent.EventId, default, default, default, 0);
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
                ref readonly Float32AbilityInputEvent value = ref m_CurrentEvents[actor][i];
                if (value.Kind == 1 && value.SourceInstance == instanceId && value.EventId.Equals(eventId)) return true;
            }
            return false;
        }
        public bool HasGameplayEvent(ActorId actorId, string eventId)
        {
            int actor = RequireInputActor(actorId);
            for (int i = 0; i < m_CurrentEventCounts[actor]; i++)
            {
                ref readonly Float32AbilityInputEvent value = ref m_CurrentEvents[actor][i];
                if (value.Kind == 1 && value.EventId.Equals(eventId))
                    return true;
            }
            return false;
        }

        void WriteRetainedEvents(CanonicalWriter writer)
        {
            for (int actor = 0; actor < m_Bindings.Count; actor++)
            {
                writer.WriteInt32(m_CurrentEventCounts[actor]);
                for (int i = 0; i < m_CurrentEventCounts[actor]; i++)
                {
                    ref readonly Float32AbilityInputEvent value = ref m_CurrentEvents[actor][i];
                    writer.WriteByte(value.Kind); writer.WriteByte(value.Disposition);
                    writer.WriteText(value.SourceActor.Value); writer.WriteText(value.TargetActor.Value);
                    writer.WriteUInt64(value.SourceInstance); writer.WriteUInt64(value.TargetInstance);
                    writer.WriteText(value.EventId); writer.WriteText(value.AttackEvent);
                    writer.WriteText(value.SourceFeedback); writer.WriteText(value.TargetFeedback);
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
                    byte kind = reader.ReadByte(), disposition = reader.ReadByte();
                    var sourceText = reader.ReadText(); var targetText = reader.ReadText();
                    ActorId source = sourceText.IsEmpty ? default : new ActorId(sourceText);
                    ActorId target = targetText.IsEmpty ? default : new ActorId(targetText);
                    var value = new Float32AbilityInputEvent(kind, source, target, reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadText(), reader.ReadText(), reader.ReadText(), reader.ReadText(), disposition);
                    value.Retain(); m_CurrentEvents[actor][m_CurrentEventCounts[actor]++] = value;
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
                for (int i = 0; i < values.Count; i++) WriteRetainedValue(writer, values[i]);
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
                    CanonicalTextValue inputId = reader.ReadText();
                    SimulationInputValue value = ReadRetainedValue(reader, inputId);
                    value.Retain();
                    values.Add(value);
                }
            }
        }
        static void WriteRetainedValue(CanonicalWriter writer, SimulationInputValue value)
        {
            writer.WriteText(value.InputId);
            writer.WriteByte((byte)value.Kind);
            switch (value.Kind)
            {
                case ProgramInputValueKind.Boolean: writer.WriteBoolean(value.Boolean); break;
                case ProgramInputValueKind.Scalar: writer.WriteScalar(value.Scalar); break;
                case ProgramInputValueKind.Vector2: writer.WriteVector2(value.Vector2); break;
                case ProgramInputValueKind.Vector3: writer.WriteVector3(value.Vector3); break;
                case ProgramInputValueKind.Yaw: writer.WriteYaw(value.Yaw); break;
                case ProgramInputValueKind.ActionTargetSnapshot:
                    writer.WriteText(value.ActionTargetSnapshot.TargetId); writer.WriteVector3(value.ActionTargetSnapshot.Position); writer.WriteYaw(value.ActionTargetSnapshot.Yaw); break;
                default: throw new System.IO.InvalidDataException("Unsupported retained input value kind.");
            }
        }
        static SimulationInputValue ReadRetainedValue(CanonicalReader reader, CanonicalTextValue inputId) => (ProgramInputValueKind)reader.ReadByte() switch
        {
            ProgramInputValueKind.Boolean => SimulationInputValue.FromBoolean(inputId, reader.ReadBoolean()),
            ProgramInputValueKind.Scalar => SimulationInputValue.FromScalar(inputId, reader.ReadScalar()),
            ProgramInputValueKind.Vector2 => SimulationInputValue.FromVector2(inputId, reader.ReadVector2()),
            ProgramInputValueKind.Vector3 => SimulationInputValue.FromVector3(inputId, reader.ReadVector3()),
            ProgramInputValueKind.Yaw => SimulationInputValue.FromYaw(inputId, reader.ReadYaw()),
            ProgramInputValueKind.ActionTargetSnapshot => SimulationInputValue.FromActionTargetSnapshot(inputId, new SimulationActionTargetSnapshot(reader.ReadText(), reader.ReadVector3(), reader.ReadYaw())),
            _ => throw new System.IO.InvalidDataException("Unsupported retained input value kind.")
        };

        public SimulationPortDescriptor Descriptor { get; }
        public int StatePayloadCapacity { get; private set; }
        public int StateTextCapacity { get; private set; }

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

        public void WriteState(CanonicalWriter writer)
        {
            writer.WriteUInt32(0x53434c46);
            writer.WriteInt32(3);
            writer.WriteUInt64(m_LastReadSourceTick);
            writer.WriteInt32(m_StatefulBindings.Length);
            for (int i = 0; i < m_StatefulBindings.Length; i++)
            {
                StateBinding binding = m_StatefulBindings[i];
                writer.WriteBytes(binding.ActorIdentity);
                writer.WriteBytes(binding.SourceIdentity);
                writer.WriteBytes(binding.SchemaIdentity);
                writer.WriteInt32(binding.Runtime.StateSchemaVersion);
                long payloadPrefix = writer.BeginLengthPrefixedBlock();
                binding.Runtime.WriteState(writer);

                writer.EndLengthPrefixedBlock(payloadPrefix);
            }
            WriteRetainedInputs(writer);
            WriteRetainedEvents(writer);
        }

        public void RestoreState(ReadOnlyMemory<byte> state, ICanonicalTextResolver resolver)
        {
            CanonicalReader reader = m_StateReader;
            reader.Reset(state);
            reader.SetTextResolver(resolver);
            if (reader.ReadUInt32() != 0x53434c46 || reader.ReadInt32() != 3)
                throw new InvalidDataException("Local Control Source state header is invalid.");
            ulong lastReadSourceTick = reader.ReadUInt64();
            int count = reader.ReadInt32();
            if (count != m_StatefulBindings.Length)
                throw new InvalidDataException("Local Control Source state roster does not match its locked bindings.");
            for (int i = 0; i < count; i++)
            {
                StateBinding binding = m_StatefulBindings[i];
                ArraySegment<byte> actorId = reader.ReadTextSegment();
                ArraySegment<byte> sourceIdentity = reader.ReadTextSegment();
                ArraySegment<byte> schemaId = reader.ReadTextSegment();
                int schemaVersion = reader.ReadInt32();
                ArraySegment<byte> payload = reader.ReadBytesSegment();
                if (!actorId.AsSpan().SequenceEqual(binding.ActorIdentity) ||
                    !sourceIdentity.AsSpan().SequenceEqual(binding.SourceIdentity) ||
                    !schemaId.AsSpan().SequenceEqual(binding.SchemaIdentity) ||
                    schemaVersion != binding.Runtime.StateSchemaVersion)
                {
                    throw new InvalidDataException("Local Control Source state identity does not match its locked binding.");
                }
                binding.Runtime.RestoreState(payload.AsMemory(), resolver);
            }
            ReadRetainedInputs(reader);
            ReadRetainedEvents(reader);
            reader.RequireComplete();
            m_LastReadSourceTick = lastReadSourceTick;
        }

        static byte[] EncodeIdentity(CanonicalTextValue identity)
        {
            var encoded = new byte[CanonicalTextEncoding.ByteCount(identity.Span)];
            CanonicalTextEncoding.Encode(identity.Span, encoded);
            return encoded;
        }

        readonly struct StateBinding
        {
            internal StateBinding(LocalSimulationInputBinding binding, ICharacterControlSourceStateRuntime runtime)
            {
                Runtime = runtime;
                ActorIdentity = EncodeIdentity(binding.ActorId.Value);
                SourceIdentity = EncodeIdentity(binding.ControlSource.SourceIdentity);
                SchemaIdentity = EncodeIdentity(runtime.StateSchemaId);
            }

            internal ICharacterControlSourceStateRuntime Runtime { get; }
            internal byte[] ActorIdentity { get; }
            internal byte[] SourceIdentity { get; }
            internal byte[] SchemaIdentity { get; }
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
