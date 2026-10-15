using TimelinePlaybackProgress = BTSMTL.Orchestration.OrchestrationTimelineProgress<
    Photon.Deterministic.FP,
    BTSMTL.Timeline.TimelineRuntimePlaybackState,
    ThirdPersonSimulation.AbilityTimelinePlaybackControl,
    BTSMTL.Timeline.TimelinePlaybackStopContext>;
using System;
using Photon.Deterministic;
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
        TimelinePlaybackProgress m_Progress;
        string m_SectionId = string.Empty;
        readonly int m_TickRate;

        internal TimelineRuntimePlayback(
            TimelineRuntimePlaybackHandle handle,
            ulong generation,
            TimelineRuntimePreparationResult preparation,
            int tickRate,
            TimelineRuntimeEvaluationStoragePool evaluationStoragePool,
            int maximumClips = 0, int maximumPendingExits = 0)
            : this(preparation.Content, preparation.PlaybackMode, tickRate, evaluationStoragePool,
                maximumClips, maximumPendingExits)
        {
            ResetForReuse(handle, generation, preparation);
        }

        internal TimelineRuntimePlayback(
            TimelineContentUnit content,
            TimelinePlaybackMode mode,
            int tickRate,
            TimelineRuntimeEvaluationStoragePool evaluationStoragePool,
            int maximumClips = 0, int maximumPendingExits = 0)
        {
            if (tickRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            m_TickRate = tickRate;
            GetSnapshotCapacity(content, mode,
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
            m_CandidateEvaluation = evaluationStoragePool.Rent(content, mode);
            m_CommittedEvaluation = evaluationStoragePool.Rent(content, mode);
            m_Progress.Control = AbilityTimelinePlaybackControl.Normal;
            m_Progress.State = TimelineRuntimePlaybackState.Disposed;
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

        public TimelineRuntimeMotionPosition SampleMotionSource(string clipId, FP time) => MotionSampling.SampleSource(clipId, time);

        public AbilityTimelineMotionSourcePose SampleMotionPrefix(string clipId, int cycle, FP time)
        {
            return AbilityTimelineMotionSourcePose.AccumulatePrefix(
                Pose(m_Progress.CursorTime), Pose(time), Pose(FP._0), Pose(Content.Duration), cycle - m_Progress.Cycle);

            AbilityTimelineMotionSourcePose Pose(FP sourceTime)
            {
                TimelineRuntimeMotionPosition pose = MotionSampling.SampleSource(clipId, sourceTime);
                return new AbilityTimelineMotionSourcePose(pose.X, pose.Y, pose.Z, pose.Yaw);
            }
        }
        internal TimelineRuntimePreparationResult Preparation { get; private set; }
        public CanonicalTextValue ContentRevision => Content.Revision;
        internal int ConsumedMarkerCount;
        internal int ConsumedTreeClipCount;
        public TimelineRuntimePreparedDependencies PreparedDependencies { get; private set; }
        public TimelinePreparedBindings PreparedBindings { get; private set; }
        public TimelineRuntimePlaybackState State => m_Progress.State;
        public FP CursorTime => m_Progress.CursorTime;
        public int TickRate => m_TickRate;
        internal IReadOnlyList<AbilityTimelineTreeClipExit> ExitedTreeDecisionClips => m_ExitedTreeDecisionClips;
        internal int TimeCarry => m_Progress.TimeCarry;
        public AbilityTimelinePlaybackControl Control => m_Progress.Control;
        public int Cycle => m_Progress.Cycle;
        public string SectionId => m_SectionId;
        public IReadOnlyList<CanonicalTextValue> ActiveClipIds => m_ActiveClipIdsView;
        public List<AbilityTimelineTreeClipState> ActiveTreeClips { get; }
        public AbilityTimelineSnapshotStorageBank SnapshotStorage { get; }
        public bool HasStopContext => m_Progress.HasStopContext;
        public TimelinePlaybackStopContext StopContext => m_Progress.StopContext;
        internal ref readonly TimelinePlaybackProgress Progress => ref m_Progress;

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
            m_Progress = default;
            m_Progress.CursorTime = preparation.StartTime;
            m_Progress.Control = AbilityTimelinePlaybackControl.Normal;
            m_SectionId = string.Empty;
            m_ActiveClipIds.Clear();
            TimelineRuntimeTreeClipLifecycle.Clear(this);
            SnapshotStorage.ReleaseCurrent();
            m_PendingTreeClipExits.Clear();
            m_ExitedTreeDecisionClips.Clear();
            m_AdvanceInjectedTreeClipExits.Clear();
            m_AdvanceProducedTreeClipExits.Clear();
            m_AdvanceExitedTreeDecisionClips.Clear();
            m_Progress.State = TimelineRuntimePlaybackState.Prepared;
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
            m_Progress.State = TimelineRuntimePlaybackState.Running;
            m_Progress.InitialBoundaryPending = true;
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
            long numerator = checked(TimeCarry + (long)tickCount * (control.IsPaused ? 0L : control.Rate.RawValue));
            long parityOffset = (Cycle & 1) * (Content.Duration.RawValue & 1);
            int remainder = checked((int)(numerator % TickRate));
            long targetRaw = checked(CursorTime.RawValue + (numerator - remainder) / TickRate);
            long doubledRemainder = Math.Abs((long)remainder) * 2;
            if (doubledRemainder > TickRate ||
                doubledRemainder == TickRate && ((targetRaw ^ parityOffset) & 1) != 0)
            {
                int direction = Math.Sign(remainder);
                targetRaw = checked(targetRaw + direction);
                remainder -= direction * TickRate;
            }
            return new TimelineRuntimeAdvanceRequest(logicTick, CursorTime, FP.FromRaw(targetRaw), remainder, control);
        }

        public TimelineRuntimeAdvanceResult Advance(TimelineRuntimeAdvanceRequest request)
        {
            if (State != TimelineRuntimePlaybackState.Running)
                throw new InvalidOperationException("Timeline playback must be running before Advance.");
            if (m_PendingAdvance.IsValid || m_StopPending)
                throw new InvalidOperationException("Timeline playback has an uncommitted Advance result.");
            if (request.PreviousTime != m_Progress.CursorTime)
                throw new InvalidOperationException("Timeline interval does not begin at the committed cursor.");
            m_CandidateEvaluation.Clear();
            ulong advanceSequence = checked(++m_AdvanceSequence);
            if (request.Control.IsPaused)
            {
                if (request.TargetTime != m_Progress.CursorTime || request.TimeCarry != m_Progress.TimeCarry)
                    throw new InvalidOperationException("A paused Timeline interval must preserve the committed cursor and time carry.");
                for (int index = 0; index < m_ActiveClipIds.Count; index++)
                    m_CandidateEvaluation.ActiveClipIds.Add(m_ActiveClipIds[index]);
                TimelineRuntimeEvaluationResult heldEvaluation = TimelineRuntimeEvaluator.Evaluate(
                    m_CandidateEvaluation, Index, MotionSampling, Preparation.ClipSampling, Content,
                    m_Progress.CursorTime, m_Progress.Cycle, m_Progress.CursorTime, m_Progress.Cycle, false,
                    m_CandidateEvaluation.Boundaries, m_ActiveClipIds, ExecutionIdentity, PlaybackIdentity,
                    request.LogicTick, false, m_ExitedTreeDecisionClips);
                m_PendingAdvance = new TimelineRuntimeAdvanceResult(this, advanceSequence, Generation, request,
                    m_Progress.CursorTime, m_Progress.CursorTime, m_Progress.Cycle, m_Progress.Cycle, m_Progress.TimeCarry, m_SectionId,
                    m_CandidateEvaluation.ActiveClipIds.View, m_CandidateEvaluation.Boundaries.View, heldEvaluation, false);
                return m_PendingAdvance;
            }
            FP duration = Content.Duration;
            bool loop = PlaybackMode == TimelinePlaybackMode.Loop;
            int nextTimeCarry = request.TimeCarry;
            FP requestedTime = request.TargetTime;
            FP nextTime;
            int nextCycle = m_Progress.Cycle;
            if (loop && duration > FP._0)
            {
                long cycleDelta = requestedTime.RawValue / duration.RawValue;
                nextCycle = checked(nextCycle + (int)cycleDelta);
                nextTime = FP.FromRaw(requestedTime.RawValue % duration.RawValue);
            }
            else if (PlaybackMode == TimelinePlaybackMode.HoldLastFrame)
            {
                FP lastSample = FP.FromRaw(Math.Max(0L, duration.RawValue - 1L));
                nextTime = FPMath.Min(requestedTime, lastSample);
                if (nextTime == lastSample)
                    nextTimeCarry = 0;
            }
            else
                nextTime = FPMath.Min(requestedTime, duration);

            string sectionId = Index.SectionAt(nextTime);
            TimelineRuntimeSampleBuffer<TimelineRuntimeClipBoundary> boundaries = CollectBoundaries(
                m_Progress.CursorTime,
                m_Progress.Cycle,
                nextTime,
                nextCycle,
                duration,
                loop,
                m_Progress.InitialBoundaryPending,
                m_ExitedTreeDecisionClips,
                m_PendingTreeClipExits);
            TimelineRuntimeSampleBuffer<CanonicalTextValue> activeClipIds = m_CandidateEvaluation.ActiveClipIds;
            CaptureActiveClips(nextTime, nextCycle);
            bool hasUnexitedTreeDecisionClip = false;
            for (int index = 0; index < activeClipIds.Count; index++)
                if (Content.Clips[Index.FindContent(activeClipIds[index])].ExitSource == TimelineClipExitSource.TreeDecision)
                {
                    hasUnexitedTreeDecisionClip = true;
                    break;
                }
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
                m_Progress.CursorTime,
                m_Progress.Cycle,
                nextTime,
                nextCycle,
                loop,
                boundaries,
                m_ActiveClipIds,
                ExecutionIdentity,
                PlaybackIdentity,
                request.LogicTick,
                m_Progress.InitialBoundaryPending,
                evaluationExitedTreeDecisionClips);
            bool completes = PlaybackMode == TimelinePlaybackMode.Once && nextTime >= duration && !hasUnexitedTreeDecisionClip;
            m_PendingAdvance = new TimelineRuntimeAdvanceResult(
                this,
                advanceSequence,
                Generation,
                request,
                m_Progress.CursorTime,
                nextTime,
                m_Progress.Cycle,
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
            m_Progress.CursorTime = advance.Time;
            m_Progress.Cycle = advance.Cycle;
            m_Progress.TimeCarry = advance.TimeCarry;
            m_Progress.Control = advance.Request.Control;
            m_SectionId = advance.SectionId;
            if (!advance.Request.Control.IsPaused)
                m_Progress.InitialBoundaryPending = false;
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
            m_AdvanceInjectedTreeClipExits.Clear();
            m_PendingAdvance = default;
            for (int index = 0; index < m_AdvanceProducedTreeClipExits.Count; index++)
                m_PendingTreeClipExits.Add(m_AdvanceProducedTreeClipExits[index]);
            m_AdvanceProducedTreeClipExits.Clear();
            for (int index = m_ExitedTreeDecisionClips.Count - 1; index >= 0; index--)
                if (m_ExitedTreeDecisionClips[index].Cycle < m_Progress.Cycle)
                    m_ExitedTreeDecisionClips.RemoveAt(index);
            if (advance.Completes)
                m_Progress.State = TimelineRuntimePlaybackState.Completed;
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
            m_Progress.StopContext = m_PendingStopContext;
            m_Progress.HasStopContext = true;
            m_StopPending = false;
            m_Progress.State = TimelineRuntimePlaybackState.Stopping;
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
            m_Progress.State = TimelineRuntimePlaybackState.Stopped;
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
        internal bool InitialBoundaryPending => m_Progress.InitialBoundaryPending;
        internal IReadOnlyList<AbilityTimelineTreeClipExit> PendingTreeDecisionClips => m_PendingTreeClipExits;

        internal void RestoreCommittedState(
            in TimelinePlaybackProgress progress,
            CanonicalTextValue sectionId,
            ReadOnlySpan<CanonicalTextValue> activeClipIds,
            ReadOnlySpan<AbilityTimelineTreeClipExit> exitedTreeDecisionClips,
            ReadOnlySpan<AbilityTimelineTreeClipExit> pendingTreeDecisionClips)
        {
            if (m_PendingAdvance.IsValid || m_StopPending)
                throw new InvalidOperationException($"Timeline restore '{RequestId}' has an uncommitted operation: advance={m_PendingAdvance.IsValid}, stop={m_StopPending}.");
            if (progress.CursorTime < FP._0 || progress.Cycle < 0 || progress.CursorTime > Content.Duration)
                throw new InvalidOperationException($"Timeline restore '{RequestId}' cursor is outside its content: time={progress.CursorTime}, duration={Content.Duration}, cycle={progress.Cycle}.");
            if (Math.Abs((long)progress.TimeCarry) * 2 > m_TickRate)
                throw new InvalidOperationException($"Timeline restore '{RequestId}' has an invalid time carry: carry={progress.TimeCarry}, tickRate={m_TickRate}.");
            if (progress.State != TimelineRuntimePlaybackState.Prepared &&
                progress.State != TimelineRuntimePlaybackState.Running &&
                progress.State != TimelineRuntimePlaybackState.Stopping &&
                progress.State != TimelineRuntimePlaybackState.Completed &&
                progress.State != TimelineRuntimePlaybackState.Stopped)
                throw new InvalidOperationException($"Timeline restore '{RequestId}' has an invalid committed state '{progress.State}'.");
            bool requiresStopContext = progress.State == TimelineRuntimePlaybackState.Stopping ||
                                       progress.State == TimelineRuntimePlaybackState.Stopped;
            if (requiresStopContext != progress.HasStopContext)
                throw new InvalidOperationException($"Timeline restore '{RequestId}' stop context does not match state '{progress.State}': hasStopContext={progress.HasStopContext}.");
            if (!ValidateRestoredSection(sectionId, progress.CursorTime, out string declaredSection))
                throw new InvalidOperationException($"Timeline restore '{RequestId}' section '{sectionId}' is not committed at time '{progress.CursorTime}'.");
            RequireRestoredActiveClips(activeClipIds, progress.State, progress.CursorTime, progress.Cycle, exitedTreeDecisionClips);
            m_CandidateEvaluation.Clear();
            m_CommittedEvaluation.Clear();
            m_SectionId = declaredSection;
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
            m_Progress = progress;
        }

        bool ValidateRestoredSection(CanonicalTextValue sectionId, FP cursorTime, out string declaredSection)
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

        void RequireRestoredActiveClips(
            ReadOnlySpan<CanonicalTextValue> activeClipIds,
            TimelineRuntimePlaybackState state,
            FP cursorTime,
            int cycle,
            ReadOnlySpan<AbilityTimelineTreeClipExit> exitedTreeDecisionClips)
        {
            int suppliedCount = activeClipIds.Length;
            if (state != TimelineRuntimePlaybackState.Running)
            {
                if (suppliedCount != 0)
                    throw new InvalidOperationException($"Timeline restore '{RequestId}' state '{state}' contains '{suppliedCount}' active clips at time '{cursorTime}'.");
                return;
            }
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
                    throw new InvalidOperationException($"Timeline restore '{RequestId}' active clip '{clip.AuthoringId}' has '{occurrences}' occurrences at time '{cursorTime}', cycle '{cycle}'.");
            }
            if (expectedCount != suppliedCount)
                throw new InvalidOperationException($"Timeline restore '{RequestId}' active clip count does not match time '{cursorTime}', cycle '{cycle}': expected={expectedCount}, supplied={suppliedCount}, clips=[{string.Join(", ", activeClipIds.ToArray())}].");
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
            CaptureActiveClips(m_Progress.CursorTime, m_Progress.Cycle);
            TimelineRuntimeSampleBuffer<CanonicalTextValue> active = m_CandidateEvaluation.ActiveClipIds;
            for (int index = 0; index < active.Count; index++)
                m_ActiveClipIds.Add(active[index]);
            m_SectionId = Index.SectionAt(m_Progress.CursorTime);
        }

        void CaptureActiveClips(FP cursorTime, int cycle)
        {
            TimelineRuntimeSampleBuffer<CanonicalTextValue> active = m_CandidateEvaluation.ActiveClipIds;
            List<int> selected = m_CandidateEvaluation.SamplingClips;
            selected.Clear();
            TimelineRuntimeClipIndex lifetimes = Index.Logic.Lifetimes;
            lifetimes.Select(cursorTime, cursorTime, true, selected);
            for (int index = 0; index < selected.Count; index++)
            {
                TimelineContentClip clip = Content.Clips[lifetimes[selected[index]].ContentIndex];
                bool treeDecision = clip.ExitSource == TimelineClipExitSource.TreeDecision;
                if (treeDecision
                    ? !ContainsExit(m_ExitedTreeDecisionClips, clip.AuthoringId, cycle) &&
                      !ContainsExit(m_PendingTreeClipExits, clip.AuthoringId, cycle)
                    : cursorTime < clip.EndTime)
                    active.Add(clip.AuthoringId);
            }
            active.Sort(s_CompareClipIds);
        }

        TimelineRuntimeSampleBuffer<TimelineRuntimeClipBoundary> CollectBoundaries(
            FP previousTime,
            int previousCycle,
            FP nextTime,
            int nextCycle,
            FP duration,
            bool loop,
            bool initialBoundaryPending,
            IReadOnlyList<AbilityTimelineTreeClipExit> exitedTreeDecisionClips,
            IReadOnlyList<AbilityTimelineTreeClipExit> pendingTreeClipExits)
        {
            TimelineRuntimeSampleBuffer<TimelineRuntimeClipBoundary> result = m_CandidateEvaluation.Boundaries;
            if (duration <= FP._0)
                return result;
            FP previousAbsolute = duration * previousCycle + previousTime;
            FP nextAbsolute = duration * nextCycle + nextTime;
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
                    FP from = cycle == previousCycle ? previousTime : FP._0;
                    FP to = cycle == nextCycle ? nextTime : duration;
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
            m_Progress.State = TimelineRuntimePlaybackState.Disposed;
        }

        static void AddBoundary(
            TimelineRuntimeSampleBuffer<TimelineRuntimeClipBoundary> result,
            TimelineContentClip clip,
            FP time,
            int cycle,
            TimelineRuntimeClipBoundaryKind kind,
            FP previousAbsolute,
            FP nextAbsolute,
            FP duration,
            bool initialBoundaryPending)
        {
            FP absolute = duration * cycle + time;
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
