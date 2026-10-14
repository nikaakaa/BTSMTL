using System;
using Photon.Deterministic;
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
            FP clipStartTime,
            FP curveEndTime,
            double sourceStartTime,
            double sourceEndTime)
        {
            if (clipStartTime < FP._0 || curveEndTime <= clipStartTime ||
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
                clipStartTime.RawValue.ToString(CultureInfo.InvariantCulture),
                curveEndTime.RawValue.ToString(CultureInfo.InvariantCulture),
                Format(sourceStartTime),
                Format(sourceEndTime));
        }

        public FP ClipStartTime { get; }
        public FP CurveEndTime { get; }
        public double SourceStartTime { get; }
        public double SourceEndTime { get; }
        public double TimelineDurationSeconds => (CurveEndTime - ClipStartTime).AsDouble;
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

}
