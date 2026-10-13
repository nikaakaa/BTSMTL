using GameplayAbilityCurveCodec = ThirdPersonSimulation.GameplayAbilityCurveCodec<ThirdPersonSimulation.Fixed.FixedScalar, ThirdPersonSimulation.Fixed.FixedSimulationMath, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Fixed.FixedScalar>>;
using GameplayAbilityCurve = ThirdPersonSimulation.GameplayAbilityCurve<ThirdPersonSimulation.Fixed.FixedScalar, ThirdPersonSimulation.Fixed.FixedSimulationMath>;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed class FixedMotionWarpSubmitRuntime : FixedOperationModule
    {
        readonly FixedAbilityExecutionFrame m_Frame;
        readonly FixedValueRuntime m_Values;
        readonly FixedActionStateStore m_Actions;
        readonly IAbilityTimelineMotionSourceReader m_Sources;
        readonly List<AbilityTimelineLogicMotionWarp> m_Candidates;
        readonly Profile[] m_Profiles;

        public FixedMotionWarpSubmitRuntime(FixedGameplayAbilityExecutionAccess access,
            FixedAbilityExecutionFrame frame, FixedValueRuntime values, FixedActionStateStore actions,
            IAbilityTimelineRuntime timelines, List<AbilityTimelineLogicMotionWarp> candidates) : base(access)
        {
            m_Frame = frame;
            m_Values = values;
            m_Actions = actions;
            m_Sources = (IAbilityTimelineMotionSourceReader)timelines;
            m_Candidates = candidates;
            m_Profiles = new Profile[access.Data.Operations.Count];
            for (int index = 0; index < m_Profiles.Length; index++)
                if (access.Data.Operations[index].Code == SimulationOperationCode.MotionWarp)
                    m_Profiles[index] = new Profile(access.Layout, access.Data.Operations[index]);
            m_Candidates.Capacity = checked(access.Layout.MotionWarpOperationIds.Count * (AbilityTimelineRuntimeLimits.MaximumCycleAdvance + 1));
        }

        public void Submit<TTarget>(OperationControlCursor<TTarget> cursor, SimulationOperation operation)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            AbilityTreeClipInvocation invocation = m_Frame.TreeClipInvocation;
            if (invocation.Hook != AbilityTreeClipHook.Root)
                throw new InvalidOperationException("MotionWarp 必须由 TimelineBody 的 Root 提交。");
            Profile profile = m_Profiles[operation.Handle.Value];
            FixedScalar start = invocation.WindowStartTime;
            FixedScalar end = profile.EndTime;
            FixedScalar previous = FixedScalar.Max(start, invocation.PreviousTime);
            FixedScalar current = FixedScalar.Min(end, invocation.Time);
            if (current <= previous)
                return;
            using FixedValueInputLease inputs = m_Values.ReadInputs(cursor, operation);
            m_Actions.TryGetEvaluatedInstance(invocation.ActionInstanceId, out FixedActionInstanceState action);
            AbilityTimelineMotionSourcePose startPose = m_Sources.SampleMotionSource(invocation.TimelineRuntimeHandle, operation.Text0, start);
            AbilityTimelineMotionSourcePose endPose = m_Sources.SampleMotionSource(invocation.TimelineRuntimeHandle, operation.Text0, end);
            AbilityTimelineMotionSourcePose previousPose = m_Sources.SampleMotionSource(invocation.TimelineRuntimeHandle, operation.Text0, previous);
            AbilityTimelineMotionSourcePose currentPose = m_Sources.SampleMotionSource(invocation.TimelineRuntimeHandle, operation.Text0, current);
            FixedScalar previousWindowTime = previous - start;
            FixedScalar windowDuration = end - start;
            FixedScalar previousProgress = previousWindowTime / windowDuration;
            FixedScalar currentProgress = (current - start) / windowDuration;
            FixedVector3 target = inputs[2].Vector3;
            FixedVector2 input = inputs[4].Vector2;
            m_Candidates.Add(new AbilityTimelineLogicMotionWarp(
                m_Sources.ReadMotionSource(invocation.TimelineRuntimeHandle), m_Ability.AbilityId,
                action.ContextId, action.InstanceId, action.PredictionKey, action.SkillExecutionGeneration,
                operation.Handle, invocation.PlaybackGeneration,
                invocation.Cycle, start, end, previous, current,
                startPose.X, startPose.Y, startPose.Z, startPose.Yaw,
                endPose.X, endPose.Y, endPose.Z, endPose.Yaw,
                previousPose.X, previousPose.Y, previousPose.Z, previousPose.Yaw,
                currentPose.X, currentPose.Y, currentPose.Z, currentPose.Yaw,
                profile.PositionProgress(previousProgress), profile.YawProgress(previousProgress),
                profile.PositionProgress(currentProgress), profile.YawProgress(currentProgress),
                profile.Response(currentProgress), input.X, input.Y,
                profile.InputResponse(currentProgress),
                (ProgramMotionWarpTranslationMode)operation.Integer0, (ProgramMotionWarpTargetOffsetSpace)(operation.Flags & 255),
                (ProgramMotionWarpRotationMode)operation.Integer1, (ProgramMotionWarpRotationMethod)(operation.Flags >> 8 & 255),
                profile.OffsetX, profile.OffsetZ, profile.YawOffset, profile.MaxPosition, profile.MaxYaw, profile.MaxYawRate,
                (ProgramMotionWarpLimitPolicy)(operation.Flags >> 16 & 255), inputs[0].Boolean, inputs[1].Boolean,
                new AbilityTimelineMotionSourcePose(target.X, target.Y, target.Z, inputs[3].Scalar),
                operation.Text0, invocation, SimulationExecutionSource.FromSkillOperation(operation.Handle, SourcePath(operation)),
                m_Frame.Trace.ReadInvocationGeneration(operation.Handle), m_Frame.Trace.ReadParentInvocationGeneration(operation.Handle),
                m_Sources.SampleMotionPrefix(invocation.TimelineRuntimeHandle, operation.Text0, invocation.Cycle, previous)));
        }

        sealed class Profile
        {
            readonly GameplayAbilityCurve m_Position;
            readonly GameplayAbilityCurve m_Yaw;
            readonly GameplayAbilityCurve m_Response;
            readonly GameplayAbilityCurve m_InputResponse;

            public Profile(GameplayAbilityExecutionLayout layout, SimulationOperation operation)
            {
                EndTime = layout.FindNamedConstant(operation.Handle, OperationNamedConstant.WindowEndTime).Scalar;
                OffsetX = Scalar(OperationNamedConstant.TargetPlanarOffsetX);
                OffsetZ = Scalar(OperationNamedConstant.TargetPlanarOffsetZ);
                YawOffset = Scalar(OperationNamedConstant.TargetYawOffsetDegrees);
                MaxPosition = Scalar(OperationNamedConstant.MaximumPositionCorrection);
                MaxYaw = Scalar(OperationNamedConstant.MaximumYawCorrectionDegrees);
                MaxYawRate = Scalar(OperationNamedConstant.MaximumYawRateDegreesPerSecond);
                var translation = (ProgramMotionWarpTranslationMode)operation.Integer0;
                var rotation = (ProgramMotionWarpRotationMode)operation.Integer1;
                var method = (ProgramMotionWarpRotationMethod)(operation.Flags >> 8 & 255);
                if (translation is ProgramMotionWarpTranslationMode.SkewToTarget or ProgramMotionWarpTranslationMode.LinearToTarget)
                    m_Position = Curve(OperationNamedConstant.PositionProgressCurve);
                if (rotation != ProgramMotionWarpRotationMode.Disabled && method == ProgramMotionWarpRotationMethod.ProgressCurve)
                    m_Yaw = Curve(OperationNamedConstant.YawProgressCurve);
                if (rotation != ProgramMotionWarpRotationMode.Disabled && method == ProgramMotionWarpRotationMethod.TargetResponse)
                {
                    m_Response = Curve(OperationNamedConstant.YawResponseCurve);
                    m_InputResponse = Curve(OperationNamedConstant.InputYawResponseCurve);
                }

                float Scalar(OperationNamedConstant field) => layout.FindNamedConstant(operation.Handle, field).Scalar.ToSingle();
                GameplayAbilityCurve Curve(OperationNamedConstant field) =>
                    GameplayAbilityCurveCodec.Read(layout.FindNamedConstant(operation.Handle, field).Bytes.ToArray());
            }

            public FixedScalar EndTime { get; }
            public float OffsetX { get; }
            public float OffsetZ { get; }
            public float YawOffset { get; }
            public float MaxPosition { get; }
            public float MaxYaw { get; }
            public float MaxYawRate { get; }
            public FixedScalar PositionProgress(FixedScalar progress) => m_Position == null ? progress : m_Position.Evaluate(progress, FixedScalar.Zero);
            public FixedScalar YawProgress(FixedScalar progress) => m_Yaw == null ? progress : m_Yaw.Evaluate(progress, FixedScalar.Zero);
            public FixedScalar Response(FixedScalar progress) => m_Response == null ? FixedScalar.Zero : m_Response.Evaluate(progress, FixedScalar.Zero);
            public FixedScalar InputResponse(FixedScalar progress) => m_InputResponse == null ? FixedScalar.Zero : m_InputResponse.Evaluate(progress, FixedScalar.Zero);
        }
    }
}
