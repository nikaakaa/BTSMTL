using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;
using System.IO;
using ThirdPersonSimulation;
namespace ThirdPersonSimulation.FPGameplay
{
    [ValueOperations(ExplicitResources = true)]
    public readonly partial struct SimulationActorState
    {
        public SimulationActorState(ActorId actorId, FPCharacterRuntimeState state)
        {
            if (!actorId.IsValid)
                throw new ArgumentException("ActorId is invalid.", nameof(actorId));
            ActorId = actorId;
            State = state;
        }

        public ActorId ActorId { get; }
        [ValueResource("RetainStatePages", "ReleaseStatePages")]
        public FPCharacterRuntimeState State { get; }
    }

    [ValueOperations(ExplicitResources = true, RetainMethod = nameof(RetainStateStorage), ReleaseMethod = nameof(ReleaseStateStorage))]
    public sealed partial class SimulationWorldSnapshot
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

    [ValueOperations(ExplicitResources = true, RetainMethod = nameof(RetainValues), ReleaseMethod = nameof(ReleaseValues))]
    public readonly partial struct SimulationWorldStateSet
    {
        [ValueResource(Elements = true)]
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
        [ValueResource("RetainStateStorage", "ReleaseStateStorage")]
        public WorldSimulationState WorldState { get; }

        internal void RetainActorStorage() => m_Storage?.Retain(m_StorageGeneration);
        internal void ReleaseActorStorage() => m_Storage?.Release();
        internal bool SharesStateStorage(SimulationWorldStateSet other) =>
            ReferenceEquals(m_Actors, other.m_Actors) &&
            m_StorageGeneration == other.m_StorageGeneration &&
            LastCompletedTick == other.LastCompletedTick;

        public void Retain()
        {
            RetainActorStorage();
            RetainValues();
        }

        public void Release()
        {
            ReleaseValues();
            ReleaseActorStorage();
        }

        private partial void RetainValues();
        private partial void ReleaseValues();

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

    public sealed class SimulationWorldStateSetStorage
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

        public SimulationWorldStateSetStorage(int actorCount)
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

        public void PrepareCapacity(int statePageCount)
        {
            m_Pages.Capacity = statePageCount;
            while (m_Pages.Count < statePageCount)
                m_Pages.Add(new Page(m_ActorCount));
        }

        public SimulationWorldStateSet CreateCandidate(
            SimulationTick tick,
            IReadOnlyList<SimulationActorTickResult> actors,
            WorldSimulationState worldState)
        {
            Page page = BeginWrite();
            try
            {
                for (int i = 0; i < m_ActorCount; i++)
                    page.Actors[i] = new SimulationActorState(actors[i].ActorId, actors[i].State);
                SimulationWorldStateSet candidate = SimulationWorldStateSet.FromStateStorage(tick.Value, page, worldState);
                candidate.Retain();
                return candidate;
            }
            finally
            {
                page.Release();
            }
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
        readonly FPCharacterRuntime m_Runtime;
        readonly SimulationWorldStateSetStorage m_StateStorage;
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

        public SimulationWorldStateStore(FPCharacterRuntime runtime, SimulationWorldStateSet initialState)
        {
            m_Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            m_Current = initialState;
            ValidateCurrentBindings(initialState);
            m_StateStorage = new SimulationWorldStateSetStorage(initialState.Actors.Count);
            initialState.Retain();
        }

        public SimulationWorldStateSet Current => m_Current;

        public void Dispose()
        {
            ClearRecordedFrames();
            m_Current.Release();
            m_Current = default;
        }
        internal int RecordedFrameCapacity => m_RecordedFrames.Length;
        internal void PrepareStepStorage(int maximumSteps) => m_StateStorage.PrepareStepStorage(maximumSteps);
        internal SimulationWorldStateSetStorage StateStorage => m_StateStorage;

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
            m_Current.Retain();
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
            frame.State.Release();
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

        public void ReplaceValidated(SimulationWorldStateSet stateSet)
        {
            ValidateCurrentBindings(stateSet);
            RequireSameRosterAndWorldBinding(m_Current, stateSet);
            stateSet.Retain();
            m_Current.Release();
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
            stateSet.Retain();
            m_Current.Release();
            m_Current = stateSet;
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

}

