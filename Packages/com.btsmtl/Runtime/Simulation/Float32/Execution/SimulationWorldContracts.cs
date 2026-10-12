using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace ThirdPersonSimulation
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
            Float32Vector3 displacement,
            Float32Vector3 requestedVelocity,
            Float32Vector2 locomotionPlanarBasis,
            Float32Scalar yawDegrees,
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
            ActionOwnerIdentity = actionOwnerIdentity.IsNull ? string.Empty : actionOwnerIdentity;
            GameplayResultOwnerIdentity = gameplayResultOwnerIdentity.IsNull ? string.Empty : gameplayResultOwnerIdentity;
        }

        public string SourceIdentity { get; }
        public Float32Vector3 Displacement { get; }
        public Float32Vector3 RequestedVelocity { get; }
        public Float32Vector2 LocomotionPlanarBasis { get; }
        public Float32Scalar YawDegrees { get; }
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

    public enum ObservedWorldConstraintSamplingKind : byte
    {
        Exact = 1,
        Interpolation = 2,
        ConstantVelocityExtrapolation = 3
    }

    public readonly struct ObservedWorldConstraint
    {
        public ObservedWorldConstraint(
            ActorId actorId,
            SimulationTick targetTick,
            WorldBodyState beforeBody,
            WorldBodyState finalBody,
            SimulationTick sourcePreviousTick,
            SimulationTick sourceCurrentTick,
            ObservedWorldConstraintSamplingKind samplingKind,
            StableHash contactShapeConfigurationHash)
        {
            if (!actorId.IsValid || !targetTick.IsValid || !sourcePreviousTick.IsValid || !sourceCurrentTick.IsValid ||
                beforeBody.ActorId != actorId || finalBody.ActorId != actorId ||
                sourceCurrentTick.CompareTo(sourcePreviousTick) < 0 ||
                samplingKind is not (ObservedWorldConstraintSamplingKind.Exact or
                    ObservedWorldConstraintSamplingKind.Interpolation or ObservedWorldConstraintSamplingKind.ConstantVelocityExtrapolation) ||
                !contactShapeConfigurationHash.IsValid)
            {
                throw new ArgumentException("Observed world constraint identity is incomplete or inconsistent.");
            }
            ActorId = actorId;
            TargetTick = targetTick;
            BeforeBody = beforeBody;
            FinalBody = finalBody;
            SourcePreviousTick = sourcePreviousTick;
            SourceCurrentTick = sourceCurrentTick;
            SamplingKind = samplingKind;
            ContactShapeConfigurationHash = contactShapeConfigurationHash;
        }

        public ActorId ActorId { get; }
        public SimulationTick TargetTick { get; }
        public WorldBodyState BeforeBody { get; }
        public WorldBodyState FinalBody { get; }
        public SimulationTick SourcePreviousTick { get; }
        public SimulationTick SourceCurrentTick { get; }
        public ObservedWorldConstraintSamplingKind SamplingKind { get; }
        public StableHash ContactShapeConfigurationHash { get; }
    }

    public sealed class ObservedWorldConstraintFrame
    {
        readonly IReadOnlyList<ObservedWorldConstraint> m_Constraints;
        readonly CanonicalWriter m_EmptyHashWriter;

        internal ObservedWorldConstraintFrame()
        {
            m_Constraints = Array.Empty<ObservedWorldConstraint>();
            m_EmptyHashWriter = new CanonicalWriter();
        }

        internal ObservedWorldConstraintFrame(CanonicalBuffer<ObservedWorldConstraint> constraints) =>
            m_Constraints = constraints;

        public ObservedWorldConstraintFrame(
            SimulationTick tick,
            IReadOnlyList<ObservedWorldConstraint> constraints)
            : this(tick, CopyConstraints(tick, constraints))
        {
        }

        internal ObservedWorldConstraintFrame(
            SimulationTick tick,
            ObservedWorldConstraint[] constraints)
        {
            if (!tick.IsValid)
                throw new ArgumentException("Observed world constraint frame Tick is invalid.", nameof(tick));
            ObservedWorldConstraint[] values = constraints ?? throw new ArgumentNullException(nameof(constraints));
            Array.Sort(values, (left, right) => left.ActorId.CompareTo(right.ActorId));
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].TargetTick != tick || i > 0 && values[i - 1].ActorId == values[i].ActorId)
                    throw new ArgumentException("Observed world constraint frame contains a duplicate Actor or mismatched Tick.", nameof(constraints));
            }
            Tick = tick;
            m_Constraints = values;
            FrameHash = ObservedWorldConstraintCodec.ComputeHash(this);
        }

        internal void ResetEmpty(SimulationTick tick)
        {
            Tick = tick;
            FrameHash = ObservedWorldConstraintCodec.ComputeHash(this, m_EmptyHashWriter);
        }

        internal void ResetPrepared(SimulationTick tick, CanonicalWriter writer)
        {
            Tick = tick;
            FrameHash = ObservedWorldConstraintCodec.ComputeHash(this, writer);
        }

        internal void SetPreparedMetadata(SimulationTick tick, StableHash frameHash)
        {
            Tick = tick;
            FrameHash = frameHash;
        }

        public SimulationTick Tick { get; private set; }
        public IReadOnlyList<ObservedWorldConstraint> Constraints => m_Constraints;
        public StableHash FrameHash { get; private set; }
        public static ObservedWorldConstraintFrame Empty(SimulationTick tick) =>
            new ObservedWorldConstraintFrame(tick, Array.Empty<ObservedWorldConstraint>());

        static ObservedWorldConstraint[] CopyConstraints(
            SimulationTick tick,
            IReadOnlyList<ObservedWorldConstraint> constraints)
        {
            if (!tick.IsValid)
                throw new ArgumentException("Observed world constraint frame Tick is invalid.", nameof(tick));
            if (constraints == null)
                throw new ArgumentNullException(nameof(constraints));
            if (constraints.Count == 0)
                return Array.Empty<ObservedWorldConstraint>();
            var values = new ObservedWorldConstraint[constraints.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = constraints[i];
            return values;
        }
    }

    public interface IObservedWorldConstraintProfileProvider
    {
        StableHash ObservedContactShapeConfigurationHash { get; }
    }

    public static class ObservedWorldConstraintCodec
    {
        public const string CanonicalIdentity = "float32-observed-world-constraint-frame/1";

        [ThreadStatic] static CanonicalWriter s_HashWriter;

        internal static int MaximumEncodedByteCount(IReadOnlyList<ActorId> actors)
        {
            int bytes = checked(sizeof(int) + CanonicalIdentity.Length * sizeof(char) + sizeof(ulong) + sizeof(int));
            int bodyBytes = sizeof(int) + 8 * sizeof(float) + sizeof(byte) + sizeof(uint);
            int constraintBytes = sizeof(int) + sizeof(ulong) + 2 * bodyBytes + 2 * sizeof(ulong) +
                sizeof(byte) + sizeof(int) + 64;
            for (int index = 0; index < actors.Count; index++)
                bytes = checked(bytes + constraintBytes + 3 * actors[index].Value.Length * sizeof(char));
            return bytes;
        }

        public static StableHash ComputeHash(ObservedWorldConstraintFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            return ComputeHash(frame, s_HashWriter ??= new CanonicalWriter());
        }

        internal static StableHash ComputeHash(ObservedWorldConstraintFrame frame, CanonicalWriter writer)
        {
            writer.Reset();
            Write(writer, frame);
            return writer.ComputeHash();
        }

        public static void Write(CanonicalWriter writer, ObservedWorldConstraintFrame frame)
        {
            if (writer == null || frame == null)
                throw new ArgumentNullException(writer == null ? nameof(writer) : nameof(frame));
            writer.WriteText(CanonicalIdentity);
            writer.WriteUInt64(frame.Tick.Value);
            writer.WriteInt32(frame.Constraints.Count);
            for (int i = 0; i < frame.Constraints.Count; i++)
            {
                ObservedWorldConstraint value = frame.Constraints[i];
                writer.WriteText(value.ActorId.Value);
                writer.WriteUInt64(value.TargetTick.Value);
                WriteBody(writer, value.BeforeBody);
                WriteBody(writer, value.FinalBody);
                writer.WriteUInt64(value.SourcePreviousTick.Value);
                writer.WriteUInt64(value.SourceCurrentTick.Value);
                writer.WriteByte((byte)value.SamplingKind);
                writer.WriteHash(value.ContactShapeConfigurationHash);
            }
        }

        static void WriteBody(CanonicalWriter writer, WorldBodyState body)
        {
            writer.WriteText(body.ActorId.Value);
            writer.WriteVector3(body.Position);
            writer.WriteYaw(body.Yaw);
            writer.WriteVector3(body.Velocity);
            writer.WriteScalar(body.VerticalVelocity);
            writer.WriteBoolean(body.Grounded);
            writer.WriteUInt32((uint)body.Collision);
        }
    }

    public sealed class WorldSolveBatchRequest
    {
        readonly CharacterWorldSolveRequest[] m_Requests;

        public WorldSolveBatchRequest(
            SimulationTick tick,
            WorldSimulationState beforeWorldState,
            IReadOnlyList<CharacterWorldSolveRequest> requests,
            ObservedWorldConstraintFrame observedWorldConstraints)
            : this(requests?.Count ?? 0)
        {
            Reset(tick, beforeWorldState, requests, observedWorldConstraints);
        }

        internal WorldSolveBatchRequest(int actorCount)
        {
            if (actorCount < 0)
                throw new ArgumentOutOfRangeException(nameof(actorCount));
            m_Requests = actorCount == 0
                ? Array.Empty<CharacterWorldSolveRequest>()
                : new CharacterWorldSolveRequest[actorCount];
        }

        internal WorldSolveBatchRequest Reset(
            SimulationTick tick,
            WorldSimulationState beforeWorldState,
            IReadOnlyList<CharacterWorldSolveRequest> requests,
            ObservedWorldConstraintFrame observedWorldConstraints)
        {
            if (!tick.IsValid)
                throw new ArgumentException("World batch Tick is invalid.", nameof(tick));
            WorldSimulationState worldState = beforeWorldState ?? throw new ArgumentNullException(nameof(beforeWorldState));
            ObservedWorldConstraintFrame observedConstraints = observedWorldConstraints ?? throw new ArgumentNullException(nameof(observedWorldConstraints));
            if (observedConstraints.Tick != tick)
                throw new ArgumentException("Observed world constraint frame Tick does not match the batch.", nameof(observedWorldConstraints));
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
            int activeIndex = 0;
            for (int i = 0; i < observedConstraints.Constraints.Count && activeIndex < m_Requests.Length; i++)
            {
                ObservedWorldConstraint observed = observedConstraints.Constraints[i];
                while (activeIndex < m_Requests.Length &&
                       m_Requests[activeIndex].ActorId.CompareTo(observed.ActorId) < 0)
                    activeIndex++;
                if (activeIndex < m_Requests.Length && m_Requests[activeIndex].ActorId == observed.ActorId)
                    throw new ArgumentException($"Observed ActorId '{observed.ActorId}' is already active in the World batch.", nameof(observedWorldConstraints));
            }
            Tick = tick;
            BeforeWorldState = worldState;
            ObservedWorldConstraints = observedConstraints;
            NumericProfile = numericProfile;
            RequiredCapabilities = required;
            return this;
        }

        public SimulationNumericProfile NumericProfile { get; private set; }
        public SimulationTick Tick { get; private set; }
        public WorldSimulationState BeforeWorldState { get; private set; }
        public ObservedWorldConstraintFrame ObservedWorldConstraints { get; private set; }
        public IReadOnlyList<CharacterWorldSolveRequest> Requests => m_Requests;
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
            Float32Vector3 appliedDisplacement,
            Float32Scalar appliedYawDegrees)
        {
            if (!numericProfile.IsValid || !actorId.IsValid || !requestId.IsValid || !tick.IsValid || CanonicalTextValue.IsNullOrEmpty(solverId.Value))
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
        public Float32Vector3 AppliedDisplacement { get; }
        public Float32Scalar AppliedYawDegrees { get; }
    }

    public sealed class WorldSolveBatchResult
    {
        readonly IReadOnlyList<CharacterWorldSolveResult> m_Results;
        readonly Comparison<CharacterWorldSolveResult> m_CompareResults = CompareResults;

        public WorldSolveBatchResult(
            WorldSolveBatchRequest request,
            SolverImplementationId solverId,
            string solverVersion,
            WorldSimulationState nextWorldState,
            IReadOnlyList<CharacterWorldSolveResult> results)
            : this(CopyResults(results))
        {
            Reset(request, solverId, solverVersion, nextWorldState);
        }

        internal WorldSolveBatchResult(int actorCount)
            : this(new CharacterWorldSolveResult[actorCount])
        {
        }

        WorldSolveBatchResult(CharacterWorldSolveResult[] results)
        {
            m_Results = results;
        }

        internal CharacterWorldSolveResult[] ResultArray => (CharacterWorldSolveResult[])m_Results;

        internal WorldSolveBatchResult Reset(
            WorldSolveBatchRequest request,
            SolverImplementationId solverId,
            string solverVersion,
            WorldSimulationState nextWorldState)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            if (CanonicalTextValue.IsNullOrEmpty(solverId.Value))
                throw new ArgumentException("Solver identity is missing.", nameof(solverId));
            SolverId = solverId;
            SolverVersion = SimulationIdentity.Require(solverVersion, nameof(solverVersion));
            NextWorldState = nextWorldState ?? throw new ArgumentNullException(nameof(nextWorldState));
            if (nextWorldState.NumericProfile != request.NumericProfile)
                throw new ArgumentException("World result state Numeric Profile does not match request.", nameof(nextWorldState));
            if (!nextWorldState.SolverId.Equals(solverId) || !CanonicalTextValue.Equals(nextWorldState.SolverVersion, SolverVersion, StringComparison.Ordinal))
                throw new ArgumentException("Next World state does not match Solver identity.", nameof(nextWorldState));
            CharacterWorldSolveResult[] copied = ResultArray;
            Array.Sort(copied, m_CompareResults);
            if (copied.Length != request.Requests.Count || copied.Length != nextWorldState.Bodies.Count)
                throw new ArgumentException("World result must contain exactly one result per request.", "results");
            for (int i = 0; i < copied.Length; i++)
            {
                CharacterWorldSolveResult result = copied[i];
                CharacterWorldSolveRequest expected = request.Requests[i];
                if (result.NumericProfile != request.NumericProfile || result.ActorId != expected.ActorId || !result.RequestId.Equals(expected.RequestId) || result.Tick != request.Tick ||
                    !result.SolverId.Equals(solverId) || result.FinalBody.ActorId != expected.ActorId ||
                    !WorldSolveBatchRequest.BodyEquals(result.FinalBody, nextWorldState.Bodies[i]))
                    throw new ArgumentException("World result is missing, duplicated, unknown, or mismatched.", "results");
            }
            return this;
        }

        internal void ClearPrepared()
        {
            Request = null;
            SolverId = default;
            SolverVersion = null;
            NextWorldState = null;
            Array.Clear(ResultArray, 0, ResultArray.Length);
        }

        static int CompareResults(CharacterWorldSolveResult left, CharacterWorldSolveResult right) =>
            left.ActorId.CompareTo(right.ActorId);

        public WorldSolveBatchRequest Request { get; private set; }
        public SimulationTick Tick => Request.Tick;
        public SolverImplementationId SolverId { get; private set; }
        public string SolverVersion { get; private set; }
        public WorldSimulationState NextWorldState { get; private set; }
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
            if (!numericProfile.IsValid || CanonicalTextValue.IsNullOrEmpty(implementationId.Value) || capabilities == WorldCapability.None)
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

}
