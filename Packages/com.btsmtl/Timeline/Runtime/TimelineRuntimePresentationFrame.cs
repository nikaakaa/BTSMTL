using System;
using BTSMTL.Orchestration;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    sealed class TimelineRuntimePresentationBuffer
    {
        public readonly struct Capacity
        {
            public Capacity(int[] samples, int events, int trees, int activeTrees, int samplingClips)
            {
                Samples = samples;
                Events = events;
                Trees = trees;
                ActiveTrees = activeTrees;
                SamplingClips = samplingClips;
            }

            public readonly int[] Samples;
            public readonly int Events;
            public readonly int Trees;
            public readonly int ActiveTrees;
            public readonly int SamplingClips;
        }

        public TimelineRuntimePresentationBuffer()
        {
            Samples = Array.Empty<TimelineRuntimeSampleBuffer<TimelineRuntimeSample<Float32Scalar>>>();
            SamplingClips = new List<int>();
            Events = new(0);
            TreeClips = new(0);
            ActiveTreeClips = new(0);
        }

        public TimelineRuntimePresentationBuffer(TimelineRuntimePreparedContent prepared,
            OrchestrationTimelinePreparedContent<OrchestrationTime> executionContent,
            TimelineRuntimePresentationGraphSource graphSource)
            : this(CreateCapacity(prepared, executionContent, graphSource), prepared.SampleKindCount)
        {
        }

        public TimelineRuntimePresentationBuffer(TimelineRuntimePreparedContent prepared)
            : this(CreateCapacity(prepared, 1, 0, 0, false), prepared.SampleKindCount)
        {
        }

        TimelineRuntimePresentationBuffer(in Capacity capacity, int sampleKindCount) : this()
        {
            PrepareCapacity(in capacity);
            SampleKindCount = sampleKindCount;
        }

        public static Capacity CreateCapacity(TimelineRuntimePreparedContent prepared,
            OrchestrationTimelinePreparedContent<OrchestrationTime> executionContent,
            TimelineRuntimePresentationGraphSource graphSource) =>
            CreateCapacity(prepared,
                executionContent.Mode == OrchestrationTimelinePlaybackMode.Loop
                    ? OrchestrationTimelineLimits.MaximumCycleAdvance + 1 : 1,
                graphSource == TimelineRuntimePresentationGraphSource.Unexecuted
                    ? checked(executionContent.TickCapacity.GraphInvocationsPerAdvance + 2 * executionContent.RequiredActiveTrees) : 0,
                graphSource == TimelineRuntimePresentationGraphSource.Unexecuted ? executionContent.RequiredActiveTrees : 0,
                graphSource == TimelineRuntimePresentationGraphSource.Unexecuted);

        static Capacity CreateCapacity(TimelineRuntimePreparedContent prepared, int traversals,
            int treeCapacity, int activeTreeCapacity, bool includeMarkers)
        {
            TimelineContentUnit content = prepared.Content;
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
            if (includeMarkers)
                for (int index = 0; index < content.Markers.Count; index++)
                {
                    TimelineContentMarker marker = content.Markers[index];
                    if (marker.ExecutionPolicy.IsPresentation && !marker.TrackMuted)
                        markers++;
                }
            return new Capacity(capacities, checked(markers * traversals), treeCapacity,
                activeTreeCapacity, Math.Max(content.Clips.Count, content.Markers.Count));
        }

        public void PrepareCapacity(in Capacity capacity)
        {
            int previousKinds = Samples.Length;
            ulong version = previousKinds == 0 ? 0 : Samples[0].Version;
            if (previousKinds < capacity.Samples.Length)
            {
                Array.Resize(ref Samples, capacity.Samples.Length);
                for (int kindIndex = previousKinds; kindIndex < Samples.Length; kindIndex++)
                {
                    Samples[kindIndex] = new TimelineRuntimeSampleBuffer<TimelineRuntimeSample<Float32Scalar>>(0);
                    Samples[kindIndex].RestoreRecordedState(ReadOnlySpan<TimelineRuntimeSample<Float32Scalar>>.Empty, version);
                }
            }
            for (int kindIndex = 0; kindIndex < capacity.Samples.Length; kindIndex++)
                Samples[kindIndex].PrepareCapacity(capacity.Samples[kindIndex]);
            SamplingClips.Capacity = Math.Max(SamplingClips.Capacity, capacity.SamplingClips);
            Events.PrepareCapacity(capacity.Events);
            TreeClips.PrepareCapacity(capacity.Trees);
            ActiveTreeClips.PrepareCapacity(capacity.ActiveTrees);
        }

        public int SampleKindCount;
        public TimelineRuntimeSampleBuffer<TimelineRuntimeSample<Float32Scalar>>[] Samples;
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
        readonly TimelineRuntimePresentationBuffer m_Buffer;
        readonly ulong m_SampleVersion;

        internal TimelineRuntimePresentationOperations(TimelineRuntimePresentationBuffer buffer)
        {
            m_Buffer = buffer;
            m_SampleVersion = buffer.SampleKindCount == 0 ? 0 : buffer.Samples[0].Version;
            TreeClips = buffer.TreeClips.View;
            ActiveTreeClips = buffer.ActiveTreeClips.View;
        }

        public TimelineRuntimeSampleView<TimelineRuntimeTreeClipRequest> TreeClips { get; }
        public TimelineRuntimeSampleView<TimelineRuntimeTreeClipRequest> ActiveTreeClips { get; }
        public int SampleKindCount => m_Buffer.SampleKindCount;
        public TimelineRuntimeSampleView<TimelineRuntimeSample<Float32Scalar>> GetSamples(int kindIndex) =>
            new(m_Buffer.Samples[kindIndex], m_SampleVersion);
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
