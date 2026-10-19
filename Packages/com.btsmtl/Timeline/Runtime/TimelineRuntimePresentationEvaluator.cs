using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    internal static class TimelineRuntimePresentationEvaluator
    {
        public static void Evaluate(
            TimelineRuntimePreparedContent preparedContent,
            OrchestrationTime previousPosition,
            int previousCycle,
            OrchestrationTime currentPosition,
            int currentCycle,
            TimelinePresentationSampleReason reason,
            TimelineRuntimePresentationBuffer buffer,
            bool traversalOnly = false)
        {
            TimelineContentUnit content = preparedContent.Content;
            var selected = buffer.SamplingClips;
            TimelineRuntimeClipIndex sampled = preparedContent.Index.Presentation.Sampled;
            for (int cycle = previousCycle; cycle <= currentCycle; cycle++)
            {
                OrchestrationTime from = cycle == previousCycle ? previousPosition : OrchestrationTime.Zero;
                OrchestrationTime to = cycle == currentCycle ? currentPosition : content.Duration;
                selected.Clear();
                sampled.Select(from, to, true, selected);
                for (int selectedIndex = 0; selectedIndex < selected.Count; selectedIndex++)
                {
                    TimelineRuntimeIndexedClip indexed = sampled[selected[selectedIndex]];
                    TimelineContentClip clip = indexed.Clip;
                    if (clip.Sampling.Window == TimelineSampleWindow.TraversalWindow)
                    {
                        if (clip.EndTime == from && from != to)
                            continue;
                    }
                    else if (traversalOnly || cycle != currentCycle ||
                        clip.EndTime < to && clip.Sampling.Extrapolation != ExtraPolationMode.Hold)
                        continue;
                    if (preparedContent.ClipSampling.TrySample(in clip, indexed.ContentIndex, from, to, cycle, reason, out var sample))
                        buffer.Samples[sample.KindIndex].Add(sample);
                }
            }
        }
    }

    public sealed class TimelineRuntimePresentationTargetSampling
    {
        readonly TimelineRuntimePreparedContent m_Prepared;
        readonly TimelineRuntimePresentationBuffer m_Buffer;

        public TimelineRuntimePresentationTargetSampling(TimelineRuntimePreparedContent prepared,
            OrchestrationTimelinePreparedContent<OrchestrationTime> executionContent)
        {
            m_Prepared = prepared;
            m_Buffer = new TimelineRuntimePresentationBuffer(prepared, executionContent,
                TimelineRuntimePresentationGraphSource.Executed);
        }

        public TimelineRuntimeSampleView<TimelineRuntimeSample<Float32Scalar>> SampleAt(
            OrchestrationTime time, int cycle, int kindIndex)
        {
            m_Buffer.Clear();
            TimelineRuntimePresentationEvaluator.Evaluate(m_Prepared, time, cycle, time, cycle,
                TimelinePresentationSampleReason.Correction, m_Buffer);
            return m_Buffer.Samples[kindIndex].View;
        }
    }
}
