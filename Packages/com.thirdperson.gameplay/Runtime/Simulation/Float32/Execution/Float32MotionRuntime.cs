using LocomotionProfile = ThirdPersonSimulation.LocomotionMotionProfile<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32SimulationMath>;
using LocomotionMotionCalculation = ThirdPersonSimulation.LocomotionMotionCalculation<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32Yaw, ThirdPersonSimulation.Float32SimulationMath>;
using GameplayAbilityCurve = ThirdPersonSimulation.GameplayAbilityCurve<ThirdPersonSimulation.Float32Scalar>;
using MotionWarpWindowRuntime = ThirdPersonSimulation.MotionWarpWindowRuntime<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32Yaw, ThirdPersonSimulation.Float32ActionInstanceReference, ThirdPersonSimulation.Float32SimulationMath>;
using MotionWarpCalculation = ThirdPersonSimulation.MotionWarpCalculation<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32Yaw, ThirdPersonSimulation.Float32SimulationMath>;
using MotionWarpState = ThirdPersonSimulation.MotionWarpState<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32Yaw, ThirdPersonSimulation.Float32ActionInstanceReference>;
using SimulationMotionContribution = ThirdPersonSimulation.SimulationMotionContribution<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32SimulationMath>;
using ResolvedMotionChannel = ThirdPersonSimulation.ResolvedMotionChannel<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32SimulationMath>;
using System;
using System.Collections.Generic;
using Photon.Deterministic;

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
                Float32Scalar.FromSingle(warp.Target.X.AsFloat), Float32Scalar.FromSingle(warp.Target.Y.AsFloat), Float32Scalar.FromSingle(warp.Target.Z.AsFloat));
            yaw = new Float32Yaw(Float32Scalar.FromSingle(warp.Target.Yaw.AsFloat));
        }

        public Float32Vector3 BodyPosition => m_Frame.BodyFacts.Position;
        public Float32Yaw BodyYaw => m_Frame.BodyFacts.Yaw;

        public void ReadSourceDelta(in AbilityTimelineLogicMotionWarp warp, out Float32Vector3 displacement, out Float32Scalar yaw)
        {
            displacement = new Float32Vector3(
                Float32Scalar.FromSingle((warp.CurrentPositionX - warp.PreviousPositionX).AsFloat), Float32Scalar.FromSingle((warp.CurrentPositionY - warp.PreviousPositionY).AsFloat), Float32Scalar.FromSingle((warp.CurrentPositionZ - warp.PreviousPositionZ).AsFloat));
            yaw = Float32Scalar.FromSingle((warp.CurrentYawDegrees - warp.PreviousYawDegrees).AsFloat);
        }

        public void ReadSourcePrefix(in AbilityTimelineLogicMotionWarp warp, out Float32Vector3 displacement, out Float32Scalar yaw)
        {
            displacement = new Float32Vector3(
                Float32Scalar.FromSingle(warp.SourcePrefix.X.AsFloat), Float32Scalar.FromSingle(warp.SourcePrefix.Y.AsFloat), Float32Scalar.FromSingle(warp.SourcePrefix.Z.AsFloat));
            yaw = Float32Scalar.FromSingle(warp.SourcePrefix.Yaw.AsFloat);
        }

        public void ReadProgress(in AbilityTimelineLogicMotionWarp warp, bool previous,
            out Float32Scalar position, out Float32Scalar yaw)
        {
            position = Float32Scalar.FromSingle((previous ? warp.PreviousPositionProgress : warp.CurrentPositionProgress).AsFloat);
            yaw = Float32Scalar.FromSingle((previous ? warp.PreviousYawProgress : warp.CurrentYawProgress).AsFloat);
        }

        public void ApplyResponse(
            in AbilityTimelineLogicMotionWarp warp,
            in MotionWarpParameters<Float32Scalar> parameters,
            in MotionWarpState state,
            ref ResolvedMotionChannel channel)
        {
            if (warp.TranslationMode != ProgramMotionWarpTranslationMode.Disabled ||
                warp.YawResponse < FP._0 ||
                warp.InputYawResponse < FP._0)
                FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.InvalidState,
                    "Target response requires non-negative response and no translation correction.");
            MotionWarpCalculation.TargetResponse(
                in parameters, state.HasTarget, state.CapturedTargetPosition, state.CapturedTargetYaw,
                Float32Scalar.FromSingle(warp.YawResponse.AsFloat), Float32Scalar.FromSingle(warp.InputYawResponse.AsFloat),
                new Float32Vector2(Float32Scalar.FromSingle(warp.InputDirectionX.AsFloat), Float32Scalar.FromSingle(warp.InputDirectionZ.AsFloat)),
                Float32Scalar.FromSingle((warp.CurrentTime - warp.PreviousTime).AsFloat),
                new Float32Vector3(
                    Float32Scalar.FromSingle((warp.CurrentPositionX - warp.PreviousPositionX).AsFloat),
                    Float32Scalar.FromSingle((warp.CurrentPositionY - warp.PreviousPositionY).AsFloat),
                    Float32Scalar.FromSingle((warp.CurrentPositionZ - warp.PreviousPositionZ).AsFloat)),
                m_Frame.BodyFacts.Position, m_Frame.BodyFacts.Yaw,
                channel.MotionCorrectionDisplacement, channel.MotionCorrectionYawDegrees,
                out Float32Vector3 previousDirectionDelta, out Float32Vector3 nextDirectionDelta, out Float32Scalar yawDelta);
            channel.ApplyMotionWarp(previousDirectionDelta, Float32Scalar.FromSingle((warp.CurrentYawDegrees - warp.PreviousYawDegrees).AsFloat),
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
                Float32Scalar.FromSingle(warp.TargetPlanarOffsetX.AsFloat), Float32Scalar.FromSingle(warp.TargetPlanarOffsetY.AsFloat),
                Float32Scalar.FromSingle(warp.TargetYawOffsetDegrees.AsFloat), Float32Scalar.FromSingle(warp.MaximumPositionCorrection.AsFloat),
                Float32Scalar.FromSingle(warp.MaximumYawCorrectionDegrees.AsFloat), Float32Scalar.FromSingle(warp.MaximumYawRateDegreesPerSecond.AsFloat),
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
                    Float32Scalar.FromSingle(warp.SourceStartPositionX.AsFloat),
                    Float32Scalar.FromSingle(warp.SourceStartPositionY.AsFloat),
                    Float32Scalar.FromSingle(warp.SourceStartPositionZ.AsFloat));
            sourceWindowStartYaw = Float32Scalar.FromSingle(warp.SourceStartYawDegrees.AsFloat);
            MotionWarpCalculation.ResolveTarget(
                in parameters,
                new Float32Vector3(
                    Float32Scalar.FromSingle((warp.SourceEndPositionX - warp.SourceStartPositionX).AsFloat),
                    Float32Scalar.FromSingle((warp.SourceEndPositionY - warp.SourceStartPositionY).AsFloat),
                    Float32Scalar.FromSingle((warp.SourceEndPositionZ - warp.SourceStartPositionZ).AsFloat)),
                Float32Scalar.FromSingle((warp.SourceEndYawDegrees - warp.SourceStartYawDegrees).AsFloat), Float32Scalar.FromSingle((warp.EndTime - warp.StartTime).AsFloat),
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
            FP sourcePositionX = previous ? warp.PreviousPositionX : warp.CurrentPositionX;
            FP sourcePositionY = previous ? warp.PreviousPositionY : warp.CurrentPositionY;
            FP sourcePositionZ = previous ? warp.PreviousPositionZ : warp.CurrentPositionZ;
            FP sourceYawDegrees = previous ? warp.PreviousYawDegrees : warp.CurrentYawDegrees;
            FP positionProgress = previous ? warp.PreviousPositionProgress : warp.CurrentPositionProgress;
            FP yawProgress = previous ? warp.PreviousYawProgress : warp.CurrentYawProgress;
            FP sampleTime = previous ? warp.PreviousTime : warp.CurrentTime;
            MotionWarpCalculation.EvaluatePose(
                in parameters,
                new Float32Vector3(
                    Float32Scalar.FromSingle(sourcePositionX.AsFloat),
                    Float32Scalar.FromSingle(sourcePositionY.AsFloat),
                    Float32Scalar.FromSingle(sourcePositionZ.AsFloat)) - sourceWindowStartPosition,
                Float32Scalar.FromSingle(sourceYawDegrees.AsFloat) - sourceWindowStartYaw,
                new Float32Vector3(
                    Float32Scalar.FromSingle(warp.SourceEndPositionX.AsFloat),
                    Float32Scalar.FromSingle(warp.SourceEndPositionY.AsFloat),
                    Float32Scalar.FromSingle(warp.SourceEndPositionZ.AsFloat)) - sourceWindowStartPosition,
                Float32Scalar.FromSingle(warp.SourceEndYawDegrees.AsFloat) - sourceWindowStartYaw,
                Float32Scalar.FromSingle(positionProgress.AsFloat), Float32Scalar.FromSingle(yawProgress.AsFloat), Float32Scalar.FromSingle((sampleTime - warp.StartTime).AsFloat),
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
            Float32Vector2 move = LocomotionMotionCalculation.NormalizeInput(input.Vector2);
            var movementPlaybackClock = new CommittedMovementPlaybackClock(
                SourcePath(operation),
                generation,
                m_Frame.Tick,
                committedTicks,
                m_Ability.TickRate);
            ref readonly LocomotionProfile profile = ref m_Profiles[operation.Handle.Value];
            Float32Scalar delta = profile.TickDelta;
            LocomotionMotionCalculation.Evaluate(in profile, move, m_Frame.BodyFacts.Yaw, committedTicks - 1,
                out Float32Vector3 displacement, out Float32Scalar yaw);
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
                    if (xConstant == null || xConstant.Kind != ProgramConstantKind.Curve ||
                        zConstant == null || zConstant.Kind != ProgramConstantKind.Curve ||
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
                    zCurve,
                    layout.TickRate);
            }

            return profiles;
        }

    }
}

