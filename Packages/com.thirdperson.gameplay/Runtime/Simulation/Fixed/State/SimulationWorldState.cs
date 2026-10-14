using ThirdPersonSimulation;
using System;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation.Fixed
{
    [CanonicalContract]
    public readonly partial struct WorldBodyState
    {
        public WorldBodyState(
            ActorId actorId,
            FixedVector3 position,
            FixedYaw yaw,
            FixedVector3 velocity,
            FixedScalar verticalVelocity,
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
        public FixedVector3 Position { get; }
        public FixedYaw Yaw { get; }
        public FixedVector3 Velocity { get; }
        public FixedScalar VerticalVelocity { get; }
        public bool Grounded { get; }
        public WorldCollisionSummary Collision { get; }

        [CanonicalValidation]
        void RequireRecord()
        {
            if (!ActorId.IsValid)
                throw new ArgumentException("World body ActorId is invalid.", "ActorId");
        }
    }

    public readonly struct WorldSimulationState
    {
        readonly WorldSimulationStateIdentity m_Identity;
        readonly IReadOnlyList<WorldBodyState> m_Bodies;
        readonly ReadOnlyMemory<byte> m_SolverStatePayload;
        readonly WorldSimulationStateStorage.Page m_Storage;
        readonly uint m_StorageGeneration;

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
            ReadOnlyMemory<byte> solverStatePayload)
        {
            if (!numericProfile.IsValid || string.IsNullOrEmpty(solverId.Value) || string.IsNullOrEmpty(worldRevision.Value))
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
            m_SolverStatePayload = solverStatePayload;
            m_Storage = null;
            m_StorageGeneration = 0;
        }

        WorldSimulationState(
            SimulationNumericProfile numericProfile,
            SolverImplementationId solverId,
            string solverVersion,
            WorldRevision worldRevision,
            WorldStatePersistenceMode persistenceMode,
            WorldSimulationStateStorage.Page storage)
        {
            m_Identity = new WorldSimulationStateIdentity(numericProfile, solverId, solverVersion,
                worldRevision, persistenceMode);
            m_Bodies = storage.Bodies;
            m_SolverStatePayload = storage.Payload;
            m_Storage = storage;
            m_StorageGeneration = storage.Generation;
        }

        public static WorldSimulationState FromStateStorage(
            SimulationNumericProfile numericProfile,
            SolverImplementationId solverId,
            string solverVersion,
            WorldRevision worldRevision,
            WorldStatePersistenceMode persistenceMode,
            WorldSimulationStateStorage.Page storage) =>
            new WorldSimulationState(numericProfile, solverId, solverVersion,
                worldRevision, persistenceMode, storage);

        WorldSimulationState(in WorldSimulationStateIdentity identity, WorldBodyState[] bodies,
            ReadOnlyMemory<byte> payload, WorldSimulationStateStorage.Page storage)
        {
            m_Identity = identity;
            m_Bodies = bodies;
            m_SolverStatePayload = payload;
            m_Storage = storage;
            m_StorageGeneration = storage == null ? 0 : storage.Generation;
        }

        internal static WorldSimulationState FromCanonicalState(in WorldSimulationStateIdentity identity,
            WorldBodyState[] bodies, ReadOnlyMemory<byte> payload) =>
            new WorldSimulationState(in identity, bodies, payload, null);

        internal static WorldSimulationState FromCanonicalState(in WorldSimulationStateIdentity identity,
            WorldSimulationStateStorage.Page storage) =>
            new WorldSimulationState(in identity, storage.Bodies, storage.Payload, storage);

        public void RetainStateStorage() => m_Storage?.Retain(m_StorageGeneration);
        public void ReleaseStateStorage() => m_Storage?.Release();

        public static WorldSimulationState FromOwnedState(
            SimulationNumericProfile numericProfile,
            SolverImplementationId solverId,
            string solverVersion,
            WorldRevision worldRevision,
            WorldStatePersistenceMode persistenceMode,
            WorldBodyState[] bodies,
            ReadOnlyMemory<byte> solverStatePayload) =>
            new WorldSimulationState(
                numericProfile,
                solverId,
                solverVersion,
                worldRevision,
                persistenceMode,
                bodies,
                solverStatePayload);

        public WorldSimulationStateIdentity Identity => m_Identity;
        public SimulationNumericProfile NumericProfile => m_Identity.NumericProfile;
        public SolverImplementationId SolverId => m_Identity.SolverId;
        public string SolverVersion => m_Identity.SolverVersion;
        public WorldRevision WorldRevision => m_Identity.WorldRevision;
        public WorldStatePersistenceMode PersistenceMode => m_Identity.PersistenceMode;
        public IReadOnlyList<WorldBodyState> Bodies
        {
            get
            {
                m_Storage?.RequireGeneration(m_StorageGeneration);
                return m_Bodies;
            }
        }
        public ReadOnlyMemory<byte> SolverStatePayload
        {
            get
            {
                m_Storage?.RequireGeneration(m_StorageGeneration);
                return m_SolverStatePayload;
            }
        }

        public WorldSimulationState Clone() =>
            new WorldSimulationState(
                NumericProfile,
                SolverId,
                SolverVersion,
                WorldRevision,
                PersistenceMode,
                CopyBodies(Bodies),
                CopySolverStatePayload(SolverStatePayload));

        static WorldBodyState[] CopyBodies(IReadOnlyList<WorldBodyState> bodies)
        {
            if (bodies == null || bodies.Count == 0)
                return Array.Empty<WorldBodyState>();
            var copied = new WorldBodyState[bodies.Count];
            for (int i = 0; i < copied.Length; i++)
                copied[i] = bodies[i];
            return copied;
        }

        static ReadOnlyMemory<byte> CopySolverStatePayload(ReadOnlyMemory<byte> solverStatePayload) =>
            solverStatePayload.IsEmpty ? ReadOnlyMemory<byte>.Empty : solverStatePayload.ToArray();
    }

    public sealed class WorldSimulationStateStorage
    {
        public sealed class Page
        {
            int m_References;
            readonly byte[] m_Payload;

            internal Page(int actorCount, int payloadCapacity)
            {
                Bodies = new WorldBodyState[actorCount];
                Results = new CharacterWorldSolveResult[actorCount];
                m_Payload = new byte[payloadCapacity];
                Writer = new CanonicalWriter(m_Payload);
            }

            public WorldBodyState[] Bodies { get; }
            public CharacterWorldSolveResult[] Results { get; }
            public CanonicalWriter Writer { get; }
            public ReadOnlyMemory<byte> Payload => m_Payload.AsMemory(0, checked((int)Writer.Length));
            internal uint Generation { get; private set; }
            internal bool IsReleased => m_References == 0;

            internal void BeginWrite()
            {
                Generation++;
                m_References = 1;
                Writer.Reset();
            }

            internal void RequireGeneration(uint generation)
            {
                if (Generation != generation)
                    throw new InvalidOperationException("World state storage has already been reused.");
            }

            internal void Retain(uint generation)
            {
                RequireGeneration(generation);
                m_References++;
            }

            public void Release() => m_References--;
        }

        readonly int m_ActorCount;
        readonly int m_PayloadCapacity;
        readonly List<Page> m_Pages = new List<Page>(2);

        public int PayloadCapacity => m_PayloadCapacity;
        internal int ActorCount => m_ActorCount;

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

        internal void PrepareCapacity(int statePageCount)
        {
            m_Pages.Capacity = statePageCount;
            while (m_Pages.Count < statePageCount)
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
            "fixed-world-state/" + Version, WorldSimulationStateIdentity.CanonicalSchema,
            WorldBodyState.CanonicalSchema);
        static readonly StableHash CanonicalSchemaHash = new StableHash(CanonicalSchema);

        [ThreadStatic] static CanonicalWriter s_Writer;

        public static byte[] Write(WorldSimulationState state)
        {
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
            CanonicalWriter writer = PrepareWriter();
            WriteCanonical(writer, state);
            return writer.ComputeHash();
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
            reader.Reset(bytes);
            reader.SetTextResolver(resolver);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("World state header is invalid.");
            reader.RequireSchema(CanonicalSchemaHash, "World state");
            WorldSimulationStateIdentity identity = WorldSimulationStateIdentity.CanonicalReadBound(reader,
                in expectedNumericProfile, in expectedSolverId, in expectedSolverVersion, in expectedWorldRevision);
            int count = reader.ReadInt32();
            if (count < 0 || count > 1000000)
                throw new InvalidDataException($"World body count '{count}' is invalid.");
            var bodies = new WorldBodyState[count];
            for (int i = 0; i < count; i++)
            {
                bodies[i] = WorldBodyState.CanonicalRead(reader);
            }
            ArraySegment<byte> payload = reader.ReadBytesSegment();
            reader.RequireComplete();
            for (int i = 1; i < bodies.Length; i++)
                if (bodies[i - 1].ActorId.CompareTo(bodies[i].ActorId) >= 0)
                    throw new InvalidDataException("World bodies are not in the canonical unique Actor order.");
            return WorldSimulationState.FromCanonicalState(in identity, bodies,
                payload.Count == 0 ? Array.Empty<byte>() : payload.AsSpan().ToArray());
        }

        internal static WorldSimulationState ReadPrepared(
            ReadOnlyMemory<byte> bytes, SimulationNumericProfile expectedNumericProfile,
            SolverImplementationId expectedSolverId, string expectedSolverVersion, WorldRevision expectedWorldRevision,
            CanonicalReader reader, ICanonicalTextResolver resolver, WorldSimulationStateStorage storage,
            IReadOnlyList<WorldBodyState> publishedBodies)
        {
            WorldSimulationStateStorage.Page page = storage.BeginWrite();
            try
            {
                reader.Reset(bytes);
                reader.SetTextResolver(resolver);
                if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                    throw new InvalidDataException("World state header is invalid.");
                reader.RequireSchema(CanonicalSchemaHash, "World state");
                WorldSimulationStateIdentity identity = WorldSimulationStateIdentity.CanonicalReadBound(reader,
                    in expectedNumericProfile, in expectedSolverId, in expectedSolverVersion, in expectedWorldRevision);
                int count = reader.ReadInt32();
                if (count != storage.ActorCount)
                    throw new InvalidDataException("World body count does not match the prepared Session roster.");
                WorldBodyState[] bodies = page.Bodies;
                for (int i = 0; i < count; i++)
                {
                    ActorId actor = publishedBodies[i].ActorId;
                    bodies[i] = WorldBodyState.CanonicalReadBound(reader, in actor);
                }
                ArraySegment<byte> payload = reader.ReadBytesSegment();
                reader.RequireComplete();
                page.Writer.WriteRawBytes(payload.AsSpan());
                return WorldSimulationState.FromCanonicalState(in identity, page);
            }
            catch
            {
                page.Release();
                throw;
            }
        }

        public static ArraySegment<byte> ReadSolverStatePayloadSegment(
            ReadOnlyMemory<byte> bytes, in SimulationNumericProfile expectedNumericProfile,
            SolverImplementationId expectedSolverId, string expectedSolverVersion, WorldRevision expectedWorldRevision,
            CanonicalReader reader)
        {
            reader.Reset(bytes);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("World state header is invalid.");
            reader.RequireSchema(CanonicalSchemaHash, "World state");
            WorldSimulationStateIdentity.CanonicalReadBound(reader, in expectedNumericProfile,
                in expectedSolverId, in expectedSolverVersion, in expectedWorldRevision);
            int count = reader.ReadInt32();
            if (count < 0 || count > 1000000)
                throw new InvalidDataException($"World body count '{count}' is invalid.");
            for (int i = 0; i < count; i++)
            {
                WorldBodyState.CanonicalSkip(reader);
            }
            ArraySegment<byte> payload = reader.ReadBytesSegment();
            reader.RequireComplete();
            return payload;
        }



    }
}
