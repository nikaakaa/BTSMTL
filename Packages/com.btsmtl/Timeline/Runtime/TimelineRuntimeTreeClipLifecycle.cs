using System;
using System.Collections.Generic;
using ThirdPersonSimulation;
using ThirdPersonSimulation.Fixed;

namespace BTSMTL.Timeline.Runtime
{
    public static class TimelineRuntimeTreeClipLifecycle
    {
        public static bool Consume(IAbilityTreeClipInvoker invoker, ulong actionInstanceId,
            TimelineRuntimeTreeClipRequest request, TimelineRuntimeStepContext context)
        {
            if (request.EventKind == TimelineRuntimeTreeClipEventKind.Update && !IsActive(request, context))
                return true;
            FixedScalar advance = context.Request.TargetTime - context.Request.PreviousTime;
            FixedScalar startFraction = AbilityTimelineTimeProjection.Fraction(context.Advance.PreviousTime,
                advance, context.Advance.PreviousCycle, context.Advance.Duration, request.PreviousTime, request.Cycle);
            FixedScalar endFraction = advance == FixedScalar.Zero && request.EventKind == TimelineRuntimeTreeClipEventKind.Update
                ? FixedScalar.One : AbilityTimelineTimeProjection.Fraction(context.Advance.PreviousTime,
                    advance, context.Advance.PreviousCycle, context.Advance.Duration, request.Time, request.Cycle);
            var invocation = new AbilityTreeClipInvocation(
                request.ClipAuthoringId,
                request.TreeGraphId,
                request.TreeGraphRevision,
                request.ClipAuthoringId,
                request.EventKind switch
                {
                    TimelineRuntimeTreeClipEventKind.Enter => AbilityTreeClipHook.OnEnable,
                    TimelineRuntimeTreeClipEventKind.Update => AbilityTreeClipHook.Root,
                    TimelineRuntimeTreeClipEventKind.Exit => AbilityTreeClipHook.OnDisable,
                    _ => throw new ArgumentOutOfRangeException(nameof(request))
                },
                request.Time,
                request.PreviousTime,
                request.WindowStartTime,
                request.Cycle,
                request.Generation,
                request.BranchRevision,
                actionInstanceId,
                context.Playback.PlaybackIdentity, startFraction, endFraction);
            return invoker.InvokeTreeClip(invocation);
        }

        static bool IsActive(TimelineRuntimeTreeClipRequest request, TimelineRuntimeStepContext context)
        {
            TimelineRuntimeSampleView<TimelineRuntimeTreeClipRequest> requests = context.Advance.Evaluation.TreeClips;
            for (int index = requests.Count - 1; index >= 0; index--)
            {
                TimelineRuntimeTreeClipRequest candidate = requests[index];
                if (candidate.Time > request.Time || candidate.Time == request.Time && candidate.EventKind > request.EventKind)
                    continue;
                if (candidate.EventKind != TimelineRuntimeTreeClipEventKind.Update &&
                    candidate.ClipAuthoringId == request.ClipAuthoringId && candidate.Cycle == request.Cycle)
                    return candidate.EventKind == TimelineRuntimeTreeClipEventKind.Enter;
            }
            List<AbilityTimelineTreeClipState> active = context.Playback.ActiveTreeClips;
            for (int index = 0; index < active.Count; index++)
                if (active[index].ClipAuthoringId == request.ClipAuthoringId && active[index].Cycle == request.Cycle)
                    return true;
            return false;
        }

        public static bool ConsumeMarker(IAbilityTreeClipInvoker invoker, ulong actionInstanceId,
            TimelineRuntimeMarkerRequest request, TimelineRuntimeStepContext context)
        {
            FixedScalar fraction = AbilityTimelineTimeProjection.Fraction(context.Advance.PreviousTime,
                context.Request.TargetTime - context.Request.PreviousTime, context.Advance.PreviousCycle,
                context.Advance.Duration, request.Time, request.Cycle);
            var invocation = new AbilityTreeClipInvocation(
                request.MarkerAuthoringId,
                request.GraphId,
                request.GraphRevision,
                request.MarkerAuthoringId,
                AbilityTreeClipHook.OnEnable,
                request.Time,
                request.Time,
                request.Time,
                request.Cycle,
                request.Generation,
                TimelineRuntimeTreeClipRequest.ComposeBranchRevision(request.Cycle),
                actionInstanceId,
                context.Playback.PlaybackIdentity, fraction, fraction);
            return invoker.InvokeTreeClip(invocation);
        }

        public static void Commit(TimelineRuntimeStepContext context)
        {
            TimelineRuntimeSampleView<TimelineRuntimeTreeClipRequest> requests = context.Advance.Evaluation.TreeClips;
            List<AbilityTimelineTreeClipState> clips = context.Playback.ActiveTreeClips;
            for (int i = 0; i < requests.Count; i++)
            {
                TimelineRuntimeTreeClipRequest request = requests[i];
                if (request.EventKind == TimelineRuntimeTreeClipEventKind.Update)
                    continue;
                for (int j = clips.Count - 1; j >= 0; j--)
                    if (clips[j].ClipAuthoringId == request.ClipAuthoringId && clips[j].Cycle == request.Cycle)
                    {
                        clips[j].Release();
                        clips.RemoveAt(j);
                    }
                if (request.EventKind != TimelineRuntimeTreeClipEventKind.Enter)
                    continue;
                var state = new AbilityTimelineTreeClipState(request.ClipAuthoringId, request.TreeGraphId,
                    request.TreeGraphRevision, request.ClipAuthoringId, request.Generation, request.BranchRevision, request.Cycle);
                state.Retain();
                clips.Add(state);
            }
            if (context.Advance.Completes)
                Clear(context.Playback);
        }

        public static bool ConsumeStop(TimelineRuntimePlayback playback, TimelinePlaybackStopCause cause,
            IAbilityTreeClipInvoker invoker, ulong actionInstanceId)
        {
            if (cause == TimelinePlaybackStopCause.Shutdown)
                return true;
            List<AbilityTimelineTreeClipState> clips = playback.ActiveTreeClips;
            for (int i = 0; i < clips.Count; i++)
            {
                AbilityTimelineTreeClipState clip = clips[i];
                var invocation = new AbilityTreeClipInvocation(clip.ClipAuthoringId, clip.TreeGraphId,
                    clip.TreeGraphRevision, clip.NodeAuthoringId, AbilityTreeClipHook.OnDestroy,
                    FixedScalar.Zero, FixedScalar.Zero, FixedScalar.Zero, clip.Cycle, clip.PlaybackGeneration,
                    clip.BranchRevision, actionInstanceId, playback.PlaybackIdentity, FixedScalar.Zero, FixedScalar.Zero);
                if (!invoker.InvokeTreeClip(invocation))
                    return false;
            }
            return true;
        }

        public static void Restore(TimelineRuntimePlayback playback, TimelineSnapshotItems<AbilityTimelineTreeClipState> restored)
        {
            Clear(playback);
            for (int i = 0; i < restored.Count; i++)
            {
                restored[i].Retain();
                playback.ActiveTreeClips.Add(restored[i]);
            }
        }

        public static void Clear(TimelineRuntimePlayback playback)
        {
            List<AbilityTimelineTreeClipState> clips = playback.ActiveTreeClips;
            for (int i = 0; i < clips.Count; i++)
                clips[i].Release();
            clips.Clear();
        }
    }
}
