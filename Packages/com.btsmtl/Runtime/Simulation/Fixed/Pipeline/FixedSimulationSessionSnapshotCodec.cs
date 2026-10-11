using System;
using System.IO;
using ThirdPersonSimulation;
namespace ThirdPersonSimulation.Fixed
{
    public interface IFixedSimulationSessionSnapshotCodec
    {
        SimulationComponentIdentity Identity { get; }
        void PrepareCheckpointStorage(SimulationSessionCompositionDescriptor descriptor, FixedCharacterRuntime runtime,
            System.Collections.Generic.IReadOnlyList<ISimulationPipelineStateParticipant> participants,
            in GameplayEffectSessionCapacity capacity, WorldSimulationState publishedWorld);
        SimulationSessionCheckpoint CaptureCheckpoint(in SimulationWorldStateSet state,
            SimulationPipelinePassStateSnapshot[] recordedParticipants = null);
        FixedSimulationSessionSnapshot ReadCheckpoint(SimulationSessionCheckpoint checkpoint);
        void CompleteCheckpointReception(SimulationSessionCheckpoint checkpoint);
        void DisposeCheckpointStorage();
        FixedSimulationSessionSnapshotStorage PrepareStepSnapshotStorage(int retainedSnapshots, int candidateSnapshots);
        FixedSimulationSessionSnapshot CaptureStepSnapshot(in SimulationWorldStateSet state);
        ReadOnlyMemory<byte> Write(FixedSimulationSessionSnapshot snapshot);
        FixedSimulationSessionSnapshot Read(ReadOnlySpan<byte> bytes);
        void RequireRestore(
            SimulationSessionCompositionDescriptor descriptor,
            FixedCharacterRuntime characterRuntime,
            ICharacterWorldSolver solver,
            SimulationRestoreDirective directive,
            FixedSimulationSessionSnapshot snapshot);
    }

    public sealed class FixedSimulationSessionSnapshotCodec : IFixedSimulationSessionSnapshotCodec
    {
        const uint Magic = 0x53534643;
        const int Version = 6;

        SimulationSessionCheckpointPayloadStorage m_Checkpoints;
        CheckpointDescriptors[] m_Descriptors;
        SimulationSessionCompositionDescriptor m_Composition;
        FixedCharacterRuntime m_Runtime;
        ISimulationPipelineStateParticipant[] m_Participants;
        CanonicalWriter m_HashWriter;
        CanonicalWriter m_CanonicalWriter;
        bool m_Deterministic;
        FixedSimulationSessionSnapshotStorage m_StepSnapshots;
        GameplayEffectSessionCapacity m_SessionCapacity;
        WorldSimulationState m_PublishedWorld;
        int m_MaximumDetachedTextReferences;
        int m_MaximumDetachedTextPayloads;
        int m_MaximumTextValuesPerPayload;

        internal sealed class CheckpointDescriptors
        {
            internal CheckpointDescriptors(SimulationSessionCompositionDescriptor composition,
                FixedCharacterRuntime runtime, ISimulationPipelineStateParticipant[] participants, CanonicalWriter hashWriter, WorldSimulationState publishedWorld)
            {
                RuntimeParticipants = participants;
                var actors = new SimulationActorSnapshot[runtime.Roster.Count];
                for (int index = 0; index < actors.Length; index++)
                    actors[index] = new SimulationActorSnapshot(runtime.Roster[index], hashWriter);
                World = new SimulationWorldSnapshot(actors, hashWriter, publishedWorld);
                Participants = new SimulationPipelinePassStateSnapshot[participants.Length];
                for (int index = 0; index < participants.Length; index++)
                    Participants[index] = SimulationPipelinePassStateSnapshot.FromPreparedIdentity(participants[index].StateIdentity);
                Pipeline = new SimulationPipelineStateSnapshot(composition.Pipeline, composition.ExecutionBackend, Participants);
                Session = new FixedSimulationSessionSnapshot(composition.Identity);
                WorldReader = new CanonicalReader(Array.Empty<byte>());
            }

            internal readonly ISimulationPipelineStateParticipant[] RuntimeParticipants;
            internal readonly SimulationWorldSnapshot World;
            internal readonly SimulationPipelinePassStateSnapshot[] Participants;
            internal readonly SimulationPipelineStateSnapshot Pipeline;
            internal readonly FixedSimulationSessionSnapshot Session;
            internal readonly CanonicalReader WorldReader;

            internal void Clear()
            {
                World.ClearPreparedPayload();
                for (int index = 0; index < Participants.Length; index++)
                    Participants[index].ClearPreparedPayload();
                WorldReader.SetTextResolver(null);
                WorldReader.Reset(Array.Empty<byte>());
                Session.ClearStorage();
            }
        }

        public FixedSimulationSessionSnapshotCodec(SimulationComponentIdentity identity)
        {
            if (!identity.IsValid || identity.Role != SimulationComponentRole.SnapshotCodec)
                throw new ArgumentException("Snapshot codec identity is invalid.", nameof(identity));
            Identity = identity;
        }

        public SimulationComponentIdentity Identity { get; }

        public void PrepareCheckpointStorage(
            SimulationSessionCompositionDescriptor descriptor, FixedCharacterRuntime runtime,
            System.Collections.Generic.IReadOnlyList<ISimulationPipelineStateParticipant> participants,
            in GameplayEffectSessionCapacity capacity, WorldSimulationState publishedWorld)
        {
            m_Composition = descriptor;
            m_Runtime = runtime;
            m_SessionCapacity = capacity;
            m_PublishedWorld = publishedWorld;
            for (int actor = 0; actor < runtime.Roster.Count; actor++)
            {
                runtime.Roster[actor].PrepareCheckpointControlSchema(
                    runtime.ControlModules.RequireContract(runtime.Roster[actor].ControlRuntimeBinding.ModuleId));
                runtime.Roster[actor].TimelineSnapshotPages.PrepareDecodeLayout(runtime.Roster[actor].TimelineRuntime,
                    descriptor.MaximumSessionCheckpointPayloadBytes);
            }
            long scalarTextValues = checked(12 + 2 * runtime.Roster.Count + publishedWorld.Bodies.Count);
            long exitTextValues = 0;
            long treeTextValues = 0;
            var actorTextValues = new int[runtime.Roster.Count];
            for (int index = 0; index < participants.Count; index++)
                scalarTextValues = checked(scalarTextValues + 4 + participants[index].MaximumTextValues);
            for (int actor = 0; actor < runtime.Roster.Count; actor++)
            {
                SimulationActorBinding binding = runtime.Roster[actor];
                AbilityTimelineSnapshotDecodeLayout timeline = binding.TimelineSnapshotPages.DecodeLayout;
                long actorScalars = checked(
                    FixedCharacterRuntimeStateCodec.MaximumTextValues(binding, binding.CheckpointControlSchema, 0) +
                    timeline.ScalarTextValues(binding.DeclaredCheckpointIdentityCount));
                actorTextValues[actor] = SimulationSessionCheckpointPayloadLayout.MaximumNonEmptyTextValues(
                    descriptor.MaximumSessionCheckpointPayloadBytes, actorScalars, timeline.ExitTextValues, timeline.TreeTextValues);
                scalarTextValues = checked(scalarTextValues + actorScalars);
                exitTextValues = checked(exitTextValues + timeline.ExitTextValues);
                treeTextValues = checked(treeTextValues + timeline.TreeTextValues);
                m_MaximumDetachedTextReferences = checked(m_MaximumDetachedTextReferences +
                    runtime.Roster[actor].MaximumDetachedCheckpointTextReferences);
                m_MaximumDetachedTextPayloads = checked(m_MaximumDetachedTextPayloads +
                    runtime.Roster[actor].MaximumDetachedCheckpointTextPayloads);
            }
            if (descriptor.MaximumSessionCheckpointPayloadBytes > 0)
            {
                var encoding = new SimulationCharacterStatePayloadStorage(
                    checked(capacity.StatePageCount + runtime.Roster.Count), descriptor.MaximumSessionCheckpointPayloadBytes,
                    FixedCharacterRuntimeStateCodec.HashPrefixBytes, actorTextValues);
                for (int actor = 0; actor < runtime.Roster.Count; actor++)
                {
                    runtime.Roster[actor].RuntimeStatePages.EncodingStorage = encoding;
                    runtime.Roster[actor].RuntimeStatePages.EncodingActorIndex = actor;
                }
            }
            m_MaximumTextValuesPerPayload = SimulationSessionCheckpointPayloadLayout.MaximumNonEmptyTextValues(
                descriptor.MaximumSessionCheckpointPayloadBytes, scalarTextValues, exitTextValues, treeTextValues);
            m_Checkpoints = new SimulationSessionCheckpointPayloadStorage(descriptor, in capacity,
                m_MaximumTextValuesPerPayload, m_MaximumDetachedTextReferences, m_MaximumDetachedTextPayloads);
            m_Participants = new ISimulationPipelineStateParticipant[participants.Count];
            for (int index = 0; index < participants.Count; index++)
                m_Participants[index] = participants[index];
            Array.Sort(m_Participants, (left, right) => left.StateIdentity.PassId.CompareTo(right.StateIdentity.PassId));
            m_Descriptors = new CheckpointDescriptors[m_Checkpoints.Count];
            m_Deterministic = runtime.NumericProfile.DeterministicReplay &&
                (descriptor.SolverCapabilities & WorldCapability.DeterministicReplay) != 0;
            for (int actor = 0; actor < runtime.Roster.Count; actor++)
                for (int ability = 0; ability < runtime.Roster[actor].AbilityInstallations.Installations.Count; ability++)
                {
                    FixedGameplayAbilityExecutionData data = runtime.Roster[actor].AbilityInstallations.Installations[ability].Data;
                    m_Deterministic &= data.NumericProfile.DeterministicReplay && data.Capabilities.HasGameplayCapability("DeterministicReplay");
                }
            if (m_Checkpoints.Count == 0)
                return;
            PrepareEncodingStorage();
            for (int index = 0; index < m_Descriptors.Length; index++)
            {
                var descriptors = new CheckpointDescriptors(descriptor, runtime, m_Participants, m_HashWriter, m_PublishedWorld);
                m_Descriptors[index] = descriptors;
                m_Checkpoints[index].ClearDescriptors = descriptors.Clear;
            }
        }

        void PrepareEncodingStorage()
        {
            m_HashWriter = new CanonicalWriter(new byte[checked(m_Composition.MaximumSessionCheckpointPayloadBytes +
                FixedCharacterRuntimeStateCodec.HashPrefixBytes)]);
            m_CanonicalWriter = new CanonicalWriter(new byte[m_Composition.MaximumSessionCheckpointPayloadBytes]);
            for (int actor = 0; actor < m_Runtime.Roster.Count; actor++)
                m_Runtime.Roster[actor].TimelineSnapshotPages.PrepareDecodeStorage();
        }

        public FixedSimulationSessionSnapshotStorage PrepareStepSnapshotStorage(int retainedSnapshots, int candidateSnapshots)
        {
            int count = 0;
            for (int index = 0; index < m_Participants.Length; index++)
                if (m_Participants[index].StepProjectionMode == SimulationPipelineStepProjectionMode.Include)
                    count++;
            var participants = new ISimulationPipelineStateParticipant[count];
            int target = 0;
            for (int index = 0; index < m_Participants.Length; index++)
                if (m_Participants[index].StepProjectionMode == SimulationPipelineStepProjectionMode.Include)
                    participants[target++] = m_Participants[index];
            var storage = new SimulationSessionCheckpointPayloadStorage(m_Composition, checked(retainedSnapshots + candidateSnapshots), in m_SessionCapacity, m_MaximumTextValuesPerPayload, m_MaximumDetachedTextReferences, m_MaximumDetachedTextPayloads);
            if (m_Checkpoints.Count == 0)
                PrepareEncodingStorage();
            m_StepSnapshots = new FixedSimulationSessionSnapshotStorage(storage, m_Composition, m_Runtime, participants,
                m_HashWriter, retainedSnapshots, candidateSnapshots, m_PublishedWorld);
            return m_StepSnapshots;
        }

        public FixedSimulationSessionSnapshot CaptureStepSnapshot(in SimulationWorldStateSet state)
        {
            SimulationSessionCheckpointPayloadStorage.Page page = m_StepSnapshots.Payloads.Acquire();
            CheckpointDescriptors descriptors = m_StepSnapshots.Descriptors[page.Index];
            try
            {
                CaptureInto(page, descriptors, in state, null);
                descriptors.Session.SetStorage(page);
                return descriptors.Session;
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
            SimulationSessionCheckpointPayloadStorage.Page page = m_Checkpoints.Acquire();
            try
            {
                CaptureInto(page, m_Descriptors[page.Index], in state, recordedParticipants);
                return page.PreparedCheckpoint;
            }
            catch
            {
                page.Release();
                throw;
            }
        }

        void CaptureInto(SimulationSessionCheckpointPayloadStorage.Page page, CheckpointDescriptors descriptors,
            in SimulationWorldStateSet state, SimulationPipelinePassStateSnapshot[] recordedParticipants)
        {
#if UNITY_EDITOR
            long captureStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            SimulationSnapshotCaptureProfile profile = default;
#endif
            CanonicalWriter writer = page.Writer;
            writer.WriteUInt32(Magic);
            writer.WriteInt32(Version);
            long hashPosition = writer.Position;
            writer.WriteHash(m_Composition.Identity);
            writer.WriteHash(m_Composition.Identity);
            SimulationWorldSnapshotCodec.CapturePrepared(writer, m_Runtime, in state, m_Deterministic,
                descriptors.World, page.Text
#if UNITY_EDITOR
                , ref profile
#endif
                );
            WritePreparedPipeline(writer, descriptors, state.LastCompletedTick, recordedParticipants, page.Text
#if UNITY_EDITOR
                , ref profile
#endif
                );
#if UNITY_EDITOR
            long sessionHashStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            descriptors.Session.SetPrepared(descriptors.World, descriptors.Pipeline, m_HashWriter);
#if UNITY_EDITOR
            profile.SessionHashElapsedStopwatchTicks = System.Diagnostics.Stopwatch.GetTimestamp() - sessionHashStarted;
#endif
            long end = writer.Position;
            writer.Position = hashPosition;
            writer.WriteHash(descriptors.Session.SnapshotHash);
            writer.Position = end;
            page.PreparedCheckpoint.CommitPayload(page, descriptors.Session.Tick, descriptors.Session.SnapshotHash);
#if UNITY_EDITOR
            profile.Calls = 1;
            profile.TotalElapsedStopwatchTicks = System.Diagnostics.Stopwatch.GetTimestamp() - captureStarted;
            descriptors.Session.CaptureProfile = profile;
#endif
        }

        void WritePreparedPipeline(CanonicalWriter writer, CheckpointDescriptors descriptors, ulong tick,
            SimulationPipelinePassStateSnapshot[] recordedParticipants, CanonicalTextReferences text
#if UNITY_EDITOR
            , ref SimulationSnapshotCaptureProfile profile
#endif
            )
        {
#if UNITY_EDITOR
            long pipelineStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            long hashPosition = writer.Position;
            writer.WriteHash(m_Composition.Pipeline.Hash);
            writer.WriteText(m_Composition.Pipeline.Id.Value);
            writer.WriteText(m_Composition.Pipeline.Revision.Value);
            writer.WriteInt32(m_Composition.Pipeline.SchemaVersion.Value);
            writer.WriteHash(m_Composition.Pipeline.Hash);
            WriteComponent(writer, m_Composition.ExecutionBackend);
            writer.WriteUInt64(tick);
            ISimulationPipelineStateParticipant[] participants = descriptors.RuntimeParticipants;
            writer.WriteInt32(participants.Length);
            for (int index = 0; index < participants.Length; index++)
            {
                SimulationPipelineStateParticipantIdentity identity = participants[index].StateIdentity;
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
                    participants[index].WriteCheckpointState(writer);
                else
                {
                    SimulationPipelinePassStateSnapshot recorded = recordedParticipants[index];
                    recorded.TextReferences.CopyTo(text);
                    writer.WriteRawBytes(recorded.Payload.Span);
                }
                int end = checked((int)writer.Position);
#if UNITY_EDITOR
                long participantHashStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                StableHash hash = SimulationCanonicalPayloadHash.Compute(writer.WrittenSpan.Slice(start, end - start));
#if UNITY_EDITOR
                profile.ParticipantHashElapsedStopwatchTicks += System.Diagnostics.Stopwatch.GetTimestamp() - participantHashStarted;
#endif
                writer.EndLengthPrefixedBlock(prefix);
                writer.Position = participantHashPosition;
                writer.WriteHash(hash);
                writer.Position = end;
                descriptors.Participants[index].SetPreparedPayload(writer.WrittenSegment.Array.AsMemory(start, end - start), hash, text);
            }
#if UNITY_EDITOR
            long pipelineHashStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            descriptors.Pipeline.SetPreparedTick(tick, m_HashWriter);
#if UNITY_EDITOR
            profile.PipelineHashElapsedStopwatchTicks = System.Diagnostics.Stopwatch.GetTimestamp() - pipelineHashStarted;
#endif
            long pipelineEnd = writer.Position;
            writer.Position = hashPosition;
            writer.WriteHash(descriptors.Pipeline.SnapshotHash);
            writer.Position = pipelineEnd;
#if UNITY_EDITOR
            profile.PipelineEncodeElapsedStopwatchTicks = System.Diagnostics.Stopwatch.GetTimestamp() - pipelineStarted -
                profile.ParticipantHashElapsedStopwatchTicks - profile.PipelineHashElapsedStopwatchTicks;
#endif
        }

        public FixedSimulationSessionSnapshot ReadCheckpoint(SimulationSessionCheckpoint checkpoint)
        {
            SimulationSessionCheckpointPayloadStorage.Page page = checkpoint.PayloadPage ?? m_Checkpoints.Receive(checkpoint);
            CheckpointDescriptors descriptors = m_Descriptors[page.Index];
            ICanonicalTextResolver resolver = page.ReceptionComplete ? page.Text : page.Text.ReceptionResolver;
            return ReadStored(page, descriptors, checkpoint.Payload, resolver);
        }

        FixedSimulationSessionSnapshot ReadStored(SimulationSessionCheckpointPayloadStorage.Page page,
            CheckpointDescriptors descriptors, ReadOnlyMemory<byte> payload, ICanonicalTextResolver resolver)
        {
            CanonicalReader reader = page.Reader;
            reader.Reset(payload);
            reader.SetTextResolver(resolver);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("Fixed Session snapshot header is invalid.");
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
                throw new InvalidDataException("Fixed Session snapshot hash is invalid.");
            m_CanonicalWriter.Reset();
            WriteCanonical(m_CanonicalWriter, descriptors.Session);
            if (!m_CanonicalWriter.ContentEquals(payload.Span))
                throw new InvalidDataException("Fixed Session snapshot is not canonical.");
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
                SimulationPipelineStateParticipantIdentity identity = descriptors.RuntimeParticipants[index].StateIdentity;
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

        public void CompleteCheckpointReception(SimulationSessionCheckpoint checkpoint) =>
            checkpoint.PayloadPage.ReceptionComplete = true;

        public void DisposeCheckpointStorage()
        {
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

        public ReadOnlyMemory<byte> Write(FixedSimulationSessionSnapshot snapshot) => snapshot.StoredPayload;

        public FixedSimulationSessionSnapshot Read(ReadOnlySpan<byte> bytes)
        {
            SimulationSessionCheckpointPayloadStorage.Page page = m_StepSnapshots.Payloads.Acquire();
            try
            {
                page.Writer.WriteRawBytes(bytes);
                CheckpointDescriptors descriptors = m_StepSnapshots.Descriptors[page.Index];
                FixedSimulationSessionSnapshot snapshot = ReadStored(page, descriptors,
                    page.Buffer.AsMemory(0, bytes.Length), page.Text.ReceptionResolver);
                page.PreparedCheckpoint.CommitPayload(page, snapshot.Tick, snapshot.SnapshotHash, false);
                snapshot.SetStorage(page);
                return snapshot;
            }
            catch
            {
                page.Release();
                throw;
            }
        }

        public void RequireRestore(
            SimulationSessionCompositionDescriptor descriptor,
            FixedCharacterRuntime characterRuntime,
            ICharacterWorldSolver solver,
            SimulationRestoreDirective directive,
            FixedSimulationSessionSnapshot snapshot)
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
                !string.Equals(directive.BackendId, descriptor.ExecutionBackend.ComponentId, StringComparison.Ordinal) ||
                !string.Equals(directive.BackendSemanticVersion, descriptor.ExecutionBackend.SemanticVersion, StringComparison.Ordinal) ||
                !snapshot.World.SolverId.Equals(solver.Descriptor.ImplementationId) ||
                !string.Equals(snapshot.World.SolverVersion, solver.Descriptor.Version, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Restore snapshot identity does not match the active Session composition.");
            }
        }

        static void WriteCanonical(CanonicalWriter writer, FixedSimulationSessionSnapshot snapshot)
        {
            writer.WriteUInt32(Magic);
            writer.WriteInt32(Version);
            writer.WriteHash(snapshot.SnapshotHash);
            writer.WriteHash(snapshot.CompositionIdentity);
            SimulationWorldSnapshotCodec.WriteLengthPrefixed(writer, snapshot.World);
            WritePipeline(writer, snapshot.Pipeline);
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

        static void RequireComponent(CanonicalReader reader, SimulationComponentIdentity expected)
        {
            if (reader.ReadByte() != (byte)expected.Role)
                throw new InvalidDataException("Checkpoint backend role does not match the compiled Pipeline.");
            reader.RequireText(expected.ComponentId);
            reader.RequireText(expected.SemanticVersion);
            if (!reader.ReadHash().Equals(expected.ConfigurationHash))
                throw new InvalidDataException("Checkpoint backend identity does not match the compiled Pipeline.");
        }

    }

    public sealed class FixedSimulationSessionSnapshotStorage : IDisposable
    {
        internal FixedSimulationSessionSnapshotStorage(SimulationSessionCheckpointPayloadStorage payloads,
            SimulationSessionCompositionDescriptor composition, FixedCharacterRuntime runtime,
            ISimulationPipelineStateParticipant[] participants, CanonicalWriter hashWriter,
            int retainedSnapshots, int candidateSnapshots, WorldSimulationState publishedWorld)
        {
            Payloads = payloads;
            MaximumRetainedSnapshots = retainedSnapshots;
            MaximumCandidateSnapshots = candidateSnapshots;
            Descriptors = new FixedSimulationSessionSnapshotCodec.CheckpointDescriptors[payloads.Count];
            for (int index = 0; index < Descriptors.Length; index++)
            {
                var descriptors = new FixedSimulationSessionSnapshotCodec.CheckpointDescriptors(composition,
                    runtime, participants, hashWriter, publishedWorld);
                Descriptors[index] = descriptors;
                payloads[index].ClearDescriptors = descriptors.Clear;
            }
        }

        internal readonly SimulationSessionCheckpointPayloadStorage Payloads;
        internal readonly FixedSimulationSessionSnapshotCodec.CheckpointDescriptors[] Descriptors;
        public int MaximumRetainedSnapshots { get; }
        public int MaximumCandidateSnapshots { get; }
        public void Dispose() => Payloads.Dispose();
    }

}

