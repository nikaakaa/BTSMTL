using ThirdPersonSimulation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace ThirdPersonSimulation.Fixed
{
    public enum WorldMotionSpace : byte
    {
        World = 1,
        ActorLocal = 2
    }

    public readonly struct CharacterMotionRequest
    {
        internal CharacterMotionRequest(
            string sourceIdentity,
            FixedVector3 displacement,
            FixedVector3 requestedVelocity,
            FixedVector2 locomotionPlanarBasis,
            FixedScalar yawDegrees,
            WorldMotionSpace space,
            bool hasMotion,
            CommittedMovementPlaybackClock movementPlaybackClock,
            CommittedLocomotionPlanarMotionTimeline locomotionTimeline,
            CanonicalTextValue actionOwnerIdentity,
            CanonicalTextValue gameplayResultOwnerIdentity)
        {
            SourceIdentity = SimulationIdentity.Require(sourceIdentity, nameof(sourceIdentity));
            Displacement = displacement;
            RequestedVelocity = requestedVelocity;
            LocomotionPlanarBasis = locomotionPlanarBasis;
            YawDegrees = yawDegrees;
            Space = space;
            HasMotion = hasMotion;
            MovementPlaybackClock = movementPlaybackClock;
            if (movementPlaybackClock.IsValid != locomotionTimeline.IsValid ||
                locomotionTimeline.IsValid && !locomotionTimeline.Matches(movementPlaybackClock))
            {
                throw new ArgumentException("Character motion request Locomotion timeline does not match its Movement clock.", nameof(locomotionTimeline));
            }
            LocomotionTimeline = locomotionTimeline;
            ActionOwnerIdentity = actionOwnerIdentity;
            GameplayResultOwnerIdentity = gameplayResultOwnerIdentity;
        }

        public string SourceIdentity { get; }
        public FixedVector3 Displacement { get; }
        public FixedVector3 RequestedVelocity { get; }
        public FixedVector2 LocomotionPlanarBasis { get; }
        public FixedScalar YawDegrees { get; }
        public WorldMotionSpace Space { get; }
        public bool HasMotion { get; }
        public CommittedMovementPlaybackClock MovementPlaybackClock { get; }
        public CommittedLocomotionPlanarMotionTimeline LocomotionTimeline { get; }
        public CanonicalTextValue ActionOwnerIdentity { get; }
        public CanonicalTextValue GameplayResultOwnerIdentity { get; }
    }

    public readonly struct CharacterWorldSolveRequest
    {
        public CharacterWorldSolveRequest(
            SimulationNumericProfile numericProfile,
            ActorId actorId,
            WorldRequestId requestId,
            SimulationTick tick,
            WorldBodyState beforeBody,
            CharacterMotionRequest motion,
            BodyMotionIntegrationPlan bodyMotionPlan,
            WorldCapability requiredCapabilities)
        {
            if (!numericProfile.IsValid || !actorId.IsValid || !requestId.IsValid || !tick.IsValid || actorId != requestId.ActorId || requestId.Tick != tick)
                throw new ArgumentException("World solve request identity is incomplete or inconsistent.");
            if (beforeBody.ActorId != actorId)
                throw new ArgumentException("World solve request body does not match ActorId.", nameof(beforeBody));
            if (motion.MovementPlaybackClock.IsValid && motion.MovementPlaybackClock.AuthorityTick != tick)
                throw new ArgumentException("World solve request Movement playback clock does not match Tick.", nameof(motion));
            if (requiredCapabilities == WorldCapability.None)
                throw new ArgumentOutOfRangeException(nameof(requiredCapabilities));
            if (bodyMotionPlan.ActorId != actorId || bodyMotionPlan.Tick != tick ||
                bodyMotionPlan.PreviousVerticalVelocity != beforeBody.VerticalVelocity ||
                bodyMotionPlan.RequestedDisplacement != motion.Displacement)
            {
                throw new ArgumentException("World solve request Body Motion plan is inconsistent.", nameof(bodyMotionPlan));
            }
            NumericProfile = numericProfile;
            ActorId = actorId;
            RequestId = requestId;
            Tick = tick;
            BeforeBody = beforeBody;
            Motion = motion;
            BodyMotionPlan = bodyMotionPlan;
            RequiredCapabilities = requiredCapabilities;
        }

        public SimulationNumericProfile NumericProfile { get; }
        public ActorId ActorId { get; }
        public WorldRequestId RequestId { get; }
        public SimulationTick Tick { get; }
        public WorldBodyState BeforeBody { get; }
        public CharacterMotionRequest Motion { get; }
        public BodyMotionIntegrationPlan BodyMotionPlan { get; }
        public WorldCapability RequiredCapabilities { get; }
    }

    public readonly struct WorldKinematicBodyBinding
    {
        public WorldKinematicBodyBinding(ActorId actorId, FixedScalar radius, FixedScalar height)
        {
            ActorId = actorId;
            Radius = radius;
            Height = height;
        }

        public ActorId ActorId { get; }
        public FixedScalar Radius { get; }
        public FixedScalar Height { get; }
    }

    public readonly struct WorldKinematicBodySample
    {
        public WorldKinematicBodySample(ActorId actorId, FixedVector3 beforePosition, FixedVector3 position)
        {
            ActorId = actorId;
            BeforePosition = beforePosition;
            Position = position;
        }

        public ActorId ActorId { get; }
        public FixedVector3 BeforePosition { get; }
        public FixedVector3 Position { get; }
    }

    public sealed class WorldSolveBatchRequest
    {
        readonly CharacterWorldSolveRequest[] m_Requests;
        readonly WorldKinematicBodySample[] m_KinematicBodies;

        public WorldSolveBatchRequest(SimulationTick tick, WorldSimulationState beforeWorldState,
            IReadOnlyList<CharacterWorldSolveRequest> requests, IReadOnlyList<WorldKinematicBodySample> kinematicBodies = null)
            : this(requests?.Count ?? 0, kinematicBodies?.Count ?? 0)
        {
            Reset(tick, beforeWorldState, requests, kinematicBodies);
        }

        internal WorldSolveBatchRequest(int actorCount, int kinematicBodyCount = 0)
        {
            if (actorCount < 0)
                throw new ArgumentOutOfRangeException(nameof(actorCount));
            m_Requests = actorCount == 0
                ? Array.Empty<CharacterWorldSolveRequest>()
                : new CharacterWorldSolveRequest[actorCount];
            m_KinematicBodies = kinematicBodyCount == 0
                ? Array.Empty<WorldKinematicBodySample>() : new WorldKinematicBodySample[kinematicBodyCount];
        }

        internal WorldSolveBatchRequest Reset(
            SimulationTick tick,
            WorldSimulationState beforeWorldState,
            IReadOnlyList<CharacterWorldSolveRequest> requests,
            IReadOnlyList<WorldKinematicBodySample> kinematicBodies = null)
        {
            if (!tick.IsValid)
                throw new ArgumentException("World batch Tick is invalid.", nameof(tick));
            WorldSimulationState worldState = beforeWorldState;
            if (requests == null || requests.Count != m_Requests.Length || m_Requests.Length != worldState.Bodies.Count)
                throw new ArgumentException("World batch must contain exactly one request per world body.", nameof(requests));
            for (int i = 0; i < m_Requests.Length; i++)
                m_Requests[i] = requests[i];
            Array.Sort(m_Requests, (left, right) => left.ActorId.CompareTo(right.ActorId));
            SimulationNumericProfile numericProfile = worldState.NumericProfile;
            WorldCapability required = WorldCapability.None;
            for (int i = 0; i < m_Requests.Length; i++)
            {
                CharacterWorldSolveRequest request = m_Requests[i];
                if (request.NumericProfile != numericProfile || request.Tick != tick || request.ActorId != worldState.Bodies[i].ActorId || !BodyEquals(request.BeforeBody, worldState.Bodies[i]))
                    throw new ArgumentException("World batch request order or before-body state does not match WorldSimulationState.", nameof(requests));
                if (i > 0 && m_Requests[i - 1].ActorId == request.ActorId)
                    throw new ArgumentException($"World batch contains duplicate ActorId '{request.ActorId}'.", nameof(requests));
                required |= request.RequiredCapabilities;
            }
            Tick = tick;
            kinematicBodies ??= Array.Empty<WorldKinematicBodySample>();
            if (kinematicBodies.Count != m_KinematicBodies.Length)
                throw new ArgumentException("World batch kinematic body count does not match its prepared capacity.", nameof(kinematicBodies));
            for (int i = 0; i < m_KinematicBodies.Length; i++)
                m_KinematicBodies[i] = kinematicBodies[i];
            if (m_KinematicBodies.Length != 0)
                required |= WorldCapability.Collision;
            BeforeWorldState = worldState;
            NumericProfile = numericProfile;
            RequiredCapabilities = required;
            return this;
        }

        public SimulationNumericProfile NumericProfile { get; private set; }
        public SimulationTick Tick { get; private set; }
        public WorldSimulationState BeforeWorldState { get; private set; }
        public IReadOnlyList<CharacterWorldSolveRequest> Requests => m_Requests;
        public IReadOnlyList<WorldKinematicBodySample> KinematicBodies => m_KinematicBodies;
        public WorldCapability RequiredCapabilities { get; private set; }

        internal static bool BodyEquals(WorldBodyState left, WorldBodyState right)
        {
            return left.ActorId == right.ActorId &&
                   left.Position == right.Position &&
                   left.Yaw == right.Yaw &&
                   left.Velocity == right.Velocity &&
                   left.VerticalVelocity == right.VerticalVelocity &&
                   left.Grounded == right.Grounded &&
                   left.Collision == right.Collision;
        }
    }

    public readonly struct CharacterWorldSolveResult
    {
        public CharacterWorldSolveResult(
            SimulationNumericProfile numericProfile,
            ActorId actorId,
            WorldRequestId requestId,
            SimulationTick tick,
            SolverImplementationId solverId,
            WorldBodyState finalBody,
            FixedVector3 appliedDisplacement,
            FixedScalar appliedYawDegrees)
        {
            if (!numericProfile.IsValid || !actorId.IsValid || !requestId.IsValid || !tick.IsValid || string.IsNullOrEmpty(solverId.Value))
                throw new ArgumentException("World solve result identity is incomplete.");
            if (requestId.ActorId != actorId || requestId.Tick != tick || finalBody.ActorId != actorId)
                throw new ArgumentException("World solve result identity does not match its body or request.");
            NumericProfile = numericProfile;
            ActorId = actorId;
            RequestId = requestId;
            Tick = tick;
            SolverId = solverId;
            FinalBody = finalBody;
            AppliedDisplacement = appliedDisplacement;
            AppliedYawDegrees = appliedYawDegrees;
        }

        public SimulationNumericProfile NumericProfile { get; }
        public ActorId ActorId { get; }
        public WorldRequestId RequestId { get; }
        public SimulationTick Tick { get; }
        public SolverImplementationId SolverId { get; }
        public WorldBodyState FinalBody { get; }
        public FixedVector3 AppliedDisplacement { get; }
        public FixedScalar AppliedYawDegrees { get; }
    }

    public readonly struct WorldSolveBatchResult
    {
        readonly IReadOnlyList<CharacterWorldSolveResult> m_Results;

        public WorldSolveBatchResult(
            WorldSolveBatchRequest request,
            SolverImplementationId solverId,
            string solverVersion,
            WorldSimulationState nextWorldState,
            IReadOnlyList<CharacterWorldSolveResult> results)
            : this(request, solverId, solverVersion, nextWorldState, CopyResults(results), false)
        {
        }

        WorldSolveBatchResult(
            WorldSolveBatchRequest request,
            SolverImplementationId solverId,
            string solverVersion,
            WorldSimulationState nextWorldState,
            CharacterWorldSolveResult[] results,
            bool preparedOrder)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrEmpty(solverId.Value))
                throw new ArgumentException("Solver identity is missing.", nameof(solverId));
            SolverId = solverId;
            SolverVersion = SimulationIdentity.Require(solverVersion, nameof(solverVersion));
            NextWorldState = nextWorldState;
            if (nextWorldState.NumericProfile != request.NumericProfile)
                throw new ArgumentException("World result state Numeric Profile does not match request.", nameof(nextWorldState));
            if (!nextWorldState.SolverId.Equals(solverId) || !string.Equals(nextWorldState.SolverVersion, SolverVersion, StringComparison.Ordinal))
                throw new ArgumentException("Next World state does not match Solver identity.", nameof(nextWorldState));
            CharacterWorldSolveResult[] copied = results ?? Array.Empty<CharacterWorldSolveResult>();
            if (!preparedOrder)
                Array.Sort(copied, (left, right) => left.ActorId.CompareTo(right.ActorId));
            if (copied.Length != request.Requests.Count || copied.Length != nextWorldState.Bodies.Count)
                throw new ArgumentException("World result must contain exactly one result per request.", nameof(results));
            for (int i = 0; i < copied.Length; i++)
            {
                CharacterWorldSolveResult result = copied[i];
                CharacterWorldSolveRequest expected = request.Requests[i];
                if (result.NumericProfile != request.NumericProfile || result.ActorId != expected.ActorId || !result.RequestId.Equals(expected.RequestId) || result.Tick != request.Tick ||
                    !result.SolverId.Equals(solverId) || result.FinalBody.ActorId != expected.ActorId ||
                    !WorldSolveBatchRequest.BodyEquals(result.FinalBody, nextWorldState.Bodies[i]))
                    throw new ArgumentException("World result is missing, duplicated, unknown, or mismatched.", nameof(results));
            }
            m_Results = copied;
        }

        public static WorldSolveBatchResult FromOwnedResults(
            WorldSolveBatchRequest request,
            SolverImplementationId solverId,
            string solverVersion,
            WorldSimulationState nextWorldState,
            CharacterWorldSolveResult[] results) =>
            new WorldSolveBatchResult(request, solverId, solverVersion, nextWorldState, results, false);

        public static WorldSolveBatchResult FromStateStorage(
            WorldSolveBatchRequest request,
            SolverImplementationId solverId,
            string solverVersion,
            WorldSimulationState nextWorldState,
            WorldSimulationStateStorage.Page storage) =>
            new WorldSolveBatchResult(request, solverId, solverVersion, nextWorldState, storage.Results, true);

        public WorldSolveBatchRequest Request { get; }
        public SimulationTick Tick => Request.Tick;
        public SolverImplementationId SolverId { get; }
        public string SolverVersion { get; }
        public WorldSimulationState NextWorldState { get; }
        public IReadOnlyList<CharacterWorldSolveResult> Results => m_Results;

        static CharacterWorldSolveResult[] CopyResults(IReadOnlyList<CharacterWorldSolveResult> results)
        {
            if (results == null || results.Count == 0)
                return Array.Empty<CharacterWorldSolveResult>();
            var copied = new CharacterWorldSolveResult[results.Count];
            for (int i = 0; i < copied.Length; i++)
                copied[i] = results[i];
            return copied;
        }
    }

    public sealed class CharacterWorldSolverDescriptor
    {
        public CharacterWorldSolverDescriptor(
            SimulationNumericProfile numericProfile,
            SolverImplementationId implementationId,
            string version,
            WorldCapability capabilities,
            WorldFeature features)
        {
            if (!numericProfile.IsValid || string.IsNullOrEmpty(implementationId.Value) || capabilities == WorldCapability.None)
                throw new ArgumentException("World Solver descriptor is incomplete.");
            if ((capabilities & WorldCapability.DeterministicReplay) != 0 && !numericProfile.DeterministicReplay)
                throw new ArgumentException("World Solver cannot declare DeterministicReplay for a non-deterministic Numeric Profile.", nameof(capabilities));
            NumericProfile = numericProfile;
            ImplementationId = implementationId;
            Version = SimulationIdentity.Require(version, nameof(version));
            Capabilities = capabilities;
            Features = features;
        }

        public SimulationNumericProfile NumericProfile { get; }
        public SolverImplementationId ImplementationId { get; }
        public string Version { get; }
        public WorldCapability Capabilities { get; }
        public WorldFeature Features { get; }
        public bool Supports(WorldCapability required) => (Capabilities & required) == required;
    }

    public interface ICharacterWorldSolver : global::ThirdPersonSimulation.ICharacterWorldSolver<
        CharacterWorldSolverDescriptor,
        WorldBodyState,
        WorldSimulationState,
        WorldSolveBatchRequest,
        WorldSolveBatchResult,
        ISimulationDiagnosticsSink>
    {
        void PrepareStepStorage(int maximumSteps);
    }

    public sealed class SortedTickResultBuffer<TValue>
    {
        readonly ulong[] m_Keys;
        readonly TValue[] m_Values;
        readonly string m_Owner;

        public SortedTickResultBuffer(int capacity, string owner)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            m_Keys = new ulong[capacity];
            m_Values = new TValue[capacity];
            m_Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public int Capacity => m_Keys.Length;
        public int Count { get; private set; }

        public TValue GetValue(int index) => m_Values[index];
        public ulong GetKey(int index) => m_Keys[index];

        public void Clear()
        {
            Array.Clear(m_Values, 0, Count);
            Count = 0;
        }

        public bool TryGetValue(ulong key, out TValue value)
        {
            int index = BinarySearch(key);
            value = index < 0 ? default : m_Values[index];
            return index >= 0;
        }

        public void RemoveAt(int index)
        {
            int remaining = Count - index - 1;
            Array.Copy(m_Keys, index + 1, m_Keys, index, remaining);
            Array.Copy(m_Values, index + 1, m_Values, index, remaining);
            m_Values[--Count] = default;
        }

        public void CopyFrom(SortedTickResultBuffer<TValue> source)
        {
            Array.Copy(source.m_Keys, m_Keys, source.Count);
            Array.Copy(source.m_Values, m_Values, source.Count);
            if (Count > source.Count)
                Array.Clear(m_Values, source.Count, Count - source.Count);
            Count = source.Count;
        }

        public bool Set(ulong key, TValue value)
        {
            int index = BinarySearch(key);
            if (index >= 0)
            {
                m_Values[index] = value;
                return true;
            }
            if (Count == Capacity)
            {
                throw new InvalidOperationException(
                    $"{m_Owner} exceeds storage capacity '{Capacity}'.");
            }
            index = ~index;
            Array.Copy(m_Keys, index, m_Keys, index + 1, Count - index);
            Array.Copy(m_Values, index, m_Values, index + 1, Count - index);
            m_Keys[index] = key;
            m_Values[index] = value;
            Count++;
            return false;
        }

        int BinarySearch(ulong key)
        {
            int left = 0;
            int right = Count - 1;
            while (left <= right)
            {
                int middle = left + (right - left) / 2;
                int comparison = m_Keys[middle].CompareTo(key);
                if (comparison == 0)
                    return middle;
                if (comparison < 0)
                    left = middle + 1;
                else
                    right = middle - 1;
            }
            return ~left;
        }
    }
}

