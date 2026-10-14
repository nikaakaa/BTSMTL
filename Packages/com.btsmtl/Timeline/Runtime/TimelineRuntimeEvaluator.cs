using System;
using Photon.Deterministic;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    internal static class TimelineRuntimeEvaluator
    {
        internal static int CompareTreeRequests(TimelineRuntimeTreeClipRequest left, TimelineRuntimeTreeClipRequest right)
        {
            int result = left.Cycle.CompareTo(right.Cycle);
            if (result == 0)
                result = left.Time.CompareTo(right.Time);
            if (result == 0)
                result = ((byte)left.EventKind).CompareTo((byte)right.EventKind);
            return result != 0 ? result : string.CompareOrdinal(left.ClipAuthoringId, right.ClipAuthoringId);
        }

        public static TimelineRuntimeEvaluationResult Evaluate(
            TimelineRuntimeEvaluationStorage storage,
            TimelineRuntimeContentIndex index,
            TimelineRuntimeMotionSampling motionSampling,
            TimelineRuntimeClipSampling clipSampling,
            TimelineContentUnit content,
            FP previousPosition,
            int previousCycle,
            FP currentPosition,
            int currentCycle,
            bool loop,
            IReadOnlyList<TimelineRuntimeClipBoundary> boundaries,
            IReadOnlyList<CanonicalTextValue> previousActiveClipIds,
            TimelineExecutionIdentity executionIdentity,
            ulong generation,
            ulong logicTick,
            bool includeStartBoundary,
            IReadOnlyList<AbilityTimelineTreeClipExit> exitedTreeDecisionClips)
        {
            TimelineRuntimeSampleBuffer<TimelineAnimationContribution> animations = storage.AnimationContributions;
            TimelineRuntimeSampleBuffer<TimelineMotionCurveContribution> motions = storage.MotionContributions;
            TimelineRuntimeSampleBuffer<TimelineCameraStateSample> cameraStates = storage.CameraStates;
            TimelineRuntimeSampleBuffer<TimelineCameraResponseSample> cameraResponses = storage.CameraResponses;
            TimelineRuntimeSampleBuffer<TimelineCameraResourceSample> cameraResources = storage.CameraResources;
            TimelineRuntimeSampleBuffer<TimelineRuntimeTreeClipRequest> treeClips = storage.TreeClips;
            TimelineRuntimeSampleBuffer<TimelineRuntimeMarkerRequest> markers = storage.Markers;
            TimelineRuntimeSampleBuffer<TimelineRuntimeScenePresentationSample> scenePresentation = storage.ScenePresentation;
            TimelineRuntimeSampleBuffer<TimelineRuntimeClipSample> clipSamples = storage.ClipSamples;
            TimelineRuntimeSampleBuffer<TimelineRuntimeTraceOutput> traces = storage.Traces;
            var segments = new TimelineRuntimeEvaluationSegments(
                previousPosition,
                previousCycle,
                currentPosition,
                currentCycle,
                content.Duration,
                loop);
            AppendMarkerRequests(
                index.Logic.Markers,
                content.Duration,
                generation,
                previousPosition,
                previousCycle,
                currentPosition,
                currentCycle,
                loop,
                includeStartBoundary,
                markers);
            motionSampling.Sample(content, index.Logic.Motions, segments, storage.SamplingClips, motions);
            var selected = storage.SamplingClips;
            TimelineRuntimeClipIndex animationIndex = index.Logic.Animations;
            for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                TimelineRuntimeEvaluationSegment segment = segments[segmentIndex];
                selected.Clear();
                animationIndex.Select(segment.CurrentTime, segment.CurrentTime, true, selected);
                for (int selectedIndex = 0; selectedIndex < selected.Count; selectedIndex++)
                {
                    TimelineRuntimeIndexedClip indexed = animationIndex[selected[selectedIndex]];
                    clipSampling.SampleAnimation(indexed, content, segment.CurrentTime, loop, segment.Cycle, animations);
                }
            }
            FP currentTime = currentPosition;
            for (int boundaryIndex = 0; boundaryIndex < boundaries.Count; boundaryIndex++)
            {
                TimelineRuntimeClipBoundary boundary = boundaries[boundaryIndex];
                TimelineRuntimeIndexedClip indexed = index[boundary.AuthoringId];
                TimelineContentClip boundaryClip = indexed.Clip;
                clipSamples.Add(new TimelineRuntimeClipSample(
                    boundary.AuthoringId,
                    boundary.TrackAuthoringId,
                    boundaryClip.ContractKind,
                    boundary.Kind == TimelineRuntimeClipBoundaryKind.Enter
                        ? TimelineRuntimeTreeClipEventKind.Enter
                        : TimelineRuntimeTreeClipEventKind.Exit,
                    boundary.Time,
                    boundary.Cycle,
                    FPMath.Clamp((boundary.Time - boundaryClip.StartTime) / boundaryClip.DurationTime,
                        FP._0, FP._1).AsFloat));
                traces.Add(new TimelineRuntimeTraceOutput(
                    content.Identity,
                    boundary.TrackAuthoringId,
                    boundary.AuthoringId,
                    boundary.Kind == TimelineRuntimeClipBoundaryKind.Enter
                        ? "timeline.clip.enter"
                        : "timeline.clip.exit",
                    TimelineTraceSeverity.Detail,
                    boundary.Kind == TimelineRuntimeClipBoundaryKind.Enter
                        ? "Clip boundary Enter." : "Clip boundary Exit.",
                    executionIdentity,
                    generation,
                    logicTick,
                    boundary.Time,
                    boundary.Cycle));
                if (boundaryClip.Parameters is not TimelineContentTree)
                    continue;
                TimelineContentClip treeClip = boundaryClip;
                treeClips.Add(new TimelineRuntimeTreeClipRequest(
                    treeClip.AuthoringId,
                    treeClip.TrackAuthoringId,
                    indexed.GraphId,
                    indexed.GraphRevision,
                    treeClip.TreeExecutionPhase,
                    treeClip.ExitSource,
                    boundary.Kind == TimelineRuntimeClipBoundaryKind.Enter
                        ? TimelineRuntimeTreeClipEventKind.Enter
                        : TimelineRuntimeTreeClipEventKind.Exit,
                    boundary.Time,
                    boundary.Time,
                    treeClip.StartTime,
                    boundary.Cycle,
                    FPMath.Clamp((boundary.Time - treeClip.StartTime) / treeClip.DurationTime,
                        FP._0, FP._1).AsFloat,
                    generation,
                    TimelineRuntimeTreeClipRequest.ComposeBranchRevision(boundary.Cycle)));
            }
            selected.Clear();
            TimelineRuntimeClipIndex cameraIndex = index.Logic.CameraAndScene;
            cameraIndex.Select(currentTime, currentTime, true, selected);
            for (int selectedIndex = 0; selectedIndex < selected.Count; selectedIndex++)
            {
                clipSampling.SampleCameraAndScene(cameraIndex[selected[selectedIndex]], content,
                    currentTime, currentCycle, executionIdentity, generation,
                    cameraStates, cameraResponses, cameraResources, scenePresentation);
            }
            selected.Clear();
            for (int activeIndex = 0; activeIndex < previousActiveClipIds.Count; activeIndex++)
            {
                int position = index.Logic.TreePositions[index.FindContent(previousActiveClipIds[activeIndex])];
                if (position >= 0)
                    TimelineRuntimeClipIndex.AddSelected(selected, position);
            }
            for (int boundaryIndex = 0; boundaryIndex < boundaries.Count; boundaryIndex++)
            {
                TimelineRuntimeClipBoundary boundary = boundaries[boundaryIndex];
                if (boundary.Kind != TimelineRuntimeClipBoundaryKind.Enter)
                    continue;
                int position = index.Logic.TreePositions[index[boundary.AuthoringId].ContentIndex];
                if (position >= 0)
                    TimelineRuntimeClipIndex.AddSelected(selected, position);
            }
            for (int selectedIndex = 0; selectedIndex < selected.Count; selectedIndex++)
            {
                TimelineRuntimeIndexedClip indexed = index.Logic.Trees[selected[selectedIndex]];
                TimelineContentClip treeClip = indexed.Clip;
                TimelineContentTrack treeTrack = indexed.Track;
                bool treeDecisionExit = treeClip.ExitSource == TimelineClipExitSource.TreeDecision;
                for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
                {
                    TimelineRuntimeEvaluationSegment segment = segments[segmentIndex];
                    if (TimelineRuntimePlayback.ContainsExit(exitedTreeDecisionClips, treeClip.AuthoringId, segment.Cycle))
                        continue;
                    if (segment.CurrentTime <= treeClip.StartTime)
                        continue;
                    bool finite = treeDecisionExit && ((TimelineContentTree)treeClip.Parameters).HasDeclaredEndTime;
                    if (finite && segment.PreviousTime >= treeClip.EndTime ||
                        !treeDecisionExit && segment.CurrentTime >= treeClip.EndTime)
                        continue;
                    FP sampleTime = finite ? FPMath.Min(segment.CurrentTime, treeClip.EndTime) : segment.CurrentTime;
                    FP samplePrevious = FPMath.Max(segment.PreviousTime, treeClip.StartTime);
                    float local = FPMath.Clamp((sampleTime - treeClip.StartTime) / treeClip.DurationTime,
                        FP._0, FP._1).AsFloat;
                    treeClips.Add(new TimelineRuntimeTreeClipRequest(treeClip.AuthoringId, treeTrack.AuthoringId,
                        indexed.GraphId, indexed.GraphRevision, treeClip.TreeExecutionPhase, treeClip.ExitSource,
                        TimelineRuntimeTreeClipEventKind.Update, sampleTime, samplePrevious, treeClip.StartTime,
                        segment.Cycle, local, generation,
                        TimelineRuntimeTreeClipRequest.ComposeBranchRevision(segment.Cycle)));
                    traces.Add(new TimelineRuntimeTraceOutput(content.Identity, treeTrack.AuthoringId,
                        treeClip.AuthoringId, "timeline.treeclip.update", TimelineTraceSeverity.Detail,
                        "TreeClip update candidate.", executionIdentity, generation, logicTick, sampleTime, segment.Cycle));
                }
            }
            selected.Clear();
            TimelineRuntimeClipIndex clipIndex = index.Logic.Clips;
            clipIndex.Select(currentTime, currentTime, false, selected);
            for (int selectedIndex = 0; selectedIndex < selected.Count; selectedIndex++)
            {
                TimelineContentClip clip = clipIndex[selected[selectedIndex]].Clip;
                if (currentTime == clip.StartTime)
                    continue;
                float duration = Math.Max(0.0001f, clip.DurationTime.AsFloat);
                clipSamples.Add(new TimelineRuntimeClipSample(
                    clip.AuthoringId,
                    clip.TrackAuthoringId,
                    clip.ContractKind,
                    TimelineRuntimeTreeClipEventKind.Update,
                    currentPosition,
                    currentCycle,
                    (currentTime - clip.StartTime).AsFloat / duration));
            }
            treeClips.Sort(TimelineRuntimeEvaluationStorage.TreeRequestComparison);
            return new TimelineRuntimeEvaluationResult(storage);
        }

        static void AppendMarkerRequests(
            TimelineRuntimeMarkerIndex index,
            FP duration,
            ulong generation,
            FP previousPosition,
            int previousCycle,
            FP currentPosition,
            int currentCycle,
            bool loop,
            bool includeStartBoundary,
            TimelineRuntimeSampleBuffer<TimelineRuntimeMarkerRequest> markers)
        {
            if (duration <= FP._0)
                return;
            FP previousTime = previousPosition;
            FP currentTime = currentPosition;
            if (currentCycle < previousCycle || currentCycle == previousCycle && currentTime < previousTime)
                throw new InvalidOperationException("Timeline logic cursor moved backward without a generation reset.");
            int firstCycle = loop ? previousCycle : 0;
            int lastCycle = loop ? currentCycle : 0;
            for (int cycle = firstCycle; cycle <= lastCycle; cycle++)
            {
                FP from = cycle == previousCycle ? previousTime : FP._0;
                FP to = cycle == currentCycle ? currentTime : duration;
                int first = index.Find(from, includeStartBoundary || cycle > previousCycle);
                for (int markerIndex = first; markerIndex < index.Count; markerIndex++)
                {
                    TimelineContentMarker marker = index[markerIndex];
                    if (marker.Time > to)
                        break;
                    markers.Add(new TimelineRuntimeMarkerRequest(
                        marker.MarkerId,
                        marker.TrackAuthoringId,
                        marker.GraphId,
                        marker.GraphRevision,
                        marker.Time,
                        cycle,
                        generation));
                }
            }
        }

    }
}
