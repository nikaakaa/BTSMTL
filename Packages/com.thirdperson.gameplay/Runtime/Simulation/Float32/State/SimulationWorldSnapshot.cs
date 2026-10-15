using System;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation
{
    [ValueOperations(ExplicitResources = true)]
    public readonly partial struct SimulationActorState
    {
        public SimulationActorState(ActorId actorId, Float32CharacterRuntimeState state)
        {
            if (!actorId.IsValid)
                throw new ArgumentException("ActorId is invalid.", nameof(actorId));
            ActorId = actorId;
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        public ActorId ActorId { get; }
        [ValueResource("RetainStatePages", "ReleaseStatePages")]
        public Float32CharacterRuntimeState State { get; }
    }

    [ValueOperations(ExplicitResources = true, RetainMethod = nameof(RetainStateStorage), ReleaseMethod = nameof(ReleaseStateStorage))]
    public sealed partial class SimulationWorldSnapshot : ISimulationWorldSnapshot<SimulationWorldStateSet>
    {
        public SimulationWorldSnapshot(GameplayContentHash gameplayContentHash, StableHash stateSchemaHash, IValueResources storage)
        {
            GameplayContentHash = gameplayContentHash;
            StateSchemaHash = stateSchemaHash;
            m_Storage = storage;
        }

        public SimulationWorldStateSet State { get; private set; }
        public SimulationNumericProfile NumericProfile => State.WorldState.NumericProfile;
        public GameplayContentHash GameplayContentHash { get; }
        public StableHash StateSchemaHash { get; }
        public SolverImplementationId SolverId => State.WorldState.SolverId;
        public string SolverVersion => State.WorldState.SolverVersion;
        public WorldRevision WorldRevision => State.WorldState.WorldRevision;
        public SimulationTick Tick => new SimulationTick(State.LastCompletedTick);
        public IReadOnlyList<SimulationActorState> Actors => State.Actors;

        [ValueResource]
        readonly IValueResources m_Storage;

        public void SetPrepared(SimulationWorldStateSet state) => State = state;
        public partial void RetainStateStorage();
        public partial void ReleaseStateStorage();
    }

    [ValueOperations(ExplicitResources = true)]
    public sealed partial class SimulationWorldStateSet : ISimulationSnapshotState
    {
        [ValueResource(Elements = true)]
        readonly SimulationActorState[] m_Actors;
        [ValueResource(Optional = true)]
        readonly SimulationWorldStateStore.StatePage m_Storage;
        [ValueResource(Optional = true)]
        readonly IValueResources m_PreparedStorage;

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

        public SimulationWorldStateSet(int actorCount, IValueResources preparedStorage)
        {
            m_PreparedStorage = preparedStorage;
            m_Actors = new SimulationActorState[actorCount];
        }

        public void SetPrepared(ulong tick, IReadOnlyList<SimulationActorState> actors, WorldSimulationState world)
        {
            for (int index = 0; index < m_Actors.Length; index++)
                m_Actors[index] = actors[index];
            SetPrepared(tick, world);
        }

        public void ClearPrepared()
        {
            Array.Clear(m_Actors, 0, m_Actors.Length);
            SetPrepared(0, null);
        }

        internal void CompleteCandidate(ulong tick, WorldSimulationState world)
        {
            ValidatePrepared(m_Actors, world);
            world.RetainStateStorage();
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
        [ValueResource("RetainStateStorage", "ReleaseStateStorage")]
        public WorldSimulationState WorldState { get; private set; }

        public partial void Retain();
        public partial void Release();

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
            internal int References;

            internal void Retain() => References++;
            internal void Release()
            {
                if (--References != 0)
                    return;
                Array.Clear(State.ActorArray, 0, State.ActorArray.Length);
                State.WorldState?.ReleaseStateStorage();
                State.SetPrepared(0, null);
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

        internal static void RetainStatePages(SimulationWorldStateSet state) => state.Retain();

        internal static void ReleaseStatePages(SimulationWorldStateSet state) => state.Release();

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

}
