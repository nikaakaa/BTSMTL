using System;
using System.Collections.Generic;
using BTSMTL.Timeline.Runtime;
using ThirdPersonSimulation;
using Color = BTSMTL.Authoring.Graph.GraphAuthoringColor;

namespace BTSMTL.Timeline
{
#if UNITY_EDITOR || BTSMTL_AUTHORING
    public enum TimelineAuthoringContentKind : byte
    {
        SpanClip,
        ContinuousCurve
    }
#endif

    public sealed class TimelineCurveChannelDescriptor
    {
        readonly Func<TimelineCurve> m_DefaultFactory;
        readonly Func<Clip, bool> m_Availability;
        readonly Func<object, TimelineCurve> m_Read;
        readonly Action<object, TimelineCurve> m_Replace;
        readonly Func<object, float> m_SourceDuration;

        public TimelineCurveChannelDescriptor(
            TimelineCurveChannelId channelId,
            Type ownerType,
            string displayName,
            Color color,
            TimelineCurveValueDomain valueDomain,
            Func<TimelineCurve> defaultFactory,
            Func<object, TimelineCurve> read,
            Action<object, TimelineCurve> replace,
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
            Func<object, TimelineCurve> read,
            Action<object, TimelineCurve> replace,
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
        public TimelineCurve CreateDefaultCurve() => m_DefaultFactory();
        public bool IsDefault(TimelineCurve curve) => m_DefaultFactory != null && TimelineCurveAuthoring.AreEquivalent(curve, CreateDefaultCurve());

        public TimelineCurve Read(object owner) => m_Read(owner);

#if UNITY_EDITOR || BTSMTL_AUTHORING
        public void Replace(object owner, TimelineCurve curve)
        {
            Validate(owner, curve);
            m_Replace(owner, curve);
#if UNITY_EDITOR
            if (owner is Clip clip)
                clip.RebindTimeline();
#endif
        }
#endif

        public void Validate(object owner, TimelineCurve curve)
        {
            if (TimeDomain == TimelineCurveTimeDomain.SourceSeconds)
                TimelineCurveAuthoring.ValidateSourceSeconds(curve, ValueDomain, m_SourceDuration(owner), ChannelId.Value);
            else
                TimelineCurveAuthoring.ValidateNormalized(curve, ValueDomain, 1);
        }

    }

    public readonly struct TimelineCurveChannelValue
    {
        public TimelineCurveChannelValue(TimelineCurveChannelId channelId, TimelineCurve curve)
        {
            ChannelId = channelId;
            Curve = curve;
        }

        public TimelineCurveChannelId ChannelId { get; }
        public TimelineCurve Curve { get; }
    }

    public static class TimelineCurveChannelCatalog
    {
        public static IReadOnlyList<TimelineCurveChannelDescriptor> Sampled(SampledClip clip)
        {
            string prefix = clip.CurveChannelPrefix;
            Type owner = clip.GetType();
            return new[]
            {
                Sampled(prefix, owner, TimelineSampledCurveChannel.Weight,
                    value => ((SampledClip)value).WeightCurve,
                    (value, curve) => ((SampledClip)value).WeightCurve = curve),
                Sampled(prefix, owner, TimelineSampledCurveChannel.EaseIn,
                    value => ((SampledClip)value).EaseInCurve,
                    (value, curve) => ((SampledClip)value).EaseInCurve = curve),
                Sampled(prefix, owner, TimelineSampledCurveChannel.EaseOut,
                    value => ((SampledClip)value).EaseOutCurve,
                    (value, curve) => ((SampledClip)value).EaseOutCurve = curve)
            };
        }

        static TimelineCurveChannelDescriptor Sampled(string prefix, Type owner, TimelineSampledCurveChannel channel,
            Func<object, TimelineCurve> read, Action<object, TimelineCurve> replace) =>
            new(new TimelineCurveChannelId(prefix + channel.Suffix), owner, channel.DisplayName,
                DisplayColor(channel.Red, channel.Green, channel.Blue), channel.ValueDomain,
                () => Linear(0f, channel.DefaultStart, 1f, channel.DefaultEnd), read, replace,
                role: channel.Role);

        public static Color DisplayColor(byte red, byte green, byte blue)
            => new Color(red / 255f, green / 255f, blue / 255f);

        public static TimelineCurve Linear(float startTime, float startValue, float endTime, float endValue)
        {
            float tangent = (endValue - startValue) / (endTime - startTime);
            return new TimelineCurve(8, 8, new[]
            {
                new TimelineCurveKey(startTime, startValue, tangent, tangent, 0f, 0f, 0, 0),
                new TimelineCurveKey(endTime, endValue, tangent, tangent, 0f, 0f, 0, 0)
            });
        }

        public static TimelineCurveChannelDescriptor Require(Clip owner, string channelId)
        {
            IReadOnlyList<TimelineCurveChannelDescriptor> descriptors = owner.CurveChannels;
            for (int i = 0; i < descriptors.Count; i++)
                if (string.Equals(descriptors[i].ChannelId.Value, channelId, StringComparison.Ordinal))
                    return descriptors[i];
            throw new InvalidOperationException($"Clip '{owner.ContractKind}' has no curve channel '{channelId}'.");
        }
    }

    public static class TimelineCurveAuthoring
    {
        public static TimelineCurve Read(Clip owner, TimelineCurveChannelId channelId) =>
            TimelineCurveChannelCatalog.Require(owner, channelId.Value).Read(owner);

#if UNITY_EDITOR || BTSMTL_AUTHORING
        public static void Replace(Clip owner, TimelineCurveChannelId channelId, TimelineCurve curve) =>
            TimelineCurveChannelCatalog.Require(owner, channelId.Value).Replace(owner, curve);
#endif

        public static void Validate(Clip owner, TimelineCurveChannelId channelId, TimelineCurve curve) =>
            TimelineCurveChannelCatalog.Require(owner, channelId.Value).Validate(owner, curve);

        public static bool AreEquivalent(TimelineCurve left, TimelineCurve right)
        {
            if (left == null || right == null || left.PreWrapMode != right.PreWrapMode ||
                left.PostWrapMode != right.PostWrapMode || left.Keys.Count != right.Keys.Count)
                return false;
            for (int i = 0; i < left.Keys.Count; i++)
                if (!AreEquivalent(left.Keys[i], right.Keys[i]))
                    return false;
            return true;
        }

        public static bool AreEquivalent(TimelineCurveKey left, TimelineCurveKey right) => left.Time == right.Time &&
            left.Value == right.Value && left.InTangent == right.InTangent && left.OutTangent == right.OutTangent &&
            left.WeightedMode == right.WeightedMode && left.TangentMode == right.TangentMode &&
            left.InWeight == right.InWeight && left.OutWeight == right.OutWeight;

        public static ulong Revision(TimelineCurve curve)
        {
            if (curve == null)
                return 0UL;
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                Mix(ref hash, curve.PreWrapMode);
                Mix(ref hash, curve.PostWrapMode);
                IReadOnlyList<TimelineCurveKey> keys = curve.Keys;
                Mix(ref hash, keys.Count);
                for (int i = 0; i < keys.Count; i++)
                {
                    TimelineCurveKey key = keys[i];
                    Mix(ref hash, BitConverter.SingleToInt32Bits(key.Time));
                    Mix(ref hash, BitConverter.SingleToInt32Bits(key.Value));
                    Mix(ref hash, BitConverter.SingleToInt32Bits(key.InTangent));
                    Mix(ref hash, BitConverter.SingleToInt32Bits(key.OutTangent));
                    Mix(ref hash, BitConverter.SingleToInt32Bits(key.InWeight));
                    Mix(ref hash, BitConverter.SingleToInt32Bits(key.OutWeight));
                    Mix(ref hash, key.WeightedMode);
                    Mix(ref hash, key.TangentMode);
                }
                return hash;
            }
        }

        public static void ValidateNormalized(TimelineCurve curve, TimelineCurveValueDomain domain, int minimumKeys)
        {
            if (curve == null || curve.Keys.Count < minimumKeys)
                throw new InvalidOperationException($"Timeline curve requires at least {minimumKeys} key(s).");
            IReadOnlyList<TimelineCurveKey> keys = curve.Keys;
            for (int i = 0; i < keys.Count; i++)
            {
                TimelineCurveKey key = keys[i];
                if (!IsFinite(key.Time) || !IsFinite(key.Value) || !IsFinite(key.InWeight) || !IsFinite(key.OutWeight) ||
                    key.Time < 0f || key.Time > 1f || i > 0 && key.Time <= keys[i - 1].Time)
                    throw new InvalidOperationException($"Timeline curve key {i} has an invalid normalized time or non-finite payload.");
                if (domain.IsBounded && (key.Value < domain.Minimum || key.Value > domain.Maximum))
                    throw new InvalidOperationException($"Timeline curve key {i} value is outside [{domain.Minimum}, {domain.Maximum}].");
            }
        }

        public static void ValidateSourceSeconds(TimelineCurve curve, TimelineCurveValueDomain domain,
            float duration, string source)
        {
            if (curve == null || curve.Keys.Count == 0)
                throw new InvalidOperationException($"{source} 缺少源曲线关键帧。");
            IReadOnlyList<TimelineCurveKey> keys = curve.Keys;
            var builder = new TimelineRuntimeCurveBuilder<Float32Scalar, Float32ScalarContentLowering>();
            TimelineRuntimeCurve<Float32Scalar> compiled = builder.PrepareOptional(
                TimelineContentDiscovery.CaptureSourceCurve(source, curve), Float32Scalar.Zero);
            GameplayAbilityCurveKey<Float32Scalar>[] compiledKeys = builder.ToKeyArray();
            var evaluator = new TimelineRuntimeCurveEvaluator<Float32Scalar, Float32SimulationMath>();
            float Evaluate(float time) => evaluator.Evaluate(in compiled, compiledKeys, Float32Scalar.FromSingle(time)).Value;
            for (int i = 0; i < keys.Count; i++)
            {
                TimelineCurveKey key = keys[i];
                if (!float.IsFinite(key.Time) || key.Time < 0f || key.Time > duration ||
                    i > 0 && key.Time <= keys[i - 1].Time || !float.IsFinite(key.Value) ||
                    float.IsNaN(key.InTangent) || float.IsNaN(key.OutTangent) ||
                    !float.IsFinite(key.InWeight) || !float.IsFinite(key.OutWeight) ||
                    key.InWeight < 0f || key.InWeight > 1f || key.OutWeight < 0f || key.OutWeight > 1f ||
                    key.WeightedMode < 0 || key.WeightedMode > 3)
                    throw new InvalidOperationException($"{source} 的关键帧 {i} 不符合源秒数、有限数值或切线权重合同。");
                RequireSourceValue(key.Value, domain, source);
                if (i > 0)
                    RequireSegmentRange(Evaluate, keys[i - 1], key, domain, source);
            }
            RequireSourceValue(Evaluate(0f), domain, source);
            RequireSourceValue(Evaluate(duration), domain, source);
        }

        static void RequireSegmentRange(Func<float, float> evaluate, TimelineCurveKey left, TimelineCurveKey right,
            TimelineCurveValueDomain domain, string source)
        {
            if (float.IsInfinity(left.OutTangent) || float.IsInfinity(right.InTangent))
                return;
            double span = right.Time - left.Time;
            double outWeight = (left.WeightedMode & 2) != 0 ? left.OutWeight : 1d / 3d;
            double inWeight = (right.WeightedMode & 1) != 0 ? right.InWeight : 1d / 3d;
            double p0 = left.Value;
            double p1 = p0 + span * left.OutTangent * outWeight;
            double p3 = right.Value;
            double p2 = p3 - span * right.InTangent * inWeight;
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
                RequireSourceValue(evaluate((float)(left.Time + span * x)), domain, source);
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
