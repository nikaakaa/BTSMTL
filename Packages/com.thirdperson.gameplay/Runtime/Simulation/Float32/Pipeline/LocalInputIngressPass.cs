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

        public void PrepareAvailabilityRead(System.Collections.Generic.IReadOnlyList<SimulationActorBinding> roster)
        {
            m_Source.PrepareAvailabilityRead(roster);
            m_Checkpoint = new LocalControlInputCheckpoint(StateIdentity, m_Source);
        }
        public void CaptureFrameEvents(ActorId actorId, ReadOnlySpan<SimulationIngress> ingress) => m_Source.CaptureFrameEvents(actorId, ingress);
        public bool HasActionEvent(ActorId actorId, ulong instanceId, string eventId, CanonicalTextValue targetId) => m_Source.HasActionEvent(actorId, instanceId, eventId, targetId);
        public bool HasGameplayEvent(ActorId actorId, string eventId) => m_Source.HasGameplayEvent(actorId, eventId);
        public void RestoreControlInput(IValueResources state) => m_Source.RestoreValueSnapshot(state);
        public void GetInputCapacity(ActorId actorId, out int values, out int requests) => m_Source.GetInputCapacity(actorId, out values, out requests);
        public ulong ReadInputSequence(ActorId actorId) => m_Source.ReadInputSequence(actorId);
        public SimulationInputValue ReadInputValue(ActorId actorId, CanonicalTextValue id, ProgramInputValueKind kind) => m_Source.ReadInputValue(actorId, id, kind);
        protected override void OnDispose() { m_Source.ReleaseInputFrames(); m_Source.ReleaseAvailabilityInputs(); }

        public SimulationPipelineStateParticipantIdentity StateIdentity { get; }
        public SimulationPipelineStepProjectionMode StepProjectionMode => SimulationPipelineStepProjectionMode.Include;

        public void PrepareStepStorage(int snapshotCapacity)
        {
            m_StepStorage = new SimulationPipelinePassStateStorage(StateIdentity, snapshotCapacity, m_Source);
            m_Restore = new LocalControlInputRestoreTransaction(StateIdentity, m_Source);
        }
        public SimulationPipelinePassStateSnapshot CaptureStepState()
        {
            RequireCaptureOrRestore();
            return m_StepStorage.Capture();
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
        readonly IValueResources m_State;
        bool m_Restored;
        Float32InputTraceProgress m_TraceProgress;

        public LocalControlInputCheckpoint(
            SimulationPipelineStateParticipantIdentity participant,
            IFloat32LocalInputSourcePort source)
        {
            Participant = participant;
            m_State = source.CreateValueSnapshot();
            m_State.Release();
        }

        internal LocalControlInputCheckpoint Begin(IFloat32LocalInputSourcePort source)
        {
            source.CaptureValueSnapshot(m_State);
            m_TraceProgress = Float32CharacterInputTraceModule.CaptureProgress();
            m_Source = source;
            m_Restored = false;
            return this;
        }

        public SimulationPipelineStateParticipantIdentity Participant { get; }

        public void Restore()
        {
            m_Source.RestoreValueSnapshot(m_State);
            m_Source.NotifyStateDisposition(CharacterControlSourceStateDisposition.Discarded);
            Float32CharacterInputTraceModule.RestoreProgress(in m_TraceProgress);
            m_Restored = true;
        }

        public void Dispose()
        {
            try
            {
                if (!m_Restored)
                    m_Source.NotifyStateDisposition(CharacterControlSourceStateDisposition.Committed);
            }
            finally
            {
                m_State.Release();
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
        readonly IValueResources m_Before;
        readonly IValueResources m_Current;
        IValueResources m_Replacement;
        bool m_Applied;

        public LocalControlInputRestoreTransaction(
            SimulationPipelineStateParticipantIdentity participant,
            IFloat32LocalInputSourcePort source)
        {
            Participant = participant;
            m_Source = source;
            m_Before = source.CreateValueSnapshot();
            m_Before.Release();
            m_Current = source.CreateValueSnapshot();
            m_Current.Release();
        }

        internal LocalControlInputRestoreTransaction Prepare(SimulationPipelinePassStateSnapshot snapshot)
        {
            m_Source.CaptureValueSnapshot(m_Before);
            m_Replacement = snapshot.State;
            m_Replacement.Retain();
            m_Applied = false;
            return this;
        }

        public SimulationPipelineStateParticipantIdentity Participant { get; }

        public void Apply()
        {
            m_Applied = true;
            m_Source.RestoreValueSnapshot(m_Replacement);
        }

        public void ValidateApplied()
        {
            m_Source.CaptureValueSnapshot(m_Current);
            try
            {
                if (!m_Applied || !m_Current.Equals(m_Replacement))
                    throw new InvalidOperationException("Local Control Input state restore did not reproduce the captured state.");
            }
            finally
            {
                m_Current.Release();
            }
        }

        public void Rollback()
        {
            if (m_Applied)
                m_Source.RestoreValueSnapshot(m_Before);
            m_Applied = false;
        }

        public void Dispose()
        {
            m_Before.Release();
            m_Replacement.Release();
            m_Replacement = null;
        }
    }
}
