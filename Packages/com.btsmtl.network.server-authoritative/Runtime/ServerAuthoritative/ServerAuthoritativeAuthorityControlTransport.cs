using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    public enum ServerAuthoritativeAuthorityControlTransportStatus : byte
    {
        Pending = 1,
        Ready = 2,
        Failed = 3
    }

    public sealed class ServerAuthoritativeAuthorityControlFailure
    {
        public ServerAuthoritativeAuthorityControlFailure(string code, string message)
        {
            Code = Require(code, nameof(code));
            Message = Require(message, nameof(message));
        }

        public string Code { get; }
        public string Message { get; }

        static string Require(string value, string parameter) => CanonicalTextValue.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Authority control failure value is required.", parameter)
            : value.Trim();
    }

    public sealed class ServerAuthoritativeAuthorityRegistrationResult
    {
        public ServerAuthoritativeAuthorityRegistrationResult(
            ServerAuthoritativeSessionId sessionId,
            ServerAuthoritativeAuthorityHostIdentity host)
        {
            if (!sessionId.IsValid || !host.IsValid)
                throw new ArgumentException("Authority registration result identity is incomplete.");
            SessionId = sessionId;
            Host = host;
        }

        public ServerAuthoritativeRoomId RoomId => Host.RoomId;
        public ServerAuthoritativeSessionId SessionId { get; }
        public ServerAuthoritativeAuthorityHostIdentity Host { get; }
    }

    public sealed class ServerAuthoritativeAuthorityRosterLock
    {
        readonly ReadOnlyCollection<ServerAuthoritativeRosterEntry> m_Roster;

        public ServerAuthoritativeAuthorityRosterLock(
            ServerAuthoritativeSessionId sessionId,
            ServerAuthoritativeAuthorityHostIdentity host,
            ulong revision,
            IEnumerable<ServerAuthoritativeRosterEntry> roster)
        {
            if (!sessionId.IsValid || !host.IsValid || revision == 0)
                throw new ArgumentException("Authority roster lock identity is incomplete.");
            var values = roster == null
                ? new List<ServerAuthoritativeRosterEntry>()
                : new List<ServerAuthoritativeRosterEntry>(roster);
            values.Sort();
            if (values.Count == 0)
                throw new ArgumentException("Authority roster lock is empty.", nameof(roster));
            for (int i = 1; i < values.Count; i++)
            {
                if (values[i - 1].ActorId == values[i].ActorId ||
                    values[i - 1].PlayerId.Equals(values[i].PlayerId))
                {
                    throw new ArgumentException("Authority roster lock contains duplicate routes.", nameof(roster));
                }
            }
            SessionId = sessionId;
            Host = host;
            Revision = revision;
            m_Roster = values.AsReadOnly();
        }

        public ServerAuthoritativeRoomId RoomId => Host.RoomId;
        public ServerAuthoritativeSessionId SessionId { get; }
        public ServerAuthoritativeAuthorityHostIdentity Host { get; }
        public ulong Revision { get; }
        public IReadOnlyList<ServerAuthoritativeRosterEntry> Roster => m_Roster;
    }

    public readonly struct ServerAuthoritativeAuthorityDataPlaneTicket
    {
        public ServerAuthoritativeAuthorityDataPlaneTicket(
            ServerAuthoritativeSessionId sessionId,
            ServerAuthoritativeAuthorityHostIdentity host,
            ServerAuthoritativePlayerId playerId,
            ActorId actorId,
            string ticketId,
            string nonce,
            long expiresAtUnixMilliseconds)
        {
            if (!sessionId.IsValid || !host.IsValid ||
                !playerId.IsValid || !actorId.IsValid || expiresAtUnixMilliseconds <= 0)
            {
                throw new ArgumentException("Authority data-plane ticket identity is incomplete.");
            }
            SessionId = sessionId;
            Host = host;
            PlayerId = playerId;
            ActorId = actorId;
            TicketId = Require(ticketId, nameof(ticketId));
            Nonce = Require(nonce, nameof(nonce));
            ExpiresAtUnixMilliseconds = expiresAtUnixMilliseconds;
        }

        public ServerAuthoritativeRoomId RoomId => Host.RoomId;
        public ServerAuthoritativeSessionId SessionId { get; }
        public ServerAuthoritativeAuthorityHostIdentity Host { get; }
        public ServerAuthoritativePlayerId PlayerId { get; }
        public ActorId ActorId { get; }
        public string TicketId { get; }
        public string Nonce { get; }
        public long ExpiresAtUnixMilliseconds { get; }

        public bool IsValid => SessionId.IsValid && Host.IsValid && PlayerId.IsValid && ActorId.IsValid &&
            ExpiresAtUnixMilliseconds > 0 && TicketId != null && Nonce != null;

        static string Require(string value, string parameter) => CanonicalTextValue.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Authority data-plane ticket value is required.", parameter)
            : value.Trim();
    }

    public readonly struct ServerAuthoritativeAuthorityHeartbeatAck
    {
        public ServerAuthoritativeAuthorityHeartbeatAck(
            ulong sequence,
            long sentUnixMilliseconds,
            long serverUnixMilliseconds)
        {
            if (sequence == 0 || sentUnixMilliseconds <= 0 || serverUnixMilliseconds <= 0)
                throw new ArgumentException("Authority heartbeat acknowledgement is incomplete.");
            Sequence = sequence;
            SentUnixMilliseconds = sentUnixMilliseconds;
            ServerUnixMilliseconds = serverUnixMilliseconds;
        }

        public ulong Sequence { get; }
        public long SentUnixMilliseconds { get; }
        public long ServerUnixMilliseconds { get; }
    }

    public readonly struct ServerAuthoritativeAuthorityFullCheckpointRequest
    {
        public ServerAuthoritativeAuthorityFullCheckpointRequest(
            ServerAuthoritativePlayerId playerId,
            ActorId actorId,
            ulong requestSequence)
        {
            if (!playerId.IsValid || !actorId.IsValid || requestSequence == 0)
                throw new ArgumentException("Full checkpoint request identity is incomplete.");
            PlayerId = playerId;
            ActorId = actorId;
            RequestSequence = requestSequence;
        }

        public ServerAuthoritativePlayerId PlayerId { get; }
        public ActorId ActorId { get; }
        public ulong RequestSequence { get; }
    }

    public readonly struct ServerAuthoritativeAuthorityFullCheckpointOutput
    {
        readonly NetworkCheckpoint m_Checkpoint;

        public ServerAuthoritativeAuthorityFullCheckpointOutput(
            ServerAuthoritativePlayerId playerId,
            ActorId actorId,
            ulong requestSequence,
            ulong snapshotSequence,
            in NetworkCheckpoint checkpoint,
            byte[] payload)
        {
            if (!playerId.IsValid || !actorId.IsValid || snapshotSequence == 0)
                throw new ArgumentException("Full checkpoint output identity is incomplete.");
            PlayerId = playerId;
            ActorId = actorId;
            RequestSequence = requestSequence;
            SnapshotSequence = snapshotSequence;
            if (!checkpoint.IsValid)
                throw new ArgumentOutOfRangeException(nameof(checkpoint));
            m_Checkpoint = checkpoint;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public ServerAuthoritativePlayerId PlayerId { get; }
        public ActorId ActorId { get; }
        public ulong RequestSequence { get; }
        public ulong SnapshotSequence { get; }
        public NetworkCheckpoint Checkpoint => m_Checkpoint;
        public byte[] Payload { get; }
        public bool IsValid => PlayerId.IsValid && ActorId.IsValid && SnapshotSequence != 0 &&
            m_Checkpoint.IsValid && Payload != null;
    }

    public readonly struct ServerAuthoritativeAuthorityReliableEventOutput
    {
        public ServerAuthoritativeAuthorityReliableEventOutput(
            ActorId recipientActorId,
            ActorId sourceActorId,
            ServerAuthoritativeReliableEvent value,
            byte[] payload)
        {
            if (!recipientActorId.IsValid || !sourceActorId.IsValid || recipientActorId == sourceActorId)
                throw new ArgumentException("Reliable event output route is invalid.");
            RecipientActorId = recipientActorId;
            SourceActorId = sourceActorId;
            Value = value;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public ActorId RecipientActorId { get; }
        public ActorId SourceActorId { get; }
        public ServerAuthoritativeReliableEvent Value { get; }
        public byte[] Payload { get; }
        public bool IsValid => RecipientActorId.IsValid && SourceActorId.IsValid &&
            RecipientActorId != SourceActorId && Payload != null;
    }

    public readonly struct ServerAuthoritativeAuthorityReliableEventBatchOutput
    {
        readonly ServerAuthoritativeAuthorityReliableEventOutput[] m_Events;

        public ServerAuthoritativeAuthorityReliableEventBatchOutput(
            ActorId recipientActorId,
            ActorId sourceActorId,
            ServerAuthoritativeAuthorityReliableEventOutput[] events)
        {
            if (!recipientActorId.IsValid || !sourceActorId.IsValid || recipientActorId == sourceActorId)
                throw new ArgumentException("Reliable event batch route is invalid.");
            if (events == null || events.Length == 0)
                throw new ArgumentException("Reliable event batch is empty.", nameof(events));
            for (int i = 0; i < events.Length; i++)
            {
                if (!events[i].IsValid || events[i].RecipientActorId != recipientActorId ||
                    events[i].SourceActorId != sourceActorId)
                {
                    throw new ArgumentException("Reliable event batch contains another route.", nameof(events));
                }
            }
            RecipientActorId = recipientActorId;
            SourceActorId = sourceActorId;
            m_Events = events;
        }

        public ActorId RecipientActorId { get; }
        public ActorId SourceActorId { get; }
        public IReadOnlyList<ServerAuthoritativeAuthorityReliableEventOutput> Events => m_Events;
        public bool IsValid => RecipientActorId.IsValid && SourceActorId.IsValid &&
            RecipientActorId != SourceActorId && m_Events != null && m_Events.Length > 0;
    }

    public interface IServerAuthoritativeAuthorityControlTransport : IDisposable
    {
        ServerAuthoritativeAuthorityControlTransportStatus ControlStatus { get; }
        ServerAuthoritativeAuthorityControlFailure ControlFailure { get; }
        void Step(SimulationTickSourceIdentity source);
        bool TryTakeRegistration(out ServerAuthoritativeAuthorityRegistrationResult value);
        bool TryTakeRoster(out ServerAuthoritativeAuthorityRosterLock value);
        bool TryTakeTicket(out ServerAuthoritativeAuthorityDataPlaneTicket value);
        bool TryTakeHeartbeatAck(out ServerAuthoritativeAuthorityHeartbeatAck value);
        bool TryTakeFullCheckpointRequest(out ServerAuthoritativeAuthorityFullCheckpointRequest value);
        void SendTicketConsumed(ServerAuthoritativeAuthorityDataPlaneTicket ticket);
        void SendReliableEvents(ServerAuthoritativeAuthorityReliableEventBatchOutput value);
        void SendFullCheckpoint(in ServerAuthoritativeAuthorityFullCheckpointOutput value);
        void SendLeave(string reason);
        void SendFailure(string code, string message);
    }
}
