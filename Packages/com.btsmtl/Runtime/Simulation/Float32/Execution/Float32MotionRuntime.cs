using System;
using System.Collections.Generic;
using ThirdPersonSimulation.Fixed;

namespace ThirdPersonSimulation
{
    public readonly struct ResolvedGameplayMotion
    {
        public ResolvedGameplayMotion(
            Float32Vector3 displacement,
            Float32Scalar yawDegrees,
            Float32Vector2 locomotionPlanarBasis,
            bool hasMotion,
            CommittedMovementPlaybackClock movementPlaybackClock,
            CommittedLocomotionPlanarMotionTimeline locomotionTimeline,
            CanonicalTextValue actionOwnerIdentity,
            CanonicalTextValue gameplayResultOwnerIdentity)
        {
            Displacement = displacement;
            YawDegrees = yawDegrees;
            LocomotionPlanarBasis = locomotionPlanarBasis;
            HasMotion = hasMotion;
            MovementPlaybackClock = movementPlaybackClock;
            LocomotionTimeline = locomotionTimeline;
            ActionOwnerIdentity = actionOwnerIdentity.IsNull ? string.Empty : actionOwnerIdentity;
            GameplayResultOwnerIdentity = gameplayResultOwnerIdentity.IsNull ? string.Empty : gameplayResultOwnerIdentity;
        }

        public Float32Vector3 Displacement { get; }
        public Float32Scalar YawDegrees { get; }
        public Float32Vector2 LocomotionPlanarBasis { get; }
        public bool HasMotion { get; }
        public CommittedMovementPlaybackClock MovementPlaybackClock { get; }
        public CommittedLocomotionPlanarMotionTimeline LocomotionTimeline { get; }
        public CanonicalTextValue ActionOwnerIdentity { get; }
        public CanonicalTextValue GameplayResultOwnerIdentity { get; }
    }

    internal enum SimulationMotionChannel
    {
        Locomotion = 0,
        Action = 1,
        GameplayResult = 2
    }

    internal enum SimulationMotionBlendMode
    {
        Additive = 0,
        WeightedBlend = 1,
        Override = 2
    }

    internal enum SimulationMotionContributionSpace
    {
        ActorLocal = 0,
        World = 1
    }

    internal readonly struct SimulationMotionContribution
    {
        public SimulationMotionContribution(
            SimulationExecutionSource source,
            CanonicalTextValue sourceIdentity,
            CharacterSkillId abilityId,
            ulong sourceGeneration,
            Float32Vector3 displacement,
            Float32Scalar yawDegrees,
            Float32Vector2 planarBasis,
            SimulationMotionContributionSpace space,
            Float32Scalar weight,
            int priority,
            SimulationMotionChannel channel,
            SimulationMotionBlendMode blendMode,
            bool consumeLowerChannels,
            CommittedMovementPlaybackClock movementPlaybackClock,
            CommittedLocomotionPlanarMotionTimeline locomotionTimeline)
        {
            if (!source.IsValid)
                throw new ArgumentException("Motion contribution source is invalid.", nameof(source));
            Source = source;
            SourceIdentity = CanonicalTextValue.IsNullOrEmpty(sourceIdentity)
                ? throw new ArgumentException("Motion contribution source identity is incomplete.", nameof(sourceIdentity))
                : sourceIdentity;
            AbilityId = abilityId;
            SourceGeneration = sourceGeneration;
            Displacement = displacement;
            YawDegrees = yawDegrees;
            PlanarBasis = planarBasis;
            Space = space;
            Weight = Float32Scalar.Clamp(weight, Float32Scalar.Zero, Float32Scalar.One);
            Priority = priority;
            Channel = channel;
            BlendMode = blendMode;
            ConsumeLowerChannels = consumeLowerChannels;
            if (channel == SimulationMotionChannel.Locomotion && !movementPlaybackClock.IsValid)
                throw new ArgumentException("Locomotion motion contribution has no committed Movement playback clock.", nameof(movementPlaybackClock));
            if (channel != SimulationMotionChannel.Locomotion && movementPlaybackClock.IsValid)
                throw new ArgumentException("Only the Locomotion motion channel may carry a committed Movement playback clock.", nameof(movementPlaybackClock));
            MovementPlaybackClock = movementPlaybackClock;
            if (channel == SimulationMotionChannel.Locomotion &&
                (!locomotionTimeline.IsValid || !locomotionTimeline.Matches(movementPlaybackClock)))
            {
                throw new ArgumentException("Locomotion motion contribution has no matching committed motion timeline.", nameof(locomotionTimeline));
            }
            if (channel != SimulationMotionChannel.Locomotion && locomotionTimeline.IsValid)
                throw new ArgumentException("Only the Locomotion motion channel may carry a committed motion timeline.", nameof(locomotionTimeline));
            LocomotionTimeline = locomotionTimeline;
        }

        public SimulationExecutionSource Source { get; }
        public CanonicalTextValue SourceIdentity { get; }
        public CharacterSkillId AbilityId { get; }
        public ulong SourceGeneration { get; }
        public Float32Vector3 Displacement { get; }
        public Float32Scalar YawDegrees { get; }
        public Float32Vector2 PlanarBasis { get; }
        public SimulationMotionContributionSpace Space { get; }
        public Float32Scalar Weight { get; }
        public int Priority { get; }
        public SimulationMotionChannel Channel { get; }
        public SimulationMotionBlendMode BlendMode { get; }
        public bool ConsumeLowerChannels { get; }
        public CommittedMovementPlaybackClock MovementPlaybackClock { get; }
        public CommittedLocomotionPlanarMotionTimeline LocomotionTimeline { get; }
        public bool HasDelta => Weight > Float32Scalar.Zero &&
            (Displacement != Float32Vector3.Zero || YawDegrees != Float32Scalar.Zero);
        public bool ClaimsLowerChannels => Weight > Float32Scalar.Zero &&
            BlendMode == SimulationMotionBlendMode.Override &&
            ConsumeLowerChannels;
        public bool CanResolve => HasDelta || ClaimsLowerChannels || MovementPlaybackClock.IsValid;
    }

    internal struct ResolvedMotionChannel
    {
        public ResolvedMotionChannel(
            SimulationMotionChannel channel,
            Float32Vector3 displacement,
            Float32Scalar yawDegrees,
            Float32Vector2 planarBasis,
            bool hasContribution,
            bool claimsLowerChannels,
            SimulationExecutionSource resolvedOwnerSource,
            CharacterSkillId resolvedOwnerAbilityId,
            CanonicalTextValue resolvedOwnerIdentity,
            CommittedMovementPlaybackClock movementPlaybackClock,
            CommittedLocomotionPlanarMotionTimeline locomotionTimeline,
            Float32Vector3 resolvedOwnerDisplacement,
            Float32Scalar resolvedOwnerYawDegrees,
            SimulationExecutionSource traceSource,
            CharacterSkillId traceAbilityId,
            ulong traceSourceGeneration,
            int participatingSourceCount,
            ulong participatingSourceFingerprint)
        {
            Channel = channel;
            Displacement = displacement;
            YawDegrees = yawDegrees;
            PlanarBasis = planarBasis;
            HasContribution = hasContribution;
            ClaimsLowerChannels = claimsLowerChannels;
            ResolvedOwnerSource = resolvedOwnerSource;
            ResolvedOwnerAbilityId = resolvedOwnerAbilityId;
            ResolvedOwnerIdentity = resolvedOwnerIdentity;
            MovementPlaybackClock = movementPlaybackClock;
            LocomotionTimeline = locomotionTimeline;
            ResolvedOwnerDisplacement = resolvedOwnerDisplacement;
            ResolvedOwnerYawDegrees = resolvedOwnerYawDegrees;
            TraceSource = traceSource;
            TraceAbilityId = traceAbilityId;
            TraceSourceGeneration = traceSourceGeneration;
            ParticipatingSourceCount = participatingSourceCount;
            ParticipatingSourceFingerprint = participatingSourceFingerprint;
            MotionCorrectionDisplacement = Float32Vector3.Zero;
            MotionCorrectionYawDegrees = Float32Scalar.Zero;
        }

        public SimulationMotionChannel Channel { get; }
        public Float32Vector3 Displacement { get; private set; }
        public Float32Scalar YawDegrees { get; private set; }
        public Float32Vector2 PlanarBasis { get; }
        public bool HasContribution { get; }
        public bool ClaimsLowerChannels { get; }
        public SimulationExecutionSource ResolvedOwnerSource { get; }
        public CharacterSkillId ResolvedOwnerAbilityId { get; }
        public CanonicalTextValue ResolvedOwnerIdentity { get; }
        public CommittedMovementPlaybackClock MovementPlaybackClock { get; }
        public CommittedLocomotionPlanarMotionTimeline LocomotionTimeline { get; }
        public Float32Vector3 ResolvedOwnerDisplacement { get; }
        public Float32Scalar ResolvedOwnerYawDegrees { get; }
        public SimulationExecutionSource TraceSource { get; }
        public CharacterSkillId TraceAbilityId { get; }
        public ulong TraceSourceGeneration { get; }
        public int ParticipatingSourceCount { get; }
        public ulong ParticipatingSourceFingerprint { get; }
        public bool HasDelta => Displacement != Float32Vector3.Zero || YawDegrees != Float32Scalar.Zero;

        public Float32Vector3 MotionCorrectionDisplacement { get; private set; }
        public Float32Scalar MotionCorrectionYawDegrees { get; private set; }

        public void ApplyMotionWarp(
            Float32Vector3 sourceDisplacement, Float32Scalar sourceYaw,
            Float32Vector3 warpedDisplacement, Float32Scalar warpedYaw)
        {
            Displacement += warpedDisplacement - sourceDisplacement;
            YawDegrees += warpedYaw - sourceYaw;
            MotionCorrectionDisplacement += warpedDisplacement - sourceDisplacement;
            MotionCorrectionYawDegrees += warpedYaw - sourceYaw;
        }
    }

    internal readonly struct Float32MotionWarpState
    {
        public Float32MotionWarpState(
            bool active,
            bool initialized,
            ulong playbackGeneration,
            int cycle,
            Float32ActionInstanceReference actionInstance,
            Float32Vector3 startBodyPosition,
            Float32Yaw startBodyYaw,
            Float32Vector3 sourceWindowStartPosition,
            Float32Scalar sourceWindowStartYaw,
            Float32Vector3 resolvedTargetPosition,
            Float32Yaw resolvedTargetYaw,
            ProgramMotionWarpLimitResult limitResult,
            Float32Vector3 previousWarpedPosition,
            Float32Yaw previousWarpedYaw,
            Float32Scalar lastPositionProgress,
            Float32Scalar lastYawProgress,
            OperationHandle sourceOperation,
            MotionWarpWindowDecision decision,
            bool hasTarget,
            Float32Vector3 capturedTargetPosition,
            Float32Yaw capturedTargetYaw)
        {
            Active = active;
            Initialized = initialized;
            PlaybackGeneration = playbackGeneration;
            Cycle = cycle;
            ActionInstance = actionInstance;
            StartBodyPosition = startBodyPosition;
            StartBodyYaw = startBodyYaw;
            SourceWindowStartPosition = sourceWindowStartPosition;
            SourceWindowStartYaw = sourceWindowStartYaw;
            ResolvedTargetPosition = resolvedTargetPosition;
            ResolvedTargetYaw = resolvedTargetYaw;
            LimitResult = limitResult;
            PreviousWarpedPosition = previousWarpedPosition;
            PreviousWarpedYaw = previousWarpedYaw;
            LastPositionProgress = lastPositionProgress;
            LastYawProgress = lastYawProgress;
            SourceOperation = sourceOperation;
            Decision = decision;
            HasTarget = hasTarget;
            CapturedTargetPosition = capturedTargetPosition;
            CapturedTargetYaw = capturedTargetYaw;
        }

        public void Retain() => ActionInstance.Retain();
        public void Release() => ActionInstance.Release();
        public bool Active { get; }
        public bool Initialized { get; }
        public ulong PlaybackGeneration { get; }
        public int Cycle { get; }
        public Float32ActionInstanceReference ActionInstance { get; }
        public Float32Vector3 StartBodyPosition { get; }
        public Float32Yaw StartBodyYaw { get; }
        public Float32Vector3 SourceWindowStartPosition { get; }
        public Float32Scalar SourceWindowStartYaw { get; }
        public Float32Vector3 ResolvedTargetPosition { get; }
        public Float32Yaw ResolvedTargetYaw { get; }
        public ProgramMotionWarpLimitResult LimitResult { get; }
        public Float32Vector3 PreviousWarpedPosition { get; }
        public Float32Yaw PreviousWarpedYaw { get; }
        public Float32Scalar LastPositionProgress { get; }
        public Float32Scalar LastYawProgress { get; }
        public OperationHandle SourceOperation { get; }
        public MotionWarpWindowDecision Decision { get; }
        public bool HasTarget { get; }
        public Float32Vector3 CapturedTargetPosition { get; }
        public Float32Yaw CapturedTargetYaw { get; }
        public bool Equals(Float32MotionWarpState other) =>
            Active == other.Active &&
            Initialized == other.Initialized &&
            PlaybackGeneration == other.PlaybackGeneration &&
            Cycle == other.Cycle &&
            ActionInstance.Equals(other.ActionInstance) &&
            StartBodyPosition.Equals(other.StartBodyPosition) &&
            StartBodyYaw.Equals(other.StartBodyYaw) &&
            SourceWindowStartPosition.Equals(other.SourceWindowStartPosition) &&
            SourceWindowStartYaw.Equals(other.SourceWindowStartYaw) &&
            ResolvedTargetPosition.Equals(other.ResolvedTargetPosition) &&
            ResolvedTargetYaw.Equals(other.ResolvedTargetYaw) &&
            LimitResult == other.LimitResult &&
            PreviousWarpedPosition.Equals(other.PreviousWarpedPosition) &&
            PreviousWarpedYaw.Equals(other.PreviousWarpedYaw) &&
            LastPositionProgress.Equals(other.LastPositionProgress) &&
            LastYawProgress.Equals(other.LastYawProgress) &&
            SourceOperation.Equals(other.SourceOperation) &&
            Decision == other.Decision &&
            HasTarget == other.HasTarget &&
            CapturedTargetPosition.Equals(other.CapturedTargetPosition) &&
            CapturedTargetYaw.Equals(other.CapturedTargetYaw);

        public Float32MotionWarpState WithDecision(MotionWarpWindowDecision decision) =>
            new Float32MotionWarpState(Active, Initialized, PlaybackGeneration, Cycle, ActionInstance,
                StartBodyPosition, StartBodyYaw, SourceWindowStartPosition, SourceWindowStartYaw,
                ResolvedTargetPosition, ResolvedTargetYaw, LimitResult, PreviousWarpedPosition,
                PreviousWarpedYaw, LastPositionProgress, LastYawProgress, SourceOperation,
                decision, HasTarget, CapturedTargetPosition, CapturedTargetYaw);

        public Float32MotionWarpState WithProgress(
            Float32Vector3 previousWarpedPosition,
            Float32Yaw previousWarpedYaw,
            Float32Scalar lastPositionProgress,
            Float32Scalar lastYawProgress) =>
            new Float32MotionWarpState(
                Active,
                Initialized,
                PlaybackGeneration,
                Cycle,
                ActionInstance,
                StartBodyPosition,
                StartBodyYaw,
                SourceWindowStartPosition,
                SourceWindowStartYaw,
                ResolvedTargetPosition,
                ResolvedTargetYaw,
                LimitResult,
                previousWarpedPosition,
                previousWarpedYaw,
                lastPositionProgress,
                lastYawProgress,
                SourceOperation,
                Decision,
                HasTarget,
                CapturedTargetPosition,
                CapturedTargetYaw);
    }

    internal static class Float32CharacterMotionResolver
    {


        public static ResolvedMotionChannel ResolveChannel(
            SimulationMotionContribution[] contributions,
            int contributionCount,
            Float32Yaw bodyYaw,
            SimulationMotionChannel channel)
        {
            Float32Vector3 additiveDisplacement = Float32Vector3.Zero;
            Float32Scalar additiveYaw = Float32Scalar.Zero;
            Float32Vector3 weightedDisplacement = Float32Vector3.Zero;
            Float32Scalar weightedYaw = Float32Scalar.Zero;
            Float32Scalar totalWeight = Float32Scalar.Zero;
            SimulationMotionContribution overrideWinner = default;
            Float32Vector3 overrideDisplacement = Float32Vector3.Zero;
            Float32Scalar overrideYaw = Float32Scalar.Zero;
            SimulationExecutionSource traceSource = default;
            CharacterSkillId traceAbilityId = default;
            ulong traceSourceGeneration = 0;
            int sourceCount = 0;
            ulong sourceFingerprint = 1469598103934665603UL;
            bool hasAdditive = false;
            bool hasWeighted = false;
            bool hasOverride = false;
            for (int i = 0; i < contributionCount; i++)
            {
                ref readonly SimulationMotionContribution contribution = ref contributions[i];
                if (contribution.Channel != channel || !contribution.CanResolve)
                    continue;
                if (!traceSource.IsValid)
                {
                    traceSource = contribution.Source;
                    traceAbilityId = contribution.AbilityId;
                    traceSourceGeneration = contribution.SourceGeneration;
                }
                sourceCount++;
                sourceFingerprint = MixSource(sourceFingerprint, contribution.SourceIdentity.Span);
                Float32Vector3 resolved = contribution.Space == SimulationMotionContributionSpace.ActorLocal
                    ? Float32Angle.RotatePlanar(contribution.Displacement, bodyYaw)
                    : contribution.Displacement;
                switch (contribution.BlendMode)
                {
                    case SimulationMotionBlendMode.Additive:
                        additiveDisplacement += resolved * contribution.Weight;
                        additiveYaw += contribution.YawDegrees * contribution.Weight;
                        hasAdditive = true;
                        break;
                    case SimulationMotionBlendMode.WeightedBlend:
                        weightedDisplacement += resolved * contribution.Weight;
                        weightedYaw += contribution.YawDegrees * contribution.Weight;
                        totalWeight += contribution.Weight;
                        hasWeighted = true;
                        break;
                    case SimulationMotionBlendMode.Override:
                        if (!hasOverride || contribution.Priority > overrideWinner.Priority)
                        {
                            overrideWinner = contribution;
                            overrideDisplacement = resolved * contribution.Weight;
                            overrideYaw = contribution.YawDegrees * contribution.Weight;
                            hasOverride = true;
                        }
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Motion contribution '{contribution.SourceIdentity}' has invalid blend mode '{contribution.BlendMode}'.");
                }
            }
            if (!hasAdditive && !hasWeighted && !hasOverride)
                return new ResolvedMotionChannel(channel, Float32Vector3.Zero, Float32Scalar.Zero, Float32Vector2.Zero, false, false, default, default, string.Empty, default, default, Float32Vector3.Zero, Float32Scalar.Zero, default, default, 0, 0, 0);

            Float32Vector3 channelDisplacement = additiveDisplacement;
            Float32Scalar channelYaw = additiveYaw;
            if (hasOverride)
            {
                channelDisplacement += overrideDisplacement;
                channelYaw += overrideYaw;
            }
            else if (hasWeighted && totalWeight > Float32Scalar.Zero)
            {
                channelDisplacement += new Float32Vector3(
                    weightedDisplacement.X / totalWeight,
                    weightedDisplacement.Y / totalWeight,
                    weightedDisplacement.Z / totalWeight);
                channelYaw += weightedYaw / totalWeight;
            }
            CommittedMovementPlaybackClock movementPlaybackClock =
                channel == SimulationMotionChannel.Locomotion && hasOverride
                    ? overrideWinner.MovementPlaybackClock
                    : default;
            if (channel == SimulationMotionChannel.Locomotion && !movementPlaybackClock.IsValid)
                throw new InvalidOperationException("Resolved Locomotion motion has no single committed Movement playback clock owner.");
            var result = new ResolvedMotionChannel(
                channel,
                channelDisplacement,
                channelYaw,
                hasOverride ? overrideWinner.PlanarBasis : Float32Vector2.Zero,
                true,
                hasOverride && overrideWinner.ConsumeLowerChannels,
                hasOverride ? overrideWinner.Source : default,
                hasOverride ? overrideWinner.AbilityId : default,
                hasOverride ? overrideWinner.SourceIdentity : string.Empty,
                movementPlaybackClock,
                channel == SimulationMotionChannel.Locomotion && hasOverride
                    ? overrideWinner.LocomotionTimeline
                    : default,
                hasOverride ? overrideDisplacement : Float32Vector3.Zero,
                hasOverride ? overrideYaw : Float32Scalar.Zero,
                hasOverride ? overrideWinner.Source : traceSource,
                hasOverride ? overrideWinner.AbilityId : traceAbilityId,
                hasOverride ? overrideWinner.SourceGeneration : traceSourceGeneration,
                sourceCount,
                sourceFingerprint);
            return result;
        }

        public static ResolvedGameplayMotion Compose(
            in ResolvedMotionChannel locomotion,
            in ResolvedMotionChannel action,
            in ResolvedMotionChannel gameplayResult)
        {
            Float32Vector3 displacement = Float32Vector3.Zero;
            Float32Scalar yaw = Float32Scalar.Zero;
            ComposeChannel(locomotion, ref displacement, ref yaw);
            ComposeChannel(action, ref displacement, ref yaw);
            ComposeChannel(gameplayResult, ref displacement, ref yaw);
            return new ResolvedGameplayMotion(
                displacement,
                yaw,
                locomotion.PlanarBasis,
                displacement != Float32Vector3.Zero || yaw != Float32Scalar.Zero,
                locomotion.MovementPlaybackClock,
                locomotion.LocomotionTimeline,
                action.ResolvedOwnerIdentity,
                gameplayResult.ResolvedOwnerIdentity);
        }

        static void ComposeChannel(
            in ResolvedMotionChannel channel,
            ref Float32Vector3 displacement,
            ref Float32Scalar yaw)
        {
            if (!channel.HasContribution)
                return;
            if (channel.ClaimsLowerChannels)
            {
                displacement = channel.Displacement;
                yaw = channel.YawDegrees;
                return;
            }
            displacement += channel.Displacement;
            yaw += channel.YawDegrees;
        }

        static ulong MixSource(ulong hash, ReadOnlySpan<char> identity)
        {
            unchecked
            {
                for (int i = 0; i < identity.Length; i++)
                {
                    hash ^= identity[i];
                    hash *= 1099511628211UL;
                }
                return hash;
            }
        }
    }

    internal sealed class Float32MotionAccumulator : Float32OperationModule,
        IFloat32MotionContributionSink
    {
        readonly Float32AbilityExecutionFrame m_Frame;
        readonly Float32AbilityExecutionWorkspace m_Workspace;
        readonly List<AbilityTimelineLogicMotionWarp> m_TimelineMotionWarps;
        readonly Float32MotionWarpTarget m_MotionWarp;

        public Float32MotionAccumulator(
            Float32GameplayAbilityExecutionAccess access,
            Float32AbilityExecutionFrame frame,
            Float32AbilityExecutionWorkspace workspace,
            List<AbilityTimelineLogicMotionWarp> timelineMotionWarps,
            Float32ActionStateStore actions)
            : base(access)
        {
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            m_Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            m_TimelineMotionWarps = timelineMotionWarps ?? throw new ArgumentNullException(nameof(timelineMotionWarps));
            m_MotionWarp = new Float32MotionWarpTarget(access, frame, actions);
        }

        public void Submit(SimulationMotionContribution contribution)
        {
            if (!contribution.CanResolve)
                return;
            m_Workspace.SubmitMotionContribution(contribution);
            if (m_Frame.Trace.Enabled)
            {
                CommittedMovementPlaybackClock clock = contribution.MovementPlaybackClock;
                var detail = new SimulationMotionTrace(SimulationMotionTraceDetailKind.Contribution,
                    contribution.Displacement, contribution.YawDegrees, in clock,
                    channel: (int)contribution.Channel, blend: (int)contribution.BlendMode, priority: contribution.Priority,
                    weight: contribution.Weight, claimsLowerChannels: contribution.ClaimsLowerChannels);
                m_Frame.Trace.Add(contribution.Source, "motion_contribution", SimulationTraceSeverity.Detail,
                    string.Empty, contribution.SourceGeneration, structured: m_Frame.CaptureMotionTrace(in detail));
            }
        }

        public void ApplyTimelineMotionWarps(ref ResolvedMotionChannel action)
        {
            OperationHandle resolvedOwner = action.ResolvedOwnerAbilityId == m_Ability.AbilityId &&
                action.ResolvedOwnerSource.IsSkillOperation
                ? action.ResolvedOwnerSource.Operation
                : OperationHandle.Invalid;
            AbilityTimelineMotionWarpRuntime.ApplyDirect(
                m_TimelineMotionWarps,
                resolvedOwner,
                action.ResolvedOwnerIdentity,
                ref action,
                m_MotionWarp);
        }

    }

    internal sealed class Float32MotionWarpTarget : Float32OperationModule,
        IAbilityTimelineMotionWarpTarget<ResolvedMotionChannel>
    {
        readonly Float32AbilityExecutionFrame m_Frame;
        readonly Float32ActionStateStore m_Actions;

        public Float32MotionWarpTarget(
            Float32GameplayAbilityExecutionAccess access,
            Float32AbilityExecutionFrame frame,
            Float32ActionStateStore actions)
            : base(access)
        {
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            m_Actions = actions ?? throw new ArgumentNullException(nameof(actions));
        }

        public void CaptureWindowEntry(in AbilityTimelineLogicMotionWarp warp)
        {
            Float32MotionWarpState state = m_Frame.SkillState.GetMotionWarpState(warp.StateOperation);
            if (state.Active && state.PlaybackGeneration == warp.PlaybackGeneration && state.Cycle == warp.Cycle)
                return;
            m_Actions.TryGetEvaluatedInstance(warp.ActionInstanceId, out Float32ActionInstanceState action);
            MotionWarpWindowDecision decision = !warp.Enabled ? MotionWarpWindowDecision.Disabled
                : !warp.HasTarget && warp.RotationMethod != ProgramMotionWarpRotationMethod.TargetResponse
                    ? MotionWarpWindowDecision.NoTarget : MotionWarpWindowDecision.Accepted;
            var targetPosition = new Float32Vector3(Float32Scalar.FromSingle(warp.Target.X.ToSingle()), Float32Scalar.FromSingle(warp.Target.Y.ToSingle()), Float32Scalar.FromSingle(warp.Target.Z.ToSingle()));
            var targetYaw = new Float32Yaw(Float32Scalar.FromSingle(warp.Target.Yaw.ToSingle()));
            m_Frame.SkillState.SetMotionWarpState(warp.StateOperation,
                new Float32MotionWarpState(true, false, warp.PlaybackGeneration, warp.Cycle,
                    Float32ActionInstanceReference.FromInstance(action), default, default, default, default,
                    default, default, default, default, default, default, default, warp.Source.Operation,
                    decision, warp.HasTarget, targetPosition, targetYaw));
            Trace(warp, decision == MotionWarpWindowDecision.Disabled ? MotionModifierDiagnosticCode.Disabled :
                decision == MotionWarpWindowDecision.NoTarget ? MotionModifierDiagnosticCode.NoTargetByOptionalPolicy :
                MotionModifierDiagnosticCode.WindowEntered);
        }

        public void ApplyTimelineMotionWarp(in AbilityTimelineLogicMotionWarp warp, ref ResolvedMotionChannel channel)
        {
            Float32MotionWarpState storedState = m_Frame.SkillState.GetMotionWarpState(warp.StateOperation);
            if (storedState.Decision != MotionWarpWindowDecision.Accepted)
                return;
            if (warp.RotationMethod == ProgramMotionWarpRotationMethod.TargetResponse)
            {
                ApplyTargetResponse(warp, storedState, ref channel);
                Trace(warp, MotionModifierDiagnosticCode.Applied);
                return;
            }
            Float32Vector3 rawSourceDelta = Float32Angle.RotatePlanar(
                new Float32Vector3(
                    Float32Scalar.FromSingle((warp.CurrentPositionX - warp.PreviousPositionX).ToSingle()),
                    Float32Scalar.FromSingle((warp.CurrentPositionY - warp.PreviousPositionY).ToSingle()),
                    Float32Scalar.FromSingle((warp.CurrentPositionZ - warp.PreviousPositionZ).ToSingle())),
                m_Frame.BodyFacts.Yaw);
            Float32Scalar rawSourceYawDelta = Float32Scalar.FromSingle((warp.CurrentYawDegrees - warp.PreviousYawDegrees).ToSingle());
            Float32Vector3 startBodyPosition;
            Float32Yaw startBodyYaw;
            Float32Vector3 sourceWindowStartPosition;
            Float32Scalar sourceWindowStartYaw;
            Float32Vector3 resolvedTargetPosition;
            Float32Yaw resolvedTargetYaw;
            ProgramMotionWarpLimitResult limitResult;
            Float32Vector3 previousWarpedPosition;
            Float32Yaw previousWarpedYaw;
            Float32Scalar previousPositionProgress;
            Float32Scalar previousYawProgress;
            if (!storedState.Initialized)
            {
                ResolveDirectTarget(warp, storedState.CapturedTargetPosition, storedState.CapturedTargetYaw,
                    m_Frame.BodyFacts.Position + channel.MotionCorrectionDisplacement +
                        Float32Angle.RotatePlanar(new Float32Vector3(Float32Scalar.FromSingle(warp.SourcePrefix.X.ToSingle()),
                            Float32Scalar.FromSingle(warp.SourcePrefix.Y.ToSingle()), Float32Scalar.FromSingle(warp.SourcePrefix.Z.ToSingle())), m_Frame.BodyFacts.Yaw),
                    new Float32Yaw(m_Frame.BodyFacts.Yaw.Degrees + channel.MotionCorrectionYawDegrees + Float32Scalar.FromSingle(warp.SourcePrefix.Yaw.ToSingle())),
                    out startBodyPosition,
                    out startBodyYaw,
                    out sourceWindowStartPosition,
                    out sourceWindowStartYaw,
                    out resolvedTargetPosition,
                    out resolvedTargetYaw,
                    out limitResult);
                if (limitResult == ProgramMotionWarpLimitResult.PreservedByLimitPolicy)
                {
                    m_Frame.SkillState.SetMotionWarpState(warp.StateOperation, storedState.WithDecision(MotionWarpWindowDecision.PreservedByLimitPolicy));
                    Trace(warp, MotionModifierDiagnosticCode.PreservedByLimitPolicy);
                    channel.ApplyMotionWarp(rawSourceDelta, rawSourceYawDelta, rawSourceDelta, rawSourceYawDelta);
                    return;
                }
                EvaluateDirectPose(warp,
                    warp.PreviousPositionX,
                    warp.PreviousPositionY,
                    warp.PreviousPositionZ,
                    warp.PreviousYawDegrees,
                    warp.PreviousPositionProgress,
                    warp.PreviousYawProgress,
                    warp.PreviousTime,
                    startBodyPosition,
                    startBodyYaw,
                    sourceWindowStartPosition,
                    sourceWindowStartYaw,
                    resolvedTargetPosition,
                    resolvedTargetYaw,
                    out previousWarpedPosition,
                    out previousWarpedYaw);
                previousPositionProgress = Float32Scalar.FromSingle(warp.PreviousPositionProgress.ToSingle());
                previousYawProgress = Float32Scalar.FromSingle(warp.PreviousYawProgress.ToSingle());
                storedState = new Float32MotionWarpState(
                        true,
                        true,
                        warp.PlaybackGeneration,
                        warp.Cycle,
                        storedState.ActionInstance,
                        startBodyPosition,
                        startBodyYaw,
                        sourceWindowStartPosition,
                        sourceWindowStartYaw,
                        resolvedTargetPosition,
                        resolvedTargetYaw,
                        limitResult,
                        previousWarpedPosition,
                        previousWarpedYaw,
                        previousPositionProgress,
                        previousYawProgress,
                        warp.Source.Operation,
                        storedState.Decision,
                        storedState.HasTarget,
                        storedState.CapturedTargetPosition,
                        storedState.CapturedTargetYaw);
            }
            else
            {
                startBodyPosition = storedState.StartBodyPosition;
                startBodyYaw = storedState.StartBodyYaw;
                sourceWindowStartPosition = storedState.SourceWindowStartPosition;
                sourceWindowStartYaw = storedState.SourceWindowStartYaw;
                resolvedTargetPosition = storedState.ResolvedTargetPosition;
                resolvedTargetYaw = storedState.ResolvedTargetYaw;
                limitResult = storedState.LimitResult;
                previousWarpedPosition = storedState.PreviousWarpedPosition;
                previousWarpedYaw = storedState.PreviousWarpedYaw;
                previousPositionProgress = storedState.LastPositionProgress;
                previousYawProgress = storedState.LastYawProgress;
            }
            Float32Scalar positionProgress = Float32Scalar.FromSingle(warp.CurrentPositionProgress.ToSingle());
            Float32Scalar yawProgress = Float32Scalar.FromSingle(warp.CurrentYawProgress.ToSingle());
            if (positionProgress < previousPositionProgress || yawProgress < previousYawProgress)
                FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.InvalidState,
                    "Cumulative Timeline MotionWarp progress moved backwards.");
            EvaluateDirectPose(warp,
                warp.CurrentPositionX,
                warp.CurrentPositionY,
                warp.CurrentPositionZ,
                warp.CurrentYawDegrees,
                warp.CurrentPositionProgress,
                warp.CurrentYawProgress,
                warp.CurrentTime,
                startBodyPosition,
                startBodyYaw,
                sourceWindowStartPosition,
                sourceWindowStartYaw,
                resolvedTargetPosition,
                resolvedTargetYaw,
                out Float32Vector3 currentWarpedPosition,
                out Float32Yaw currentWarpedYaw);
            channel.ApplyMotionWarp(rawSourceDelta, rawSourceYawDelta,
                currentWarpedPosition - previousWarpedPosition,
                Float32Angle.Delta(previousWarpedYaw, currentWarpedYaw));
            m_Frame.SkillState.SetMotionWarpState(warp.StateOperation,
                storedState.WithProgress(
                    currentWarpedPosition,
                    currentWarpedYaw,
                    positionProgress,
                    yawProgress));
            Trace(warp, limitResult == ProgramMotionWarpLimitResult.AppliedClamped
                ? MotionModifierDiagnosticCode.AppliedClamped : MotionModifierDiagnosticCode.Applied);
        }

        void ApplyTargetResponse(
            in AbilityTimelineLogicMotionWarp warp,
            Float32MotionWarpState state,
            ref ResolvedMotionChannel channel)
        {
            if (warp.TranslationMode != ProgramMotionWarpTranslationMode.Disabled ||
                warp.YawResponse < ThirdPersonSimulation.Fixed.FixedScalar.Zero ||
                warp.InputYawResponse < ThirdPersonSimulation.Fixed.FixedScalar.Zero)
                FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.InvalidState,
                    "Target response requires non-negative response and no translation correction.");
            Float32Yaw currentYaw = new Float32Yaw(m_Frame.BodyFacts.Yaw.Degrees + channel.MotionCorrectionYawDegrees);
            Float32Scalar yawDelta = Float32Scalar.Zero;
            Float32Yaw targetYaw = currentYaw;
            Float32Scalar response = Float32Scalar.FromSingle((warp.YawResponse).ToSingle());
            bool useTarget = state.HasTarget && response > Float32Scalar.Zero;
            if (useTarget)
            {
                targetYaw = state.CapturedTargetYaw;
                if (warp.RotationMode == ProgramMotionWarpRotationMode.FaceTarget)
                {
                    Float32Vector3 direction = state.CapturedTargetPosition -
                        (m_Frame.BodyFacts.Position + channel.MotionCorrectionDisplacement);
                    targetYaw = direction.X != Float32Scalar.Zero || direction.Z != Float32Scalar.Zero
                        ? Float32Angle.FromPlanarDirection(direction.X, direction.Z)
                        : currentYaw;
                }
            }
            else
            {
                response = Float32Scalar.FromSingle((warp.InputYawResponse).ToSingle());
                if (response > Float32Scalar.Zero)
                {
                    Float32Vector2 direction = new Float32Vector2(Float32Scalar.FromSingle(warp.InputDirectionX.ToSingle()), Float32Scalar.FromSingle(warp.InputDirectionZ.ToSingle()));
                    if (direction != Float32Vector2.Zero)
                        targetYaw = Float32Angle.FromPlanarDirection(direction.X, direction.Y);
                }
            }
            Float32Scalar delta = Float32Scalar.FromSingle((warp.CurrentTime - warp.PreviousTime).ToSingle());
            Float32Scalar amount = Float32Scalar.Clamp(response * delta, Float32Scalar.Zero, Float32Scalar.One);
            Float32Yaw desired = new Float32Yaw(targetYaw.Degrees + Float32Scalar.FromSingle(warp.TargetYawOffsetDegrees));
            Float32Scalar maximum = Float32Scalar.FromSingle(warp.MaximumYawCorrectionDegrees);
            yawDelta = Float32Scalar.Clamp(Float32Angle.Delta(currentYaw, desired), -maximum, maximum) * amount;
            var localDelta = new Float32Vector3(
                Float32Scalar.FromSingle((warp.CurrentPositionX - warp.PreviousPositionX).ToSingle()),
                Float32Scalar.FromSingle((warp.CurrentPositionY - warp.PreviousPositionY).ToSingle()),
                Float32Scalar.FromSingle((warp.CurrentPositionZ - warp.PreviousPositionZ).ToSingle()));
            Float32Vector3 previousDirectionDelta = Float32Angle.RotatePlanar(localDelta, m_Frame.BodyFacts.Yaw);
            Float32Vector3 nextDirectionDelta = Float32Angle.RotatePlanar(localDelta, new Float32Yaw(currentYaw.Degrees + yawDelta));
            channel.ApplyMotionWarp(
                previousDirectionDelta,
                Float32Scalar.FromSingle((warp.CurrentYawDegrees - warp.PreviousYawDegrees).ToSingle()),
                nextDirectionDelta, yawDelta);
        }

        public void FailTimelineMotionWarp(OperationHandle operation, string code, string detail)
        {
            throw new InvalidOperationException($"{code}: {detail}");
        }

        public void TraceSourceNotResolved(in AbilityTimelineLogicMotionWarp warp) => Trace(warp, MotionModifierDiagnosticCode.SourceNotResolved);

        void Trace(in AbilityTimelineLogicMotionWarp warp, string code)
        {
            m_Frame.Trace.AddMotionWarp(warp, code);
        }

        void ResolveDirectTarget(
            in AbilityTimelineLogicMotionWarp warp,
            Float32Vector3 targetPosition,
            Float32Yaw targetYaw,
            Float32Vector3 bodyPosition,
            Float32Yaw bodyYaw,
            out Float32Vector3 startBodyPosition,
            out Float32Yaw startBodyYaw,
            out Float32Vector3 sourceWindowStartPosition,
            out Float32Scalar sourceWindowStartYaw,
            out Float32Vector3 resolvedTargetPosition,
            out Float32Yaw resolvedTargetYaw,
            out ProgramMotionWarpLimitResult limitResult)
        {
            startBodyPosition = bodyPosition;
            startBodyYaw = bodyYaw;
            sourceWindowStartPosition = new Float32Vector3(
                Float32Scalar.FromSingle(warp.SourceStartPositionX.ToSingle()),
                Float32Scalar.FromSingle(warp.SourceStartPositionY.ToSingle()),
                Float32Scalar.FromSingle(warp.SourceStartPositionZ.ToSingle()));
            sourceWindowStartYaw = Float32Scalar.FromSingle(warp.SourceStartYawDegrees.ToSingle());
            Float32Vector3 sourceEnd = new Float32Vector3(
                Float32Scalar.FromSingle((warp.SourceEndPositionX - warp.SourceStartPositionX).ToSingle()),
                Float32Scalar.FromSingle((warp.SourceEndPositionY - warp.SourceStartPositionY).ToSingle()),
                Float32Scalar.FromSingle((warp.SourceEndPositionZ - warp.SourceStartPositionZ).ToSingle()));
            Float32Scalar sourceEndYaw = Float32Scalar.FromSingle((warp.SourceEndYawDegrees - warp.SourceStartYawDegrees).ToSingle());
            Float32Vector3 nominalSourceEndOffset = Float32Angle.RotatePlanar(sourceEnd, startBodyYaw);
            Float32Vector3 nominalSourceEnd = startBodyPosition + nominalSourceEndOffset;
            Float32Vector3 requestedTargetPosition = warp.TranslationMode == ProgramMotionWarpTranslationMode.Disabled
                ? nominalSourceEnd
                : ResolveDirectTargetPosition(warp, targetPosition, targetYaw, startBodyPosition, startBodyYaw, nominalSourceEnd.Y);
            Float32Vector3 requestedPositionCorrection = new Float32Vector3(
                requestedTargetPosition.X - nominalSourceEnd.X,
                Float32Scalar.Zero,
                requestedTargetPosition.Z - nominalSourceEnd.Z);
            Float32Scalar maximumPosition = warp.TranslationMode == ProgramMotionWarpTranslationMode.Disabled
                ? Float32Scalar.Zero
                : Float32Scalar.FromSingle(warp.MaximumPositionCorrection);
            bool positionExceeded = warp.TranslationMode != ProgramMotionWarpTranslationMode.Disabled &&
                                    new Float32Vector2(requestedPositionCorrection.X, requestedPositionCorrection.Z).Magnitude > maximumPosition;
            Float32Vector3 effectivePositionCorrection = positionExceeded
                ? ClampMagnitude(requestedPositionCorrection, maximumPosition)
                : requestedPositionCorrection;
            resolvedTargetPosition = nominalSourceEnd + effectivePositionCorrection;
            Float32Yaw nominalSourceEndYaw = new Float32Yaw(startBodyYaw.Degrees + sourceEndYaw);
            Float32Yaw requestedTargetYaw = nominalSourceEndYaw;
            if (warp.RotationMode == ProgramMotionWarpRotationMode.MatchTargetYaw)
                requestedTargetYaw = new Float32Yaw(targetYaw.Degrees + Float32Scalar.FromSingle(warp.TargetYawOffsetDegrees));
            else if (warp.RotationMode == ProgramMotionWarpRotationMode.FaceTarget)
            {
                Float32Scalar directionX = targetPosition.X - resolvedTargetPosition.X;
                Float32Scalar directionZ = targetPosition.Z - resolvedTargetPosition.Z;
                if (directionX == Float32Scalar.Zero && directionZ == Float32Scalar.Zero)
                    FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.FaceTargetZeroDirection,
                        "FaceTarget desired actor position equals the target planar position.");
                requestedTargetYaw = new Float32Yaw(
                    Float32Angle.FromPlanarDirection(directionX, directionZ).Degrees +
                    Float32Scalar.FromSingle(warp.TargetYawOffsetDegrees));
            }
            Float32Scalar requestedYawCorrection = warp.RotationMode == ProgramMotionWarpRotationMode.Disabled
                ? Float32Scalar.Zero
                : Float32Angle.Delta(nominalSourceEndYaw, requestedTargetYaw);
            Float32Scalar maximumYaw = warp.RotationMode == ProgramMotionWarpRotationMode.Disabled
                ? Float32Scalar.Zero
                : Float32Scalar.FromSingle(warp.MaximumYawCorrectionDegrees);
            if (warp.RotationMode != ProgramMotionWarpRotationMode.Disabled &&
                warp.RotationMethod == ProgramMotionWarpRotationMethod.ConstantRate)
            {
                Float32Scalar maximumRate = Float32Scalar.FromSingle(warp.MaximumYawRateDegreesPerSecond) *
                    Float32Scalar.FromSingle((warp.EndTime - warp.StartTime).ToSingle());
                maximumYaw = Float32Scalar.Min(maximumYaw, maximumRate);
            }
            bool yawExceeded = warp.RotationMode != ProgramMotionWarpRotationMode.Disabled &&
                               Float32Scalar.Abs(requestedYawCorrection) > maximumYaw;
            Float32Scalar effectiveYawCorrection = Float32Scalar.Clamp(requestedYawCorrection, -maximumYaw, maximumYaw);
            resolvedTargetYaw = new Float32Yaw(nominalSourceEndYaw.Degrees + effectiveYawCorrection);
            bool exceeded = positionExceeded || yawExceeded;
            limitResult = exceeded ? ProgramMotionWarpLimitResult.AppliedClamped : ProgramMotionWarpLimitResult.Applied;
            if (exceeded && warp.LimitPolicy == ProgramMotionWarpLimitPolicy.PreserveSource)
                limitResult = ProgramMotionWarpLimitResult.PreservedByLimitPolicy;
        }

        Float32Vector3 ResolveDirectTargetPosition(
            in AbilityTimelineLogicMotionWarp warp,
            Float32Vector3 targetPosition,
            Float32Yaw targetYaw,
            Float32Vector3 startBodyPosition,
            Float32Yaw startBodyYaw,
            Float32Scalar targetY)
        {
            Float32Vector2 offset = new Float32Vector2(
                Float32Scalar.FromSingle(warp.TargetPlanarOffsetX),
                Float32Scalar.FromSingle(warp.TargetPlanarOffsetY));
            Float32Vector3 localOffset = new Float32Vector3(offset.X, Float32Scalar.Zero, offset.Y);
            Float32Vector3 worldOffset;
            switch (warp.TargetOffsetSpace)
            {
                case ProgramMotionWarpTargetOffsetSpace.TargetLocal:
                    worldOffset = Float32Angle.RotatePlanar(localOffset, targetYaw);
                    break;
                case ProgramMotionWarpTargetOffsetSpace.ApproachDirection:
                {
                    Float32Vector2 outward = new Float32Vector2(
                        startBodyPosition.X - targetPosition.X,
                        startBodyPosition.Z - targetPosition.Z);
                    if (outward.SqrMagnitude == Float32Scalar.Zero)
                        FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.ApproachDirectionZero,
                            "ApproachDirection requires distinct target and warp-start planar positions.");
                    Float32Vector2 forward = outward.Normalized;
                    Float32Vector2 right = new Float32Vector2(forward.Y, -forward.X);
                    worldOffset = new Float32Vector3(
                        right.X * offset.X + forward.X * offset.Y,
                        Float32Scalar.Zero,
                        right.Y * offset.X + forward.Y * offset.Y);
                    break;
                }
                case ProgramMotionWarpTargetOffsetSpace.ActorStartLocal:
                    worldOffset = Float32Angle.RotatePlanar(localOffset, startBodyYaw);
                    break;
                case ProgramMotionWarpTargetOffsetSpace.World:
                    worldOffset = localOffset;
                    break;
                default:
                    FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.InvalidState,
                        $"Unsupported target offset space '{warp.TargetOffsetSpace}'.");
                    return default;
            }
            return new Float32Vector3(
                targetPosition.X + worldOffset.X,
                targetY,
                targetPosition.Z + worldOffset.Z);
        }

        void EvaluateDirectPose(
            in AbilityTimelineLogicMotionWarp warp,
            FixedScalar sourcePositionX,
            FixedScalar sourcePositionY,
            FixedScalar sourcePositionZ,
            FixedScalar sourceYawDegrees,
            FixedScalar positionProgress,
            FixedScalar yawProgress,
            FixedScalar sampleTime,
            Float32Vector3 startBodyPosition,
            Float32Yaw startBodyYaw,
            Float32Vector3 sourceWindowStartPosition,
            Float32Scalar sourceWindowStartYaw,
            Float32Vector3 resolvedTargetPosition,
            Float32Yaw resolvedTargetYaw,
            out Float32Vector3 warpedPosition,
            out Float32Yaw warpedYaw)
        {
            warpedPosition = default;
            warpedYaw = default;
            Float32Vector3 sourceRelative = new Float32Vector3(
                Float32Scalar.FromSingle(sourcePositionX.ToSingle()),
                Float32Scalar.FromSingle(sourcePositionY.ToSingle()),
                Float32Scalar.FromSingle(sourcePositionZ.ToSingle())) - sourceWindowStartPosition;
            Float32Vector3 sourceEndRelative = new Float32Vector3(
                Float32Scalar.FromSingle(warp.SourceEndPositionX.ToSingle()),
                Float32Scalar.FromSingle(warp.SourceEndPositionY.ToSingle()),
                Float32Scalar.FromSingle(warp.SourceEndPositionZ.ToSingle())) - sourceWindowStartPosition;
            Float32Scalar sourceYawRelative = Float32Scalar.FromSingle(sourceYawDegrees.ToSingle()) - sourceWindowStartYaw;
            Float32Scalar sourceEndYawRelative = Float32Scalar.FromSingle(warp.SourceEndYawDegrees.ToSingle()) - sourceWindowStartYaw;
            Float32Yaw nominalCurrentYaw = new Float32Yaw(startBodyYaw.Degrees + sourceYawRelative);
            Float32Yaw nominalEndYaw = new Float32Yaw(startBodyYaw.Degrees + sourceEndYawRelative);
            Float32Scalar finalYawCorrection = warp.RotationMode == ProgramMotionWarpRotationMode.Disabled
                ? Float32Scalar.Zero
                : Float32Angle.Delta(nominalEndYaw, resolvedTargetYaw);
            Float32Scalar currentYawCorrection;
            switch (warp.RotationMode == ProgramMotionWarpRotationMode.Disabled
                ? ProgramMotionWarpRotationMethod.ProgressCurve
                : warp.RotationMethod)
            {
                case ProgramMotionWarpRotationMethod.ProgressCurve:
                    currentYawCorrection = finalYawCorrection * Float32Scalar.FromSingle(yawProgress.ToSingle());
                    break;
                case ProgramMotionWarpRotationMethod.ConstantRate:
                {
                    Float32Scalar elapsed = Float32Scalar.Max(
                        Float32Scalar.Zero,
                        Float32Scalar.FromSingle((sampleTime - warp.StartTime).ToSingle()));
                    Float32Scalar maximum = Float32Scalar.FromSingle(warp.MaximumYawRateDegreesPerSecond) * elapsed;
                    currentYawCorrection = Float32Scalar.Clamp(finalYawCorrection, -maximum, maximum);
                    break;
                }
                case ProgramMotionWarpRotationMethod.ScaleSourceYaw:
                {
                    if (Float32Scalar.Abs(sourceEndYawRelative) <= Float32Scalar.FromSingle(0.000001f))
                        FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.ScaleSourceYawZero,
                            "ScaleSourceYaw requires non-zero source window yaw.");
                    Float32Scalar targetYawRelative = Float32Angle.Delta(startBodyYaw, resolvedTargetYaw);
                    currentYawCorrection = sourceYawRelative * (targetYawRelative / sourceEndYawRelative) - sourceYawRelative;
                    break;
                }
                default:
                    FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.InvalidState,
                        $"Unsupported rotation method '{warp.RotationMethod}'.");
                    return;
            }
            warpedYaw = new Float32Yaw(nominalCurrentYaw.Degrees + currentYawCorrection);
            Float32Vector3 rotatedSource = Float32Angle.RotatePlanar(
                sourceRelative,
                new Float32Yaw(startBodyYaw.Degrees + currentYawCorrection));
            Float32Vector3 rotatedSourceEnd = Float32Angle.RotatePlanar(
                sourceEndRelative,
                new Float32Yaw(startBodyYaw.Degrees + finalYawCorrection));
            Float32Vector3 targetRelative = resolvedTargetPosition - startBodyPosition;
            Float32Vector3 warpedRelative;
            switch (warp.TranslationMode)
            {
                case ProgramMotionWarpTranslationMode.Disabled:
                    warpedRelative = rotatedSource;
                    break;
                case ProgramMotionWarpTranslationMode.ScaleToTarget:
                    warpedRelative = ScalePlanarToTarget(rotatedSource, rotatedSourceEnd, targetRelative);
                    break;
                case ProgramMotionWarpTranslationMode.SkewToTarget:
                {
                    Float32Vector3 endpointCorrection = Planar(targetRelative - rotatedSourceEnd);
                    warpedRelative = rotatedSource + endpointCorrection * Float32Scalar.FromSingle(positionProgress.ToSingle());
                    break;
                }
                case ProgramMotionWarpTranslationMode.LinearToTarget:
                    warpedRelative = new Float32Vector3(
                        targetRelative.X * Float32Scalar.FromSingle(positionProgress.ToSingle()),
                        sourceRelative.Y,
                        targetRelative.Z * Float32Scalar.FromSingle(positionProgress.ToSingle()));
                    break;
                default:
                    FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.InvalidState,
                        $"Unsupported translation mode '{warp.TranslationMode}'.");
                    return;
            }
            warpedPosition = startBodyPosition + new Float32Vector3(
                warpedRelative.X,
                sourceRelative.Y,
                warpedRelative.Z);
        }

        static Float32Vector3 ScalePlanarToTarget(
            Float32Vector3 value,
            Float32Vector3 sourceEnd,
            Float32Vector3 targetEnd)
        {
            Float32Scalar denominator = sourceEnd.X * sourceEnd.X + sourceEnd.Z * sourceEnd.Z;
            if (denominator <= Float32Scalar.FromSingle(0.000001f))
                throw new InvalidOperationException("ScaleToTarget requires a non-zero source window planar endpoint.");
            Float32Scalar dot = sourceEnd.X * targetEnd.X + sourceEnd.Z * targetEnd.Z;
            Float32Scalar cross = sourceEnd.X * targetEnd.Z - sourceEnd.Z * targetEnd.X;
            return new Float32Vector3(
                (dot * value.X - cross * value.Z) / denominator,
                value.Y,
                (cross * value.X + dot * value.Z) / denominator);
        }

        static Float32Vector3 Planar(Float32Vector3 value) =>
            new Float32Vector3(value.X, Float32Scalar.Zero, value.Z);

        static Float32Vector3 ClampMagnitude(Float32Vector3 value, Float32Scalar maximum)
        {
            if (maximum <= Float32Scalar.Zero)
                return Float32Vector3.Zero;
            Float32Scalar magnitude = new Float32Vector2(value.X, value.Z).Magnitude;
            return magnitude > maximum ? value * (maximum / magnitude) : value;
        }
    }

    internal sealed class Float32LocomotionRuntime : Float32OperationModule
    {
        readonly IFloat32ValueInputReader m_Values;
        readonly IFloat32MotionContributionSink m_Motion;
        readonly Float32AbilityExecutionFrame m_Frame;
        readonly LocomotionProfile[] m_Profiles;

        public Float32LocomotionRuntime(
            Float32GameplayAbilityExecutionAccess access,
            IFloat32ValueInputReader values,
            IFloat32MotionContributionSink motion,
            Float32AbilityExecutionFrame frame)
            : base(access)
        {
            m_Values = values ?? throw new ArgumentNullException(nameof(values));
            m_Motion = motion ?? throw new ArgumentNullException(nameof(motion));
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            m_Profiles = BuildProfiles(Access.Layout, Access.Services);
        }

        internal int DurationTicks(OperationHandle operation) => m_Profiles[operation.Value].DurationTicks;

        public void Submit<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation,
            int committedTicks,
            ulong generation)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            using Float32ValueInputLease inputs = m_Values.ReadInputs(cursor, operation);
            AbilityStateValue input = inputs.FindByKind(ProgramStateValueKind.Vector2);
            if (input.Kind != ProgramStateValueKind.Vector2)
                throw new InvalidOperationException($"Locomotion operation '{SourcePath(operation)}' has no Vector2 input.");
            Float32Vector2 move = input.Vector2;
            if (move.SqrMagnitude > Float32Scalar.One)
                move = move.Normalized;
            var movementPlaybackClock = new CommittedMovementPlaybackClock(
                SourcePath(operation),
                generation,
                m_Frame.Tick,
                committedTicks,
                m_Ability.TickRate);
            Float32Scalar delta = Float32Scalar.One / Float32Scalar.FromInt64(m_Ability.TickRate);
            ref readonly LocomotionProfile profile = ref m_Profiles[operation.Handle.Value];
            Float32Scalar maxYaw = profile.TurnSpeed * delta;
            Float32Vector3 displacement = ResolveDisplacement(in profile, move, delta, committedTicks - 1);
            Float32Scalar yaw = Float32Scalar.Zero;
            if (move != Float32Vector2.Zero && maxYaw > Float32Scalar.Zero)
            {
                Float32Yaw desired = Float32Angle.FromPlanarDirection(move);
                yaw = Float32Scalar.Clamp(Float32Angle.Delta(m_Frame.BodyFacts.Yaw, desired), -maxYaw, maxYaw);
            }
            CommittedLocomotionPlanarMotionTimeline locomotionTimeline = ResolveMotionTimeline(
                cursor,
                operation,
                move,
                displacement,
                yaw,
                profile.TurnSpeed,
                delta,
                generation);
            m_Motion.Submit(new SimulationMotionContribution(
                SimulationExecutionSource.FromSkillOperation(operation.Handle, SourcePath(operation)),
                Access.Services.SkillOperationIdentity(operation.Handle),
                m_Ability.AbilityId,
                generation,
                displacement,
                yaw,
                move,
				SimulationMotionContributionSpace.World,
				Float32Scalar.One,
				0,
				SimulationMotionChannel.Locomotion,
				SimulationMotionBlendMode.Override,
				false,
                movementPlaybackClock,
                locomotionTimeline));
        }

        CommittedLocomotionPlanarMotionTimeline ResolveMotionTimeline<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation,
            Float32Vector2 move,
            Float32Vector3 displacement,
            Float32Scalar yawDegrees,
            Float32Scalar maximumYawVelocityDegreesPerSecond,
            Float32Scalar delta,
            ulong generation)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            ref readonly LocomotionProfile profile = ref m_Profiles[operation.Handle.Value];
            int durationTicks = profile.DurationTicks;
            string continuationOwner = string.Empty;
            Float32Vector2 continuationVelocity = Float32Vector2.Zero;
            if ((LocomotionInputMotionExecutionMode)operation.Integer0 == LocomotionInputMotionExecutionMode.Timed)
            {
                ProgramControlFlowEdge transition = cursor.PredictCurrentStateRootCompletionTransition();
                OperationExecutionDescriptor continuation = transition == null
                    ? null
                    : Access.Topology.StateSingleLocomotion(transition.Target);
                if (continuation != null &&
                    continuation.Code == SimulationOperationCode.LocomotionInputMotion &&
                    (LocomotionInputMotionExecutionMode)continuation.Integer0 == LocomotionInputMotionExecutionMode.Continuous &&
                    (LocomotionInputMotionDisplacementMode)continuation.Integer1 == LocomotionInputMotionDisplacementMode.ConstantSpeed)
                {
                    ProgramConstant speed = Access.Layout.FindNamedConstant(continuation.Handle, OperationNamedConstant.MoveSpeed);
                    if (speed == null || speed.Kind != ProgramConstantKind.Scalar || speed.Scalar < Float32Scalar.Zero)
                        throw new InvalidOperationException($"Locomotion continuation '{Access.Services.SourcePath(continuation.Handle)}' has invalid Move Speed.");
                    continuationOwner = Access.Services.SourcePath(continuation.Handle);
                    continuationVelocity = move * speed.Scalar;
                }
            }
            Float32Vector2 currentVelocity = delta > Float32Scalar.Zero
                ? new Float32Vector2(displacement.X / delta, displacement.Z / delta)
                : Float32Vector2.Zero;
            return new CommittedLocomotionPlanarMotionTimeline(
                SourcePath(operation),
                generation,
                m_Frame.Tick,
                m_Ability.TickRate,
                currentVelocity.X.ToSingle(),
                currentVelocity.Y.ToSingle(),
                (yawDegrees / delta).ToSingle(),
                maximumYawVelocityDegreesPerSecond.ToSingle(),
                durationTicks,
                continuationOwner,
                continuationVelocity.X.ToSingle(),
                continuationVelocity.Y.ToSingle());
        }

        Float32Vector3 ResolveDisplacement(
            in LocomotionProfile profile,
            Float32Vector2 move,
            Float32Scalar delta,
            int elapsedTicks)
        {
            if (profile.DisplacementMode == LocomotionInputMotionDisplacementMode.ConstantSpeed)
            {
                return new Float32Vector3(
                    move.X * profile.MoveSpeed * delta,
                    Float32Scalar.Zero,
                    move.Y * profile.MoveSpeed * delta);
            }
            if (move == Float32Vector2.Zero)
                return Float32Vector3.Zero;

            Float32Scalar tickRate = Float32Scalar.FromInt64(m_Ability.TickRate);
            Float32Scalar fromTime = Float32Scalar.FromInt64(elapsedTicks) / tickRate;
            Float32Scalar toTime = Float32Scalar.FromInt64(checked(elapsedTicks + 1)) / tickRate;
            bool looping = profile.ExecutionMode == LocomotionInputMotionExecutionMode.Continuous;
            Float32GameplayAbilityCurve xCurve = profile.XCurve;
            Float32GameplayAbilityCurve zCurve = profile.ZCurve;
            Float32Scalar duration = profile.CurveDuration;
            Float32Scalar xCycleTotal = looping ? xCurve.Evaluate(duration, Float32Scalar.Zero) : Float32Scalar.Zero;
            Float32Scalar zCycleTotal = looping ? zCurve.Evaluate(duration, Float32Scalar.Zero) : Float32Scalar.Zero;
            Float32Scalar localX = SampleCumulative(xCurve, toTime, duration, looping, xCycleTotal) -
                SampleCumulative(xCurve, fromTime, duration, looping, xCycleTotal);
            Float32Scalar localZ = SampleCumulative(zCurve, toTime, duration, looping, zCycleTotal) -
                SampleCumulative(zCurve, fromTime, duration, looping, zCycleTotal);

            Float32Vector2 forward = move.Normalized;
            Float32Vector2 right = new Float32Vector2(forward.Y, -forward.X);
            return new Float32Vector3(
                right.X * localX + forward.X * localZ,
                Float32Scalar.Zero,
                right.Y * localX + forward.Y * localZ);
        }

        static Float32Scalar SampleCumulative(
            Float32GameplayAbilityCurve curve,
            Float32Scalar time,
            Float32Scalar duration,
            bool looping,
            Float32Scalar cycleTotal)
        {
            if (!looping)
                return curve.Evaluate(Float32Scalar.Clamp(time, Float32Scalar.Zero, duration), Float32Scalar.Zero);
            int cycle = (int)Math.Floor((time / duration).ToDouble());
            Float32Scalar localTime = time - duration * Float32Scalar.FromInt64(cycle);
            return cycleTotal * Float32Scalar.FromInt64(cycle) + curve.Evaluate(localTime, Float32Scalar.Zero);
        }

        static LocomotionProfile[] BuildProfiles(
            GameplayAbilityExecutionLayout layout,
            Float32GameplayAbilityExecutionServices services)
        {
            var profiles = new LocomotionProfile[layout.Operations.Count];
            for (int i = 0; i < layout.Operations.Count; i++)
            {
                SimulationOperation operation = layout.Operations[i];
                if (operation.Code != SimulationOperationCode.LocomotionInputMotion)
                    continue;

                ProgramConstant turn = layout.FindNamedConstant(operation.Handle, OperationNamedConstant.TurnSpeedDegrees);
                if (turn == null || turn.Kind != ProgramConstantKind.Scalar)
                    throw new InvalidOperationException($"Locomotion operation '{services.SourcePath(operation.Handle)}' has invalid turn speed.");

                var executionMode = (LocomotionInputMotionExecutionMode)operation.Integer0;
                var displacementMode = (LocomotionInputMotionDisplacementMode)operation.Integer1;
                Float32Scalar moveSpeed = Float32Scalar.Zero;
                Float32Scalar curveDuration = Float32Scalar.Zero;
                Float32GameplayAbilityCurve xCurve = null;
                Float32GameplayAbilityCurve zCurve = null;
                int durationTicks = 0;
                if (executionMode == LocomotionInputMotionExecutionMode.Timed)
                {
                    ProgramConstant duration = layout.FindNamedConstant(operation.Handle, OperationNamedConstant.DurationSeconds);
                    if (duration == null || duration.Kind != ProgramConstantKind.Scalar || duration.Scalar <= Float32Scalar.Zero)
                        throw new InvalidOperationException($"Locomotion operation '{services.SourcePath(operation.Handle)}' has invalid duration.");
                    durationTicks = checked((int)Math.Ceiling(duration.Scalar.ToDouble() * layout.TickRate));
                }

                if (displacementMode == LocomotionInputMotionDisplacementMode.ConstantSpeed)
                {
                    ProgramConstant speed = layout.FindNamedConstant(operation.Handle, OperationNamedConstant.MoveSpeed);
                    if (speed == null || speed.Kind != ProgramConstantKind.Scalar)
                        throw new InvalidOperationException($"Locomotion operation '{services.SourcePath(operation.Handle)}' has no Move Speed.");
                    moveSpeed = speed.Scalar;
                }
                else if (displacementMode == LocomotionInputMotionDisplacementMode.ActionMotionCurve)
                {
                    ProgramConstant xConstant = layout.FindNamedConstant(operation.Handle, OperationNamedConstant.ActionMotionPositionX);
                    ProgramConstant zConstant = layout.FindNamedConstant(operation.Handle, OperationNamedConstant.ActionMotionPositionZ);
                    ProgramConstant durationConstant = layout.FindNamedConstant(operation.Handle, OperationNamedConstant.ActionMotionDuration);
                    if (xConstant == null || xConstant.Kind != ProgramConstantKind.Bytes ||
                        zConstant == null || zConstant.Kind != ProgramConstantKind.Bytes ||
                        durationConstant == null || durationConstant.Kind != ProgramConstantKind.Scalar ||
                        durationConstant.Scalar <= Float32Scalar.Zero)
                        throw new InvalidOperationException($"Locomotion operation '{services.SourcePath(operation.Handle)}' has invalid Action Motion Curve constants.");
                    xCurve = services.RequireTimelineCurve(xConstant, xConstant.Identity);
                    zCurve = services.RequireTimelineCurve(zConstant, zConstant.Identity);
                    curveDuration = durationConstant.Scalar;
                }
                else
                {
                    throw new InvalidOperationException($"Locomotion operation '{services.SourcePath(operation.Handle)}' has invalid displacement mode '{operation.Integer1}'.");
                }

                profiles[i] = new LocomotionProfile(
                    executionMode,
                    displacementMode,
                    turn.Scalar,
                    moveSpeed,
                    curveDuration,
                    durationTicks,
                    xCurve,
                    zCurve);
            }

            return profiles;
        }

        readonly struct LocomotionProfile
        {
            public LocomotionProfile(
                LocomotionInputMotionExecutionMode executionMode,
                LocomotionInputMotionDisplacementMode displacementMode,
                Float32Scalar turnSpeed,
                Float32Scalar moveSpeed,
                Float32Scalar curveDuration,
                int durationTicks,
                Float32GameplayAbilityCurve xCurve,
                Float32GameplayAbilityCurve zCurve)
            {
                ExecutionMode = executionMode;
                DisplacementMode = displacementMode;
                TurnSpeed = turnSpeed;
                MoveSpeed = moveSpeed;
                CurveDuration = curveDuration;
                DurationTicks = durationTicks;
                XCurve = xCurve;
                ZCurve = zCurve;
            }

            public LocomotionInputMotionExecutionMode ExecutionMode { get; }
            public LocomotionInputMotionDisplacementMode DisplacementMode { get; }
            public Float32Scalar TurnSpeed { get; }
            public Float32Scalar MoveSpeed { get; }
            public Float32Scalar CurveDuration { get; }
            public int DurationTicks { get; }
            public Float32GameplayAbilityCurve XCurve { get; }
            public Float32GameplayAbilityCurve ZCurve { get; }
        }
    }
}

