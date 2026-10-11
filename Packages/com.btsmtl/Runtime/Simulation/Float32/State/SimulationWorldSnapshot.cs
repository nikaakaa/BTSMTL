using System;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation
{
    public readonly struct SimulationActorState
    {
        public SimulationActorState(ActorId actorId, Float32CharacterRuntimeState state)
        {
            if (!actorId.IsValid)
                throw new ArgumentException("ActorId is invalid.", nameof(actorId));
            ActorId = actorId;
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        public ActorId ActorId { get; }
        public Float32CharacterRuntimeState State { get; }
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
                !CanonicalTextValue.Equals(stateCodecIdentity, Float32CharacterRuntimeStateCodec.CodecIdentity, StringComparison.Ordinal))
            {
                throw new ArgumentException("Actor snapshot identity is incomplete.");
            }
            ActorId = actorId;
            GameplayContentHash = gameplayContentHash;
            StateSchemaHash = stateSchemaHash;
            StateHash = stateHash;
            StateCodecIdentity = stateCodecIdentity;
            m_StateBytes = ownedStateBytes == null
                ? throw new ArgumentNullException(nameof(ownedStateBytes))
                : ownedStateBytes;
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
            StateCodecIdentity = Float32CharacterRuntimeStateCodec.CodecIdentity;
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

        public Float32CharacterRuntimeState Decode(
            SimulationActorBinding actor)
        {
            if (actor == null)
                throw new InvalidDataException($"Actor '{ActorId}' snapshot Ability binding is stale or mismatched.");
            using SimulationCharacterStatePayloadStorage.Page encoding = actor.RuntimeStatePages.EncodingStorage?.Acquire();
            return Decode(actor, encoding);
        }

        internal Float32CharacterRuntimeState Decode(SimulationActorBinding actor,
            SimulationCharacterStatePayloadStorage.Page encoding)
        {
            if (actor.ActorId != ActorId ||
                !GameplayContentHash.Equals(new GameplayContentHash(actor.GameplayContentHash)) ||
                !StateSchemaHash.Equals(actor.StateSchemaHash))
                throw new InvalidDataException($"Actor '{ActorId}' snapshot Character Runtime binding is stale or mismatched.");
            Float32CharacterRuntimeState state = Float32CharacterRuntimeStateCodec.Read(
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
            if (!numericProfile.IsValid || !gameplayContentHash.IsValid || !stateSchemaHash.IsValid || CanonicalTextValue.IsNullOrEmpty(solverId.Value) || CanonicalTextValue.IsNullOrEmpty(worldRevision.Value) || !tick.IsValid)
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
            NumericProfile = publishedWorld.NumericProfile;
            SolverId = publishedWorld.SolverId;
            SolverVersion = publishedWorld.SolverVersion;
            WorldRevision = publishedWorld.WorldRevision;
            m_Actors = actors;
            m_WorldReader = new CanonicalReader(Array.Empty<byte>());
            m_CanonicalWriter = writer;
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
            SolverId = default;
            SolverVersion = null;
            WorldRevision = default;
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

        public WorldSimulationState DecodeWorldState() =>
            WorldSimulationStateCodec.Read(m_WorldStateBytes, NumericProfile, SolverId, SolverVersion, WorldRevision,
                m_WorldReader, m_CanonicalWriter, m_TextResolver);

        internal WorldSimulationState DecodeWorldState(WorldSimulationState storage) =>
            WorldSimulationStateCodec.Read(m_WorldStateBytes, NumericProfile, SolverId, SolverVersion, WorldRevision,
                m_WorldReader, m_CanonicalWriter, m_TextResolver, storage);

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
            Float32CharacterRuntime characterRuntime,
            SimulationTick tick,
            SimulationActorState[] actorStates,
            WorldSimulationState worldState,
            WorldCapability solverCapabilities)
        {
            if (characterRuntime == null)
                throw new ArgumentNullException(nameof(characterRuntime));
            if (worldState == null)
                throw new ArgumentNullException(nameof(worldState));
            if (characterRuntime.NumericProfile != worldState.NumericProfile)
                throw new InvalidOperationException("Character Runtime and World state Numeric Profiles do not match.");
            if (actorStates == null)
                throw new ArgumentNullException(nameof(actorStates));
            if (actorStates.Length == 0)
                throw new InvalidOperationException("Character runtime state roster contains no entry.");
            if (actorStates.Length != worldState.Bodies.Count || actorStates.Length != characterRuntime.Roster.Count)
                throw new InvalidOperationException("Character runtime state, Character Runtime and World body rosters do not match.");
            Float32CharacterRuntimeStateCodec.PreparePayloads(actorStates);
            var snapshots = new SimulationActorSnapshot[actorStates.Length];
            bool abilitiesDeterministic = true;
            for (int i = 0; i < actorStates.Length; i++)
            {
                SimulationActorState actor = actorStates[i];
                SimulationActorBinding binding = characterRuntime.Roster[i];
                if (actor.State == null || i > 0 && actorStates[i - 1].ActorId == actor.ActorId ||
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
                SimulationCharacterStatePayload payload = actor.State.CanonicalPayload;
                byte[] stateBytes = payload.Bytes.ToArray();
                snapshots[i] = new SimulationActorSnapshot(
                    actor.ActorId,
                    actor.State.GameplayContentHash,
                    actor.State.StateSchemaHash,
                    payload.Hash,
                    Float32CharacterRuntimeStateCodec.CodecIdentity,
                    stateBytes);
                for (int abilityIndex = 0; abilityIndex < binding.AbilityInstallations.Installations.Count; abilityIndex++)
                {
                    Float32GameplayAbilityExecutionData ability = binding.AbilityInstallations.Installations[abilityIndex].Data;
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

    public sealed class SimulationWorldStateSet
    {
        readonly SimulationActorState[] m_Actors;
        readonly SimulationWorldStateStore.StatePage m_Storage;

        public SimulationWorldStateSet(ulong lastCompletedTick, IReadOnlyList<SimulationActorState> actors, WorldSimulationState worldState)
        {
            LastCompletedTick = lastCompletedTick;
            WorldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            var copied = actors == null || actors.Count == 0
                ? Array.Empty<SimulationActorState>()
                : new SimulationActorState[actors.Count];
            for (int i = 0; i < copied.Length; i++)
            {
                copied[i] = actors[i];
                if (copied[i].State == null)
                    throw new ArgumentException("Simulation state Actor roster contains a null entry.", nameof(actors));
            }
            Array.Sort(copied, ActorStateComparer.Instance);
            ValidatePrepared(copied, worldState);
            m_Actors = copied;
        }

        internal SimulationWorldStateSet(SimulationWorldStateStore.StatePage page, int actorCount)
        {
            m_Storage = page;
            m_Actors = new SimulationActorState[actorCount];
        }

        internal void CompleteCandidate(ulong tick, WorldSimulationState world)
        {
            ValidatePrepared(m_Actors, world);
            SetPrepared(tick, world);
        }

        internal void SetPrepared(ulong tick, WorldSimulationState world)
        {
            LastCompletedTick = tick;
            WorldState = world;
        }

        internal SimulationWorldStateStore.StatePage Storage => m_Storage;
        public ulong LastCompletedTick { get; private set; }
        public IReadOnlyList<SimulationActorState> Actors => m_Actors;
        internal SimulationActorState[] ActorArray => m_Actors;
        public WorldSimulationState WorldState { get; private set; }

        static void ValidatePrepared(
            SimulationActorState[] actors,
            WorldSimulationState worldState)
        {
            if (actors.Length == 0 || actors.Length != worldState.Bodies.Count)
                throw new ArgumentException("Simulation state Actor and World body rosters must be non-empty and equal.", nameof(actors));
            for (int i = 0; i < actors.Length; i++)
            {
                if (actors[i].State == null)
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

    public sealed class SimulationWorldStateStore : IDisposable
    {
        readonly Float32CharacterRuntime m_Runtime;
        SimulationWorldStateSet m_Current;
        StatePage[] m_StatePages;

        internal sealed class StatePage
        {
            internal StatePage(int actorCount)
            {
                State = new SimulationWorldStateSet(this, actorCount);
            }

            internal readonly SimulationWorldStateSet State;
            internal WorldSimulationState World;
            internal int References;

            internal void Release()
            {
                if (--References != 0)
                    return;
                Array.Clear(State.ActorArray, 0, State.ActorArray.Length);
                State.SetPrepared(0, null);
                World?.ClearPrepared();
            }
        }

        public SimulationWorldStateStore(Float32CharacterRuntime runtime, SimulationWorldStateSet initialState)
        {
            m_Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            m_Current = initialState ?? throw new ArgumentNullException(nameof(initialState));
            ValidateCurrentBindings(initialState);
            RetainStatePages(initialState);
        }

        public SimulationWorldStateSet Current => m_Current;

        internal void PrepareStateStorage(int statePageCount)
        {
            m_StatePages = new StatePage[statePageCount];
            for (int index = 0; index < m_StatePages.Length; index++)
                m_StatePages[index] = new StatePage(m_Runtime.Roster.Count);
        }

        internal void PrepareRestoreStorage(int maximumSolverPayloadBytes)
        {
            for (int index = 0; index < m_StatePages.Length; index++)
                m_StatePages[index].World = new WorldSimulationState(m_Runtime.Roster.Count, maximumSolverPayloadBytes);
        }

        internal StatePage AcquireStatePage()
        {
            for (int index = 0; index < m_StatePages.Length; index++)
            {
                StatePage page = m_StatePages[index];
                if (page.References != 0)
                    continue;
                page.References = 1;
                return page;
            }
            throw new InvalidOperationException("Simulation World state pages exceed the prepared Session state capacity.");
        }

        public void Dispose()
        {
            ReleaseStatePages(m_Current);
            m_Current = default;
            m_StatePages = null;
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
            if (stateSet == null)
                throw new ArgumentNullException(nameof(stateSet));
            ValidateCurrentBindings(stateSet);
            RequireSameRosterAndWorldBinding(m_Current, stateSet);
            RetainStatePages(stateSet);
            ReleaseStatePages(m_Current);
            m_Current = stateSet;
        }

        public void Publish(SimulationWorldStateSet stateSet)
        {
            if (stateSet == null)
                throw new ArgumentNullException(nameof(stateSet));
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
            if (state.Storage != null)
                state.Storage.References++;
            RetainActorStatePages(state);
        }

        static void RetainActorStatePages(SimulationWorldStateSet state)
        {
            for (int index = 0; index < state.Actors.Count; index++)
            {
                state.Actors[index].State.RetainStatePages();
            }
        }

        internal static void ReleaseStatePages(SimulationWorldStateSet state)
        {
            for (int index = 0; index < state.Actors.Count; index++)
            {
                state.Actors[index].State.ReleaseStatePages();
            }
            state.Storage?.Release();
        }

        SimulationWorldStateSet ValidateAndDecode(SimulationWorldSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.NumericProfile != m_Runtime.NumericProfile || !snapshot.GameplayContentHash.Equals(m_Runtime.GameplayContentHash) ||
                !snapshot.StateSchemaHash.Equals(m_Runtime.StateSchemaHash))
                throw new InvalidDataException("Snapshot Numeric Profile or GameplayContentHash does not match the active Character Runtime.");
            if (!snapshot.SolverId.Equals(m_Current.WorldState.SolverId) ||
                !CanonicalTextValue.Equals(snapshot.SolverVersion, m_Current.WorldState.SolverVersion, StringComparison.Ordinal) ||
                !snapshot.WorldRevision.Equals(m_Current.WorldState.WorldRevision))
            {
                throw new InvalidDataException("Snapshot Solver or WorldRevision binding does not match the active world.");
            }
            if (snapshot.Actors.Count != m_Current.Actors.Count)
                throw new InvalidDataException("Snapshot Actor roster count does not match the active roster.");
            using SimulationCharacterStatePayloadStorage.Page encoding = m_Runtime.Roster[0].RuntimeStatePages.EncodingStorage?.Acquire();
            StatePage page = AcquireStatePage();
            SimulationActorState[] restoredActors = page.State.ActorArray;
            int preparedActors = 0;
            try
            {
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
                    Float32CharacterRuntimeState state = actorSnapshot.Decode(binding, encoding);
                    state.RetainStatePages();
                    restoredActors[i] = new SimulationActorState(actorSnapshot.ActorId, state);
                    preparedActors++;
                    foreach (GameplayAbilityExecutionStateBank<AbilityStateValue> bank in binding.ExecutionStatePages.Values)
                        bank.ReleaseCurrent();
                    binding.GameplayEffectState.Dispose();
                    binding.EquipmentState.Storage?.ReleaseDecodedState();
                    if (state.LastCompletedTick != snapshot.Tick.Value)
                        throw new InvalidDataException($"Actor '{actorSnapshot.ActorId}' state Tick does not match Snapshot Tick.");
                }
                WorldSimulationState worldState = snapshot.DecodeWorldState(page.World);
                if (worldState.Bodies.Count != restoredActors.Length)
                    throw new InvalidDataException("Snapshot World body roster does not match Actor roster.");
                for (int i = 0; i < restoredActors.Length; i++)
                    if (worldState.Bodies[i].ActorId != restoredActors[i].ActorId)
                        throw new InvalidDataException("Snapshot World body order does not match Actor roster.");
                page.State.SetPrepared(snapshot.Tick.Value, worldState);
                return page.State;
            }
            catch
            {
                for (int index = preparedActors - 1; index >= 0; index--)
                    restoredActors[index].State.ReleaseStatePages();
                page.Release();
                throw;
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
                !CanonicalTextValue.Equals(candidate.WorldState.SolverVersion, current.WorldState.SolverVersion, StringComparison.Ordinal) ||
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
        const int Version = 7;

        [ThreadStatic] static CanonicalWriter s_HashWriter;
        [ThreadStatic] static CanonicalWriter s_CanonicalWriter;

        internal static void CapturePrepared(
            CanonicalWriter writer, Float32CharacterRuntime runtime, in SimulationWorldStateSet state,
            bool deterministic, SimulationWorldSnapshot snapshot,
            ICanonicalTextResolver resolver)
        {
            Float32CharacterRuntimeStateCodec.PreparePayloads(state.Actors);
            long prefix = writer.BeginLengthPrefixedBlock();
            writer.WriteUInt32(Magic);
            writer.WriteInt32(Version);
            long hashPosition = writer.Position;
            writer.WriteHash(runtime.StateSchemaHash);
            int hashPayloadStart = checked((int)writer.Position);
            SimulationNumericProfileCodec.Write(writer, runtime.NumericProfile);
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
                payload.Text.CopyTo(writer.TextCapture);
                writer.WriteHash(payload.Hash.Value);
                writer.WriteText(Float32CharacterRuntimeStateCodec.CodecIdentity);
                long actorPrefix = writer.BeginLengthPrefixedBlock();
                int actorStart = checked((int)writer.Position);
                writer.WriteRawBytes(payload.Bytes.Span);
                int actorEnd = checked((int)writer.Position);
                writer.EndLengthPrefixedBlock(actorPrefix);
                snapshot.Actors[index].SetPreparedPayload(payload.Hash,
                    writer.WrittenSegment.Array.AsMemory(actorStart, actorEnd - actorStart), resolver);
            }
            long worldPrefix = writer.Position;
            WorldSimulationStateCodec.WriteLengthPrefixed(writer, state.WorldState);
            int end = checked((int)writer.Position);
            StableHash worldHash = SimulationCanonicalPayloadHash.Compute(writer.WrittenSpan.Slice(hashPayloadStart, end - hashPayloadStart));
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
            var expectedHash = new SimulationWorldHash(reader.ReadHash());
            int hashPayloadStart = bytes.Length - reader.Remaining;
            SimulationNumericProfile numeric = SimulationNumericProfileCodec.Read(reader, snapshot.NumericProfile);
            var content = new GameplayContentHash(reader.ReadHash());
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
                var actorContent = new GameplayContentHash(reader.ReadHash());
                StableHash actorSchema = reader.ReadHash();
                var stateHash = new CharacterStateHash(reader.ReadHash());
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
            var expectedHash = new SimulationWorldHash(reader.ReadHash());
            SimulationNumericProfile numericProfile = SimulationNumericProfileCodec.Read(reader, Float32SimulationNumericProfile.Value);
            var gameplayContentHash = new GameplayContentHash(reader.ReadHash());
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
                    new GameplayContentHash(reader.ReadHash()),
                    reader.ReadHash(),
                    new CharacterStateHash(reader.ReadHash()),
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
            writer.WriteHash(snapshot.WorldHash.Value);
            WriteHashPayload(writer, snapshot);
        }

        static void WriteHashPayload(CanonicalWriter writer, SimulationWorldSnapshot snapshot)
        {
            SimulationNumericProfileCodec.Write(writer, snapshot.NumericProfile);
            writer.WriteHash(snapshot.GameplayContentHash.Value);
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
                writer.WriteHash(actor.GameplayContentHash.Value);
                writer.WriteHash(actor.StateSchemaHash);
                writer.WriteHash(actor.StateHash.Value);
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
