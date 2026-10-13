using MotionWarpWindowRuntime = ThirdPersonSimulation.MotionWarpWindowRuntime<ThirdPersonSimulation.Fixed.FixedScalar, ThirdPersonSimulation.Fixed.FixedVector2, ThirdPersonSimulation.Fixed.FixedVector3, ThirdPersonSimulation.Fixed.FixedYaw, ThirdPersonSimulation.Fixed.FixedActionInstanceReference, ThirdPersonSimulation.Fixed.FixedSimulationMath, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Fixed.FixedScalar>, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Fixed.FixedVector3>, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Fixed.FixedYaw>>;
using MotionWarpCalculation = ThirdPersonSimulation.MotionWarpCalculation<ThirdPersonSimulation.Fixed.FixedScalar, ThirdPersonSimulation.Fixed.FixedVector2, ThirdPersonSimulation.Fixed.FixedVector3, ThirdPersonSimulation.Fixed.FixedYaw, ThirdPersonSimulation.Fixed.FixedSimulationMath>;
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
        IAbilityTimelineMotionWarpTarget<ResolvedMotionChannel>,
        IMotionWarpWindowTarget<AbilityTimelineLogicMotionWarp, FixedScalar, FixedVector2, FixedVector3,
            FixedYaw, FixedActionInstanceReference, MotionWarpState, FixedSimulationMath>
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

        public void CaptureWindowEntry(in AbilityTimelineLogicMotionWarp warp) =>
            MotionWarpWindowRuntime.Capture(in warp, this);

        public void ApplyTimelineMotionWarp(in AbilityTimelineLogicMotionWarp warp, ref ResolvedMotionChannel channel) =>
            MotionWarpWindowRuntime.Apply(in warp, this, ref channel);

        public MotionWarpState ReadState(in AbilityTimelineLogicMotionWarp warp) =>
            m_Frame.SkillState.GetMotionWarpState(warp.StateOperation);

        public void WriteState(in AbilityTimelineLogicMotionWarp warp, in MotionWarpState state) =>
            m_Frame.SkillState.SetMotionWarpState(warp.StateOperation, in state);

        public FixedActionInstanceReference ReadAction(in AbilityTimelineLogicMotionWarp warp)
        {
            m_Actions.TryGetEvaluatedInstance(warp.ActionInstanceId, out FixedActionInstanceState action);
            return FixedActionInstanceReference.FromInstance(action);
        }

        public void ReadTarget(in AbilityTimelineLogicMotionWarp warp, out FixedVector3 position, out FixedYaw yaw)
        {
            position = new FixedVector3(
                warp.Target.X, warp.Target.Y, warp.Target.Z);
            yaw = new FixedYaw(warp.Target.Yaw);
        }

        public FixedVector3 BodyPosition => m_Frame.BodyFacts.Position;
        public FixedYaw BodyYaw => m_Frame.BodyFacts.Yaw;

        public void ReadSourceDelta(in AbilityTimelineLogicMotionWarp warp, out FixedVector3 displacement, out FixedScalar yaw)
        {
            displacement = new FixedVector3(
                warp.CurrentPositionX - warp.PreviousPositionX, warp.CurrentPositionY - warp.PreviousPositionY, warp.CurrentPositionZ - warp.PreviousPositionZ);
            yaw = warp.CurrentYawDegrees - warp.PreviousYawDegrees;
        }

        public void ReadSourcePrefix(in AbilityTimelineLogicMotionWarp warp, out FixedVector3 displacement, out FixedScalar yaw)
        {
            displacement = new FixedVector3(
                warp.SourcePrefix.X, warp.SourcePrefix.Y, warp.SourcePrefix.Z);
            yaw = warp.SourcePrefix.Yaw;
        }

        public void ReadProgress(in AbilityTimelineLogicMotionWarp warp, bool previous,
            out FixedScalar position, out FixedScalar yaw)
        {
            position = previous ? warp.PreviousPositionProgress : warp.CurrentPositionProgress;
            yaw = previous ? warp.PreviousYawProgress : warp.CurrentYawProgress;
        }

        public void ApplyResponse(
            in AbilityTimelineLogicMotionWarp warp,
            in MotionWarpParameters<FixedScalar> parameters,
            in MotionWarpState state,
            ref ResolvedMotionChannel channel)
        {
            if (warp.TranslationMode != ProgramMotionWarpTranslationMode.Disabled ||
                warp.YawResponse < ThirdPersonSimulation.Fixed.FixedScalar.Zero ||
                warp.InputYawResponse < ThirdPersonSimulation.Fixed.FixedScalar.Zero)
                FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.InvalidState,
                    "Target response requires non-negative response and no translation correction.");
            MotionWarpCalculation.TargetResponse(
                in parameters, state.HasTarget, state.CapturedTargetPosition, state.CapturedTargetYaw,
                warp.YawResponse, warp.InputYawResponse,
                new FixedVector2(warp.InputDirectionX, warp.InputDirectionZ),
                warp.CurrentTime - warp.PreviousTime,
                new FixedVector3(warp.CurrentPositionX - warp.PreviousPositionX, warp.CurrentPositionY - warp.PreviousPositionY, warp.CurrentPositionZ - warp.PreviousPositionZ),
                m_Frame.BodyFacts.Position, m_Frame.BodyFacts.Yaw,
                channel.MotionCorrectionDisplacement, channel.MotionCorrectionYawDegrees,
                out FixedVector3 previousDirectionDelta, out FixedVector3 nextDirectionDelta, out FixedScalar yawDelta);
            channel.ApplyMotionWarp(previousDirectionDelta, warp.CurrentYawDegrees - warp.PreviousYawDegrees,
                nextDirectionDelta, yawDelta);
        }

        public void FailTimelineMotionWarp(OperationHandle operation, string code, string detail)
        {
            throw new InvalidOperationException($"{code}: {detail}");
        }

        public void TraceSourceNotResolved(in AbilityTimelineLogicMotionWarp warp) => Trace(warp, MotionModifierDiagnosticCode.SourceNotResolved);

        public void Trace(in AbilityTimelineLogicMotionWarp warp, string code)
        {
            m_Frame.Trace.AddMotionWarp(warp, code);
        }

        public MotionWarpParameters<FixedScalar> ReadParameters(in AbilityTimelineLogicMotionWarp warp) =>
            new MotionWarpParameters<FixedScalar>(
                warp.TranslationMode, warp.TargetOffsetSpace, warp.RotationMode, warp.RotationMethod, warp.LimitPolicy,
                FixedScalar.FromSingle(warp.TargetPlanarOffsetX), FixedScalar.FromSingle(warp.TargetPlanarOffsetY),
                FixedScalar.FromSingle(warp.TargetYawOffsetDegrees), FixedScalar.FromSingle(warp.MaximumPositionCorrection),
                FixedScalar.FromSingle(warp.MaximumYawCorrectionDegrees), FixedScalar.FromSingle(warp.MaximumYawRateDegreesPerSecond),
                FixedScalar.FromRatio(1, 1000000));

        public void ResolveTarget(
            in AbilityTimelineLogicMotionWarp warp,
            in MotionWarpParameters<FixedScalar> parameters,
            FixedVector3 targetPosition,
            FixedYaw targetYaw,
            FixedVector3 bodyPosition,
            FixedYaw bodyYaw,
            out FixedVector3 sourceWindowStartPosition,
            out FixedScalar sourceWindowStartYaw,
            out FixedVector3 resolvedTargetPosition,
            out FixedYaw resolvedTargetYaw,
            out ProgramMotionWarpLimitResult limitResult)
        {
            sourceWindowStartPosition = new FixedVector3(warp.SourceStartPositionX, warp.SourceStartPositionY, warp.SourceStartPositionZ);
            sourceWindowStartYaw = warp.SourceStartYawDegrees;
            MotionWarpCalculation.ResolveTarget(
                in parameters,
                new FixedVector3(warp.SourceEndPositionX - warp.SourceStartPositionX, warp.SourceEndPositionY - warp.SourceStartPositionY, warp.SourceEndPositionZ - warp.SourceStartPositionZ),
                warp.SourceEndYawDegrees - warp.SourceStartYawDegrees, warp.EndTime - warp.StartTime,
                targetPosition, targetYaw, bodyPosition, bodyYaw,
                out resolvedTargetPosition, out resolvedTargetYaw, out limitResult);
        }

        public void EvaluatePose(
            in AbilityTimelineLogicMotionWarp warp,
            in MotionWarpParameters<FixedScalar> parameters,
            bool previous,
            FixedVector3 startBodyPosition,
            FixedYaw startBodyYaw,
            FixedVector3 sourceWindowStartPosition,
            FixedScalar sourceWindowStartYaw,
            FixedVector3 resolvedTargetPosition,
            FixedYaw resolvedTargetYaw,
            out FixedVector3 warpedPosition,
            out FixedYaw warpedYaw)
        {
            FixedScalar sourcePositionX = previous ? warp.PreviousPositionX : warp.CurrentPositionX;
            FixedScalar sourcePositionY = previous ? warp.PreviousPositionY : warp.CurrentPositionY;
            FixedScalar sourcePositionZ = previous ? warp.PreviousPositionZ : warp.CurrentPositionZ;
            FixedScalar sourceYawDegrees = previous ? warp.PreviousYawDegrees : warp.CurrentYawDegrees;
            FixedScalar positionProgress = previous ? warp.PreviousPositionProgress : warp.CurrentPositionProgress;
            FixedScalar yawProgress = previous ? warp.PreviousYawProgress : warp.CurrentYawProgress;
            FixedScalar sampleTime = previous ? warp.PreviousTime : warp.CurrentTime;
            MotionWarpCalculation.EvaluatePose(
                in parameters,
                new FixedVector3(sourcePositionX, sourcePositionY, sourcePositionZ) - sourceWindowStartPosition,
                sourceYawDegrees - sourceWindowStartYaw,
                new FixedVector3(warp.SourceEndPositionX, warp.SourceEndPositionY, warp.SourceEndPositionZ) - sourceWindowStartPosition,
                warp.SourceEndYawDegrees - sourceWindowStartYaw,
                positionProgress, yawProgress, sampleTime - warp.StartTime,
                startBodyPosition, startBodyYaw, resolvedTargetPosition, resolvedTargetYaw,
                out warpedPosition, out warpedYaw);
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

