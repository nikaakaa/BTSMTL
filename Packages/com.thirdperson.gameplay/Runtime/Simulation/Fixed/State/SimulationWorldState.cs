using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    [ValueOperations(Resources = false)]
    public readonly partial struct WorldBodyState : IEquatable<WorldBodyState>
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
        public ActorId ActorId { get; }
        public FixedVector3 Position { get; }
        public FixedYaw Yaw { get; }
        public FixedVector3 Velocity { get; }
        public FixedScalar VerticalVelocity { get; }
        public bool Grounded { get; }
        public WorldCollisionSummary Collision { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (!ActorId.IsValid)
                throw new ArgumentException("World body ActorId is invalid.", "ActorId");
        }
    }

    [ValueOperations(Resources = false)]
    public readonly partial struct WorldSimulationState : IEquatable<WorldSimulationState>
    {
        readonly WorldSimulationStateIdentity m_Identity;
        readonly IReadOnlyList<WorldBodyState> m_Bodies;
        readonly IValueResources m_SolverState;
        [ValueEqualityIgnore]
        readonly WorldSimulationStateStorage.Page m_Storage;
        [ValueEqualityIgnore]
        readonly uint m_StorageGeneration;

        public WorldSimulationState(
            SimulationNumericProfile numericProfile,
            SolverImplementationId solverId,
            string solverVersion,
            WorldRevision worldRevision,
            WorldStatePersistenceMode persistenceMode,
            IReadOnlyList<WorldBodyState> bodies,
            IValueResources solverState)
            : this(
                numericProfile,
                solverId,
                solverVersion,
                worldRevision,
                persistenceMode,
                CopyBodies(bodies),
                solverState)
        {
            solverState?.Retain();
        }

        WorldSimulationState(
            SimulationNumericProfile numericProfile,
            SolverImplementationId solverId,
            string solverVersion,
            WorldRevision worldRevision,
            WorldStatePersistenceMode persistenceMode,
            WorldBodyState[] bodies,
            IValueResources solverState)
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
            m_SolverState = solverState;
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
            m_SolverState = storage.SolverState;
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

        public void RetainStateStorage()
        {
            if (m_Storage == null) m_SolverState?.Retain();
            else m_Storage.Retain(m_StorageGeneration);
        }

        public void ReleaseStateStorage()
        {
            if (m_Storage == null) m_SolverState?.Release();
            else m_Storage.Release();
        }

        public static WorldSimulationState FromOwnedState(
            SimulationNumericProfile numericProfile,
            SolverImplementationId solverId,
            string solverVersion,
            WorldRevision worldRevision,
            WorldStatePersistenceMode persistenceMode,
            WorldBodyState[] bodies,
            IValueResources solverState) =>
            new WorldSimulationState(
                numericProfile,
                solverId,
                solverVersion,
                worldRevision,
                persistenceMode,
                bodies,
                solverState);

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
        public IValueResources SolverState
        {
            get
            {
                m_Storage?.RequireGeneration(m_StorageGeneration);
                return m_SolverState;
            }
        }

        public WorldSimulationState Clone()
        {
            WorldBodyState[] bodies = CopyBodies(Bodies);
            IValueResources solverState = SolverState;
            solverState?.Retain();
            return new WorldSimulationState(NumericProfile, SolverId, SolverVersion, WorldRevision,
                PersistenceMode, bodies, solverState);
        }

        static WorldBodyState[] CopyBodies(IReadOnlyList<WorldBodyState> bodies)
        {
            if (bodies == null || bodies.Count == 0)
                return Array.Empty<WorldBodyState>();
            var copied = new WorldBodyState[bodies.Count];
            for (int i = 0; i < copied.Length; i++)
                copied[i] = bodies[i];
            return copied;
        }

    }

    public sealed class WorldSimulationStateStorage
    {
        public sealed class Page
        {
            int m_References;
            readonly IValueSnapshotSource m_SolverStateSource;
            readonly IValueSnapshotCopy m_PreparedSolverState;

            internal Page(int actorCount, IValueSnapshotSource solverStateSource)
            {
                Bodies = new WorldBodyState[actorCount];
                Results = new CharacterWorldSolveResult[actorCount];
                m_SolverStateSource = solverStateSource;
                m_PreparedSolverState = solverStateSource?.CreateValueSnapshot();
                m_PreparedSolverState?.Release();
            }

            public WorldBodyState[] Bodies { get; }
            public CharacterWorldSolveResult[] Results { get; }
            public IValueResources SolverState { get; private set; }

            public void CaptureSolverState()
            {
                m_SolverStateSource.CaptureValueSnapshot(m_PreparedSolverState);
                SolverState = m_PreparedSolverState;
            }
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
                    throw new InvalidOperationException("World state storage has already been reused.");
            }

            internal void Retain(uint generation)
            {
                RequireGeneration(generation);
                m_References++;
            }

            public void Release()
            {
                if (--m_References != 0) return;
                SolverState?.Release();
                SolverState = null;
            }
        }

        readonly int m_ActorCount;
        readonly IValueSnapshotSource m_SolverStateSource;
        readonly List<Page> m_Pages = new List<Page>(2);

        internal int ActorCount => m_ActorCount;

        public WorldSimulationStateStorage(int actorCount, IValueSnapshotSource solverStateSource = null)
        {
            m_ActorCount = actorCount;
            m_SolverStateSource = solverStateSource;
            m_Pages.Add(new Page(actorCount, solverStateSource));
            m_Pages.Add(new Page(actorCount, solverStateSource));
        }

        public void PrepareStepStorage(int maximumSteps)
        {
            int count = checked(maximumSteps + 4);
            m_Pages.Capacity = Math.Max(m_Pages.Capacity, count);
            while (m_Pages.Count < count)
                m_Pages.Add(new Page(m_ActorCount, m_SolverStateSource));
        }

        internal void PrepareCapacity(int statePageCount)
        {
            m_Pages.Capacity = statePageCount;
            while (m_Pages.Count < statePageCount)
                m_Pages.Add(new Page(m_ActorCount, m_SolverStateSource));
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

}
