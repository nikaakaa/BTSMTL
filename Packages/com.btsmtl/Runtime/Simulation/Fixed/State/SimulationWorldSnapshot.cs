using System;
using System.Collections.Generic;
using System.IO;
using ThirdPersonSimulation;
namespace ThirdPersonSimulation.Fixed
{
    public readonly struct SimulationActorState
    {
        public SimulationActorState(ActorId actorId, FixedCharacterRuntimeState state)
        {
            if (!actorId.IsValid)
                throw new ArgumentException("ActorId is invalid.", nameof(actorId));
            ActorId = actorId;
            State = state;
        }

        public ActorId ActorId { get; }
        public FixedCharacterRuntimeState State { get; }
    }

    public sealed class SimulationActorSnapshot
    {
        ReadOnlyMemory<byte> m_StateBytes;
        readonly CanonicalReader m_StateReader;
        readonly CanonicalWriter m_CanonicalWriter;
        ICanonicalTextResolver m_TextResolver;

        public SimulationActorSnapshot(
            ActorId actorId,
            GameplayContentHash gameplayContentHash,
            StableHash stateSchemaHash,
            CharacterStateHash stateHash,
            string stateCodecIdentity,
            byte[] ownedStateBytes)
        {
            if (!actorId.IsValid || !gameplayContentHash.IsValid || !stateSchemaHash.IsValid || !stateHash.IsValid ||
                !string.Equals(stateCodecIdentity, FixedCharacterRuntimeStateCodec.CodecIdentity, StringComparison.Ordinal))
            {
                throw new ArgumentException("Actor snapshot identity is incomplete.");
            }
            ActorId = actorId;
            GameplayContentHash = gameplayContentHash;
            StateSchemaHash = stateSchemaHash;
            StateHash = stateHash;
            StateCodecIdentity = stateCodecIdentity;
            m_StateBytes = ownedStateBytes ?? throw new ArgumentNullException(nameof(ownedStateBytes));
            m_StateReader = new CanonicalReader(Array.Empty<byte>());
            m_CanonicalWriter = new CanonicalWriter();
        }

        public ActorId ActorId { get; }
        public GameplayContentHash GameplayContentHash { get; }
        public StableHash StateSchemaHash { get; }
        public CharacterStateHash StateHash { get; private set; }
        public string StateCodecIdentity { get; }
        public ReadOnlyMemory<byte> StateBytes => m_StateBytes;
        internal SimulationActorSnapshot(SimulationActorBinding actor, CanonicalWriter writer)
        {
            ActorId = actor.ActorId;
            GameplayContentHash = new GameplayContentHash(actor.GameplayContentHash);
            StateSchemaHash = actor.StateSchemaHash;
            StateCodecIdentity = FixedCharacterRuntimeStateCodec.CodecIdentity;
            m_StateReader = new CanonicalReader(Array.Empty<byte>());
            m_CanonicalWriter = writer;
        }

        internal void SetPreparedPayload(CharacterStateHash hash, ReadOnlyMemory<byte> bytes, ICanonicalTextResolver resolver)
        {
            StateHash = hash;
            m_StateBytes = bytes;
            m_TextResolver = resolver;
        }

        internal void ClearPreparedPayload()
        {
            m_StateBytes = ReadOnlyMemory<byte>.Empty;
            m_TextResolver = null;
            m_StateReader.SetTextResolver(null);
            m_StateReader.Reset(Array.Empty<byte>());
        }

        public FixedCharacterRuntimeState Decode(
            SimulationActorBinding actor)
        {
            if (actor == null)
                throw new InvalidDataException($"Actor '{ActorId}' snapshot Ability binding is stale or mismatched.");
            using SimulationCharacterStatePayloadStorage.Page encoding = actor.RuntimeStatePages.EncodingStorage?.Acquire();
            return Decode(actor, encoding);
        }

        internal FixedCharacterRuntimeState Decode(SimulationActorBinding actor,
            SimulationCharacterStatePayloadStorage.Page encoding)
        {
            if (actor.ActorId != ActorId ||
                !GameplayContentHash.Equals(new GameplayContentHash(actor.GameplayContentHash)) ||
                !StateSchemaHash.Equals(actor.StateSchemaHash))
                throw new InvalidDataException($"Actor '{ActorId}' snapshot Character Runtime binding is stale or mismatched.");
            FixedCharacterRuntimeState state = FixedCharacterRuntimeStateCodec.Read(
                m_StateBytes,
                actor, m_StateReader, m_CanonicalWriter, m_TextResolver, encoding, out CharacterStateHash hash);
            if (!hash.Equals(StateHash))
            {
                state.DiscardDecodedStorage();
                foreach (GameplayAbilityExecutionStateBank<AbilityStateValue> bank in actor.ExecutionStatePages.Values)
                    bank.ReleaseCurrent();
                actor.GameplayEffectState.Dispose();
                actor.EquipmentState.Storage?.ReleaseDecodedState();
                throw new InvalidDataException($"Actor '{ActorId}' Character runtime state hash is invalid.");
            }
            return state;
        }
    }

    public sealed class SimulationWorldSnapshot
    {
        readonly IReadOnlyList<SimulationActorSnapshot> m_Actors;
        ReadOnlyMemory<byte> m_WorldStateBytes;
        readonly CanonicalReader m_WorldReader;
        readonly CanonicalWriter m_CanonicalWriter;
        ICanonicalTextResolver m_TextResolver;

        public SimulationWorldSnapshot(
            SimulationNumericProfile numericProfile,
            GameplayContentHash gameplayContentHash,
            StableHash stateSchemaHash,
            SolverImplementationId solverId,
            string solverVersion,
            WorldRevision worldRevision,
            SimulationTick tick,
            IReadOnlyList<SimulationActorSnapshot> actors,
            byte[] worldStateBytes,
            bool deterministicValidity)
            : this(
                numericProfile,
                gameplayContentHash,
                stateSchemaHash,
                solverId,
                solverVersion,
                worldRevision,
                tick,
                CopyActors(actors),
                CopyWorldStateBytes(worldStateBytes),
                deterministicValidity)
        {
        }

        internal SimulationWorldSnapshot(
            SimulationNumericProfile numericProfile,
            GameplayContentHash gameplayContentHash,
            StableHash stateSchemaHash,
            SolverImplementationId solverId,
            string solverVersion,
            WorldRevision worldRevision,
            SimulationTick tick,
            SimulationActorSnapshot[] actors,
            byte[] worldStateBytes,
            bool deterministicValidity)
        {
            if (!numericProfile.IsValid || !gameplayContentHash.IsValid || !stateSchemaHash.IsValid || string.IsNullOrEmpty(solverId.Value) || string.IsNullOrEmpty(worldRevision.Value) || !tick.IsValid)
                throw new ArgumentException("Simulation World Snapshot header is incomplete.");
            NumericProfile = numericProfile;
            GameplayContentHash = gameplayContentHash;
            StateSchemaHash = stateSchemaHash;
            SolverId = solverId;
            SolverVersion = SimulationIdentity.Require(solverVersion, nameof(solverVersion));
            WorldRevision = worldRevision;
            Tick = tick;
            SimulationActorSnapshot[] copied = actors ?? throw new ArgumentNullException(nameof(actors));
            for (int i = 0; i < copied.Length; i++)
                if (copied[i] == null)
                    throw new ArgumentException("Simulation World Snapshot actor roster contains a null entry.", nameof(actors));
            if (copied.Length == 0)
                throw new ArgumentException("Simulation World Snapshot actor roster cannot be empty.", nameof(actors));
            ValidatePreparedActors(copied);
            m_Actors = copied;
            m_WorldStateBytes = worldStateBytes ?? throw new ArgumentNullException(nameof(worldStateBytes));
            m_WorldReader = new CanonicalReader(Array.Empty<byte>());
            m_CanonicalWriter = new CanonicalWriter();
            DeterministicValidity = deterministicValidity;
            WorldHash = SimulationWorldSnapshotCodec.ComputeHash(this);
        }

        public SimulationNumericProfile NumericProfile { get; private set; }
        public GameplayContentHash GameplayContentHash { get; private set; }
        public StableHash StateSchemaHash { get; private set; }
        public SolverImplementationId SolverId { get; private set; }
        public string SolverVersion { get; private set; }
        public WorldRevision WorldRevision { get; private set; }
        public SimulationTick Tick { get; private set; }
        public IReadOnlyList<SimulationActorSnapshot> Actors => m_Actors;
        public ReadOnlyMemory<byte> WorldStateBytes => m_WorldStateBytes;
        internal ReadOnlyMemory<byte> WorldStatePayload => m_WorldStateBytes;
        public bool DeterministicValidity { get; private set; }
        public SimulationWorldHash WorldHash { get; private set; }

        internal SimulationWorldSnapshot(SimulationActorSnapshot[] actors, CanonicalWriter writer, WorldSimulationState publishedWorld)
        {
            m_Actors = actors;
            m_WorldReader = new CanonicalReader(Array.Empty<byte>());
            m_CanonicalWriter = writer;
            NumericProfile = publishedWorld.NumericProfile;
            SolverId = publishedWorld.SolverId;
            SolverVersion = publishedWorld.SolverVersion;
            WorldRevision = publishedWorld.WorldRevision;
        }

        internal void SetPrepared(
            SimulationNumericProfile numericProfile, GameplayContentHash content, StableHash schema,
            SolverImplementationId solverId, string solverVersion, WorldRevision worldRevision,
            SimulationTick tick, bool deterministic, ReadOnlyMemory<byte> payload,
            SimulationWorldHash hash, ICanonicalTextResolver resolver)
        {
            NumericProfile = numericProfile;
            GameplayContentHash = content;
            StateSchemaHash = schema;
            SolverId = solverId;
            SolverVersion = solverVersion;
            WorldRevision = worldRevision;
            Tick = tick;
            DeterministicValidity = deterministic;
            m_WorldStateBytes = payload;
            WorldHash = hash;
            m_TextResolver = resolver;
        }

        internal void ClearPreparedPayload()
        {
            m_WorldStateBytes = ReadOnlyMemory<byte>.Empty;
            m_TextResolver = null;
            m_WorldReader.SetTextResolver(null);
            m_WorldReader.Reset(Array.Empty<byte>());
            for (int index = 0; index < m_Actors.Count; index++)
                m_Actors[index].ClearPreparedPayload();
        }

        static SimulationActorSnapshot[] CopyActors(IReadOnlyList<SimulationActorSnapshot> actors)
        {
            if (actors == null || actors.Count == 0)
                return Array.Empty<SimulationActorSnapshot>();
            var copied = new SimulationActorSnapshot[actors.Count];
            for (int i = 0; i < copied.Length; i++)
                copied[i] = actors[i];
            return copied;
        }

        static byte[] CopyWorldStateBytes(byte[] worldStateBytes) =>
            worldStateBytes == null
                ? throw new ArgumentNullException(nameof(worldStateBytes))
                : (byte[])worldStateBytes.Clone();

        internal WorldSimulationState DecodeWorldState(WorldSimulationStateStorage storage, IReadOnlyList<WorldBodyState> publishedBodies) =>
            WorldSimulationStateCodec.ReadPrepared(m_WorldStateBytes, NumericProfile, SolverId, SolverVersion, WorldRevision,
                m_WorldReader, m_CanonicalWriter, m_TextResolver, storage, publishedBodies);

        public WorldSimulationState DecodeWorldState() =>
            WorldSimulationStateCodec.Read(m_WorldStateBytes, NumericProfile, SolverId, SolverVersion, WorldRevision,
                m_WorldReader, m_CanonicalWriter, m_TextResolver);

        public StableHash ComputeSolverStatePayloadHash() =>
            SimulationCanonicalPayloadHash.Compute(
                WorldSimulationStateCodec.ReadSolverStatePayloadSegment(m_WorldStateBytes, NumericProfile, m_WorldReader).AsSpan());

        static void ValidatePreparedActors(SimulationActorSnapshot[] actors)
        {
            for (int i = 1; i < actors.Length; i++)
                if (actors[i - 1].ActorId.CompareTo(actors[i].ActorId) >= 0)
                    throw new ArgumentException("Simulation World Snapshot actor roster is not in a stable unique ActorId order.", nameof(actors));
        }

        sealed class ActorSnapshotComparer : IComparer<SimulationActorSnapshot>
        {
            public static readonly ActorSnapshotComparer Instance = new ActorSnapshotComparer();

            ActorSnapshotComparer() { }

            public int Compare(SimulationActorSnapshot left, SimulationActorSnapshot right)
            {
                return left.ActorId.CompareTo(right.ActorId);
            }
        }
    }

    public static class SimulationWorldSnapshotFactory
    {
        [ThirdPersonPerformance.Instrumentation.PerformanceProbe("simulation.snapshot.world-capture")]
        public static SimulationWorldSnapshot Capture(
            FixedCharacterRuntime characterRuntime,
            SimulationTick tick,
            SimulationActorState[] actorStates,
            WorldSimulationState worldState,
            WorldCapability solverCapabilities)
        {
            if (characterRuntime == null)
                throw new ArgumentNullException(nameof(characterRuntime));
            if (characterRuntime.NumericProfile != worldState.NumericProfile)
                throw new InvalidOperationException("Character Runtime and World state Numeric Profiles do not match.");
            if (actorStates == null)
                throw new ArgumentNullException(nameof(actorStates));
            if (actorStates.Length == 0)
                throw new InvalidOperationException("Character runtime state roster contains no entry.");
            if (actorStates.Length != worldState.Bodies.Count || actorStates.Length != characterRuntime.Roster.Count)
                throw new InvalidOperationException("Character runtime state, Character Runtime and World body rosters do not match.");
            var snapshots = new SimulationActorSnapshot[actorStates.Length];
            bool abilitiesDeterministic = true;
            for (int i = 0; i < actorStates.Length; i++)
            {
                SimulationActorState actor = actorStates[i];
                SimulationActorBinding binding = characterRuntime.Roster[i];
                if (!actor.State.IsValid || i > 0 && actorStates[i - 1].ActorId == actor.ActorId ||
                    worldState.Bodies[i].ActorId != actor.ActorId || binding.ActorId != actor.ActorId)
                {
                    throw new InvalidOperationException("Character runtime state, Character Runtime and World body rosters are not the same stable ActorId order.");
                }
                GameplayContentHash actorContentHash = new GameplayContentHash(binding.GameplayContentHash);
                if (!actor.State.GameplayContentHash.Equals(actorContentHash))
                    throw new InvalidOperationException($"Actor '{actor.ActorId}' Character runtime state identity does not match Character Runtime binding.");
                if (!actor.State.StateSchemaHash.Equals(binding.StateSchemaHash))
                    throw new InvalidOperationException($"Actor '{actor.ActorId}' Character runtime state schema does not match Character Runtime binding.");
                if (actor.State.NumericProfile != characterRuntime.NumericProfile)
                    throw new InvalidOperationException($"Actor '{actor.ActorId}' Character runtime state Numeric Profile does not match Character Runtime.");
                byte[] ownedStateBytes = FixedCharacterRuntimeStateCodec.Write(actor.State, out CharacterStateHash stateHash);
                snapshots[i] = new SimulationActorSnapshot(
                    actor.ActorId,
                    actor.State.GameplayContentHash,
                    actor.State.StateSchemaHash,
                    stateHash,
                    FixedCharacterRuntimeStateCodec.CodecIdentity,
                    ownedStateBytes);
                for (int abilityIndex = 0; abilityIndex < binding.AbilityInstallations.Installations.Count; abilityIndex++)
                {
                    FixedGameplayAbilityExecutionData ability = binding.AbilityInstallations.Installations[abilityIndex].Data;
                    abilitiesDeterministic &= ability.NumericProfile.DeterministicReplay && ability.Capabilities.HasGameplayCapability("DeterministicReplay");
                }
            }
            bool deterministicValidity = characterRuntime.NumericProfile.DeterministicReplay && abilitiesDeterministic &&
                (solverCapabilities & WorldCapability.DeterministicReplay) != 0;
            return new SimulationWorldSnapshot(
                characterRuntime.NumericProfile,
                characterRuntime.GameplayContentHash,
                characterRuntime.StateSchemaHash,
                worldState.SolverId,
                worldState.SolverVersion,
                worldState.WorldRevision,
                tick,
                snapshots,
                WorldSimulationStateCodec.Write(worldState),
                deterministicValidity);
        }
    }

    public readonly struct SimulationWorldStateSet
    {
        readonly SimulationActorState[] m_Actors;
        readonly SimulationWorldStateSetStorage.Page m_Storage;
        readonly uint m_StorageGeneration;

        public SimulationWorldStateSet(ulong lastCompletedTick, IReadOnlyList<SimulationActorState> actors, WorldSimulationState worldState)
        {
            LastCompletedTick = lastCompletedTick;
            WorldState = worldState;
            var copied = actors == null || actors.Count == 0
                ? Array.Empty<SimulationActorState>()
                : new SimulationActorState[actors.Count];
            for (int i = 0; i < copied.Length; i++)
            {
                copied[i] = actors[i];
                if (!copied[i].State.IsValid)
                    throw new ArgumentException("Simulation state Actor roster contains a null entry.", nameof(actors));
            }
            Array.Sort(copied, ActorStateComparer.Instance);
            ValidatePrepared(copied, worldState);
            m_Actors = copied;
            m_Storage = null;
            m_StorageGeneration = 0;
        }

        internal static SimulationWorldStateSet FromStateStorage(
            ulong lastCompletedTick,
            SimulationWorldStateSetStorage.Page storage,
            WorldSimulationState worldState)
        {
            return new SimulationWorldStateSet(lastCompletedTick, storage, worldState);
        }

        SimulationWorldStateSet(ulong lastCompletedTick, SimulationWorldStateSetStorage.Page storage, WorldSimulationState worldState)
        {
            LastCompletedTick = lastCompletedTick;
            WorldState = worldState;
            m_Actors = storage.Actors;
            m_Storage = storage;
            m_StorageGeneration = storage.Generation;
        }

        public ulong LastCompletedTick { get; }
        public bool IsValid => m_Actors != null;
        public IReadOnlyList<SimulationActorState> Actors => ActorArray;
        internal SimulationActorState[] ActorArray
        {
            get
            {
                m_Storage?.RequireGeneration(m_StorageGeneration);
                return m_Actors;
            }
        }
        public WorldSimulationState WorldState { get; }

        internal void RetainActorStorage() => m_Storage?.Retain(m_StorageGeneration);
        internal void ReleaseActorStorage() => m_Storage?.Release();
        internal bool SharesStateStorage(SimulationWorldStateSet other) =>
            ReferenceEquals(m_Actors, other.m_Actors) &&
            m_StorageGeneration == other.m_StorageGeneration &&
            LastCompletedTick == other.LastCompletedTick;

        static void ValidatePrepared(
            SimulationActorState[] actors,
            WorldSimulationState worldState)
        {
            if (actors.Length == 0 || actors.Length != worldState.Bodies.Count)
                throw new ArgumentException("Simulation state Actor and World body rosters must be non-empty and equal.", nameof(actors));
            for (int i = 0; i < actors.Length; i++)
            {
                if (!actors[i].State.IsValid)
                    throw new ArgumentException("Simulation state Actor roster contains a null entry.", nameof(actors));
                if (actors[i].State.NumericProfile != worldState.NumericProfile ||
                    actors[i].ActorId != worldState.Bodies[i].ActorId ||
                    i > 0 && actors[i - 1].ActorId == actors[i].ActorId)
                {
                    throw new ArgumentException("Simulation state Actor and World body rosters must share one stable ActorId order.", nameof(actors));
                }
            }
        }

        sealed class ActorStateComparer : IComparer<SimulationActorState>
        {
            public static readonly ActorStateComparer Instance = new ActorStateComparer();

            ActorStateComparer() { }

            public int Compare(SimulationActorState left, SimulationActorState right)
            {
                return left.ActorId.CompareTo(right.ActorId);
            }
        }
    }

    internal sealed class SimulationWorldStateSetStorage
    {
        internal sealed class Page
        {
            int m_References;

            internal Page(int actorCount) => Actors = new SimulationActorState[actorCount];
            internal SimulationActorState[] Actors { get; }
            internal uint Generation { get; private set; }
            internal bool IsReleased => m_References == 0;

            internal void BeginWrite()
            {
                Generation++;
                m_References = 1;
            }

            internal void RequireGeneration(uint generation)
            {
                if (Generation != generation)
                    throw new InvalidOperationException("World Actor state storage has already been reused.");
            }

            internal void Retain(uint generation)
            {
                RequireGeneration(generation);
                m_References++;
            }

            internal void Release()
            {
                if (--m_References == 0)
                    Array.Clear(Actors, 0, Actors.Length);
            }
        }

        readonly int m_ActorCount;
        readonly List<Page> m_Pages = new List<Page>(2);

        internal SimulationWorldStateSetStorage(int actorCount)
        {
            m_ActorCount = actorCount;
            m_Pages.Add(new Page(actorCount));
            m_Pages.Add(new Page(actorCount));
        }

        internal void PrepareStepStorage(int maximumSteps)
        {
            int count = checked(maximumSteps + 4);
            m_Pages.Capacity = Math.Max(m_Pages.Capacity, count);
            while (m_Pages.Count < count)
                m_Pages.Add(new Page(m_ActorCount));
        }

        internal void PrepareCapacity(int statePageCount)
        {
            m_Pages.Capacity = statePageCount;
            while (m_Pages.Count < statePageCount)
                m_Pages.Add(new Page(m_ActorCount));
        }

        internal Page BeginWrite()
        {
            for (int i = 0; i < m_Pages.Count; i++)
            {
                Page page = m_Pages[i];
                if (!page.IsReleased)
                    continue;
                page.BeginWrite();
                return page;
            }
            throw new InvalidOperationException("World Actor state storage exceeded its prepared transaction capacity.");
        }
    }

    public sealed class SimulationWorldStateStore : IDisposable
    {
        readonly FixedCharacterRuntime m_Runtime;
        readonly SimulationWorldStateSetStorage m_StateStorage;
        WorldSimulationStateStorage m_RestoreWorldStorage;
        SimulationWorldStateSet m_Current;
        struct RecordedFrame
        {
            internal ulong PresentationFrame;
            internal SimulationWorldStateSet State;
            internal SimulationPipelinePassStateSnapshot[] Pipeline;
        }
        RecordedFrame[] m_RecordedFrames = Array.Empty<RecordedFrame>();
        int m_RecordedCount;
        int m_RecordedNext;

        public SimulationWorldStateStore(FixedCharacterRuntime runtime, SimulationWorldStateSet initialState)
        {
            m_Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            m_Current = initialState;
            ValidateCurrentBindings(initialState);
            m_StateStorage = new SimulationWorldStateSetStorage(initialState.Actors.Count);
            RetainStatePages(initialState);
        }

        public SimulationWorldStateSet Current => m_Current;

        public void Dispose()
        {
            ClearRecordedFrames();
            ReleaseStatePages(m_Current);
            m_Current = default;
        }
        internal void PrepareRestoreStorage(int statePageCount, int maximumSolverPayloadBytes)
        {
            m_StateStorage.PrepareCapacity(statePageCount);
            m_RestoreWorldStorage = new WorldSimulationStateStorage(m_Runtime.Roster.Count, maximumSolverPayloadBytes);
            m_RestoreWorldStorage.PrepareCapacity(statePageCount);
        }

        internal int RecordedFrameCapacity => m_RecordedFrames.Length;
        internal void PrepareStepStorage(int maximumSteps) => m_StateStorage.PrepareStepStorage(maximumSteps);
        internal SimulationWorldStateSetStorage.Page BeginCandidateState() => m_StateStorage.BeginWrite();

        internal void PrepareRecordedFrames(int capacity, int participantCount)
        {
            ClearRecordedFrames();
            m_RecordedFrames = new RecordedFrame[capacity];
            for (int i = 0; i < capacity; i++)
                m_RecordedFrames[i].Pipeline = new SimulationPipelinePassStateSnapshot[participantCount];
        }

        internal void RecordFrame(ulong presentationFrame, ReadOnlySpan<SimulationPipelinePassStateSnapshot> pipeline)
        {
            ref RecordedFrame frame = ref m_RecordedFrames[m_RecordedNext];
            if (m_RecordedCount == m_RecordedFrames.Length)
                ReleaseRecordedFrame(ref frame);
            RetainStatePages(m_Current);
            frame.PresentationFrame = presentationFrame;
            frame.State = m_Current;
            pipeline.CopyTo(frame.Pipeline);
            m_RecordedNext = (m_RecordedNext + 1) % m_RecordedFrames.Length;
            m_RecordedCount = Math.Min(m_RecordedCount + 1, m_RecordedFrames.Length);
        }

        internal bool TryGetRecordedFrame(ulong presentationFrame, out SimulationWorldStateSet state,
            out SimulationPipelinePassStateSnapshot[] pipeline)
        {
            for (int i = 0; i < m_RecordedCount; i++)
            {
                ref RecordedFrame frame = ref m_RecordedFrames[(m_RecordedNext + m_RecordedFrames.Length - i - 1) % m_RecordedFrames.Length];
                if (frame.PresentationFrame != presentationFrame)
                    continue;
                state = frame.State;
                pipeline = frame.Pipeline;
                return true;
            }
            state = default;
            pipeline = null;
            return false;
        }

        static void ReleaseRecordedFrame(ref RecordedFrame frame)
        {
            ReleaseStatePages(frame.State);
            for (int i = 0; i < frame.Pipeline.Length; i++)
            {
                frame.Pipeline[i].ReleaseStepStorage();
                frame.Pipeline[i] = null;
            }
            frame.PresentationFrame = 0;
            frame.State = default;
        }

        internal void ClearRecordedFrames()
        {
            for (int i = 0; i < m_RecordedCount; i++)
            {
                ref RecordedFrame frame = ref m_RecordedFrames[(m_RecordedNext + m_RecordedFrames.Length - i - 1) % m_RecordedFrames.Length];
                ReleaseRecordedFrame(ref frame);
            }
            m_RecordedCount = 0;
            m_RecordedNext = 0;
        }

        public void Restore(SimulationWorldSnapshot snapshot)
        {
            SimulationWorldStateSet restored = ValidateAndDecode(snapshot);
            ReleaseStatePages(m_Current);
            m_Current = restored;
        }

        public SimulationWorldStateSet PrepareRestore(SimulationWorldSnapshot snapshot) => ValidateAndDecode(snapshot);

        public void ReplaceValidated(SimulationWorldStateSet stateSet)
        {
            ValidateCurrentBindings(stateSet);
            RequireSameRosterAndWorldBinding(m_Current, stateSet);
            RetainStatePages(stateSet);
            ReleaseStatePages(m_Current);
            m_Current = stateSet;
        }

        public void Publish(SimulationWorldStateSet stateSet)
        {
            if (stateSet.LastCompletedTick != checked(m_Current.LastCompletedTick + 1))
                throw new InvalidOperationException("Published Simulation state must immediately follow the current Tick.");
            ValidateCurrentBindings(stateSet);
            RequireSameRosterAndWorldBinding(m_Current, stateSet);
            for (int i = 0; i < stateSet.Actors.Count; i++)
                if (stateSet.Actors[i].State.LastCompletedTick != stateSet.LastCompletedTick)
                    throw new InvalidOperationException($"Actor '{stateSet.Actors[i].ActorId}' state Tick does not match the published Tick.");
            RetainStatePages(stateSet);
            ReleaseStatePages(m_Current);
            m_Current = stateSet;
        }

        internal static void RetainStatePages(SimulationWorldStateSet state)
        {
            state.RetainActorStorage();
            state.WorldState.RetainStateStorage();
            for (int index = 0; index < state.Actors.Count; index++)
            {
                state.Actors[index].State.MotionWarpStates.Retain();
                state.Actors[index].State.RetainStatePages();
            }
        }

        internal static void ReleaseStatePages(SimulationWorldStateSet state)
        {
            state.WorldState.ReleaseStateStorage();
            for (int index = 0; index < state.Actors.Count; index++)
            {
                state.Actors[index].State.MotionWarpStates.Release();
                state.Actors[index].State.ReleaseStatePages();
            }
            state.ReleaseActorStorage();
        }

        SimulationWorldStateSet ValidateAndDecode(SimulationWorldSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.NumericProfile != m_Runtime.NumericProfile || !snapshot.GameplayContentHash.Equals(m_Runtime.GameplayContentHash) ||
                !snapshot.StateSchemaHash.Equals(m_Runtime.StateSchemaHash))
                throw new InvalidDataException("Snapshot Numeric Profile or GameplayContentHash does not match the active Character Runtime.");
            if (!snapshot.SolverId.Equals(m_Current.WorldState.SolverId) ||
                !string.Equals(snapshot.SolverVersion, m_Current.WorldState.SolverVersion, StringComparison.Ordinal) ||
                !snapshot.WorldRevision.Equals(m_Current.WorldState.WorldRevision))
            {
                throw new InvalidDataException("Snapshot Solver or WorldRevision binding does not match the active world.");
            }
            if (snapshot.Actors.Count != m_Current.Actors.Count)
                throw new InvalidDataException("Snapshot Actor roster count does not match the active roster.");
            using SimulationCharacterStatePayloadStorage.Page encoding = m_Runtime.Roster[0].RuntimeStatePages.EncodingStorage?.Acquire();
            SimulationWorldStateSetStorage.Page actorPage = m_StateStorage.BeginWrite();
            WorldSimulationState decodedWorld = default;
            int preparedActors = 0;
            try
            {
            SimulationActorState[] restoredActors = actorPage.Actors;
            for (int i = 0; i < snapshot.Actors.Count; i++)
            {
                SimulationActorSnapshot actorSnapshot = snapshot.Actors[i];
                SimulationActorState currentActor = m_Current.Actors[i];
                SimulationActorBinding binding = m_Runtime.Roster[i];
                if (actorSnapshot.ActorId != currentActor.ActorId ||
                    !actorSnapshot.GameplayContentHash.Equals(currentActor.State.GameplayContentHash) ||
                    !actorSnapshot.StateSchemaHash.Equals(currentActor.State.StateSchemaHash) ||
                    binding.ActorId != actorSnapshot.ActorId)
                    throw new InvalidDataException("Snapshot Actor roster or Ability binding does not match the active roster.");
                FixedCharacterRuntimeState state = actorSnapshot.Decode(
                    binding, encoding);
                state.RetainStatePages();
                state.MotionWarpStates.Retain();
                restoredActors[i] = new SimulationActorState(actorSnapshot.ActorId, state);
                preparedActors++;
                foreach (GameplayAbilityExecutionStateBank<AbilityStateValue> bank in binding.ExecutionStatePages.Values)
                    bank.ReleaseCurrent();
                binding.GameplayEffectState.Dispose();
                binding.EquipmentState.Storage?.ReleaseDecodedState();
                if (state.LastCompletedTick != snapshot.Tick.Value)
                    throw new InvalidDataException($"Actor '{actorSnapshot.ActorId}' state Tick does not match Snapshot Tick.");
            }
            WorldSimulationState worldState = snapshot.DecodeWorldState(m_RestoreWorldStorage, m_Current.WorldState.Bodies);
            decodedWorld = worldState;
            if (worldState.Bodies.Count != restoredActors.Length)
                throw new InvalidDataException("Snapshot World body roster does not match Actor roster.");
            for (int i = 0; i < restoredActors.Length; i++)
                if (worldState.Bodies[i].ActorId != restoredActors[i].ActorId)
                    throw new InvalidDataException("Snapshot World body order does not match Actor roster.");
            SimulationWorldStateSet restored = SimulationWorldStateSet.FromStateStorage(snapshot.Tick.Value, actorPage, worldState);
            RetainStatePages(restored);
            worldState.ReleaseStateStorage();
            return restored;
            }
            catch
            {
                decodedWorld.ReleaseStateStorage();
                throw;
            }
            finally
            {
                for (int index = 0; index < preparedActors; index++)
                {
                    FixedCharacterRuntimeState state = actorPage.Actors[index].State;
                    state.MotionWarpStates.Release();
                    state.ReleaseStatePages();
                }
                actorPage.Release();
            }
        }

        void ValidateCurrentBindings(SimulationWorldStateSet stateSet)
        {
            if (stateSet.WorldState.NumericProfile != m_Runtime.NumericProfile || stateSet.Actors.Count != m_Runtime.Roster.Count)
                throw new InvalidDataException("Simulation state does not match the active Character Runtime.");
            for (int i = 0; i < stateSet.Actors.Count; i++)
            {
                SimulationActorState actor = stateSet.Actors[i];
                SimulationActorBinding binding = m_Runtime.Roster[i];
                if (actor.ActorId != binding.ActorId)
                    throw new InvalidDataException($"Simulation state Actor '{actor.ActorId}' does not match the active Character Runtime roster.");
                GameplayContentHash expected = new GameplayContentHash(binding.GameplayContentHash);
                if (!actor.State.GameplayContentHash.Equals(expected))
                    throw new InvalidDataException($"Simulation state Actor '{actor.ActorId}' content identity does not match the active Character Runtime.");
                if (!actor.State.StateSchemaHash.Equals(binding.StateSchemaHash))
                    throw new InvalidDataException($"Simulation state Actor '{actor.ActorId}' schema identity does not match the active Character Runtime.");
            }
        }

        static void RequireSameRosterAndWorldBinding(SimulationWorldStateSet current, SimulationWorldStateSet candidate)
        {
            if (!candidate.WorldState.SolverId.Equals(current.WorldState.SolverId) ||
                candidate.WorldState.NumericProfile != current.WorldState.NumericProfile ||
                !string.Equals(candidate.WorldState.SolverVersion, current.WorldState.SolverVersion, StringComparison.Ordinal) ||
                !candidate.WorldState.WorldRevision.Equals(current.WorldState.WorldRevision) ||
                candidate.Actors.Count != current.Actors.Count)
            {
                throw new InvalidOperationException("Simulation state replacement changes the locked Solver, WorldRevision, or Actor roster.");
            }
            for (int i = 0; i < current.Actors.Count; i++)
            {
                if (candidate.Actors[i].ActorId != current.Actors[i].ActorId ||
                    !candidate.Actors[i].State.GameplayContentHash.Equals(current.Actors[i].State.GameplayContentHash) ||
                    !candidate.Actors[i].State.StateSchemaHash.Equals(current.Actors[i].State.StateSchemaHash) ||
                    candidate.WorldState.Bodies[i].ActorId != current.WorldState.Bodies[i].ActorId)
                {
                    throw new InvalidOperationException("Simulation state replacement changes the locked Actor or Ability binding.");
                }
            }
        }
    }

    public static class SimulationWorldSnapshotCodec
    {
        const uint Magic = 0x504e5343;
        const int Version = 8;

        [ThreadStatic] static CanonicalWriter s_HashWriter;
        [ThreadStatic] static CanonicalWriter s_CanonicalWriter;

        internal static void CapturePrepared(
            CanonicalWriter writer, FixedCharacterRuntime runtime, in SimulationWorldStateSet state,
            bool deterministic, SimulationWorldSnapshot snapshot,
            ICanonicalTextResolver resolver
#if UNITY_EDITOR
            , ref SimulationSnapshotCaptureProfile profile
#endif
            )
        {
#if UNITY_EDITOR
            long actorStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            FixedCharacterRuntimeStateCodec.PreparePayloads(state.Actors
#if UNITY_EDITOR
                , ref profile
#endif
                );
#if UNITY_EDITOR
            long worldStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            profile.ActorPayloadElapsedStopwatchTicks = worldStarted - actorStarted;
#endif
            long prefix = writer.BeginLengthPrefixedBlock();
            writer.WriteUInt32(Magic);
            writer.WriteInt32(Version);
            long hashPosition = writer.Position;
            writer.WriteHash(runtime.StateSchemaHash);
            int hashPayloadStart = checked((int)writer.Position);
            SimulationNumericProfile.CanonicalWrite(writer, runtime.NumericProfile);
            writer.WriteHash(runtime.GameplayContentHash);
            writer.WriteHash(runtime.StateSchemaHash);
            writer.WriteText(state.WorldState.SolverId.Value);
            writer.WriteText(state.WorldState.SolverVersion);
            writer.WriteText(state.WorldState.WorldRevision.Value);
            writer.WriteUInt64(state.LastCompletedTick);
            writer.WriteBoolean(deterministic);
            writer.WriteInt32(state.Actors.Count);
            for (int index = 0; index < state.Actors.Count; index++)
            {
                SimulationActorState actor = state.Actors[index];
                writer.WriteText(actor.ActorId.Value);
                writer.WriteHash(actor.State.GameplayContentHash);
                writer.WriteHash(actor.State.StateSchemaHash);
                SimulationCharacterStatePayload payload = actor.State.CanonicalPayload;
                writer.WriteHash(payload.Hash);
                writer.WriteText(FixedCharacterRuntimeStateCodec.CodecIdentity);
                long actorPrefix = writer.BeginLengthPrefixedBlock();
                int actorStart = checked((int)writer.Position);
                FixedCharacterRuntimeStateCodec.WriteCanonical(writer, actor.State);
                int actorEnd = checked((int)writer.Position);
                writer.EndLengthPrefixedBlock(actorPrefix);
                snapshot.Actors[index].SetPreparedPayload(payload.Hash,
                    writer.WrittenSegment.Array.AsMemory(actorStart, actorEnd - actorStart), resolver);
            }
            long worldPrefix = writer.Position;
            WorldSimulationStateCodec.WriteLengthPrefixed(writer, state.WorldState);
            int end = checked((int)writer.Position);
#if UNITY_EDITOR
            long hashStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            profile.WorldEncodeElapsedStopwatchTicks = hashStarted - worldStarted;
#endif
            StableHash worldHash = SimulationCanonicalPayloadHash.Compute(writer.WrittenSpan.Slice(hashPayloadStart, end - hashPayloadStart));
#if UNITY_EDITOR
            profile.WorldHashElapsedStopwatchTicks = System.Diagnostics.Stopwatch.GetTimestamp() - hashStarted;
#endif
            writer.Position = hashPosition;
            writer.WriteHash(worldHash);
            writer.Position = end;
            writer.EndLengthPrefixedBlock(prefix);
            snapshot.SetPrepared(runtime.NumericProfile, runtime.GameplayContentHash, runtime.StateSchemaHash,
                state.WorldState.SolverId, state.WorldState.SolverVersion, state.WorldState.WorldRevision,
                new SimulationTick(state.LastCompletedTick), deterministic,
                writer.WrittenSegment.Array.AsMemory(checked((int)worldPrefix + sizeof(int)), end - checked((int)worldPrefix) - sizeof(int)),
                new SimulationWorldHash(worldHash), resolver);
        }

        internal static void ReadPrepared(
            ReadOnlyMemory<byte> bytes, CanonicalReader reader, SimulationWorldSnapshot snapshot,
            ICanonicalTextResolver resolver)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(resolver);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("Simulation World Snapshot header is invalid.");
            var expectedHash = SimulationWorldHash.CanonicalFromHash(reader.ReadHash());
            int hashPayloadStart = bytes.Length - reader.Remaining;
            SimulationNumericProfile numeric = SimulationNumericProfile.CanonicalReadPublished(reader, snapshot.NumericProfile);
            var content = GameplayContentHash.CanonicalFromHash(reader.ReadHash());
            StableHash schema = reader.ReadHash();
            reader.RequireText(snapshot.SolverId.Value);
            reader.RequireText(snapshot.SolverVersion);
            reader.RequireText(snapshot.WorldRevision.Value);
            SolverImplementationId solver = snapshot.SolverId;
            string solverVersion = snapshot.SolverVersion;
            WorldRevision revision = snapshot.WorldRevision;
            var tick = new SimulationTick(reader.ReadUInt64());
            bool deterministic = reader.ReadBoolean();
            int count = reader.ReadInt32();
            if (count != snapshot.Actors.Count)
                throw new InvalidDataException("Snapshot Actor count does not match the prepared Session roster.");
            for (int index = 0; index < count; index++)
            {
                SimulationActorSnapshot actor = snapshot.Actors[index];
                reader.RequireText(actor.ActorId.Value);
                var actorContent = GameplayContentHash.CanonicalFromHash(reader.ReadHash());
                StableHash actorSchema = reader.ReadHash();
                var stateHash = CharacterStateHash.CanonicalFromHash(reader.ReadHash());
                reader.RequireText(actor.StateCodecIdentity);
                if (!actorContent.Equals(actor.GameplayContentHash) || !actorSchema.Equals(actor.StateSchemaHash))
                    throw new InvalidDataException("Snapshot Actor identity does not match the prepared Character Runtime.");
                ArraySegment<byte> payload = reader.ReadBytesSegment();
                actor.SetPreparedPayload(stateHash, payload.Array.AsMemory(payload.Offset, payload.Count), resolver);
            }
            ArraySegment<byte> world = reader.ReadBytesSegment();
            reader.RequireComplete();
            StableHash hash = SimulationCanonicalPayloadHash.Compute(bytes.Span.Slice(hashPayloadStart));
            if (!expectedHash.Value.Equals(hash))
                throw new InvalidDataException("Simulation World Snapshot hash is invalid.");
            snapshot.SetPrepared(numeric, content, schema, solver, solverVersion, revision, tick, deterministic,
                world.Array.AsMemory(world.Offset, world.Count), expectedHash, resolver);
        }

        public static void WriteLengthPrefixed(CanonicalWriter writer, SimulationWorldSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            long prefixPosition = writer.BeginLengthPrefixedBlock();
            WriteCanonicalPayload(writer, snapshot);
            writer.EndLengthPrefixedBlock(prefixPosition);
        }

        public static SimulationWorldSnapshot Read(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("Simulation World Snapshot header is invalid.");
            var expectedHash = SimulationWorldHash.CanonicalFromHash(reader.ReadHash());
            SimulationNumericProfile numericProfile = SimulationNumericProfile.CanonicalReadMaterialized(reader);
            var gameplayContentHash = GameplayContentHash.CanonicalFromHash(reader.ReadHash());
            var stateSchemaHash = reader.ReadHash();
            var solverId = new SolverImplementationId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            string solverVersion = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            var worldRevision = new WorldRevision(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            var tick = new SimulationTick(reader.ReadUInt64());
            bool deterministicValidity = reader.ReadBoolean();
            int count = reader.ReadInt32();
            if (count <= 0 || count > 1000000)
                throw new InvalidDataException($"Snapshot actor count '{count}' is invalid.");
            var actors = new SimulationActorSnapshot[count];
            for (int i = 0; i < count; i++)
            {
                actors[i] = new SimulationActorSnapshot(
                    new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                    GameplayContentHash.CanonicalFromHash(reader.ReadHash()),
                    reader.ReadHash(),
                    CharacterStateHash.CanonicalFromHash(reader.ReadHash()),
                    CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                    reader.ReadBytes());
            }
            byte[] worldStateBytes = reader.ReadBytes();
            reader.RequireComplete();
            var snapshot = new SimulationWorldSnapshot(
                numericProfile,
                gameplayContentHash,
                stateSchemaHash,
                solverId,
                solverVersion,
                worldRevision,
                tick,
                actors,
                worldStateBytes,
                deterministicValidity);
            if (!snapshot.WorldHash.Equals(expectedHash))
                throw new InvalidDataException($"Simulation World Snapshot hash mismatch. Expected '{expectedHash}', actual '{snapshot.WorldHash}'.");
            CanonicalWriter writer = CanonicalScratch();
            WriteCanonicalPayload(writer, snapshot);
            if (!writer.ContentEquals(bytes))
                throw new InvalidDataException("Simulation World Snapshot is not canonical.");
            return snapshot;
        }

        public static SimulationWorldHash ComputeHash(SimulationWorldSnapshot snapshot)
        {
            CanonicalWriter writer = HashWriter();
            WriteHashPayload(writer, snapshot);
            return new SimulationWorldHash(writer.ComputeHash());
        }

        static void WriteCanonicalPayload(CanonicalWriter writer, SimulationWorldSnapshot snapshot)
        {
            writer.WriteUInt32(Magic);
            writer.WriteInt32(Version);
            writer.WriteHash(snapshot.WorldHash);
            WriteHashPayload(writer, snapshot);
        }

        static void WriteHashPayload(CanonicalWriter writer, SimulationWorldSnapshot snapshot)
        {
            SimulationNumericProfile.CanonicalWrite(writer, snapshot.NumericProfile);
            writer.WriteHash(snapshot.GameplayContentHash);
            writer.WriteHash(snapshot.StateSchemaHash);
            writer.WriteText(snapshot.SolverId.Value);
            writer.WriteText(snapshot.SolverVersion);
            writer.WriteText(snapshot.WorldRevision.Value);
            writer.WriteUInt64(snapshot.Tick.Value);
            writer.WriteBoolean(snapshot.DeterministicValidity);
            writer.WriteInt32(snapshot.Actors.Count);
            for (int i = 0; i < snapshot.Actors.Count; i++)
            {
                SimulationActorSnapshot actor = snapshot.Actors[i];
                writer.WriteText(actor.ActorId.Value);
                writer.WriteHash(actor.GameplayContentHash);
                writer.WriteHash(actor.StateSchemaHash);
                writer.WriteHash(actor.StateHash);
                writer.WriteText(actor.StateCodecIdentity);
                writer.WriteBytes(actor.StateBytes.Span);
            }
            writer.WriteBytes(snapshot.WorldStatePayload.Span);
        }

        static CanonicalWriter HashWriter()
        {
            if (s_HashWriter == null)
                s_HashWriter = new CanonicalWriter();
            s_HashWriter.Reset();
            return s_HashWriter;
        }

        static CanonicalWriter CanonicalScratch()
        {
            if (s_CanonicalWriter == null)
                s_CanonicalWriter = new CanonicalWriter();
            s_CanonicalWriter.Reset();
            return s_CanonicalWriter;
        }
    }
}

