using MotionWarpState = ThirdPersonSimulation.MotionWarpState<ThirdPersonSimulation.Fixed.FixedScalar, ThirdPersonSimulation.Fixed.FixedVector3, ThirdPersonSimulation.Fixed.FixedYaw, ThirdPersonSimulation.Fixed.FixedActionInstanceReference, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Fixed.FixedScalar>, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Fixed.FixedVector3>, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Fixed.FixedYaw>>;
using SimulationMotionContribution = ThirdPersonSimulation.SimulationMotionContribution<ThirdPersonSimulation.Fixed.FixedScalar, ThirdPersonSimulation.Fixed.FixedVector2, ThirdPersonSimulation.Fixed.FixedVector3, ThirdPersonSimulation.Fixed.FixedSimulationMath>;
using ResolvedMotionChannel = ThirdPersonSimulation.ResolvedMotionChannel<ThirdPersonSimulation.Fixed.FixedScalar, ThirdPersonSimulation.Fixed.FixedVector2, ThirdPersonSimulation.Fixed.FixedVector3, ThirdPersonSimulation.Fixed.FixedSimulationMath>;
using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed class FixedMotionAccumulator : FixedOperationModule,
        IFixedMotionContributionSink
    {
        readonly FixedAbilityExecutionFrame m_Frame;
        readonly FixedAbilityExecutionWorkspace m_Workspace;
        readonly List<AbilityTimelineLogicMotionWarp> m_TimelineMotionWarps;
        readonly FixedMotionWarpTarget m_MotionWarp;

        public FixedMotionAccumulator(
            FixedGameplayAbilityExecutionAccess access,
            FixedAbilityExecutionFrame frame,
            FixedAbilityExecutionWorkspace workspace,
            List<AbilityTimelineLogicMotionWarp> timelineMotionWarps,
            FixedActionStateStore actions)
            : base(access)
        {
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            m_Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            m_TimelineMotionWarps = timelineMotionWarps ?? throw new ArgumentNullException(nameof(timelineMotionWarps));
            m_MotionWarp = new FixedMotionWarpTarget(access, frame, actions);
        }

        public void Submit(SimulationMotionContribution contribution)
        {
            if (!contribution.CanResolve)
                return;
            m_Workspace.SubmitMotionContribution(contribution);
            if (m_Frame.Trace.CaptureMotion)
            {
                var detail = new SimulationMotionTraceDetail(
                    SimulationMotionTraceDetailKind.Contribution,
                    contribution.Displacement.X.Raw, contribution.Displacement.Y.Raw, contribution.Displacement.Z.Raw,
                    contribution.YawDegrees.Raw, contribution.MovementPlaybackClock,
                    channel: (int)contribution.Channel, blendMode: (int)contribution.BlendMode,
                    priority: contribution.Priority, weightRaw: contribution.Weight.Raw,
                    claimsLowerChannels: contribution.ClaimsLowerChannels);
                m_Frame.Trace.Add(
                    contribution.Source,
                    "motion_contribution",
                    SimulationTraceSeverity.Detail,
                    string.Empty,
                    contribution.SourceGeneration,
                    motionDetail: in detail);
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

    internal sealed class FixedMotionWarpTarget : FixedOperationModule,
        IAbilityTimelineMotionWarpTarget<ResolvedMotionChannel>
    {
        readonly FixedAbilityExecutionFrame m_Frame;
        readonly FixedActionStateStore m_Actions;

        public FixedMotionWarpTarget(
            FixedGameplayAbilityExecutionAccess access,
            FixedAbilityExecutionFrame frame,
            FixedActionStateStore actions)
            : base(access)
        {
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            m_Actions = actions ?? throw new ArgumentNullException(nameof(actions));
        }

        public void CaptureWindowEntry(in AbilityTimelineLogicMotionWarp warp)
        {
            MotionWarpState state = m_Frame.SkillState.GetMotionWarpState(warp.StateOperation);
            if (state.Active && state.PlaybackGeneration == warp.PlaybackGeneration && state.Cycle == warp.Cycle)
                return;
            m_Actions.TryGetEvaluatedInstance(warp.ActionInstanceId, out FixedActionInstanceState action);
            MotionWarpWindowDecision decision = !warp.Enabled ? MotionWarpWindowDecision.Disabled
                : !warp.HasTarget && warp.RotationMethod != ProgramMotionWarpRotationMethod.TargetResponse
                    ? MotionWarpWindowDecision.NoTarget : MotionWarpWindowDecision.Accepted;
            var targetPosition = new FixedVector3(warp.Target.X, warp.Target.Y, warp.Target.Z);
            var targetYaw = new FixedYaw(warp.Target.Yaw);
            m_Frame.SkillState.SetMotionWarpState(warp.StateOperation,
                new MotionWarpState(true, false, warp.PlaybackGeneration, warp.Cycle,
                    FixedActionInstanceReference.FromInstance(action), default, default, default, default,
                    default, default, default, default, default, default, default, warp.Source.Operation,
                    decision, warp.HasTarget, targetPosition, targetYaw));
            Trace(warp, decision == MotionWarpWindowDecision.Disabled ? MotionModifierDiagnosticCode.Disabled :
                decision == MotionWarpWindowDecision.NoTarget ? MotionModifierDiagnosticCode.NoTargetByOptionalPolicy :
                MotionModifierDiagnosticCode.WindowEntered);
        }

        public void ApplyTimelineMotionWarp(in AbilityTimelineLogicMotionWarp warp, ref ResolvedMotionChannel channel)
        {
            MotionWarpState storedState = m_Frame.SkillState.GetMotionWarpState(warp.StateOperation);
            if (storedState.Decision != MotionWarpWindowDecision.Accepted)
                return;
            if (warp.RotationMethod == ProgramMotionWarpRotationMethod.TargetResponse)
            {
                ApplyTargetResponse(warp, storedState, ref channel);
                Trace(warp, MotionModifierDiagnosticCode.Applied);
                return;
            }
            FixedVector3 rawSourceDelta = FixedAngle.RotatePlanar(
                new FixedVector3(
                    (warp.CurrentPositionX - warp.PreviousPositionX),
                    (warp.CurrentPositionY - warp.PreviousPositionY),
                    (warp.CurrentPositionZ - warp.PreviousPositionZ)),
                m_Frame.BodyFacts.Yaw);
            FixedScalar rawSourceYawDelta = (warp.CurrentYawDegrees - warp.PreviousYawDegrees);
            FixedVector3 startBodyPosition;
            FixedYaw startBodyYaw;
            FixedVector3 sourceWindowStartPosition;
            FixedScalar sourceWindowStartYaw;
            FixedVector3 resolvedTargetPosition;
            FixedYaw resolvedTargetYaw;
            ProgramMotionWarpLimitResult limitResult;
            FixedVector3 previousWarpedPosition;
            FixedYaw previousWarpedYaw;
            FixedScalar previousPositionProgress;
            FixedScalar previousYawProgress;
            if (!storedState.Initialized)
            {
                ResolveDirectTarget(warp, storedState.CapturedTargetPosition, storedState.CapturedTargetYaw,
                    m_Frame.BodyFacts.Position + channel.MotionCorrectionDisplacement +
                        FixedAngle.RotatePlanar(new FixedVector3(warp.SourcePrefix.X, warp.SourcePrefix.Y, warp.SourcePrefix.Z), m_Frame.BodyFacts.Yaw),
                    new FixedYaw(m_Frame.BodyFacts.Yaw.Degrees + channel.MotionCorrectionYawDegrees + warp.SourcePrefix.Yaw),
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
                previousPositionProgress = warp.PreviousPositionProgress;
                previousYawProgress = warp.PreviousYawProgress;
                storedState = new MotionWarpState(
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
            FixedScalar positionProgress = warp.CurrentPositionProgress;
            FixedScalar yawProgress = warp.CurrentYawProgress;
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
                out FixedVector3 currentWarpedPosition,
                out FixedYaw currentWarpedYaw);
            channel.ApplyMotionWarp(rawSourceDelta, rawSourceYawDelta,
                currentWarpedPosition - previousWarpedPosition,
                FixedAngle.Delta(previousWarpedYaw, currentWarpedYaw));
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
            MotionWarpState state,
            ref ResolvedMotionChannel channel)
        {
            if (warp.TranslationMode != ProgramMotionWarpTranslationMode.Disabled ||
                warp.YawResponse < ThirdPersonSimulation.Fixed.FixedScalar.Zero ||
                warp.InputYawResponse < ThirdPersonSimulation.Fixed.FixedScalar.Zero)
                FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.InvalidState,
                    "Target response requires non-negative response and no translation correction.");
            FixedYaw currentYaw = new FixedYaw(m_Frame.BodyFacts.Yaw.Degrees + channel.MotionCorrectionYawDegrees);
            FixedScalar yawDelta = FixedScalar.Zero;
            FixedYaw targetYaw = currentYaw;
            FixedScalar response = warp.YawResponse;
            bool useTarget = state.HasTarget && response > FixedScalar.Zero;
            if (useTarget)
            {
                targetYaw = state.CapturedTargetYaw;
                if (warp.RotationMode == ProgramMotionWarpRotationMode.FaceTarget)
                {
                    FixedVector3 direction = state.CapturedTargetPosition -
                        (m_Frame.BodyFacts.Position + channel.MotionCorrectionDisplacement);
                    targetYaw = direction.X != FixedScalar.Zero || direction.Z != FixedScalar.Zero
                        ? FixedAngle.FromPlanarDirection(direction.X, direction.Z)
                        : currentYaw;
                }
            }
            else
            {
                response = warp.InputYawResponse;
                if (response > FixedScalar.Zero)
                {
                    FixedVector2 direction = new FixedVector2(warp.InputDirectionX, warp.InputDirectionZ);
                    if (direction != FixedVector2.Zero)
                        targetYaw = FixedAngle.FromPlanarDirection(direction.X, direction.Y);
                }
            }
            FixedScalar delta = warp.CurrentTime - warp.PreviousTime;
            FixedScalar amount = FixedScalar.Clamp(response * delta, FixedScalar.Zero, FixedScalar.One);
            FixedYaw desired = new FixedYaw(targetYaw.Degrees + FixedScalar.FromSingle(warp.TargetYawOffsetDegrees));
            FixedScalar maximum = FixedScalar.FromSingle(warp.MaximumYawCorrectionDegrees);
            yawDelta = FixedScalar.Clamp(FixedAngle.Delta(currentYaw, desired), -maximum, maximum) * amount;
            var localDelta = new FixedVector3(
                (warp.CurrentPositionX - warp.PreviousPositionX),
                (warp.CurrentPositionY - warp.PreviousPositionY),
                (warp.CurrentPositionZ - warp.PreviousPositionZ));
            FixedVector3 previousDirectionDelta = FixedAngle.RotatePlanar(localDelta, m_Frame.BodyFacts.Yaw);
            FixedVector3 nextDirectionDelta = FixedAngle.RotatePlanar(localDelta, new FixedYaw(currentYaw.Degrees + yawDelta));
            channel.ApplyMotionWarp(
                previousDirectionDelta,
                warp.CurrentYawDegrees - warp.PreviousYawDegrees,
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
            FixedVector3 targetPosition,
            FixedYaw targetYaw,
            FixedVector3 bodyPosition,
            FixedYaw bodyYaw,
            out FixedVector3 startBodyPosition,
            out FixedYaw startBodyYaw,
            out FixedVector3 sourceWindowStartPosition,
            out FixedScalar sourceWindowStartYaw,
            out FixedVector3 resolvedTargetPosition,
            out FixedYaw resolvedTargetYaw,
            out ProgramMotionWarpLimitResult limitResult)
        {
            startBodyPosition = default;
            startBodyYaw = default;
            sourceWindowStartPosition = default;
            sourceWindowStartYaw = default;
            resolvedTargetPosition = default;
            resolvedTargetYaw = default;
            limitResult = default;
            startBodyPosition = bodyPosition;
            startBodyYaw = bodyYaw;
            sourceWindowStartPosition = new FixedVector3(
                warp.SourceStartPositionX,
                warp.SourceStartPositionY,
                warp.SourceStartPositionZ);
            sourceWindowStartYaw = warp.SourceStartYawDegrees;
            FixedVector3 sourceEnd = new FixedVector3(
                (warp.SourceEndPositionX - warp.SourceStartPositionX),
                (warp.SourceEndPositionY - warp.SourceStartPositionY),
                (warp.SourceEndPositionZ - warp.SourceStartPositionZ));
            FixedScalar sourceEndYaw = (warp.SourceEndYawDegrees - warp.SourceStartYawDegrees);
            FixedVector3 nominalSourceEndOffset = FixedAngle.RotatePlanar(sourceEnd, startBodyYaw);
            FixedVector3 nominalSourceEnd = startBodyPosition + nominalSourceEndOffset;
            FixedVector3 requestedTargetPosition = warp.TranslationMode == ProgramMotionWarpTranslationMode.Disabled
                ? nominalSourceEnd
                : ResolveDirectTargetPosition(warp, targetPosition, targetYaw, startBodyPosition, startBodyYaw, nominalSourceEnd.Y);
            FixedVector3 requestedPositionCorrection = new FixedVector3(
                requestedTargetPosition.X - nominalSourceEnd.X,
                FixedScalar.Zero,
                requestedTargetPosition.Z - nominalSourceEnd.Z);
            FixedScalar maximumPosition = warp.TranslationMode == ProgramMotionWarpTranslationMode.Disabled
                ? FixedScalar.Zero
                : FixedScalar.FromSingle(warp.MaximumPositionCorrection);
            bool positionExceeded = warp.TranslationMode != ProgramMotionWarpTranslationMode.Disabled &&
                                    new FixedVector2(requestedPositionCorrection.X, requestedPositionCorrection.Z).Magnitude > maximumPosition;
            FixedVector3 effectivePositionCorrection = positionExceeded
                ? ClampMagnitude(requestedPositionCorrection, maximumPosition)
                : requestedPositionCorrection;
            resolvedTargetPosition = nominalSourceEnd + effectivePositionCorrection;
            FixedYaw nominalSourceEndYaw = new FixedYaw(startBodyYaw.Degrees + sourceEndYaw);
            FixedYaw requestedTargetYaw = nominalSourceEndYaw;
            if (warp.RotationMode == ProgramMotionWarpRotationMode.MatchTargetYaw)
                requestedTargetYaw = new FixedYaw(targetYaw.Degrees + FixedScalar.FromSingle(warp.TargetYawOffsetDegrees));
            else if (warp.RotationMode == ProgramMotionWarpRotationMode.FaceTarget)
            {
                FixedScalar directionX = targetPosition.X - resolvedTargetPosition.X;
                FixedScalar directionZ = targetPosition.Z - resolvedTargetPosition.Z;
                if (directionX == FixedScalar.Zero && directionZ == FixedScalar.Zero)
                    FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.FaceTargetZeroDirection,
                        "FaceTarget desired actor position equals the target planar position.");
                requestedTargetYaw = new FixedYaw(
                    FixedAngle.FromPlanarDirection(directionX, directionZ).Degrees +
                    FixedScalar.FromSingle(warp.TargetYawOffsetDegrees));
            }
            FixedScalar requestedYawCorrection = warp.RotationMode == ProgramMotionWarpRotationMode.Disabled
                ? FixedScalar.Zero
                : FixedAngle.Delta(nominalSourceEndYaw, requestedTargetYaw);
            FixedScalar maximumYaw = warp.RotationMode == ProgramMotionWarpRotationMode.Disabled
                ? FixedScalar.Zero
                : FixedScalar.FromSingle(warp.MaximumYawCorrectionDegrees);
            if (warp.RotationMode != ProgramMotionWarpRotationMode.Disabled &&
                warp.RotationMethod == ProgramMotionWarpRotationMethod.ConstantRate)
            {
                FixedScalar maximumRate = FixedScalar.FromSingle(warp.MaximumYawRateDegreesPerSecond) *
                    (warp.EndTime - warp.StartTime);
                maximumYaw = FixedScalar.Min(maximumYaw, maximumRate);
            }
            bool yawExceeded = warp.RotationMode != ProgramMotionWarpRotationMode.Disabled &&
                               FixedScalar.Abs(requestedYawCorrection) > maximumYaw;
            FixedScalar effectiveYawCorrection = FixedScalar.Clamp(requestedYawCorrection, -maximumYaw, maximumYaw);
            resolvedTargetYaw = new FixedYaw(nominalSourceEndYaw.Degrees + effectiveYawCorrection);
            bool exceeded = positionExceeded || yawExceeded;
            limitResult = exceeded ? ProgramMotionWarpLimitResult.AppliedClamped : ProgramMotionWarpLimitResult.Applied;
            if (exceeded && warp.LimitPolicy == ProgramMotionWarpLimitPolicy.PreserveSource)
                limitResult = ProgramMotionWarpLimitResult.PreservedByLimitPolicy;
        }

        FixedVector3 ResolveDirectTargetPosition(
            in AbilityTimelineLogicMotionWarp warp,
            FixedVector3 targetPosition,
            FixedYaw targetYaw,
            FixedVector3 startBodyPosition,
            FixedYaw startBodyYaw,
            FixedScalar targetY)
        {
            FixedVector2 offset = new FixedVector2(
                FixedScalar.FromSingle(warp.TargetPlanarOffsetX),
                FixedScalar.FromSingle(warp.TargetPlanarOffsetY));
            FixedVector3 localOffset = new FixedVector3(offset.X, FixedScalar.Zero, offset.Y);
            FixedVector3 worldOffset;
            switch (warp.TargetOffsetSpace)
            {
                case ProgramMotionWarpTargetOffsetSpace.TargetLocal:
                    worldOffset = FixedAngle.RotatePlanar(localOffset, targetYaw);
                    break;
                case ProgramMotionWarpTargetOffsetSpace.ApproachDirection:
                {
                    FixedVector2 outward = new FixedVector2(
                        startBodyPosition.X - targetPosition.X,
                        startBodyPosition.Z - targetPosition.Z);
                    if (outward.SqrMagnitude == FixedScalar.Zero)
                        FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.ApproachDirectionZero,
                            "ApproachDirection requires distinct target and warp-start planar positions.");
                    FixedVector2 forward = outward.Normalized;
                    FixedVector2 right = new FixedVector2(forward.Y, -forward.X);
                    worldOffset = new FixedVector3(
                        right.X * offset.X + forward.X * offset.Y,
                        FixedScalar.Zero,
                        right.Y * offset.X + forward.Y * offset.Y);
                    break;
                }
                case ProgramMotionWarpTargetOffsetSpace.ActorStartLocal:
                    worldOffset = FixedAngle.RotatePlanar(localOffset, startBodyYaw);
                    break;
                case ProgramMotionWarpTargetOffsetSpace.World:
                    worldOffset = localOffset;
                    break;
                default:
                    FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.InvalidState,
                        $"Unsupported target offset space '{warp.TargetOffsetSpace}'.");
                    return default;
            }
            return new FixedVector3(
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
            FixedVector3 startBodyPosition,
            FixedYaw startBodyYaw,
            FixedVector3 sourceWindowStartPosition,
            FixedScalar sourceWindowStartYaw,
            FixedVector3 resolvedTargetPosition,
            FixedYaw resolvedTargetYaw,
            out FixedVector3 warpedPosition,
            out FixedYaw warpedYaw)
        {
            warpedPosition = default;
            warpedYaw = default;
            FixedVector3 sourceRelative = new FixedVector3(
                sourcePositionX,
                sourcePositionY,
                sourcePositionZ) - sourceWindowStartPosition;
            FixedVector3 sourceEndRelative = new FixedVector3(
                warp.SourceEndPositionX,
                warp.SourceEndPositionY,
                warp.SourceEndPositionZ) - sourceWindowStartPosition;
            FixedScalar sourceYawRelative = sourceYawDegrees - sourceWindowStartYaw;
            FixedScalar sourceEndYawRelative = warp.SourceEndYawDegrees - sourceWindowStartYaw;
            FixedYaw nominalCurrentYaw = new FixedYaw(startBodyYaw.Degrees + sourceYawRelative);
            FixedYaw nominalEndYaw = new FixedYaw(startBodyYaw.Degrees + sourceEndYawRelative);
            FixedScalar finalYawCorrection = warp.RotationMode == ProgramMotionWarpRotationMode.Disabled
                ? FixedScalar.Zero
                : FixedAngle.Delta(nominalEndYaw, resolvedTargetYaw);
            FixedScalar currentYawCorrection;
            switch (warp.RotationMode == ProgramMotionWarpRotationMode.Disabled
                ? ProgramMotionWarpRotationMethod.ProgressCurve
                : warp.RotationMethod)
            {
                case ProgramMotionWarpRotationMethod.ProgressCurve:
                    currentYawCorrection = finalYawCorrection * yawProgress;
                    break;
                case ProgramMotionWarpRotationMethod.ConstantRate:
                {
                    FixedScalar elapsed = FixedScalar.Max(FixedScalar.Zero, sampleTime - warp.StartTime);
                    FixedScalar maximum = FixedScalar.FromSingle(warp.MaximumYawRateDegreesPerSecond) * elapsed;
                    currentYawCorrection = FixedScalar.Clamp(finalYawCorrection, -maximum, maximum);
                    break;
                }
                case ProgramMotionWarpRotationMethod.ScaleSourceYaw:
                {
                    if (FixedScalar.Abs(sourceEndYawRelative) <= FixedScalar.FromRatio(1, 1000000))
                        FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.ScaleSourceYawZero,
                            "ScaleSourceYaw requires non-zero source window yaw.");
                    FixedScalar targetYawRelative = FixedAngle.Delta(startBodyYaw, resolvedTargetYaw);
                    currentYawCorrection = sourceYawRelative * (targetYawRelative / sourceEndYawRelative) - sourceYawRelative;
                    break;
                }
                default:
                    FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.InvalidState,
                        $"Unsupported rotation method '{warp.RotationMethod}'.");
                    return;
            }
            warpedYaw = new FixedYaw(nominalCurrentYaw.Degrees + currentYawCorrection);
            FixedVector3 rotatedSource = FixedAngle.RotatePlanar(
                sourceRelative,
                new FixedYaw(startBodyYaw.Degrees + currentYawCorrection));
            FixedVector3 rotatedSourceEnd = FixedAngle.RotatePlanar(
                sourceEndRelative,
                new FixedYaw(startBodyYaw.Degrees + finalYawCorrection));
            FixedVector3 targetRelative = resolvedTargetPosition - startBodyPosition;
            FixedVector3 warpedRelative;
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
                    FixedVector3 endpointCorrection = Planar(targetRelative - rotatedSourceEnd);
                    warpedRelative = rotatedSource + endpointCorrection * positionProgress;
                    break;
                }
                case ProgramMotionWarpTranslationMode.LinearToTarget:
                    warpedRelative = new FixedVector3(
                        targetRelative.X * positionProgress,
                        sourceRelative.Y,
                        targetRelative.Z * positionProgress);
                    break;
                default:
                    FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.InvalidState,
                        $"Unsupported translation mode '{warp.TranslationMode}'.");
                    return;
            }
            warpedPosition = startBodyPosition + new FixedVector3(
                warpedRelative.X,
                sourceRelative.Y,
                warpedRelative.Z);
        }

        static FixedVector3 ScalePlanarToTarget(
            FixedVector3 value,
            FixedVector3 sourceEnd,
            FixedVector3 targetEnd)
        {
            FixedScalar denominator = sourceEnd.X * sourceEnd.X + sourceEnd.Z * sourceEnd.Z;
            if (denominator <= FixedScalar.FromRatio(1, 1000000))
                throw new InvalidOperationException("ScaleToTarget requires a non-zero source window planar endpoint.");
            FixedScalar dot = sourceEnd.X * targetEnd.X + sourceEnd.Z * targetEnd.Z;
            FixedScalar cross = sourceEnd.X * targetEnd.Z - sourceEnd.Z * targetEnd.X;
            return new FixedVector3(
                (dot * value.X - cross * value.Z) / denominator,
                value.Y,
                (cross * value.X + dot * value.Z) / denominator);
        }

        static FixedVector3 Planar(FixedVector3 value) =>
            new FixedVector3(value.X, FixedScalar.Zero, value.Z);

        static FixedVector3 ClampMagnitude(FixedVector3 value, FixedScalar maximum)
        {
            if (maximum <= FixedScalar.Zero)
                return FixedVector3.Zero;
            FixedScalar magnitude = new FixedVector2(value.X, value.Z).Magnitude;
            return magnitude > maximum ? value * (maximum / magnitude) : value;
        }
    }

    internal sealed class FixedLocomotionRuntime : FixedOperationModule
    {
        readonly IFixedValueInputReader m_Values;
        readonly IFixedMotionContributionSink m_Motion;
        readonly FixedAbilityExecutionFrame m_Frame;
        readonly LocomotionProfile[] m_Profiles;

        public FixedLocomotionRuntime(
            FixedGameplayAbilityExecutionAccess access,
            IFixedValueInputReader values,
            IFixedMotionContributionSink motion,
            FixedAbilityExecutionFrame frame)
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
            using FixedValueInputLease inputs = m_Values.ReadInputs(cursor, operation);
            AbilityStateValue input = inputs.FindByKind(ProgramStateValueKind.Vector2);
            if (input.Kind != ProgramStateValueKind.Vector2)
                throw new InvalidOperationException($"Locomotion operation '{SourcePath(operation)}' has no Vector2 input.");
            FixedVector2 move = input.Vector2;
            if (move.SqrMagnitude > FixedScalar.One)
                move = move.Normalized;
            var movementPlaybackClock = new CommittedMovementPlaybackClock(
                SourcePath(operation),
                generation,
                m_Frame.Tick,
                committedTicks,
                m_Ability.TickRate);
            FixedScalar delta = FixedScalar.One / FixedScalar.FromInt64(m_Ability.TickRate);
            ref readonly LocomotionProfile profile = ref m_Profiles[operation.Handle.Value];
            FixedScalar maxYaw = profile.TurnSpeed * delta;
            FixedVector3 displacement = ResolveDisplacement(in profile, move, delta, committedTicks - 1);
            FixedScalar yaw = FixedScalar.Zero;
            if (move != FixedVector2.Zero && maxYaw > FixedScalar.Zero)
            {
                FixedYaw desired = FixedAngle.FromPlanarDirection(move);
                yaw = FixedScalar.Clamp(FixedAngle.Delta(m_Frame.BodyFacts.Yaw, desired), -maxYaw, maxYaw);
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
                FixedScalar.One,
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
            FixedVector2 move,
            FixedVector3 displacement,
            FixedScalar yawDegrees,
            FixedScalar maximumYawVelocityDegreesPerSecond,
            FixedScalar delta,
            ulong generation)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            ref readonly LocomotionProfile profile = ref m_Profiles[operation.Handle.Value];
            int durationTicks = profile.DurationTicks;
            string continuationOwner = string.Empty;
            FixedVector2 continuationVelocity = FixedVector2.Zero;
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
                    if (speed == null || speed.Kind != ProgramConstantKind.Scalar || speed.Scalar < FixedScalar.Zero)
                        throw new InvalidOperationException($"Locomotion continuation '{Access.Services.SourcePath(continuation.Handle)}' has invalid Move Speed.");
                    continuationOwner = Access.Services.SourcePath(continuation.Handle);
                    continuationVelocity = move * speed.Scalar;
                }
            }
            FixedVector2 currentVelocity = delta > FixedScalar.Zero
                ? new FixedVector2(displacement.X / delta, displacement.Z / delta)
                : FixedVector2.Zero;
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

        FixedVector3 ResolveDisplacement(
            in LocomotionProfile profile,
            FixedVector2 move,
            FixedScalar delta,
            int elapsedTicks)
        {
            if (profile.DisplacementMode == LocomotionInputMotionDisplacementMode.ConstantSpeed)
            {
                return new FixedVector3(
                    move.X * profile.MoveSpeed * delta,
                    FixedScalar.Zero,
                    move.Y * profile.MoveSpeed * delta);
            }
            if (move == FixedVector2.Zero)
                return FixedVector3.Zero;

            FixedScalar tickRate = FixedScalar.FromInt64(m_Ability.TickRate);
            FixedScalar fromTime = FixedScalar.FromInt64(elapsedTicks) / tickRate;
            FixedScalar toTime = FixedScalar.FromInt64(checked(elapsedTicks + 1)) / tickRate;
            bool looping = profile.ExecutionMode == LocomotionInputMotionExecutionMode.Continuous;
            FixedGameplayAbilityCurve xCurve = profile.XCurve;
            FixedGameplayAbilityCurve zCurve = profile.ZCurve;
            FixedScalar duration = profile.CurveDuration;
            FixedScalar xCycleTotal = looping ? xCurve.Evaluate(duration, FixedScalar.Zero) : FixedScalar.Zero;
            FixedScalar zCycleTotal = looping ? zCurve.Evaluate(duration, FixedScalar.Zero) : FixedScalar.Zero;
            FixedScalar localX = SampleCumulative(xCurve, toTime, duration, looping, xCycleTotal) -
                SampleCumulative(xCurve, fromTime, duration, looping, xCycleTotal);
            FixedScalar localZ = SampleCumulative(zCurve, toTime, duration, looping, zCycleTotal) -
                SampleCumulative(zCurve, fromTime, duration, looping, zCycleTotal);

            FixedVector2 forward = move.Normalized;
            FixedVector2 right = new FixedVector2(forward.Y, -forward.X);
            return new FixedVector3(
                right.X * localX + forward.X * localZ,
                FixedScalar.Zero,
                right.Y * localX + forward.Y * localZ);
        }

        static FixedScalar SampleCumulative(
            FixedGameplayAbilityCurve curve,
            FixedScalar time,
            FixedScalar duration,
            bool looping,
            FixedScalar cycleTotal)
        {
            if (!looping)
                return curve.Evaluate(FixedScalar.Clamp(time, FixedScalar.Zero, duration), FixedScalar.Zero);
            int cycle = (time / duration).TruncateToInt32();
            FixedScalar localTime = time - duration * FixedScalar.FromInt64(cycle);
            return cycleTotal * FixedScalar.FromInt64(cycle) + curve.Evaluate(localTime, FixedScalar.Zero);
        }

        static LocomotionProfile[] BuildProfiles(
            GameplayAbilityExecutionLayout layout,
            FixedGameplayAbilityExecutionServices services)
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
                FixedScalar moveSpeed = FixedScalar.Zero;
                FixedScalar curveDuration = FixedScalar.Zero;
                FixedGameplayAbilityCurve xCurve = null;
                FixedGameplayAbilityCurve zCurve = null;
                int durationTicks = 0;
                if (executionMode == LocomotionInputMotionExecutionMode.Timed)
                {
                    ProgramConstant duration = layout.FindNamedConstant(operation.Handle, OperationNamedConstant.DurationSeconds);
                    if (duration == null || duration.Kind != ProgramConstantKind.Scalar || duration.Scalar <= FixedScalar.Zero)
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
                        durationConstant.Scalar <= FixedScalar.Zero)
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
                FixedScalar turnSpeed,
                FixedScalar moveSpeed,
                FixedScalar curveDuration,
                int durationTicks,
                FixedGameplayAbilityCurve xCurve,
                FixedGameplayAbilityCurve zCurve)
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
            public FixedScalar TurnSpeed { get; }
            public FixedScalar MoveSpeed { get; }
            public FixedScalar CurveDuration { get; }
            public int DurationTicks { get; }
            public FixedGameplayAbilityCurve XCurve { get; }
            public FixedGameplayAbilityCurve ZCurve { get; }
        }
    }
}

