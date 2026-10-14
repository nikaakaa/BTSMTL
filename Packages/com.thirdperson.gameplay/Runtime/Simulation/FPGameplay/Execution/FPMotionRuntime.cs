using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using LocomotionProfile = ThirdPersonSimulation.LocomotionMotionProfile<Photon.Deterministic.FP, ThirdPersonSimulation.QuantumNumerics.QuantumSimulationMath>;
using LocomotionMotionCalculation = ThirdPersonSimulation.LocomotionMotionCalculation<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, Photon.Deterministic.FP, ThirdPersonSimulation.QuantumNumerics.QuantumSimulationMath>;
using GameplayAbilityCurve = ThirdPersonSimulation.GameplayAbilityCurve<Photon.Deterministic.FP>;
using MotionWarpWindowRuntime = ThirdPersonSimulation.MotionWarpWindowRuntime<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, Photon.Deterministic.FP, ThirdPersonSimulation.FPGameplay.FPActionInstanceReference, ThirdPersonSimulation.QuantumNumerics.QuantumSimulationMath>;
using MotionWarpCalculation = ThirdPersonSimulation.MotionWarpCalculation<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, Photon.Deterministic.FP, ThirdPersonSimulation.QuantumNumerics.QuantumSimulationMath>;
using MotionWarpState = ThirdPersonSimulation.MotionWarpState<Photon.Deterministic.FP, Photon.Deterministic.FPVector3, Photon.Deterministic.FP, ThirdPersonSimulation.FPGameplay.FPActionInstanceReference>;
using SimulationMotionContribution = ThirdPersonSimulation.SimulationMotionContribution<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, ThirdPersonSimulation.QuantumNumerics.QuantumSimulationMath>;
using ResolvedMotionChannel = ThirdPersonSimulation.ResolvedMotionChannel<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, ThirdPersonSimulation.QuantumNumerics.QuantumSimulationMath>;
using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    internal sealed class FPMotionAccumulator : FPOperationModule,
        IFPMotionContributionSink
    {
        readonly FPAbilityExecutionFrame m_Frame;
        readonly FPAbilityExecutionWorkspace m_Workspace;
        readonly List<AbilityTimelineLogicMotionWarp> m_TimelineMotionWarps;
        readonly FPMotionWarpTarget m_MotionWarp;

        public FPMotionAccumulator(
            FPGameplayAbilityExecutionAccess access,
            FPAbilityExecutionFrame frame,
            FPAbilityExecutionWorkspace workspace,
            List<AbilityTimelineLogicMotionWarp> timelineMotionWarps,
            FPActionStateStore actions)
            : base(access)
        {
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            m_Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            m_TimelineMotionWarps = timelineMotionWarps ?? throw new ArgumentNullException(nameof(timelineMotionWarps));
            m_MotionWarp = new FPMotionWarpTarget(access, frame, actions);
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
                    contribution.Displacement.X.RawValue, contribution.Displacement.Y.RawValue, contribution.Displacement.Z.RawValue,
                    contribution.YawDegrees.RawValue, contribution.MovementPlaybackClock,
                    channel: (int)contribution.Channel, blendMode: (int)contribution.BlendMode,
                    priority: contribution.Priority, weightRaw: contribution.Weight.RawValue,
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

    internal sealed class FPMotionWarpTarget : FPOperationModule,
        IAbilityTimelineMotionWarpTarget<ResolvedMotionChannel>,
        IMotionWarpWindowTarget<AbilityTimelineLogicMotionWarp, FP, FPVector2, FPVector3,
            FP, FPActionInstanceReference, MotionWarpState, QuantumSimulationMath>
    {
        readonly FPAbilityExecutionFrame m_Frame;
        readonly FPActionStateStore m_Actions;

        public FPMotionWarpTarget(
            FPGameplayAbilityExecutionAccess access,
            FPAbilityExecutionFrame frame,
            FPActionStateStore actions)
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

        public FPActionInstanceReference ReadAction(in AbilityTimelineLogicMotionWarp warp)
        {
            m_Actions.TryGetEvaluatedInstance(warp.ActionInstanceId, out FPActionInstanceState action);
            return FPActionInstanceReference.FromInstance(action);
        }

        public void ReadTarget(in AbilityTimelineLogicMotionWarp warp, out FPVector3 position, out FP yaw)
        {
            position = new FPVector3(
                warp.Target.X, warp.Target.Y, warp.Target.Z);
            yaw = default(QuantumSimulationMath).Yaw(warp.Target.Yaw);
        }

        public FPVector3 BodyPosition => m_Frame.BodyFacts.Position;
        public FP BodyYaw => m_Frame.BodyFacts.Yaw;

        public void ReadSourceDelta(in AbilityTimelineLogicMotionWarp warp, out FPVector3 displacement, out FP yaw)
        {
            displacement = new FPVector3(
                warp.CurrentPositionX - warp.PreviousPositionX, warp.CurrentPositionY - warp.PreviousPositionY, warp.CurrentPositionZ - warp.PreviousPositionZ);
            yaw = warp.CurrentYawDegrees - warp.PreviousYawDegrees;
        }

        public void ReadSourcePrefix(in AbilityTimelineLogicMotionWarp warp, out FPVector3 displacement, out FP yaw)
        {
            displacement = new FPVector3(
                warp.SourcePrefix.X, warp.SourcePrefix.Y, warp.SourcePrefix.Z);
            yaw = warp.SourcePrefix.Yaw;
        }

        public void ReadProgress(in AbilityTimelineLogicMotionWarp warp, bool previous,
            out FP position, out FP yaw)
        {
            position = previous ? warp.PreviousPositionProgress : warp.CurrentPositionProgress;
            yaw = previous ? warp.PreviousYawProgress : warp.CurrentYawProgress;
        }

        public void ApplyResponse(
            in AbilityTimelineLogicMotionWarp warp,
            in MotionWarpParameters<FP> parameters,
            in MotionWarpState state,
            ref ResolvedMotionChannel channel)
        {
            if (warp.TranslationMode != ProgramMotionWarpTranslationMode.Disabled ||
                warp.YawResponse < Photon.Deterministic.FP._0 ||
                warp.InputYawResponse < Photon.Deterministic.FP._0)
                FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.InvalidState,
                    "Target response requires non-negative response and no translation correction.");
            MotionWarpCalculation.TargetResponse(
                in parameters, state.HasTarget, state.CapturedTargetPosition, state.CapturedTargetYaw,
                warp.YawResponse, warp.InputYawResponse,
                new FPVector2(warp.InputDirectionX, warp.InputDirectionZ),
                warp.CurrentTime - warp.PreviousTime,
                new FPVector3(warp.CurrentPositionX - warp.PreviousPositionX, warp.CurrentPositionY - warp.PreviousPositionY, warp.CurrentPositionZ - warp.PreviousPositionZ),
                m_Frame.BodyFacts.Position, m_Frame.BodyFacts.Yaw,
                channel.MotionCorrectionDisplacement, channel.MotionCorrectionYawDegrees,
                out FPVector3 previousDirectionDelta, out FPVector3 nextDirectionDelta, out FP yawDelta);
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

        public MotionWarpParameters<FP> ReadParameters(in AbilityTimelineLogicMotionWarp warp) =>
            new MotionWarpParameters<FP>(
                warp.TranslationMode, warp.TargetOffsetSpace, warp.RotationMode, warp.RotationMethod, warp.LimitPolicy,
                warp.TargetPlanarOffsetX, warp.TargetPlanarOffsetY,
                warp.TargetYawOffsetDegrees, warp.MaximumPositionCorrection,
                warp.MaximumYawCorrectionDegrees, warp.MaximumYawRateDegreesPerSecond,
                FP.SmallestNonZero);

        public void ResolveTarget(
            in AbilityTimelineLogicMotionWarp warp,
            in MotionWarpParameters<FP> parameters,
            FPVector3 targetPosition,
            FP targetYaw,
            FPVector3 bodyPosition,
            FP bodyYaw,
            out FPVector3 sourceWindowStartPosition,
            out FP sourceWindowStartYaw,
            out FPVector3 resolvedTargetPosition,
            out FP resolvedTargetYaw,
            out ProgramMotionWarpLimitResult limitResult)
        {
            sourceWindowStartPosition = new FPVector3(warp.SourceStartPositionX, warp.SourceStartPositionY, warp.SourceStartPositionZ);
            sourceWindowStartYaw = warp.SourceStartYawDegrees;
            MotionWarpCalculation.ResolveTarget(
                in parameters,
                new FPVector3(warp.SourceEndPositionX - warp.SourceStartPositionX, warp.SourceEndPositionY - warp.SourceStartPositionY, warp.SourceEndPositionZ - warp.SourceStartPositionZ),
                warp.SourceEndYawDegrees - warp.SourceStartYawDegrees, warp.EndTime - warp.StartTime,
                targetPosition, targetYaw, bodyPosition, bodyYaw,
                out resolvedTargetPosition, out resolvedTargetYaw, out limitResult);
        }

        public void EvaluatePose(
            in AbilityTimelineLogicMotionWarp warp,
            in MotionWarpParameters<FP> parameters,
            bool previous,
            FPVector3 startBodyPosition,
            FP startBodyYaw,
            FPVector3 sourceWindowStartPosition,
            FP sourceWindowStartYaw,
            FPVector3 resolvedTargetPosition,
            FP resolvedTargetYaw,
            out FPVector3 warpedPosition,
            out FP warpedYaw)
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
                new FPVector3(sourcePositionX, sourcePositionY, sourcePositionZ) - sourceWindowStartPosition,
                sourceYawDegrees - sourceWindowStartYaw,
                new FPVector3(warp.SourceEndPositionX, warp.SourceEndPositionY, warp.SourceEndPositionZ) - sourceWindowStartPosition,
                warp.SourceEndYawDegrees - sourceWindowStartYaw,
                positionProgress, yawProgress, sampleTime - warp.StartTime,
                startBodyPosition, startBodyYaw, resolvedTargetPosition, resolvedTargetYaw,
                out warpedPosition, out warpedYaw);
        }
    }

    internal sealed class FPLocomotionRuntime : FPOperationModule
    {
        readonly IFPValueInputReader m_Values;
        readonly IFPMotionContributionSink m_Motion;
        readonly FPAbilityExecutionFrame m_Frame;
        readonly LocomotionProfile[] m_Profiles;

        public FPLocomotionRuntime(
            FPGameplayAbilityExecutionAccess access,
            IFPValueInputReader values,
            IFPMotionContributionSink motion,
            FPAbilityExecutionFrame frame)
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
            using FPValueInputLease inputs = m_Values.ReadInputs(cursor, operation);
            AbilityStateValue input = inputs.FindByKind(ProgramStateValueKind.Vector2);
            if (input.Kind != ProgramStateValueKind.Vector2)
                throw new InvalidOperationException($"Locomotion operation '{SourcePath(operation)}' has no Vector2 input.");
            FPVector2 move = LocomotionMotionCalculation.NormalizeInput(input.Vector2);
            var movementPlaybackClock = new CommittedMovementPlaybackClock(
                SourcePath(operation),
                generation,
                m_Frame.Tick,
                committedTicks,
                m_Ability.TickRate);
            ref readonly LocomotionProfile profile = ref m_Profiles[operation.Handle.Value];
            FP delta = profile.TickDelta;
            LocomotionMotionCalculation.Evaluate(in profile, move, m_Frame.BodyFacts.Yaw, committedTicks - 1,
                out FPVector3 displacement, out FP yaw);
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
                FP._1,
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
            FPVector2 move,
            FPVector3 displacement,
            FP yawDegrees,
            FP maximumYawVelocityDegreesPerSecond,
            FP delta,
            ulong generation)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            ref readonly LocomotionProfile profile = ref m_Profiles[operation.Handle.Value];
            int durationTicks = profile.DurationTicks;
            string continuationOwner = string.Empty;
            FPVector2 continuationVelocity = FPVector2.Zero;
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
                    if (speed == null || speed.Kind != ProgramConstantKind.Scalar || speed.Scalar < FP._0)
                        throw new InvalidOperationException($"Locomotion continuation '{Access.Services.SourcePath(continuation.Handle)}' has invalid Move Speed.");
                    continuationOwner = Access.Services.SourcePath(continuation.Handle);
                    continuationVelocity = move * speed.Scalar;
                }
            }
            FPVector2 currentVelocity = delta > FP._0
                ? new FPVector2(displacement.X / delta, displacement.Z / delta)
                : FPVector2.Zero;
            return new CommittedLocomotionPlanarMotionTimeline(
                SourcePath(operation),
                generation,
                m_Frame.Tick,
                m_Ability.TickRate,
                currentVelocity.X.AsFloat,
                currentVelocity.Y.AsFloat,
                (yawDegrees / delta).AsFloat,
                maximumYawVelocityDegreesPerSecond.AsFloat,
                durationTicks,
                continuationOwner,
                continuationVelocity.X.AsFloat,
                continuationVelocity.Y.AsFloat);
        }

        static LocomotionProfile[] BuildProfiles(
            GameplayAbilityExecutionLayout layout,
            FPGameplayAbilityExecutionServices services)
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
                FP moveSpeed = FP._0;
                FP curveDuration = FP._0;
                GameplayAbilityCurve xCurve = null;
                GameplayAbilityCurve zCurve = null;
                int durationTicks = 0;
                if (executionMode == LocomotionInputMotionExecutionMode.Timed)
                {
                    ProgramConstant duration = layout.FindNamedConstant(operation.Handle, OperationNamedConstant.DurationSeconds);
                    if (duration == null || duration.Kind != ProgramConstantKind.Scalar || duration.Scalar <= FP._0)
                        throw new InvalidOperationException($"Locomotion operation '{services.SourcePath(operation.Handle)}' has invalid duration.");
                    durationTicks = FPMath.CeilToInt(duration.Scalar * layout.TickRate);
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
                        durationConstant.Scalar <= FP._0)
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

