using System;
using ThirdPersonSimulation.Fixed;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    sealed class TimelineRuntimeEvaluationStoragePool
    {
        Stack<TimelineRuntimeEvaluationStorage> m_Shared;
        readonly Dictionary<(TimelineContentUnit Timeline, TimelinePlaybackMode Mode), Stack<TimelineRuntimeEvaluationStorage>> m_Recycled = new();

        internal int MaximumMotionContributionsPerAdvance { get; private set; }

        public void Prepare(TimelineRuntimePreparedContent content, TimelinePlaybackMode mode)
        {
            var key = (content.Content, mode);
            if (m_Recycled.ContainsKey(key))
                return;
            var recycled = new Stack<TimelineRuntimeEvaluationStorage>(2);
            var storage = new TimelineRuntimeEvaluationStorage(content.Content, mode, recycled);
            MaximumMotionContributionsPerAdvance = Math.Max(MaximumMotionContributionsPerAdvance,
                storage.MotionContributions.Capacity);
            recycled.Push(storage);
            recycled.Push(new TimelineRuntimeEvaluationStorage(content.Content, mode, recycled));
            m_Recycled.Add(key, recycled);
        }

        public void PrepareShared(IReadOnlyList<TimelineContentUnit> contents, IReadOnlyList<TimelinePlaybackMode> modes, int playbackCount)
        {
            m_Shared = new Stack<TimelineRuntimeEvaluationStorage>(checked(playbackCount * 2));
            for (int i = 0; i < playbackCount * 2; i++)
            {
                var storage = new TimelineRuntimeEvaluationStorage(contents[0], modes[0], m_Shared, contents, modes);
                MaximumMotionContributionsPerAdvance = storage.MotionContributions.Capacity;
                m_Shared.Push(storage);
            }
        }

        public TimelineRuntimeEvaluationStorage Rent(TimelineRuntimePreparationResult preparation)
        {
            if (m_Shared != null) return m_Shared.Pop();
            var key = (preparation.Content, preparation.PlaybackMode);
            Stack<TimelineRuntimeEvaluationStorage> recycled = m_Recycled[key];
            return recycled.Count == 0
                ? new TimelineRuntimeEvaluationStorage(preparation.Content, preparation.PlaybackMode, recycled) : recycled.Pop();
        }

        public void RetireContent(TimelineContentUnit timeline, TimelinePlaybackMode mode) =>
            m_Recycled.Remove((timeline, mode));

        public void Clear() => m_Recycled.Clear();
    }

    sealed class TimelineRuntimeEvaluationStorage
    {
        public static readonly Comparison<TimelineRuntimeTreeClipRequest> TreeRequestComparison =
            TimelineRuntimeEvaluator.CompareTreeRequests;
        readonly Stack<TimelineRuntimeEvaluationStorage> m_Recycled;

        public TimelineRuntimeEvaluationStorage(
            TimelineContentUnit content,
            TimelinePlaybackMode mode,
            Stack<TimelineRuntimeEvaluationStorage> recycled,
            IReadOnlyList<TimelineContentUnit> capacityContents = null,
            IReadOnlyList<TimelinePlaybackMode> capacityModes = null)
        {
            m_Recycled = recycled;
            int animations = 0, motions = 0, cameraStates = 0;
            int cameraResponses = 0, cameraResources = 0, treeClips = 0;
            int markers = 0, scenePresentation = 0, clips = 0;
            int boundaries = 0, samples = 0, traces = 0;
            if (capacityContents == null)
                IncludeCapacity(content, mode, ref boundaries, ref samples, ref traces, ref animations, ref motions, ref cameraStates, ref cameraResponses,
                    ref cameraResources, ref treeClips, ref markers, ref scenePresentation, ref clips);
            else
                for (int i = 0; i < capacityContents.Count; i++)
                    IncludeCapacity(capacityContents[i], capacityModes[i], ref boundaries, ref samples, ref traces, ref animations, ref motions, ref cameraStates, ref cameraResponses,
                        ref cameraResources, ref treeClips, ref markers, ref scenePresentation, ref clips);
            ActiveClipIds = new(clips);
            SamplingClips = new List<int>(clips);
            Boundaries = new(boundaries);
            AnimationContributions = new(animations);
            MotionContributions = new(motions);
            CameraStates = new(checked(cameraStates));
            CameraResponses = new(checked(cameraResponses));
            CameraResources = new(checked(cameraResources));
            TreeClips = new(treeClips);
            Markers = new(markers);
            ScenePresentation = new(checked(scenePresentation));
            ClipSamples = new(samples);
            Traces = new(traces);
        }

        static void IncludeCapacity(TimelineContentUnit content, TimelinePlaybackMode mode,
            ref int boundaries, ref int samples, ref int traces, ref int animations, ref int motions,
            ref int states, ref int responses, ref int resources, ref int trees, ref int markers, ref int scenes, ref int clips)
        {
            int a = 0, m = 0, s = 0, r = 0, e = 0, t = 0, k = 0, p = 0, c = content.Clips.Count;
            for (int i = 0; i < content.Clips.Count; i++)
            {
                TimelineContentClip clip = content.Clips[i];
                if (clip.TrackMuted || !clip.ExecutionPolicy.IsLogic) continue;
                switch (clip.Parameters)
                {
                    case TimelineContentAnimation: a++; break;
                    case TimelineContentMotion: m++; break;
                    case TimelineContentCameraState: s++; break;
                    case TimelineContentCameraResponse: r++; break;
                    case TimelineContentCameraEffect: e++; break;
                    case TimelineContentTree: t++; break;
                    case TimelineContentSceneParameter: p++; break;
                }
            }
            for (int i = 0; i < content.Markers.Count; i++)
                if (!content.Markers[i].TrackMuted && content.Markers[i].ExecutionPolicy.IsLogic) k++;
            int traversals = mode == TimelinePlaybackMode.Loop
                ? TimelineRuntimeEvaluationSegments.MaximumCycleAdvance + 1 : 1;
            boundaries = Math.Max(boundaries, checked(2 * c * traversals));
            samples = Math.Max(samples, checked(c * (2 * traversals + 1)));
            traces = Math.Max(traces, checked((2 * c + t) * traversals));
            animations = Math.Max(animations, checked(a * traversals)); motions = Math.Max(motions, checked(m * traversals));
            states = Math.Max(states, s); responses = Math.Max(responses, r); resources = Math.Max(resources, e);
            trees = Math.Max(trees, checked(3 * t * traversals)); markers = Math.Max(markers, checked(k * traversals)); scenes = Math.Max(scenes, p); clips = Math.Max(clips, c);
        }

        public readonly TimelineRuntimeSampleBuffer<CanonicalTextValue> ActiveClipIds;
        public readonly List<int> SamplingClips;
        public readonly TimelineRuntimeSampleBuffer<TimelineRuntimeClipBoundary> Boundaries;
        public readonly TimelineRuntimeSampleBuffer<TimelineAnimationContribution> AnimationContributions;
        public readonly TimelineRuntimeSampleBuffer<TimelineMotionCurveContribution> MotionContributions;
        public readonly TimelineRuntimeSampleBuffer<TimelineCameraStateSample> CameraStates;
        public readonly TimelineRuntimeSampleBuffer<TimelineCameraResponseSample> CameraResponses;
        public readonly TimelineRuntimeSampleBuffer<TimelineCameraResourceSample> CameraResources;
        public readonly TimelineRuntimeSampleBuffer<TimelineRuntimeTreeClipRequest> TreeClips;
        public readonly TimelineRuntimeSampleBuffer<TimelineRuntimeMarkerRequest> Markers;
        public readonly TimelineRuntimeSampleBuffer<TimelineRuntimeScenePresentationSample> ScenePresentation;
        public readonly TimelineRuntimeSampleBuffer<TimelineRuntimeClipSample> ClipSamples;
        public readonly TimelineRuntimeSampleBuffer<TimelineRuntimeTraceOutput> Traces;

        public void Recycle() => m_Recycled.Push(this);

        public void Clear()
        {
            ActiveClipIds.Clear();
            SamplingClips.Clear();
            Boundaries.Clear();
            AnimationContributions.Clear();
            MotionContributions.Clear();
            CameraStates.Clear();
            CameraResponses.Clear();
            CameraResources.Clear();
            TreeClips.Clear();
            Markers.Clear();
            ScenePresentation.Clear();
            ClipSamples.Clear();
            Traces.Clear();
        }
    }
}
