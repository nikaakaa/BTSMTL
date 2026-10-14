using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

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

    public sealed class GameplayAbilityCurve<TScalar, TMath>
        where TScalar : struct, IComparable<TScalar>, IEquatable<TScalar>
        where TMath : struct, IScalarMath<TScalar>
    {
        static readonly TScalar Two = default(TMath).Add(default(TMath).One, default(TMath).One);
        static readonly TScalar Three = default(TMath).Add(Two, default(TMath).One);
        static readonly TScalar DefaultWeight = default(TMath).Divide(default(TMath).One, Three);
        readonly ReadOnlyCollection<GameplayAbilityCurveKey<TScalar>> m_Keys;

        public GameplayAbilityCurve(int preWrapMode, int postWrapMode, IEnumerable<GameplayAbilityCurveKey<TScalar>> keys)
        {
            PreWrapMode = preWrapMode;
            PostWrapMode = postWrapMode;
            var copied = keys == null ? new List<GameplayAbilityCurveKey<TScalar>>() : new List<GameplayAbilityCurveKey<TScalar>>(keys);
            copied.Sort((left, right) => left.Time.CompareTo(right.Time));
            for (int i = 1; i < copied.Count; i++)
            {
                if (copied[i - 1].Time.Equals(copied[i].Time))
                    throw new ArgumentException($"Gameplay Ability Curve contains duplicate key time '{copied[i].Time}'.", nameof(keys));
            }
            m_Keys = copied.AsReadOnly();
        }

        public int PreWrapMode { get; }
        public int PostWrapMode { get; }
        public IReadOnlyList<GameplayAbilityCurveKey<TScalar>> Keys => m_Keys;

        public TScalar Evaluate(TScalar time, TScalar emptyValue)
        {
            if (m_Keys.Count == 0)
                return emptyValue;
            TMath math = default;
            if (m_Keys.Count == 1 || !math.GreaterThan(time, m_Keys[0].Time))
                return m_Keys[0].Value;
            if (!math.GreaterThan(m_Keys[m_Keys.Count - 1].Time, time))
                return m_Keys[m_Keys.Count - 1].Value;
            int low = 0;
            int high = m_Keys.Count - 1;
            while (high - low > 1)
            {
                int middle = low + (high - low) / 2;
                if (!math.GreaterThan(m_Keys[middle].Time, time))
                    low = middle;
                else
                    high = middle;
            }
            GameplayAbilityCurveKey<TScalar> from = m_Keys[low];
            if (from.Stepped)
                return from.Value;
            GameplayAbilityCurveKey<TScalar> to = m_Keys[high];
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

    public static class GameplayAbilityCurveCodec<TScalar, TMath, TScalarCodec>
        where TScalar : struct, IComparable<TScalar>, IEquatable<TScalar>
        where TMath : struct, IScalarMath<TScalar>
        where TScalarCodec : struct, ICanonicalLeafCodec<TScalar>
    {
        const uint Magic = 0x56525543;
        const int Version = 2;

        public static byte[] Write(GameplayAbilityCurve<TScalar, TMath> curve)
        {
            using var writer = new CanonicalWriter();
            writer.WriteUInt32(Magic);
            writer.WriteInt32(Version);
            writer.WriteInt32(curve.PreWrapMode);
            writer.WriteInt32(curve.PostWrapMode);
            writer.WriteInt32(curve.Keys.Count);
            TScalarCodec codec = default;
            for (int i = 0; i < curve.Keys.Count; i++)
            {
                GameplayAbilityCurveKey<TScalar> key = curve.Keys[i];
                codec.Write(writer, key.Time);
                codec.Write(writer, key.Value);
                codec.Write(writer, key.InTangent);
                codec.Write(writer, key.OutTangent);
                codec.Write(writer, key.InWeight);
                codec.Write(writer, key.OutWeight);
                writer.WriteInt32(key.WeightedMode);
                writer.WriteBoolean(key.Stepped);
            }
            return writer.ToArray();
        }

        public static GameplayAbilityCurve<TScalar, TMath> Read(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("Gameplay Ability Curve header is invalid.");
            int preWrap = reader.ReadInt32();
            int postWrap = reader.ReadInt32();
            int count = reader.ReadInt32();
            if (count < 0 || count > 1000000)
                throw new InvalidDataException($"Gameplay Ability Curve key count '{count}' is invalid.");
            var keys = new GameplayAbilityCurveKey<TScalar>[count];
            TScalarCodec codec = default;
            for (int i = 0; i < count; i++)
            {
                keys[i] = new GameplayAbilityCurveKey<TScalar>(
                    codec.Read(reader), codec.Read(reader), codec.Read(reader),
                    codec.Read(reader), codec.Read(reader), codec.Read(reader),
                    reader.ReadInt32(), reader.ReadBoolean());
            }
            reader.RequireComplete();
            return new GameplayAbilityCurve<TScalar, TMath>(preWrap, postWrap, keys);
        }
    }
}
