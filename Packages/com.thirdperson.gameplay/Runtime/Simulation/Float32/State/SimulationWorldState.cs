using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    [ValueOperations(Resources = false)]
    public readonly partial struct WorldBodyState : IEquatable<WorldBodyState>
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
        public ActorId ActorId { get; }
        public Float32Vector3 Position { get; }
        public Float32Yaw Yaw { get; }
        public Float32Vector3 Velocity { get; }
        public Float32Scalar VerticalVelocity { get; }
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
    public sealed partial class WorldSimulationState : IEquatable<WorldSimulationState>
    {
        WorldSimulationStateIdentity m_Identity;
        readonly IReadOnlyList<WorldBodyState> m_Bodies;
        IValueResources m_SolverState;
        [ValueEqualityIgnore]
        readonly WorldSimulationStateStorage.Page m_Storage;

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
            m_SolverState = solverState;
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

        public WorldSimulationState(int actorCount)
        {
            m_Bodies = new WorldBodyState[actorCount];
        }

        internal WorldSimulationState(int actorCount, WorldSimulationStateStorage.Page storage)
            : this(actorCount)
        {
            m_Storage = storage;
        }

        public void RetainStateStorage()
        {
            if (m_Storage == null) m_SolverState?.Retain();
            else m_Storage.Retain();
        }

        public void ReleaseStateStorage()
        {
            if (m_Storage == null) m_SolverState?.Release();
            else m_Storage.Release();
        }

        internal WorldBodyState[] BodyArray => (WorldBodyState[])m_Bodies;

        internal void SetPrepared(in WorldSimulationStateIdentity identity, IValueResources solverState)
        {
            solverState?.Retain();
            m_SolverState?.Release();
            m_SolverState = solverState;
            m_Identity = identity;
        }

        public void ClearPrepared()
        {
            Array.Clear(BodyArray, 0, BodyArray.Length);
            m_Identity = default;
            m_SolverState?.Release();
            m_SolverState = null;
        }

        public WorldSimulationStateIdentity Identity => m_Identity;
        public SimulationNumericProfile NumericProfile => m_Identity.NumericProfile;
        public SolverImplementationId SolverId => m_Identity.SolverId;
        public string SolverVersion => m_Identity.SolverVersion;
        public WorldRevision WorldRevision => m_Identity.WorldRevision;
        public WorldStatePersistenceMode PersistenceMode => m_Identity.PersistenceMode;
        public IReadOnlyList<WorldBodyState> Bodies => m_Bodies;
        public IValueResources SolverState => m_SolverState;

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

            internal Page(int actorCount)
            {
                World = new WorldSimulationState(actorCount, this);
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
                string version, WorldRevision revision, WorldStatePersistenceMode persistence, IValueResources solverState)
            {
                var identity = new WorldSimulationStateIdentity(numeric, solver, version, revision, persistence);
                World.SetPrepared(in identity, solverState);
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
                World.SetPrepared(in identity, state.SolverState);
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
        readonly List<Page> m_Pages = new List<Page>(2);

        public WorldSimulationStateStorage(int actorCount)
        {
            m_ActorCount = actorCount;
            m_Pages.Add(new Page(actorCount));
            m_Pages.Add(new Page(actorCount));
        }

        public void PrepareStepStorage(int maximumSteps)
        {
            int count = checked(maximumSteps + 4);
            m_Pages.Capacity = Math.Max(m_Pages.Capacity, count);
            while (m_Pages.Count < count)
                m_Pages.Add(new Page(m_ActorCount));
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
