using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public readonly struct GameplayAbilityCurveKey<TScalar> where TScalar : struct
    {
        public GameplayAbilityCurveKey(TScalar time, TScalar value, TScalar inTangent, TScalar outTangent,
            TScalar inWeight, TScalar outWeight, int weightedMode, bool stepped)
        {
            Time = time;
            Value = value;
            InTangent = inTangent;
            OutTangent = outTangent;
            InWeight = inWeight;
            OutWeight = outWeight;
            WeightedMode = weightedMode;
            Stepped = stepped;
        }

        public TScalar Time { get; }
        public TScalar Value { get; }
        public TScalar InTangent { get; }
        public TScalar OutTangent { get; }
        public TScalar InWeight { get; }
        public TScalar OutWeight { get; }
        public int WeightedMode { get; }
        public bool Stepped { get; }
    }

    [ValueStorage]
    public sealed partial class GameplayAbilityCurve<TScalar>
        where TScalar : struct, IComparable<TScalar>, IEquatable<TScalar>
    {
        readonly ReadOnlyCollection<GameplayAbilityCurveKey<TScalar>> m_Keys;

        public GameplayAbilityCurve(int preWrapMode, int postWrapMode, IEnumerable<GameplayAbilityCurveKey<TScalar>> keys)
        {
            PreWrapMode = preWrapMode;
            PostWrapMode = postWrapMode;
            var copied = keys == null ? new List<GameplayAbilityCurveKey<TScalar>>() : new List<GameplayAbilityCurveKey<TScalar>>(keys);
            copied.Sort((left, right) => left.Time.CompareTo(right.Time));
            m_Keys = copied.AsReadOnly();
            RequireKeyOrder();
        }

        [ValueValidation]
        void RequireKeyOrder()
        {
            for (int i = 1; i < m_Keys.Count; i++)
                if (m_Keys[i - 1].Time.CompareTo(m_Keys[i].Time) >= 0)
                    throw new ArgumentException($"Gameplay Ability Curve key times must increase at '{m_Keys[i].Time}'.");
        }

        public int PreWrapMode { get; }
        public int PostWrapMode { get; }
        public IReadOnlyList<GameplayAbilityCurveKey<TScalar>> Keys => m_Keys;

        public TScalar Evaluate<TMath>(TScalar time, TScalar emptyValue)
            where TMath : struct, IScalarMath<TScalar> => Calculation<TMath>.Evaluate(m_Keys, time, emptyValue);

        static class Calculation<TMath> where TMath : struct, IScalarMath<TScalar>
        {
            static readonly TScalar Two = default(TMath).Add(default(TMath).One, default(TMath).One);
            static readonly TScalar Three = default(TMath).Add(Two, default(TMath).One);
            static readonly TScalar DefaultWeight = default(TMath).Divide(default(TMath).One, Three);

            public static TScalar Evaluate(IReadOnlyList<GameplayAbilityCurveKey<TScalar>> keys, TScalar time, TScalar emptyValue)
            {
                if (keys.Count == 0)
                    return emptyValue;
                TMath math = default;
                if (keys.Count == 1 || !math.GreaterThan(time, keys[0].Time))
                    return keys[0].Value;
                if (!math.GreaterThan(keys[keys.Count - 1].Time, time))
                    return keys[keys.Count - 1].Value;
                int low = 0;
                int high = keys.Count - 1;
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
                if ((from.WeightedMode & 2) != 0 || (to.WeightedMode & 1) != 0)
                    return EvaluateWeighted(in from, in to, duration, t);
                TScalar t2 = math.Multiply(t, t);
                TScalar t3 = math.Multiply(t2, t);
                TScalar h00 = math.Add(math.Subtract(math.Multiply(Two, t3), math.Multiply(Three, t2)), math.One);
                TScalar h10 = math.Add(math.Subtract(t3, math.Multiply(Two, t2)), t);
                TScalar h01 = math.Add(math.Multiply(math.Negate(Two), t3), math.Multiply(Three, t2));
                TScalar h11 = math.Subtract(t3, t2);
                return math.Add(math.Add(math.Add(math.Multiply(h00, from.Value),
                    math.Multiply(math.Multiply(h10, duration), from.OutTangent)), math.Multiply(h01, to.Value)),
                    math.Multiply(math.Multiply(h11, duration), to.InTangent));
            }

            static TScalar EvaluateWeighted(in GameplayAbilityCurveKey<TScalar> from, in GameplayAbilityCurveKey<TScalar> to,
                TScalar duration, TScalar normalizedTime)
            {
                TMath math = default;
                TScalar outgoing = (from.WeightedMode & 2) != 0 ? from.OutWeight : DefaultWeight;
                TScalar incoming = (to.WeightedMode & 1) != 0 ? to.InWeight : DefaultWeight;
                TScalar incomingControl = math.Subtract(math.One, incoming);
                TScalar low = math.Zero;
                TScalar high = math.One;
                for (int iteration = 0; iteration < 24; iteration++)
                {
                    TScalar middle = math.Divide(math.Add(low, high), Two);
                    TScalar x = Bezier(math.Zero, outgoing, incomingControl, math.One, middle);
                    if (math.GreaterThan(normalizedTime, x))
                        low = middle;
                    else
                        high = middle;
                }
                return Bezier(from.Value,
                    math.Add(from.Value, math.Multiply(math.Multiply(duration, outgoing), from.OutTangent)),
                    math.Subtract(to.Value, math.Multiply(math.Multiply(duration, incoming), to.InTangent)),
                    to.Value, math.Divide(math.Add(low, high), Two));
            }

            static TScalar Bezier(TScalar a, TScalar b, TScalar c, TScalar d, TScalar t)
            {
                TMath math = default;
                TScalar oneMinusT = math.Subtract(math.One, t);
                TScalar first = math.Multiply(math.Multiply(math.Multiply(oneMinusT, oneMinusT), oneMinusT), a);
                TScalar weightedOneMinusT = math.Multiply(Three, oneMinusT);
                return math.Add(math.Add(math.Add(first,
                    math.Multiply(math.Multiply(math.Multiply(weightedOneMinusT, oneMinusT), t), b)),
                    math.Multiply(math.Multiply(math.Multiply(weightedOneMinusT, t), t), c)),
                    math.Multiply(math.Multiply(math.Multiply(t, t), t), d));
            }
        }
    }

}
