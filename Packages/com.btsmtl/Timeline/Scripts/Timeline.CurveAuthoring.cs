using System;
using System.Collections.Generic;
using UnityEngine;

namespace BTSMTL.Timeline
{
    public enum TimelineCurveChannelRole : byte
    {
        Value = 0,
        Weight = 1,
        EaseIn = 2,
        EaseOut = 3
    }

#if UNITY_EDITOR
    public enum TimelineAuthoringContentKind : byte
    {
        SpanClip,
        ContinuousCurve
    }
#endif

    public sealed class TimelineCurveChannelDescriptor
    {
        readonly Func<AnimationCurve> m_DefaultFactory;
        readonly Func<Clip, bool> m_Availability;
        readonly Func<object, AnimationCurve> m_Read;
        readonly Action<object, AnimationCurve> m_Replace;
        readonly Func<object, float> m_SourceDuration;

        public TimelineCurveChannelDescriptor(
            TimelineCurveChannelId channelId,
            Type ownerType,
            string displayName,
            Color color,
            TimelineCurveValueDomain valueDomain,
            Func<AnimationCurve> defaultFactory,
            Func<object, AnimationCurve> read,
            Action<object, AnimationCurve> replace,
            Func<Clip, bool> availability = null,
            TimelineCurveChannelRole role = TimelineCurveChannelRole.Value)
        {
            if (ownerType == null || !typeof(Clip).IsAssignableFrom(ownerType))
                throw new ArgumentException("Timeline curve owner must be a Clip type.", nameof(ownerType));
            ChannelId = channelId;
            Role = role;
            OwnerType = ownerType;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException(nameof(displayName)) : displayName;
            Color = color;
            TimeDomain = TimelineCurveTimeDomain.ClipNormalized;
            ValueDomain = valueDomain;
            m_DefaultFactory = defaultFactory ?? throw new ArgumentNullException(nameof(defaultFactory));
            m_Read = read;
            m_Replace = replace;
            m_Availability = availability;
        }

        public TimelineCurveChannelDescriptor(
            TimelineCurveChannelId channelId,
            Type ownerType,
            string displayName,
            string parameterGroup,
            int componentIndex,
            Color color,
            TimelineCurveValueDomain valueDomain,
            Func<object, AnimationCurve> read,
            Action<object, AnimationCurve> replace,
            Func<object, float> sourceDuration)
        {
            ChannelId = channelId;
            OwnerType = ownerType;
            DisplayName = displayName;
            ParameterGroup = parameterGroup;
            ComponentIndex = componentIndex;
            Color = color;
            TimeDomain = TimelineCurveTimeDomain.SourceSeconds;
            ValueDomain = valueDomain;
            m_Read = read;
            m_Replace = replace;
            m_SourceDuration = sourceDuration;
        }

        public TimelineCurveChannelId ChannelId { get; }
        public TimelineCurveChannelRole Role { get; }
        public Type OwnerType { get; }
        public string DisplayName { get; }
        public string ParameterGroup { get; }
        public int ComponentIndex { get; }
        public Color Color { get; }
        public TimelineCurveTimeDomain TimeDomain { get; }
        public TimelineCurveValueDomain ValueDomain { get; }
        public bool Supports(object owner) => owner != null && OwnerType.IsInstanceOfType(owner) && (m_Availability?.Invoke((Clip)owner) ?? true);
        public AnimationCurve CreateDefaultCurve() => TimelineCurveAuthoring.CopyCurve(m_DefaultFactory());
        public bool IsDefault(AnimationCurve curve) => m_DefaultFactory != null && TimelineCurveAuthoring.AreEquivalent(curve, CreateDefaultCurve());

        public AnimationCurve Read(object owner)
        {
            return TimelineCurveAuthoring.CopyCurve(m_Read(owner));
        }

#if UNITY_EDITOR
        public void Replace(object owner, AnimationCurve curve)
        {
            Validate(owner, curve);
            m_Replace(owner, TimelineCurveAuthoring.CopyCurve(curve));
            if (owner is Clip clip)
                clip.RebindTimeline();
        }
#endif

        public void Validate(object owner, AnimationCurve curve)
        {
            if (TimeDomain == TimelineCurveTimeDomain.SourceSeconds)
                TimelineCurveAuthoring.ValidateSourceSeconds(curve, ValueDomain, m_SourceDuration(owner), ChannelId.Value);
            else
                TimelineCurveAuthoring.ValidateNormalized(curve, ValueDomain, 1);
        }

    }

    public readonly struct TimelineCurveChannelValue
    {
        public TimelineCurveChannelValue(TimelineCurveChannelId channelId, AnimationCurve curve)
        {
            ChannelId = channelId;
            Curve = curve;
        }

        public TimelineCurveChannelId ChannelId { get; }
        public AnimationCurve Curve { get; }
    }

    public static class TimelineCurveChannelCatalog
    {
        public static IReadOnlyList<TimelineCurveChannelDescriptor> Sampled(SampledClip clip)
        {
            string prefix = clip.CurveChannelPrefix;
            Type owner = clip.GetType();
            TimelineCurveValueDomain unit = TimelineCurveValueDomain.Bounded(0f, 1f);
            return new[]
            {
                new TimelineCurveChannelDescriptor(new TimelineCurveChannelId(prefix + ".weight"), owner,
                    "Weight", new Color32(120, 211, 151, 255), unit, One,
                    value => ((SampledClip)value).WeightCurve,
                    (value, curve) => ((SampledClip)value).WeightCurve = curve,
                    role: TimelineCurveChannelRole.Weight),
                new TimelineCurveChannelDescriptor(new TimelineCurveChannelId(prefix + ".ease-in"), owner,
                    "Ease In", new Color32(93, 189, 141, 255), unit, ZeroOne,
                    value => ((SampledClip)value).EaseInCurve,
                    (value, curve) => ((SampledClip)value).EaseInCurve = curve,
                    role: TimelineCurveChannelRole.EaseIn),
                new TimelineCurveChannelDescriptor(new TimelineCurveChannelId(prefix + ".ease-out"), owner,
                    "Ease Out", new Color32(226, 165, 79, 255), unit, ZeroOne,
                    value => ((SampledClip)value).EaseOutCurve,
                    (value, curve) => ((SampledClip)value).EaseOutCurve = curve,
                    role: TimelineCurveChannelRole.EaseOut)
            };
        }

        public static TimelineCurveChannelDescriptor Require(Clip owner, string channelId)
        {
            IReadOnlyList<TimelineCurveChannelDescriptor> descriptors = owner.CurveChannels;
            for (int i = 0; i < descriptors.Count; i++)
                if (string.Equals(descriptors[i].ChannelId.Value, channelId, StringComparison.Ordinal))
                    return descriptors[i];
            throw new InvalidOperationException($"Clip '{owner.ContractKind}' has no curve channel '{channelId}'.");
        }

        static AnimationCurve One() => AnimationCurve.Linear(0f, 1f, 1f, 1f);
        static AnimationCurve ZeroOne() => AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }

    public static class TimelineCurveAuthoring
    {
        public static AnimationCurve Read(Clip owner, TimelineCurveChannelId channelId) =>
            TimelineCurveChannelCatalog.Require(owner, channelId.Value).Read(owner);

#if UNITY_EDITOR
        public static void Replace(Clip owner, TimelineCurveChannelId channelId, AnimationCurve curve) =>
            TimelineCurveChannelCatalog.Require(owner, channelId.Value).Replace(owner, curve);
#endif

        public static void Validate(Clip owner, TimelineCurveChannelId channelId, AnimationCurve curve) =>
            TimelineCurveChannelCatalog.Require(owner, channelId.Value).Validate(owner, curve);

        public static AnimationCurve CopyCurve(AnimationCurve source)
        {
            if (source == null)
                throw new InvalidOperationException("Timeline curve cannot be null.");
            return new AnimationCurve(source.keys) { preWrapMode = source.preWrapMode, postWrapMode = source.postWrapMode };
        }

        public static bool AreEquivalent(AnimationCurve left, AnimationCurve right)
        {
            if (left == null || right == null || left.preWrapMode != right.preWrapMode ||
                left.postWrapMode != right.postWrapMode || left.length != right.length)
                return false;
            for (int i = 0; i < left.length; i++)
                if (!AreEquivalent(left[i], right[i]))
                    return false;
            return true;
        }

        public static bool AreEquivalent(Keyframe left, Keyframe right) => left.time == right.time &&
            left.value == right.value && left.inTangent == right.inTangent && left.outTangent == right.outTangent &&
            left.weightedMode == right.weightedMode && (left.weightedMode == WeightedMode.None ||
            left.inWeight == right.inWeight && left.outWeight == right.outWeight);

        public static ulong Revision(AnimationCurve curve)
        {
            if (curve == null)
                return 0UL;
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                Mix(ref hash, (int)curve.preWrapMode);
                Mix(ref hash, (int)curve.postWrapMode);
                Keyframe[] keys = curve.keys;
                Mix(ref hash, keys.Length);
                for (int i = 0; i < keys.Length; i++)
                {
                    Keyframe key = keys[i];
                    Mix(ref hash, BitConverter.SingleToInt32Bits(key.time));
                    Mix(ref hash, BitConverter.SingleToInt32Bits(key.value));
                    Mix(ref hash, BitConverter.SingleToInt32Bits(key.inTangent));
                    Mix(ref hash, BitConverter.SingleToInt32Bits(key.outTangent));
                    Mix(ref hash, BitConverter.SingleToInt32Bits(key.inWeight));
                    Mix(ref hash, BitConverter.SingleToInt32Bits(key.outWeight));
                    Mix(ref hash, (int)key.weightedMode);
                }
                return hash;
            }
        }

        public static void ValidateNormalized(AnimationCurve curve, TimelineCurveValueDomain domain, int minimumKeys)
        {
            if (curve == null || curve.length < minimumKeys)
                throw new InvalidOperationException($"Timeline curve requires at least {minimumKeys} key(s).");
            Keyframe[] keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                Keyframe key = keys[i];
                if (!IsFinite(key.time) || !IsFinite(key.value) || !IsFinite(key.inWeight) || !IsFinite(key.outWeight) ||
                    key.time < 0f || key.time > 1f || i > 0 && key.time <= keys[i - 1].time)
                    throw new InvalidOperationException($"Timeline curve key {i} has an invalid normalized time or non-finite payload.");
                if (domain.IsBounded && (key.value < domain.Minimum || key.value > domain.Maximum))
                    throw new InvalidOperationException($"Timeline curve key {i} value is outside [{domain.Minimum}, {domain.Maximum}].");
            }
        }

        public static void ValidateSourceSeconds(AnimationCurve curve, TimelineCurveValueDomain domain,
            float duration, string source)
        {
            if (curve == null || curve.length == 0)
                throw new InvalidOperationException($"{source} 缺少源曲线关键帧。");
            Keyframe[] keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                Keyframe key = keys[i];
                if (!float.IsFinite(key.time) || key.time < 0f || key.time > duration ||
                    i > 0 && key.time <= keys[i - 1].time || !float.IsFinite(key.value) ||
                    float.IsNaN(key.inTangent) || float.IsNaN(key.outTangent) ||
                    !float.IsFinite(key.inWeight) || !float.IsFinite(key.outWeight) ||
                    key.inWeight < 0f || key.inWeight > 1f || key.outWeight < 0f || key.outWeight > 1f ||
                    (int)key.weightedMode < 0 || (int)key.weightedMode > (int)WeightedMode.Both)
                    throw new InvalidOperationException($"{source} 的关键帧 {i} 不符合源秒数、有限数值或切线权重合同。");
                RequireSourceValue(key.value, domain, source);
                if (i > 0)
                    RequireSegmentRange(curve, keys[i - 1], key, domain, source);
            }
            RequireSourceValue(curve.Evaluate(0f), domain, source);
            RequireSourceValue(curve.Evaluate(duration), domain, source);
        }

        static void RequireSegmentRange(AnimationCurve curve, Keyframe left, Keyframe right,
            TimelineCurveValueDomain domain, string source)
        {
            if (float.IsInfinity(left.outTangent) || float.IsInfinity(right.inTangent))
                return;
            double span = right.time - left.time;
            double outWeight = (left.weightedMode & WeightedMode.Out) != 0 ? left.outWeight : 1d / 3d;
            double inWeight = (right.weightedMode & WeightedMode.In) != 0 ? right.inWeight : 1d / 3d;
            double p0 = left.value;
            double p1 = p0 + span * left.outTangent * outWeight;
            double p3 = right.value;
            double p2 = p3 - span * right.inTangent * inWeight;
            double a = -p0 + 3d * p1 - 3d * p2 + p3;
            double b = 2d * (p0 - 2d * p1 + p2);
            double c = p1 - p0;
            if (a == 0d)
            {
                if (b != 0d)
                    RequireExtremum(-c / b);
                return;
            }
            double discriminant = b * b - 4d * a * c;
            if (discriminant < 0d)
                return;
            double root = Math.Sqrt(discriminant);
            RequireExtremum((-b - root) / (2d * a));
            RequireExtremum((-b + root) / (2d * a));

            void RequireExtremum(double t)
            {
                if (t <= 0d || t >= 1d)
                    return;
                double u = 1d - t;
                double x = 3d * u * u * t * outWeight + 3d * u * t * t * (1d - inWeight) + t * t * t;
                RequireSourceValue(curve.Evaluate((float)(left.time + span * x)), domain, source);
            }
        }

        static void RequireSourceValue(float value, TimelineCurveValueDomain domain, string source)
        {
            if (!float.IsFinite(value) || domain.IsBounded && (value < domain.Minimum || value > domain.Maximum))
                throw new InvalidOperationException($"{source} 的曲线插值超出正式数值域 {domain.Summary}。");
        }

        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        static void Mix(ref ulong hash, int value)
        {
            hash ^= (uint)value;
            hash *= 1099511628211UL;
        }
    }
}
