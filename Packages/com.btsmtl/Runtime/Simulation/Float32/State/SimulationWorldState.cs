using System;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation
{
    [CanonicalContract]
    public readonly partial struct WorldBodyState
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
            ActorId = actorId;
            Position = position;
            Yaw = yaw;
            Velocity = velocity;
            VerticalVelocity = verticalVelocity;
            Grounded = grounded;
            Collision = collision;
            RequireRecord();
        }
        [CanonicalBound]
        public ActorId ActorId { get; }
        public Float32Vector3 Position { get; }
        public Float32Yaw Yaw { get; }
        public Float32Vector3 Velocity { get; }
        public Float32Scalar VerticalVelocity { get; }
        public bool Grounded { get; }
        public WorldCollisionSummary Collision { get; }

        [CanonicalValidation]
        void RequireRecord()
        {
            if (!ActorId.IsValid)
                throw new ArgumentException("World body ActorId is invalid.", "ActorId");
        }
    }

    public sealed class WorldSimulationState
    {
        WorldSimulationStateIdentity m_Identity;
        readonly IReadOnlyList<WorldBodyState> m_Bodies;
        readonly byte[] m_SolverStatePayload;
        readonly WorldSimulationStateStorage.Page m_Storage;
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
            m_Identity = new WorldSimulationStateIdentity(numericProfile, solverId,
                SimulationIdentity.Require(solverVersion, nameof(solverVersion)), worldRevision, persistenceMode);
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

        internal WorldSimulationState(int actorCount, int maximumPayloadBytes, WorldSimulationStateStorage.Page storage)
            : this(actorCount, maximumPayloadBytes)
        {
            m_Storage = storage;
        }

        public void RetainStateStorage() => m_Storage?.Retain();
        public void ReleaseStateStorage() => m_Storage?.Release();

        internal WorldBodyState[] BodyArray => (WorldBodyState[])m_Bodies;

        internal void SetPrepared(in WorldSimulationStateIdentity identity, ReadOnlySpan<byte> payload)
        {
            if (payload.Length > m_SolverStatePayload.Length)
                throw new InvalidDataException("World Solver payload exceeds the prepared storage capacity.");
            payload.CopyTo(m_SolverStatePayload);
            m_SolverPayloadLength = payload.Length;
            m_Identity = identity;
        }

        internal void ClearPrepared()
        {
            Array.Clear(BodyArray, 0, BodyArray.Length);
            m_Identity = default;
            m_SolverPayloadLength = 0;
        }

        public WorldSimulationStateIdentity Identity => m_Identity;
        public SimulationNumericProfile NumericProfile => m_Identity.NumericProfile;
        public SolverImplementationId SolverId => m_Identity.SolverId;
        public string SolverVersion => m_Identity.SolverVersion;
        public WorldRevision WorldRevision => m_Identity.WorldRevision;
        public WorldStatePersistenceMode PersistenceMode => m_Identity.PersistenceMode;
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

    public sealed class WorldSimulationStateStorage
    {
        public sealed class Page
        {
            int m_References;

            internal Page(int actorCount, int payloadCapacity)
            {
                World = new WorldSimulationState(actorCount, payloadCapacity, this);
                Result = new WorldSolveBatchResult(actorCount);
            }

            internal bool IsReleased => m_References == 0;
            public WorldSimulationState World { get; }
            public WorldSolveBatchResult Result { get; }
            public WorldBodyState[] Bodies => World.BodyArray;
            public CharacterWorldSolveResult[] Results => Result.ResultArray;

            internal void BeginWrite() => m_References = 1;
            internal void Retain() => m_References++;

            public WorldSimulationState CompleteState(SimulationNumericProfile numeric, SolverImplementationId solver,
                string version, WorldRevision revision, WorldStatePersistenceMode persistence, ReadOnlySpan<byte> payload)
            {
                var identity = new WorldSimulationStateIdentity(numeric, solver, version, revision, persistence);
                World.SetPrepared(in identity, payload);
                return World;
            }

            public WorldSolveBatchResult CompleteResult(WorldSolveBatchRequest request, SolverImplementationId solver, string version)
            {
                return Result.Reset(request, solver, version, World);
            }

            public void CopyState(WorldSimulationState state)
            {
                for (int i = 0; i < Bodies.Length; i++)
                    Bodies[i] = state.Bodies[i];
                WorldSimulationStateIdentity identity = state.Identity;
                World.SetPrepared(in identity, state.SolverStatePayload.Span);
            }

            public void Release()
            {
                if (--m_References != 0)
                    return;
                Result.ClearPrepared();
                World.ClearPrepared();
            }
        }

        readonly int m_ActorCount;
        readonly int m_PayloadCapacity;
        readonly List<Page> m_Pages = new List<Page>(2);

        public WorldSimulationStateStorage(int actorCount, int payloadCapacity)
        {
            m_ActorCount = actorCount;
            m_PayloadCapacity = payloadCapacity;
            m_Pages.Add(new Page(actorCount, payloadCapacity));
            m_Pages.Add(new Page(actorCount, payloadCapacity));
        }

        public void PrepareStepStorage(int maximumSteps)
        {
            int count = checked(maximumSteps + 4);
            m_Pages.Capacity = Math.Max(m_Pages.Capacity, count);
            while (m_Pages.Count < count)
                m_Pages.Add(new Page(m_ActorCount, m_PayloadCapacity));
        }

        public Page BeginWrite()
        {
            for (int i = 0; i < m_Pages.Count; i++)
            {
                Page page = m_Pages[i];
                if (!page.IsReleased)
                    continue;
                page.BeginWrite();
                return page;
            }
            throw new InvalidOperationException("World state storage exceeded its prepared transaction capacity.");
        }
    }

    public static class WorldSimulationStateCodec
    {
        const uint Magic = 0x54535743;
        const int Version = 7;
        public static readonly string CanonicalSchema = CanonicalContractProtocol.ComposeSchema(
            "float32-world-state/" + Version, WorldSimulationStateIdentity.CanonicalSchema,
            WorldBodyState.CanonicalSchema);
        static readonly StableHash CanonicalSchemaHash = new StableHash(CanonicalSchema);

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
            writer.WriteHash(CanonicalSchemaHash);
            WorldSimulationStateIdentity.CanonicalWrite(writer, state.Identity);
            writer.WriteInt32(state.Bodies.Count);
            for (int i = 0; i < state.Bodies.Count; i++)
            {
                WorldBodyState.CanonicalWrite(writer, state.Bodies[i]);
            }
            writer.WriteBytes(state.SolverStatePayload.Span);
        }

        public static WorldSimulationState Read(
            byte[] bytes, SimulationNumericProfile expectedNumericProfile,
            SolverImplementationId expectedSolverId, string expectedSolverVersion, WorldRevision expectedWorldRevision) =>
            Read(bytes, expectedNumericProfile, expectedSolverId, expectedSolverVersion, expectedWorldRevision,
                new CanonicalReader(Array.Empty<byte>()), null);

        internal static WorldSimulationState Read(
            ReadOnlyMemory<byte> bytes, SimulationNumericProfile expectedNumericProfile,
            SolverImplementationId expectedSolverId, string expectedSolverVersion, WorldRevision expectedWorldRevision,
            CanonicalReader reader, ICanonicalTextResolver resolver)
        {
            WorldSimulationStateIdentity identity = ReadHeader(bytes, reader, resolver, 1000000,
                expectedNumericProfile, expectedSolverId, expectedSolverVersion, expectedWorldRevision, out int count);
            var storage = new WorldSimulationState(count, bytes.Length);
            return ReadBodies(in identity, reader, storage);
        }

        internal static WorldSimulationState Read(
            ReadOnlyMemory<byte> bytes, SimulationNumericProfile expectedNumericProfile,
            SolverImplementationId expectedSolverId, string expectedSolverVersion, WorldRevision expectedWorldRevision,
            CanonicalReader reader, ICanonicalTextResolver resolver,
            WorldSimulationState storage)
        {
            WorldSimulationStateIdentity identity = ReadHeader(bytes, reader, resolver, storage.Bodies.Count,
                expectedNumericProfile, expectedSolverId, expectedSolverVersion, expectedWorldRevision, out int count);
            if (count != storage.Bodies.Count)
                throw new InvalidDataException("World body count does not match the prepared Session roster.");
            return ReadBodies(in identity, reader, storage);
        }

        static WorldSimulationStateIdentity ReadHeader(ReadOnlyMemory<byte> bytes, CanonicalReader reader,
            ICanonicalTextResolver resolver, int maximumBodies, SimulationNumericProfile expectedNumericProfile,
            SolverImplementationId expectedSolverId, string expectedSolverVersion, WorldRevision expectedWorldRevision,
            out int count)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(resolver);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("World state header is invalid.");
            reader.RequireSchema(CanonicalSchemaHash, "World state");
            WorldSimulationStateIdentity identity = WorldSimulationStateIdentity.CanonicalReadBound(reader,
                in expectedNumericProfile, in expectedSolverId, in expectedSolverVersion, in expectedWorldRevision);
            count = reader.ReadInt32();
            if (count < 0 || count > maximumBodies)
                throw new InvalidDataException($"World body count '{count}' is invalid.");
            return identity;
        }

        static WorldSimulationState ReadBodies(in WorldSimulationStateIdentity identity,
            CanonicalReader reader, WorldSimulationState storage)
        {
            WorldBodyState[] bodies = storage.BodyArray;
            for (int i = 0; i < bodies.Length; i++)
            {
                bodies[i] = WorldBodyState.CanonicalRead(reader);
            }
            ArraySegment<byte> payload = reader.ReadBytesSegment();
            reader.RequireComplete();
            for (int i = 1; i < bodies.Length; i++)
                if (bodies[i - 1].ActorId.CompareTo(bodies[i].ActorId) >= 0)
                    throw new InvalidDataException("World bodies are not in the canonical unique Actor order.");
            storage.SetPrepared(in identity, payload.AsSpan());
            return storage;
        }





    }
}
