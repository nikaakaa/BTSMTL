using System;
using BTSMTL.Timeline;
using BTSMTL.Timeline.Runtime;
using ThirdPersonSimulation;

namespace ThirdPersonGameplay.Movement.Timeline
{
    public enum RootMotionCurveEvaluationMode
    {
        Unspecified = 0,
        FullLocalDelta = 1,
        ForwardDistanceYaw = 2
    }

    [ValueStorage(UseConstructor = true)]
    public sealed partial class RootMotionCurveContent
    {
        [ValueStorageIgnore] readonly GameplayAbilityCurveKey<Float32Scalar>[] m_Keys;
        [ValueStorageIgnore] readonly TimelineRuntimeCurve<Float32Scalar> m_PositionX;
        [ValueStorageIgnore] readonly TimelineRuntimeCurve<Float32Scalar> m_PositionY;
        [ValueStorageIgnore] readonly TimelineRuntimeCurve<Float32Scalar> m_PositionZ;
        [ValueStorageIgnore] readonly TimelineRuntimeCurve<Float32Scalar> m_ForwardDistance;
        [ValueStorageIgnore] readonly TimelineRuntimeCurve<Float32Scalar> m_Yaw;

        public RootMotionCurveContent(string authoringId, string contentRevision, string displayName,
            string sourceAnimationIdentity, string sourceAnimationRevision, bool sourceLooping,
            float duration, float sampleRate, RootMotionCurveEvaluationMode evaluationMode,
            TimelineContentCurve localPositionX, TimelineContentCurve localPositionY,
            TimelineContentCurve localPositionZ, TimelineContentCurve forwardDistance,
            TimelineContentCurve localYaw, Float32Vector3 totalLocalPosition,
            float totalForwardDistance, float totalYaw, string authoredSource)
        {
            AuthoringId = authoringId;
            ContentRevision = contentRevision;
            DisplayName = displayName;
            SourceAnimationIdentity = sourceAnimationIdentity;
            SourceAnimationRevision = sourceAnimationRevision;
            SourceLooping = sourceLooping;
            Duration = duration;
            SampleRate = sampleRate;
            EvaluationMode = evaluationMode;
            LocalPositionX = localPositionX;
            LocalPositionY = localPositionY;
            LocalPositionZ = localPositionZ;
            ForwardDistance = forwardDistance;
            LocalYaw = localYaw;
            TotalLocalPosition = totalLocalPosition;
            TotalForwardDistance = totalForwardDistance;
            TotalYaw = totalYaw;
            AuthoredSource = authoredSource;
            var builder = new TimelineRuntimeCurveBuilder<Float32Scalar, Float32ScalarContentLowering>();
            m_PositionX = builder.PrepareOptional(localPositionX, Float32Scalar.Zero);
            m_PositionY = builder.PrepareOptional(localPositionY, Float32Scalar.Zero);
            m_PositionZ = builder.PrepareOptional(localPositionZ, Float32Scalar.Zero);
            m_ForwardDistance = builder.PrepareOptional(forwardDistance, Float32Scalar.Zero);
            m_Yaw = builder.PrepareOptional(localYaw, Float32Scalar.Zero);
            m_Keys = builder.ToKeyArray();
        }

        public string AuthoringId { get; }
        public string ContentRevision { get; }
        public string DisplayName { get; }
        public string SourceAnimationIdentity { get; }
        public string SourceAnimationRevision { get; }
        public bool SourceLooping { get; }
        public float Duration { get; }
        public float SampleRate { get; }
        public RootMotionCurveEvaluationMode EvaluationMode { get; }
        public TimelineContentCurve LocalPositionX { get; }
        public TimelineContentCurve LocalPositionY { get; }
        public TimelineContentCurve LocalPositionZ { get; }
        public TimelineContentCurve ForwardDistance { get; }
        public TimelineContentCurve LocalYaw { get; }
        public Float32Vector3 TotalLocalPosition { get; }
        public float TotalForwardDistance { get; }
        public float TotalYaw { get; }
        public string AuthoredSource { get; }

        public Float32Vector3 EvaluatePosition(float sourceTime)
        {
            Float32Scalar time = SourceTime(sourceTime);
            return EvaluationMode == RootMotionCurveEvaluationMode.ForwardDistanceYaw
                ? new Float32Vector3(Float32Scalar.Zero, Float32Scalar.Zero, Evaluate(in m_ForwardDistance, time))
                : new Float32Vector3(Evaluate(in m_PositionX, time), Evaluate(in m_PositionY, time),
                    Evaluate(in m_PositionZ, time));
        }

        public float EvaluateYaw(float sourceTime) => Evaluate(in m_Yaw, SourceTime(sourceTime)).ToSingle();

        Float32Scalar SourceTime(float sourceTime) =>
            Float32Scalar.FromSingle(Math.Min(Math.Max(sourceTime, 0f), Duration));

        Float32Scalar Evaluate(in TimelineRuntimeCurve<Float32Scalar> curve, Float32Scalar time) =>
            default(TimelineRuntimeCurveEvaluator<Float32Scalar, Float32SimulationMath>)
                .Evaluate(in curve, m_Keys, time);

        public static bool IsValidEvaluationMode(RootMotionCurveEvaluationMode mode) =>
            mode == RootMotionCurveEvaluationMode.FullLocalDelta ||
            mode == RootMotionCurveEvaluationMode.ForwardDistanceYaw;

        public static bool TryValidateEvaluationMode(RootMotionCurveEvaluationMode mode, out string error)
        {
            switch (mode)
            {
                case RootMotionCurveEvaluationMode.FullLocalDelta:
                case RootMotionCurveEvaluationMode.ForwardDistanceYaw:
                    error = string.Empty;
                    return true;
                case RootMotionCurveEvaluationMode.Unspecified:
                    error = "Root Motion 曲线未指定求值模式。";
                    return false;
                default:
                    error = $"Root Motion 曲线包含未知求值模式值：{(int)mode}。";
                    return false;
            }
        }

        public bool TryValidate(out string error)
        {
            if (!TryValidateEvaluationMode(EvaluationMode, out error))
                return false;
            if (Duration <= 0f)
            {
                error = "Root Motion 曲线源时长必须大于 0。";
                return false;
            }
            if (LocalYaw.Keys.Count == 0)
            {
                error = "Root Motion 曲线源缺少 Yaw 曲线。";
                return false;
            }
            if (EvaluationMode == RootMotionCurveEvaluationMode.ForwardDistanceYaw)
            {
                if (ForwardDistance.Keys.Count == 0)
                {
                    error = "ForwardDistanceYaw 曲线源缺少前向距离曲线。";
                    return false;
                }
            }
            else if (LocalPositionX.Keys.Count == 0 || LocalPositionY.Keys.Count == 0 || LocalPositionZ.Keys.Count == 0)
            {
                error = "FullLocalDelta 曲线源缺少局部位移曲线。";
                return false;
            }
            error = string.Empty;
            return true;
        }
    }
}
