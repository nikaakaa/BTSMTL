using ThirdPersonSimulation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace ThirdPersonSimulation.Fixed
{
    public readonly struct FixedGameplayAbilityCurveKey
    {
        public FixedGameplayAbilityCurveKey(FixedScalar time, FixedScalar value, FixedScalar inTangent, FixedScalar outTangent, FixedScalar inWeight, FixedScalar outWeight, int weightedMode, bool stepped)
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
        public FixedScalar Time { get; }
        public FixedScalar Value { get; }
        public FixedScalar InTangent { get; }
        public FixedScalar OutTangent { get; }
        public FixedScalar InWeight { get; }
        public FixedScalar OutWeight { get; }
        public int WeightedMode { get; }
        public bool Stepped { get; }
    }

    public sealed class FixedGameplayAbilityCurve
    {
        static readonly FixedScalar DefaultWeight = FixedScalar.One / FixedScalar.FromInt64(3);
        readonly ReadOnlyCollection<FixedGameplayAbilityCurveKey> m_Keys;

        public FixedGameplayAbilityCurve(int preWrapMode, int postWrapMode, IEnumerable<FixedGameplayAbilityCurveKey> keys)
        {
            PreWrapMode = preWrapMode;
            PostWrapMode = postWrapMode;
            var copied = keys == null ? new List<FixedGameplayAbilityCurveKey>() : new List<FixedGameplayAbilityCurveKey>(keys);
            copied.Sort((left, right) => left.Time.CompareTo(right.Time));
            for (int i = 1; i < copied.Count; i++)
            {
                if (copied[i - 1].Time == copied[i].Time)
                    throw new ArgumentException($"Gameplay Ability Curve contains duplicate key time '{copied[i].Time}'.", nameof(keys));
            }
            m_Keys = copied.AsReadOnly();
        }

        public int PreWrapMode { get; }
        public int PostWrapMode { get; }
        public IReadOnlyList<FixedGameplayAbilityCurveKey> Keys => m_Keys;

        public FixedScalar Evaluate(FixedScalar time, FixedScalar fallback)
        {
            if (m_Keys.Count == 0)
                return fallback;
            if (m_Keys.Count == 1 || time <= m_Keys[0].Time)
                return m_Keys[0].Value;
            if (time >= m_Keys[m_Keys.Count - 1].Time)
                return m_Keys[m_Keys.Count - 1].Value;
            int low = 0;
            int high = m_Keys.Count - 1;
            while (high - low > 1)
            {
                int middle = low + (high - low) / 2;
                if (m_Keys[middle].Time <= time)
                    low = middle;
                else
                    high = middle;
            }
            FixedGameplayAbilityCurveKey from = m_Keys[low];
            if (from.Stepped)
                return from.Value;
            FixedGameplayAbilityCurveKey to = m_Keys[high];
            FixedScalar duration = to.Time - from.Time;
            FixedScalar t = (time - from.Time) / duration;
            if ((from.WeightedMode & 2) != 0 || (to.WeightedMode & 1) != 0)
                return EvaluateWeighted(from, to, duration, t);
            FixedScalar t2 = t * t;
            FixedScalar t3 = t2 * t;
            FixedScalar two = FixedScalar.FromInt64(2);
            FixedScalar three = FixedScalar.FromInt64(3);
            FixedScalar h00 = two * t3 - three * t2 + FixedScalar.One;
            FixedScalar h10 = t3 - two * t2 + t;
            FixedScalar h01 = -two * t3 + three * t2;
            FixedScalar h11 = t3 - t2;
            return h00 * from.Value + h10 * duration * from.OutTangent + h01 * to.Value + h11 * duration * to.InTangent;
        }

        static FixedScalar EvaluateWeighted(FixedGameplayAbilityCurveKey from, FixedGameplayAbilityCurveKey to,
            FixedScalar duration, FixedScalar normalizedTime)
        {
            FixedScalar third = DefaultWeight;
            FixedScalar outgoing = (from.WeightedMode & 2) != 0 ? from.OutWeight : third;
            FixedScalar incoming = (to.WeightedMode & 1) != 0 ? to.InWeight : third;
            FixedScalar incomingControl = FixedScalar.One - incoming;
            FixedScalar low = FixedScalar.Zero;
            FixedScalar high = FixedScalar.One;
            FixedScalar two = FixedScalar.FromInt64(2);
            for (int iteration = 0; iteration < 24; iteration++)
            {
                FixedScalar middle = (low + high) / two;
                FixedScalar x = Bezier(FixedScalar.Zero, outgoing, incomingControl, FixedScalar.One, middle);
                if (x < normalizedTime)
                    low = middle;
                else
                    high = middle;
            }
            return Bezier(from.Value, from.Value + duration * outgoing * from.OutTangent,
                to.Value - duration * incoming * to.InTangent, to.Value, (low + high) / two);
        }

        static FixedScalar Bezier(FixedScalar a, FixedScalar b, FixedScalar c, FixedScalar d, FixedScalar t)
        {
            FixedScalar oneMinusT = FixedScalar.One - t;
            FixedScalar three = FixedScalar.FromInt64(3);
            FixedScalar first = oneMinusT * oneMinusT * oneMinusT * a;
            FixedScalar weightedOneMinusT = three * oneMinusT;
            return first + weightedOneMinusT * oneMinusT * t * b +
                weightedOneMinusT * t * t * c + t * t * t * d;
        }
    }

    public static class FixedGameplayAbilityCurveCodec
    {
        const uint Magic = 0x56525543;
        const int Version = 2;

        public static byte[] Write(FixedGameplayAbilityCurve curve)
        {
            if (curve == null)
                throw new ArgumentNullException(nameof(curve));
            using var writer = new CanonicalWriter();
            writer.WriteUInt32(Magic);
            writer.WriteInt32(Version);
            writer.WriteInt32(curve.PreWrapMode);
            writer.WriteInt32(curve.PostWrapMode);
            writer.WriteInt32(curve.Keys.Count);
            for (int i = 0; i < curve.Keys.Count; i++)
            {
                FixedGameplayAbilityCurveKey key = curve.Keys[i];
                writer.WriteScalar(key.Time);
                writer.WriteScalar(key.Value);
                writer.WriteScalar(key.InTangent);
                writer.WriteScalar(key.OutTangent);
                writer.WriteScalar(key.InWeight);
                writer.WriteScalar(key.OutWeight);
                writer.WriteInt32(key.WeightedMode);
                writer.WriteBoolean(key.Stepped);
            }
            return writer.ToArray();
        }

        public static FixedGameplayAbilityCurve Read(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("Gameplay Ability Curve header is invalid.");
            int preWrap = reader.ReadInt32();
            int postWrap = reader.ReadInt32();
            int count = reader.ReadInt32();
            if (count < 0 || count > 1000000)
                throw new InvalidDataException($"Gameplay Ability Curve key count '{count}' is invalid.");
            var keys = new FixedGameplayAbilityCurveKey[count];
            for (int i = 0; i < count; i++)
            {
                keys[i] = new FixedGameplayAbilityCurveKey(
                    reader.ReadScalar(),
                    reader.ReadScalar(),
                    reader.ReadScalar(),
                    reader.ReadScalar(),
                    reader.ReadScalar(),
                    reader.ReadScalar(),
                    reader.ReadInt32(),
                    reader.ReadBoolean());
            }
            reader.RequireComplete();
            return new FixedGameplayAbilityCurve(preWrap, postWrap, keys);
        }
    }
}

