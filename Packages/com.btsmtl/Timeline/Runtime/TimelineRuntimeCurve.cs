using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public enum TimelineRuntimeCurveKind : byte
    {
        Constant,
        Keyed,
    }

    public readonly struct TimelineRuntimeCurve<TScalar> where TScalar : unmanaged
    {
        public TimelineRuntimeCurve(TimelineRuntimeCurveKind kind, TScalar value, int keyStart, int keyCount,
            int preWrapMode, int postWrapMode)
        {
            Kind = kind;
            Value = value;
            KeyStart = keyStart;
            KeyCount = keyCount;
            PreWrapMode = preWrapMode;
            PostWrapMode = postWrapMode;
        }

        public readonly TimelineRuntimeCurveKind Kind;
        public readonly TScalar Value;
        public readonly int KeyStart;
        public readonly int KeyCount;
        public readonly int PreWrapMode;
        public readonly int PostWrapMode;
    }

    public sealed class TimelineRuntimeCurveBuilder<TScalar, TLowering>
        where TScalar : unmanaged, IComparable<TScalar>, IEquatable<TScalar>
        where TLowering : struct, IScalarContentLowering<TScalar>
    {
        readonly List<GameplayAbilityCurveKey<TScalar>> m_Keys = new List<GameplayAbilityCurveKey<TScalar>>();

        public TimelineRuntimeCurve<TScalar> Constant(TScalar value) =>
            new TimelineRuntimeCurve<TScalar>(TimelineRuntimeCurveKind.Constant, value, 0, 0, 0, 0);

        public TimelineRuntimeCurve<TScalar> PrepareOptional(TimelineContentCurve source, TScalar emptyValue)
        {
            if (source.Keys.Count == 0)
                return Constant(emptyValue);
            TLowering lowering = default;
            var keys = new GameplayAbilityCurveKey<TScalar>[source.Keys.Count];
            for (int i = 0; i < keys.Length; i++)
            {
                TimelineContentCurveKey key = source.Keys[i];
                keys[i] = new GameplayAbilityCurveKey<TScalar>(
                    lowering.FromSingle(key.Time), lowering.FromSingle(key.Value),
                    lowering.FromSingle(float.IsInfinity(key.InTangent) ? 0f : key.InTangent),
                    lowering.FromSingle(float.IsInfinity(key.OutTangent) ? 0f : key.OutTangent),
                    lowering.FromSingle(key.InWeight), lowering.FromSingle(key.OutWeight), key.WeightedMode,
                    i + 1 < keys.Length && (float.IsInfinity(key.OutTangent) || float.IsInfinity(source.Keys[i + 1].InTangent)));
            }
            var ordered = new GameplayAbilityCurve<TScalar>(source.PreWrapMode, source.PostWrapMode, keys);
            int start = m_Keys.Count;
            for (int i = 0; i < ordered.Keys.Count; i++)
                m_Keys.Add(ordered.Keys[i]);
            return new TimelineRuntimeCurve<TScalar>(TimelineRuntimeCurveKind.Keyed, default, start, ordered.Keys.Count,
                ordered.PreWrapMode, ordered.PostWrapMode);
        }

        public GameplayAbilityCurveKey<TScalar>[] ToKeyArray() => m_Keys.ToArray();
    }

    // 与 GameplayAbilityCurve 逐运算一致，常量就地计算以免依赖泛型静态字段。
    public readonly struct TimelineRuntimeCurveEvaluator<TScalar, TMath>
        where TScalar : unmanaged
        where TMath : struct, IScalarMath<TScalar>
    {
        public TScalar Evaluate(in TimelineRuntimeCurve<TScalar> curve,
            ReadOnlySpan<GameplayAbilityCurveKey<TScalar>> keyStore, TScalar time)
        {
            if (curve.Kind == TimelineRuntimeCurveKind.Constant)
                return curve.Value;
            ReadOnlySpan<GameplayAbilityCurveKey<TScalar>> keys = keyStore.Slice(curve.KeyStart, curve.KeyCount);
            TMath math = default;
            TScalar first = keys[0].Time;
            TScalar last = keys[keys.Length - 1].Time;
            if (!math.Equal(first, last) && (math.GreaterThan(first, time) || math.GreaterThan(time, last)))
            {
                int wrap = math.GreaterThan(first, time) ? curve.PreWrapMode : curve.PostWrapMode;
                if (wrap == 2 || wrap == 4)
                {
                    TScalar duration = math.Subtract(last, first);
                    TScalar period = wrap == 4 ? math.Multiply(duration, math.FromInt32(2)) : duration;
                    TScalar local = math.Remainder(math.Subtract(time, first), period);
                    if (math.GreaterThan(math.Zero, local)) local = math.Add(local, period);
                    if (wrap == 4 && math.GreaterThan(local, duration)) local = math.Subtract(period, local);
                    time = math.Add(first, local);
                }
            }
            return EvaluateKeys(keys, time);
        }

        static TScalar EvaluateKeys(ReadOnlySpan<GameplayAbilityCurveKey<TScalar>> keys, TScalar time)
        {
            TMath math = default;
            if (keys.Length == 1 || !math.GreaterThan(time, keys[0].Time))
                return keys[0].Value;
            if (!math.GreaterThan(keys[keys.Length - 1].Time, time))
                return keys[keys.Length - 1].Value;
            int low = 0;
            int high = keys.Length - 1;
            while (high - low > 1)
            {
                int middle = low + (high - low) / 2;
                if (!math.GreaterThan(keys[middle].Time, time))
                    low = middle;
                else
                    high = middle;
            }
            GameplayAbilityCurveKey<TScalar> from = keys[low];
            if (from.Stepped)
                return from.Value;
            GameplayAbilityCurveKey<TScalar> to = keys[high];
            TScalar duration = math.Subtract(to.Time, from.Time);
            TScalar t = math.Divide(math.Subtract(time, from.Time), duration);
            TScalar two = math.Add(math.One, math.One);
            TScalar three = math.Add(two, math.One);
            if ((from.WeightedMode & 2) != 0 || (to.WeightedMode & 1) != 0)
                return EvaluateWeighted(in from, in to, duration, t, two, three);
            TScalar t2 = math.Multiply(t, t);
            TScalar t3 = math.Multiply(t2, t);
            TScalar h00 = math.Add(math.Subtract(math.Multiply(two, t3), math.Multiply(three, t2)), math.One);
            TScalar h10 = math.Add(math.Subtract(t3, math.Multiply(two, t2)), t);
            TScalar h01 = math.Add(math.Multiply(math.Negate(two), t3), math.Multiply(three, t2));
            TScalar h11 = math.Subtract(t3, t2);
            return math.Add(math.Add(math.Add(math.Multiply(h00, from.Value),
                math.Multiply(math.Multiply(h10, duration), from.OutTangent)), math.Multiply(h01, to.Value)),
                math.Multiply(math.Multiply(h11, duration), to.InTangent));
        }

        static TScalar EvaluateWeighted(in GameplayAbilityCurveKey<TScalar> from, in GameplayAbilityCurveKey<TScalar> to,
            TScalar duration, TScalar normalizedTime, TScalar two, TScalar three)
        {
            TMath math = default;
            TScalar defaultWeight = math.Divide(math.One, three);
            TScalar outgoing = (from.WeightedMode & 2) != 0 ? from.OutWeight : defaultWeight;
            TScalar incoming = (to.WeightedMode & 1) != 0 ? to.InWeight : defaultWeight;
            TScalar incomingControl = math.Subtract(math.One, incoming);
            TScalar low = math.Zero;
            TScalar high = math.One;
            for (int iteration = 0; iteration < 24; iteration++)
            {
                TScalar middle = math.Divide(math.Add(low, high), two);
                TScalar x = Bezier(math.Zero, outgoing, incomingControl, math.One, middle, three);
                if (math.GreaterThan(normalizedTime, x))
                    low = middle;
                else
                    high = middle;
            }
            return Bezier(from.Value,
                math.Add(from.Value, math.Multiply(math.Multiply(duration, outgoing), from.OutTangent)),
                math.Subtract(to.Value, math.Multiply(math.Multiply(duration, incoming), to.InTangent)),
                to.Value, math.Divide(math.Add(low, high), two), three);
        }

        static TScalar Bezier(TScalar a, TScalar b, TScalar c, TScalar d, TScalar t, TScalar three)
        {
            TMath math = default;
            TScalar oneMinusT = math.Subtract(math.One, t);
            TScalar first = math.Multiply(math.Multiply(math.Multiply(oneMinusT, oneMinusT), oneMinusT), a);
            TScalar weightedOneMinusT = math.Multiply(three, oneMinusT);
            return math.Add(math.Add(math.Add(first,
                math.Multiply(math.Multiply(math.Multiply(weightedOneMinusT, oneMinusT), t), b)),
                math.Multiply(math.Multiply(math.Multiply(weightedOneMinusT, t), t), c)),
                math.Multiply(math.Multiply(math.Multiply(t, t), t), d));
        }
    }
}
