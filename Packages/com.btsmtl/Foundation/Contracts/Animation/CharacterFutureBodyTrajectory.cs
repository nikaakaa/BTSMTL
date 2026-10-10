using System;

namespace ThirdPersonSimulation
{
    public readonly struct CharacterFutureBodyTranslationRequest
    {
        public CharacterFutureBodyTranslationRequest(
            ActorId actorId,
            float durationSeconds,
            float currentVelocityX,
            float currentVelocityZ,
            float continuationVelocityX,
            float continuationVelocityZ,
            float currentSegmentRemainingSeconds,
            bool hasContinuation,
            float sampleTime0,
            float sampleTime1,
            float sampleTime2,
            float sampleTime3)
        {
            if (!actorId.IsValid || !float.IsFinite(durationSeconds) || durationSeconds <= 0f ||
                !float.IsFinite(currentVelocityX) || !float.IsFinite(currentVelocityZ) ||
                !float.IsFinite(continuationVelocityX) || !float.IsFinite(continuationVelocityZ) ||
                (!float.IsFinite(currentSegmentRemainingSeconds) &&
                 !float.IsPositiveInfinity(currentSegmentRemainingSeconds)) ||
                currentSegmentRemainingSeconds < 0f ||
                !ValidSampleTime(sampleTime0, durationSeconds) ||
                !ValidSampleTime(sampleTime1, durationSeconds) ||
                !ValidSampleTime(sampleTime2, durationSeconds) ||
                !ValidSampleTime(sampleTime3, durationSeconds) ||
                !ContainsDuration(
                    durationSeconds,
                    sampleTime0,
                    sampleTime1,
                    sampleTime2,
                    sampleTime3))
            {
                throw new ArgumentException("Future Body Translation request is invalid.");
            }
            ActorId = actorId;
            DurationSeconds = durationSeconds;
            CurrentVelocityX = currentVelocityX;
            CurrentVelocityZ = currentVelocityZ;
            ContinuationVelocityX = continuationVelocityX;
            ContinuationVelocityZ = continuationVelocityZ;
            CurrentSegmentRemainingSeconds = currentSegmentRemainingSeconds;
            HasContinuation = hasContinuation;
            SampleTime0 = sampleTime0;
            SampleTime1 = sampleTime1;
            SampleTime2 = sampleTime2;
            SampleTime3 = sampleTime3;
        }

        public ActorId ActorId { get; }
        public float DurationSeconds { get; }
        public float CurrentVelocityX { get; }
        public float CurrentVelocityZ { get; }
        public float ContinuationVelocityX { get; }
        public float ContinuationVelocityZ { get; }
        public float CurrentSegmentRemainingSeconds { get; }
        public bool HasContinuation { get; }

        internal float SampleTime0 { get; }
        internal float SampleTime1 { get; }
        internal float SampleTime2 { get; }
        internal float SampleTime3 { get; }

        internal float SampleTimeAt(int index) => index switch
        {
            0 => SampleTime0,
            1 => SampleTime1,
            2 => SampleTime2,
            3 => SampleTime3,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        static bool ValidSampleTime(float value, float duration) =>
            float.IsFinite(value) && value >= 0f && value <= duration + 0.0001f;

        static bool ContainsDuration(
            float duration,
            float sampleTime0,
            float sampleTime1,
            float sampleTime2,
            float sampleTime3) =>
            Math.Abs(sampleTime0 - duration) <= 0.0001f ||
            Math.Abs(sampleTime1 - duration) <= 0.0001f ||
            Math.Abs(sampleTime2 - duration) <= 0.0001f ||
            Math.Abs(sampleTime3 - duration) <= 0.0001f;
    }

    public readonly struct CharacterFutureBodyTranslationSample
    {
        public CharacterFutureBodyTranslationSample(
            float elapsedSeconds,
            float relativePositionX,
            float relativePositionY,
            float relativePositionZ,
            float velocityX,
            float velocityY,
            float velocityZ)
        {
            if (!float.IsFinite(elapsedSeconds) || elapsedSeconds < 0f ||
                !float.IsFinite(relativePositionX) || !float.IsFinite(relativePositionY) ||
                !float.IsFinite(relativePositionZ) ||
                !float.IsFinite(velocityX) || !float.IsFinite(velocityY) ||
                !float.IsFinite(velocityZ))
            {
                throw new ArgumentException("Future Body Translation sample is invalid.");
            }
            ElapsedSeconds = elapsedSeconds;
            RelativePositionX = relativePositionX;
            RelativePositionY = relativePositionY;
            RelativePositionZ = relativePositionZ;
            VelocityX = velocityX;
            VelocityY = velocityY;
            VelocityZ = velocityZ;
        }

        public float ElapsedSeconds { get; }
        public float RelativePositionX { get; }
        public float RelativePositionY { get; }
        public float RelativePositionZ { get; }
        public float VelocityX { get; }
        public float VelocityY { get; }
        public float VelocityZ { get; }
    }

    public sealed class CharacterFutureBodyTranslation
    {
        public const int MaximumSampleCount = 5;
        readonly CharacterFutureBodyTranslationSample[] m_Samples;
        string m_SourceIdentity = string.Empty;
        int m_SampleCount;

        public CharacterFutureBodyTranslation()
        {
            m_Samples = new CharacterFutureBodyTranslationSample[MaximumSampleCount];
        }

        public string SourceIdentity => m_SourceIdentity;
        public int SampleCount => m_SampleCount;
        public bool IsAvailable => m_SampleCount >= 2;
        public float DurationSeconds => IsAvailable
            ? m_Samples[m_SampleCount - 1].ElapsedSeconds
            : 0f;

        public void Set(
            string sourceIdentity,
            ReadOnlySpan<CharacterFutureBodyTranslationSample> samples)
        {
            string identity = SimulationIdentity.Require(
                sourceIdentity,
                nameof(sourceIdentity));
            if (samples.Length < 2 || samples.Length > m_Samples.Length ||
                samples[0].ElapsedSeconds != 0f)
                throw new ArgumentException("Future Body Translation requires an anchored sample sequence.", nameof(samples));
            float previousTime = -1f;
            for (int i = 0; i < samples.Length; i++)
            {
                CharacterFutureBodyTranslationSample sample = samples[i];
                if (sample.ElapsedSeconds <= previousTime)
                    throw new ArgumentException("Future Body Translation sample time is not strictly increasing.", nameof(samples));
                m_Samples[i] = sample;
                previousTime = sample.ElapsedSeconds;
            }
            for (int i = samples.Length; i < m_Samples.Length; i++)
                m_Samples[i] = default;
            m_SourceIdentity = identity;
            m_SampleCount = samples.Length;
        }

        public void CopyFrom(CharacterFutureBodyTranslation source)
        {
            if (source == null || !source.IsAvailable)
            {
                Clear();
                return;
            }
            m_SourceIdentity = source.m_SourceIdentity;
            m_SampleCount = source.m_SampleCount;
            Array.Copy(source.m_Samples, m_Samples, m_Samples.Length);
        }

        public void Clear()
        {
            m_SourceIdentity = string.Empty;
            m_SampleCount = 0;
            Array.Clear(m_Samples, 0, m_Samples.Length);
        }

        public CharacterFutureBodyTranslationSample SampleAt(int index)
        {
            if ((uint)index >= (uint)m_SampleCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            return m_Samples[index];
        }

        public CharacterFutureBodyTranslationSample Evaluate(float elapsedSeconds)
        {
            if (!IsAvailable || !float.IsFinite(elapsedSeconds) || elapsedSeconds < 0f ||
                elapsedSeconds > DurationSeconds + 0.0001f)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            }
            float time = Math.Min(elapsedSeconds, DurationSeconds);
            if (time <= 0f)
                return m_Samples[0];
            for (int i = 1; i < m_SampleCount; i++)
            {
                CharacterFutureBodyTranslationSample end = m_Samples[i];
                if (time > end.ElapsedSeconds)
                    continue;
                CharacterFutureBodyTranslationSample start = m_Samples[i - 1];
                float duration = end.ElapsedSeconds - start.ElapsedSeconds;
                float t = duration > 0.000001f
                    ? Math.Clamp((time - start.ElapsedSeconds) / duration, 0f, 1f)
                    : 1f;
                return new CharacterFutureBodyTranslationSample(
                    time,
                    Lerp(start.RelativePositionX, end.RelativePositionX, t),
                    Lerp(start.RelativePositionY, end.RelativePositionY, t),
                    Lerp(start.RelativePositionZ, end.RelativePositionZ, t),
                    Lerp(start.VelocityX, end.VelocityX, t),
                    Lerp(start.VelocityY, end.VelocityY, t),
                    Lerp(start.VelocityZ, end.VelocityZ, t));
            }
            return m_Samples[m_SampleCount - 1];
        }

        static float Lerp(float start, float end, float t) => start + (end - start) * t;
    }

    public interface ICharacterFutureBodyTranslationSource
    {
        string PredictionSourceIdentity { get; }

        bool TryPredict(
            in CharacterFutureBodyTranslationRequest request,
            CharacterFutureBodyTranslation output);
    }
}
