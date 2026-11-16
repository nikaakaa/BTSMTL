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
            Bind(executionContent);
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
        public readonly TimelineRuntimeTreeClipBuffer TreeClips;
        public readonly TimelineRuntimeTreeClipBuffer ActiveTreeClips;

        public void Bind(OrchestrationTimelinePreparedContent<OrchestrationTime> executionContent)
        {
            TreeClips.ExecutionContent = executionContent;
            ActiveTreeClips.ExecutionContent = executionContent;
        }

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

    sealed class TimelineRuntimeTreeClipBuffer
    {
        readonly struct StoredRequest
        {
            readonly OrchestrationTime m_Time;
            readonly OrchestrationTime m_PreviousTime;
            readonly int m_ClipOrdinal;
            readonly int m_Cycle;
            readonly float m_NormalizedTime;
            readonly byte m_Phase;
            readonly TimelineClipExitSource m_ExitSource;
            readonly TimelineRuntimeTreeClipEventKind m_EventKind;
            readonly bool m_Marker;

            internal StoredRequest(in TimelineRuntimeTreeClipRequest request)
            {
                m_Time = request.Time;
                m_PreviousTime = request.PreviousTime;
                m_ClipOrdinal = request.Token.ClipOrdinal;
                m_Cycle = request.Token.Cycle;
                m_NormalizedTime = request.NormalizedTime;
                m_Phase = (byte)request.Phase;
                m_ExitSource = request.ExitSource;
                m_EventKind = request.EventKind;
                m_Marker = request.Token.Marker;
            }

            internal TimelineRuntimeTreeClipRequest Expand(
                OrchestrationTimelinePreparedContent<OrchestrationTime> executionContent,
                int playbackSlot, ulong sequence, ulong playbackIdentity) =>
                new(new OrchestrationTimelineInvocationToken(playbackSlot, sequence, playbackIdentity,
                        m_ClipOrdinal, m_Cycle, m_Marker),
                    (TimelineTreeExecutionPhase)m_Phase, m_ExitSource, m_EventKind,
                    m_Time, m_PreviousTime, executionContent.Clips.Span[m_ClipOrdinal].Start, m_NormalizedTime);
        }

        internal OrchestrationTimelinePreparedContent<OrchestrationTime> ExecutionContent;
        StoredRequest[] m_Values;
        int m_Count;
        int m_PlaybackSlot;
        ulong m_Sequence;
        ulong m_PlaybackIdentity;

        public TimelineRuntimeTreeClipBuffer(int capacity)
        {
            m_Values = capacity == 0 ? Array.Empty<StoredRequest>() : new StoredRequest[capacity];
        }

        public int Count => m_Count;
        public int Capacity => m_Values.Length;
        public ulong Version { get; private set; }
        public TimelineRuntimeTreeClipView View => new(this);

        public TimelineRuntimeTreeClipRequest this[int index]
        {
            get
            {
                if ((uint)index >= (uint)m_Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return m_Values[index].Expand(ExecutionContent, m_PlaybackSlot, m_Sequence, m_PlaybackIdentity);
            }
        }

        internal void PrepareCapacity(int capacity)
        {
            if (capacity > m_Values.Length)
                Array.Resize(ref m_Values, capacity);
        }

        public void Add(in TimelineRuntimeTreeClipRequest value)
        {
            if (m_Count == Capacity)
                throw new InvalidOperationException("Timeline sample count exceeds the prepared content capacity.");
            if (m_Count == 0)
                SetPlayback(value.Token);
            m_Values[m_Count++] = new StoredRequest(in value);
        }

        public void RemoveAt(int index)
        {
            if ((uint)index >= (uint)m_Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            m_Count--;
            Array.Copy(m_Values, index + 1, m_Values, index, m_Count - index);
        }

        public void Clear()
        {
            m_Count = 0;
            Version = checked(Version + 1);
        }

        internal void RestoreRecordedState(ReadOnlySpan<TimelineRuntimeTreeClipRequest> values, ulong version)
        {
            if (values.Length > Capacity)
                throw new InvalidOperationException("Recorded Timeline samples exceed the prepared content capacity.");
            if (values.Length != 0)
                SetPlayback(values[0].Token);
            for (int index = 0; index < values.Length; index++)
                m_Values[index] = new StoredRequest(in values[index]);
            m_Count = values.Length;
            Version = version;
        }

        void SetPlayback(OrchestrationTimelineInvocationToken token)
        {
            m_PlaybackSlot = token.PlaybackSlot;
            m_Sequence = token.Sequence;
            m_PlaybackIdentity = token.PlaybackIdentity;
        }
    }

    public readonly struct TimelineRuntimeTreeClipView
    {
        readonly TimelineRuntimeTreeClipBuffer m_Buffer;
        readonly ulong m_Version;

        internal TimelineRuntimeTreeClipView(TimelineRuntimeTreeClipBuffer buffer)
        {
            m_Buffer = buffer;
            m_Version = buffer.Version;
        }

        public int Count
        {
            get
            {
                if (m_Buffer == null)
                    return 0;
                RequireCurrent();
                return m_Buffer.Count;
            }
        }

        public TimelineRuntimeTreeClipRequest this[int index]
        {
            get
            {
                RequireCurrent();
                return m_Buffer[index];
            }
        }

        void RequireCurrent()
        {
            if (m_Buffer == null || m_Buffer.Version != m_Version)
                throw new InvalidOperationException("Timeline sample view no longer belongs to a live frame.");
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

        public TimelineRuntimeTreeClipView TreeClips { get; }
        public TimelineRuntimeTreeClipView ActiveTreeClips { get; }
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
