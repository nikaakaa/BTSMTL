using System;
using ThirdPersonSimulation.Fixed;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;

namespace ThirdPersonSimulation
{
    public enum CharacterControlMotionEvaluationMode : byte
    {
        FullLocalDelta = 1,
        ForwardDistanceYaw = 2
    }

    public readonly struct CharacterControlMotionCurveKey
    {
        public CharacterControlMotionCurveKey(
            double time,
            double value,
            double inTangent,
            double outTangent,
            double inWeight,
            double outWeight,
            int weightedMode)
        {
            if (!IsFinite(time) || !IsFinite(value) || !IsFinite(inTangent) || !IsFinite(outTangent) ||
                !IsFinite(inWeight) || !IsFinite(outWeight) || weightedMode != 0)
            {
                throw new ArgumentException("Character control motion curve key is invalid.");
            }
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

        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class CharacterControlMotionCurve
    {
        readonly ReadOnlyCollection<CharacterControlMotionCurveKey> m_Keys;

        public CharacterControlMotionCurve(
            int preWrapMode,
            int postWrapMode,
            IEnumerable<CharacterControlMotionCurveKey> keys)
        {
            var copied = keys == null
                ? new List<CharacterControlMotionCurveKey>()
                : new List<CharacterControlMotionCurveKey>(keys);
            if (copied.Count == 0)
                throw new ArgumentException("Character control motion curve requires at least one key.", nameof(keys));
            copied.Sort((left, right) => left.Time.CompareTo(right.Time));
            for (int i = 1; i < copied.Count; i++)
            {
                if (copied[i - 1].Time == copied[i].Time)
                    throw new ArgumentException($"Character control motion curve contains duplicate key time '{copied[i].Time}'.", nameof(keys));
            }
            PreWrapMode = preWrapMode;
            PostWrapMode = postWrapMode;
            m_Keys = copied.AsReadOnly();
            var hashParts = new List<string>
            {
                "character-control-motion-curve/1",
                preWrapMode.ToString(CultureInfo.InvariantCulture),
                postWrapMode.ToString(CultureInfo.InvariantCulture)
            };
            for (int i = 0; i < m_Keys.Count; i++)
            {
                CharacterControlMotionCurveKey key = m_Keys[i];
                hashParts.Add(Format(key.Time));
                hashParts.Add(Format(key.Value));
                hashParts.Add(Format(key.InTangent));
                hashParts.Add(Format(key.OutTangent));
                hashParts.Add(Format(key.InWeight));
                hashParts.Add(Format(key.OutWeight));
                hashParts.Add(key.WeightedMode.ToString(CultureInfo.InvariantCulture));
            }
            ContentHash = StableHash.Compute(hashParts.ToArray());
        }

        public int PreWrapMode { get; }
        public int PostWrapMode { get; }
        public IReadOnlyList<CharacterControlMotionCurveKey> Keys => m_Keys;
        public StableHash ContentHash { get; }

        public double Evaluate(double time)
        {
            if (double.IsNaN(time) || double.IsInfinity(time))
                throw new ArgumentOutOfRangeException(nameof(time));
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
            CharacterControlMotionCurveKey from = m_Keys[low];
            CharacterControlMotionCurveKey to = m_Keys[high];
            double duration = to.Time - from.Time;
            if (duration == 0d)
                return to.Value;
            double t = (time - from.Time) / duration;
            double t2 = t * t;
            double t3 = t2 * t;
            double h00 = 2d * t3 - 3d * t2 + 1d;
            double h10 = t3 - 2d * t2 + t;
            double h01 = -2d * t3 + 3d * t2;
            double h11 = t3 - t2;
            return h00 * from.Value + h10 * duration * from.OutTangent + h01 * to.Value + h11 * duration * to.InTangent;
        }

        static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }

    public readonly struct CharacterControlMotionTimeMapping
    {
        public CharacterControlMotionTimeMapping(
            FixedScalar clipStartTime,
            FixedScalar curveEndTime,
            double sourceStartTime,
            double sourceEndTime)
        {
            if (clipStartTime < FixedScalar.Zero || curveEndTime <= clipStartTime ||
                !IsFinite(sourceStartTime) || !IsFinite(sourceEndTime) || sourceStartTime < 0d || sourceEndTime <= sourceStartTime)
            {
                throw new ArgumentException("Character control motion time mapping is invalid.");
            }
            ClipStartTime = clipStartTime;
            CurveEndTime = curveEndTime;
            SourceStartTime = sourceStartTime;
            SourceEndTime = sourceEndTime;
            ContentHash = StableHash.Compute(
                "character-control-motion-time-mapping/2",
                clipStartTime.Raw.ToString(CultureInfo.InvariantCulture),
                curveEndTime.Raw.ToString(CultureInfo.InvariantCulture),
                Format(sourceStartTime),
                Format(sourceEndTime));
        }

        public FixedScalar ClipStartTime { get; }
        public FixedScalar CurveEndTime { get; }
        public double SourceStartTime { get; }
        public double SourceEndTime { get; }
        public double TimelineDurationSeconds => (CurveEndTime - ClipStartTime).ToDouble();
        public StableHash ContentHash { get; }

        public double SourceTimeAt(double timelineElapsedSeconds)
        {
            if (!IsFinite(timelineElapsedSeconds))
                throw new ArgumentOutOfRangeException(nameof(timelineElapsedSeconds));
            double elapsed = Math.Max(0d, Math.Min(timelineElapsedSeconds, TimelineDurationSeconds));
            return Math.Min(SourceEndTime, SourceStartTime + elapsed);
        }

        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }

    public readonly struct CharacterControlMotionDelta
    {
        public CharacterControlMotionDelta(double x, double y, double z, double yaw)
        {
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double Yaw { get; }
    }

    public sealed class CharacterControlMotionBinding
    {
        public CharacterControlMotionBinding(
            string sourceIdentity,
            string sourceCurveIdentity,
            StableHash sourceRevision,
            CharacterControlMotionEvaluationMode evaluationMode,
            CharacterControlMotionTimeMapping mapping,
            CharacterControlMotionCurve positionX,
            CharacterControlMotionCurve positionY,
            CharacterControlMotionCurve positionZ,
            CharacterControlMotionCurve forwardDistance,
            CharacterControlMotionCurve yaw)
        {
            SourceIdentity = SimulationIdentity.Require(sourceIdentity, nameof(sourceIdentity));
            SourceCurveIdentity = SimulationIdentity.Require(sourceCurveIdentity, nameof(sourceCurveIdentity));
            if (!sourceRevision.IsValid || !Enum.IsDefined(typeof(CharacterControlMotionEvaluationMode), evaluationMode))
                throw new ArgumentException("Character control motion binding identity is incomplete.");
            SourceRevision = sourceRevision;
            EvaluationMode = evaluationMode;
            Mapping = mapping;
            PositionX = positionX ?? throw new ArgumentNullException(nameof(positionX));
            PositionY = positionY ?? throw new ArgumentNullException(nameof(positionY));
            PositionZ = positionZ ?? throw new ArgumentNullException(nameof(positionZ));
            ForwardDistance = forwardDistance ?? throw new ArgumentNullException(nameof(forwardDistance));
            Yaw = yaw ?? throw new ArgumentNullException(nameof(yaw));
            ContentHash = StableHash.Compute(
                "character-control-motion-binding/3",
                SourceIdentity,
                SourceCurveIdentity,
                SourceRevision.Value,
                ((int)EvaluationMode).ToString(CultureInfo.InvariantCulture),
                Mapping.ContentHash.Value,
                PositionX.ContentHash.Value,
                PositionY.ContentHash.Value,
                PositionZ.ContentHash.Value,
                ForwardDistance.ContentHash.Value,
                Yaw.ContentHash.Value);
        }

        public string SourceIdentity { get; }
        public string SourceCurveIdentity { get; }
        public StableHash SourceRevision { get; }
        public CharacterControlMotionEvaluationMode EvaluationMode { get; }
        public CharacterControlMotionTimeMapping Mapping { get; }
        public CharacterControlMotionCurve PositionX { get; }
        public CharacterControlMotionCurve PositionY { get; }
        public CharacterControlMotionCurve PositionZ { get; }
        public CharacterControlMotionCurve ForwardDistance { get; }
        public CharacterControlMotionCurve Yaw { get; }
        public StableHash ContentHash { get; }

        public CharacterControlMotionDelta EvaluateDelta(double previousTimelineSeconds, double currentTimelineSeconds)
        {
            double previous = Mapping.SourceTimeAt(previousTimelineSeconds);
            double current = Mapping.SourceTimeAt(currentTimelineSeconds);
            double yaw = Yaw.Evaluate(current) - Yaw.Evaluate(previous);
            if (EvaluationMode == CharacterControlMotionEvaluationMode.ForwardDistanceYaw)
                return new CharacterControlMotionDelta(0d, 0d, ForwardDistance.Evaluate(current) - ForwardDistance.Evaluate(previous), yaw);
            return new CharacterControlMotionDelta(
                PositionX.Evaluate(current) - PositionX.Evaluate(previous),
                PositionY.Evaluate(current) - PositionY.Evaluate(previous),
                PositionZ.Evaluate(current) - PositionZ.Evaluate(previous),
                yaw);
        }

        public static StableHash ComputeSourceRevision(
            double sourceDuration,
            double sampleRate,
            CharacterControlMotionEvaluationMode evaluationMode,
            CharacterControlMotionCurve positionX,
            CharacterControlMotionCurve positionY,
            CharacterControlMotionCurve positionZ,
            CharacterControlMotionCurve forwardDistance,
            CharacterControlMotionCurve yaw)
        {
            if (double.IsNaN(sourceDuration) || double.IsInfinity(sourceDuration) || sourceDuration <= 0d ||
                double.IsNaN(sampleRate) || double.IsInfinity(sampleRate) ||
                !Enum.IsDefined(typeof(CharacterControlMotionEvaluationMode), evaluationMode))
            {
                throw new ArgumentException("Character control motion source revision inputs are invalid.");
            }
            return StableHash.Compute(
                "character-control-motion-source/3",
                Format(sourceDuration),
                Format(sampleRate),
                ((int)evaluationMode).ToString(CultureInfo.InvariantCulture),
                positionX?.ContentHash.Value ?? throw new ArgumentNullException(nameof(positionX)),
                positionY?.ContentHash.Value ?? throw new ArgumentNullException(nameof(positionY)),
                positionZ?.ContentHash.Value ?? throw new ArgumentNullException(nameof(positionZ)),
                forwardDistance?.ContentHash.Value ?? throw new ArgumentNullException(nameof(forwardDistance)),
                yaw?.ContentHash.Value ?? throw new ArgumentNullException(nameof(yaw)));
        }

        static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }

    public sealed class CharacterControlMotionBindingCatalog
    {
        readonly ReadOnlyCollection<CharacterControlMotionBinding> m_Bindings;
        readonly Dictionary<string, CharacterControlMotionBinding> m_BySource;

        public CharacterControlMotionBindingCatalog(IEnumerable<CharacterControlMotionBinding> bindings)
        {
            var sorted = new List<CharacterControlMotionBinding>(bindings ?? Array.Empty<CharacterControlMotionBinding>());
            sorted.Sort((left, right) => string.CompareOrdinal(left.SourceIdentity, right.SourceIdentity));
            m_BySource = new Dictionary<string, CharacterControlMotionBinding>(StringComparer.Ordinal);
            for (int i = 0; i < sorted.Count; i++)
            {
                CharacterControlMotionBinding binding = sorted[i]
                    ?? throw new ArgumentException("Character control motion binding catalog contains a missing binding.", nameof(bindings));
                if (!m_BySource.TryAdd(binding.SourceIdentity, binding))
                    throw new ArgumentException($"Character control motion source '{binding.SourceIdentity}' is duplicated.", nameof(bindings));
            }
            m_Bindings = sorted.AsReadOnly();
            var hashParts = new List<string> { "character-control-motion-binding-catalog/3" };
            for (int i = 0; i < m_Bindings.Count; i++)
            {
                hashParts.Add(m_Bindings[i].SourceIdentity);
                hashParts.Add(m_Bindings[i].ContentHash.Value);
            }
            ContentHash = StableHash.Compute(hashParts.ToArray());
        }

        public IReadOnlyList<CharacterControlMotionBinding> Bindings => m_Bindings;
        public StableHash ContentHash { get; }

        public CharacterControlMotionBinding Require(string sourceIdentity) =>
            m_BySource.TryGetValue(SimulationIdentity.Require(sourceIdentity, nameof(sourceIdentity)), out CharacterControlMotionBinding binding)
                ? binding
                : throw new InvalidOperationException($"Character control motion source '{sourceIdentity}' is not bound.");

        public void RequireContract(CharacterControlModuleContract contract)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            for (int i = 0; i < contract.Motions.Count; i++)
            {
                CharacterControlMotionDescriptor motion = contract.Motions[i];
                if (motion.DisplacementMode == CharacterControlMotionDisplacementMode.SourceCurve)
                    Require(motion.SourceMotionIdentity);
            }
        }
    }

    public static class CharacterControlMotionBindingCodec
    {
        const uint Magic = 0x4d424343;
        const int Version = 5;
        public const string CodecIdentity = "character-control-motion-bindings/v5";

        public static byte[] Write(CharacterControlMotionBindingCatalog catalog)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            using var writer = new CanonicalWriter();
            writer.WriteUInt32(Magic);
            writer.WriteInt32(Version);
            writer.WriteText(CodecIdentity);
            writer.WriteInt32(catalog.Bindings.Count);
            for (int i = 0; i < catalog.Bindings.Count; i++)
                WriteBinding(writer, catalog.Bindings[i]);
            writer.WriteHash(catalog.ContentHash);
            return writer.ToArray();
        }

        public static CharacterControlMotionBindingCatalog Read(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes ?? throw new ArgumentNullException(nameof(bytes)));
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version ||
                !string.Equals(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), CodecIdentity, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Character control motion binding header is invalid.");
            }
            int count = reader.ReadInt32();
            if (count < 0 || count > 4096)
                throw new InvalidDataException($"Character control motion binding count '{count}' is invalid.");
            var bindings = new CharacterControlMotionBinding[count];
            for (int i = 0; i < count; i++)
            {
                bindings[i] = ReadBinding(reader);
                StableHash expectedBindingHash = reader.ReadHash();
                if (!bindings[i].ContentHash.Equals(expectedBindingHash))
                    throw new InvalidDataException($"Character control motion binding '{bindings[i].SourceIdentity}' hash is invalid.");
            }
            StableHash expectedCatalogHash = reader.ReadHash();
            reader.RequireComplete();
            var catalog = new CharacterControlMotionBindingCatalog(bindings);
            if (!catalog.ContentHash.Equals(expectedCatalogHash))
                throw new InvalidDataException("Character control motion binding catalog hash is invalid.");
            byte[] canonical = Write(catalog);
            if (canonical.Length != bytes.Length)
                throw new InvalidDataException("Character control motion binding catalog is not canonical.");
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != canonical[i])
                    throw new InvalidDataException("Character control motion binding catalog is not canonical.");
            }
            return catalog;
        }

        static void WriteBinding(CanonicalWriter writer, CharacterControlMotionBinding binding)
        {
            writer.WriteText(binding.SourceIdentity);
            writer.WriteText(binding.SourceCurveIdentity);
            writer.WriteText(binding.SourceRevision.Value);
            writer.WriteByte((byte)binding.EvaluationMode);
            WriteMapping(writer, binding.Mapping);
            WriteCurve(writer, binding.PositionX);
            WriteCurve(writer, binding.PositionY);
            WriteCurve(writer, binding.PositionZ);
            WriteCurve(writer, binding.ForwardDistance);
            WriteCurve(writer, binding.Yaw);
            writer.WriteHash(binding.ContentHash);
        }

        static CharacterControlMotionBinding ReadBinding(CanonicalReader reader)
        {
            var sourceIdentity = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            var sourceCurveIdentity = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            var sourceRevision = new StableHash(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            byte evaluationModeValue = reader.ReadByte();
            if (evaluationModeValue < (byte)CharacterControlMotionEvaluationMode.FullLocalDelta ||
                evaluationModeValue > (byte)CharacterControlMotionEvaluationMode.ForwardDistanceYaw)
            {
                throw new InvalidDataException($"Character control motion motion evaluation mode '{evaluationModeValue}' is invalid.");
            }
            var evaluationMode = (CharacterControlMotionEvaluationMode)evaluationModeValue;
            CharacterControlMotionTimeMapping mapping = ReadMapping(reader);
            CharacterControlMotionCurve positionX = ReadCurve(reader);
            CharacterControlMotionCurve positionY = ReadCurve(reader);
            CharacterControlMotionCurve positionZ = ReadCurve(reader);
            CharacterControlMotionCurve forwardDistance = ReadCurve(reader);
            CharacterControlMotionCurve yaw = ReadCurve(reader);
            return new CharacterControlMotionBinding(
                sourceIdentity,
                sourceCurveIdentity,
                sourceRevision,
                evaluationMode,
                mapping,
                positionX,
                positionY,
                positionZ,
                forwardDistance,
                yaw);
        }

        static void WriteMapping(CanonicalWriter writer, CharacterControlMotionTimeMapping mapping)
        {
            writer.WriteInt64(mapping.ClipStartTime.Raw);
            writer.WriteInt64(mapping.CurveEndTime.Raw);
            writer.WriteDouble(mapping.SourceStartTime);
            writer.WriteDouble(mapping.SourceEndTime);
        }

        static CharacterControlMotionTimeMapping ReadMapping(CanonicalReader reader) =>
            new CharacterControlMotionTimeMapping(
                FixedScalar.FromRaw(reader.ReadInt64()),
                FixedScalar.FromRaw(reader.ReadInt64()),
                reader.ReadDouble(),
                reader.ReadDouble());

        static void WriteCurve(CanonicalWriter writer, CharacterControlMotionCurve curve)
        {
            writer.WriteInt32(curve.PreWrapMode);
            writer.WriteInt32(curve.PostWrapMode);
            writer.WriteInt32(curve.Keys.Count);
            for (int i = 0; i < curve.Keys.Count; i++)
            {
                CharacterControlMotionCurveKey key = curve.Keys[i];
                writer.WriteDouble(key.Time);
                writer.WriteDouble(key.Value);
                writer.WriteDouble(key.InTangent);
                writer.WriteDouble(key.OutTangent);
                writer.WriteDouble(key.InWeight);
                writer.WriteDouble(key.OutWeight);
                writer.WriteInt32(key.WeightedMode);
            }
        }

        static CharacterControlMotionCurve ReadCurve(CanonicalReader reader)
        {
            int preWrapMode = reader.ReadInt32();
            int postWrapMode = reader.ReadInt32();
            int count = reader.ReadInt32();
            if (count <= 0 || count > 1000000)
                throw new InvalidDataException($"Character control motion curve key count '{count}' is invalid.");
            var keys = new CharacterControlMotionCurveKey[count];
            for (int i = 0; i < count; i++)
            {
                keys[i] = new CharacterControlMotionCurveKey(
                    reader.ReadDouble(),
                    reader.ReadDouble(),
                    reader.ReadDouble(),
                    reader.ReadDouble(),
                    reader.ReadDouble(),
                    reader.ReadDouble(),
                    reader.ReadInt32());
            }
            return new CharacterControlMotionCurve(preWrapMode, postWrapMode, keys);
        }

    }
}
