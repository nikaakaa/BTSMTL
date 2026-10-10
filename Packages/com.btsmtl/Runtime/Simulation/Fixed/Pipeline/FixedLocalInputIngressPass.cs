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

        public void PrepareAvailabilityRead(int maximumPayloadBytes, System.Collections.Generic.IReadOnlyList<SimulationActorBinding> roster)
        {
            m_Source.PrepareAvailabilityRead(maximumPayloadBytes, roster);
            m_Checkpoint = new FixedLocalControlInputCheckpoint(StateIdentity, m_Source.StatePayloadCapacity, m_Source.StateTextCapacity);
            m_Restore = new FixedLocalControlInputRestoreTransaction(StateIdentity, m_Source);
        }
        public void CaptureFrameEvents(ActorId actorId, ReadOnlySpan<SimulationIngress> ingress) => m_Source.CaptureFrameEvents(actorId, ingress);
        public bool HasActionEvent(ActorId actorId, ulong instanceId, string eventId, CanonicalTextValue targetId) => m_Source.HasActionEvent(actorId, instanceId, eventId, targetId);
        public bool HasGameplayEvent(ActorId actorId, string eventId) => m_Source.HasGameplayEvent(actorId, eventId);
        public void RestoreControlInput(ArraySegment<byte> bytes)
        {
            using SimulationPipelinePassStateStorage.Page page = m_StepStorage.BeginWrite();
            page.Writer.WriteRawBytes(bytes.AsSpan());
            m_Source.RestoreState(page.Writer.WrittenSegment, page.Text.ReceptionResolver);
        }

        public void RestoreControlSources(ArraySegment<byte> bytes)
        {
            using SimulationPipelinePassStateStorage.Page page = m_StepStorage.BeginWrite();
            page.Writer.WriteRawBytes(bytes.AsSpan());
            m_Source.RestoreControlSources(page.Writer.WrittenSegment, page.Text.ReceptionResolver);
        }
        public ulong ReadInputSequence(ActorId actorId) => m_Source.ReadInputSequence(actorId);
        public SimulationInputValue ReadInputValue(ActorId actorId, CanonicalTextValue id, ProgramInputValueKind kind) => m_Source.ReadInputValue(actorId, id, kind);
        protected override void OnDispose() => m_Source.ReleaseAvailabilityInputs();

        public SimulationPipelineStateParticipantIdentity StateIdentity { get; }
        public int MaximumPayloadBytes => m_Source.StatePayloadCapacity;
        public int MaximumTextValues => m_Source.StateTextCapacity;
        public SimulationPipelineStepProjectionMode StepProjectionMode => SimulationPipelineStepProjectionMode.Include;

        public void PrepareStepStorage(int snapshotCapacity)
        {
            m_Source.PrepareInputStorage(snapshotCapacity);
            m_StepStorage = new SimulationPipelinePassStateStorage(StateIdentity, snapshotCapacity, MaximumPayloadBytes, MaximumTextValues);
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

        public void WriteCheckpointState(CanonicalWriter writer)
        {
            RequireCaptureOrRestore();
            m_Source.WriteState(writer);
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
        readonly CanonicalWriter m_State;
        readonly CanonicalTextReferences m_Text;
        bool m_Restored;

        public FixedLocalControlInputCheckpoint(
            SimulationPipelineStateParticipantIdentity participant,
            int payloadCapacity, int maximumTextValues)
        {
            Participant = participant;
            m_State = new CanonicalWriter(new byte[payloadCapacity]);
            m_Text = new CanonicalTextReferences(maximumTextValues, new CanonicalTextStorage(maximumTextValues, payloadCapacity / sizeof(char)));
            m_State.SetTextCapture(m_Text);
        }

        internal FixedLocalControlInputCheckpoint Begin(IFixedLocalInputSourcePort source)
        {
            if (m_Source != null)
                throw new InvalidOperationException("Fixed Local input checkpoint is still held by the outer transaction.");
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
            m_Source = source;
            m_Restored = false;
            return this;
        }

        public SimulationPipelineStateParticipantIdentity Participant { get; }

        public void Restore()
        {
            if (m_Source == null)
                throw new ObjectDisposedException(nameof(FixedLocalControlInputCheckpoint));
            if (m_Restored)
                return;
            m_Source.RestoreState(m_State.WrittenSegment, m_Text);
            m_Source.NotifyStateDisposition(FixedCharacterControlSourceStateDisposition.Discarded);
            m_Restored = true;
        }

        public void Dispose()
        {
            if (m_Source == null)
                return;
            try
            {
                if (!m_Restored)
                    m_Source.NotifyStateDisposition(FixedCharacterControlSourceStateDisposition.Committed);
            }
            finally
            {
                m_Source.ReleaseInputFrames();
                m_Text.Reset();
                m_Source = null;
            }
        }
    }

    sealed class FixedLocalControlInputRestoreTransaction : ISimulationPipelinePassRestoreTransaction
    {
        readonly IFixedLocalInputSourcePort m_Source;
        readonly CanonicalWriter m_Before;
        ArraySegment<byte> m_Replacement;
        StableHash m_ReplacementHash;
        ICanonicalTextResolver m_ReplacementText;
        readonly CanonicalTextReferences m_BeforeText;
        readonly CanonicalWriter m_Current;
        bool m_Applied;

        public FixedLocalControlInputRestoreTransaction(
            SimulationPipelineStateParticipantIdentity participant, IFixedLocalInputSourcePort source)
        {
            Participant = participant;
            m_Source = source;
            m_Before = new CanonicalWriter(new byte[source.StatePayloadCapacity]);
            m_BeforeText = new CanonicalTextReferences(source.StateTextCapacity, new CanonicalTextStorage(source.StateTextCapacity, source.StatePayloadCapacity / sizeof(char)));
            m_Before.SetTextCapture(m_BeforeText);
            m_Current = new CanonicalWriter(new byte[source.StatePayloadCapacity]);
        }

        public FixedLocalControlInputRestoreTransaction Prepare(SimulationPipelinePassStateSnapshot snapshot)
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
            System.Runtime.InteropServices.MemoryMarshal.TryGetArray(snapshot.Payload, out m_Replacement);
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
            m_Source.NotifyStateDisposition(FixedCharacterControlSourceStateDisposition.Restored);
        }

        public void ValidateApplied()
        {
            if (!m_Applied || !MatchesReplacement())
            {
                throw new InvalidOperationException("Fixed Local Control Source state restore did not reproduce canonical bytes.");
            }
        }

        public void CompleteAfterSessionPublish()
        {
        }

        public void Rollback()
        {
            if (m_Applied)
                m_Source.RestoreState(m_Before.WrittenSegment, m_BeforeText);
            m_Applied = false;
        }

        public void Dispose()
        {
            m_Replacement = default;
            m_ReplacementText = null;
            m_BeforeText.Reset();
            m_Before.Reset();
        }

        bool MatchesReplacement()
        {
            m_Current.Reset();
            m_Source.WriteState(m_Current);
            return m_Current.ComputeHash().Equals(m_ReplacementHash);
        }
    }
}
