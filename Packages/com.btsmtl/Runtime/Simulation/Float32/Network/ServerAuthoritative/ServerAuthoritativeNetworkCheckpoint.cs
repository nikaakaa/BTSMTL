using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    public sealed class NetworkCheckpointLayout
    {
        const int SchemaVersion = 2;
        readonly Float32CharacterRuntime m_CharacterRuntime;

        public NetworkCheckpointLayout(Float32CharacterRuntime characterRuntime)
        {
            m_CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            using var writer = new CanonicalWriter();
            writer.WriteText("server-authoritative-network-checkpoint-layout");
            writer.WriteInt32(SchemaVersion);
            writer.WriteText(Float32CharacterRuntimeStateCodec.CodecIdentity);
            writer.WriteHash(characterRuntime.GameplayContentHash);
            writer.WriteText(characterRuntime.NumericProfile.Id.Value);
            writer.WriteInt32(characterRuntime.NumericProfile.AbiVersion.Value);
            writer.WriteText(characterRuntime.OperationSetVersion.Value);
            for (int i = 0; i < characterRuntime.Roster.Count; i++)
            {
                SimulationActorBinding actor = characterRuntime.Roster[i];
                writer.WriteText(actor.ActorId.Value);
                writer.WriteHash(actor.GameplayContentHash);
                for (int abilityIndex = 0; abilityIndex < actor.AbilityInstallations.Installations.Count; abilityIndex++)
                {
                    Float32GameplayAbilityExecutionData ability = actor.AbilityInstallations.Installations[abilityIndex].Data;
                    writer.WriteText(ability.AbilityId.Value);
                    writer.WriteHash(ability.ContentHash);
                    writer.WriteHash(ability.StateSchemaHash);
                }
            }
            LayoutIdentity = writer.ComputeHash();
        }

        public Float32CharacterRuntime CharacterRuntime => m_CharacterRuntime;
        public string StateCodecIdentity => Float32CharacterRuntimeStateCodec.CodecIdentity;
        public StableHash LayoutIdentity { get; }

        internal SimulationActorBinding RequireActor(ActorId actorId)
        {
            for (int i = 0; i < m_CharacterRuntime.Roster.Count; i++)
                if (m_CharacterRuntime.Roster[i].ActorId == actorId)
                    return m_CharacterRuntime.Roster[i];
            throw new InvalidDataException($"Character Runtime has no Actor '{actorId}'.");
        }

        internal GameplayContentHash RequireActorContentHash(ActorId actorId) =>
            new GameplayContentHash(RequireActor(actorId).GameplayContentHash);

        internal CompactProducer ResolveCompactProducer(
            ActorId actorId,
            OperationHandle operation,
            CanonicalTextValue executionPath)
        {
            SimulationActorBinding actor = RequireActor(actorId);
            CompactProducer? found = null;
            for (int abilityIndex = 0; abilityIndex < actor.AbilityInstallations.Installations.Count; abilityIndex++)
            {
                Float32GameplayAbilityExecutionInstallation installation = actor.AbilityInstallations.Installations[abilityIndex];
                if (!operation.IsValid || operation.Value >= installation.Data.Operations.Count ||
                    !CanonicalTextValue.Equals(installation.Services.SourcePath(operation), executionPath, StringComparison.Ordinal))
                    continue;
                IReadOnlyList<ProgramReference> references = installation.Layout.References(
                    operation,
                    ProgramReferenceKind.Producer);
                if (references.Count != 1)
                    throw new InvalidDataException($"Remote sample operation '{operation}' does not have exactly one producer reference.");
                int producerIndex = references[0].TargetIndex;
                if (producerIndex < 0 || producerIndex >= installation.Data.Producers.Count)
                    throw new InvalidDataException($"Remote sample operation '{operation}' producer reference is invalid.");
                var candidate = new CompactProducer(
                    installation.Data.ContentHash,
                    installation.Data.Producers[producerIndex],
                    executionPath);
                if (found.HasValue)
                    throw new InvalidDataException($"Remote sample operation '{operation}' is ambiguous across Ability data.");
                found = candidate;
            }
            if (!found.HasValue)
                throw new InvalidDataException($"Character has no Ability producer for operation '{operation}/{executionPath}'.");
            return found.Value;
        }

        public void Require(NetworkCheckpoint checkpoint)
        {
            if (!checkpoint.IsValid)
                throw new ArgumentOutOfRangeException(nameof(checkpoint));
            if (!checkpoint.Baseline.GameplayContentHash.Equals(RequireActorContentHash(checkpoint.Baseline.ActorId)) ||
                !CanonicalTextValue.Equals(checkpoint.Baseline.StateCodecIdentity, StateCodecIdentity, StringComparison.Ordinal))
                throw new InvalidDataException("Network checkpoint does not match the locked Character Runtime state.");
            ValidateState(checkpoint.Baseline.ActorId, checkpoint.Baseline.AuthorityTick, checkpoint.StateMemory, checkpoint.Baseline.StateHash);
        }

        internal Float32CharacterRuntimeState ValidateState(
            ActorId actorId,
            SimulationTick tick,
            ReadOnlyMemory<byte> bytes,
            CharacterStateHash expectedHash)
        {
            SimulationActorBinding actor = RequireActor(actorId);
            Float32CharacterRuntimeState state = Float32CharacterRuntimeStateCodec.Read(
                bytes,
                actor, out CharacterStateHash hash);
            if (state.LastCompletedTick != tick.Value ||
                !hash.Equals(expectedHash))
                throw new InvalidDataException("Network checkpoint Character state does not match its Tick or hash.");
            return state;
        }
    }

    internal readonly struct CompactProducer
    {
        public CompactProducer(
            StableHash gameplayContentHash,
            ProgramProducer producer,
            CanonicalTextValue executionPath)
        {
            GameplayContentHash = gameplayContentHash;
            Producer = producer ?? throw new ArgumentNullException(nameof(producer));
            ExecutionPath = executionPath.IsNull ? CanonicalTextValue.Empty : executionPath;
        }

        public StableHash GameplayContentHash { get; }
        public ProgramProducer Producer { get; }
        public CanonicalTextValue ExecutionPath { get; }
    }

    public readonly struct NetworkCheckpoint
    {
        readonly byte[] m_StateBytes;

        internal NetworkCheckpoint(AuthoritativeActorBaseline baseline)
        {
            if (!baseline.IsValid)
                throw new ArgumentOutOfRangeException(nameof(baseline));
            Baseline = baseline;
            m_StateBytes = baseline.StateBuffer;
            CheckpointHash = ComputeHash(baseline, m_StateBytes);
        }

        public AuthoritativeActorBaseline Baseline { get; }
        internal ReadOnlySpan<byte> StateSpan => m_StateBytes;
        internal ReadOnlyMemory<byte> StateMemory => m_StateBytes;
        public StableHash CheckpointHash { get; }
        public bool IsValid => Baseline.IsValid && m_StateBytes != null;

        static StableHash ComputeHash(AuthoritativeActorBaseline baseline, byte[] stateBytes)
        {
            using var writer = new CanonicalWriter();
            writer.WriteText("server-authoritative-network-checkpoint/14");
            writer.WriteText(baseline.ActorId.Value);
            writer.WriteUInt64(baseline.AuthorityTick.Value);
            writer.WriteHash(baseline.GameplayContentHash);
            writer.WriteText(baseline.StateCodecIdentity);
            writer.WriteHash(baseline.StateHash);
            writer.WriteHash(baseline.BodyHash);
            writer.WriteUInt64(baseline.ConfirmedInputSequence);
            writer.WriteUInt64(baseline.ConfirmedEventHorizon.Sequence);
            writer.WriteBytes(stateBytes);
            return writer.ComputeHash();
        }
    }

    public static class NetworkCheckpointCodec
    {
        const uint FullMagic = 0x50434E53;
        const uint DeltaMagic = 0x44434E53;
        const int FullVersion = 18;
        const int DeltaVersion = 21;
        const string PresentationChannel = "Presentation";

        public static NetworkCheckpoint Capture(NetworkCheckpointLayout layout, AuthoritativeActorBaseline baseline)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));
            if (!baseline.IsValid)
                throw new ArgumentOutOfRangeException(nameof(baseline));
            var checkpoint = new NetworkCheckpoint(baseline);
            layout.Require(checkpoint);
            return checkpoint;
        }

        public static byte[] WriteFull(NetworkCheckpointLayout layout, NetworkCheckpoint checkpoint)
        {
            layout.Require(checkpoint);
            AuthoritativeActorBaseline baseline = checkpoint.Baseline;
            using var writer = new CanonicalWriter();
            writer.WriteUInt32(FullMagic);
            writer.WriteInt32(FullVersion);
            writer.WriteText(layout.LayoutIdentity.ToString());
            writer.WriteHash(baseline.GameplayContentHash);
            writer.WriteText(baseline.StateCodecIdentity);
            writer.WriteText(baseline.ActorId.Value);
            writer.WriteUInt64(baseline.AuthorityTick.Value);
            writer.WriteHash(baseline.StateHash);
            writer.WriteText(baseline.WorldRevision.Value);
            writer.WriteText(baseline.SolverId.Value);
            writer.WriteText(baseline.SolverVersion);
            writer.WriteUInt64((ulong)baseline.SolverCapabilities);
            WriteBody(writer, baseline.Body);
            writer.WriteUInt64(baseline.ConfirmedInputSequence);
            WriteHorizon(writer, baseline.ConfirmedEventHorizon);
            writer.WriteBytes(checkpoint.StateSpan);
            writer.WriteHash(checkpoint.CheckpointHash);
            return writer.ToArray();
        }

        public static NetworkCheckpoint ReadFull(NetworkCheckpointLayout layout, byte[] payload)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));
            var reader = new CanonicalReader(payload ?? throw new ArgumentNullException(nameof(payload)));
            if (reader.ReadUInt32() != FullMagic || reader.ReadInt32() != FullVersion ||
                !reader.ReadHash().Equals(layout.LayoutIdentity))
                throw new InvalidDataException("Full Network Checkpoint identity is invalid.");
            var contentHash = GameplayContentHash.CanonicalFromHash(reader.ReadHash());
            string stateCodecIdentity = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            var actorId = new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            var tick = new SimulationTick(reader.ReadUInt64());
            var stateHash = CharacterStateHash.CanonicalFromHash(reader.ReadHash());
            var worldRevision = new WorldRevision(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            var solverId = new SolverImplementationId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            string solverVersion = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            var capabilities = (WorldCapability)reader.ReadUInt64();
            WorldBodyState body = ReadBody(reader, actorId);
            ulong inputSequence = reader.ReadUInt64();
            ServerAuthoritativeEventHorizon horizon = ReadHorizon(reader);
            byte[] stateBytes = reader.ReadBytes();
            StableHash expectedCheckpointHash = reader.ReadHash();
            reader.RequireComplete();
            AuthoritativeActorBaseline baseline = BuildBaseline(
                layout,
                actorId,
                tick,
                contentHash,
                stateCodecIdentity,
                stateHash,
                worldRevision,
                solverId,
                solverVersion,
                capabilities,
                body,
                inputSequence,
                horizon,
                stateBytes);
            var checkpoint = new NetworkCheckpoint(baseline);
            if (!checkpoint.CheckpointHash.Equals(expectedCheckpointHash))
                throw new InvalidDataException("Full Network Checkpoint hash is invalid.");
            return checkpoint;
        }

        public static byte[] WriteDelta(
            NetworkCheckpointLayout layout,
            NetworkCheckpoint baseline,
            NetworkCheckpoint target,
            RemotePresentationBatch remote)
        {
            layout.Require(baseline);
            layout.Require(target);
            if (baseline.Baseline.ActorId != target.Baseline.ActorId ||
                target.Baseline.AuthorityTick.CompareTo(baseline.Baseline.AuthorityTick) <= 0)
                throw new InvalidDataException("Network Checkpoint delta does not advance the same Actor.");
            if (!remote.IsValid || remote.ActorId != target.Baseline.ActorId)
                throw new InvalidDataException("Network Checkpoint delta requires the target Actor remote presentation.");
            using var writer = new CanonicalWriter();
            AuthoritativeActorBaseline value = target.Baseline;
            writer.WriteUInt32(DeltaMagic);
            writer.WriteInt32(DeltaVersion);
            writer.WriteText(layout.LayoutIdentity.ToString());
            writer.WriteHash(baseline.CheckpointHash);
            writer.WriteHash(value.GameplayContentHash);
            writer.WriteText(value.StateCodecIdentity);
            writer.WriteText(value.ActorId.Value);
            writer.WriteUInt64(value.AuthorityTick.Value);
            writer.WriteHash(value.StateHash);
            writer.WriteText(value.WorldRevision.Value);
            writer.WriteText(value.SolverId.Value);
            writer.WriteText(value.SolverVersion);
            writer.WriteUInt64((ulong)value.SolverCapabilities);
            WriteBody(writer, value.Body);
            writer.WriteUInt64(value.ConfirmedInputSequence);
            WriteDeltaHorizon(writer, baseline.Baseline.ConfirmedEventHorizon, value.ConfirmedEventHorizon);
            ReadOnlySpan<byte> targetState = target.StateSpan;
            bool stateChanged = !Equal(baseline.StateSpan, targetState);
            writer.WriteBoolean(stateChanged);
            if (stateChanged)
                writer.WriteBytes(targetState);
            WriteCompactRemote(writer, layout, value.AuthorityTick, remote);
            writer.WriteHash(target.CheckpointHash);
            return writer.ToArray();
        }

        public static NetworkCheckpoint ReadDelta(
            NetworkCheckpointLayout layout,
            NetworkCheckpoint baseline,
            byte[] payload,
            out RemotePresentationBatch remote)
        {
            layout.Require(baseline);
            var reader = new CanonicalReader(payload ?? throw new ArgumentNullException(nameof(payload)));
            if (reader.ReadUInt32() != DeltaMagic || reader.ReadInt32() != DeltaVersion ||
                !reader.ReadHash().Equals(layout.LayoutIdentity) ||
                !reader.ReadHash().Equals(baseline.CheckpointHash))
                throw new InvalidDataException("Delta Network Checkpoint identity is invalid.");
            var contentHash = GameplayContentHash.CanonicalFromHash(reader.ReadHash());
            string stateCodecIdentity = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            var actorId = new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            var authorityTick = new SimulationTick(reader.ReadUInt64());
            var stateHash = CharacterStateHash.CanonicalFromHash(reader.ReadHash());
            var worldRevision = new WorldRevision(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            var solverId = new SolverImplementationId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            string solverVersion = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            var capabilities = (WorldCapability)reader.ReadUInt64();
            WorldBodyState body = ReadBody(reader, actorId);
            ulong confirmedInputSequence = reader.ReadUInt64();
            ServerAuthoritativeEventHorizon horizon = ReadDeltaHorizon(
                reader,
                baseline.Baseline.ConfirmedEventHorizon,
                reader.ReadUInt64());
            bool stateChanged = reader.ReadBoolean();
            byte[] stateBytes = stateChanged ? reader.ReadBytes() : baseline.Baseline.StateBuffer;
            remote = ReadCompactRemote(reader, layout, authorityTick, actorId);
            StableHash expectedCheckpointHash = reader.ReadHash();
            reader.RequireComplete();
            if (remote.BodySamples.Count == 0)
                throw new InvalidDataException("Network Checkpoint delta has no remote body sample.");
            AuthoritativeActorBaseline rebuilt = BuildBaseline(
                layout,
                actorId,
                authorityTick,
                contentHash,
                stateCodecIdentity,
                stateHash,
                worldRevision,
                solverId,
                solverVersion,
                capabilities,
                body,
                confirmedInputSequence,
                horizon,
                stateBytes);
            var checkpoint = new NetworkCheckpoint(rebuilt);
            if (!checkpoint.CheckpointHash.Equals(expectedCheckpointHash))
                throw new InvalidDataException("Delta Network Checkpoint hash is invalid.");
            return checkpoint;
        }

        static AuthoritativeActorBaseline BuildBaseline(
            NetworkCheckpointLayout layout,
            ActorId actorId,
            SimulationTick tick,
            GameplayContentHash contentHash,
            string stateCodecIdentity,
            CharacterStateHash expectedStateHash,
            WorldRevision worldRevision,
            SolverImplementationId solverId,
            string solverVersion,
            WorldCapability capabilities,
            WorldBodyState body,
            ulong inputSequence,
            ServerAuthoritativeEventHorizon horizon,
            byte[] stateBytes)
        {
            SimulationActorBinding actor = layout.RequireActor(actorId);
            GameplayContentHash expectedContentHash = new GameplayContentHash(actor.GameplayContentHash);
            if (!contentHash.Equals(expectedContentHash))
                throw new InvalidDataException("Network Checkpoint Character content identity is invalid.");
            if (!CanonicalTextValue.Equals(stateCodecIdentity, layout.StateCodecIdentity, StringComparison.Ordinal))
                throw new InvalidDataException("Network Checkpoint Character state codec identity is invalid.");
            layout.ValidateState(actorId, tick, stateBytes, expectedStateHash);
            if (body.ActorId != actorId)
                throw new InvalidDataException("Network Checkpoint body ActorId does not match Character state.");
            return new AuthoritativeActorBaseline(
                actorId,
                tick,
                layout.CharacterRuntime.NumericProfile,
                layout.CharacterRuntime.NumericProfile.AbiVersion,
                stateCodecIdentity,
                contentHash,
                layout.CharacterRuntime.OperationSetVersion,
                stateBytes,
                expectedStateHash,
                worldRevision,
                solverId,
                solverVersion,
                capabilities,
                body,
                inputSequence,
                horizon);
        }

        static void WriteCompactRemote(
            CanonicalWriter writer,
            NetworkCheckpointLayout layout,
            SimulationTick authorityTick,
            RemotePresentationBatch remote)
        {
            CharacterBodySample body = remote.BodySamples[0];
            if (body.ActorId != remote.ActorId || body.Tick != authorityTick)
                throw new InvalidDataException("Remote body sample does not match the checkpoint Tick.");
            WriteCompactBody(writer, body.BeforeBody);
            WriteCompactBody(writer, body.FinalBody);
            writer.WriteVector3(body.AppliedDisplacement);
            writer.WriteScalar(body.AppliedYawDegrees);
            if (remote.SampleCommands.Count > ushort.MaxValue)
                throw new InvalidDataException("Remote sample command count exceeds its wire boundary.");
            writer.WriteUInt16((ushort)remote.SampleCommands.Count);
            for (int i = 0; i < remote.SampleCommands.Count; i++)
                WriteCompactSampleCommand(writer, layout, authorityTick, remote.ActorId, remote.SampleCommands[i]);
        }

        static RemotePresentationBatch ReadCompactRemote(
            CanonicalReader reader,
            NetworkCheckpointLayout layout,
            SimulationTick authorityTick,
            ActorId actorId)
        {
            WorldBodyState before = ReadCompactBody(reader, actorId);
            WorldBodyState final = ReadCompactBody(reader, actorId);
            var body = new CharacterBodySample(
                actorId,
                authorityTick,
                before,
                final,
                reader.ReadVector3(),
                reader.ReadScalar());
            int commandCount = reader.ReadUInt16();
            var commands = new PresentationCommand[commandCount];
            for (int i = 0; i < commands.Length; i++)
                commands[i] = ReadCompactSampleCommand(reader, layout, authorityTick, actorId);
            return new RemotePresentationBatch(actorId, new[] { body }, commands, Array.Empty<ServerAuthoritativeReliableEvent>(), false);
        }

        static void WriteCompactSampleCommand(
            CanonicalWriter writer,
            NetworkCheckpointLayout layout,
            SimulationTick authorityTick,
            ActorId actorId,
            PresentationCommand command)
        {
            SimulationEventHeader header = command.Header;
            if (command.Kind != PresentationCommandKind.SampleProducer || header.ActorId != actorId ||
                header.Tick != authorityTick || !header.NumericProfile.Equals(layout.CharacterRuntime.NumericProfile) ||
                !header.Activation.Source.IsSkillOperation)
                throw new InvalidDataException("Remote sample command does not match the checkpoint route.");
            OperationHandle operation = header.Activation.Source.Operation;
            CompactProducer producer = layout.ResolveCompactProducer(
                actorId,
                operation,
                header.Activation.Source.ExecutionPath);
            var expectedEventId = EventIdBuilder.Create(
                new GameplayContentHash(producer.GameplayContentHash),
                actorId,
                new ActivationId(
                    SimulationExecutionSource.FromSkillOperation(operation, producer.ExecutionPath),
                    header.Activation.Generation),
                authorityTick,
                header.Sequence,
                PresentationChannel);
            if (header.Activation.Source.ExecutionPath != producer.ExecutionPath ||
                !CanonicalTextValue.Equals(header.Channel, PresentationChannel, StringComparison.Ordinal) ||
                !CanonicalTextValue.Equals(command.ProducerId, producer.Producer.Identity, StringComparison.Ordinal) ||
                !header.EventId.Equals(expectedEventId))
                throw new InvalidDataException("Remote sample command does not match its locked Ability identity.");
            writer.WriteInt32(operation.Value);
            writer.WriteText(producer.ExecutionPath);
            writer.WriteUInt64(header.Activation.Generation);
            writer.WriteUInt64(header.Sequence);
            writer.WriteScalar(command.SampleTime);
            writer.WriteScalar(command.Weight);
            writer.WriteUInt64(command.ProducerGeneration);
            writer.WriteInt32(command.Cycle);
            writer.WriteUInt64(command.SourceActionInstanceId);
            writer.WriteScalar(command.VisualTimeScale);
        }

        static PresentationCommand ReadCompactSampleCommand(
            CanonicalReader reader,
            NetworkCheckpointLayout layout,
            SimulationTick authorityTick,
            ActorId actorId)
        {
            var operation = new OperationHandle(reader.ReadInt32());
            string executionPath = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            CompactProducer producer = layout.ResolveCompactProducer(actorId, operation, executionPath);
            var activation = new ActivationId(
                SimulationExecutionSource.FromSkillOperation(operation, executionPath),
                reader.ReadUInt64());
            ulong sequence = reader.ReadUInt64();
            var header = new SimulationEventHeader(
                layout.CharacterRuntime.NumericProfile,
                EventIdBuilder.Create(
                    new GameplayContentHash(producer.GameplayContentHash),
                    actorId,
                    activation,
                    authorityTick,
                    sequence,
                    PresentationChannel),
                actorId,
                authorityTick,
                activation,
                sequence,
                PresentationChannel);
            return new PresentationCommand(
                header,
                PresentationCommandKind.SampleProducer,
                producer.Producer.Identity,
                reader.ReadScalar(),
                reader.ReadScalar(),
                reader.ReadUInt64(),
                reader.ReadInt32(),
                reader.ReadUInt64(),
                reader.ReadScalar());
        }

        static void WriteCompactBody(CanonicalWriter writer, WorldBodyState body)
        {
            writer.WriteVector3(body.Position);
            writer.WriteYaw(body.Yaw);
            writer.WriteVector3(body.Velocity);
            writer.WriteScalar(body.VerticalVelocity);
            writer.WriteBoolean(body.Grounded);
            writer.WriteUInt32((uint)body.Collision);
        }

        static WorldBodyState ReadCompactBody(CanonicalReader reader, ActorId actorId) => new WorldBodyState(
            actorId,
            reader.ReadVector3(),
            reader.ReadYaw(),
            reader.ReadVector3(),
            reader.ReadScalar(),
            reader.ReadBoolean(),
            (WorldCollisionSummary)reader.ReadUInt32());

        static void WriteDeltaHorizon(
            CanonicalWriter writer,
            ServerAuthoritativeEventHorizon baseline,
            ServerAuthoritativeEventHorizon target)
        {
            if (target.Sequence < baseline.Sequence ||
                target.Sequence == baseline.Sequence && !target.EventId.Equals(baseline.EventId))
                throw new InvalidDataException("Network Checkpoint event horizon regressed or changed its EventId.");
            bool changed = target.Sequence != baseline.Sequence;
            writer.WriteBoolean(changed);
            writer.WriteUInt64(target.Sequence);
            if (changed && !target.IsEmpty)
            {
                Span<byte> bytes = stackalloc byte[32];
                target.EventId.CopyTo(bytes);
                writer.WriteRawBytes(bytes);
            }
        }

        static ServerAuthoritativeEventHorizon ReadDeltaHorizon(
            CanonicalReader reader,
            ServerAuthoritativeEventHorizon baseline,
            ulong encodedSequence)
        {
            bool changed = reader.ReadBoolean();
            ulong sequence = encodedSequence;
            if (!changed)
            {
                if (sequence != baseline.Sequence)
                    throw new InvalidDataException("Network Checkpoint event horizon sequence changed without a delta.");
                return baseline;
            }
            if (sequence < baseline.Sequence)
                throw new InvalidDataException("Network Checkpoint event horizon delta regressed.");
            if (sequence == 0)
                return ServerAuthoritativeEventHorizon.Empty;
            ArraySegment<byte> bytes = reader.ReadRawBytesSegment(32);
            return new ServerAuthoritativeEventHorizon(sequence, EventId.FromBytes(bytes.AsSpan()));
        }

        static void WriteBody(CanonicalWriter writer, WorldBodyState body)
        {
            writer.WriteText(body.ActorId.Value);
            writer.WriteVector3(body.Position);
            writer.WriteYaw(body.Yaw);
            writer.WriteVector3(body.Velocity);
            writer.WriteScalar(body.VerticalVelocity);
            writer.WriteBoolean(body.Grounded);
            writer.WriteUInt32((uint)body.Collision);
        }

        static WorldBodyState ReadBody(CanonicalReader reader, ActorId actorId)
        {
            var encodedActorId = new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            if (encodedActorId != actorId)
                throw new InvalidDataException("Network Checkpoint body ActorId is invalid.");
            return new WorldBodyState(
                actorId,
                reader.ReadVector3(),
                reader.ReadYaw(),
                reader.ReadVector3(),
                reader.ReadScalar(),
                reader.ReadBoolean(),
                (WorldCollisionSummary)reader.ReadUInt32());
        }

        static void WriteHorizon(CanonicalWriter writer, ServerAuthoritativeEventHorizon horizon)
        {
            writer.WriteUInt64(horizon.Sequence);
            writer.WriteEventId(horizon.EventId);
        }

        static ServerAuthoritativeEventHorizon ReadHorizon(CanonicalReader reader)
        {
            ulong sequence = reader.ReadUInt64();
            EventId eventId = reader.ReadEventId();
            return new ServerAuthoritativeEventHorizon(sequence, eventId);
        }

        static bool Equal(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => left.SequenceEqual(right);
    }
}
