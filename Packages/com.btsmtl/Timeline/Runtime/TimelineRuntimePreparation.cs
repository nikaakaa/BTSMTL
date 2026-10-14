using System;
using Photon.Deterministic;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public readonly struct TimelineRuntimePrepareRequest
    {
        public TimelineRuntimePrepareRequest(
            CanonicalTextValue requestId,
            TimelineRuntimePreparedContent content,
            TimelineExecutionIdentity executionIdentity,
            TimelinePlaybackMode playbackMode,
            FP startTime = default)
        {
            RequestId = CanonicalTextValue.IsNullOrWhiteSpace(requestId)
                ? throw new ArgumentException("Timeline prepare request identity is required.", nameof(requestId))
                : requestId.Trim();
            Content = content;
            if (!executionIdentity.IsValid)
                throw new ArgumentException("Timeline execution identity is invalid.", nameof(executionIdentity));
            ExecutionIdentity = executionIdentity;
            if (playbackMode != TimelinePlaybackMode.Once && playbackMode != TimelinePlaybackMode.Loop &&
                playbackMode != TimelinePlaybackMode.HoldLastFrame)
                throw new ArgumentOutOfRangeException(nameof(playbackMode));
            PlaybackMode = playbackMode;
            if (content.IsReady && (startTime < FP._0 ||
                startTime != FP._0 && startTime >= content.Content.Duration))
                throw new ArgumentOutOfRangeException(nameof(startTime), "Timeline start time must precede the end of its content.");
            StartTime = startTime;
        }

        public CanonicalTextValue RequestId { get; }
        public TimelineRuntimePreparedContent Content { get; }
        public TimelineExecutionIdentity ExecutionIdentity { get; }
        public TimelinePlaybackMode PlaybackMode { get; }
        public FP StartTime { get; }
    }

    public enum TimelineRuntimePreparationStatus : byte
    {
        Failed = 0,
        Ready = 1
    }

    public sealed class TimelineRuntimePreparedContent
    {
        internal TimelineRuntimePreparedContent(TimelineRuntimePreparedContent source,
            TimelineContentUnit content)
        {
            Status = source.Status;
            NumericTarget = source.NumericTarget;
            Content = content;
            Index = new TimelineRuntimeContentIndex(content);
            MotionSampling = new TimelineRuntimeMotionSampling(Index.Logic.Motions, source.MotionSampling);
            ClipSampling = source.ClipSampling;
            BindingPlan = source.BindingPlan;
            CallInput = source.CallInput;
            PreparedBindings = source.PreparedBindings;
            PreparedDependencies = source.PreparedDependencies;
            Errors = source.Errors;
        }

        public void RefreshTimeRanges()
        {
            Index.RefreshTimeRanges();
            MotionSampling.RefreshTimeRanges();
        }

        internal TimelineRuntimePreparedContent(
            TimelineRuntimePreparationStatus status,
            TimelineRuntimeNumericTarget numericTarget,
            TimelineContentUnit content,
            TimelineBindingPlan bindingPlan,
            TimelineCallInput callInput,
            TimelinePreparedBindings preparedBindings,
            TimelineRuntimePreparedDependencies preparedDependencies,
            IReadOnlyList<string> errors)
        {
            Status = status;
            NumericTarget = numericTarget;
            Index = status == TimelineRuntimePreparationStatus.Ready
                ? new TimelineRuntimeContentIndex(content)
                : null;
            MotionSampling = status == TimelineRuntimePreparationStatus.Ready
                ? new TimelineRuntimeMotionSampling(Index.Logic.Motions, numericTarget)
                : null;
            ClipSampling = status == TimelineRuntimePreparationStatus.Ready ? new TimelineRuntimeClipSampling(content) : null;
            Content = content;
            BindingPlan = bindingPlan;
            CallInput = callInput;
            PreparedBindings = preparedBindings;
            PreparedDependencies = preparedDependencies;
            Errors = errors ?? Array.Empty<string>();
        }

        public TimelineRuntimePreparationStatus Status { get; }
        public TimelineRuntimeNumericTarget NumericTarget { get; }
        internal TimelineRuntimeContentIndex Index { get; }
        internal TimelineRuntimeMotionSampling MotionSampling { get; }
        internal TimelineRuntimeClipSampling ClipSampling { get; }
        public TimelineContentUnit Content { get; }
        public CanonicalTextValue ContentRevision => Content == null ? CanonicalTextValue.Empty : Content.Revision;
        public TimelineBindingPlan BindingPlan { get; }
        public TimelineCallInput CallInput { get; }
        public TimelinePreparedBindings PreparedBindings { get; }
        public TimelineRuntimePreparedDependencies PreparedDependencies { get; }
        public IReadOnlyList<string> Errors { get; }
        public bool IsReady => Status == TimelineRuntimePreparationStatus.Ready;

        internal static TimelineRuntimePreparedContent Failed(
            TimelineRuntimeNumericTarget numericTarget,
            IEnumerable<string> errors)
        {
            return new TimelineRuntimePreparedContent(
                TimelineRuntimePreparationStatus.Failed,
                numericTarget,
                null,
                null,
                null,
                null,
                null,
                new List<string>(errors ?? Array.Empty<string>()).AsReadOnly());
        }

        internal static TimelineRuntimePreparedContent Ready(
            TimelineRuntimeNumericTarget numericTarget,
            TimelineContentUnit content,
            TimelineBindingPlan bindingPlan,
            TimelineCallInput callInput,
            TimelinePreparedBindings preparedBindings,
            TimelineRuntimePreparedDependencies preparedDependencies)
        {
            return new TimelineRuntimePreparedContent(
                TimelineRuntimePreparationStatus.Ready,
                numericTarget,
                content,
                bindingPlan,
                callInput,
                preparedBindings,
                preparedDependencies,
                Array.Empty<string>());
        }
    }

    public sealed class TimelineRuntimeTargetSampling
    {
        readonly TimelineRuntimePreparedContent m_Content;
        readonly TimelineRuntimePresentationBuffer m_Presentation;
        readonly TimelineRuntimeSampleBuffer<TimelineMotionCurveContribution> m_Motions;

        public TimelineRuntimeTargetSampling(TimelineRuntimePreparedContent content)
        {
            m_Content = content;
            m_Presentation = new TimelineRuntimePresentationBuffer(content.Content, TimelinePlaybackMode.Once);
            int motions = content.Index.Logic.Motions.Count;
            m_Motions = new TimelineRuntimeSampleBuffer<TimelineMotionCurveContribution>(motions);
        }

        public int MotionClipCapacity => m_Content.Index.Logic.Motions.Count;

        public TimelineRuntimeSampleView<TimelineAnimationContribution> SampleAnimation(FP time)
        {
            m_Presentation.Clear();
            TimelineRuntimePresentationEvaluator.SampleAnimations(m_Content.Content, m_Content.Index,
                m_Content.ClipSampling, time, 0, false, m_Presentation);
            return m_Presentation.Animations.View;
        }

        public TimelineRuntimeSampleView<TimelineMotionCurveContribution> SampleMotion(FP previous, FP current)
        {
            var segments = new TimelineRuntimeEvaluationSegments(previous, 0, current, 0,
                m_Content.Content.Duration, false);
            m_Motions.Clear();
            m_Content.MotionSampling.Sample(m_Content.Content, m_Content.Index.Logic.Motions,
                segments, m_Presentation.SamplingClips, m_Motions);
            return m_Motions.View;
        }
    }

    public readonly struct TimelineRuntimePreparationResult
    {
        readonly TimelineRuntimePreparedContent m_Content;

        internal TimelineRuntimePreparationResult(TimelineRuntimePrepareRequest request)
        {
            m_Content = request.Content;
            Status = request.Content.Status;
            RequestId = request.RequestId;
            ExecutionIdentity = request.ExecutionIdentity;
            PlaybackMode = request.PlaybackMode;
            StartTime = request.StartTime;
        }

        public TimelineRuntimePreparationStatus Status { get; }
        public CanonicalTextValue RequestId { get; }
        public TimelineExecutionIdentity ExecutionIdentity { get; }
        public TimelinePlaybackMode PlaybackMode { get; }
        public FP StartTime { get; }
        public TimelineRuntimeNumericTarget NumericTarget => m_Content.NumericTarget;
        internal TimelineRuntimeContentIndex Index => m_Content.Index;
        internal TimelineRuntimeMotionSampling MotionSampling => m_Content.MotionSampling;
        internal TimelineRuntimeClipSampling ClipSampling => m_Content.ClipSampling;
        public TimelineContentUnit Content => m_Content.Content;
        public CanonicalTextValue ContentRevision => m_Content.ContentRevision;
        public TimelineBindingPlan BindingPlan => m_Content.BindingPlan;
        public TimelineCallInput CallInput => m_Content.CallInput;
        public TimelinePreparedBindings PreparedBindings => m_Content.PreparedBindings;
        public TimelineRuntimePreparedDependencies PreparedDependencies => m_Content.PreparedDependencies;
        public IReadOnlyList<string> Errors => m_Content.Errors;
        public bool IsReady => Status == TimelineRuntimePreparationStatus.Ready;
    }

    public static class TimelineRuntimePreparation
    {
        public static TimelineRuntimePreparedContent PrepareContent(
            TimelineContentUnit content,
            TimelineRuntimeNumericTarget numericTarget,
            IReadOnlyList<TimelineCallBinding> callBindings,
            ITimelineDomainBindingResolver domainResolver,
            ITimelineRuntimeDependencyResolver dependencyResolver)
        {
            try
            {
                var errors = new List<string>();
                var dependencyHandles = new List<TimelineRuntimeDependencyHandle>(content.Dependencies.Count);
                for (int index = 0; index < content.Dependencies.Count; index++)
                {
                    TimelineContentDependency dependency = content.Dependencies[index];
                    if (!dependencyResolver.TryResolve(
                            dependency,
                            numericTarget,
                            out TimelineRuntimeDependencyHandle handle,
                            out string error) || !handle.IsValid)
                    {
                        errors.Add($"timeline_dependency_unresolved:{dependency.Identity}:{error ?? "dependency is unresolved"}");
                        continue;
                    }
                    dependencyHandles.Add(handle);
                }
                if (errors.Count != 0)
                    return TimelineRuntimePreparedContent.Failed(
                        numericTarget,
                        errors);

                TimelineBindingPlan bindingPlan = new TimelineBindingPlan(content);
                TimelineCallInput callInput = new TimelineCallInput(bindingPlan, callBindings);
                TimelinePreparedBindings preparedBindings = TimelineBindingPreparation.Prepare(
                    bindingPlan,
                    callInput,
                    domainResolver,
                    errors);
                if (preparedBindings == null || errors.Count != 0)
                    return TimelineRuntimePreparedContent.Failed(
                        numericTarget,
                        errors);
                return TimelineRuntimePreparedContent.Ready(
                    numericTarget,
                        content,
                    bindingPlan,
                    callInput,
                    preparedBindings,
                    new TimelineRuntimePreparedDependencies(content.Dependencies, dependencyHandles));
            }
            catch (Exception exception)
            {
                return TimelineRuntimePreparedContent.Failed(
                    numericTarget,
                    new[] { exception.Message });
            }
        }

        internal static TimelineRuntimePlayback CreatePlayback(
            TimelineRuntimePreparationResult preparation,
            TimelineRuntimePlaybackHandle handle,
            ulong generation,
            int tickRate,
            TimelineRuntimeEvaluationStoragePool evaluationStoragePool)
        {
            if (!preparation.IsReady)
                throw new InvalidOperationException("Timeline playback cannot be created from a failed preparation.");
            if (!handle.IsValid)
                throw new ArgumentOutOfRangeException(nameof(handle));
            if (generation == 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            return new TimelineRuntimePlayback(
                handle,
                generation,
                preparation,
                tickRate,
                evaluationStoragePool);
        }
    }

    readonly struct TimelineRuntimeIndexedClip
    {
        readonly TimelineContentUnit m_Content;
        public TimelineRuntimeIndexedClip(TimelineContentUnit content, int trackIndex, int clipIndex, int contentIndex)
        {
            m_Content = content;
            TrackIndex = trackIndex;
            ClipIndex = clipIndex;
            ContentIndex = contentIndex;
        }

        public TimelineContentClip Clip => m_Content.Clips[ContentIndex];
        public TimelineContentTrack Track => m_Content.Tracks[TrackIndex];
        public string GraphId => Clip.GraphBinding.GraphId;
        public string GraphRevision => Clip.GraphBinding.Revision;
        public readonly int TrackIndex;
        public readonly int ClipIndex;
        public readonly int ContentIndex;
    }

    sealed class TimelineRuntimeClipIndex
    {
        readonly TimelineRuntimeIndexedClip[] m_Clips;
        readonly int[] m_StartOrder;
        readonly int[] m_EndOrder;
        readonly FP[] m_Ends;
        readonly FP[] m_SubtreeEnds;
        readonly Func<TimelineContentClip, FP> m_EndTime;
        readonly Comparison<int> m_StartComparison;
        readonly Comparison<int> m_EndComparison;

        public TimelineRuntimeClipIndex(List<TimelineRuntimeIndexedClip> clips, Func<TimelineContentClip, FP> endTime)
        {
            m_Clips = clips.ToArray();
            m_StartOrder = new int[clips.Count];
            m_EndOrder = new int[clips.Count];
            m_Ends = new FP[clips.Count];
            m_SubtreeEnds = new FP[clips.Count];
            m_EndTime = endTime;
            m_StartComparison = CompareStart;
            m_EndComparison = CompareEnd;
            RefreshTimeRanges();
        }

        internal void RefreshTimeRanges()
        {
            for (int index = 0; index < m_Clips.Length; index++)
            {
                m_StartOrder[index] = index;
                m_EndOrder[index] = index;
                m_Ends[index] = m_EndTime(m_Clips[index].Clip);
            }
            Array.Sort(m_StartOrder, m_StartComparison);
            Array.Sort(m_EndOrder, m_EndComparison);
            BuildSubtreeEnds(0, m_Clips.Length);
        }

        int CompareStart(int left, int right)
        {
            int time = m_Clips[left].Clip.StartTime.CompareTo(m_Clips[right].Clip.StartTime);
            return time != 0 ? time : left.CompareTo(right);
        }

        int CompareEnd(int left, int right)
        {
            int time = m_Ends[left].CompareTo(m_Ends[right]);
            return time != 0 ? time : left.CompareTo(right);
        }

        public int Count => m_Clips.Length;
        public TimelineRuntimeIndexedClip this[int index] => m_Clips[index];
        public int StartIndexAt(int index) => m_StartOrder[index];
        public TimelineRuntimeIndexedClip StartAt(int index) => m_Clips[m_StartOrder[index]];
        public TimelineRuntimeIndexedClip EndAt(int index) => m_Clips[m_EndOrder[index]];
        public FP EndTimeAt(int index) => m_Ends[m_EndOrder[index]];

        public int FindStart(FP time, bool includeEqual) => Find(time, includeEqual, true);
        public int FindEnd(FP time, bool includeEqual) => Find(time, includeEqual, false);

        int Find(FP time, bool includeEqual, bool start)
        {
            int lower = 0;
            int upper = Count;
            while (lower < upper)
            {
                int middle = lower + (upper - lower) / 2;
                FP position = start ? StartAt(middle).Clip.StartTime : EndTimeAt(middle);
                if (position < time || !includeEqual && position == time)
                    lower = middle + 1;
                else
                    upper = middle;
            }
            return lower;
        }

        FP BuildSubtreeEnds(int lower, int upper)
        {
            if (lower == upper)
                return FP.MinValue;
            int middle = lower + (upper - lower) / 2;
            FP end = m_Ends[m_StartOrder[middle]];
            end = FPMath.Max(end, BuildSubtreeEnds(lower, middle));
            end = FPMath.Max(end, BuildSubtreeEnds(middle + 1, upper));
            m_SubtreeEnds[middle] = end;
            return end;
        }

        public void Select(FP previous, FP current, bool includeEnd, List<int> selected)
        {
            Select(0, Count, previous, current, includeEnd, selected);
        }

        void Select(int lower, int upper, FP previous, FP current, bool includeEnd, List<int> selected)
        {
            if (lower == upper)
                return;
            int middle = lower + (upper - lower) / 2;
            FP subtreeEnd = m_SubtreeEnds[middle];
            if (subtreeEnd < previous || !includeEnd && subtreeEnd == previous)
                return;
            Select(lower, middle, previous, current, includeEnd, selected);
            int index = m_StartOrder[middle];
            if (m_Clips[index].Clip.StartTime > current)
                return;
            FP end = m_Ends[index];
            if (end > previous || includeEnd && end == previous)
                AddSelected(selected, index);
            Select(middle + 1, upper, previous, current, includeEnd, selected);
        }

        public static void AddSelected(List<int> selected, int index)
        {
            int lower = 0;
            int upper = selected.Count;
            while (lower < upper)
            {
                int middle = lower + (upper - lower) / 2;
                if (selected[middle] < index)
                    lower = middle + 1;
                else
                    upper = middle;
            }
            if (lower == selected.Count || selected[lower] != index)
                selected.Insert(lower, index);
        }
    }

    sealed class TimelineRuntimeMarkerIndex
    {
        readonly TimelineContentUnit m_Content;
        readonly int[] m_Markers;
        readonly int[] m_InitialSeekMarkers;

        public TimelineRuntimeMarkerIndex(TimelineContentUnit content, TimelineExecutionDomain domain)
        {
            m_Content = content;
            var markers = new List<int>();
            var initialSeekMarkers = new List<int>();
            for (int index = 0; index < content.Markers.Count; index++)
                if (!content.Markers[index].TrackMuted && content.Markers[index].ExecutionPolicy.Domain == domain)
                {
                    if (content.Markers[index].ForceTriggerOnInitialSeek)
                        initialSeekMarkers.Add(markers.Count);
                    markers.Add(index);
                }
            m_Markers = markers.ToArray();
            m_InitialSeekMarkers = initialSeekMarkers.ToArray();
        }

        public int Count => m_Markers.Length;
        public int ContentIndexAt(int index) => m_Markers[index];
        public TimelineContentMarker this[int index] => m_Content.Markers[m_Markers[index]];

        public void Select(FP from, FP to, bool includeEqual, bool initialSeek, List<int> selected)
        {
            selected.Clear();
            if (initialSeek)
                for (int index = 0; index < m_InitialSeekMarkers.Length; index++)
                {
                    int markerIndex = m_InitialSeekMarkers[index];
                    if (this[markerIndex].Time >= from || this[markerIndex].Time > to)
                        break;
                    selected.Add(markerIndex);
                }
            for (int index = Find(from, includeEqual); index < Count; index++)
            {
                if (this[index].Time > to)
                    break;
                selected.Add(index);
            }
        }

        public int Find(FP time, bool includeEqual)
        {
            int lower = 0;
            int upper = Count;
            while (lower < upper)
            {
                int middle = lower + (upper - lower) / 2;
                FP position = this[middle].Time;
                if (position < time || !includeEqual && position == time)
                    lower = middle + 1;
                else
                    upper = middle;
            }
            return lower;
        }
    }

    sealed class TimelineRuntimeDomainIndex
    {
        internal void RefreshTimeRanges()
        {
            Clips.RefreshTimeRanges();
            Animations.RefreshTimeRanges();
            Motions.RefreshTimeRanges();
            CameraAndScene.RefreshTimeRanges();
            Lifetimes.RefreshTimeRanges();
            TreeLifetimes.RefreshTimeRanges();
        }

        public TimelineRuntimeDomainIndex(List<TimelineRuntimeIndexedClip> clips,
            TimelineContentUnit content, TimelineExecutionDomain domain)
        {
            var animations = new List<TimelineRuntimeIndexedClip>();
            var motions = new List<TimelineRuntimeIndexedClip>();
            var cameraAndScene = new List<TimelineRuntimeIndexedClip>();
            var trees = new List<TimelineRuntimeIndexedClip>();
            for (int index = 0; index < clips.Count; index++)
            {
                TimelineRuntimeIndexedClip clip = clips[index];
                switch (clip.Clip.Parameters)
                {
                    case TimelineContentAnimation: animations.Add(clip); break;
                    case TimelineContentMotion: motions.Add(clip); break;
                    case TimelineContentCameraState:
                    case TimelineContentCameraResponse:
                    case TimelineContentCameraEffect:
                    case TimelineContentSceneParameter: cameraAndScene.Add(clip); break;
                    case TimelineContentTree: trees.Add(clip); break;
                }
            }
            Clips = new TimelineRuntimeClipIndex(clips, ClipEnd);
            Animations = new TimelineRuntimeClipIndex(animations, AnimationEnd);
            Motions = new TimelineRuntimeClipIndex(motions, ClipEnd);
            CameraAndScene = new TimelineRuntimeClipIndex(cameraAndScene, ClipEnd);
            Trees = trees.ToArray();
            TreePositions = new int[content.Clips.Count];
            Array.Fill(TreePositions, -1);
            for (int index = 0; index < trees.Count; index++)
                TreePositions[trees[index].ContentIndex] = index;
            clips.Sort((left, right) => left.ContentIndex.CompareTo(right.ContentIndex));
            Lifetimes = new TimelineRuntimeClipIndex(clips, LifetimeEnd);
            trees.Sort((left, right) => left.ContentIndex.CompareTo(right.ContentIndex));
            TreeLifetimes = new TimelineRuntimeClipIndex(trees, LifetimeEnd);
            Markers = new TimelineRuntimeMarkerIndex(content, domain);
        }

        static FP ClipEnd(TimelineContentClip clip) => clip.EndTime;
        static FP AnimationEnd(TimelineContentClip clip) => ((TimelineContentAnimation)clip.Parameters).Extrapolation == ExtraPolationMode.Hold
            ? FP.MaxValue : clip.EndTime;
        static FP LifetimeEnd(TimelineContentClip clip) => clip.ExitSource == TimelineClipExitSource.TreeDecision
            ? FP.MaxValue : clip.EndTime;

        public readonly TimelineRuntimeClipIndex Clips;
        public readonly TimelineRuntimeClipIndex Animations;
        public readonly TimelineRuntimeClipIndex Motions;
        public readonly TimelineRuntimeClipIndex CameraAndScene;
        public readonly TimelineRuntimeIndexedClip[] Trees;
        public readonly int[] TreePositions;
        public readonly TimelineRuntimeClipIndex Lifetimes;
        public readonly TimelineRuntimeClipIndex TreeLifetimes;
        public readonly TimelineRuntimeMarkerIndex Markers;
    }

    sealed class TimelineRuntimeContentIndex
    {
        internal void RefreshTimeRanges()
        {
            Logic.RefreshTimeRanges();
            Presentation.RefreshTimeRanges();
        }

        readonly Dictionary<string, TimelineRuntimeIndexedClip> m_Clips;
        readonly TimelineContentUnit m_Content;

        public TimelineRuntimeContentIndex(TimelineContentUnit content)
        {
            m_Content = content;
            m_Clips = new Dictionary<string, TimelineRuntimeIndexedClip>(content.Clips.Count, StringComparer.Ordinal);
            var contentIndices = new Dictionary<string, int>(content.Clips.Count, StringComparer.Ordinal);
            for (int index = 0; index < content.Clips.Count; index++)
                contentIndices.Add(content.Clips[index].AuthoringId, index);
            var logic = new List<TimelineRuntimeIndexedClip>();
            var presentation = new List<TimelineRuntimeIndexedClip>();
            for (int trackIndex = 0; trackIndex < content.Tracks.Count; trackIndex++)
            {
                TimelineContentTrack track = content.Tracks[trackIndex];
                for (int clipIndex = 0; clipIndex < track.ClipAuthoringIds.Count; clipIndex++)
                {
                    string identity = track.ClipAuthoringIds[clipIndex];
                    var indexed = new TimelineRuntimeIndexedClip(content, trackIndex, clipIndex, contentIndices[identity]);
                    m_Clips.Add(identity, indexed);
                    if (!track.Muted)
                        (track.ExecutionPolicy.IsLogic ? logic : presentation).Add(indexed);
                }
            }
            Logic = new TimelineRuntimeDomainIndex(logic, content, TimelineExecutionDomain.Logic);
            Presentation = new TimelineRuntimeDomainIndex(presentation, content, TimelineExecutionDomain.Presentation);
        }

        public readonly TimelineRuntimeDomainIndex Logic;
        public readonly TimelineRuntimeDomainIndex Presentation;
        public TimelineRuntimeIndexedClip this[string identity] => m_Clips[identity];
        public bool TryResolveClip(string identity, out TimelineContentClip clip)
        {
            bool found = m_Clips.TryGetValue(identity, out TimelineRuntimeIndexedClip indexed);
            clip = indexed.Clip;
            return found;
        }

        public int FindContent(CanonicalTextValue identity)
        {
            int lower = 0;
            int upper = m_Content.Clips.Count - 1;
            while (lower <= upper)
            {
                int middle = lower + (upper - lower) / 2;
                int order = CanonicalTextValue.CompareOrdinal(m_Content.Clips[middle].AuthoringId, identity);
                if (order == 0)
                    return middle;
                if (order < 0)
                    lower = middle + 1;
                else
                    upper = middle - 1;
            }
            return -1;
        }

        public string SectionAt(FP time)
        {
            int lower = 0;
            int upper = m_Content.Sections.Count;
            while (lower < upper)
            {
                int middle = lower + (upper - lower) / 2;
                if (m_Content.Sections[middle].Time <= time)
                    lower = middle + 1;
                else
                    upper = middle;
            }
            return lower == 0 ? string.Empty : m_Content.Sections[lower - 1].AuthoringId;
        }
    }
}
