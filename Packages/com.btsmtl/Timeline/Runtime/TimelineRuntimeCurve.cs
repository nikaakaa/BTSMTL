using System;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public abstract class TimelineRuntimeCurve<TScalar, TMath, TLowering>
        where TScalar : struct, IComparable<TScalar>, IEquatable<TScalar>
        where TMath : struct, IScalarMath<TScalar>
        where TLowering : struct, IScalarContentLowering<TScalar>
    {
        public abstract TScalar Evaluate(TScalar time);

        public static TimelineRuntimeCurve<TScalar, TMath, TLowering> Constant(TScalar value) => new ConstantCurve(value);

        public static TimelineRuntimeCurve<TScalar, TMath, TLowering> PrepareOptional(TimelineContentCurve curve, TScalar emptyValue) =>
            curve.Keys.Count == 0 ? new ConstantCurve(emptyValue) : new NumericCurve(curve);

        sealed class ConstantCurve : TimelineRuntimeCurve<TScalar, TMath, TLowering>
        {
            readonly TScalar m_Value;
            public ConstantCurve(TScalar value) => m_Value = value;
            public override TScalar Evaluate(TScalar time) => m_Value;
        }

        sealed class NumericCurve : TimelineRuntimeCurve<TScalar, TMath, TLowering>
        {
            readonly GameplayAbilityCurve<TScalar> m_Curve;
            public NumericCurve(TimelineContentCurve source)
            {
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
                m_Curve = new GameplayAbilityCurve<TScalar>(source.PreWrapMode, source.PostWrapMode, keys);
            }

            public override TScalar Evaluate(TScalar time)
            {
                TMath math = default;
                TScalar first = m_Curve.Keys[0].Time;
                TScalar last = m_Curve.Keys[m_Curve.Keys.Count - 1].Time;
                if (!math.Equal(first, last) && (math.GreaterThan(first, time) || math.GreaterThan(time, last)))
                {
                    int wrap = math.GreaterThan(first, time) ? m_Curve.PreWrapMode : m_Curve.PostWrapMode;
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
                return m_Curve.Evaluate<TMath>(time, math.Zero);
            }
        }
    }
}
