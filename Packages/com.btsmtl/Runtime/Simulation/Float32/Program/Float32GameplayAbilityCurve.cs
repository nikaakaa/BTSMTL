using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace ThirdPersonSimulation
{
    public readonly struct Float32GameplayAbilityCurveKey
    {
        public Float32GameplayAbilityCurveKey(Float32Scalar time, Float32Scalar value, Float32Scalar inTangent, Float32Scalar outTangent, Float32Scalar inWeight, Float32Scalar outWeight, int weightedMode, bool stepped)
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
        public Float32Scalar Time { get; }
        public Float32Scalar Value { get; }
        public Float32Scalar InTangent { get; }
        public Float32Scalar OutTangent { get; }
        public Float32Scalar InWeight { get; }
        public Float32Scalar OutWeight { get; }
        public int WeightedMode { get; }
        public bool Stepped { get; }
    }

    public sealed class Float32GameplayAbilityCurve
    {
        static readonly Float32Scalar DefaultWeight = Float32Scalar.One / Float32Scalar.FromInt64(3);
        readonly ReadOnlyCollection<Float32GameplayAbilityCurveKey> m_Keys;

        public Float32GameplayAbilityCurve(int preWrapMode, int postWrapMode, IEnumerable<Float32GameplayAbilityCurveKey> keys)
        {
            PreWrapMode = preWrapMode;
            PostWrapMode = postWrapMode;
            var copied = keys == null ? new List<Float32GameplayAbilityCurveKey>() : new List<Float32GameplayAbilityCurveKey>(keys);
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
        public IReadOnlyList<Float32GameplayAbilityCurveKey> Keys => m_Keys;

        public Float32Scalar Evaluate(Float32Scalar time, Float32Scalar fallback)
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
            Float32GameplayAbilityCurveKey from = m_Keys[low];
            if (from.Stepped)
                return from.Value;
            Float32GameplayAbilityCurveKey to = m_Keys[high];
            Float32Scalar duration = to.Time - from.Time;
            if (duration == Float32Scalar.Zero)
                return to.Value;
            Float32Scalar t = (time - from.Time) / duration;
            if ((from.WeightedMode & 2) != 0 || (to.WeightedMode & 1) != 0)
                return EvaluateWeighted(from, to, duration, t);
            Float32Scalar t2 = t * t;
            Float32Scalar t3 = t2 * t;
            Float32Scalar two = Float32Scalar.FromInt64(2);
            Float32Scalar three = Float32Scalar.FromInt64(3);
            Float32Scalar h00 = two * t3 - three * t2 + Float32Scalar.One;
            Float32Scalar h10 = t3 - two * t2 + t;
            Float32Scalar h01 = -two * t3 + three * t2;
            Float32Scalar h11 = t3 - t2;
            return h00 * from.Value + h10 * duration * from.OutTangent + h01 * to.Value + h11 * duration * to.InTangent;
        }
        static Float32Scalar EvaluateWeighted(Float32GameplayAbilityCurveKey from, Float32GameplayAbilityCurveKey to,
            Float32Scalar duration, Float32Scalar normalizedTime)
        {
            Float32Scalar third = DefaultWeight;
            Float32Scalar outgoing = (from.WeightedMode & 2) != 0 ? from.OutWeight : third;
            Float32Scalar incoming = (to.WeightedMode & 1) != 0 ? to.InWeight : third;
            Float32Scalar incomingControl = Float32Scalar.One - incoming;
            Float32Scalar low = Float32Scalar.Zero;
            Float32Scalar high = Float32Scalar.One;
            Float32Scalar two = Float32Scalar.FromInt64(2);
            for (int iteration = 0; iteration < 24; iteration++)
            {
                Float32Scalar middle = (low + high) / two;
                Float32Scalar x = Bezier(Float32Scalar.Zero, outgoing, incomingControl, Float32Scalar.One, middle);
                if (x < normalizedTime)
                    low = middle;
                else
                    high = middle;
            }
            return Bezier(from.Value, from.Value + duration * outgoing * from.OutTangent,
                to.Value - duration * incoming * to.InTangent, to.Value, (low + high) / two);
        }

        static Float32Scalar Bezier(Float32Scalar a, Float32Scalar b, Float32Scalar c, Float32Scalar d, Float32Scalar t)
        {
            Float32Scalar oneMinusT = Float32Scalar.One - t;
            Float32Scalar three = Float32Scalar.FromInt64(3);
            Float32Scalar first = oneMinusT * oneMinusT * oneMinusT * a;
            Float32Scalar weightedOneMinusT = three * oneMinusT;
            return first + weightedOneMinusT * oneMinusT * t * b +
                weightedOneMinusT * t * t * c + t * t * t * d;
        }
    }

    public static class Float32GameplayAbilityCurveCodec
    {
        const uint Magic = 0x56525543;
        const int Version = 2;

        public static byte[] Write(Float32GameplayAbilityCurve curve)
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
                Float32GameplayAbilityCurveKey key = curve.Keys[i];
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

        public static Float32GameplayAbilityCurve Read(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("Gameplay Ability Curve header is invalid.");
            int preWrap = reader.ReadInt32();
            int postWrap = reader.ReadInt32();
            int count = reader.ReadInt32();
            if (count < 0 || count > 1000000)
                throw new InvalidDataException($"Gameplay Ability Curve key count '{count}' is invalid.");
            var keys = new Float32GameplayAbilityCurveKey[count];
            for (int i = 0; i < count; i++)
            {
                keys[i] = new Float32GameplayAbilityCurveKey(
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
            return new Float32GameplayAbilityCurve(preWrap, postWrap, keys);
        }
    }
}
