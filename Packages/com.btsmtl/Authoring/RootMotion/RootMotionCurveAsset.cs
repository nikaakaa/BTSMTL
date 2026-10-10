using System;
using UnityEngine;

namespace ThirdPersonCharacter.Pipeline.Motion.RootMotion
{
    public enum RootMotionCurveEvaluationMode
    {
        Unspecified = 0,
        FullLocalDelta = 1,
        ForwardDistanceYaw = 2
    }

    [CreateAssetMenu(fileName = "RootMotionCurve", menuName = "3C/Animation/Root Motion Curve")]
    public sealed class RootMotionCurveAsset : ScriptableObject
    {
        [SerializeField] AnimationClip sourceClip;
        [SerializeField] float duration;
        [SerializeField] float sampleRate;
        [SerializeField] RootMotionCurveEvaluationMode evaluationMode;
        [SerializeField] AnimationCurve localPositionX = NewZeroCurve();
        [SerializeField] AnimationCurve localPositionY = NewZeroCurve();
        [SerializeField] AnimationCurve localPositionZ = NewZeroCurve();
        [SerializeField] AnimationCurve forwardDistance = NewZeroCurve();
        [SerializeField] AnimationCurve localYaw = NewZeroCurve();
        [SerializeField] Vector3 totalLocalPosition;
        [SerializeField] float totalForwardDistance;
        [SerializeField] float totalYaw;

        public AnimationClip SourceClip => sourceClip;
        public float Duration => duration;
        public float SampleRate => sampleRate;
        public RootMotionCurveEvaluationMode EvaluationMode => evaluationMode;
        public AnimationCurve LocalPositionX => localPositionX;
        public AnimationCurve LocalPositionY => localPositionY;
        public AnimationCurve LocalPositionZ => localPositionZ;
        public AnimationCurve ForwardDistance => forwardDistance;
        public AnimationCurve LocalYaw => localYaw;
        public Vector3 TotalLocalPosition => totalLocalPosition;
        public float TotalForwardDistance => totalForwardDistance;
        public float TotalYaw => totalYaw;

        public Vector3 EvaluatePosition(float sourceTime)
        {
            float time = Mathf.Clamp(sourceTime, 0f, duration);
            return evaluationMode == RootMotionCurveEvaluationMode.ForwardDistanceYaw
                ? Vector3.forward * Evaluate(forwardDistance, time)
                : new Vector3(
                    Evaluate(localPositionX, time),
                    Evaluate(localPositionY, time),
                    Evaluate(localPositionZ, time));
        }

        public float EvaluateYaw(float sourceTime)
        {
            return Evaluate(localYaw, Mathf.Clamp(sourceTime, 0f, duration));
        }

        public static bool IsValidEvaluationMode(RootMotionCurveEvaluationMode mode)
        {
            return mode == RootMotionCurveEvaluationMode.FullLocalDelta ||
                mode == RootMotionCurveEvaluationMode.ForwardDistanceYaw;
        }

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
            if (!TryValidateEvaluationMode(evaluationMode, out error))
                return false;
            if (duration <= 0f)
            {
                error = "Root Motion 曲线源时长必须大于 0。";
                return false;
            }
            if (!HasCurve(localYaw))
            {
                error = "Root Motion 曲线源缺少 Yaw 曲线。";
                return false;
            }
            if (evaluationMode == RootMotionCurveEvaluationMode.ForwardDistanceYaw)
            {
                if (!HasCurve(forwardDistance))
                {
                    error = "ForwardDistanceYaw 曲线源缺少前向距离曲线。";
                    return false;
                }
            }
            else if (!HasCurve(localPositionX) || !HasCurve(localPositionY) || !HasCurve(localPositionZ))
            {
                error = "FullLocalDelta 曲线源缺少局部位移曲线。";
                return false;
            }
            error = string.Empty;
            return true;
        }

        public void SetBakedData(
            AnimationClip source,
            float bakedDuration,
            float bakedSampleRate,
            RootMotionCurveEvaluationMode bakedEvaluationMode,
            AnimationCurve positionX,
            AnimationCurve positionY,
            AnimationCurve positionZ,
            AnimationCurve bakedForwardDistance,
            AnimationCurve yaw,
            Vector3 totalPosition,
            float totalDistance,
            float totalRotationYaw)
        {
            if (!TryValidateEvaluationMode(bakedEvaluationMode, out string error))
                throw new ArgumentException(error, nameof(bakedEvaluationMode));

            sourceClip = source;
            duration = Mathf.Max(0f, bakedDuration);
            sampleRate = Mathf.Max(0f, bakedSampleRate);
            evaluationMode = bakedEvaluationMode;
            localPositionX = CopyCurve(positionX);
            localPositionY = CopyCurve(positionY);
            localPositionZ = CopyCurve(positionZ);
            forwardDistance = CopyCurve(bakedForwardDistance);
            localYaw = CopyCurve(yaw);
            totalLocalPosition = totalPosition;
            totalForwardDistance = Mathf.Max(0f, totalDistance);
            totalYaw = totalRotationYaw;
        }

        static AnimationCurve CopyCurve(AnimationCurve source)
        {
            if (source == null)
                return NewZeroCurve();
            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode
            };
        }

        static float Evaluate(AnimationCurve curve, float time)
        {
            return curve != null && curve.length != 0 ? curve.Evaluate(time) : 0f;
        }

        static bool HasCurve(AnimationCurve curve)
        {
            return curve != null && curve.length != 0;
        }

        static AnimationCurve NewZeroCurve()
        {
            return new AnimationCurve(new Keyframe(0f, 0f));
        }
    }
}
