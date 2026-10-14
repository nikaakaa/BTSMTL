using System;
using Float32CommittedActorPoseSnapshot = ThirdPersonSimulation.CommittedActorPoseSnapshot<ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32Yaw>;

namespace ThirdPersonSimulation
{
    public sealed class LocalInputIngressPassRuntimeFactory : IFloat32PipelinePassRuntimeFactory
    {
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFloat32PipelinePassContracts.CreateFactoryDescriptor(
                StandardFloat32PipelinePassContracts.LocalInputIngress);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;

        public IFloat32CompiledPipelinePassRuntime Create(Float32PipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new LocalInputIngressReadPorts(
                context.BindSourcePort<IFloat32LocalInputSourcePort>(Float32LocalInputSourcePortContract.PortId),
                context.BindTargetPort<IFloat32CharacterRuntimePort>(Float32PipelineRuntimePortIds.CharacterRuntime),
                context.BindTargetPort<ICommittedActorPoseReadPort<Float32Vector3, Float32Yaw>>(Float32PipelineRuntimePortIds.CommittedObservation));
            var writes = new LocalInputIngressWritePorts(
                context.Products.BindExclusiveWriter<Float32CanonicalInputBatch>(SimulationPipelineProducts.CanonicalInputs),
                context.Products.BindExclusiveWriter<Float32TypedIngressBatch>(SimulationPipelineProducts.TypedIngress));
            return new Float32IngressPassRuntimeAdapter<LocalInputIngressReadPorts, LocalInputIngressWritePorts>(
                new LocalInputIngressPassRuntime(context.Pass.Descriptor, reads.Source),
                reads,
                writes);
        }
    }

    public sealed class LocalInputIngressPassRuntime :
        Float32PipelinePassRuntimeBase,
        ISimulationIngressPassRuntime<LocalInputIngressReadPorts, LocalInputIngressWritePorts>,
        ISimulationPipelineStateParticipant, IFloat32CommittedInputReadPort, ISimulationPipelineStepStateStorage
    {
        readonly IFloat32LocalInputSourcePort m_Source;
        SimulationPipelinePassStateStorage m_StepStorage;
        LocalControlInputCheckpoint m_Checkpoint;
        LocalControlInputRestoreTransaction m_Restore;

        public LocalInputIngressPassRuntime(
            SimulationPipelinePassDescriptor descriptor,
            IFloat32LocalInputSourcePort source)
            : base(descriptor)
        {
            m_Source = source ?? throw new ArgumentNullException(nameof(source));
            StateIdentity = new SimulationPipelineStateParticipantIdentity(
                descriptor.PassId,
                descriptor.ImplementationVersion,
                descriptor.StateOwner,
                StandardFloat32PipelinePassContracts.LocalControlInputStateSchemaId,
                StandardFloat32PipelinePassContracts.LocalControlInputStateSchemaVersion);
        }

        public void PrepareAvailabilityRead(int maximumPayloadBytes, System.Collections.Generic.IReadOnlyList<SimulationActorBinding> roster)
        {
            m_Source.PrepareAvailabilityRead(maximumPayloadBytes, roster);
            m_Checkpoint = new LocalControlInputCheckpoint(StateIdentity, m_Source.StatePayloadCapacity, m_Source.StateTextCapacity);
        }
        public void CaptureFrameEvents(ActorId actorId, ReadOnlySpan<SimulationIngress> ingress) => m_Source.CaptureFrameEvents(actorId, ingress);
        public bool HasActionEvent(ActorId actorId, ulong instanceId, string eventId, CanonicalTextValue targetId) => m_Source.HasActionEvent(actorId, instanceId, eventId, targetId);
        public bool HasGameplayEvent(ActorId actorId, string eventId) => m_Source.HasGameplayEvent(actorId, eventId);
        public void RestoreControlInput(ArraySegment<byte> bytes)
        {
            using SimulationPipelinePassStateStorage.Page page = m_StepStorage.BeginWrite();
            page.Writer.WriteRawBytes(bytes.AsSpan());
            m_Source.RestoreState(page.Writer.WrittenSegment.AsMemory(), page.Text.ReceptionResolver);
        }
        public void GetInputCapacity(ActorId actorId, out int values, out int requests) => m_Source.GetInputCapacity(actorId, out values, out requests);
        public ulong ReadInputSequence(ActorId actorId) => m_Source.ReadInputSequence(actorId);
        public SimulationInputValue ReadInputValue(ActorId actorId, CanonicalTextValue id, ProgramInputValueKind kind) => m_Source.ReadInputValue(actorId, id, kind);
        protected override void OnDispose() { m_Source.ReleaseInputFrames(); m_Source.ReleaseAvailabilityInputs(); }

        public SimulationPipelineStateParticipantIdentity StateIdentity { get; }
        public int MaximumPayloadBytes => m_Source.StatePayloadCapacity;
        public int MaximumTextValues => m_Source.StateTextCapacity;
        public SimulationPipelineStepProjectionMode StepProjectionMode => SimulationPipelineStepProjectionMode.Include;

        public void PrepareStepStorage(int snapshotCapacity)
        {
            m_StepStorage = new SimulationPipelinePassStateStorage(StateIdentity, snapshotCapacity, MaximumPayloadBytes, MaximumTextValues);
            m_Restore = new LocalControlInputRestoreTransaction(StateIdentity, m_Source);
        }
        public SimulationPipelinePassStateSnapshot CaptureStepState()
        {
            RequireCaptureOrRestore();
            using SimulationPipelinePassStateStorage.Page page = m_StepStorage.BeginWrite();
            m_Source.WriteState(page.Writer);
            return page.Commit();
        }

        public void Execute(
            SimulationPipelineIngressContext context,
            LocalInputIngressReadPorts readPorts,
            LocalInputIngressWritePorts writePorts)
        {
            RequireExecution();
            if (!ReferenceEquals(m_Source, readPorts.Source))
                throw new InvalidOperationException("Local Control Input Ingress source port changed after activation.");
            var nextTick = new SimulationTick(checked(context.CurrentCompletedTick + 1));
            Float32CommittedActorPoseSnapshot observation = readPorts.CommittedObservation.Read();
            Float32LocalInputFrame frame = readPorts.Source.Read(
                context.Source,
                nextTick,
                readPorts.CharacterRuntime.Runtime.NumericProfile,
                readPorts.CharacterRuntime.Runtime.TickRate,
                readPorts.CharacterRuntime.Runtime.Roster,
                observation);
            writePorts.CanonicalInputs.Write(frame.CanonicalInputs);
            writePorts.TypedIngress.Write(frame.TypedIngress);
        }

        public ISimulationPipelinePassStateCheckpoint CaptureCheckpoint()
        {
            RequireCaptureOrRestore();
            return m_Checkpoint.Begin(m_Source);
        }

        public void WriteCheckpointState(CanonicalWriter writer)
        {
            RequireCaptureOrRestore();
            m_Source.WriteState(writer);
        }

        public SimulationPipelinePassStateSnapshot CaptureState() => CaptureStepState();

        public ISimulationPipelinePassRestoreTransaction PrepareRestore(SimulationPipelinePassStateSnapshot snapshot)
        {
            RequireCaptureOrRestore();
            return m_Restore.Prepare(snapshot);
        }

    }

    sealed class LocalControlInputCheckpoint : ISimulationPipelinePassStateCheckpoint
    {
        IFloat32LocalInputSourcePort m_Source;
        readonly CanonicalWriter m_State;
        readonly CanonicalTextReferences m_Text;
        bool m_Restored;
        Float32InputTraceProgress m_TraceProgress;

        public LocalControlInputCheckpoint(
            SimulationPipelineStateParticipantIdentity participant,
            int payloadCapacity, int maximumTextValues)
        {
            Participant = participant;
            m_State = new CanonicalWriter(new byte[payloadCapacity]);
            m_Text = new CanonicalTextReferences(maximumTextValues, new CanonicalTextStorage(maximumTextValues, payloadCapacity / sizeof(char)));
            m_State.SetTextCapture(m_Text);
        }

        internal LocalControlInputCheckpoint Begin(IFloat32LocalInputSourcePort source)
        {
            if (m_Source != null)
                throw new InvalidOperationException("Local input checkpoint is still held by the outer transaction.");
            m_Text.Reset();
            m_State.Reset();
            try
            {
                source.WriteState(m_State);
            }
            catch
            {
                m_Text.Reset();
                throw;
            }
            m_TraceProgress = Float32CharacterInputTraceModule.CaptureProgress();
            m_Source = source;
            m_Restored = false;
            return this;
        }

        public SimulationPipelineStateParticipantIdentity Participant { get; }

        public void Restore()
        {
            if (m_Source == null)
                throw new ObjectDisposedException(nameof(LocalControlInputCheckpoint));
            if (m_Restored)
                return;
            m_Source.RestoreState(m_State.WrittenSegment.AsMemory(), m_Text);
            m_Source.NotifyStateDisposition(CharacterControlSourceStateDisposition.Discarded);
            Float32CharacterInputTraceModule.RestoreProgress(in m_TraceProgress);
            m_Restored = true;
        }

        public void Dispose()
        {
            if (m_Source == null)
                return;
            try
            {
                if (!m_Restored)
                    m_Source.NotifyStateDisposition(CharacterControlSourceStateDisposition.Committed);
            }
            finally
            {
                m_Text.Reset();
                m_Source = null;
            }
        }
    }

    public sealed class LocalInputIngressReadPorts : ISimulationPipelineReadPortSet
    {
        public LocalInputIngressReadPorts(
            IFloat32LocalInputSourcePort source,
            IFloat32CharacterRuntimePort characterRuntime,
            ICommittedActorPoseReadPort<Float32Vector3, Float32Yaw> committedObservation)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            CommittedObservation = committedObservation ?? throw new ArgumentNullException(nameof(committedObservation));
        }

        public IFloat32LocalInputSourcePort Source { get; }
        public IFloat32CharacterRuntimePort CharacterRuntime { get; }
        public ICommittedActorPoseReadPort<Float32Vector3, Float32Yaw> CommittedObservation { get; }
    }

    public sealed class LocalInputIngressWritePorts : ISimulationPipelineWritePortSet
    {
        public LocalInputIngressWritePorts(
            IExclusiveSimulationPipelineProductWriter<Float32CanonicalInputBatch> canonicalInputs,
            IExclusiveSimulationPipelineProductWriter<Float32TypedIngressBatch> typedIngress)
        {
            CanonicalInputs = canonicalInputs ?? throw new ArgumentNullException(nameof(canonicalInputs));
            TypedIngress = typedIngress ?? throw new ArgumentNullException(nameof(typedIngress));
        }

        public IExclusiveSimulationPipelineProductWriter<Float32CanonicalInputBatch> CanonicalInputs { get; }
        public IExclusiveSimulationPipelineProductWriter<Float32TypedIngressBatch> TypedIngress { get; }
    }

    sealed class LocalControlInputRestoreTransaction : ISimulationPipelinePassRestoreTransaction
    {
        readonly IFloat32LocalInputSourcePort m_Source;
        readonly CanonicalWriter m_Before;
        readonly CanonicalWriter m_Current;
        ReadOnlyMemory<byte> m_Replacement;
        StableHash m_ReplacementHash;
        ICanonicalTextResolver m_ReplacementText;
        readonly CanonicalTextReferences m_BeforeText;
        bool m_Applied;

        public LocalControlInputRestoreTransaction(
            SimulationPipelineStateParticipantIdentity participant,
            IFloat32LocalInputSourcePort source)
        {
            Participant = participant;
            m_Source = source ?? throw new ArgumentNullException(nameof(source));
            m_Before = new CanonicalWriter(new byte[source.StatePayloadCapacity]);
            m_BeforeText = new CanonicalTextReferences(source.StateTextCapacity, new CanonicalTextStorage(source.StateTextCapacity, source.StatePayloadCapacity / sizeof(char)));
            m_Before.SetTextCapture(m_BeforeText);
            m_Current = new CanonicalWriter(new byte[source.StatePayloadCapacity]);
        }

        internal LocalControlInputRestoreTransaction Prepare(SimulationPipelinePassStateSnapshot snapshot)
        {
            m_BeforeText.Reset();
            m_Before.Reset();
            try
            {
                m_Source.WriteState(m_Before);
            }
            catch
            {
                m_BeforeText.Reset();
                throw;
            }
            m_Replacement = snapshot.Payload;
            m_ReplacementHash = snapshot.StateHash;
            m_ReplacementText = snapshot.TextResolver;
            m_Applied = false;
            return this;
        }

        public SimulationPipelineStateParticipantIdentity Participant { get; }

        public void Apply()
        {
            m_Applied = true;
            m_Source.RestoreState(m_Replacement, m_ReplacementText);
        }

        public void ValidateApplied()
        {
            m_Current.Reset();
            m_Source.WriteState(m_Current);
            if (!m_Applied || !SimulationCanonicalPayloadHash.Compute(m_Current.WrittenSpan).Equals(m_ReplacementHash))
            {
                throw new InvalidOperationException("Local Control Input state restore did not reproduce canonical bytes.");
            }
        }

        public void CompleteAfterSessionPublish()
        {
        }

        public void Rollback()
        {
            if (m_Applied)
                m_Source.RestoreState(m_Before.WrittenSegment.AsMemory(), m_BeforeText);
            m_Applied = false;
        }

        public void Dispose()
        {
            m_Replacement = default;
            m_ReplacementText = null;
            m_BeforeText.Reset();
        }
    }
}
