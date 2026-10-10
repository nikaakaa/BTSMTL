using System;
using ThirdPersonSimulation.Fixed;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public sealed class TimelineRuntimePlayback : IDisposable
    {
        readonly List<CanonicalTextValue> m_ActiveClipIds;
        readonly List<AbilityTimelineTreeClipExit> m_PendingTreeClipExits;
        readonly List<AbilityTimelineTreeClipExit> m_ExitedTreeDecisionClips;
        readonly List<AbilityTimelineTreeClipExit> m_AdvanceInjectedTreeClipExits;
        readonly List<AbilityTimelineTreeClipExit> m_AdvanceProducedTreeClipExits;
        readonly List<AbilityTimelineTreeClipExit> m_AdvanceExitedTreeDecisionClips;
        static readonly Comparison<TimelineRuntimeClipBoundary> s_CompareBoundaries = CompareBoundaries;
        static readonly Comparison<CanonicalTextValue> s_CompareClipIds = CanonicalTextValue.CompareOrdinal;
        readonly ReadOnlyCollection<CanonicalTextValue> m_ActiveClipIdsView;
        TimelineRuntimeEvaluationStorage m_CandidateEvaluation;
        TimelineRuntimeEvaluationStorage m_CommittedEvaluation;
        TimelineRuntimeAdvanceResult m_PendingAdvance;
        ulong m_AdvanceSequence;
        ulong m_StopSequence;
        TimelinePlaybackStopContext m_PendingStopContext;
        bool m_StopPending;
        FixedScalar m_CursorTime;
        int m_Cycle;
        string m_SectionId = string.Empty;
        bool m_InitialBoundaryPending;
        readonly int m_TickRate;
        int m_TimeCarry;

        internal TimelineRuntimePlayback(
            TimelineRuntimePlaybackHandle handle,
            ulong generation,
            TimelineRuntimePreparationResult preparation,
            int tickRate,
            TimelineRuntimeEvaluationStoragePool evaluationStoragePool,
            int maximumClips = 0, int maximumPendingExits = 0)
        {
            if (tickRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            m_TickRate = tickRate;
            GetSnapshotCapacity(preparation.Content, preparation.PlaybackMode,
                out int clipCapacity, out _, out int pendingExitCapacity, out _);
            clipCapacity = Math.Max(clipCapacity, maximumClips);
            pendingExitCapacity = Math.Max(pendingExitCapacity, maximumPendingExits);
            m_ActiveClipIds = new List<CanonicalTextValue>(clipCapacity);
            ActiveTreeClips = new List<AbilityTimelineTreeClipState>(clipCapacity);
            SnapshotStorage = new AbilityTimelineSnapshotStorageBank(clipCapacity, clipCapacity,
                pendingExitCapacity, clipCapacity);
            m_PendingTreeClipExits = new List<AbilityTimelineTreeClipExit>(pendingExitCapacity);
            m_ExitedTreeDecisionClips = new List<AbilityTimelineTreeClipExit>(clipCapacity);
            m_AdvanceInjectedTreeClipExits = new List<AbilityTimelineTreeClipExit>(pendingExitCapacity);
            m_AdvanceProducedTreeClipExits = new List<AbilityTimelineTreeClipExit>(pendingExitCapacity);
            m_AdvanceExitedTreeDecisionClips = new List<AbilityTimelineTreeClipExit>(pendingExitCapacity);
            m_ActiveClipIdsView = new ReadOnlyCollection<CanonicalTextValue>(m_ActiveClipIds);
            m_CandidateEvaluation = evaluationStoragePool.Rent(preparation);
            m_CommittedEvaluation = evaluationStoragePool.Rent(preparation);
            State = TimelineRuntimePlaybackState.Disposed;
            ResetForReuse(handle, generation, preparation);
        }

        public static void GetSnapshotCapacity(TimelineContentUnit content, TimelinePlaybackMode mode,
            out int clips, out int exits, out int pendingExits, out int trees)
        {
            clips = content.Clips.Count;
            exits = clips;
            trees = clips;
            int treeDecisionCount = 0;
            for (int index = 0; index < clips; index++)
            {
                var clip = content.Clips[index];
                if (clip.ExecutionPolicy.IsLogic && clip.ExitSource == TimelineClipExitSource.TreeDecision)
                    treeDecisionCount++;
            }
            int traversals = mode == TimelinePlaybackMode.Loop
                ? AbilityTimelineRuntimeLimits.MaximumCycleAdvance + 1 : 1;
            pendingExits = checked(treeDecisionCount * traversals);
        }

        public TimelineRuntimePlaybackHandle Handle { get; private set; }
        public ulong Generation { get; private set; }
        public ulong PlaybackIdentity => ExecutionIdentity.InstanceId;
        CanonicalTextValue m_RequestId;
        public CanonicalTextValue RequestId
        {
            get => m_RequestId;
            private set { value.Retain(); m_RequestId.Release(); m_RequestId = value; }
        }
        TimelineExecutionIdentity m_ExecutionIdentity;
        public TimelineExecutionIdentity ExecutionIdentity
        {
            get => m_ExecutionIdentity;
            private set { value.Retain(); m_ExecutionIdentity.Release(); m_ExecutionIdentity = value; }
        }
        public TimelinePlaybackMode PlaybackMode { get; private set; }
        public TimelineRuntimeNumericTarget NumericTarget { get; private set; }
        public TimelineContentUnit Content { get; private set; }
        internal TimelineRuntimeMotionSampling MotionSampling { get; private set; }
        internal TimelineRuntimeContentIndex Index => Preparation.Index;

        public TimelineRuntimeMotionPosition SampleMotionSource(string clipId, FixedScalar time) => MotionSampling.SampleSource(clipId, time);

        public AbilityTimelineMotionSourcePose SampleMotionPrefix(string clipId, int cycle, FixedScalar time)
        {
            return AbilityTimelineMotionSourcePose.AccumulatePrefix(
                Pose(m_CursorTime), Pose(time), Pose(FixedScalar.Zero), Pose(Content.Duration), cycle - m_Cycle);

            AbilityTimelineMotionSourcePose Pose(FixedScalar sourceTime)
            {
                TimelineRuntimeMotionPosition pose = MotionSampling.SampleSource(clipId, sourceTime);
                return new AbilityTimelineMotionSourcePose(pose.X, pose.Y, pose.Z, pose.Yaw);
            }
        }
        internal TimelineRuntimePreparationResult Preparation { get; private set; }
        public CanonicalTextValue ContentRevision => Content.Revision;
        public TimelineRuntimePreparedDependencies PreparedDependencies { get; private set; }
        public TimelinePreparedBindings PreparedBindings { get; private set; }
        public TimelineRuntimePlaybackState State { get; private set; }
        public FixedScalar CursorTime => m_CursorTime;
        public int TickRate => m_TickRate;
        internal IReadOnlyList<AbilityTimelineTreeClipExit> ExitedTreeDecisionClips => m_ExitedTreeDecisionClips;
        internal int TimeCarry => m_TimeCarry;
        public AbilityTimelinePlaybackControl Control { get; private set; } = AbilityTimelinePlaybackControl.Normal;
        public int Cycle => m_Cycle;
        public string SectionId => m_SectionId;
        public IReadOnlyList<CanonicalTextValue> ActiveClipIds => m_ActiveClipIdsView;
        public List<AbilityTimelineTreeClipState> ActiveTreeClips { get; }
        public AbilityTimelineSnapshotStorageBank SnapshotStorage { get; }
        public bool HasStopContext { get; private set; }
        public TimelinePlaybackStopContext StopContext { get; private set; }

        internal void ResetForReuse(
            TimelineRuntimePlaybackHandle handle,
            ulong generation,
            TimelineRuntimePreparationResult preparation)
        {
            if (State != TimelineRuntimePlaybackState.Disposed)
                throw new InvalidOperationException("Timeline playback reuse requires a disposed playback.");
            Handle = handle;
            Generation = generation;
            Preparation = preparation;
            RequestId = preparation.RequestId;
            ExecutionIdentity = preparation.ExecutionIdentity;
            PlaybackMode = preparation.PlaybackMode;
            NumericTarget = preparation.NumericTarget;
            Content = preparation.Content;
            MotionSampling = preparation.MotionSampling;
            PreparedDependencies = preparation.PreparedDependencies;
            PreparedBindings = preparation.PreparedBindings;
            m_CandidateEvaluation.Clear();
            m_CommittedEvaluation.Clear();
            m_PendingAdvance = default;
            m_AdvanceSequence = 0;
            m_StopSequence = 0;
            m_PendingStopContext = default;
            m_StopPending = false;
            m_CursorTime = preparation.StartTime;
            m_Cycle = 0;
            m_SectionId = string.Empty;
            m_InitialBoundaryPending = false;
            m_TimeCarry = 0;
            Control = AbilityTimelinePlaybackControl.Normal;
            HasStopContext = false;
            StopContext = default;
            m_ActiveClipIds.Clear();
            TimelineRuntimeTreeClipLifecycle.Clear(this);
            SnapshotStorage.ReleaseCurrent();
            m_PendingTreeClipExits.Clear();
            m_ExitedTreeDecisionClips.Clear();
            m_AdvanceInjectedTreeClipExits.Clear();
            m_AdvanceProducedTreeClipExits.Clear();
            m_AdvanceExitedTreeDecisionClips.Clear();
            State = TimelineRuntimePlaybackState.Prepared;
        }

        public bool TryResolveTargetIdentity(string bindingId, out string targetIdentity)
        {
            targetIdentity = string.Empty;
            string inputBindingId = bindingId ?? string.Empty;
            if (PreparedBindings.Plan.TryGetTargetBindingId(inputBindingId, out string targetBindingId))
                inputBindingId = targetBindingId;
            if (!PreparedBindings.CallInput.TryGet(inputBindingId, out TimelineBindingValue value) ||
                value.ValueKind != TimelineBindingValueKind.Target ||
                string.IsNullOrEmpty(value.TargetIdentity))
                return false;
            targetIdentity = value.TargetIdentity;
            return true;
        }

        public bool TryResolveClip(string authoringId, out TimelineContentClip clip)
        {
            return Index.TryResolveClip(authoringId, out clip);
        }

        public bool Start()
        {
            if (State != TimelineRuntimePlaybackState.Prepared)
                return false;
            State = TimelineRuntimePlaybackState.Running;
            m_InitialBoundaryPending = true;
            RefreshActiveState();
            return true;
        }

        public bool RequestTreeClipExit(CanonicalTextValue clipAuthoringId, int cycle)
        {
            if (CanonicalTextValue.IsNullOrWhiteSpace(clipAuthoringId))
                throw new ArgumentException("TreeClip exit identity is required.", nameof(clipAuthoringId));
            CanonicalTextValue trimmed = clipAuthoringId.Trim();
            int contentIndex = Index.FindContent(trimmed);
            if (contentIndex < 0)
                return false;
            TimelineContentClip clip = Content.Clips[contentIndex];
            if (!clip.ExecutionPolicy.IsLogic || clip.ExitSource != TimelineClipExitSource.TreeDecision)
                return false;
            string declaredIdentity = clip.AuthoringId;
            var exit = new AbilityTimelineTreeClipExit(declaredIdentity, cycle);
            if (m_PendingTreeClipExits.Contains(exit) ||
                m_ExitedTreeDecisionClips.Contains(exit) ||
                m_AdvanceProducedTreeClipExits.Contains(exit))
                return false;
            if (m_PendingAdvance.IsValid)
                m_AdvanceProducedTreeClipExits.Add(exit);
            else
                m_PendingTreeClipExits.Add(exit);
            return true;
        }

        public TimelineRuntimeAdvanceRequest CreateAdvanceRequest(ulong logicTick, int tickCount, AbilityTimelinePlaybackControl control)
        {
            if (tickCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(tickCount));
            decimal numerator = (decimal)CursorTime.Raw * TickRate + TimeCarry +
                (decimal)tickCount * (control.IsPaused ? 0L : control.Rate.Raw);
            long parityOffset = (Cycle & 1) * (Content.Duration.Raw & 1);
            int remainder = checked((int)(numerator % TickRate));
            long targetRaw = checked((long)((numerator - remainder) / TickRate));
            long doubledRemainder = (long)remainder * 2;
            if (doubledRemainder > TickRate ||
                doubledRemainder == TickRate && ((targetRaw ^ parityOffset) & 1) != 0)
            {
                targetRaw = checked(targetRaw + 1);
                remainder -= TickRate;
            }
            return new TimelineRuntimeAdvanceRequest(logicTick, CursorTime, FixedScalar.FromRaw(targetRaw), remainder, control);
        }

        public TimelineRuntimeAdvanceResult Advance(TimelineRuntimeAdvanceRequest request)
        {
            if (State != TimelineRuntimePlaybackState.Running)
                throw new InvalidOperationException("Timeline playback must be running before Advance.");
            if (m_PendingAdvance.IsValid || m_StopPending)
                throw new InvalidOperationException("Timeline playback has an uncommitted Advance result.");
            if (request.PreviousTime != m_CursorTime)
                throw new InvalidOperationException("Timeline interval does not begin at the committed cursor.");
            m_CandidateEvaluation.Clear();
            ulong advanceSequence = checked(++m_AdvanceSequence);
            if (request.Control.IsPaused)
            {
                if (request.TargetTime != m_CursorTime || request.TimeCarry != m_TimeCarry)
                    throw new InvalidOperationException("A paused Timeline interval must preserve the committed cursor and time carry.");
                for (int index = 0; index < m_ActiveClipIds.Count; index++)
                    m_CandidateEvaluation.ActiveClipIds.Add(m_ActiveClipIds[index]);
                TimelineRuntimeEvaluationResult heldEvaluation = TimelineRuntimeEvaluator.Evaluate(
                    m_CandidateEvaluation, Index, MotionSampling, Preparation.ClipSampling, Content,
                    m_CursorTime, m_Cycle, m_CursorTime, m_Cycle, false,
                    m_CandidateEvaluation.Boundaries, m_ActiveClipIds, ExecutionIdentity, PlaybackIdentity,
                    request.LogicTick, false, m_ExitedTreeDecisionClips);
                m_PendingAdvance = new TimelineRuntimeAdvanceResult(this, advanceSequence, Generation, request,
                    m_CursorTime, m_CursorTime, m_Cycle, m_Cycle, m_TimeCarry, m_SectionId,
                    m_CandidateEvaluation.ActiveClipIds.View, m_CandidateEvaluation.Boundaries.View, heldEvaluation, false);
                return m_PendingAdvance;
            }
            FixedScalar duration = Content.Duration;
            bool loop = PlaybackMode == TimelinePlaybackMode.Loop;
            int nextTimeCarry = request.TimeCarry;
            FixedScalar requestedTime = request.TargetTime;
            FixedScalar nextTime;
            int nextCycle = m_Cycle;
            if (loop && duration > FixedScalar.Zero)
            {
                long cycleDelta = requestedTime.Raw / duration.Raw;
                nextCycle = checked(nextCycle + (int)cycleDelta);
                nextTime = FixedScalar.FromRaw(requestedTime.Raw % duration.Raw);
            }
            else if (PlaybackMode == TimelinePlaybackMode.HoldLastFrame)
            {
                FixedScalar lastSample = FixedScalar.FromRaw(Math.Max(0L, duration.Raw - 1L));
                nextTime = FixedScalar.Min(requestedTime, lastSample);
                if (nextTime == lastSample)
                    nextTimeCarry = 0;
            }
            else
                nextTime = FixedScalar.Min(requestedTime, duration);

            string sectionId = Index.SectionAt(nextTime);
            TimelineRuntimeSampleBuffer<TimelineRuntimeClipBoundary> boundaries = CollectBoundaries(
                m_CursorTime,
                m_Cycle,
                nextTime,
                nextCycle,
                duration,
                loop,
                m_InitialBoundaryPending,
                m_ExitedTreeDecisionClips,
                m_PendingTreeClipExits);
            TimelineRuntimeSampleBuffer<CanonicalTextValue> activeClipIds = m_CandidateEvaluation.ActiveClipIds;
            UpdateActiveClips(nextCycle, boundaries);
            bool hasUnexitedTreeDecisionClip = false;
            for (int index = 0; index < activeClipIds.Count; index++)
                if (Content.Clips[Index.FindContent(activeClipIds[index])].ExitSource == TimelineClipExitSource.TreeDecision)
                {
                    hasUnexitedTreeDecisionClip = true;
                    break;
                }
            m_AdvanceInjectedTreeClipExits.Clear();
            m_AdvanceInjectedTreeClipExits.AddRange(m_PendingTreeClipExits);
            List<AbilityTimelineTreeClipExit> evaluationExitedTreeDecisionClips = m_AdvanceExitedTreeDecisionClips;
            evaluationExitedTreeDecisionClips.Clear();
            evaluationExitedTreeDecisionClips.AddRange(m_ExitedTreeDecisionClips);
            for (int index = 0; index < m_PendingTreeClipExits.Count; index++)
            {
                if (!evaluationExitedTreeDecisionClips.Contains(m_PendingTreeClipExits[index]))
                    evaluationExitedTreeDecisionClips.Add(m_PendingTreeClipExits[index]);
            }
            TimelineRuntimeEvaluationResult evaluation = TimelineRuntimeEvaluator.Evaluate(
                m_CandidateEvaluation,
                Index,
                MotionSampling,
                Preparation.ClipSampling,
                Content,
                m_CursorTime,
                m_Cycle,
                nextTime,
                nextCycle,
                loop,
                boundaries,
                m_ActiveClipIds,
                ExecutionIdentity,
                PlaybackIdentity,
                request.LogicTick,
                m_InitialBoundaryPending,
                evaluationExitedTreeDecisionClips);
            bool completes = PlaybackMode == TimelinePlaybackMode.Once && nextTime >= duration && !hasUnexitedTreeDecisionClip;
            m_PendingAdvance = new TimelineRuntimeAdvanceResult(
                this,
                advanceSequence,
                Generation,
                request,
                m_CursorTime,
                nextTime,
                m_Cycle,
                nextCycle,
                nextTimeCarry,
                sectionId,
                activeClipIds.View,
                boundaries.View,
                evaluation,
                completes);
            return m_PendingAdvance;
        }

        public bool Commit(TimelineRuntimeAdvanceResult advance)
        {
            RequirePendingAdvance(advance);
            TimelineRuntimeEvaluationStorage previous = m_CommittedEvaluation;
            m_CommittedEvaluation = m_CandidateEvaluation;
            m_CandidateEvaluation = previous;
            m_CandidateEvaluation.Clear();
            m_CursorTime = advance.Time;
            m_Cycle = advance.Cycle;
            m_TimeCarry = advance.TimeCarry;
            Control = advance.Request.Control;
            m_SectionId = advance.SectionId;
            if (!advance.Request.Control.IsPaused)
                m_InitialBoundaryPending = false;
            m_ActiveClipIds.Clear();
            for (int index = 0; index < advance.ActiveClipIds.Count; index++)
                m_ActiveClipIds.Add(advance.ActiveClipIds[index]);
            for (int index = 0; index < m_AdvanceInjectedTreeClipExits.Count; index++)
            {
                AbilityTimelineTreeClipExit exitedClipId = m_AdvanceInjectedTreeClipExits[index];
                m_PendingTreeClipExits.Remove(exitedClipId);
                if (!m_ExitedTreeDecisionClips.Contains(exitedClipId))
                    m_ExitedTreeDecisionClips.Add(exitedClipId);
            }
            m_PendingAdvance = default;
            for (int index = 0; index < m_AdvanceProducedTreeClipExits.Count; index++)
                m_PendingTreeClipExits.Add(m_AdvanceProducedTreeClipExits[index]);
            m_AdvanceProducedTreeClipExits.Clear();
            for (int index = m_ExitedTreeDecisionClips.Count - 1; index >= 0; index--)
                if (m_ExitedTreeDecisionClips[index].Cycle < m_Cycle)
                    m_ExitedTreeDecisionClips.RemoveAt(index);
            if (advance.Completes)
                State = TimelineRuntimePlaybackState.Completed;
            return true;
        }

        public bool Discard(TimelineRuntimeAdvanceResult advance)
        {
            RequirePendingAdvance(advance);
            m_AdvanceInjectedTreeClipExits.Clear();
            m_AdvanceProducedTreeClipExits.Clear();
            m_PendingAdvance = default;
            return true;
        }

        public bool RequestStop(TimelinePlaybackStopContext context)
        {
            if (State == TimelineRuntimePlaybackState.Disposed ||
                State == TimelineRuntimePlaybackState.Completed ||
                State == TimelineRuntimePlaybackState.Stopped ||
                State == TimelineRuntimePlaybackState.Failed)
                return false;
            if (m_StopPending)
                return false;
            m_StopSequence = checked(m_StopSequence + 1);
            m_PendingStopContext = context;
            m_StopPending = true;
            return true;
        }

        public bool CommitStop()
        {
            if (!m_StopPending || m_PendingAdvance.IsValid)
                return false;
            StopContext = m_PendingStopContext;
            HasStopContext = true;
            m_StopPending = false;
            State = TimelineRuntimePlaybackState.Stopping;
            m_ActiveClipIds.Clear();
            return true;
        }

        public bool DiscardStop()
        {
            if (!m_StopPending)
                return false;
            m_StopPending = false;
            return true;
        }

        public bool CompleteStop()
        {
            if (State != TimelineRuntimePlaybackState.Stopping)
                return false;
            m_ActiveClipIds.Clear();
            m_CandidateEvaluation.Clear();
            m_CommittedEvaluation.Clear();
            State = TimelineRuntimePlaybackState.Stopped;
            return true;
        }

        public void Dispose()
        {
            if (State != TimelineRuntimePlaybackState.Disposed)
            {
                ReleaseForReuse();
                m_CandidateEvaluation.Recycle();
                m_CommittedEvaluation.Recycle();
            }
        }

        internal void DiscardEvaluation()
        {
            m_CandidateEvaluation.Clear();
            m_CommittedEvaluation.Clear();
        }

        internal bool HasPendingStop => m_StopPending;
        internal ulong PendingStopSequence => m_StopPending ? m_StopSequence : 0;
        internal bool HasPendingAdvance => m_PendingAdvance.IsValid;
        internal bool InitialBoundaryPending => m_InitialBoundaryPending;
        internal IReadOnlyList<AbilityTimelineTreeClipExit> PendingTreeDecisionClips => m_PendingTreeClipExits;

        internal bool RestoreCommittedState(
            TimelineRuntimePlaybackState state,
            FixedScalar cursorTime,
            int cycle,
            int timeCarry,
            AbilityTimelinePlaybackControl control,
            CanonicalTextValue sectionId,
            ReadOnlySpan<CanonicalTextValue> activeClipIds,
            ReadOnlySpan<AbilityTimelineTreeClipExit> exitedTreeDecisionClips,
            ReadOnlySpan<AbilityTimelineTreeClipExit> pendingTreeDecisionClips,
            bool hasStopContext,
            TimelinePlaybackStopContext stopContext,
            bool initialBoundaryPending)
        {
            if (m_PendingAdvance.IsValid || m_StopPending)
                return false;
            if (cursorTime < FixedScalar.Zero || cycle < 0 || cursorTime > Content.Duration)
                return false;
            if (Math.Abs((long)timeCarry) * 2 > m_TickRate)
                return false;
            if (state != TimelineRuntimePlaybackState.Prepared &&
                state != TimelineRuntimePlaybackState.Running &&
                state != TimelineRuntimePlaybackState.Stopping &&
                state != TimelineRuntimePlaybackState.Completed &&
                state != TimelineRuntimePlaybackState.Stopped)
                return false;
            bool requiresStopContext = state == TimelineRuntimePlaybackState.Stopping ||
                                       state == TimelineRuntimePlaybackState.Stopped;
            if (requiresStopContext != hasStopContext)
                return false;
            if (!ValidateRestoredSection(sectionId, cursorTime, out string declaredSection) ||
                !ValidateRestoredActiveClips(
                    activeClipIds,
                    state,
                    cursorTime,
                    cycle,
                    exitedTreeDecisionClips))
                return false;
            m_CandidateEvaluation.Clear();
            m_CommittedEvaluation.Clear();
            m_CursorTime = cursorTime;
            m_Cycle = cycle;
            m_SectionId = declaredSection;
            m_InitialBoundaryPending = initialBoundaryPending;
            m_TimeCarry = timeCarry;
            Control = control;
            m_ActiveClipIds.Clear();
            for (int index = 0; index < activeClipIds.Length; index++)
                m_ActiveClipIds.Add(Content.Clips[Index.FindContent(activeClipIds[index])].AuthoringId);
            m_ActiveClipIds.Sort(s_CompareClipIds);
            m_ExitedTreeDecisionClips.Clear();
            for (int index = 0; index < exitedTreeDecisionClips.Length; index++)
                m_ExitedTreeDecisionClips.Add(new AbilityTimelineTreeClipExit(
                    Content.Clips[Index.FindContent(exitedTreeDecisionClips[index].ClipAuthoringId)].AuthoringId,
                    exitedTreeDecisionClips[index].Cycle));
            m_PendingTreeClipExits.Clear();
            for (int index = 0; index < pendingTreeDecisionClips.Length; index++)
                m_PendingTreeClipExits.Add(new AbilityTimelineTreeClipExit(
                    Content.Clips[Index.FindContent(pendingTreeDecisionClips[index].ClipAuthoringId)].AuthoringId,
                    pendingTreeDecisionClips[index].Cycle));
            HasStopContext = hasStopContext;
            StopContext = stopContext;
            State = state;
            return true;
        }

        bool ValidateRestoredSection(CanonicalTextValue sectionId, FixedScalar cursorTime, out string declaredSection)
        {
            declaredSection = string.Empty;
            if (CanonicalTextValue.IsNullOrEmpty(sectionId))
                return true;
            for (int index = 0; index < Content.Sections.Count; index++)
            {
                TimelineContentSection section = Content.Sections[index];
                if (sectionId.Equals(section.AuthoringId))
                {
                    declaredSection = section.AuthoringId;
                    return section.Time <= cursorTime;
                }
            }
            return false;
        }

        bool ValidateRestoredActiveClips(
            ReadOnlySpan<CanonicalTextValue> activeClipIds,
            TimelineRuntimePlaybackState state,
            FixedScalar cursorTime,
            int cycle,
            ReadOnlySpan<AbilityTimelineTreeClipExit> exitedTreeDecisionClips)
        {
            int suppliedCount = activeClipIds.Length;
            if (state != TimelineRuntimePlaybackState.Running)
                return suppliedCount == 0;
            int expectedCount = 0;
            List<int> selected = m_CandidateEvaluation.SamplingClips;
            selected.Clear();
            TimelineRuntimeClipIndex lifetimes = Index.Logic.Lifetimes;
            lifetimes.Select(cursorTime, cursorTime, true, selected);
            for (int clipIndex = 0; clipIndex < selected.Count; clipIndex++)
            {
                TimelineContentClip clip = Content.Clips[lifetimes[selected[clipIndex]].ContentIndex];
                if (clip.ExitSource == TimelineClipExitSource.TreeDecision
                    ? ContainsExit(exitedTreeDecisionClips, clip.AuthoringId, cycle)
                    : cursorTime >= clip.EndTime)
                    continue;
                expectedCount++;
                int occurrences = 0;
                for (int index = 0; index < suppliedCount; index++)
                    if (activeClipIds[index].Equals(clip.AuthoringId))
                        occurrences++;
                if (occurrences != 1)
                    return false;
            }
            return expectedCount == suppliedCount;
        }

        internal static bool ContainsExit(IReadOnlyList<AbilityTimelineTreeClipExit> exits, string clipId, int cycle)
        {
            var occurrence = new AbilityTimelineTreeClipExit(clipId, cycle);
            for (int index = 0; index < exits.Count; index++)
                if (exits[index].Equals(occurrence))
                    return true;
            return false;
        }

        static bool ContainsExit(ReadOnlySpan<AbilityTimelineTreeClipExit> exits, string clipId, int cycle)
        {
            var occurrence = new AbilityTimelineTreeClipExit(clipId, cycle);
            for (int index = 0; index < exits.Length; index++)
                if (exits[index].Equals(occurrence))
                    return true;
            return false;
        }

        void RequirePendingAdvance(TimelineRuntimeAdvanceResult advance)
        {
            if (!m_PendingAdvance.IsValid || !ReferenceEquals(advance.Owner, this) ||
                advance.AdvanceSequence != m_PendingAdvance.AdvanceSequence)
                throw new InvalidOperationException("Timeline Advance result does not belong to the active playback.");
        }

        void RefreshActiveState()
        {
            m_ActiveClipIds.Clear();
            List<int> selected = m_CandidateEvaluation.SamplingClips;
            selected.Clear();
            TimelineRuntimeClipIndex lifetimes = Index.Logic.Lifetimes;
            lifetimes.Select(m_CursorTime, m_CursorTime, true, selected);
            for (int index = 0; index < selected.Count; index++)
            {
                TimelineContentClip clip = Content.Clips[lifetimes[selected[index]].ContentIndex];
                bool treeDecision = clip.ExitSource == TimelineClipExitSource.TreeDecision;
                if (treeDecision
                    ? !m_ExitedTreeDecisionClips.Contains(new AbilityTimelineTreeClipExit(clip.AuthoringId, m_Cycle)) &&
                      !m_PendingTreeClipExits.Contains(new AbilityTimelineTreeClipExit(clip.AuthoringId, m_Cycle))
                    : m_CursorTime < clip.EndTime)
                    m_ActiveClipIds.Add(clip.AuthoringId);
            }
            m_SectionId = Index.SectionAt(m_CursorTime);
        }

        void UpdateActiveClips(int cycle, IReadOnlyList<TimelineRuntimeClipBoundary> boundaries)
        {
            TimelineRuntimeSampleBuffer<CanonicalTextValue> active = m_CandidateEvaluation.ActiveClipIds;
            if (cycle == m_Cycle)
                for (int index = 0; index < m_ActiveClipIds.Count; index++)
                    active.Add(m_ActiveClipIds[index]);
            for (int index = 0; index < boundaries.Count; index++)
            {
                TimelineRuntimeClipBoundary boundary = boundaries[index];
                if (boundary.Cycle != cycle)
                    continue;
                int lower = 0;
                int upper = active.Count;
                while (lower < upper)
                {
                    int middle = lower + (upper - lower) / 2;
                    if (CanonicalTextValue.CompareOrdinal(active[middle], boundary.AuthoringId) < 0)
                        lower = middle + 1;
                    else
                        upper = middle;
                }
                bool present = lower < active.Count && active[lower].Equals(boundary.AuthoringId);
                if (boundary.Kind == TimelineRuntimeClipBoundaryKind.Exit)
                {
                    if (present)
                        active.RemoveAt(lower);
                    continue;
                }
                TimelineContentClip clip = Content.Clips[Index[boundary.AuthoringId].ContentIndex];
                bool treeDecision = clip.ExitSource == TimelineClipExitSource.TreeDecision;
                if (!present && (treeDecision
                    ? !ContainsExit(m_ExitedTreeDecisionClips, clip.AuthoringId, cycle) &&
                      !ContainsExit(m_PendingTreeClipExits, clip.AuthoringId, cycle)
                    : clip.StartTime < clip.EndTime))
                    active.Insert(lower, clip.AuthoringId);
            }
        }

        TimelineRuntimeSampleBuffer<TimelineRuntimeClipBoundary> CollectBoundaries(
            FixedScalar previousTime,
            int previousCycle,
            FixedScalar nextTime,
            int nextCycle,
            FixedScalar duration,
            bool loop,
            bool initialBoundaryPending,
            IReadOnlyList<AbilityTimelineTreeClipExit> exitedTreeDecisionClips,
            IReadOnlyList<AbilityTimelineTreeClipExit> pendingTreeClipExits)
        {
            TimelineRuntimeSampleBuffer<TimelineRuntimeClipBoundary> result = m_CandidateEvaluation.Boundaries;
            if (duration <= FixedScalar.Zero)
                return result;
            FixedScalar previousAbsolute = duration * FixedScalar.FromInt64(previousCycle) + previousTime;
            FixedScalar nextAbsolute = duration * FixedScalar.FromInt64(nextCycle) + nextTime;
            if (loop && nextCycle - previousCycle > TimelineRuntimeEvaluationSegments.MaximumCycleAdvance)
                throw new InvalidOperationException("Timeline playback crossed more than 20 cycles in one Advance.");

            int firstCycle = loop ? previousCycle : 0;
            int lastCycle = loop ? nextCycle : 0;
            for (int exitIndex = 0; exitIndex < pendingTreeClipExits.Count; exitIndex++)
            {
                AbilityTimelineTreeClipExit exit = pendingTreeClipExits[exitIndex];
                int contentIndex = Index.FindContent(exit.ClipAuthoringId);
                if (contentIndex < 0)
                    continue;
                TimelineContentClip clip = Content.Clips[contentIndex];
                if (!clip.TrackMuted && clip.ExecutionPolicy.IsLogic && clip.ExitSource == TimelineClipExitSource.TreeDecision)
                    result.Add(new TimelineRuntimeClipBoundary(clip.AuthoringId, clip.TrackAuthoringId,
                        nextTime, exit.Cycle, TimelineRuntimeClipBoundaryKind.Exit));
            }
            if (nextAbsolute > previousAbsolute)
            {
                TimelineRuntimeClipIndex lifetimes = Index.Logic.Lifetimes;
                if (initialBoundaryPending)
                {
                    List<int> selected = m_CandidateEvaluation.SamplingClips;
                    selected.Clear();
                    lifetimes.Select(previousTime, previousTime, false, selected);
                    for (int index = 0; index < selected.Count; index++)
                    {
                        TimelineContentClip clip = Content.Clips[lifetimes[selected[index]].ContentIndex];
                        if (clip.StartTime < previousTime)
                            result.Add(new TimelineRuntimeClipBoundary(clip.AuthoringId, clip.TrackAuthoringId,
                                previousTime, previousCycle, TimelineRuntimeClipBoundaryKind.Enter));
                    }
                }
                for (int cycle = firstCycle; cycle <= lastCycle; cycle++)
                {
                    FixedScalar from = cycle == previousCycle ? previousTime : FixedScalar.Zero;
                    FixedScalar to = cycle == nextCycle ? nextTime : duration;
                    int start = lifetimes.FindStart(from, initialBoundaryPending || cycle > previousCycle);
                    for (int index = start; index < lifetimes.Count; index++)
                    {
                        TimelineContentClip clip = Content.Clips[lifetimes.StartAt(index).ContentIndex];
                        if (clip.StartTime > to)
                            break;
                        if (clip.ExitSource == TimelineClipExitSource.TreeDecision &&
                            (ContainsExit(exitedTreeDecisionClips, clip.AuthoringId, cycle) ||
                             ContainsExit(pendingTreeClipExits, clip.AuthoringId, cycle)))
                            continue;
                        AddBoundary(result, clip, clip.StartTime, cycle, TimelineRuntimeClipBoundaryKind.Enter,
                            previousAbsolute, nextAbsolute, duration, initialBoundaryPending);
                    }
                    int end = lifetimes.FindEnd(from, cycle > previousCycle);
                    for (int index = end; index < lifetimes.Count; index++)
                    {
                        if (lifetimes.EndTimeAt(index) > to)
                            break;
                        TimelineContentClip clip = Content.Clips[lifetimes.EndAt(index).ContentIndex];
                        if (clip.ExitSource == TimelineClipExitSource.TreeDecision)
                            continue;
                        AddBoundary(result, clip, clip.EndTime, cycle, TimelineRuntimeClipBoundaryKind.Exit,
                            previousAbsolute, nextAbsolute, duration, false);
                    }
                }
            }
            result.Sort(s_CompareBoundaries);
            return result;
        }

        static int CompareBoundaries(TimelineRuntimeClipBoundary left, TimelineRuntimeClipBoundary right)
        {
            int position = left.Cycle.CompareTo(right.Cycle);
            if (position == 0)
                position = left.Time.CompareTo(right.Time);
            if (position != 0)
                return position;
            int kind = ((byte)left.Kind).CompareTo((byte)right.Kind);
            return kind != 0 ? kind : string.CompareOrdinal(left.AuthoringId, right.AuthoringId);
        }

        internal void ReleaseForReuse()
        {
            if (State == TimelineRuntimePlaybackState.Disposed)
                return;
            m_CandidateEvaluation.Clear();
            m_CommittedEvaluation.Clear();
            m_PendingAdvance = default;
            m_StopPending = false;
            m_ActiveClipIds.Clear();
            TimelineRuntimeTreeClipLifecycle.Clear(this);
            SnapshotStorage.ReleaseCurrent();
            SnapshotStorage.ContentLifetime = null;
            m_PendingTreeClipExits.Clear();
            m_ExitedTreeDecisionClips.Clear();
            m_AdvanceInjectedTreeClipExits.Clear();
            m_AdvanceProducedTreeClipExits.Clear();
            m_AdvanceExitedTreeDecisionClips.Clear();
            RequestId = default;
            ExecutionIdentity = default;
            State = TimelineRuntimePlaybackState.Disposed;
        }

        static void AddBoundary(
            TimelineRuntimeSampleBuffer<TimelineRuntimeClipBoundary> result,
            TimelineContentClip clip,
            FixedScalar time,
            int cycle,
            TimelineRuntimeClipBoundaryKind kind,
            FixedScalar previousAbsolute,
            FixedScalar nextAbsolute,
            FixedScalar duration,
            bool initialBoundaryPending)
        {
            FixedScalar absolute = duration * FixedScalar.FromInt64(cycle) + time;
            bool initialEnter = initialBoundaryPending &&
                                kind == TimelineRuntimeClipBoundaryKind.Enter &&
                                absolute == previousAbsolute;
            if ((!initialEnter && absolute <= previousAbsolute) || absolute > nextAbsolute)
                return;
            result.Add(new TimelineRuntimeClipBoundary(
                clip.AuthoringId,
                clip.TrackAuthoringId,
                time,
                cycle,
                kind));
        }
    }
}
