using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace ThirdPersonSimulation
{
    public readonly struct CharacterCombatCurveKey
    {
        public CharacterCombatCurveKey(
            double time,
            double value,
            double inTangent,
            double outTangent,
            double inWeight,
            double outWeight,
            int weightedMode)
        {
            Time = time;
            Value = value;
            InTangent = inTangent;
            OutTangent = outTangent;
            InWeight = inWeight;
            OutWeight = outWeight;
            WeightedMode = weightedMode;
        }

        public double Time { get; }
        public double Value { get; }
        public double InTangent { get; }
        public double OutTangent { get; }
        public double InWeight { get; }
        public double OutWeight { get; }
        public int WeightedMode { get; }
    }

    public sealed class CharacterCombatCurve
    {
        readonly ReadOnlyCollection<CharacterCombatCurveKey> m_Keys;

        public CharacterCombatCurve(IEnumerable<CharacterCombatCurveKey> keys)
        {
            var values = new List<CharacterCombatCurveKey>(keys ?? throw new ArgumentNullException(nameof(keys)));
            if (values.Count == 0)
                throw new ArgumentException("Character combat curve requires keys.", nameof(keys));
            values.Sort((left, right) => left.Time.CompareTo(right.Time));
            for (int index = 1; index < values.Count; index++)
                if (values[index - 1].Time == values[index].Time)
                    throw new ArgumentException("Character combat curve contains duplicate key time.", nameof(keys));
            m_Keys = values.AsReadOnly();
        }

        public IReadOnlyList<CharacterCombatCurveKey> Keys => m_Keys;

        public double Evaluate(double time)
        {
            if (time <= m_Keys[0].Time)
                return m_Keys[0].Value;
            int last = m_Keys.Count - 1;
            if (time >= m_Keys[last].Time)
                return m_Keys[last].Value;
            int low = 0;
            int high = last;
            while (high - low > 1)
            {
                int middle = low + (high - low) / 2;
                if (m_Keys[middle].Time <= time)
                    low = middle;
                else
                    high = middle;
            }
            CharacterCombatCurveKey from = m_Keys[low];
            CharacterCombatCurveKey to = m_Keys[high];
            double duration = to.Time - from.Time;
            double t = (time - from.Time) / duration;
            if ((from.WeightedMode & 2) != 0 || (to.WeightedMode & 1) != 0)
                return EvaluateWeighted(from, to, duration, t);
            double t2 = t * t;
            double t3 = t2 * t;
            double h00 = 2d * t3 - 3d * t2 + 1d;
            double h10 = t3 - 2d * t2 + t;
            double h01 = -2d * t3 + 3d * t2;
            double h11 = t3 - t2;
            return h00 * from.Value + h10 * duration * from.OutTangent + h01 * to.Value + h11 * duration * to.InTangent;
        }

        static double EvaluateWeighted(CharacterCombatCurveKey from, CharacterCombatCurveKey to,
            double duration, double normalizedTime)
        {
            double outgoing = (from.WeightedMode & 2) != 0 ? from.OutWeight : 1d / 3d;
            double incoming = (to.WeightedMode & 1) != 0 ? to.InWeight : 1d / 3d;
            double low = 0d;
            double high = 1d;
            for (int iteration = 0; iteration < 24; iteration++)
            {
                double middle = (low + high) * 0.5d;
                if (Bezier(0d, outgoing, 1d - incoming, 1d, middle) < normalizedTime)
                    low = middle;
                else
                    high = middle;
            }
            return Bezier(from.Value, from.Value + duration * outgoing * from.OutTangent,
                to.Value - duration * incoming * to.InTangent, to.Value, (low + high) * 0.5d);
        }

        static double Bezier(double a, double b, double c, double d, double t)
        {
            double oneMinusT = 1d - t;
            return oneMinusT * oneMinusT * oneMinusT * a + 3d * oneMinusT * oneMinusT * t * b +
                3d * oneMinusT * t * t * c + t * t * t * d;
        }

        internal void Write(CanonicalWriter writer)
        {
            writer.WriteInt32(m_Keys.Count);
            for (int index = 0; index < m_Keys.Count; index++)
            {
                CharacterCombatCurveKey key = m_Keys[index];
                writer.WriteDouble(key.Time);
                writer.WriteDouble(key.Value);
                writer.WriteDouble(key.InTangent);
                writer.WriteDouble(key.OutTangent);
                writer.WriteDouble(key.InWeight);
                writer.WriteDouble(key.OutWeight);
                writer.WriteInt32(key.WeightedMode);
            }
        }

        internal static CharacterCombatCurve Read(CanonicalReader reader)
        {
            int count = ReadCount(reader, "Timeline curve key", 4096);
            var keys = new CharacterCombatCurveKey[count];
            for (int index = 0; index < count; index++)
                keys[index] = new CharacterCombatCurveKey(
                    reader.ReadDouble(),
                    reader.ReadDouble(),
                    reader.ReadDouble(),
                    reader.ReadDouble(),
                    reader.ReadDouble(),
                    reader.ReadDouble(),
                    reader.ReadInt32());
            return new CharacterCombatCurve(keys);
        }

        static int ReadCount(CanonicalReader reader, string label, int maximum)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > maximum)
                throw new InvalidDataException($"Portable Timeline {label} count '{count}' is invalid.");
            return count;
        }
    }

}
