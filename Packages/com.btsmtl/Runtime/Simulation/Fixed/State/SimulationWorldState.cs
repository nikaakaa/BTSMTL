using ThirdPersonSimulation;
using System;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation.Fixed
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
            FixedVector3 position,
            FixedYaw yaw,
            FixedVector3 velocity,
            FixedScalar verticalVelocity,
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
        public FixedVector3 Position { get; }
        public FixedYaw Yaw { get; }
        public FixedVector3 Velocity { get; }
        public FixedScalar VerticalVelocity { get; }
        public bool Grounded { get; }
        public WorldCollisionSummary Collision { get; }
    }

    public readonly struct WorldSimulationState
    {
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
            NumericProfile = numericProfile;
            SolverId = solverId;
            SolverVersion = solverVersion;
            WorldRevision = worldRevision;
            PersistenceMode = persistenceMode;
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

        public SimulationNumericProfile NumericProfile { get; }
        public SolverImplementationId SolverId { get; }
        public string SolverVersion { get; }
        public WorldRevision WorldRevision { get; }
        public WorldStatePersistenceMode PersistenceMode { get; }
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
        const int Version = 5;

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
            SimulationNumericProfile.CanonicalWrite(writer, state.NumericProfile);
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
            reader.Reset(bytes);
            reader.SetTextResolver(resolver);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("World state header is invalid.");
            SimulationNumericProfile numericProfile = SimulationNumericProfile.ReadPublished(reader, expectedNumericProfile);
            reader.RequireText(expectedSolverId.Value);
            reader.RequireText(expectedSolverVersion);
            reader.RequireText(expectedWorldRevision.Value);
            SolverImplementationId solverId = expectedSolverId;
            string solverVersion = expectedSolverVersion;
            WorldRevision worldRevision = expectedWorldRevision;
            WorldStatePersistenceMode persistenceMode = ReadPersistenceMode(reader.ReadByte());
            int count = reader.ReadInt32();
            if (count < 0 || count > 1000000)
                throw new InvalidDataException($"World body count '{count}' is invalid.");
            var bodies = new WorldBodyState[count];
            for (int i = 0; i < count; i++)
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
            if (numericProfile != expectedNumericProfile || !solverId.Equals(expectedSolverId) || !string.Equals(solverVersion, expectedSolverVersion, StringComparison.Ordinal) || !worldRevision.Equals(expectedWorldRevision))
                throw new InvalidDataException("World state Numeric Profile, Solver, or revision binding is stale or mismatched.");
            var result = WorldSimulationState.FromOwnedState(
                numericProfile,
                solverId,
                solverVersion,
                worldRevision,
                persistenceMode,
                bodies,
                payload.Count == 0 ? Array.Empty<byte>() : payload.AsSpan().ToArray());
            writer.Reset();
            writer.SetTextCapture(null);
            WriteCanonical(writer, result);
            if (!writer.ContentEquals(bytes.Span))
                throw new InvalidDataException("World state is not canonical.");
            return result;
        }

        internal static WorldSimulationState ReadPrepared(
            ReadOnlyMemory<byte> bytes, SimulationNumericProfile expectedNumericProfile,
            SolverImplementationId expectedSolverId, string expectedSolverVersion, WorldRevision expectedWorldRevision,
            CanonicalReader reader, CanonicalWriter writer, ICanonicalTextResolver resolver, WorldSimulationStateStorage storage,
            IReadOnlyList<WorldBodyState> publishedBodies)
        {
            WorldSimulationStateStorage.Page page = storage.BeginWrite();
            try
            {
                reader.Reset(bytes);
                reader.SetTextResolver(resolver);
                if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                    throw new InvalidDataException("World state header is invalid.");
                SimulationNumericProfile numericProfile = SimulationNumericProfile.ReadPublished(reader, expectedNumericProfile);
                reader.RequireText(expectedSolverId.Value);
                reader.RequireText(expectedSolverVersion);
                reader.RequireText(expectedWorldRevision.Value);
                SolverImplementationId solverId = expectedSolverId;
                string solverVersion = expectedSolverVersion;
                WorldRevision worldRevision = expectedWorldRevision;
                WorldStatePersistenceMode persistenceMode = ReadPersistenceMode(reader.ReadByte());
                int count = reader.ReadInt32();
                if (count != storage.ActorCount)
                    throw new InvalidDataException("World body count does not match the prepared Session roster.");
                WorldBodyState[] bodies = page.Bodies;
                for (int i = 0; i < count; i++)
                {
                    ActorId actor = publishedBodies[i].ActorId;
                    reader.RequireText(actor.Value);
                    bodies[i] = new WorldBodyState(
                        actor,
                        reader.ReadVector3(),
                        reader.ReadYaw(),
                        reader.ReadVector3(),
                        reader.ReadScalar(),
                        reader.ReadBoolean(),
                        (WorldCollisionSummary)reader.ReadUInt32());
                }
                ArraySegment<byte> payload = reader.ReadBytesSegment();
                reader.RequireComplete();
                page.Writer.WriteRawBytes(payload.AsSpan());
                WorldSimulationState result = WorldSimulationState.FromStateStorage(
                    numericProfile, solverId, solverVersion, worldRevision, persistenceMode, page);
                writer.Reset();
                writer.SetTextCapture(null);
                WriteCanonical(writer, result);
                if (!writer.ContentEquals(bytes.Span))
                    throw new InvalidDataException("World state is not canonical.");
                return result;
            }
            catch
            {
                page.Release();
                throw;
            }
        }

        public static ArraySegment<byte> ReadSolverStatePayloadSegment(
            ReadOnlyMemory<byte> bytes, in SimulationNumericProfile expectedNumericProfile, CanonicalReader reader)
        {
            reader.Reset(bytes);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("World state header is invalid.");
            SimulationNumericProfile.ReadPublished(reader, expectedNumericProfile);
            reader.ReadTextSegment();
            reader.ReadTextSegment();
            reader.ReadTextSegment();
            ReadPersistenceMode(reader.ReadByte());
            int count = reader.ReadInt32();
            if (count < 0 || count > 1000000)
                throw new InvalidDataException($"World body count '{count}' is invalid.");
            for (int i = 0; i < count; i++)
            {
                ArraySegment<byte> actorId = reader.ReadTextSegment();
                if (actorId.Count == 0)
                    throw new InvalidDataException("World state Actor identity is invalid.");
                reader.ReadVector3();
                reader.ReadYaw();
                reader.ReadVector3();
                reader.ReadScalar();
                reader.ReadBoolean();
                reader.ReadUInt32();
            }
            ArraySegment<byte> payload = reader.ReadBytesSegment();
            reader.RequireComplete();
            return payload;
        }

        static WorldStatePersistenceMode ReadPersistenceMode(byte value)
        {
            if ((WorldStatePersistenceMode)value is not (WorldStatePersistenceMode.Reconstruct or WorldStatePersistenceMode.Snapshot))
                throw new InvalidDataException($"World persistence mode '{value}' is invalid.");
            return (WorldStatePersistenceMode)value;
        }

    }
}
