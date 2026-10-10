using System;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation
{
    [Flags]
    public enum WorldCollisionSummary : uint
    {
        None = 0,
        Sides = 1,
        Above = 2,
        Below = 4
    }

    public enum WorldStatePersistenceMode : byte
    {
        Reconstruct = 1,
        Snapshot = 2
    }

    public readonly struct WorldBodyState
    {
        public WorldBodyState(
            ActorId actorId,
            Float32Vector3 position,
            Float32Yaw yaw,
            Float32Vector3 velocity,
            Float32Scalar verticalVelocity,
            bool grounded,
            WorldCollisionSummary collision)
        {
            if (!actorId.IsValid)
                throw new ArgumentException("World body ActorId is invalid.", nameof(actorId));
            ActorId = actorId;
            Position = position;
            Yaw = yaw;
            Velocity = velocity;
            VerticalVelocity = verticalVelocity;
            Grounded = grounded;
            Collision = collision;
        }
        public ActorId ActorId { get; }
        public Float32Vector3 Position { get; }
        public Float32Yaw Yaw { get; }
        public Float32Vector3 Velocity { get; }
        public Float32Scalar VerticalVelocity { get; }
        public bool Grounded { get; }
        public WorldCollisionSummary Collision { get; }
    }

    public sealed class WorldSimulationState
    {
        readonly IReadOnlyList<WorldBodyState> m_Bodies;
        readonly byte[] m_SolverStatePayload;
        int m_SolverPayloadLength;

        public WorldSimulationState(
            SimulationNumericProfile numericProfile,
            SolverImplementationId solverId,
            string solverVersion,
            WorldRevision worldRevision,
            WorldStatePersistenceMode persistenceMode,
            IReadOnlyList<WorldBodyState> bodies,
            byte[] solverStatePayload)
            : this(
                numericProfile,
                solverId,
                solverVersion,
                worldRevision,
                persistenceMode,
                CopyBodies(bodies),
                CopySolverStatePayload(solverStatePayload))
        {
        }

        WorldSimulationState(
            SimulationNumericProfile numericProfile,
            SolverImplementationId solverId,
            string solverVersion,
            WorldRevision worldRevision,
            WorldStatePersistenceMode persistenceMode,
            WorldBodyState[] bodies,
            byte[] solverStatePayload)
        {
            if (!numericProfile.IsValid || CanonicalTextValue.IsNullOrEmpty(solverId.Value) || CanonicalTextValue.IsNullOrEmpty(worldRevision.Value))
                throw new ArgumentException("World state identity is incomplete.");
            NumericProfile = numericProfile;
            SolverId = solverId;
            SolverVersion = SimulationIdentity.Require(solverVersion, nameof(solverVersion));
            WorldRevision = worldRevision;
            PersistenceMode = persistenceMode;
            WorldBodyState[] ownedBodies = bodies ?? Array.Empty<WorldBodyState>();
            Array.Sort(ownedBodies, (left, right) => left.ActorId.CompareTo(right.ActorId));
            for (int i = 1; i < ownedBodies.Length; i++)
            {
                if (ownedBodies[i - 1].ActorId == ownedBodies[i].ActorId)
                    throw new ArgumentException($"World state contains duplicate ActorId '{ownedBodies[i].ActorId}'.", nameof(bodies));
            }
            m_Bodies = ownedBodies;
            m_SolverStatePayload = solverStatePayload ?? Array.Empty<byte>();
            m_SolverPayloadLength = m_SolverStatePayload.Length;
        }

        public static WorldSimulationState FromOwnedState(
            SimulationNumericProfile numericProfile,
            SolverImplementationId solverId,
            string solverVersion,
            WorldRevision worldRevision,
            WorldStatePersistenceMode persistenceMode,
            WorldBodyState[] bodies,
            byte[] solverStatePayload) =>
            new WorldSimulationState(
                numericProfile,
                solverId,
                solverVersion,
                worldRevision,
                persistenceMode,
                bodies,
                solverStatePayload);

        internal WorldSimulationState(int actorCount, int maximumPayloadBytes)
        {
            m_Bodies = new WorldBodyState[actorCount];
            m_SolverStatePayload = new byte[maximumPayloadBytes];
        }

        internal WorldBodyState[] BodyArray => (WorldBodyState[])m_Bodies;

        internal void SetPrepared(SimulationNumericProfile numeric, SolverImplementationId solver,
            string version, WorldRevision revision, WorldStatePersistenceMode persistence, ReadOnlySpan<byte> payload)
        {
            if (payload.Length > m_SolverStatePayload.Length)
                throw new InvalidDataException("World Solver payload exceeds the prepared storage capacity.");
            payload.CopyTo(m_SolverStatePayload);
            m_SolverPayloadLength = payload.Length;
            NumericProfile = numeric;
            SolverId = solver;
            SolverVersion = version;
            WorldRevision = revision;
            PersistenceMode = persistence;
        }

        internal void ClearPrepared()
        {
            Array.Clear(BodyArray, 0, BodyArray.Length);
            SolverId = default;
            SolverVersion = null;
            WorldRevision = default;
            m_SolverPayloadLength = 0;
        }

        public SimulationNumericProfile NumericProfile { get; private set; }
        public SolverImplementationId SolverId { get; private set; }
        public string SolverVersion { get; private set; }
        public WorldRevision WorldRevision { get; private set; }
        public WorldStatePersistenceMode PersistenceMode { get; private set; }
        public IReadOnlyList<WorldBodyState> Bodies => m_Bodies;
        public ReadOnlyMemory<byte> SolverStatePayload => m_SolverStatePayload.AsMemory(0, m_SolverPayloadLength);

        public WorldSimulationState Clone() =>
            new WorldSimulationState(
                NumericProfile,
                SolverId,
                SolverVersion,
                WorldRevision,
                PersistenceMode,
                CopyBodies(m_Bodies),
                SolverStatePayload.ToArray());

        static WorldBodyState[] CopyBodies(IReadOnlyList<WorldBodyState> bodies)
        {
            if (bodies == null || bodies.Count == 0)
                return Array.Empty<WorldBodyState>();
            var copied = new WorldBodyState[bodies.Count];
            for (int i = 0; i < copied.Length; i++)
                copied[i] = bodies[i];
            return copied;
        }

        static byte[] CopySolverStatePayload(byte[] solverStatePayload) =>
            solverStatePayload == null || solverStatePayload.Length == 0
                ? Array.Empty<byte>()
                : (byte[])solverStatePayload.Clone();
    }

    public static class WorldSimulationStateCodec
    {
        const uint Magic = 0x54535743;
        const int Version = 4;

        [ThreadStatic] static CanonicalWriter s_Writer;

        public static byte[] Write(WorldSimulationState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            CanonicalWriter writer = PrepareWriter();
            WriteCanonical(writer, state);
            return writer.ToArray();
        }

        public static int MaximumEncodedBytes(WorldSimulationState initialState, int maximumSolverPayloadBytes)
        {
            using var writer = new CanonicalWriter();
            WriteCanonical(writer, initialState);
            return checked((int)writer.Length - initialState.SolverStatePayload.Length + maximumSolverPayloadBytes);
        }

        public static StableHash ComputeHash(WorldSimulationState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            CanonicalWriter writer = PrepareWriter();
            return ComputeHash(state, writer);
        }

        internal static StableHash ComputeHash(WorldSimulationState state, CanonicalWriter writer)
        {
            writer.Reset();
            WriteCanonical(writer, state);
            return writer.ComputeHash();
        }

        internal static void WriteLengthPrefixed(CanonicalWriter writer, WorldSimulationState state)
        {
            long prefixPosition = writer.BeginLengthPrefixedBlock();
            WriteCanonical(writer, state);
            writer.EndLengthPrefixedBlock(prefixPosition);
        }

        static CanonicalWriter PrepareWriter()
        {
            s_Writer ??= new CanonicalWriter();
            s_Writer.Reset();
            return s_Writer;
        }

        static void WriteCanonical(CanonicalWriter writer, WorldSimulationState state)
        {
            writer.WriteUInt32(Magic);
            writer.WriteInt32(Version);
            SimulationNumericProfileCodec.Write(writer, state.NumericProfile);
            writer.WriteText(state.SolverId.Value);
            writer.WriteText(state.SolverVersion);
            writer.WriteText(state.WorldRevision.Value);
            writer.WriteByte((byte)state.PersistenceMode);
            writer.WriteInt32(state.Bodies.Count);
            for (int i = 0; i < state.Bodies.Count; i++)
            {
                WorldBodyState body = state.Bodies[i];
                writer.WriteText(body.ActorId.Value);
                writer.WriteVector3(body.Position);
                writer.WriteYaw(body.Yaw);
                writer.WriteVector3(body.Velocity);
                writer.WriteScalar(body.VerticalVelocity);
                writer.WriteBoolean(body.Grounded);
                writer.WriteUInt32((uint)body.Collision);
            }
            writer.WriteBytes(state.SolverStatePayload.Span);
        }

        public static WorldSimulationState Read(
            byte[] bytes, SimulationNumericProfile expectedNumericProfile,
            SolverImplementationId expectedSolverId, string expectedSolverVersion, WorldRevision expectedWorldRevision) =>
            Read(bytes, expectedNumericProfile, expectedSolverId, expectedSolverVersion, expectedWorldRevision,
                new CanonicalReader(Array.Empty<byte>()), new CanonicalWriter(), null);

        internal static WorldSimulationState Read(
            ReadOnlyMemory<byte> bytes, SimulationNumericProfile expectedNumericProfile,
            SolverImplementationId expectedSolverId, string expectedSolverVersion, WorldRevision expectedWorldRevision,
            CanonicalReader reader, CanonicalWriter writer, ICanonicalTextResolver resolver)
        {
            WorldHeader header = ReadHeader(bytes, reader, resolver, 1000000, expectedNumericProfile, expectedSolverId, expectedSolverVersion, expectedWorldRevision);
            var storage = new WorldSimulationState(header.Count, bytes.Length);
            return ReadBodies(bytes, in header, expectedNumericProfile, expectedSolverId,
                expectedSolverVersion, expectedWorldRevision, reader, writer, storage);
        }

        internal static WorldSimulationState Read(
            ReadOnlyMemory<byte> bytes, SimulationNumericProfile expectedNumericProfile,
            SolverImplementationId expectedSolverId, string expectedSolverVersion, WorldRevision expectedWorldRevision,
            CanonicalReader reader, CanonicalWriter writer, ICanonicalTextResolver resolver,
            WorldSimulationState storage)
        {
            WorldHeader header = ReadHeader(bytes, reader, resolver, storage.Bodies.Count, expectedNumericProfile, expectedSolverId, expectedSolverVersion, expectedWorldRevision);
            if (header.Count != storage.Bodies.Count)
                throw new InvalidDataException("World body count does not match the prepared Session roster.");
            return ReadBodies(bytes, in header, expectedNumericProfile, expectedSolverId,
                expectedSolverVersion, expectedWorldRevision, reader, writer, storage);
        }

        static WorldHeader ReadHeader(ReadOnlyMemory<byte> bytes, CanonicalReader reader,
            ICanonicalTextResolver resolver, int maximumBodies, SimulationNumericProfile expectedNumericProfile,
            SolverImplementationId expectedSolverId, string expectedSolverVersion, WorldRevision expectedWorldRevision)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(resolver);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("World state header is invalid.");
            SimulationNumericProfile numericProfile = SimulationNumericProfileCodec.Read(reader, expectedNumericProfile);
            reader.RequireText(expectedSolverId.Value);
            reader.RequireText(expectedSolverVersion);
            reader.RequireText(expectedWorldRevision.Value);
            SolverImplementationId solverId = expectedSolverId;
            string solverVersion = expectedSolverVersion;
            WorldRevision worldRevision = expectedWorldRevision;
            WorldStatePersistenceMode persistenceMode = ReadPersistenceMode(reader.ReadByte());
            int count = reader.ReadInt32();
            if (count < 0 || count > maximumBodies)
                throw new InvalidDataException($"World body count '{count}' is invalid.");
            return new WorldHeader(numericProfile, solverId, solverVersion, worldRevision, persistenceMode, count);
        }

        static WorldSimulationState ReadBodies(ReadOnlyMemory<byte> bytes, in WorldHeader header,
            SimulationNumericProfile expectedNumericProfile, SolverImplementationId expectedSolverId,
            string expectedSolverVersion, WorldRevision expectedWorldRevision,
            CanonicalReader reader, CanonicalWriter writer, WorldSimulationState storage)
        {
            WorldBodyState[] bodies = storage.BodyArray;
            for (int i = 0; i < bodies.Length; i++)
            {
                bodies[i] = new WorldBodyState(
                    new ActorId(reader.ReadText()),
                    reader.ReadVector3(),
                    reader.ReadYaw(),
                    reader.ReadVector3(),
                    reader.ReadScalar(),
                    reader.ReadBoolean(),
                    (WorldCollisionSummary)reader.ReadUInt32());
            }
            ArraySegment<byte> payload = reader.ReadBytesSegment();
            reader.RequireComplete();
            for (int i = 1; i < bodies.Length; i++)
                if (bodies[i - 1].ActorId.CompareTo(bodies[i].ActorId) >= 0)
                    throw new InvalidDataException("World bodies are not in the canonical unique Actor order.");
            storage.SetPrepared(header.NumericProfile, header.SolverId, header.SolverVersion,
                header.WorldRevision, header.PersistenceMode, payload.AsSpan());
            writer.Reset();
            writer.SetTextCapture(null);
            WriteCanonical(writer, storage);
            if (!writer.ContentEquals(bytes.Span))
                throw new InvalidDataException("World state is not canonical.");
            return storage;
        }

        readonly struct WorldHeader
        {
            internal WorldHeader(SimulationNumericProfile numericProfile, SolverImplementationId solverId,
                string solverVersion, WorldRevision worldRevision, WorldStatePersistenceMode persistenceMode, int count)
            {
                NumericProfile = numericProfile;
                SolverId = solverId;
                SolverVersion = solverVersion;
                WorldRevision = worldRevision;
                PersistenceMode = persistenceMode;
                Count = count;
            }

            internal readonly SimulationNumericProfile NumericProfile;
            internal readonly SolverImplementationId SolverId;
            internal readonly string SolverVersion;
            internal readonly WorldRevision WorldRevision;
            internal readonly WorldStatePersistenceMode PersistenceMode;
            internal readonly int Count;
        }

        static WorldStatePersistenceMode ReadPersistenceMode(byte value)
        {
            if ((WorldStatePersistenceMode)value is not (WorldStatePersistenceMode.Reconstruct or WorldStatePersistenceMode.Snapshot))
                throw new InvalidDataException($"World persistence mode '{value}' is invalid.");
            return (WorldStatePersistenceMode)value;
        }

    }
}
