using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative.Transport
{
    public sealed class ServerAuthoritativeAuthoritySourceRuntime : IDisposable, IFloat32SourceEgressOutputPort
    {
        readonly ServerAuthoritativeAuthorityHostIdentity m_Host;
        readonly ServerAuthoritativeAuthoritySourcePolicy m_Policy;
        readonly IServerAuthoritativeAuthorityControlTransport m_Control;
        readonly IServerAuthoritativeAuthorityDataTransport m_Data;
        readonly ISimulationDiagnosticsSink m_Diagnostics;
        readonly NetworkCheckpointLayout m_CheckpointLayout;
        readonly ActorId[] m_ExpectedActors;
        readonly ServerAuthoritativeRosterEntry[] m_Roster;
        readonly ActorId[] m_RouteActors;
        readonly ServerAuthoritativeAuthorityClientRoute[] m_Routes;
        AcceptedAuthorityInput[] m_AcceptedInputs = Array.Empty<AcceptedAuthorityInput>();
        int m_RouteCount;
        readonly NetworkCheckpoint[] m_LatestCheckpoints;
        readonly bool[] m_HasLatestCheckpoints;
        readonly OutputRing<ServerAuthoritativeAuthorityReliableEventBatchOutput> m_ReliableOutput;
        readonly OutputRing<ServerAuthoritativeAuthorityFullCheckpointOutput> m_FullCheckpointOutput;
        readonly ThreadLocal<CanonicalWriter> m_PayloadWriter;
        readonly CanonicalTextReferences m_CommandClockText;
        readonly CanonicalReader m_CommandReader = new CanonicalReader(Array.Empty<byte>());
        readonly CanonicalReader m_InputReader = new CanonicalReader(Array.Empty<byte>());
        string[] m_EvidenceRouteMetrics = Array.Empty<string>();
        int m_EvidenceRouteMetricCount;
        ServerAuthoritativeSessionId m_SessionId;
        ulong m_RosterRevision;
        ulong m_LatestAuthorityTick;
        ulong m_LastSourceTick;
        ulong m_LastEvidenceAuthorityTick;
        ulong m_LastHeartbeatAckSequence;
        bool m_RegistrationAccepted;
        bool m_RosterLocked;
        bool m_FullBaselineRequested;
        bool m_Disposed;

        public ServerAuthoritativeAuthoritySourceRuntime(
            SimulationSessionSourceDescriptor descriptor,
            ServerAuthoritativeAuthoritySourcePolicy policy,
            ServerAuthoritativeAuthorityHostIdentity host,
            IEnumerable<ActorId> expectedActors,
            Float32CharacterRuntime characterRuntime,
            IServerAuthoritativeAuthorityControlTransport control,
            IServerAuthoritativeAuthorityDataTransport data,
            ISimulationDiagnosticsSink diagnostics,
            in GameplayEffectCheckpointCapacity checkpointCapacity,
            int recordedFrameCapacity)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            m_Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            if (!host.IsValid)
                throw new ArgumentException("Authority Source Host identity is invalid.", nameof(host));
            m_Host = host;
            m_Control = control ?? throw new ArgumentNullException(nameof(control));
            m_Data = data ?? throw new ArgumentNullException(nameof(data));
            m_Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            m_ReliableOutput = new OutputRing<ServerAuthoritativeAuthorityReliableEventBatchOutput>(
                policy.ReliableOutputQueueCapacity);
            m_FullCheckpointOutput = new OutputRing<ServerAuthoritativeAuthorityFullCheckpointOutput>(
                policy.FullCheckpointOutputQueueCapacity);
            m_CheckpointLayout = new NetworkCheckpointLayout(characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime)));
            m_PayloadWriter = new ThreadLocal<CanonicalWriter>(
                () => new CanonicalWriter(new byte[policy.ModelPolicy.MaxGameplayDatagramBytes]));
            ActorId[] actors = expectedActors == null
                ? Array.Empty<ActorId>()
                : expectedActors.ToArray();
            Array.Sort(actors);
            if (actors.Length == 0)
                throw new ArgumentException("Authority Source expected Actor roster is empty.", nameof(expectedActors));
            for (int i = 0; i < actors.Length; i++)
            {
                if (!actors[i].IsValid || i > 0 && actors[i - 1] == actors[i])
                    throw new ArgumentException("Authority Source expected roster contains an invalid or duplicate ActorId.", nameof(expectedActors));
            }
            int encodedPages = checked(checkpointCapacity.EncodedCheckpoints + checkpointCapacity.CapturePayloads +
                checkpointCapacity.RestorePayloads);
            int projectionPages = checked(1 + policy.ModelPolicy.AuthorityGameplayEffectCapacity.MaximumStepCount + recordedFrameCapacity);
            int clockValues = checked(4 + actors.Length *
                (policy.CommandQueueCapacity + 4 + encodedPages + projectionPages));
            m_CommandClockText = new CanonicalTextReferences(4, new CanonicalTextStorage(clockValues,
                checked(clockValues * (policy.ModelPolicy.MaxGameplayDatagramBytes / sizeof(char)))));
            m_ExpectedActors = actors;
            m_Roster = new ServerAuthoritativeRosterEntry[actors.Length];
            m_RouteActors = new ActorId[actors.Length];
            m_Routes = new ServerAuthoritativeAuthorityClientRoute[actors.Length];
            m_LatestCheckpoints = new NetworkCheckpoint[actors.Length];
            m_HasLatestCheckpoints = new bool[actors.Length];
            var accepted = new AcceptedInputPort(this);
            var clock = new AuthorityClockPort(this);
            var baseline = new FullBaselineRequestPort(this);
            var send = new AuthoritySendPort(this);
            RuntimePorts = new SimulationRuntimePortSet(new ISimulationRuntimePort[]
            {
                accepted,
                clock,
                baseline,
                send
            });
            SourceEgress = send;
        }

        public SimulationSessionSourceDescriptor Descriptor { get; }
        public ServerAuthoritativeAuthoritySourcePolicy Policy => m_Policy;
        public SimulationRuntimePortSet RuntimePorts { get; }
        public IFloat32SourceEgressOutputPort SourceEgress { get; }
        public NetworkCheckpointLayout CheckpointLayout => m_CheckpointLayout;
        public IReadOnlyList<ServerAuthoritativeRosterEntry> Roster =>
            m_RouteCount == 0 ? Array.Empty<ServerAuthoritativeRosterEntry>() : m_Roster;
        public ulong LatestAuthorityTick => m_LatestAuthorityTick;
        public bool IsReady
        {
            get
            {
                PumpTransport();
                if (!m_RegistrationAccepted || !m_RosterLocked)
                    return false;
                for (int i = 0; i < m_RouteCount; i++)
                {
                    if (!m_Routes[i].DataPlaneReady || !m_Routes[i].HasInput)
                        return false;
                }
                return true;
            }
        }

        public void Step(SimulationTickSourceIdentity source)
        {
            ThrowIfDisposed();
            RequireAuthoritySource(source);
            if (m_LastSourceTick != 0 && source.SourceTick < m_LastSourceTick)
                throw new InvalidOperationException("Authority Source outer Tick regressed.");
            m_LastSourceTick = source.SourceTick;
            m_Control.Step(source);
            PumpTransport();
        }

        public void PumpTransport()
        {
            ThrowIfDisposed();
            RequireControlAvailable();
            PumpControl();
            m_Data.ThrowIfUnavailable();
            while (m_Data.TryReceive(out ServerAuthoritativeReceivedDatagram received))
            {
                try
                {
                    ReceiveDatagram(received);
                }
                finally
                {
                    m_Data.ReturnReceived(received);
                }
            }
            m_Data.PumpSend();
            FlushControlOutputs();
        }

        public AcceptedAuthorityInputBatch ReadAcceptedInputs(SimulationTickSourceIdentity source)
        {
            RequireAuthoritySource(source);
            Step(source);
            for (int i = 0; i < m_RouteCount; i++)
            {
                ServerAuthoritativeAuthorityClientRoute route = m_Routes[i];
                if (route.DataPlaneReady && route.LastCommandSourceTick != 0 &&
                    source.SourceTick > route.LastCommandSourceTick + (ulong)m_Policy.CommandLivenessTimeoutTicks)
                {
                    Fail(
                        "server_authoritative_command_liveness_failed",
                        $"Authority received no command for Actor '{route.Roster.ActorId}' during '{source.SourceTick - route.LastCommandSourceTick}' source ticks.");
                }
            }
            ulong authorityTick = checked(m_LatestAuthorityTick + 1);
            AcceptedAuthorityInput[] inputs = m_AcceptedInputs;
            if (inputs.Length == 0)
                throw new ArgumentException("Authority input batch cannot be empty.", nameof(inputs));
            for (int i = 0; i < m_RouteCount; i++)
                inputs[i] = m_Routes[i].Select(authorityTick, m_Policy.ModelPolicy.MaximumInputLagTicks);
            return AcceptedAuthorityInputBatch.FromPreparedInputs(new SimulationTick(authorityTick), inputs);
        }

        public SimulationTick ReadAuthorityTick(SimulationTickSourceIdentity source)
        {
            RequireAuthoritySource(source);
            RequireControlAvailable();
            return new SimulationTick(checked(m_LatestAuthorityTick + 1));
        }

        public bool IsFullBaselineRequested
        {
            get
            {
                ThrowIfDisposed();
                PumpTransport();
                return m_FullBaselineRequested || HasPendingCheckpointRequest();
            }
        }

        public void Commit(Float32SourceEgressRecord record)
        {
            ThrowIfDisposed();
            PumpTransport();
            if (record == null ||
                !CanonicalTextValue.Equals(record.ChannelId, ServerAuthoritativeEgressChannels.AuthorityReplication, StringComparison.Ordinal) ||
                !CanonicalTextValue.Equals(record.SchemaId, ServerAuthoritativeEgressChannels.AuthorityReplicationSchema, StringComparison.Ordinal) ||
                record.SchemaVersion != ServerAuthoritativeEgressChannels.AuthorityReplicationSchemaVersion)
            {
                throw new InvalidOperationException("Authority Source accepts only canonical AuthorityReplication egress.");
            }
            AuthorityReplicationBatch batch = ServerAuthoritativeEgressCodec.ReadAuthorityReplication(record.Payload,
                record.PayloadReader, record.ElementReader, record.CanonicalScratch, record.TextResolver);
            if (batch.AuthorityTick.Value != checked(m_LatestAuthorityTick + 1))
                throw new InvalidOperationException("Authority replication Tick is not contiguous with the Authority Source clock.");
            m_LatestAuthorityTick = batch.AuthorityTick.Value;
            CaptureCheckpoints(batch);
            QueueReliableEvents(batch);
            WriteAuthorityEvidence(batch);
            ulong interval = checked((ulong)(m_Policy.ModelPolicy.SimulationTickRate / m_Policy.ModelPolicy.SnapshotPacketRate));
            if (batch.AuthorityTick.Value == 1 || batch.AuthorityTick.Value % interval == 0)
                SendSnapshots(batch);
            m_FullBaselineRequested = HasPendingCheckpointRequest();
            FlushControlOutputs();
            m_Data.PumpSend();
        }

        void PumpControl()
        {
            RequireControlAvailable();
            while (m_Control.TryTakeRegistration(out ServerAuthoritativeAuthorityRegistrationResult registration))
            {
                if (!registration.Host.Equals(m_Host))
                    Fail("authority_registration_identity_mismatch", "Authority registration result targets another Host.");
                if (m_RegistrationAccepted && !m_SessionId.Equals(registration.SessionId))
                    Fail("authority_registration_changed", "Authority registration SessionId changed while active.");
                m_RegistrationAccepted = true;
                m_SessionId = registration.SessionId;
            }
            while (m_Control.TryTakeRoster(out ServerAuthoritativeAuthorityRosterLock roster))
            {
                if (!roster.Host.Equals(m_Host) ||
                    m_RegistrationAccepted && !roster.SessionId.Equals(m_SessionId))
                {
                    Fail("authority_roster_identity_mismatch", "Authority roster lock targets another Host or Session.");
                }
                if (roster.Revision < m_RosterRevision)
                    continue;
                if (roster.Roster.Count != m_ExpectedActors.Length)
                    Fail("authority_roster_count_mismatch", "Authority roster lock does not match the expected Actor count.");
                for (int i = 0; i < m_ExpectedActors.Length; i++)
                {
                    if (roster.Roster[i].ActorId != m_ExpectedActors[i])
                        Fail("authority_roster_route_mismatch", "Authority roster lock does not match the expected Actor routes.");
                }
                if (!m_RosterLocked)
                {
                    for (int i = 0; i < roster.Roster.Count; i++)
                    {
                        ServerAuthoritativeRosterEntry entry = roster.Roster[i];
                        m_Roster[i] = entry;
                        m_RouteActors[i] = entry.ActorId;
                        m_Routes[i] = new ServerAuthoritativeAuthorityClientRoute(entry, m_Policy.CommandQueueCapacity);
                    }
                    m_RouteCount = roster.Roster.Count;
                    m_AcceptedInputs = new AcceptedAuthorityInput[m_RouteCount];
                }
                else
                {
                    for (int i = 0; i < m_Roster.Length; i++)
                    {
                        if (!m_Roster[i].Equals(roster.Roster[i]))
                            Fail("authority_roster_changed", "Authority roster changed after it was locked.");
                    }
                }
                m_RosterRevision = roster.Revision;
                m_RosterLocked = true;
            }
            while (m_Control.TryTakeTicket(out ServerAuthoritativeAuthorityDataPlaneTicket ticket))
                AcceptTicket(ticket);
            while (m_Control.TryTakeHeartbeatAck(out ServerAuthoritativeAuthorityHeartbeatAck heartbeat))
            {
                if (heartbeat.Sequence > m_LastHeartbeatAckSequence)
                    m_LastHeartbeatAckSequence = heartbeat.Sequence;
            }
            while (m_Control.TryTakeFullCheckpointRequest(out ServerAuthoritativeAuthorityFullCheckpointRequest request))
            {
                ServerAuthoritativeAuthorityClientRoute route = FindRoute(request.ActorId);
                if (route == null || !route.Roster.PlayerId.Equals(request.PlayerId))
                {
                    Fail("authority_full_checkpoint_route_unknown", "Full checkpoint request targets an unknown Authority route.");
                }
                route.RequestFullCheckpoint(request.RequestSequence);
                m_FullBaselineRequested = true;
            }
        }

        void AcceptTicket(ServerAuthoritativeAuthorityDataPlaneTicket ticket)
        {
            if (!ticket.Host.Equals(m_Host) ||
                !m_RegistrationAccepted || !ticket.SessionId.Equals(m_SessionId) ||
                ticket.ExpiresAtUnixMilliseconds <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            {
                Fail("authority_data_ticket_invalid", "Authority received an invalid or expired data-plane ticket.");
            }
            ServerAuthoritativeAuthorityClientRoute route = FindRoute(ticket.ActorId);
            if (route == null || !route.Roster.PlayerId.Equals(ticket.PlayerId))
            {
                Fail("authority_data_ticket_route_unknown", "Authority data-plane ticket targets an Actor outside the locked roster.");
            }
            route.SetTicket(
                ticket,
                new ServerAuthoritativeDatagramIdentity(
                    m_Host.RoomId,
                    m_SessionId,
                    route.Roster.PlayerId,
                    route.Roster.ActorId));
        }

        void ReceiveDatagram(ServerAuthoritativeReceivedDatagram received)
        {
            ServerAuthoritativeDatagramPacket packet = received.Packet;
            ServerAuthoritativeAuthorityClientRoute route = FindRoute(packet.Header.Identity.ActorId);
            if (route == null || !route.Identity.Equals(packet.Header.Identity))
            {
                return;
            }
            if (packet.Header.Kind == ServerAuthoritativeDatagramKind.DataPlaneHello)
            {
                ReceiveHello(route, received);
                return;
            }
            if (!route.DataPlaneReady || !route.AcceptPacketSequence(packet.Header.PacketSequence))
                return;
            if (packet.Header.Kind != ServerAuthoritativeDatagramKind.Command)
                Fail("authority_datagram_kind_invalid", $"Authority received unexpected gameplay datagram '{packet.Header.Kind}'.");
            try
            {
                CommandDatagram command = ServerAuthoritativeDatagramPayloadCodec.ReadCommand(packet.Payload,
                    m_CommandReader, m_InputReader, m_CommandClockText.ReceptionResolver);
                route.RecordCommand(packet.Payload.Length, command.SourceTick);
                ReceiveCommand(route, command);
            }
            finally { m_CommandClockText.Reset(); }
        }

        void ReceiveHello(
            ServerAuthoritativeAuthorityClientRoute route,
            ServerAuthoritativeReceivedDatagram received)
        {
            if (!route.Ticket.IsValid ||
                route.Ticket.ExpiresAtUnixMilliseconds <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            {
                Fail("authority_data_hello_without_ticket", "Authority received Hello without a live ticket.");
            }
            DataPlaneHello hello = ServerAuthoritativeDatagramPayloadCodec.ReadHello(received.Packet.Payload);
            if (!CanonicalTextValue.Equals(hello.TicketId, route.Ticket.TicketId, StringComparison.Ordinal) ||
                !CanonicalTextValue.Equals(hello.Nonce, route.Ticket.Nonce, StringComparison.Ordinal))
            {
                Fail("authority_data_hello_ticket_mismatch", "Authority received Hello with a mismatched ticket or nonce.");
            }
            m_Data.BindRemote(route.Identity, received.RemoteEndPoint);
            route.DataPlaneReady = true;
            route.AcceptHelloSequence(received.Packet.Header.PacketSequence);
            CanonicalWriter writer = m_PayloadWriter.Value;
            ServerAuthoritativeDatagramPayloadCodec.Write(
                new DataPlaneHelloAck(m_LatestAuthorityTick, hello.ClientClockMicros, ClockMicros()),
                writer);
            SendPacket(route, ServerAuthoritativeDatagramKind.DataPlaneHelloAck, writer);
            if (!route.TicketConsumptionReported)
            {
                route.TicketConsumptionReported = true;
                m_Control.SendTicketConsumed(route.Ticket);
            }
        }

        void ReceiveCommand(ServerAuthoritativeAuthorityClientRoute route, CommandDatagram command)
        {
            route.AcknowledgeSnapshot(command.LatestSnapshotSequence, command.LatestBaseSnapshotSequence);
            for (int i = command.Samples.Count - 1; i >= 0; i--)
            {
                CanonicalInputSample sample = command.Samples[i];
                if (sample.TargetAuthorityTick + (ulong)m_Policy.ModelPolicy.MaximumInputLagTicks < m_LatestAuthorityTick)
                    continue;
                if (sample.TargetAuthorityTick > m_LatestAuthorityTick + (ulong)m_Policy.ModelPolicy.MaximumInputLeadTicks + 1)
                {
                    Fail(
                        "authority_command_lead_exceeded",
                        $"Command target Tick '{sample.TargetAuthorityTick}' exceeds the authority lead window at '{m_LatestAuthorityTick}'.");
                }
                route.RecordCommandLead(sample.TargetAuthorityTick, m_LatestAuthorityTick);
                route.Enqueue(sample);
            }
        }

        void CaptureCheckpoints(AuthorityReplicationBatch batch)
        {
            for (int i = 0; i < batch.Baselines.Count; i++)
            {
                AuthoritativeActorBaseline baseline = batch.Baselines[i];
                int checkpointIndex = FindRouteIndex(baseline.ActorId);
                if (checkpointIndex < 0)
                    throw new InvalidOperationException("Authority checkpoint targets an unknown Authority route.");
                m_LatestCheckpoints[checkpointIndex] = NetworkCheckpointCodec.Capture(m_CheckpointLayout, baseline);
                m_HasLatestCheckpoints[checkpointIndex] = true;
            }
        }

        void SendSnapshots(AuthorityReplicationBatch batch)
        {
            for (int i = 0; i < m_RouteCount; i++)
            {
                ServerAuthoritativeAuthorityClientRoute route = m_Routes[i];
                if (!m_HasLatestCheckpoints[i] ||
                    m_LatestCheckpoints[i].Baseline.AuthorityTick != batch.AuthorityTick)
                {
                    m_FullBaselineRequested = true;
                    continue;
                }
                NetworkCheckpoint target = m_LatestCheckpoints[i];
                RemotePresentationBatch remote = FindRemote(batch, route.Roster.ActorId);
                if (route.PendingCheckpointRequest != 0)
                {
                    QueueFullCheckpoint(route, target, route.PendingCheckpointRequest);
                    route.PendingCheckpointRequest = 0;
                    continue;
                }
                if (!route.AcknowledgedCheckpoint.IsValid)
                {
                    QueueFullCheckpoint(route, target, 0);
                    continue;
                }
                ulong sequence = route.NextSnapshotSequence();
                byte[] delta = NetworkCheckpointCodec.WriteDelta(m_CheckpointLayout, route.AcknowledgedCheckpoint, target, remote);
                var snapshot = new SnapshotDatagram(
                    sequence,
                    route.AcknowledgedSnapshotSequence,
                    batch.AuthorityTick.Value,
                    FindAck(batch, route.Roster.ActorId).ConfirmedInputSequence,
                    target.Baseline.ConfirmedEventHorizon.Sequence,
                    delta);
                CanonicalWriter writer = m_PayloadWriter.Value;
                ServerAuthoritativeDatagramPayloadCodec.Write(snapshot, writer);
                int payloadLength = (int)writer.Length;
                var header = new ServerAuthoritativeDatagramHeader(
                    route.Identity,
                    ServerAuthoritativeDatagramKind.Snapshot,
                    sequence,
                    payloadLength);
                try
                {
                    m_Data.EnqueueSend(header, writer.WrittenSpan);
                    route.StoreSent(sequence, target);
                    route.RecordDeltaSnapshot(payloadLength);
                    Publish(
                        SimulationModelTraceKind.Transport,
                        "authority_snapshot_queued",
                        $"actor={route.Roster.ActorId};bytes={payloadLength};base={route.AcknowledgedSnapshotSequence};target={sequence}",
                        route.Roster.ActorId,
                        batch.AuthorityTick.Value,
                        FindAck(batch, route.Roster.ActorId).ConfirmedInputSequence,
                        route.AcknowledgedSnapshotSequence,
                        m_Data.SendQueueDepth,
                        true,
                        sequence);
                }
                catch (InvalidDataException)
                {
                    route.RecordDeltaMtuExceeded(payloadLength);
                    Publish(
                        SimulationModelTraceKind.Transport,
                        "server_authoritative_delta_mtu_exceeded",
                        $"actor={route.Roster.ActorId};deltaBytes={payloadLength};mtu={m_Policy.ModelPolicy.MaxGameplayDatagramBytes};base={route.AcknowledgedSnapshotSequence};target={sequence}",
                        route.Roster.ActorId,
                        batch.AuthorityTick.Value,
                        FindAck(batch, route.Roster.ActorId).ConfirmedInputSequence,
                        route.AcknowledgedSnapshotSequence,
                        m_Data.SendQueueDepth,
                        false);
                    QueueFullCheckpoint(route, target, 0, sequence);
                }
            }
        }

        void QueueFullCheckpoint(
            ServerAuthoritativeAuthorityClientRoute route,
            NetworkCheckpoint checkpoint,
            ulong requestSequence,
            ulong reservedSequence = 0)
        {
            if (m_FullCheckpointOutput.Count >= m_Policy.FullCheckpointOutputQueueCapacity)
                Fail("authority_full_checkpoint_queue_overflow", "Authority full checkpoint output queue overflowed.");
            ulong snapshotSequence = reservedSequence == 0 ? route.NextSnapshotSequence() : reservedSequence;
            byte[] payload = NetworkCheckpointCodec.WriteFull(m_CheckpointLayout, checkpoint);
            m_FullCheckpointOutput.Enqueue(new ServerAuthoritativeAuthorityFullCheckpointOutput(
                route.Roster.PlayerId,
                route.Roster.ActorId,
                requestSequence,
                snapshotSequence,
                checkpoint,
                payload));
            route.StoreSent(snapshotSequence, checkpoint);
            route.RecordFullCheckpoint(payload.Length);
            Publish(
                SimulationModelTraceKind.Transport,
                "authority_full_checkpoint_queued",
                $"actor={route.Roster.ActorId};bytes={payload.Length};target={snapshotSequence};request={requestSequence}",
                route.Roster.ActorId,
                checkpoint.Baseline.AuthorityTick.Value,
                checkpoint.Baseline.ConfirmedInputSequence,
                route.AcknowledgedSnapshotSequence,
                m_FullCheckpointOutput.Count,
                true,
                snapshotSequence);
        }

        void QueueReliableEvents(AuthorityReplicationBatch batch)
        {
            for (int sourceIndex = 0; sourceIndex < batch.RemotePresentation.Count; sourceIndex++)
            {
                RemotePresentationBatch source = batch.RemotePresentation[sourceIndex];
                if (source.ReliableEvents.Count == 0)
                    continue;
                ServerAuthoritativeAuthorityClientRoute recipient = null;
                for (int routeIndex = 0; routeIndex < m_RouteCount; routeIndex++)
                {
                    ServerAuthoritativeAuthorityClientRoute route = m_Routes[routeIndex];
                    if (route.Roster.ActorId != source.ActorId)
                        recipient = recipient == null ? route : throw new InvalidOperationException("Authority has more than one remote event recipient.");
                }
                if (recipient == null)
                    throw new InvalidOperationException("Authority reliable event has no remote recipient.");
                if (m_ReliableOutput.Count >= m_Policy.ReliableOutputQueueCapacity)
                    Fail("authority_reliable_output_queue_overflow", "Authority reliable event output queue overflowed.");
                var events = new ServerAuthoritativeAuthorityReliableEventOutput[source.ReliableEvents.Count];
                for (int i = 0; i < events.Length; i++)
                {
                    ServerAuthoritativeReliableEvent reliable = source.ReliableEvents[i];
                    byte[] payload = ServerAuthoritativeEgressCodec.WriteRemoteReliableEvent(source.ActorId, reliable);
                    events[i] = new ServerAuthoritativeAuthorityReliableEventOutput(
                        recipient.Roster.ActorId,
                        source.ActorId,
                        reliable,
                        payload);
                }
                var output = new ServerAuthoritativeAuthorityReliableEventBatchOutput(
                    recipient.Roster.ActorId,
                    source.ActorId,
                    events);
                m_ReliableOutput.Enqueue(output);
                output.RecipientActorId.Retain();
                output.SourceActorId.Retain();
                for (int i = 0; i < output.Events.Count; i++)
                {
                    output.Events[i].RecipientActorId.Retain();
                    output.Events[i].SourceActorId.Retain();
                    output.Events[i].Value.RetainText();
                }
            }
        }

        void FlushControlOutputs()
        {
            while (m_ReliableOutput.Count != 0)
            {
                ServerAuthoritativeAuthorityReliableEventBatchOutput output = m_ReliableOutput.Dequeue();
                try
                {
                    m_Control.SendReliableEvents(output);
                }
                finally
                {
                    ReleaseReliableOutput(output);
                }
            }
            while (m_FullCheckpointOutput.Count != 0)
                m_Control.SendFullCheckpoint(m_FullCheckpointOutput.Dequeue());
        }

        static void ReleaseReliableOutput(ServerAuthoritativeAuthorityReliableEventBatchOutput output)
        {
            output.RecipientActorId.Release();
            output.SourceActorId.Release();
            for (int i = 0; i < output.Events.Count; i++)
            {
                output.Events[i].RecipientActorId.Release();
                output.Events[i].SourceActorId.Release();
                output.Events[i].Value.ReleaseText();
            }
        }

        void SendPacket(
            ServerAuthoritativeAuthorityClientRoute route,
            ServerAuthoritativeDatagramKind kind,
            CanonicalWriter payloadWriter)
        {
            var header = new ServerAuthoritativeDatagramHeader(
                route.Identity,
                kind,
                route.NextSendPacketSequence(),
                (int)payloadWriter.Length);
            m_Data.EnqueueSend(header, payloadWriter.WrittenSpan);
        }

        bool HasPendingCheckpointRequest()
        {
            for (int i = 0; i < m_RouteCount; i++)
            {
                if (m_Routes[i].PendingCheckpointRequest != 0)
                    return true;
            }
            return false;
        }

        static AuthoritativeInputAck FindAck(AuthorityReplicationBatch batch, ActorId actorId)
        {
            for (int i = 0; i < batch.Acks.Count; i++)
            {
                if (batch.Acks[i].ActorId == actorId)
                    return batch.Acks[i];
            }
            throw new InvalidOperationException($"Authority replication has no ack for Actor '{actorId}'.");
        }

        static RemotePresentationBatch FindRemote(AuthorityReplicationBatch batch, ActorId owner)
        {
            RemotePresentationBatch remote = default;
            for (int i = 0; i < batch.RemotePresentation.Count; i++)
            {
                if (batch.RemotePresentation[i].ActorId == owner)
                    continue;
                remote = !remote.IsValid
                    ? batch.RemotePresentation[i]
                    : throw new InvalidOperationException("Authority replication has more than one remote Actor.");
            }
            if (!remote.IsValid)
                throw new InvalidOperationException("Authority replication has no remote Actor.");
            return remote;
        }

        void WriteAuthorityEvidence(AuthorityReplicationBatch batch)
        {
            if (!m_Diagnostics.IsEnabled)
                return;
            ulong interval = checked((ulong)m_Policy.ModelPolicy.SimulationTickRate * 5UL);
            if (batch.AuthorityTick.Value != 1 && batch.AuthorityTick.Value % interval != 0)
                return;
            float elapsedSeconds = m_LastEvidenceAuthorityTick == 0
                ? Math.Max(1f, batch.AuthorityTick.Value / (float)m_Policy.ModelPolicy.SimulationTickRate)
                : (batch.AuthorityTick.Value - m_LastEvidenceAuthorityTick) / (float)m_Policy.ModelPolicy.SimulationTickRate;
            m_LastEvidenceAuthorityTick = batch.AuthorityTick.Value;
            m_EvidenceRouteMetricCount = 0;
            for (int i = 0; i < m_RouteCount; i++)
            {
                ServerAuthoritativeAuthorityClientRoute route = m_Routes[i];
                if (m_EvidenceRouteMetricCount == m_EvidenceRouteMetrics.Length)
                {
                    int capacity = Math.Max(4, m_EvidenceRouteMetrics.Length * 2);
                    Array.Resize(ref m_EvidenceRouteMetrics, capacity);
                }
                m_EvidenceRouteMetrics[m_EvidenceRouteMetricCount++] = route.DescribeMetrics(elapsedSeconds);
            }
            Publish(
                SimulationModelTraceKind.Transport,
                "server_authoritative_authority_stream_metrics",
                $"tick={batch.AuthorityTick.Value};routes={string.Join("|", m_EvidenceRouteMetrics, 0, m_EvidenceRouteMetricCount)}",
                default,
                batch.AuthorityTick.Value,
                0,
                m_LastHeartbeatAckSequence,
                m_Data.ReceiveQueueDepth + m_Data.SendQueueDepth,
                true);
            Array.Clear(m_EvidenceRouteMetrics, 0, m_EvidenceRouteMetricCount);
            m_EvidenceRouteMetricCount = 0;
        }

        void RequireAuthoritySource(SimulationTickSourceIdentity source)
        {
            if (source.Kind != SimulationTickSourceKind.Authoritative || source.SourceTick == 0)
                throw new InvalidOperationException("Authority Source requires an Authoritative tick source.");
        }

        void RequireControlAvailable()
        {
            if (m_Control.ControlStatus != ServerAuthoritativeAuthorityControlTransportStatus.Failed)
                return;
            ServerAuthoritativeAuthorityControlFailure failure = m_Control.ControlFailure;
            throw new InvalidOperationException(
                failure == null
                    ? "Authority control transport failed without diagnostics."
                    : $"{failure.Code}: {failure.Message}");
        }

        void Fail(string code, string message)
        {
            m_Control.SendFailure(code, message);
            Publish(SimulationModelTraceKind.Failure, code, message, default, m_LatestAuthorityTick, 0, 0, 0, false);
            throw new InvalidOperationException($"{code}: {message}");
        }

        void Publish(
            SimulationModelTraceKind kind,
            string code,
            string detail,
            ActorId actorId,
            ulong authorityTick,
            ulong inputSequence,
            ulong ackSequence,
            int queueDepth,
            bool success,
            ulong snapshotSequence = 0)
        {
            if (!m_Diagnostics.IsEnabled)
                return;
            m_Diagnostics.PublishModel(new SimulationModelTraceRecord(
                kind,
                code,
                detail,
                actorId,
                m_LastSourceTick,
                authorityTick,
                inputSequence,
                ackSequence,
                queueDepth,
                0,
                0f,
                0f,
                success,
                snapshotSequence));
        }

        void ThrowIfDisposed()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(ServerAuthoritativeAuthoritySourceRuntime));
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;
            m_Disposed = true;
            var failures = new List<Exception>();
            try
            {
                m_Control.SendLeave("authority_source_disposed");
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            try
            {
                m_Control.Dispose();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            if (!ReferenceEquals(m_Control, m_Data))
            {
                try
                {
                    m_Data.Dispose();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            while (m_ReliableOutput.Count != 0)
                ReleaseReliableOutput(m_ReliableOutput.Dequeue());
            m_FullCheckpointOutput.Clear();
            try
            {
                m_PayloadWriter.Dispose();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            for (int index = 0; index < m_RouteCount; index++) m_Routes[index].Dispose();
            Array.Clear(m_AcceptedInputs, 0, m_AcceptedInputs.Length);
            m_CommandClockText.Reset();
            Array.Clear(m_RouteActors, 0, m_RouteCount);
            Array.Clear(m_Routes, 0, m_RouteCount);
            m_RouteCount = 0;
            Array.Clear(m_Roster, 0, m_Roster.Length);
            Array.Clear(m_LatestCheckpoints, 0, m_LatestCheckpoints.Length);
            Array.Clear(m_HasLatestCheckpoints, 0, m_HasLatestCheckpoints.Length);
            if (failures.Count != 0)
                throw new AggregateException("Authority Source failed to release completely.", failures);
        }

        static long ClockMicros() => checked(Stopwatch.GetTimestamp() * 1000000L / Stopwatch.Frequency);

        ServerAuthoritativeAuthorityClientRoute FindRoute(ActorId actorId)
        {
            int routeIndex = FindRouteIndex(actorId);
            return routeIndex < 0 ? null : m_Routes[routeIndex];
        }

        int FindRouteIndex(ActorId actorId)
        {
            int left = 0;
            int right = m_RouteCount - 1;
            while (left <= right)
            {
                int middle = left + (right - left) / 2;
                int comparison = m_RouteActors[middle].CompareTo(actorId);
                if (comparison == 0)
                    return middle;
                if (comparison < 0)
                    left = middle + 1;
                else
                    right = middle - 1;
            }
            return ~left;
        }

        sealed class AcceptedInputPort : IServerAuthoritativeAcceptedInputSourcePort
        {
            readonly ServerAuthoritativeAuthoritySourceRuntime m_Runtime;

            public AcceptedInputPort(ServerAuthoritativeAuthoritySourceRuntime runtime)
            {
                m_Runtime = runtime;
                Descriptor = SimulationPortDescriptor.CreateSource(
                    ServerAuthoritativeSourcePortContracts.AcceptedInput,
                    runtime.Descriptor.Identity);
            }

            public SimulationPortDescriptor Descriptor { get; }
            public AcceptedAuthorityInputBatch Read(SimulationTickSourceIdentity source) => m_Runtime.ReadAcceptedInputs(source);
        }

        sealed class AuthorityClockPort : IServerAuthoritativeAuthorityClockSourcePort
        {
            readonly ServerAuthoritativeAuthoritySourceRuntime m_Runtime;

            public AuthorityClockPort(ServerAuthoritativeAuthoritySourceRuntime runtime)
            {
                m_Runtime = runtime;
                Descriptor = SimulationPortDescriptor.CreateSource(
                    ServerAuthoritativeSourcePortContracts.AuthorityClock,
                    runtime.Descriptor.Identity);
            }

            public SimulationPortDescriptor Descriptor { get; }
            public SimulationTick ReadAuthorityTick(SimulationTickSourceIdentity source) => m_Runtime.ReadAuthorityTick(source);
        }

        sealed class FullBaselineRequestPort : IServerAuthoritativeFullBaselineRequestSourcePort
        {
            readonly ServerAuthoritativeAuthoritySourceRuntime m_Runtime;

            public FullBaselineRequestPort(ServerAuthoritativeAuthoritySourceRuntime runtime)
            {
                m_Runtime = runtime;
                Descriptor = SimulationPortDescriptor.CreateSource(
                    ServerAuthoritativeSourcePortContracts.FullBaselineRequest,
                    runtime.Descriptor.Identity);
            }

            public SimulationPortDescriptor Descriptor { get; }
            public bool IsRequested => m_Runtime.IsFullBaselineRequested;
        }

        sealed class AuthoritySendPort : IServerAuthoritativeNetworkSendPort
        {
            readonly ServerAuthoritativeAuthoritySourceRuntime m_Runtime;

            public AuthoritySendPort(ServerAuthoritativeAuthoritySourceRuntime runtime)
            {
                m_Runtime = runtime;
                Descriptor = SimulationPortDescriptor.CreateSource(
                    ServerAuthoritativeSourcePortContracts.AuthoritySend,
                    runtime.Descriptor.Identity);
            }

            public SimulationPortDescriptor Descriptor { get; }
            public void Commit(Float32SourceEgressRecord record) => m_Runtime.Commit(record);
        }
    }

    sealed class OutputRing<T>
    {
        readonly T[] m_Items;
        int m_Head;
        int m_Count;

        public OutputRing(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            m_Items = new T[capacity];
        }

        public int Count => m_Count;

        public void Enqueue(T value)
        {
            m_Items[(m_Head + m_Count) % m_Items.Length] = value;
            m_Count++;
        }

        public T Dequeue()
        {
            T value = m_Items[m_Head];
            m_Items[m_Head] = default;
            m_Head = (m_Head + 1) % m_Items.Length;
            m_Count--;
            return value;
        }

        public void Clear()
        {
            Array.Clear(m_Items, 0, m_Items.Length);
            m_Head = 0;
            m_Count = 0;
        }
    }
}
