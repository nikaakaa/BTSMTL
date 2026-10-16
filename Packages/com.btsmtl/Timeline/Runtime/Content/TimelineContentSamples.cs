using System;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline
{
    public enum TimelineSampleSourceMapping : byte
    {
        ClipSeconds = 1,
        SourceRange = 2,
        ClipSecondsClampedToSourceRange = 3
    }

    public enum TimelineSampleWindow : byte
    {
        CurrentPosition = 1,
        TraversalWindow = 2
    }

    [ValueStorage(UseConstructor = true)]
    public readonly partial struct TimelineContentSampling
    {
        public TimelineContentSampling(int weightCurveIndex, int easeInCurveIndex, int easeOutCurveIndex,
            TimelineSampleSourceMapping sourceMapping, ExtraPolationMode extrapolation,
            TimelineSampleWindow window, bool emitZeroWeight, float normalizationDurationMinimum)
        {
            WeightCurveIndex = weightCurveIndex;
            EaseInCurveIndex = easeInCurveIndex;
            EaseOutCurveIndex = easeOutCurveIndex;
            SourceMapping = sourceMapping;
            Extrapolation = extrapolation;
            Window = window;
            EmitZeroWeight = emitZeroWeight;
            NormalizationDurationMinimum = normalizationDurationMinimum;
        }

        public int WeightCurveIndex { get; }
        public int EaseInCurveIndex { get; }
        public int EaseOutCurveIndex { get; }
        public TimelineSampleSourceMapping SourceMapping { get; }
        public ExtraPolationMode Extrapolation { get; }
        public TimelineSampleWindow Window { get; }
        public bool EmitZeroWeight { get; }
        public float NormalizationDurationMinimum { get; }
    }

    public enum ExtraPolationMode
    {
        None = 0,
        Hold = 1,
    }

    [Flags]
    public enum ClipCapabilities
    {
        None = 0,
        Resizable = 0x1,
        Mixable = 0x2,
        ClipInable = 0x4,
        TickQuantized = 0x8,
    }

    public delegate void Evaluate(float deltaTime);
}
