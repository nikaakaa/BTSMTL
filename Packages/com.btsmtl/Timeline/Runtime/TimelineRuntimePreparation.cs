using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;
using PresentationClipSampling = BTSMTL.Timeline.Runtime.TimelineRuntimeClipSampling<ThirdPersonSimulation.Float32Scalar,
    ThirdPersonSimulation.Float32SimulationMath, ThirdPersonSimulation.Float32ScalarContentLowering,
    BTSMTL.Timeline.Runtime.TimelineRuntimeFloat32TimeProjection>;

namespace BTSMTL.Timeline.Runtime
{
    public enum TimelineRuntimePreparationStatus : byte
    {
        Failed = 0,
        Ready = 1
    }

    public sealed class TimelineRuntimePreparedContent
    {
        internal TimelineRuntimePreparedContent(TimelineRuntimePreparedContent source,
            TimelineContentUnit content, OrchestrationIdentityTable identities, bool rebuildSampling = false)
        {
            Status = source.Status;
            NumericTarget = source.NumericTarget;
            Content = content;
            BindIdentities(identities);
            Index = new TimelineRuntimeContentIndex(content);
            ClipSampling = rebuildSampling
                ? new PresentationClipSampling(content, TimelineExecutionDomain.Presentation)
                : new PresentationClipSampling(source.ClipSampling, content);
            BindingPlan = source.BindingPlan;
            CallInput = source.CallInput;
            PreparedBindings = source.PreparedBindings;
            PreparedDependencies = source.PreparedDependencies;
            Errors = source.Errors;
            RefreshSchedulingData();
        }

        public void RefreshTimeRanges(OrchestrationIdentityTable identities)
        {
            BindIdentities(identities);
            Index.RefreshTimeRanges();
            ClipSampling.RefreshTimeRanges(Content);
            RefreshSchedulingData();
        }

        internal TimelineRuntimePreparedContent(
            TimelineRuntimePreparationStatus status,
            TimelineRuntimeNumericTarget numericTarget,
            TimelineContentUnit content,
            TimelineBindingPlan bindingPlan,
            TimelineCallInput callInput,
            TimelinePreparedBindings preparedBindings,
            TimelineRuntimePreparedDependencies preparedDependencies,
            IReadOnlyList<string> errors,
            OrchestrationIdentityTable identities)
        {
            Status = status;
            NumericTarget = numericTarget;
            Index = status == TimelineRuntimePreparationStatus.Ready
                ? new TimelineRuntimeContentIndex(content)
                : null;
            ClipSampling = status == TimelineRuntimePreparationStatus.Ready
                ? new PresentationClipSampling(content, TimelineExecutionDomain.Presentation) : null;
            Content = content;
            BindingPlan = bindingPlan;
            CallInput = callInput;
            PreparedBindings = preparedBindings;
            PreparedDependencies = preparedDependencies;
            Errors = errors ?? Array.Empty<string>();
            if (status == TimelineRuntimePreparationStatus.Ready)
            {
                BindIdentities(identities);
                RefreshSchedulingData();
            }
        }

        public TimelineRuntimePreparationStatus Status { get; }
        public TimelineRuntimeNumericTarget NumericTarget { get; }
        internal TimelineRuntimeContentIndex Index { get; }
        internal PresentationClipSampling ClipSampling { get; }
        public int SampleKindCount => ClipSampling.SampleKindCount;
        public string SampleKindAt(int kindIndex) => ClipSampling.SampleKindAt(kindIndex);
        public int RequireSampleKind(string contractKind) => ClipSampling.RequireSampleKind(contractKind);
        public TimelineContentUnit Content { get; }
        public OrchestrationIdentityTable Identities { get; private set; }
        public int ContentIdentityId { get; private set; }
        public int ContentRevisionId { get; private set; }
        public ReadOnlyMemory<int> ClipIdentityIds { get; private set; }
        public TimelineBindingPlan BindingPlan { get; }
        public TimelineCallInput CallInput { get; }
        public TimelinePreparedBindings PreparedBindings { get; }
        public TimelineRuntimePreparedDependencies PreparedDependencies { get; }
        public IReadOnlyList<string> Errors { get; }
        public bool IsReady => Status == TimelineRuntimePreparationStatus.Ready;
        TimelineRuntimeSchedulingContent m_LogicScheduling;
        TimelineRuntimeSchedulingContent m_PresentationScheduling;

        public TimelineRuntimeSchedulingContent Scheduling(TimelineExecutionDomain domain) => domain switch
        {
            TimelineExecutionDomain.Logic => m_LogicScheduling,
            TimelineExecutionDomain.Presentation => m_PresentationScheduling,
            _ => throw new ArgumentOutOfRangeException(nameof(domain))
        };

        void BindIdentities(OrchestrationIdentityTable identities)
        {
            int contentIdentityId = identities.Bind(Content.Identity);
            int contentRevisionId = identities.Bind(Content.Revision);
            var clipIdentityIds = new int[Content.Clips.Count];
            for (int ordinal = 0; ordinal < clipIdentityIds.Length; ordinal++)
                clipIdentityIds[ordinal] = identities.Bind(Content.Clips[ordinal].AuthoringId);
            Identities = identities;
            ContentIdentityId = contentIdentityId;
            ContentRevisionId = contentRevisionId;
            ClipIdentityIds = clipIdentityIds;
        }

        void RefreshSchedulingData()
        {
            int count = Content.Clips.Count;
            var order = new int[count];
            var sortOrdinals = new int[count];
            for (int i = 0; i < count; i++)
                order[i] = i;
            Array.Sort(order, (left, right) => string.CompareOrdinal(
                Content.Clips[left].AuthoringId, Content.Clips[right].AuthoringId));
            for (int i = 0; i < count; i++)
                sortOrdinals[order[i]] = i;
            var errors = new List<string>();
            TimelineSchedulingSection<OrchestrationTime>[] sections = TimelineRuntimePreparation.BuildSections(Content, errors);
            if (errors.Count != 0)
                throw new InvalidOperationException(string.Join("\n", errors));
            m_LogicScheduling = PrepareScheduling(TimelineExecutionDomain.Logic, Index.Logic, sortOrdinals, sections);
            m_PresentationScheduling = PrepareScheduling(TimelineExecutionDomain.Presentation, Index.Presentation, sortOrdinals, sections);
        }

        TimelineRuntimeSchedulingContent PrepareScheduling(TimelineExecutionDomain domain,
            TimelineRuntimeDomainIndex index, int[] sortOrdinals,
            TimelineSchedulingSection<OrchestrationTime>[] sections)
        {
            int count = Content.Clips.Count;
            var enabled = new bool[count];
            for (int i = 0; i < index.Lifetimes.Count; i++)
                enabled[index.Lifetimes[i].ContentIndex] = true;
            var clips = new TimelineSchedulingClip<OrchestrationTime>[count];
            for (int i = 0; i < count; i++)
            {
                TimelineContentClip clip = Content.Clips[i];
                clips[i] = new TimelineSchedulingClip<OrchestrationTime>(sortOrdinals[i], clip.StartTime,
                    clip.EndTime, enabled[i], clip.ContentKind == TimelineContentKind.Discrete,
                    clip.ExitSource == TimelineClipExitSource.TreeDecision,
                    clip.ContentKind == TimelineContentKind.Discrete && ((TimelineContentTree)clip.Parameters).HasDeclaredEndTime);
            }
            var markers = new TimelineSchedulingMarker<OrchestrationTime>[index.Markers.Count];
            var trees = new int[index.Trees.Length];
            for (int i = 0; i < trees.Length; i++)
                trees[i] = index.Trees[i].ContentIndex;
            for (int i = 0; i < markers.Length; i++)
                markers[i] = new TimelineSchedulingMarker<OrchestrationTime>(index.Markers.ContentIndexAt(i),
                    index.Markers[i].Time);
            return new TimelineRuntimeSchedulingContent(domain, clips, trees, markers, sections);
        }

        internal static TimelineRuntimePreparedContent Failed(
            TimelineRuntimeNumericTarget numericTarget,
            OrchestrationIdentityTable identities,
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
                new List<string>(errors ?? Array.Empty<string>()).AsReadOnly(),
                identities);
        }

        internal static TimelineRuntimePreparedContent Ready(
            TimelineRuntimeNumericTarget numericTarget,
            OrchestrationIdentityTable identities,
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
                Array.Empty<string>(),
                identities);
        }
    }

    public static class TimelineRuntimePreparation
    {
        public static TimelineRuntimePreparedContent PrepareTimeRangeCandidate(
            TimelineRuntimePreparedContent source,
            TimelineContentUnit candidate,
            OrchestrationIdentityTable identities)
        {
            var frozen = new TimelineContentUnit(candidate.Identity, candidate.AuthoringId,
                candidate.ContentHash, candidate.RootFingerprint, candidate.Name, candidate.Duration,
                candidate.Loop, candidate.Tracks, candidate.Clips, candidate.Markers,
                candidate.Sections, candidate.Bindings, candidate.Dependencies);
            return new TimelineRuntimePreparedContent(source, frozen, identities);
        }

        // 结构候选沿用原绑定、依赖与状态布局；布局变化须完整发布。
        public static TimelineRuntimePreparedContent PrepareStructureCandidate(
            TimelineRuntimePreparedContent source,
            TimelineContentUnit candidate,
            OrchestrationIdentityTable identities,
            out string rejection)
        {
            rejection = FindLayoutChange(source.Content, candidate);
            return rejection == null ? new TimelineRuntimePreparedContent(source, candidate, identities, true) : null;
        }

        static string FindLayoutChange(TimelineContentUnit source, TimelineContentUnit candidate)
        {
            if (source.Identity != candidate.Identity || source.AuthoringId != candidate.AuthoringId)
                return "Timeline 身份已变化";
            if (source.Tracks.Count != candidate.Tracks.Count)
                return "轨道数量已变化";
            for (int i = 0; i < source.Tracks.Count; i++)
            {
                TimelineContentTrack left = source.Tracks[i];
                TimelineContentTrack right = candidate.Tracks[i];
                if (left.AuthoringId != right.AuthoringId || left.ContractKind != right.ContractKind ||
                    !left.ExecutionPolicy.Equals(right.ExecutionPolicy) || left.Muted != right.Muted ||
                    !Equals(left.LogicSampleExtension, right.LogicSampleExtension) ||
                    !left.ClipAuthoringIds.SequenceEqual(right.ClipAuthoringIds))
                    return $"轨道 '{left.Name}' 的类型、执行域或片段组成已变化";
            }
            if (source.Clips.Count != candidate.Clips.Count)
                return "片段数量已变化";
            for (int i = 0; i < source.Clips.Count; i++)
            {
                TimelineContentClip left = source.Clips[i];
                TimelineContentClip right = candidate.Clips[i];
                if (left.AuthoringId != right.AuthoringId || left.ContractKind != right.ContractKind ||
                    left.TrackAuthoringId != right.TrackAuthoringId || left.ContentKind != right.ContentKind ||
                    !left.ExecutionPolicy.Equals(right.ExecutionPolicy) || left.ExitSource != right.ExitSource ||
                    left.ExecutionPhase != right.ExecutionPhase || left.SupportsStop != right.SupportsStop ||
                    left.TrackMuted != right.TrackMuted ||
                    left.GraphBinding.GraphId != right.GraphBinding.GraphId ||
                    left.GraphBinding.Revision != right.GraphBinding.Revision ||
                    !left.Bindings.SequenceEqual(right.Bindings) ||
                    !left.Curves.Select(curve => curve.ChannelId).SequenceEqual(right.Curves.Select(curve => curve.ChannelId)))
                    return $"片段 '{left.AuthoringId}' 的类型、图、绑定或曲线通道已变化";
            }
            if (source.Markers.Count != candidate.Markers.Count)
                return "Marker 数量已变化";
            for (int i = 0; i < source.Markers.Count; i++)
            {
                TimelineContentMarker left = source.Markers[i];
                TimelineContentMarker right = candidate.Markers[i];
                if (left.MarkerId != right.MarkerId || left.TrackAuthoringId != right.TrackAuthoringId ||
                    !left.ExecutionPolicy.Equals(right.ExecutionPolicy) || left.TrackMuted != right.TrackMuted ||
                    left.GraphId != right.GraphId || left.GraphRevision != right.GraphRevision)
                    return $"Marker '{left.MarkerId}' 的轨道或触发图已变化";
            }
            if (!source.Sections.Select(section => section.AuthoringId)
                    .SequenceEqual(candidate.Sections.Select(section => section.AuthoringId)))
                return "Section 组成已变化";
            if (!source.Sections.Select(section => section.NextSectionId)
                    .SequenceEqual(candidate.Sections.Select(section => section.NextSectionId)))
                return "分段跳转配置已变化";
            if (!source.Bindings.SequenceEqual(candidate.Bindings))
                return "绑定声明已变化";
            if (source.Dependencies.Count != candidate.Dependencies.Count)
                return "依赖资源组成已变化";
            for (int i = 0; i < source.Dependencies.Count; i++)
            {
                TimelineContentDependency left = source.Dependencies[i];
                TimelineContentDependency right = candidate.Dependencies[i];
                if (left.Identity != right.Identity || left.Kind != right.Kind || left.ContentHash != right.ContentHash)
                    return $"依赖 '{left.Identity}' 已变化；资源或 TreeClip 图修改需要重新发布";
            }
            return null;
        }

        public static TimelineRuntimePreparedContent PrepareContent(
            TimelineContentUnit content,
            TimelineRuntimeNumericTarget numericTarget,
            IReadOnlyList<TimelineCallBinding> callBindings,
            ITimelineDomainBindingResolver domainResolver,
            ITimelineRuntimeDependencyResolver dependencyResolver,
            OrchestrationIdentityTable identities)
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
                        identities,
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
                        identities,
                        errors);
                return TimelineRuntimePreparedContent.Ready(
                    numericTarget,
                    identities,
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
                    identities,
                    new[] { exception.Message });
            }
        }

        internal static TimelineSchedulingSection<OrchestrationTime>[] BuildSections(
            TimelineContentUnit content, List<string> errors)
        {
            int count = content.Sections.Count;
            var order = new int[count];
            for (int i = 0; i < count; i++)
                order[i] = i;
            Array.Sort(order, (left, right) =>
            {
                int result = content.Sections[left].Time.CompareTo(content.Sections[right].Time);
                return result != 0 ? result : left.CompareTo(right);
            });
            var sorted = new int[count];
            for (int i = 0; i < count; i++)
                sorted[order[i]] = i;
            var sections = new TimelineSchedulingSection<OrchestrationTime>[count];
            for (int i = 0; i < count; i++)
            {
                TimelineContentSection section = content.Sections[order[i]];
                OrchestrationTime end = i + 1 < count ? content.Sections[order[i + 1]].Time : content.Duration;
                int next = -1;
                if (!string.IsNullOrEmpty(section.NextSectionId))
                {
                    int target = -1;
                    for (int candidate = 0; candidate < count; candidate++)
                        if (content.Sections[candidate].AuthoringId == section.NextSectionId)
                            target = candidate;
                    if (target < 0)
                        errors.Add($"Timeline '{content.Name}' 分段 '{section.AuthoringId}' 的下一分段 '{section.NextSectionId}' 不存在。");
                    else if (content.Sections[target].Time <= section.Time)
                        errors.Add($"Timeline '{content.Name}' 分段 '{section.AuthoringId}' 只能跳到时间更靠后的分段；'{section.NextSectionId}' 不在其后。");
                    else if (end <= section.Time || HasSameTime(content, order[i]) || HasSameTime(content, target))
                        errors.Add($"Timeline '{content.Name}' 分段 '{section.AuthoringId}' 与其下一分段必须有唯一且不为空的区间。");
                    else
                        next = sorted[target];
                }
                sections[i] = new TimelineSchedulingSection<OrchestrationTime>(order[i], section.Time, end, next);
            }
            return sections;
        }

        static bool HasSameTime(TimelineContentUnit content, int index)
        {
            for (int i = 0; i < content.Sections.Count; i++)
                if (i != index && content.Sections[i].Time == content.Sections[index].Time)
                    return true;
            return false;
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
        readonly OrchestrationTime[] m_Ends;
        readonly OrchestrationTime[] m_SubtreeEnds;
        readonly Func<TimelineContentClip, OrchestrationTime> m_EndTime;
        readonly Comparison<int> m_StartComparison;
        readonly Comparison<int> m_EndComparison;

        public TimelineRuntimeClipIndex(List<TimelineRuntimeIndexedClip> clips, Func<TimelineContentClip, OrchestrationTime> endTime)
        {
            m_Clips = clips.ToArray();
            m_StartOrder = new int[clips.Count];
            m_EndOrder = new int[clips.Count];
            m_Ends = new OrchestrationTime[clips.Count];
            m_SubtreeEnds = new OrchestrationTime[clips.Count];
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
        public OrchestrationTime EndTimeAt(int index) => m_Ends[m_EndOrder[index]];

        public int FindStart(OrchestrationTime time, bool includeEqual) => Find(time, includeEqual, true);
        public int FindEnd(OrchestrationTime time, bool includeEqual) => Find(time, includeEqual, false);

        int Find(OrchestrationTime time, bool includeEqual, bool start)
        {
            int lower = 0;
            int upper = Count;
            while (lower < upper)
            {
                int middle = lower + (upper - lower) / 2;
                OrchestrationTime position = start ? StartAt(middle).Clip.StartTime : EndTimeAt(middle);
                if (position < time || !includeEqual && position == time)
                    lower = middle + 1;
                else
                    upper = middle;
            }
            return lower;
        }

        OrchestrationTime BuildSubtreeEnds(int lower, int upper)
        {
            if (lower == upper)
                return OrchestrationTime.MinValue;
            int middle = lower + (upper - lower) / 2;
            OrchestrationTime end = m_Ends[m_StartOrder[middle]];
            end = OrchestrationTime.Max(end, BuildSubtreeEnds(lower, middle));
            end = OrchestrationTime.Max(end, BuildSubtreeEnds(middle + 1, upper));
            m_SubtreeEnds[middle] = end;
            return end;
        }

        public void Select(OrchestrationTime previous, OrchestrationTime current, bool includeEnd, List<int> selected)
        {
            Select(0, Count, previous, current, includeEnd, selected);
        }

        void Select(int lower, int upper, OrchestrationTime previous, OrchestrationTime current, bool includeEnd, List<int> selected)
        {
            if (lower == upper)
                return;
            int middle = lower + (upper - lower) / 2;
            OrchestrationTime subtreeEnd = m_SubtreeEnds[middle];
            if (subtreeEnd < previous || !includeEnd && subtreeEnd == previous)
                return;
            Select(lower, middle, previous, current, includeEnd, selected);
            int index = m_StartOrder[middle];
            if (m_Clips[index].Clip.StartTime > current)
                return;
            OrchestrationTime end = m_Ends[index];
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

        public TimelineRuntimeMarkerIndex(TimelineContentUnit content, TimelineExecutionDomain domain)
        {
            m_Content = content;
            var markers = new List<int>();
            for (int index = 0; index < content.Markers.Count; index++)
                if (!content.Markers[index].TrackMuted && content.Markers[index].ExecutionPolicy.Domain == domain)
                    markers.Add(index);
            m_Markers = markers.ToArray();
        }

        public int Count => m_Markers.Length;
        public int ContentIndexAt(int index) => m_Markers[index];
        public TimelineContentMarker this[int index] => m_Content.Markers[m_Markers[index]];
    }

    sealed class TimelineRuntimeDomainIndex
    {
        internal void RefreshTimeRanges()
        {
            Clips.RefreshTimeRanges();
            Sampled.RefreshTimeRanges();
            Lifetimes.RefreshTimeRanges();
            TreeLifetimes.RefreshTimeRanges();
        }

        public TimelineRuntimeDomainIndex(List<TimelineRuntimeIndexedClip> clips,
            TimelineContentUnit content, TimelineExecutionDomain domain)
        {
            var sampled = new List<TimelineRuntimeIndexedClip>();
            var trees = new List<TimelineRuntimeIndexedClip>();
            for (int index = 0; index < clips.Count; index++)
            {
                TimelineRuntimeIndexedClip clip = clips[index];
                if (clip.Clip.ContentKind == TimelineContentKind.Sampled)
                    sampled.Add(clip);
                else
                    trees.Add(clip);
            }
            Clips = new TimelineRuntimeClipIndex(clips, ClipEnd);
            Sampled = new TimelineRuntimeClipIndex(sampled, SampleEnd);
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

        static OrchestrationTime ClipEnd(TimelineContentClip clip) => clip.EndTime;
        static OrchestrationTime SampleEnd(TimelineContentClip clip) => clip.Sampling.Extrapolation == ExtraPolationMode.Hold
            ? OrchestrationTime.MaxValue : clip.EndTime;
        static OrchestrationTime LifetimeEnd(TimelineContentClip clip) => clip.ExitSource == TimelineClipExitSource.TreeDecision
            ? OrchestrationTime.MaxValue : clip.EndTime;

        public readonly TimelineRuntimeClipIndex Clips;
        public readonly TimelineRuntimeClipIndex Sampled;
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

        public string SectionAt(OrchestrationTime time)
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
