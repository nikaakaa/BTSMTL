using System;
using BTSMTL.Orchestration;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    sealed class TimelineRuntimePresentationBuffer
    {
        public TimelineRuntimePresentationBuffer(TimelineRuntimePreparedContent prepared,
            OrchestrationTimelinePreparedContent<OrchestrationTime> executionContent)
        {
            TimelineContentUnit content = prepared.Content;
            int traversals = executionContent.Mode == OrchestrationTimelinePlaybackMode.Loop
                ? OrchestrationTimelineLimits.MaximumCycleAdvance + 1 : 1;
            int markers = 0;
            var capacities = new int[prepared.SampleKindCount];
            for (int index = 0; index < content.Clips.Count; index++)
            {
                TimelineContentClip clip = content.Clips[index];
                if (clip.TrackMuted || !clip.ExecutionPolicy.IsPresentation)
                    continue;
                if (clip.ContentKind == TimelineContentKind.Sampled)
                {
                    int kindIndex = prepared.ClipSampling.KindIndexAt(index);
                    int count = clip.Sampling.Window == TimelineSampleWindow.TraversalWindow ? traversals : 1;
                    capacities[kindIndex] = checked(capacities[kindIndex] + count);
                }
            }
            for (int index = 0; index < content.Markers.Count; index++)
            {
                TimelineContentMarker marker = content.Markers[index];
                if (marker.ExecutionPolicy.IsPresentation && !marker.TrackMuted)
                    markers++;
            }
            Samples = new TimelineRuntimeSampleBuffer<TimelineRuntimeSample<Float32Scalar>>[capacities.Length];
            for (int kindIndex = 0; kindIndex < capacities.Length; kindIndex++)
                Samples[kindIndex] = new TimelineRuntimeSampleBuffer<TimelineRuntimeSample<Float32Scalar>>(capacities[kindIndex]);
            SamplingClips = new List<int>(Math.Max(content.Clips.Count, content.Markers.Count));
            Events = new(checked(markers * traversals));
            TreeClips = new(checked(executionContent.TickCapacity.GraphInvocationsPerAdvance +
                2 * executionContent.RequiredActiveTrees));
            ActiveTreeClips = new(executionContent.RequiredActiveTrees);
        }

        public readonly TimelineRuntimeSampleBuffer<TimelineRuntimeSample<Float32Scalar>>[] Samples;
        public readonly List<int> SamplingClips;
        public readonly TimelineRuntimeSampleBuffer<TimelineRuntimePresentationEvent> Events;
        public readonly TimelineRuntimeSampleBuffer<TimelineRuntimeTreeClipRequest> TreeClips;
        public readonly TimelineRuntimeSampleBuffer<TimelineRuntimeTreeClipRequest> ActiveTreeClips;

        public void Clear()
        {
            for (int kindIndex = 0; kindIndex < Samples.Length; kindIndex++)
                Samples[kindIndex].Clear();
            SamplingClips.Clear();
            Events.Clear();
            TreeClips.Clear();
            ActiveTreeClips.Clear();
        }
    }

    public readonly struct TimelineRuntimePresentationOperations
    {
        readonly TimelineRuntimeSampleBuffer<TimelineRuntimeSample<Float32Scalar>>[] m_Samples;
        readonly ulong m_SampleVersion;

        internal TimelineRuntimePresentationOperations(TimelineRuntimePresentationBuffer buffer)
        {
            m_Samples = buffer.Samples;
            m_SampleVersion = m_Samples.Length == 0 ? 0 : m_Samples[0].Version;
            TreeClips = buffer.TreeClips.View;
            ActiveTreeClips = buffer.ActiveTreeClips.View;
        }

        public TimelineRuntimeSampleView<TimelineRuntimeTreeClipRequest> TreeClips { get; }
        public TimelineRuntimeSampleView<TimelineRuntimeTreeClipRequest> ActiveTreeClips { get; }
        public int SampleKindCount => m_Samples.Length;
        public TimelineRuntimeSampleView<TimelineRuntimeSample<Float32Scalar>> GetSamples(int kindIndex) =>
            new(m_Samples[kindIndex], m_SampleVersion);
    }

    public readonly struct TimelineRuntimePresentationFrame
    {
        internal TimelineRuntimePresentationFrame(
            in OrchestrationTimelineStart<OrchestrationTime> start,
            ulong logicTick,
            ulong presentationFrame,
            float presentationDeltaSeconds,
            float interpolationAlpha,
            TimelineRuntimePresentationOperations operations,
            TimelineRuntimeSampleView<TimelineRuntimePresentationEvent> events,
            TimelinePresentationSampleReason reason,
            OrchestrationTime time,
            int cycle,
            bool retainForCorrection)
        {
            if (presentationFrame == 0 ||
                !float.IsFinite(presentationDeltaSeconds) || presentationDeltaSeconds < 0f ||
                !float.IsFinite(interpolationAlpha) || interpolationAlpha < 0f || interpolationAlpha > 1f)
                throw new ArgumentException("Timeline presentation frame is invalid.");
            Start = start;
            LogicTick = logicTick;
            PresentationFrame = presentationFrame;
            PresentationDeltaSeconds = presentationDeltaSeconds;
            InterpolationAlpha = interpolationAlpha;
            Operations = operations;
            Reason = reason;
            Time = time;
            Cycle = cycle;
            RetainForCorrection = retainForCorrection;
            Events = events;
        }

        public readonly OrchestrationTimelineStart<OrchestrationTime> Start;
        public ulong LogicTick { get; }
        public ulong PresentationFrame { get; }
        public float PresentationDeltaSeconds { get; }
        public float InterpolationAlpha { get; }
        public readonly TimelineRuntimePresentationOperations Operations;
        public TimelinePresentationSampleReason Reason { get; }
        public OrchestrationTime Time { get; }
        public int Cycle { get; }
        public bool RetainForCorrection { get; }
        public TimelineRuntimeSampleView<TimelineRuntimePresentationEvent> Events { get; }
    }
}
