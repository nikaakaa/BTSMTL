using BTSMTL.Orchestration;

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
            TimelineRuntimePresentationBuffer buffer)
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
                    else if (cycle != currentCycle ||
                        clip.EndTime < to && clip.Sampling.Extrapolation != ExtraPolationMode.Hold)
                        continue;
                    if (preparedContent.ClipSampling.TrySample(in clip, indexed.ContentIndex, from, to, cycle, reason, out var sample))
                        buffer.Samples[sample.KindIndex].Add(sample);
                }
            }
        }
    }
}
