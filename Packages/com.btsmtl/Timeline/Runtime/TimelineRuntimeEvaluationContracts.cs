using System;
using ThirdPersonSimulation.Fixed;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public readonly struct TimelineRuntimeTreeClipRequest
    {
        public TimelineRuntimeTreeClipRequest(
            string clipAuthoringId,
            string trackAuthoringId,
            string treeGraphId,
            string treeGraphRevision,
            TimelineTreeExecutionPhase phase,
            TimelineClipExitSource exitSource,
            TimelineRuntimeTreeClipEventKind eventKind,
            FixedScalar time,
            FixedScalar previousTime,
            FixedScalar windowStartTime,
            int cycle,
            float normalizedTime,
            ulong generation,
            ulong branchRevision)
        {
            ClipAuthoringId = string.IsNullOrWhiteSpace(clipAuthoringId)
                ? throw new ArgumentException("TreeClip identity is required.", nameof(clipAuthoringId))
                : clipAuthoringId.Trim();
            TrackAuthoringId = string.IsNullOrWhiteSpace(trackAuthoringId)
                ? throw new ArgumentException("Tree Track identity is required.", nameof(trackAuthoringId))
                : trackAuthoringId.Trim();
            TreeGraphId = string.IsNullOrWhiteSpace(treeGraphId)
                ? throw new ArgumentException("Tree graph identity is required.", nameof(treeGraphId))
                : treeGraphId.Trim();
            TreeGraphRevision = string.IsNullOrWhiteSpace(treeGraphRevision)
                ? throw new ArgumentException("Tree graph revision is required.", nameof(treeGraphRevision))
                : treeGraphRevision.Trim();
            Phase = phase;
            ExitSource = exitSource;
            EventKind = eventKind;
            Time = time;
            PreviousTime = previousTime;
            WindowStartTime = windowStartTime;
            Cycle = cycle;
            NormalizedTime = Math.Clamp(normalizedTime, 0f, 1f);
            Generation = generation == 0
                ? throw new ArgumentOutOfRangeException(nameof(generation))
                : generation;
            if (branchRevision == 0)
                throw new ArgumentOutOfRangeException(nameof(branchRevision));
            BranchRevision = branchRevision;
        }

        public string ClipAuthoringId { get; }
        public string TrackAuthoringId { get; }
        public string TreeGraphId { get; }
        public string TreeGraphRevision { get; }
        public TimelineTreeExecutionPhase Phase { get; }
        public TimelineClipExitSource ExitSource { get; }
        public TimelineRuntimeTreeClipEventKind EventKind { get; }
        public FixedScalar Time { get; }
        public FixedScalar PreviousTime { get; }
        public FixedScalar WindowStartTime { get; }
        public int Cycle { get; }
        public float NormalizedTime { get; }
        public ulong Generation { get; }
        public ulong BranchRevision { get; }

        public static ulong ComposeBranchRevision(int cycle)
        {
            if (cycle < 0)
                throw new ArgumentOutOfRangeException(nameof(cycle));
            return (ulong)cycle + 1UL;
        }
    }

    public readonly struct TimelineRuntimeMarkerRequest
    {
        public TimelineRuntimeMarkerRequest(
            string markerAuthoringId,
            string trackAuthoringId,
            string graphId,
            string graphRevision,
            FixedScalar time,
            int cycle,
            ulong generation)
        {
            MarkerAuthoringId = string.IsNullOrWhiteSpace(markerAuthoringId)
                ? throw new ArgumentException("Timeline Marker identity is required.", nameof(markerAuthoringId))
                : markerAuthoringId.Trim();
            TrackAuthoringId = string.IsNullOrWhiteSpace(trackAuthoringId)
                ? throw new ArgumentException("Timeline Track identity is required.", nameof(trackAuthoringId))
                : trackAuthoringId.Trim();
            GraphId = string.IsNullOrWhiteSpace(graphId)
                ? throw new ArgumentException("Timeline Marker graph identity is required.", nameof(graphId))
                : graphId.Trim();
            GraphRevision = string.IsNullOrWhiteSpace(graphRevision)
                ? throw new ArgumentException("Timeline Marker graph revision is required.", nameof(graphRevision))
                : graphRevision.Trim();
            if (time < FixedScalar.Zero || cycle < 0)
                throw new ArgumentOutOfRangeException(nameof(time));
            Generation = generation == 0
                ? throw new ArgumentOutOfRangeException(nameof(generation))
                : generation;
            Time = time;
            Cycle = cycle;
        }

        public string MarkerAuthoringId { get; }
        public string TrackAuthoringId { get; }
        public string GraphId { get; }
        public string GraphRevision { get; }
        public FixedScalar Time { get; }
        public int Cycle { get; }
        public ulong Generation { get; }
    }

    public enum TimelineRuntimeTreeClipEventKind : byte
    {
        Enter = 1,
        Update = 2,
        Exit = 3,
        Destroy = 4
    }

    public readonly struct TimelineRuntimePresentationEvent
    {
        public TimelineRuntimePresentationEvent(
            TimelineRuntimePlaybackHandle playbackHandle,
            TimelineExecutionIdentity executionIdentity,
            ulong generation,
            string markerAuthoringId,
            ulong traversalIndex,
            string graphId,
            string graphRevision,
            FixedScalar time,
            int cycle)
        {
            if (!playbackHandle.IsValid || !executionIdentity.IsValid || generation == 0 || traversalIndex == 0)
                throw new ArgumentException("Timeline presentation event execution identity is invalid.", nameof(executionIdentity));
            PlaybackHandle = playbackHandle;
            ExecutionIdentity = executionIdentity;
            Generation = generation;
            MarkerAuthoringId = string.IsNullOrWhiteSpace(markerAuthoringId)
                ? throw new ArgumentException("Timeline presentation event marker identity is required.", nameof(markerAuthoringId))
                : markerAuthoringId.Trim();
            GraphId = string.IsNullOrWhiteSpace(graphId)
                ? throw new ArgumentException("Timeline presentation marker graph identity is required.", nameof(graphId))
                : graphId.Trim();
            GraphRevision = string.IsNullOrWhiteSpace(graphRevision)
                ? throw new ArgumentException("Timeline presentation marker graph revision is required.", nameof(graphRevision))
                : graphRevision.Trim();
            if (time < FixedScalar.Zero || cycle < 0)
                throw new ArgumentOutOfRangeException(nameof(time));
            TraversalIndex = traversalIndex;
            Time = time;
            Cycle = cycle;
            Span<byte> block = stackalloc byte[64];
            var builder = new EventIdBuilder(block);
            builder.Append("btsmtl-timeline-presentation-marker");
            builder.Append(PlaybackHandle.Value);
            builder.Append(":", false);
            builder.Append(Generation, false);
            builder.Append(":", false);
            builder.Append(MarkerAuthoringId, false);
            builder.Append(":", false);
            builder.Append(TraversalIndex, false);
            EventId = builder.Build();
        }

        public EventId EventId { get; }
        public TimelineRuntimePlaybackHandle PlaybackHandle { get; }
        public TimelineExecutionIdentity ExecutionIdentity { get; }
        public ulong Generation { get; }
        public string MarkerAuthoringId { get; }
        public ulong TraversalIndex { get; }
        public string GraphId { get; }
        public string GraphRevision { get; }
        public FixedScalar Time { get; }
        public int Cycle { get; }
    }

    public readonly struct TimelineRuntimeClipSample
    {
        public TimelineRuntimeClipSample(
            string clipAuthoringId,
            string trackAuthoringId,
            string contractKind,
            TimelineRuntimeTreeClipEventKind eventKind,
            FixedScalar time,
            int cycle,
            float normalizedTime)
        {
            ClipAuthoringId = string.IsNullOrWhiteSpace(clipAuthoringId)
                ? throw new ArgumentException("Timeline Clip identity is required.", nameof(clipAuthoringId))
                : clipAuthoringId.Trim();
            TrackAuthoringId = string.IsNullOrWhiteSpace(trackAuthoringId)
                ? throw new ArgumentException("Timeline Track identity is required.", nameof(trackAuthoringId))
                : trackAuthoringId.Trim();
            ContractKind = string.IsNullOrWhiteSpace(contractKind)
                ? throw new ArgumentException("Timeline Clip contract kind is required.", nameof(contractKind))
                : contractKind.Trim();
            EventKind = eventKind;
            Time = time;
            Cycle = cycle;
            NormalizedTime = Math.Clamp(normalizedTime, 0f, 1f);
        }

        public string ClipAuthoringId { get; }
        public string TrackAuthoringId { get; }
        public string ContractKind { get; }
        public TimelineRuntimeTreeClipEventKind EventKind { get; }
        public FixedScalar Time { get; }
        public int Cycle { get; }
        public float NormalizedTime { get; }
    }

    public readonly struct TimelineRuntimeScenePresentationSample
    {
        public TimelineRuntimeScenePresentationSample(
            string clipAuthoringId,
            string targetBindingId,
            string parameterBindingId,
            TimelineBindingValueKind valueKind,
            float value,
            float normalizedTime,
            FixedScalar time,
            int cycle,
            TimelineExecutionIdentity executionIdentity,
            ulong generation)
        {
            ClipAuthoringId = clipAuthoringId ?? string.Empty;
            TargetBindingId = targetBindingId ?? string.Empty;
            ParameterBindingId = parameterBindingId ?? string.Empty;
            ValueKind = valueKind;
            Value = value;
            NormalizedTime = Math.Clamp(normalizedTime, 0f, 1f);
            Time = time;
            Cycle = cycle;
            ExecutionIdentity = executionIdentity;
            Generation = generation;
        }

        public string ClipAuthoringId { get; }
        public string TargetBindingId { get; }
        public string ParameterBindingId { get; }
        public TimelineBindingValueKind ValueKind { get; }
        public float Value { get; }
        public float NormalizedTime { get; }
        public FixedScalar Time { get; }
        public int Cycle { get; }
        public TimelineExecutionIdentity ExecutionIdentity { get; }
        public ulong Generation { get; }
    }


    public readonly struct TimelineRuntimeTraceOutput
    {
        public TimelineRuntimeTraceOutput(
            string timelineAuthoringId,
            string trackAuthoringId,
            string clipAuthoringId,
            string code,
            TimelineTraceSeverity severity,
            string detail,
            TimelineExecutionIdentity executionIdentity,
            ulong generation,
            ulong logicTick,
            FixedScalar time,
            int cycle)
        {
            TimelineAuthoringId = string.IsNullOrWhiteSpace(timelineAuthoringId)
                ? throw new ArgumentException("Timeline identity is required.", nameof(timelineAuthoringId))
                : timelineAuthoringId.Trim();
            TrackAuthoringId = trackAuthoringId?.Trim() ?? string.Empty;
            ClipAuthoringId = clipAuthoringId?.Trim() ?? string.Empty;
            Code = string.IsNullOrWhiteSpace(code)
                ? throw new ArgumentException("Timeline trace code is required.", nameof(code))
                : code.Trim();
            if (!executionIdentity.IsValid)
                throw new ArgumentException("Timeline trace execution identity is required.", nameof(executionIdentity));
            if (generation == 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            if (logicTick == 0)
                throw new ArgumentOutOfRangeException(nameof(logicTick));
            if (time < FixedScalar.Zero)
                throw new ArgumentOutOfRangeException(nameof(time));
            if (cycle < 0)
                throw new ArgumentOutOfRangeException(nameof(cycle));
            Severity = severity;
            Detail = detail ?? string.Empty;
            ExecutionIdentity = executionIdentity;
            Generation = generation;
            LogicTick = logicTick;
            Time = time;
            Cycle = cycle;
        }

        public string TimelineAuthoringId { get; }
        public string TrackAuthoringId { get; }
        public string ClipAuthoringId { get; }
        public string Code { get; }
        public TimelineTraceSeverity Severity { get; }
        public string Detail { get; }
        public TimelineExecutionIdentity ExecutionIdentity { get; }
        public ulong Generation { get; }
        public ulong LogicTick { get; }
        public FixedScalar Time { get; }
        public int Cycle { get; }
    }

    public readonly struct TimelineRuntimeEvaluationResult
    {
        internal bool IsCurrent => AnimationContributions.IsCurrent;

        internal TimelineRuntimeEvaluationResult(TimelineRuntimeEvaluationStorage storage)
        {
            AnimationContributions = storage.AnimationContributions.View;
            MotionContributions = storage.MotionContributions.View;
            CameraStates = storage.CameraStates.View;
            CameraResponses = storage.CameraResponses.View;
            CameraResources = storage.CameraResources.View;
            TreeClips = storage.TreeClips.View;
            Markers = storage.Markers.View;
            ScenePresentation = storage.ScenePresentation.View;
            ClipSamples = storage.ClipSamples.View;
            Traces = storage.Traces.View;
        }

        public TimelineRuntimeSampleView<TimelineAnimationContribution> AnimationContributions { get; }
        public TimelineRuntimeSampleView<TimelineMotionCurveContribution> MotionContributions { get; }
        public TimelineRuntimeSampleView<TimelineCameraStateSample> CameraStates { get; }
        public TimelineRuntimeSampleView<TimelineCameraResponseSample> CameraResponses { get; }
        public TimelineRuntimeSampleView<TimelineCameraResourceSample> CameraResources { get; }
        public TimelineRuntimeSampleView<TimelineRuntimeTreeClipRequest> TreeClips { get; }
        public TimelineRuntimeSampleView<TimelineRuntimeMarkerRequest> Markers { get; }
        public TimelineRuntimeSampleView<TimelineRuntimeScenePresentationSample> ScenePresentation { get; }
        public TimelineRuntimeSampleView<TimelineRuntimeClipSample> ClipSamples { get; }
        public TimelineRuntimeSampleView<TimelineRuntimeTraceOutput> Traces { get; }
    }
}
