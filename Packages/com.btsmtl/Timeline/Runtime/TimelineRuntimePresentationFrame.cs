using System;
using ThirdPersonSimulation.Fixed;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    sealed class TimelineRuntimePresentationBuffer
    {
        public TimelineRuntimePresentationBuffer(TimelineContentUnit content, TimelinePlaybackMode mode)
        {
            int animations = 0, states = 0, responses = 0, resources = 0, scene = 0, markers = 0, trees = 0;
            for (int index = 0; index < content.Tracks.Count; index++)
            {
                TimelineContentTrack track = content.Tracks[index];
                if (track.Muted || !track.ExecutionPolicy.IsPresentation)
                    continue;
                int count = track.ClipAuthoringIds.Count;
                switch (track.ContractKind)
                {
                    case TimelineContractKinds.TreeTrack: trees = checked(trees + count); break;
                    case TimelineContractKinds.AnimationTrack: animations = checked(animations + count); break;
                    case TimelineContractKinds.CameraStateTrack: states = checked(states + count); break;
                    case TimelineContractKinds.CameraResponseTrack: responses = checked(responses + count); break;
                    case TimelineContractKinds.CameraEffectTrack: resources = checked(resources + count); break;
                    case TimelineContractKinds.ScenePresentationParameterTrack: scene = checked(scene + count); break;
                }
            }
            for (int index = 0; index < content.Markers.Count; index++)
            {
                TimelineContentMarker marker = content.Markers[index];
                if (marker.ExecutionPolicy.IsPresentation && !marker.TrackMuted)
                    markers++;
            }
            int traversals = mode == TimelinePlaybackMode.Loop
                ? TimelineRuntimeEvaluationSegments.MaximumCycleAdvance + 1 : 1;
            Animations = new(animations);
            SamplingClips = new List<int>(Math.Max(content.Clips.Count, content.Markers.Count));
            CameraStates = new(states);
            CameraResponses = new(responses);
            CameraResources = new(resources);
            ScenePresentation = new(scene);
            Events = new(checked(markers * traversals));
            TreeClips = new(checked(trees * (traversals * 3 + 2)));
            ActiveTreeClips = new(trees);
        }

        public readonly TimelineRuntimeSampleBuffer<TimelineAnimationContribution> Animations;
        public readonly List<int> SamplingClips;
        public readonly TimelineRuntimeSampleBuffer<TimelineCameraStateSample> CameraStates;
        public readonly TimelineRuntimeSampleBuffer<TimelineCameraResponseSample> CameraResponses;
        public readonly TimelineRuntimeSampleBuffer<TimelineCameraResourceSample> CameraResources;
        public readonly TimelineRuntimeSampleBuffer<TimelineRuntimeScenePresentationSample> ScenePresentation;
        public readonly TimelineRuntimeSampleBuffer<TimelineRuntimePresentationEvent> Events;
        public readonly TimelineRuntimeSampleBuffer<TimelineRuntimeTreeClipRequest> TreeClips;
        public readonly TimelineRuntimeSampleBuffer<TimelineRuntimeTreeClipRequest> ActiveTreeClips;

        public void Clear()
        {
            Animations.Clear();
            SamplingClips.Clear();
            CameraStates.Clear();
            CameraResponses.Clear();
            CameraResources.Clear();
            ScenePresentation.Clear();
            Events.Clear();
            TreeClips.Clear();
            ActiveTreeClips.Clear();
        }
    }

    public readonly struct TimelineRuntimePresentationOperations
    {
        internal TimelineRuntimePresentationOperations(TimelineRuntimePresentationBuffer buffer)
        {
            AnimationContributions = buffer.Animations.View;
            CameraStates = buffer.CameraStates.View;
            CameraResponses = buffer.CameraResponses.View;
            CameraResources = buffer.CameraResources.View;
            ScenePresentation = buffer.ScenePresentation.View;
            TreeClips = buffer.TreeClips.View;
            ActiveTreeClips = buffer.ActiveTreeClips.View;
        }

        public TimelineRuntimeSampleView<TimelineRuntimeTreeClipRequest> TreeClips { get; }
        public TimelineRuntimeSampleView<TimelineRuntimeTreeClipRequest> ActiveTreeClips { get; }
        public TimelineRuntimeSampleView<TimelineAnimationContribution> AnimationContributions { get; }
        public TimelineRuntimeSampleView<TimelineCameraStateSample> CameraStates { get; }
        public TimelineRuntimeSampleView<TimelineCameraResponseSample> CameraResponses { get; }
        public TimelineRuntimeSampleView<TimelineCameraResourceSample> CameraResources { get; }
        public TimelineRuntimeSampleView<TimelineRuntimeScenePresentationSample> ScenePresentation { get; }
    }

    public readonly struct TimelineRuntimePresentationFrame
    {
        internal TimelineRuntimePresentationFrame(
            TimelineRuntimePlayback playback,
            ulong logicTick,
            ulong presentationFrame,
            float presentationDeltaSeconds,
            float interpolationAlpha,
            TimelineRuntimePresentationOperations operations,
            TimelineRuntimeSampleView<TimelineRuntimePresentationEvent> events,
            TimelinePresentationSampleReason reason,
            FixedScalar time,
            int cycle)
        {
            if (playback == null || !playback.Handle.IsValid || playback.Generation == 0 || presentationFrame == 0 ||
                !float.IsFinite(presentationDeltaSeconds) || presentationDeltaSeconds < 0f ||
                !float.IsFinite(interpolationAlpha) || interpolationAlpha < 0f || interpolationAlpha > 1f)
                throw new ArgumentException("Timeline presentation frame is invalid.");
            Handle = playback.Handle;
            Generation = playback.Generation;
            LogicTick = logicTick;
            PresentationFrame = presentationFrame;
            PresentationDeltaSeconds = presentationDeltaSeconds;
            InterpolationAlpha = interpolationAlpha;
            ExecutionIdentity = playback.ExecutionIdentity;
            Operations = operations;
            Reason = reason;
            Time = time;
            Cycle = cycle;
            Events = events;
        }

        public TimelineRuntimePlaybackHandle Handle { get; }
        public ulong Generation { get; }
        public ulong LogicTick { get; }
        public ulong PresentationFrame { get; }
        public float PresentationDeltaSeconds { get; }
        public float InterpolationAlpha { get; }
        public TimelineExecutionIdentity ExecutionIdentity { get; }
        public TimelineRuntimePresentationOperations Operations { get; }
        public TimelinePresentationSampleReason Reason { get; }
        public FixedScalar Time { get; }
        public int Cycle { get; }
        public TimelineRuntimeSampleView<TimelineRuntimePresentationEvent> Events { get; }
    }
}
