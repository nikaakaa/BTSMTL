using GameplayAbilityCurve = ThirdPersonSimulation.GameplayAbilityCurve<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32SimulationMath>;
using MotionWarpWindowRuntime = ThirdPersonSimulation.MotionWarpWindowRuntime<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32Yaw, ThirdPersonSimulation.Float32ActionInstanceReference, ThirdPersonSimulation.Float32SimulationMath, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Float32Scalar>, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Float32Vector3>, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Float32Yaw>>;
using MotionWarpCalculation = ThirdPersonSimulation.MotionWarpCalculation<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32Yaw, ThirdPersonSimulation.Float32SimulationMath>;
using MotionWarpState = ThirdPersonSimulation.MotionWarpState<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32Yaw, ThirdPersonSimulation.Float32ActionInstanceReference, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Float32Scalar>, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Float32Vector3>, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Float32Yaw>>;
using SimulationMotionContribution = ThirdPersonSimulation.SimulationMotionContribution<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32SimulationMath>;
using ResolvedMotionChannel = ThirdPersonSimulation.ResolvedMotionChannel<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32SimulationMath>;
using System;
using System.Collections.Generic;
using ThirdPersonSimulation.Fixed;

namespace ThirdPersonSimulation
{
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
        IAbilityTimelineMotionWarpTarget<ResolvedMotionChannel>,
        IMotionWarpWindowTarget<AbilityTimelineLogicMotionWarp, Float32Scalar, Float32Vector2, Float32Vector3,
            Float32Yaw, Float32ActionInstanceReference, MotionWarpState, Float32SimulationMath>
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

        public void CaptureWindowEntry(in AbilityTimelineLogicMotionWarp warp) =>
            MotionWarpWindowRuntime.Capture(in warp, this);

        public void ApplyTimelineMotionWarp(in AbilityTimelineLogicMotionWarp warp, ref ResolvedMotionChannel channel) =>
            MotionWarpWindowRuntime.Apply(in warp, this, ref channel);

        public MotionWarpState ReadState(in AbilityTimelineLogicMotionWarp warp) =>
            m_Frame.SkillState.GetMotionWarpState(warp.StateOperation);

        public void WriteState(in AbilityTimelineLogicMotionWarp warp, in MotionWarpState state) =>
            m_Frame.SkillState.SetMotionWarpState(warp.StateOperation, in state);

        public Float32ActionInstanceReference ReadAction(in AbilityTimelineLogicMotionWarp warp)
        {
            m_Actions.TryGetEvaluatedInstance(warp.ActionInstanceId, out Float32ActionInstanceState action);
            return Float32ActionInstanceReference.FromInstance(action);
        }

        public void ReadTarget(in AbilityTimelineLogicMotionWarp warp, out Float32Vector3 position, out Float32Yaw yaw)
        {
            position = new Float32Vector3(
                Float32Scalar.FromSingle(warp.Target.X.ToSingle()), Float32Scalar.FromSingle(warp.Target.Y.ToSingle()), Float32Scalar.FromSingle(warp.Target.Z.ToSingle()));
            yaw = new Float32Yaw(Float32Scalar.FromSingle(warp.Target.Yaw.ToSingle()));
        }

        public Float32Vector3 BodyPosition => m_Frame.BodyFacts.Position;
        public Float32Yaw BodyYaw => m_Frame.BodyFacts.Yaw;

        public void ReadSourceDelta(in AbilityTimelineLogicMotionWarp warp, out Float32Vector3 displacement, out Float32Scalar yaw)
        {
            displacement = new Float32Vector3(
                Float32Scalar.FromSingle((warp.CurrentPositionX - warp.PreviousPositionX).ToSingle()), Float32Scalar.FromSingle((warp.CurrentPositionY - warp.PreviousPositionY).ToSingle()), Float32Scalar.FromSingle((warp.CurrentPositionZ - warp.PreviousPositionZ).ToSingle()));
            yaw = Float32Scalar.FromSingle((warp.CurrentYawDegrees - warp.PreviousYawDegrees).ToSingle());
        }

        public void ReadSourcePrefix(in AbilityTimelineLogicMotionWarp warp, out Float32Vector3 displacement, out Float32Scalar yaw)
        {
            displacement = new Float32Vector3(
                Float32Scalar.FromSingle(warp.SourcePrefix.X.ToSingle()), Float32Scalar.FromSingle(warp.SourcePrefix.Y.ToSingle()), Float32Scalar.FromSingle(warp.SourcePrefix.Z.ToSingle()));
            yaw = Float32Scalar.FromSingle(warp.SourcePrefix.Yaw.ToSingle());
        }

        public void ReadProgress(in AbilityTimelineLogicMotionWarp warp, bool previous,
            out Float32Scalar position, out Float32Scalar yaw)
        {
            position = Float32Scalar.FromSingle((previous ? warp.PreviousPositionProgress : warp.CurrentPositionProgress).ToSingle());
            yaw = Float32Scalar.FromSingle((previous ? warp.PreviousYawProgress : warp.CurrentYawProgress).ToSingle());
        }

        public void ApplyResponse(
            in AbilityTimelineLogicMotionWarp warp,
            in MotionWarpParameters<Float32Scalar> parameters,
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
                Float32Scalar.FromSingle(warp.YawResponse.ToSingle()), Float32Scalar.FromSingle(warp.InputYawResponse.ToSingle()),
                new Float32Vector2(Float32Scalar.FromSingle(warp.InputDirectionX.ToSingle()), Float32Scalar.FromSingle(warp.InputDirectionZ.ToSingle())),
                Float32Scalar.FromSingle((warp.CurrentTime - warp.PreviousTime).ToSingle()),
                new Float32Vector3(
                    Float32Scalar.FromSingle((warp.CurrentPositionX - warp.PreviousPositionX).ToSingle()),
                    Float32Scalar.FromSingle((warp.CurrentPositionY - warp.PreviousPositionY).ToSingle()),
                    Float32Scalar.FromSingle((warp.CurrentPositionZ - warp.PreviousPositionZ).ToSingle())),
                m_Frame.BodyFacts.Position, m_Frame.BodyFacts.Yaw,
                channel.MotionCorrectionDisplacement, channel.MotionCorrectionYawDegrees,
                out Float32Vector3 previousDirectionDelta, out Float32Vector3 nextDirectionDelta, out Float32Scalar yawDelta);
            channel.ApplyMotionWarp(previousDirectionDelta, Float32Scalar.FromSingle((warp.CurrentYawDegrees - warp.PreviousYawDegrees).ToSingle()),
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

        public MotionWarpParameters<Float32Scalar> ReadParameters(in AbilityTimelineLogicMotionWarp warp) =>
            new MotionWarpParameters<Float32Scalar>(
                warp.TranslationMode, warp.TargetOffsetSpace, warp.RotationMode, warp.RotationMethod, warp.LimitPolicy,
                Float32Scalar.FromSingle(warp.TargetPlanarOffsetX), Float32Scalar.FromSingle(warp.TargetPlanarOffsetY),
                Float32Scalar.FromSingle(warp.TargetYawOffsetDegrees), Float32Scalar.FromSingle(warp.MaximumPositionCorrection),
                Float32Scalar.FromSingle(warp.MaximumYawCorrectionDegrees), Float32Scalar.FromSingle(warp.MaximumYawRateDegreesPerSecond),
                Float32Scalar.FromSingle(0.000001f));

        public void ResolveTarget(
            in AbilityTimelineLogicMotionWarp warp,
            in MotionWarpParameters<Float32Scalar> parameters,
            Float32Vector3 targetPosition,
            Float32Yaw targetYaw,
            Float32Vector3 bodyPosition,
            Float32Yaw bodyYaw,
            out Float32Vector3 sourceWindowStartPosition,
            out Float32Scalar sourceWindowStartYaw,
            out Float32Vector3 resolvedTargetPosition,
            out Float32Yaw resolvedTargetYaw,
            out ProgramMotionWarpLimitResult limitResult)
        {
            sourceWindowStartPosition = new Float32Vector3(
                    Float32Scalar.FromSingle(warp.SourceStartPositionX.ToSingle()),
                    Float32Scalar.FromSingle(warp.SourceStartPositionY.ToSingle()),
                    Float32Scalar.FromSingle(warp.SourceStartPositionZ.ToSingle()));
            sourceWindowStartYaw = Float32Scalar.FromSingle(warp.SourceStartYawDegrees.ToSingle());
            MotionWarpCalculation.ResolveTarget(
                in parameters,
                new Float32Vector3(
                    Float32Scalar.FromSingle((warp.SourceEndPositionX - warp.SourceStartPositionX).ToSingle()),
                    Float32Scalar.FromSingle((warp.SourceEndPositionY - warp.SourceStartPositionY).ToSingle()),
                    Float32Scalar.FromSingle((warp.SourceEndPositionZ - warp.SourceStartPositionZ).ToSingle())),
                Float32Scalar.FromSingle((warp.SourceEndYawDegrees - warp.SourceStartYawDegrees).ToSingle()), Float32Scalar.FromSingle((warp.EndTime - warp.StartTime).ToSingle()),
                targetPosition, targetYaw, bodyPosition, bodyYaw,
                out resolvedTargetPosition, out resolvedTargetYaw, out limitResult);
        }

        public void EvaluatePose(
            in AbilityTimelineLogicMotionWarp warp,
            in MotionWarpParameters<Float32Scalar> parameters,
            bool previous,
            Float32Vector3 startBodyPosition,
            Float32Yaw startBodyYaw,
            Float32Vector3 sourceWindowStartPosition,
            Float32Scalar sourceWindowStartYaw,
            Float32Vector3 resolvedTargetPosition,
            Float32Yaw resolvedTargetYaw,
            out Float32Vector3 warpedPosition,
            out Float32Yaw warpedYaw)
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
                new Float32Vector3(
                    Float32Scalar.FromSingle(sourcePositionX.ToSingle()),
                    Float32Scalar.FromSingle(sourcePositionY.ToSingle()),
                    Float32Scalar.FromSingle(sourcePositionZ.ToSingle())) - sourceWindowStartPosition,
                Float32Scalar.FromSingle(sourceYawDegrees.ToSingle()) - sourceWindowStartYaw,
                new Float32Vector3(
                    Float32Scalar.FromSingle(warp.SourceEndPositionX.ToSingle()),
                    Float32Scalar.FromSingle(warp.SourceEndPositionY.ToSingle()),
                    Float32Scalar.FromSingle(warp.SourceEndPositionZ.ToSingle())) - sourceWindowStartPosition,
                Float32Scalar.FromSingle(warp.SourceEndYawDegrees.ToSingle()) - sourceWindowStartYaw,
                Float32Scalar.FromSingle(positionProgress.ToSingle()), Float32Scalar.FromSingle(yawProgress.ToSingle()), Float32Scalar.FromSingle((sampleTime - warp.StartTime).ToSingle()),
                startBodyPosition, startBodyYaw, resolvedTargetPosition, resolvedTargetYaw,
                out warpedPosition, out warpedYaw);
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
            GameplayAbilityCurve xCurve = profile.XCurve;
            GameplayAbilityCurve zCurve = profile.ZCurve;
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
            GameplayAbilityCurve curve,
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
                GameplayAbilityCurve xCurve = null;
                GameplayAbilityCurve zCurve = null;
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
                GameplayAbilityCurve xCurve,
                GameplayAbilityCurve zCurve)
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
            public GameplayAbilityCurve XCurve { get; }
            public GameplayAbilityCurve ZCurve { get; }
        }
    }
}

