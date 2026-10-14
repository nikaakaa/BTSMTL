using System;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class FixedLocalInputIngressPassRuntimeFactory : IFixedPipelinePassRuntimeFactory
    {
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFixedLocalPipelinePassContracts.CreateFactoryDescriptor(
                StandardFixedLocalPipelinePassContracts.LocalInputIngress);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;

        public IFixedCompiledPipelinePassRuntime Create(FixedPipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new FixedLocalInputIngressReadPorts(
                context.BindSourcePort<IFixedLocalInputSourcePort>(FixedLocalInputSourcePortContract.PortId),
                context.BindTargetPort<IFixedCharacterRuntimePort>(FixedPipelineRuntimePortIds.CharacterRuntime),
                context.BindTargetPort<ICommittedActorPoseReadPort<FixedVector3, FixedYaw>>(FixedPipelineRuntimePortIds.CommittedObservation));
            var writes = new FixedLocalInputIngressWritePorts(
                context.Products.BindExclusiveWriter<FixedCanonicalInputBatch>(SimulationPipelineProducts.CanonicalInputs),
                context.Products.BindExclusiveWriter<FixedTypedIngressBatch>(SimulationPipelineProducts.TypedIngress));
            return new FixedIngressPassRuntimeAdapter<FixedLocalInputIngressReadPorts, FixedLocalInputIngressWritePorts>(
                new FixedLocalInputIngressPassRuntime(context.Pass.Descriptor, reads.Source),
                reads,
                writes);
        }
    }

    public sealed class FixedLocalInputIngressPassRuntime :
        FixedPipelinePassRuntimeBase,
        ISimulationIngressPassRuntime<FixedLocalInputIngressReadPorts, FixedLocalInputIngressWritePorts>,
        ISimulationPipelineStateParticipant, IFixedCommittedInputReadPort,
        ISimulationPipelineStepStateStorage
    {
        readonly IFixedLocalInputSourcePort m_Source;
        FixedLocalControlInputCheckpoint m_Checkpoint;
        SimulationPipelinePassStateStorage m_StepStorage;
        FixedLocalControlInputRestoreTransaction m_Restore;

        public FixedLocalInputIngressPassRuntime(
            SimulationPipelinePassDescriptor descriptor,
            IFixedLocalInputSourcePort source)
            : base(descriptor)
        {
            m_Source = source ?? throw new ArgumentNullException(nameof(source));
            StateIdentity = new SimulationPipelineStateParticipantIdentity(
                descriptor.PassId,
                descriptor.ImplementationVersion,
                descriptor.StateOwner,
                StandardFixedLocalPipelinePassContracts.LocalControlInputStateSchemaId,
                StandardFixedLocalPipelinePassContracts.LocalControlInputStateSchemaVersion);
        }

        public void PrepareAvailabilityRead(System.Collections.Generic.IReadOnlyList<SimulationActorBinding> roster, int inputFrameCapacity)
        {
            m_Source.PrepareAvailabilityRead(roster);
            m_Source.PrepareInputStorage(inputFrameCapacity);
            m_Checkpoint = new FixedLocalControlInputCheckpoint(StateIdentity, m_Source);
        }
        public void CaptureFrameEvents(ActorId actorId, ReadOnlySpan<SimulationIngress> ingress) => m_Source.CaptureFrameEvents(actorId, ingress);
        public bool HasActionEvent(ActorId actorId, ulong instanceId, string eventId, CanonicalTextValue targetId) => m_Source.HasActionEvent(actorId, instanceId, eventId, targetId);
        public bool HasGameplayEvent(ActorId actorId, string eventId) => m_Source.HasGameplayEvent(actorId, eventId);
        public void RestoreControlInput(IValueResources state) => m_Source.RestoreValueSnapshot(state);

        public void RestoreControlSources(IValueResources state) => m_Source.RestoreControlSources(state);
        public ulong ReadInputSequence(ActorId actorId) => m_Source.ReadInputSequence(actorId);
        public SimulationInputValue ReadInputValue(ActorId actorId, CanonicalTextValue id, ProgramInputValueKind kind) => m_Source.ReadInputValue(actorId, id, kind);
        protected override void OnDispose() => m_Source.ReleaseAvailabilityInputs();

        public SimulationPipelineStateParticipantIdentity StateIdentity { get; }
        public SimulationPipelineStepProjectionMode StepProjectionMode => SimulationPipelineStepProjectionMode.Include;

        public void PrepareStepStorage(int snapshotCapacity)
        {
            m_StepStorage = new SimulationPipelinePassStateStorage(StateIdentity, snapshotCapacity, m_Source);
            m_Restore = new FixedLocalControlInputRestoreTransaction(StateIdentity, m_Source);
        }

        public SimulationPipelinePassStateSnapshot CaptureStepState()
        {
            RequireCaptureOrRestore();
            return m_StepStorage.Capture();
        }

        public void Execute(
            SimulationPipelineIngressContext context,
            FixedLocalInputIngressReadPorts readPorts,
            FixedLocalInputIngressWritePorts writePorts)
        {
            RequireExecution();
            if (!ReferenceEquals(m_Source, readPorts.Source))
                throw new InvalidOperationException("Fixed Local input Source port changed after activation.");
            var nextTick = new SimulationTick(checked(context.CurrentCompletedTick + 1));
            FixedLocalInputFrame frame = readPorts.Source.Read(
                context.Source,
                nextTick,
                readPorts.CharacterRuntime.Runtime.TickRate,
                readPorts.CharacterRuntime.Runtime.Roster,
                readPorts.CommittedObservation.Read());
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
            if (m_Source.LocalEnemies.Count != 0)
                throw new NotSupportedException("本地 Root Motion 敌人的动画和行为不支持历史恢复。");
            return m_Restore.Prepare(snapshot);
        }
    }

    public sealed class FixedLocalInputIngressReadPorts : ISimulationPipelineReadPortSet
    {
        public FixedLocalInputIngressReadPorts(
            IFixedLocalInputSourcePort source,
            IFixedCharacterRuntimePort characterRuntime,
            ICommittedActorPoseReadPort<FixedVector3, FixedYaw> committedObservation)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            CommittedObservation = committedObservation ?? throw new ArgumentNullException(nameof(committedObservation));
        }

        public IFixedLocalInputSourcePort Source { get; }
        public IFixedCharacterRuntimePort CharacterRuntime { get; }
        public ICommittedActorPoseReadPort<FixedVector3, FixedYaw> CommittedObservation { get; }
    }

    public sealed class FixedLocalInputIngressWritePorts : ISimulationPipelineWritePortSet
    {
        public FixedLocalInputIngressWritePorts(
            IExclusiveSimulationPipelineProductWriter<FixedCanonicalInputBatch> canonicalInputs,
            IExclusiveSimulationPipelineProductWriter<FixedTypedIngressBatch> typedIngress)
        {
            CanonicalInputs = canonicalInputs ?? throw new ArgumentNullException(nameof(canonicalInputs));
            TypedIngress = typedIngress ?? throw new ArgumentNullException(nameof(typedIngress));
        }

        public IExclusiveSimulationPipelineProductWriter<FixedCanonicalInputBatch> CanonicalInputs { get; }
        public IExclusiveSimulationPipelineProductWriter<FixedTypedIngressBatch> TypedIngress { get; }
    }

    sealed class FixedLocalControlInputCheckpoint : ISimulationPipelinePassStateCheckpoint
    {
        IFixedLocalInputSourcePort m_Source;
        readonly IValueResources m_State;
        bool m_Restored;

        public FixedLocalControlInputCheckpoint(
            SimulationPipelineStateParticipantIdentity participant,
            IFixedLocalInputSourcePort source)
        {
            Participant = participant;
            m_State = source.CreateValueSnapshot();
            m_State.Release();
        }

        internal FixedLocalControlInputCheckpoint Begin(IFixedLocalInputSourcePort source)
        {
            source.CaptureValueSnapshot(m_State);
            m_Source = source;
            m_Restored = false;
            return this;
        }

        public SimulationPipelineStateParticipantIdentity Participant { get; }

        public void Restore()
        {
            m_Source.RestoreValueSnapshot(m_State);
            m_Source.NotifyStateDisposition(FixedCharacterControlSourceStateDisposition.Discarded);
            m_Restored = true;
        }

        public void Dispose()
        {
            try
            {
                if (!m_Restored)
                    m_Source.NotifyStateDisposition(FixedCharacterControlSourceStateDisposition.Committed);
            }
            finally
            {
                m_Source.ReleaseInputFrames();
                m_State.Release();
                m_Source = null;
            }
        }
    }

    sealed class FixedLocalControlInputRestoreTransaction : ISimulationPipelinePassRestoreTransaction
    {
        readonly IFixedLocalInputSourcePort m_Source;
        readonly IValueResources m_Before;
        readonly IValueResources m_Current;
        IValueResources m_Replacement;
        bool m_Applied;

        public FixedLocalControlInputRestoreTransaction(
            SimulationPipelineStateParticipantIdentity participant, IFixedLocalInputSourcePort source)
        {
            Participant = participant;
            m_Source = source;
            m_Before = source.CreateValueSnapshot();
            m_Before.Release();
            m_Current = source.CreateValueSnapshot();
            m_Current.Release();
        }

        public FixedLocalControlInputRestoreTransaction Prepare(SimulationPipelinePassStateSnapshot snapshot)
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
            m_Source.NotifyStateDisposition(FixedCharacterControlSourceStateDisposition.Restored);
        }

        public void ValidateApplied()
        {
            m_Source.CaptureValueSnapshot(m_Current);
            try
            {
                if (!m_Applied || !m_Current.Equals(m_Replacement))
                    throw new InvalidOperationException("Fixed Local Control Source state restore did not reproduce the captured state.");
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
