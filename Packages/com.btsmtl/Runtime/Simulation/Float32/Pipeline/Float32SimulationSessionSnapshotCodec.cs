using System;
using System.IO;

namespace ThirdPersonSimulation
{
    public interface IFloat32SimulationSessionSnapshotCodec
    {
        SimulationComponentIdentity Identity { get; }
        void PrepareCheckpointStorage(SimulationSessionCompositionDescriptor descriptor, Float32CharacterRuntime runtime,
            System.Collections.Generic.IReadOnlyList<ISimulationPipelineStateParticipant> participants,
            in GameplayEffectSessionCapacity capacity, WorldSimulationState publishedWorld, int stepSnapshotCapacity);
        SimulationWorldSnapshot CaptureStepWorldSnapshot(in SimulationWorldStateSet state);
        SimulationSessionCheckpoint CaptureCheckpoint(in SimulationWorldStateSet state,
            SimulationPipelinePassStateSnapshot[] recordedParticipants = null);
        Float32SimulationSessionSnapshot ReadCheckpoint(SimulationSessionCheckpoint checkpoint);
        void DisposeCheckpointStorage();
        void RequireRestore(
            SimulationSessionCompositionDescriptor descriptor,
            Float32CharacterRuntime characterRuntime,
            ICharacterWorldSolver solver,
            SimulationRestoreDirective directive,
            Float32SimulationSessionSnapshot snapshot);
    }

    public sealed class Float32SimulationSessionSnapshotCodec : IFloat32SimulationSessionSnapshotCodec
    {
        const uint Magic = 0x53534643;
        const int Version = 5;

        SimulationSessionCheckpointPayloadStorage m_Checkpoints;
        CheckpointDescriptors[] m_Descriptors;
        SimulationSessionCompositionDescriptor m_Composition;
        Float32CharacterRuntime m_Runtime;
        ISimulationPipelineStateParticipant[] m_Participants;
        CanonicalWriter m_HashWriter;
        CanonicalWriter m_CanonicalWriter;
        bool m_Deterministic;
        SimulationWorldSnapshotStorage m_StepSnapshots;

        sealed class CheckpointDescriptors
        {
            internal CheckpointDescriptors(SimulationSessionCompositionDescriptor composition,
                Float32CharacterRuntime runtime, ISimulationPipelineStateParticipant[] participants, CanonicalWriter hashWriter, WorldSimulationState publishedWorld)
            {
                var actors = new SimulationActorSnapshot[runtime.Roster.Count];
                for (int index = 0; index < actors.Length; index++)
                    actors[index] = new SimulationActorSnapshot(runtime.Roster[index], hashWriter);
                World = new SimulationWorldSnapshot(actors, hashWriter, publishedWorld);
                Participants = new SimulationPipelinePassStateSnapshot[participants.Length];
                for (int index = 0; index < participants.Length; index++)
                    Participants[index] = SimulationPipelinePassStateSnapshot.FromPreparedIdentity(participants[index].StateIdentity);
                Pipeline = new SimulationPipelineStateSnapshot(composition.Pipeline, composition.ExecutionBackend, Participants);
                Session = new Float32SimulationSessionSnapshot(composition.Identity);
                WorldReader = new CanonicalReader(Array.Empty<byte>());
            }

            internal readonly SimulationWorldSnapshot World;
            internal readonly SimulationPipelinePassStateSnapshot[] Participants;
            internal readonly SimulationPipelineStateSnapshot Pipeline;
            internal readonly Float32SimulationSessionSnapshot Session;
            internal readonly CanonicalReader WorldReader;

            internal void Clear()
            {
                World.ClearPreparedPayload();
                for (int index = 0; index < Participants.Length; index++)
                    Participants[index].ClearPreparedPayload();
                WorldReader.SetTextResolver(null);
                WorldReader.Reset(Array.Empty<byte>());
            }
        }

        public Float32SimulationSessionSnapshotCodec(SimulationComponentIdentity identity)
        {
            if (!identity.IsValid || identity.Role != SimulationComponentRole.SnapshotCodec)
                throw new ArgumentException("Snapshot codec identity is invalid.", nameof(identity));
            Identity = identity;
        }

        public SimulationComponentIdentity Identity { get; }

        public void PrepareCheckpointStorage(
            SimulationSessionCompositionDescriptor descriptor, Float32CharacterRuntime runtime,
            System.Collections.Generic.IReadOnlyList<ISimulationPipelineStateParticipant> participants,
            in GameplayEffectSessionCapacity capacity, WorldSimulationState publishedWorld, int stepSnapshotCapacity)
        {
            m_Composition = descriptor;
            m_Runtime = runtime;
            if (!descriptor.CapturesState)
                return;
            int detachedTextPayloads = 0;
            for (int actor = 0; actor < runtime.Roster.Count; actor++)
            {
                SimulationActorBinding binding = runtime.Roster[actor];
                binding.PrepareCheckpointControlContract(runtime.ControlModules.RequireContract(binding.ControlRuntimeBinding.ModuleId));
                binding.RuntimeStatePages.PrepareDecodeLayout(binding.TimelineRuntime, descriptor.MaximumSessionCheckpointPayloadBytes);
                binding.RuntimeStatePages.PrepareDecodeStorage(capacity.DecodedStatePageCount);
                detachedTextPayloads = checked(detachedTextPayloads + runtime.Roster[actor].MaximumDetachedCheckpointTextPayloads);
            }
            if (descriptor.MaximumSessionCheckpointPayloadBytes > 0)
            {
                var encoding = new SimulationCharacterStatePayloadStorage(
                    checked(capacity.StatePageCount + runtime.Roster.Count), descriptor.MaximumSessionCheckpointPayloadBytes,
                    Float32CharacterRuntimeStateCodec.HashPrefixBytes, runtime.Roster.Count);
                for (int actor = 0; actor < runtime.Roster.Count; actor++)
                {
                    runtime.Roster[actor].RuntimeStatePages.EncodingStorage = encoding;
                }
            }
            m_Checkpoints = new SimulationSessionCheckpointPayloadStorage(descriptor, in capacity,
                detachedTextPayloads, stepSnapshotCapacity);
            m_Participants = new ISimulationPipelineStateParticipant[participants.Count];
            for (int index = 0; index < participants.Count; index++)
                m_Participants[index] = participants[index];
            Array.Sort(m_Participants, (left, right) => left.StateIdentity.PassId.CompareTo(right.StateIdentity.PassId));
            m_Descriptors = new CheckpointDescriptors[m_Checkpoints.Count];
            m_HashWriter = new CanonicalWriter(new byte[checked(descriptor.MaximumSessionCheckpointPayloadBytes +
                Float32CharacterRuntimeStateCodec.HashPrefixBytes)]);
            m_CanonicalWriter = new CanonicalWriter(new byte[descriptor.MaximumSessionCheckpointPayloadBytes]);
            m_Deterministic = runtime.NumericProfile.DeterministicReplay &&
                (descriptor.SolverCapabilities & WorldCapability.DeterministicReplay) != 0;
            for (int actor = 0; m_Deterministic && actor < runtime.Roster.Count; actor++)
                for (int ability = 0; m_Deterministic && ability < runtime.Roster[actor].AbilityInstallations.Installations.Count; ability++)
                {
                    Float32GameplayAbilityExecutionData data = runtime.Roster[actor].AbilityInstallations.Installations[ability].Data;
                    m_Deterministic &= data.NumericProfile.DeterministicReplay && data.Capabilities.HasGameplayCapability("DeterministicReplay");
                }
            m_StepSnapshots = new SimulationWorldSnapshotStorage(stepSnapshotCapacity, m_Checkpoints.ByteStorage,
                runtime, m_HashWriter, publishedWorld);
            for (int index = 0; index < m_Descriptors.Length; index++)
            {
                var descriptors = new CheckpointDescriptors(descriptor, runtime, m_Participants, m_HashWriter, publishedWorld);
                m_Descriptors[index] = descriptors;
                m_Checkpoints[index].ClearDescriptors = descriptors.Clear;
            }
        }

        public SimulationWorldSnapshot CaptureStepWorldSnapshot(in SimulationWorldStateSet state)
        {
            SimulationWorldSnapshotStorage.Page page = m_StepSnapshots.Acquire();
            try
            {
                return SimulationWorldSnapshotFactory.Capture(m_Runtime, new SimulationTick(state.LastCompletedTick),
                    state.ActorArray, state.WorldState, page.Writer, page.Snapshot, page.Text, m_Deterministic);
            }
            catch
            {
                page.Release();
                throw;
            }
        }

        public SimulationSessionCheckpoint CaptureCheckpoint(
            in SimulationWorldStateSet state, SimulationPipelinePassStateSnapshot[] recordedParticipants = null)
        {
            if (!m_Composition.CapturesState)
                throw new InvalidOperationException("当前会话未准备历史采集或网络状态保存。");
            SimulationSessionCheckpointPayloadStorage.Page page = m_Checkpoints.Acquire();
            CheckpointDescriptors descriptors = m_Descriptors[page.Index];
            CanonicalWriter writer = page.Writer;
            try
            {
                writer.WriteUInt32(Magic);
                writer.WriteInt32(Version);
                long hashPosition = writer.Position;
                writer.WriteHash(m_Composition.Identity);
                writer.WriteHash(m_Composition.Identity);
                SimulationWorldSnapshotCodec.CapturePrepared(writer, m_Runtime, new SimulationTick(state.LastCompletedTick),
                    state.Actors, state.WorldState, m_Deterministic, descriptors.World, page.Text);
                WritePreparedPipeline(writer, descriptors, state.LastCompletedTick, recordedParticipants, page.Text);
                descriptors.Session.SetPrepared(descriptors.World, descriptors.Pipeline, m_HashWriter);
                long end = writer.Position;
                writer.Position = hashPosition;
                writer.WriteHash(descriptors.Session.SnapshotHash);
                writer.Position = end;
                page.PreparedCheckpoint.CommitPayload(page, descriptors.Session.Tick, descriptors.Session.SnapshotHash);
                return page.PreparedCheckpoint;
            }
            catch
            {
                page.Release();
                throw;
            }
        }

        void WritePreparedPipeline(CanonicalWriter writer, CheckpointDescriptors descriptors, ulong tick,
            SimulationPipelinePassStateSnapshot[] recordedParticipants, ICanonicalTextResolver text)
        {
            long hashPosition = writer.Position;
            writer.WriteHash(m_Composition.Pipeline.Hash);
            writer.WriteText(m_Composition.Pipeline.Id.Value);
            writer.WriteText(m_Composition.Pipeline.Revision.Value);
            writer.WriteInt32(m_Composition.Pipeline.SchemaVersion.Value);
            writer.WriteHash(m_Composition.Pipeline.Hash);
            WriteComponent(writer, m_Composition.ExecutionBackend);
            writer.WriteUInt64(tick);
            writer.WriteInt32(m_Participants.Length);
            for (int index = 0; index < m_Participants.Length; index++)
            {
                SimulationPipelineStateParticipantIdentity identity = m_Participants[index].StateIdentity;
                writer.WriteText(identity.PassId.Value);
                writer.WriteText(identity.ImplementationVersion.Value);
                writer.WriteText(identity.StateOwner);
                writer.WriteText(identity.StateSchemaId);
                writer.WriteInt32(identity.StateSchemaVersion);
                long participantHashPosition = writer.Position;
                writer.WriteHash(m_Composition.Pipeline.Hash);
                long prefix = writer.BeginLengthPrefixedBlock();
                int start = checked((int)writer.Position);
                if (recordedParticipants == null)
                    m_Participants[index].WriteCheckpointState(writer);
                else
                {
                    SimulationPipelinePassStateSnapshot recorded = recordedParticipants[index];
                    writer.WriteRawBytes(recorded.Payload.Span);
                }
                int end = checked((int)writer.Position);
                StableHash hash = SimulationCanonicalPayloadHash.Compute(writer.WrittenSpan.Slice(start, end - start));
                writer.EndLengthPrefixedBlock(prefix);
                writer.Position = participantHashPosition;
                writer.WriteHash(hash);
                writer.Position = end;
                descriptors.Participants[index].SetPreparedPayload(writer.WrittenSegment.Array.AsMemory(start, end - start), hash, text);
            }
            descriptors.Pipeline.SetPreparedTick(tick, m_HashWriter);
            long pipelineEnd = writer.Position;
            writer.Position = hashPosition;
            writer.WriteHash(descriptors.Pipeline.SnapshotHash);
            writer.Position = pipelineEnd;
        }

        public Float32SimulationSessionSnapshot ReadCheckpoint(SimulationSessionCheckpoint checkpoint)
        {
            SimulationSessionCheckpointPayloadStorage.Page page = checkpoint.PayloadPage ?? m_Checkpoints.Receive(checkpoint);
            CheckpointDescriptors descriptors = m_Descriptors[page.Index];
            ICanonicalTextResolver resolver = page.Text;
            CanonicalReader reader = page.Reader;
            reader.Reset(checkpoint.Payload);
            reader.SetTextResolver(resolver);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("Float32 Session snapshot header is invalid.");
            StableHash expectedHash = reader.ReadHash();
            var composition = SimulationSessionCompositionIdentity.CanonicalFromHash(reader.ReadHash());
            if (!composition.Equals(m_Composition.Identity))
                throw new InvalidDataException("Session snapshot composition does not match the prepared checkpoint owner.");
            ArraySegment<byte> world = reader.ReadBytesSegment();
            SimulationWorldSnapshotCodec.ReadPrepared(world.Array.AsMemory(world.Offset, world.Count),
                descriptors.WorldReader, descriptors.World, resolver);
            ReadPreparedPipeline(reader, descriptors, resolver);
            reader.RequireComplete();
            descriptors.Session.SetPrepared(descriptors.World, descriptors.Pipeline, m_HashWriter);
            if (!descriptors.Session.SnapshotHash.Equals(expectedHash))
                throw new InvalidDataException("Float32 Session snapshot hash is invalid.");
            m_CanonicalWriter.Reset();
            WriteCanonical(m_CanonicalWriter, descriptors.Session);
            if (!m_CanonicalWriter.ContentEquals(checkpoint.Payload.Span))
                throw new InvalidDataException("Float32 Session snapshot is not canonical.");
            return descriptors.Session;
        }

        void ReadPreparedPipeline(CanonicalReader reader, CheckpointDescriptors descriptors, ICanonicalTextResolver resolver)
        {
            StableHash expectedHash = reader.ReadHash();
            reader.RequireText(m_Composition.Pipeline.Id.Value);
            reader.RequireText(m_Composition.Pipeline.Revision.Value);
            int schemaVersion = reader.ReadInt32();
            StableHash pipelineHash = reader.ReadHash();
            RequireComponent(reader, m_Composition.ExecutionBackend);
            ulong tick = reader.ReadUInt64();
            int count = reader.ReadInt32();
            if (schemaVersion != m_Composition.Pipeline.SchemaVersion.Value ||
                !pipelineHash.Equals(m_Composition.Pipeline.Hash.Value) || count != descriptors.Participants.Length)
                throw new InvalidDataException("Pipeline snapshot does not match the prepared checkpoint owner.");
            for (int index = 0; index < count; index++)
            {
                SimulationPipelineStateParticipantIdentity identity = m_Participants[index].StateIdentity;
                reader.RequireText(identity.PassId.Value);
                reader.RequireText(identity.ImplementationVersion.Value);
                reader.RequireText(identity.StateOwner);
                reader.RequireText(identity.StateSchemaId);
                if (reader.ReadInt32() != identity.StateSchemaVersion)
                    throw new InvalidDataException("Checkpoint participant identity does not match the compiled Pipeline.");
                StableHash expectedStateHash = reader.ReadHash();
                ArraySegment<byte> payload = reader.ReadBytesSegment();
                StableHash stateHash = SimulationCanonicalPayloadHash.Compute(payload.AsSpan());
                if (!stateHash.Equals(expectedStateHash))
                    throw new InvalidDataException("Checkpoint participant payload hash is invalid.");
                descriptors.Participants[index].SetPreparedPayload(payload.Array.AsMemory(payload.Offset, payload.Count), stateHash, resolver);
            }
            descriptors.Pipeline.SetPreparedTick(tick, m_HashWriter);
            if (!descriptors.Pipeline.SnapshotHash.Equals(expectedHash))
                throw new InvalidDataException("Checkpoint Pipeline snapshot hash is invalid.");
        }

        public void DisposeCheckpointStorage()
        {
            if (!m_Composition.CapturesState)
                return;
            for (int actor = 0; actor < m_Runtime.Roster.Count; actor++)
                m_Runtime.Roster[actor].RuntimeStatePages.ReleaseEncodingStorage();
            m_Checkpoints.Dispose();
            m_Checkpoints = null;
            m_Descriptors = null;
            m_Participants = null;
            m_HashWriter = null;
            m_CanonicalWriter = null;
            m_StepSnapshots = null;
        }

        static void WriteCanonical(CanonicalWriter writer, Float32SimulationSessionSnapshot snapshot)
        {
            writer.WriteUInt32(Magic);
            writer.WriteInt32(Version);
            writer.WriteHash(snapshot.SnapshotHash);
            writer.WriteHash(snapshot.CompositionIdentity);
            SimulationWorldSnapshotCodec.WriteLengthPrefixed(writer, snapshot.World);
            WritePipeline(writer, snapshot.Pipeline);
        }

        public void RequireRestore(
            SimulationSessionCompositionDescriptor descriptor,
            Float32CharacterRuntime characterRuntime,
            ICharacterWorldSolver solver,
            SimulationRestoreDirective directive,
            Float32SimulationSessionSnapshot snapshot)
        {
            if (descriptor == null || characterRuntime == null || solver == null || directive == null || snapshot == null)
                throw new ArgumentNullException("Restore validation requires complete Session dependencies.");
            if (!snapshot.CompositionIdentity.Equals(descriptor.Identity) ||
                snapshot.Tick != directive.Tick || !snapshot.SnapshotHash.Equals(directive.SnapshotHash) ||
                !snapshot.World.GameplayContentHash.Equals(characterRuntime.GameplayContentHash) ||
                !directive.GameplayContentHash.Equals(characterRuntime.GameplayContentHash) ||
                !snapshot.Pipeline.Pipeline.Equals(descriptor.Pipeline) ||
                !directive.PipelineHash.Equals(descriptor.Pipeline.Hash) ||
                !snapshot.Pipeline.Backend.Equals(descriptor.ExecutionBackend) ||
                !CanonicalTextValue.Equals(directive.BackendId, descriptor.ExecutionBackend.ComponentId, StringComparison.Ordinal) ||
                !CanonicalTextValue.Equals(directive.BackendSemanticVersion, descriptor.ExecutionBackend.SemanticVersion, StringComparison.Ordinal) ||
                !snapshot.World.SolverId.Equals(solver.Descriptor.ImplementationId) ||
                !CanonicalTextValue.Equals(snapshot.World.SolverVersion, solver.Descriptor.Version, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Restore snapshot identity does not match the active Session composition.");
            }
        }

        static void WritePipeline(CanonicalWriter writer, SimulationPipelineStateSnapshot snapshot)
        {
            writer.WriteHash(snapshot.SnapshotHash);
            writer.WriteText(snapshot.Pipeline.Id.Value);
            writer.WriteText(snapshot.Pipeline.Revision.Value);
            writer.WriteInt32(snapshot.Pipeline.SchemaVersion.Value);
            writer.WriteHash(snapshot.Pipeline.Hash);
            WriteComponent(writer, snapshot.Backend);
            writer.WriteUInt64(snapshot.LastCompletedTick);
            writer.WriteInt32(snapshot.Participants.Count);
            for (int i = 0; i < snapshot.Participants.Count; i++)
            {
                SimulationPipelinePassStateSnapshot participant = snapshot.Participants[i];
                writer.WriteText(participant.PassId.Value);
                writer.WriteText(participant.ImplementationVersion.Value);
                writer.WriteText(participant.StateOwner);
                writer.WriteText(participant.StateSchemaId);
                writer.WriteInt32(participant.StateSchemaVersion);
                writer.WriteHash(participant.StateHash);
                writer.WriteBytes(participant.Payload.Span);
            }
        }

        static void WriteComponent(CanonicalWriter writer, SimulationComponentIdentity identity)
        {
            writer.WriteByte((byte)identity.Role);
            writer.WriteText(identity.ComponentId);
            writer.WriteText(identity.SemanticVersion);
            writer.WriteHash(identity.ConfigurationHash);
        }

        static void RequireComponent(CanonicalReader reader, SimulationComponentIdentity identity)
        {
            if (reader.ReadByte() != (byte)identity.Role)
                throw new InvalidDataException("Checkpoint execution backend role does not match its compiled composition.");
            reader.RequireText(identity.ComponentId);
            reader.RequireText(identity.SemanticVersion);
            if (!reader.ReadHash().Equals(identity.ConfigurationHash))
                throw new InvalidDataException("Checkpoint execution backend configuration does not match its compiled composition.");
        }
    }
}
