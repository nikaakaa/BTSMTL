using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    public sealed class FPLocalInputIngressPassRuntimeFactory : IFPPipelinePassRuntimeFactory
    {
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFPLocalPipelinePassContracts.CreateFactoryDescriptor(
                StandardFPLocalPipelinePassContracts.LocalInputIngress);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;

        public IFPCompiledPipelinePassRuntime Create(FPPipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new FPLocalInputIngressReadPorts(
                context.BindSourcePort<IFPLocalInputSourcePort>(FPLocalInputSourcePortContract.PortId),
                context.BindTargetPort<IFPCharacterRuntimePort>(FPPipelineRuntimePortIds.CharacterRuntime),
                context.BindTargetPort<ICommittedActorPoseReadPort<FPVector3, FP>>(FPPipelineRuntimePortIds.CommittedObservation));
            var writes = new FPLocalInputIngressWritePorts(
                context.Products.BindExclusiveWriter<FPCanonicalInputBatch>(SimulationPipelineProducts.CanonicalInputs),
                context.Products.BindExclusiveWriter<FPTypedIngressBatch>(SimulationPipelineProducts.TypedIngress));
            return new FPIngressPassRuntimeAdapter<FPLocalInputIngressReadPorts, FPLocalInputIngressWritePorts>(
                new FPLocalInputIngressPassRuntime(context.Pass.Descriptor, reads.Source),
                reads,
                writes);
        }
    }

    public sealed class FPLocalInputIngressPassRuntime :
        FPPipelinePassRuntimeBase,
        ISimulationIngressPassRuntime<FPLocalInputIngressReadPorts, FPLocalInputIngressWritePorts>,
        ISimulationPipelineStateParticipant, IFPCommittedInputReadPort,
        ISimulationPipelineStepStateStorage
    {
        readonly IFPLocalInputSourcePort m_Source;
        FPLocalControlInputCheckpoint m_Checkpoint;
        SimulationPipelinePassStateStorage m_StepStorage;
        FPLocalControlInputRestoreTransaction m_Restore;

        public FPLocalInputIngressPassRuntime(
            SimulationPipelinePassDescriptor descriptor,
            IFPLocalInputSourcePort source)
            : base(descriptor)
        {
            m_Source = source ?? throw new ArgumentNullException(nameof(source));
            StateIdentity = new SimulationPipelineStateParticipantIdentity(
                descriptor.PassId,
                descriptor.ImplementationVersion,
                descriptor.StateOwner,
                StandardFPLocalPipelinePassContracts.LocalControlInputStateSchemaId,
                StandardFPLocalPipelinePassContracts.LocalControlInputStateSchemaVersion);
        }

        public void PrepareAvailabilityRead(System.Collections.Generic.IReadOnlyList<SimulationActorBinding> roster, int inputFrameCapacity)
        {
            m_Source.PrepareAvailabilityRead(roster);
            m_Source.PrepareInputStorage(inputFrameCapacity);
            m_Checkpoint = new FPLocalControlInputCheckpoint(StateIdentity, m_Source);
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
            m_Restore = new FPLocalControlInputRestoreTransaction(StateIdentity, m_Source);
        }

        public SimulationPipelinePassStateSnapshot CaptureStepState()
        {
            RequireCaptureOrRestore();
            return m_StepStorage.Capture();
        }

        public void Execute(
            SimulationPipelineIngressContext context,
            FPLocalInputIngressReadPorts readPorts,
            FPLocalInputIngressWritePorts writePorts)
        {
            RequireExecution();
            if (!ReferenceEquals(m_Source, readPorts.Source))
                throw new InvalidOperationException("FP Local input Source port changed after activation.");
            var nextTick = new SimulationTick(checked(context.CurrentCompletedTick + 1));
            FPLocalInputFrame frame = readPorts.Source.Read(
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

    public sealed class FPLocalInputIngressReadPorts : ISimulationPipelineReadPortSet
    {
        public FPLocalInputIngressReadPorts(
            IFPLocalInputSourcePort source,
            IFPCharacterRuntimePort characterRuntime,
            ICommittedActorPoseReadPort<FPVector3, FP> committedObservation)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            CommittedObservation = committedObservation ?? throw new ArgumentNullException(nameof(committedObservation));
        }

        public IFPLocalInputSourcePort Source { get; }
        public IFPCharacterRuntimePort CharacterRuntime { get; }
        public ICommittedActorPoseReadPort<FPVector3, FP> CommittedObservation { get; }
    }

    public sealed class FPLocalInputIngressWritePorts : ISimulationPipelineWritePortSet
    {
        public FPLocalInputIngressWritePorts(
            IExclusiveSimulationPipelineProductWriter<FPCanonicalInputBatch> canonicalInputs,
            IExclusiveSimulationPipelineProductWriter<FPTypedIngressBatch> typedIngress)
        {
            CanonicalInputs = canonicalInputs ?? throw new ArgumentNullException(nameof(canonicalInputs));
            TypedIngress = typedIngress ?? throw new ArgumentNullException(nameof(typedIngress));
        }

        public IExclusiveSimulationPipelineProductWriter<FPCanonicalInputBatch> CanonicalInputs { get; }
        public IExclusiveSimulationPipelineProductWriter<FPTypedIngressBatch> TypedIngress { get; }
    }

    sealed class FPLocalControlInputCheckpoint : ISimulationPipelinePassStateCheckpoint
    {
        IFPLocalInputSourcePort m_Source;
        readonly IValueResources m_State;
        bool m_Restored;

        public FPLocalControlInputCheckpoint(
            SimulationPipelineStateParticipantIdentity participant,
            IFPLocalInputSourcePort source)
        {
            Participant = participant;
            m_State = source.CreateValueSnapshot();
            m_State.Release();
        }

        internal FPLocalControlInputCheckpoint Begin(IFPLocalInputSourcePort source)
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
            m_Source.NotifyStateDisposition(FPCharacterControlSourceStateDisposition.Discarded);
            m_Restored = true;
        }

        public void Dispose()
        {
            try
            {
                if (!m_Restored)
                    m_Source.NotifyStateDisposition(FPCharacterControlSourceStateDisposition.Committed);
            }
            finally
            {
                m_Source.ReleaseInputFrames();
                m_State.Release();
                m_Source = null;
            }
        }
    }

    sealed class FPLocalControlInputRestoreTransaction : ISimulationPipelinePassRestoreTransaction
    {
        readonly IFPLocalInputSourcePort m_Source;
        readonly IValueResources m_Before;
        readonly IValueResources m_Current;
        IValueResources m_Replacement;
        bool m_Applied;

        public FPLocalControlInputRestoreTransaction(
            SimulationPipelineStateParticipantIdentity participant, IFPLocalInputSourcePort source)
        {
            Participant = participant;
            m_Source = source;
            m_Before = source.CreateValueSnapshot();
            m_Before.Release();
            m_Current = source.CreateValueSnapshot();
            m_Current.Release();
        }

        public FPLocalControlInputRestoreTransaction Prepare(SimulationPipelinePassStateSnapshot snapshot)
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
            m_Source.NotifyStateDisposition(FPCharacterControlSourceStateDisposition.Restored);
        }

        public void ValidateApplied()
        {
            m_Source.CaptureValueSnapshot(m_Current);
            try
            {
                if (!m_Applied || !m_Current.Equals(m_Replacement))
                    throw new InvalidOperationException("FP Local Control Source state restore did not reproduce the captured state.");
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
