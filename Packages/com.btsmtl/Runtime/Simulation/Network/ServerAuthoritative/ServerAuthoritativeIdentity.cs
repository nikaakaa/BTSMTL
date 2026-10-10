using System;
using System.Globalization;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    public static class ServerAuthoritativeModelIdentity
    {
        public const string ModelId = "thirdperson.network-model.server-authoritative-hybrid";
        public const string ProtocolId = "thirdperson.protocol.server-authoritative-hybrid.outer";
        public const string SemanticVersion = "1";
        public const int ProtocolVersion = 3;

        public static SimulationComponentIdentity CreateModel(StableHash configurationHash)
        {
            return new SimulationComponentIdentity(
                SimulationComponentRole.Model,
                ModelId,
                SemanticVersion,
                configurationHash);
        }

        public static SimulationProtocolIdentity CreateProtocol(StableHash schemaHash)
        {
            return new SimulationProtocolIdentity(ProtocolId, SemanticVersion, schemaHash);
        }
    }

    public static class ServerAuthoritativeSolverCompatibilityContract
    {
        public static readonly WorldCapability PredictionRequiredCapabilities =
            WorldCapability.BodyMotion |
            WorldCapability.Grounding |
            WorldCapability.Collision |
            WorldCapability.Reconstructible;

        public static readonly WorldCapability AuthorityRequiredCapabilities =
            WorldCapability.BodyMotion |
            WorldCapability.Grounding |
            WorldCapability.Collision |
            WorldCapability.Reconstructible;
    }

    public enum ServerAuthoritativeProcessRole : byte
    {
        AuthorityWorker = 1,
        ClientA = 2,
        ClientB = 3
    }

    public enum ServerAuthoritativeAuthorityHostRouteKind : byte
    {
        ExternalAuthorityWorker = 1,
        InProcessAuthorityScene = 2
    }

    public readonly struct ServerAuthoritativeRoomId : IEquatable<ServerAuthoritativeRoomId>, IComparable<ServerAuthoritativeRoomId>
    {
        public ServerAuthoritativeRoomId(string value) { Value = ServerAuthoritativeCanonicalIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(ServerAuthoritativeRoomId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(ServerAuthoritativeRoomId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ServerAuthoritativeRoomId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct ServerAuthoritativePlayerId : IEquatable<ServerAuthoritativePlayerId>, IComparable<ServerAuthoritativePlayerId>
    {
        public ServerAuthoritativePlayerId(string value) { Value = ServerAuthoritativeCanonicalIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(ServerAuthoritativePlayerId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(ServerAuthoritativePlayerId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ServerAuthoritativePlayerId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct ServerAuthoritativeSessionId : IEquatable<ServerAuthoritativeSessionId>, IComparable<ServerAuthoritativeSessionId>
    {
        public ServerAuthoritativeSessionId(string value) { Value = ServerAuthoritativeCanonicalIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(ServerAuthoritativeSessionId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(ServerAuthoritativeSessionId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ServerAuthoritativeSessionId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct ServerAuthoritativeWorkerId : IEquatable<ServerAuthoritativeWorkerId>, IComparable<ServerAuthoritativeWorkerId>
    {
        public ServerAuthoritativeWorkerId(string value) { Value = ServerAuthoritativeCanonicalIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(ServerAuthoritativeWorkerId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(ServerAuthoritativeWorkerId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ServerAuthoritativeWorkerId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct ServerAuthoritativeAuthorityHostIdentity : IEquatable<ServerAuthoritativeAuthorityHostIdentity>
    {
        public ServerAuthoritativeAuthorityHostIdentity(
            HostProductId hostProductId,
            string hostId,
            ServerAuthoritativeAuthorityHostRouteKind routeKind,
            ServerAuthoritativeRoomId roomId)
        {
            if (!IsValidRouteKind(routeKind) || !roomId.IsValid)
                throw new ArgumentException("ServerAuthoritative Authority Host identity is incomplete.");
            HostProductId = hostProductId.IsValid
                ? hostProductId
                : throw new ArgumentException("ServerAuthoritative Authority Host product identity is missing.", nameof(hostProductId));
            HostId = ServerAuthoritativeCanonicalIdentity.Require(hostId, nameof(hostId));
            RouteKind = routeKind;
            RoomId = roomId;
        }

        public HostProductId HostProductId { get; }
        public string HostId { get; }
        public ServerAuthoritativeAuthorityHostRouteKind RouteKind { get; }
        public ServerAuthoritativeRoomId RoomId { get; }
        public bool IsValid => HostProductId.IsValid && !string.IsNullOrEmpty(HostId) &&
                               IsValidRouteKind(RouteKind) && RoomId.IsValid;

        internal static bool IsValidRouteKind(ServerAuthoritativeAuthorityHostRouteKind routeKind) =>
            routeKind == ServerAuthoritativeAuthorityHostRouteKind.ExternalAuthorityWorker ||
            routeKind == ServerAuthoritativeAuthorityHostRouteKind.InProcessAuthorityScene;
        public bool Equals(ServerAuthoritativeAuthorityHostIdentity other) =>
            HostProductId.Equals(other.HostProductId) &&
            string.Equals(HostId, other.HostId, StringComparison.Ordinal) &&
            RouteKind == other.RouteKind && RoomId.Equals(other.RoomId);
        public override bool Equals(object obj) => obj is ServerAuthoritativeAuthorityHostIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(HostProductId, HostId, (int)RouteKind, RoomId);
        public override string ToString() => $"{HostProductId}/{HostId}/{RouteKind}/{RoomId}";
    }

    public readonly struct ServerAuthoritativeProcessIdentity : IEquatable<ServerAuthoritativeProcessIdentity>
    {
        public ServerAuthoritativeProcessIdentity(
            ServerAuthoritativeProcessRole role,
            ServerAuthoritativeRoomId roomId,
            ServerAuthoritativeWorkerId workerId,
            ServerAuthoritativePlayerId playerId,
            ActorId actorId)
        {
            if (!Enum.IsDefined(typeof(ServerAuthoritativeProcessRole), role) || !roomId.IsValid)
                throw new ArgumentException("ServerAuthoritative process identity is incomplete.");
            bool worker = role == ServerAuthoritativeProcessRole.AuthorityWorker;
            if (worker != workerId.IsValid || worker == playerId.IsValid || worker == actorId.IsValid)
                throw new ArgumentException("ServerAuthoritative process role ownership is invalid.");
            Role = role;
            RoomId = roomId;
            WorkerId = workerId;
            PlayerId = playerId;
            ActorId = actorId;
        }

        public ServerAuthoritativeProcessRole Role { get; }
        public ServerAuthoritativeRoomId RoomId { get; }
        public ServerAuthoritativeWorkerId WorkerId { get; }
        public ServerAuthoritativePlayerId PlayerId { get; }
        public ActorId ActorId { get; }
        public bool IsAuthority => Role == ServerAuthoritativeProcessRole.AuthorityWorker;
        public bool Equals(ServerAuthoritativeProcessIdentity other) =>
            Role == other.Role && RoomId.Equals(other.RoomId) && WorkerId.Equals(other.WorkerId) &&
            PlayerId.Equals(other.PlayerId) && ActorId.Equals(other.ActorId);
        public override bool Equals(object obj) => obj is ServerAuthoritativeProcessIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((int)Role, RoomId, WorkerId, PlayerId, ActorId);
        public override string ToString() => IsAuthority
            ? $"{Role}/{RoomId}/{WorkerId}"
            : $"{Role}/{RoomId}/{PlayerId}/{ActorId}";
    }

    public readonly struct ServerAuthoritativeRosterEntry : IEquatable<ServerAuthoritativeRosterEntry>, IComparable<ServerAuthoritativeRosterEntry>
    {
        public ServerAuthoritativeRosterEntry(
            ServerAuthoritativePlayerId playerId,
            ActorId actorId,
            ServerAuthoritativeProcessRole clientRole)
        {
            if (!playerId.IsValid || !actorId.IsValid ||
                clientRole != ServerAuthoritativeProcessRole.ClientA && clientRole != ServerAuthoritativeProcessRole.ClientB)
            {
                throw new ArgumentException("ServerAuthoritative roster entry is incomplete.");
            }
            PlayerId = playerId;
            ActorId = actorId;
            ClientRole = clientRole;
        }

        public ServerAuthoritativePlayerId PlayerId { get; }
        public ActorId ActorId { get; }
        public ServerAuthoritativeProcessRole ClientRole { get; }
        public int CompareTo(ServerAuthoritativeRosterEntry other) => ActorId.CompareTo(other.ActorId);
        public bool Equals(ServerAuthoritativeRosterEntry other) =>
            PlayerId.Equals(other.PlayerId) && ActorId.Equals(other.ActorId) && ClientRole == other.ClientRole;
        public override bool Equals(object obj) => obj is ServerAuthoritativeRosterEntry other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(PlayerId, ActorId, (int)ClientRole);
    }

    public sealed class ServerAuthoritativePipelineCompatibilityIdentity
    {
        public ServerAuthoritativePipelineCompatibilityIdentity(
            GameplayContentHash gameplayContentHash,
            StableHash stateSchemaHash,
            OperationSetVersion operationSetVersion,
            int tickRate,
            SimulationPipelineIdentity predictionPipeline,
            SimulationPipelineIdentity authorityPipeline,
            SimulationComponentIdentity backend,
            WorldCapability predictionSolverRequiredCapabilities,
            WorldCapability authoritySolverRequiredCapabilities)
        {
            if (!gameplayContentHash.IsValid || !stateSchemaHash.IsValid || !operationSetVersion.IsValid || tickRate <= 0 ||
                !predictionPipeline.IsValid || !authorityPipeline.IsValid || !backend.IsValid ||
                backend.Role != SimulationComponentRole.ExecutionBackend ||
                predictionSolverRequiredCapabilities == WorldCapability.None ||
                authoritySolverRequiredCapabilities == WorldCapability.None)
            {
                throw new ArgumentException("ServerAuthoritative Pipeline compatibility identity is incomplete.");
            }
            GameplayContentHash = gameplayContentHash;
            StateSchemaHash = stateSchemaHash;
            OperationSetVersion = operationSetVersion;
            TickRate = tickRate;
            PredictionPipeline = predictionPipeline;
            AuthorityPipeline = authorityPipeline;
            Backend = backend;
            PredictionSolverRequiredCapabilities = predictionSolverRequiredCapabilities;
            AuthoritySolverRequiredCapabilities = authoritySolverRequiredCapabilities;
            CompatibilityHash = StableHash.Compute(
                "server-authoritative-pipeline-pair/3",
                gameplayContentHash.ToString(),
                stateSchemaHash.ToString(),
                operationSetVersion.Value,
                tickRate.ToString(CultureInfo.InvariantCulture),
                predictionPipeline.ToString(),
                authorityPipeline.ToString(),
                backend.ToString(),
                checked((ulong)predictionSolverRequiredCapabilities).ToString(CultureInfo.InvariantCulture),
                checked((ulong)authoritySolverRequiredCapabilities).ToString(CultureInfo.InvariantCulture));
        }

        public GameplayContentHash GameplayContentHash { get; }
        public StableHash StateSchemaHash { get; }
        public OperationSetVersion OperationSetVersion { get; }
        public int TickRate { get; }
        public SimulationPipelineIdentity PredictionPipeline { get; }
        public SimulationPipelineIdentity AuthorityPipeline { get; }
        public SimulationComponentIdentity Backend { get; }
        public WorldCapability PredictionSolverRequiredCapabilities { get; }
        public WorldCapability AuthoritySolverRequiredCapabilities { get; }
        public StableHash CompatibilityHash { get; }
    }

    public sealed class ServerAuthoritativeWorldIdentity
    {
        public ServerAuthoritativeWorldIdentity(
            SolverImplementationId solverId,
            string solverVersion,
            WorldCapability solverCapabilities,
            WorldFeature solverFeatures,
            SimulationWorldId worldId,
            string mapId,
            WorldRevision worldRevision,
            StableHash worldConfigurationHash,
            StableHash navigationSurfaceArtifactHash,
            StableHash queryProfileHash)
        {
            if (string.IsNullOrEmpty(solverId.Value) || solverCapabilities == WorldCapability.None ||
                !worldId.IsValid || string.IsNullOrEmpty(worldRevision.Value) ||
                !worldConfigurationHash.IsValid || !navigationSurfaceArtifactHash.IsValid || !queryProfileHash.IsValid)
            {
                throw new ArgumentException("ServerAuthoritative World identity is incomplete.");
            }
            SolverId = solverId;
            SolverVersion = ServerAuthoritativeCanonicalIdentity.Require(solverVersion, nameof(solverVersion));
            SolverCapabilities = solverCapabilities;
            SolverFeatures = solverFeatures;
            WorldId = worldId;
            MapId = ServerAuthoritativeCanonicalIdentity.Require(mapId, nameof(mapId));
            WorldRevision = worldRevision;
            WorldConfigurationHash = worldConfigurationHash;
            NavigationSurfaceArtifactHash = navigationSurfaceArtifactHash;
            QueryProfileHash = queryProfileHash;
            IdentityHash = StableHash.Compute(
                "server-authoritative-world-identity/1",
                solverId.Value,
                SolverVersion,
                checked((ulong)solverCapabilities).ToString(CultureInfo.InvariantCulture),
                checked((ulong)solverFeatures).ToString(CultureInfo.InvariantCulture),
                worldId.Value,
                MapId,
                worldRevision.Value,
                worldConfigurationHash.Value,
                navigationSurfaceArtifactHash.Value,
                queryProfileHash.Value);
        }

        public SolverImplementationId SolverId { get; }
        public string SolverVersion { get; }
        public WorldCapability SolverCapabilities { get; }
        public WorldFeature SolverFeatures { get; }
        public SimulationWorldId WorldId { get; }
        public string MapId { get; }
        public WorldRevision WorldRevision { get; }
        public StableHash WorldConfigurationHash { get; }
        public StableHash NavigationSurfaceArtifactHash { get; }
        public StableHash QueryProfileHash { get; }
        public StableHash IdentityHash { get; }

        public bool Matches(ServerAuthoritativeWorldIdentity other) =>
            other != null && IdentityHash.Equals(other.IdentityHash);
    }

    static class ServerAuthoritativeCanonicalIdentity
    {
        public static string Require(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Canonical identity is required.", parameter);
            string normalized = value.Trim();
            if (normalized.Length > 64)
                throw new ArgumentOutOfRangeException(parameter, "Canonical identity cannot exceed 64 characters.");
            for (int i = 0; i < normalized.Length; i++)
            {
                char character = normalized[i];
                bool valid = character >= 'a' && character <= 'z' || character >= 'A' && character <= 'Z' ||
                             character >= '0' && character <= '9' || character == '.' || character == '_' || character == '-';
                if (!valid)
                    throw new ArgumentException("Canonical identity contains an unsupported character.", parameter);
            }
            return normalized;
        }
    }
}
