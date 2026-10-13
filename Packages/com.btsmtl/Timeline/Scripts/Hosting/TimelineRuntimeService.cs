using System;
using System.Globalization;
using ThirdPersonSimulation;
using ThirdPersonSimulation.Fixed;
using System.Collections.Generic;
using BTSMTL.Diagnostics;

namespace BTSMTL.Timeline.Runtime
{
    public interface ITimelineRuntimeCallBindingSource
    {
        bool TryCreateExecutionIdentity(
            string sourceId,
            string sourceName,
            TimelinePlaybackActionContext actionContext,
            ulong playbackIdentity,
            out TimelineExecutionIdentity identity,
            out string error);

        IReadOnlyList<TimelineCallBinding> CallBindings { get; }
    }

    public sealed class TimelineRuntimePlaybackRequestFactory
    {
        readonly TimelineRuntimeNumericTarget m_NumericTarget;
        readonly ITimelineDomainBindingResolver m_DomainResolver;
        readonly ITimelineRuntimeDependencyResolver m_DependencyResolver;
        readonly ITimelineRuntimeCallBindingSource m_CallBindingSource;
        readonly Dictionary<TimelineData, TimelineRuntimePreparedContent> m_Contents = new();
        CanonicalTextReferences m_RequestText;

        public TimelineRuntimePlaybackRequestFactory(
            TimelineRuntimeNumericTarget numericTarget,
            ITimelineDomainBindingResolver domainResolver,
            ITimelineRuntimeDependencyResolver dependencyResolver,
            ITimelineRuntimeCallBindingSource callBindingSource)
        {
            m_NumericTarget = numericTarget;
            m_DomainResolver = domainResolver ?? throw new ArgumentNullException(nameof(domainResolver));
            m_DependencyResolver = dependencyResolver ?? throw new ArgumentNullException(nameof(dependencyResolver));
            m_CallBindingSource = callBindingSource ?? throw new ArgumentNullException(nameof(callBindingSource));
            m_RequestText = CreateRequestText(0);
        }

        internal void PrepareRequestText(int maximumSteps)
        {
            m_RequestText.Reset();
            m_RequestText = CreateRequestText(maximumSteps);
        }

        static CanonicalTextReferences CreateRequestText(int maximumSteps)
        {
            int values = checked(AbilityTimelineRuntimeLimits.MaximumPlaybackCount * (maximumSteps + 1) + 1);
            return new CanonicalTextReferences(1, new CanonicalTextStorage(values, checked(values * 29)));
        }

        internal TimelineRuntimePreparedContent InstallContent(TimelineData timeline, TimelineContentUnit content)
        {
            TimelineRuntimePreparedContent prepared = TimelineRuntimePreparation.PrepareContent(
                content, m_NumericTarget, m_CallBindingSource.CallBindings,
                m_DomainResolver, m_DependencyResolver);
            if (!prepared.IsReady)
                throw new InvalidOperationException(string.Join(" | ", prepared.Errors));
            m_Contents.Add(timeline, prepared);
            return prepared;
        }

        internal TimelineRuntimePreparedContent GetContent(TimelineData timeline) => m_Contents[timeline];

        internal TimelineRuntimePreparedContent InstallTimeRangeCandidate(TimelineData baseline,
            TimelineData timeline, TimelineContentUnit content)
        {
            var prepared = new TimelineRuntimePreparedContent(m_Contents[baseline], content);
            m_Contents.Add(timeline, prepared);
            return prepared;
        }

        internal void RetireContent(TimelineData timeline) => m_Contents.Remove(timeline);

        internal void Clear()
        {
            m_RequestText.Reset();
            m_Contents.Clear();
        }

        public bool TryCreate(
            TimelineData timeline,
            string sourceId,
            string sourceName,
            TimelinePlaybackActionContext actionContext,
            TimelinePlaybackMode playbackMode,
            ulong playbackIdentity,
            out TimelineRuntimePrepareRequest request,
            out string error,
            FixedScalar startTime = default)
        {
            request = default;
            error = string.Empty;
            if (timeline == null || playbackIdentity == 0)
            {
                error = "timeline_runtime_request_invalid";
                return false;
            }
            if (!m_CallBindingSource.TryCreateExecutionIdentity(
                    sourceId,
                    sourceName,
                    actionContext,
                    playbackIdentity,
                    out TimelineExecutionIdentity identity,
                    out error))
                return false;
            Span<char> requestIdentity = stackalloc char[29];
            "timeline:".AsSpan().CopyTo(requestIdentity);
            playbackIdentity.TryFormat(requestIdentity.Slice(9), out int digitCount,
                provider: CultureInfo.InvariantCulture);
            m_RequestText.Reset();
            request = new TimelineRuntimePrepareRequest(
                m_RequestText.CaptureCharacters(requestIdentity.Slice(0, 9 + digitCount)),
                GetContent(timeline),
                identity,
                playbackMode,
                startTime);
            return true;
        }

    }

    public sealed class TimelineRuntimeExecutionConsumer :
        ITimelineRuntimeStepConsumer,
        ITimelineRuntimeStepCommitConsumer,
        ITimelineRuntimeStopConsumer,
        ITimelineRuntimeStopCommitConsumer
    {
        readonly ITimelineRuntimeEvaluationSink m_EvaluationSink;
        readonly ITimelineRuntimeTreeClipService m_TreeClipService;
        readonly ITimelineRuntimeMarkerService m_MarkerService;

        public TimelineRuntimeExecutionConsumer(
            ITimelineRuntimeEvaluationSink evaluationSink,
            ITimelineRuntimeTreeClipService treeClipService,
            ITimelineRuntimeMarkerService markerService)
        {
            m_EvaluationSink = evaluationSink ?? throw new ArgumentNullException(nameof(evaluationSink));
            m_TreeClipService = treeClipService ?? throw new ArgumentNullException(nameof(treeClipService));
            m_MarkerService = markerService ?? throw new ArgumentNullException(nameof(markerService));
        }

        public TimelineRuntimeStepDecision Consume(TimelineRuntimeStepContext context)
        {
            TimelineRuntimePlayback playback = context.Playback;
            playback.ConsumedMarkerCount = 0;
            playback.ConsumedTreeClipCount = 0;
            int consumedMarkerCount = 0;
            for (; consumedMarkerCount < context.Advance.Evaluation.Markers.Count; consumedMarkerCount++)
            {
                TimelineRuntimeMarkerRequest marker = context.Advance.Evaluation.Markers[consumedMarkerCount];
                if (!m_MarkerService.Consume(marker, context))
                    break;
                playback.ConsumedMarkerCount = consumedMarkerCount + 1;
            }
            if (consumedMarkerCount != context.Advance.Evaluation.Markers.Count)
                return TimelineRuntimeStepDecision.Discard;

            int consumedTreeClipCount = 0;
            for (; consumedTreeClipCount < context.Advance.Evaluation.TreeClips.Count; consumedTreeClipCount++)
            {
                TimelineRuntimeTreeClipRequest request = context.Advance.Evaluation.TreeClips[consumedTreeClipCount];
                if (!m_TreeClipService.Consume(request, context))
                    break;
                playback.ConsumedTreeClipCount = consumedTreeClipCount + 1;
            }
            if (consumedTreeClipCount != context.Advance.Evaluation.TreeClips.Count)
                return TimelineRuntimeStepDecision.Discard;
            return m_EvaluationSink.Consume(context)
                ? TimelineRuntimeStepDecision.Commit
                : TimelineRuntimeStepDecision.Discard;
        }

        public bool ConsumeStop(TimelineRuntimeStopRequest request)
        {
            return m_MarkerService.ConsumeStop(request) &&
                   m_TreeClipService.ConsumeStop(request) &&
                   m_EvaluationSink.ConsumeStop(request);
        }

        public void Commit(TimelineRuntimeStepContext context)
        {
            m_MarkerService.Commit(context);
            m_TreeClipService.Commit(context);
            m_EvaluationSink.Commit(context);
            context.Playback.ConsumedTreeClipCount = 0;
            context.Playback.ConsumedMarkerCount = 0;
        }

        public void Discard(TimelineRuntimeStepContext context)
        {
            int count = context.Playback.ConsumedTreeClipCount;
            int markerCount = context.Playback.ConsumedMarkerCount;
            for (int index = 0; index < markerCount; index++)
                m_MarkerService.Discard(context.Advance.Evaluation.Markers[index], context);
            for (int index = 0; index < count; index++)
                m_TreeClipService.Discard(context.Advance.Evaluation.TreeClips[index], context);
            m_MarkerService.DiscardStep(context);
            m_TreeClipService.DiscardStep(context);
            m_EvaluationSink.Discard(context);
            context.Playback.ConsumedTreeClipCount = 0;
            context.Playback.ConsumedMarkerCount = 0;
        }

        public void CommitStop(TimelineRuntimeStopRequest request)
        {
            m_MarkerService.CommitStop(request);
            m_TreeClipService.CommitStop(request);
            m_EvaluationSink.CommitStop(request);
            request.Playback.ConsumedTreeClipCount = 0;
            request.Playback.ConsumedMarkerCount = 0;
        }

        public void DiscardStop(TimelineRuntimeStopRequest request)
        {
            m_MarkerService.DiscardStop(request);
            m_TreeClipService.DiscardStop(request);
            m_EvaluationSink.DiscardStop(request);
            request.Playback.ConsumedTreeClipCount = 0;
            request.Playback.ConsumedMarkerCount = 0;
        }
    }

    public readonly struct TimelineRuntimeRestoreCandidate
    {
        readonly TimelineRuntimePlaybackSnapshot m_Snapshot;
        readonly TimelineRuntimePreparationResult m_Preparation;
        readonly TimelineRuntimeService m_Service;

        internal TimelineRuntimeRestoreCandidate(
            TimelineRuntimeService service,
            TimelineRuntimePlaybackSnapshot snapshot,
            TimelineRuntimePreparationResult preparation)
        {
            m_Service = service ?? throw new ArgumentNullException(nameof(service));
            if (!snapshot.IsValid)
                throw new ArgumentException("Timeline restore snapshot is invalid.", nameof(snapshot));
            m_Snapshot = snapshot;
            m_Preparation = preparation;
            Validate();
        }

        public string Schema => m_Snapshot.Schema;
        public TimelineRuntimePlaybackHandle Handle => m_Snapshot.Handle;
        public ulong Generation => m_Snapshot.Generation;
        public TimelineExecutionIdentity ExecutionIdentity => m_Snapshot.Playback.ExecutionIdentity;
        public TimelineRuntimeNumericTarget NumericTarget => m_Snapshot.NumericTarget;
        public CanonicalTextValue ContentRevision => m_Preparation.ContentRevision;

        internal TimelineRuntimePlaybackSnapshot Snapshot => m_Snapshot;
        internal TimelineRuntimePreparationResult Preparation => m_Preparation;

        internal void ValidateOwnedBy(TimelineRuntimeService service)
        {
            if (!ReferenceEquals(m_Service, service))
                throw new InvalidOperationException("Timeline restore candidate belongs to another runtime service.");
        }

        internal TimelineRuntimePlayback CreatePlayback()
        {
            TimelineRuntimePlayback playback = m_Service.CreatePlayback(
                m_Preparation,
                m_Snapshot.Handle,
                m_Snapshot.Generation);
            ApplyTo(playback);
            return playback;
        }

        internal void ApplyTo(TimelineRuntimePlayback playback)
        {
            if (playback.Handle != m_Snapshot.Handle || playback.Generation != m_Snapshot.Generation ||
                playback.ExecutionIdentity != m_Snapshot.Playback.ExecutionIdentity || playback.PlaybackMode != m_Snapshot.PlaybackMode ||
                playback.NumericTarget != m_Snapshot.NumericTarget || playback.TickRate != m_Snapshot.TickRate ||
                !playback.RequestId.Equals(m_Snapshot.Playback.RequestId) ||
                !m_Snapshot.Playback.ContentRevision.Equals(playback.ContentRevision))
                throw new InvalidOperationException("Timeline restore targets a different playback identity or content.");
            playback.RestoreCommittedState(
                m_Snapshot.Playback.State,
                m_Snapshot.Playback.CursorTime,
                m_Snapshot.Playback.Cycle,
                m_Snapshot.Playback.TimeCarry,
                m_Snapshot.Playback.Control,
                m_Snapshot.Playback.SectionId,
                m_Snapshot.Playback.ActiveClipIds.Span,
                m_Snapshot.Playback.TreeDecisionExits.Span,
                m_Snapshot.Playback.PendingTreeDecisionExits.Span,
                m_Snapshot.Playback.HasStopContext,
                m_Snapshot.Playback.StopContext,
                m_Snapshot.Playback.InitialBoundaryPending);
        }

        void Validate()
        {
            if (!string.Equals(m_Snapshot.Schema, TimelineRuntimePlaybackSnapshot.CurrentSchema, StringComparison.Ordinal) ||
                !m_Snapshot.Handle.IsValid ||
                m_Snapshot.Generation == 0 ||
                m_Snapshot.Playback.State == TimelineRuntimePlaybackState.Disposed ||
                m_Snapshot.Playback.State == TimelineRuntimePlaybackState.Failed ||
                !m_Preparation.IsReady ||
                m_Snapshot.Playback.ExecutionIdentity != m_Preparation.ExecutionIdentity ||
                !m_Snapshot.Playback.RequestId.Equals(m_Preparation.RequestId) ||
                m_Snapshot.PlaybackMode != m_Preparation.PlaybackMode ||
                m_Snapshot.NumericTarget != m_Preparation.NumericTarget ||
                m_Snapshot.TickRate != m_Service.TickRate ||
                !m_Snapshot.Playback.ContentRevision.Equals(m_Preparation.ContentRevision))
                throw new InvalidOperationException("Timeline restore snapshot does not match the prepared content.");
        }
    }
    public sealed class TimelineRuntimeService : IDisposable
    {
        readonly TimelineRuntimePlaybackRequestFactory m_RequestFactory;
        readonly ITimelineRuntimeStepConsumer m_StepConsumer;
        readonly ITimelineRuntimeStopConsumer m_StopConsumer;
        readonly int m_TickRate;
        readonly Dictionary<ulong, TimelineRuntimePlayback> m_Playbacks =
            new Dictionary<ulong, TimelineRuntimePlayback>();
        readonly Dictionary<(TimelineContentUnit Timeline, TimelinePlaybackMode Mode), Stack<TimelineRuntimePlayback>> m_RecycledPlaybacks =
            new Dictionary<(TimelineContentUnit Timeline, TimelinePlaybackMode Mode), Stack<TimelineRuntimePlayback>>();
        readonly TimelineRuntimeEvaluationStoragePool m_EvaluationStoragePool = new();
        ulong m_NextPlaybackHandle = 1;
        ulong m_NextGeneration = 1;
        bool m_Disposed;
        int m_SnapshotMaximumSteps;

        public string LastFailure { get; private set; } = string.Empty;
        internal int TickRate => m_TickRate;
        public int MaximumMotionContributionsPerAdvance => m_EvaluationStoragePool.MaximumMotionContributionsPerAdvance;

        public TimelineRuntimeService(
            TimelineRuntimePlaybackRequestFactory requestFactory,
            ITimelineRuntimeStepConsumer stepConsumer,
            ITimelineRuntimeStopConsumer stopConsumer,
            int tickRate)
        {
            m_RequestFactory = requestFactory ?? throw new ArgumentNullException(nameof(requestFactory));
            m_StepConsumer = stepConsumer ?? throw new ArgumentNullException(nameof(stepConsumer));
            m_StopConsumer = stopConsumer ?? throw new ArgumentNullException(nameof(stopConsumer));
            if (tickRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            m_TickRate = tickRate;
        }

        public TimelineRuntimePreparedContent InstallContent(
            TimelineData timeline,
            TimelineContentUnit content,
            IReadOnlyList<TimelinePlaybackMode> playbackModes)
        {
            TimelineRuntimePreparedContent prepared = m_RequestFactory.InstallContent(timeline, content);
            for (int index = 0; index < playbackModes.Count; index++)
            {
                m_EvaluationStoragePool.Prepare(prepared, playbackModes[index]);
                var key = (prepared.Content, playbackModes[index]);
                if (!m_RecycledPlaybacks.ContainsKey(key))
                    m_RecycledPlaybacks.Add(key, PreparePlaybackStorage(prepared.Content, playbackModes[index]));
            }
            return prepared;
        }

        public TimelineRuntimePreparedContent GetPreparedContent(TimelineData timeline) =>
            m_RequestFactory.GetContent(timeline);

        public TimelineRuntimePreparedContent InstallTimeRangeCandidate(TimelineData baseline, TimelineData timeline,
            TimelineContentUnit content, IReadOnlyList<TimelinePlaybackMode> playbackModes)
        {
            TimelineRuntimePreparedContent prepared = m_RequestFactory.InstallTimeRangeCandidate(baseline, timeline, content);
            for (int i = 0; i < playbackModes.Count; i++)
            {
                m_EvaluationStoragePool.Prepare(prepared, playbackModes[i]);
                m_RecycledPlaybacks.Add((content, playbackModes[i]), PreparePlaybackStorage(content, playbackModes[i]));
            }
            return prepared;
        }

        public void RetireContent(
            TimelineData timeline,
            IReadOnlyList<TimelinePlaybackMode> playbackModes)
        {
            TimelineContentUnit content = m_RequestFactory.GetContent(timeline).Content;
            m_RequestFactory.RetireContent(timeline);
            for (int index = 0; index < playbackModes.Count; index++)
            {
                TimelinePlaybackMode mode = playbackModes[index];
                m_RecycledPlaybacks.Remove((content, mode));
                m_EvaluationStoragePool.RetireContent(content, mode);
            }
        }

        Stack<TimelineRuntimePlayback> PreparePlaybackStorage(TimelineContentUnit content, TimelinePlaybackMode mode)
        {
            var recycled = new Stack<TimelineRuntimePlayback>(4);
            var playback = new TimelineRuntimePlayback(content, mode, m_TickRate, m_EvaluationStoragePool);
            playback.SnapshotStorage.PrepareCapacity(m_SnapshotMaximumSteps);
            recycled.Push(playback);
            return recycled;
        }

        public bool RequestTimelinePlayback(
            TimelineData timeline,
            ulong playbackIdentity,
            string sourceId,
            string sourceName,
            TimelinePlaybackActionContext actionContext,
            TimelinePlaybackMode playbackMode,
            out TimelinePlaybackHandle handle,
            FixedScalar startTime = default)
        {
            handle = TimelinePlaybackHandle.Invalid;
            EnsureAvailable();
            LastFailure = string.Empty;
            TimelineRuntimePlaybackHandle runtimeHandle = NextHandle();
            ulong generation = NextGeneration();
            if (!m_RequestFactory.TryCreate(
                    timeline,
                    sourceId,
                    sourceName,
                    actionContext,
                    playbackMode,
                    playbackIdentity,
                    out TimelineRuntimePrepareRequest request,
                    out string requestError,
                    startTime))
            {
                LastFailure = requestError ?? "timeline_runtime_request_failed";
                return false;
            }
            var preparation = new TimelineRuntimePreparationResult(request);
            TimelineRuntimePlayback playback = CreatePlayback(preparation, runtimeHandle, generation);
            if (!playback.Start())
            {
                RecyclePlayback(playback);
                return false;
            }
            m_Playbacks.Add(runtimeHandle.Value, playback);
            handle = new TimelinePlaybackHandle(runtimeHandle.Value);
            return true;
        }

        public TimelinePlaybackStatus GetTimelinePlaybackStatus(TimelinePlaybackHandle handle)
        {
            if (!TryGet(handle, out TimelineRuntimePlayback playback))
                return TimelinePlaybackStatus.None;
            return playback.State switch
            {
                TimelineRuntimePlaybackState.Prepared => TimelinePlaybackStatus.Requested,
                TimelineRuntimePlaybackState.Running => TimelinePlaybackStatus.Running,
                TimelineRuntimePlaybackState.Stopping => TimelinePlaybackStatus.Running,
                TimelineRuntimePlaybackState.Completed => TimelinePlaybackStatus.Succeeded,
                TimelineRuntimePlaybackState.Stopped => TimelinePlaybackStatus.Cancelled,
                TimelineRuntimePlaybackState.Failed => TimelinePlaybackStatus.Failed,
                _ => TimelinePlaybackStatus.None
            };
        }

        public void CancelTimelinePlayback(
            TimelinePlaybackHandle handle,
            TimelinePlaybackStopContext stopContext)
        {
            TimelineRuntimePlayback playback = Require(handle);
            if (playback.State == TimelineRuntimePlaybackState.Completed ||
                playback.State == TimelineRuntimePlaybackState.Stopped ||
                playback.State == TimelineRuntimePlaybackState.Disposed)
                return;
            if (!RequestStopTimelinePlayback(handle, stopContext, out TimelineRuntimeStopRequest request))
                throw new InvalidOperationException($"Timeline playback '{handle.Value}' rejected Stop.");
            try
            {
                if (!CommitStopTimelinePlayback(handle, request))
                    throw new InvalidOperationException($"Timeline playback '{handle.Value}' Stop could not be committed.");
            }
            catch
            {
                if (playback.HasPendingStop)
                    DiscardStopTimelinePlayback(handle, request);
                throw;
            }
        }

        public bool RequestStopTimelinePlayback(
            TimelinePlaybackHandle handle,
            TimelinePlaybackStopContext stopContext,
            out TimelineRuntimeStopRequest request)
        {
            TimelineRuntimePlayback playback = Require(handle);
            request = default;
            if (!playback.RequestStop(stopContext))
                return false;
            request = new TimelineRuntimeStopRequest(playback, stopContext);
            bool accepted = false;
            try
            {
                accepted = m_StopConsumer.ConsumeStop(request);
                return accepted;
            }
            finally
            {
                if (!accepted)
                {
                    playback.DiscardStop();
                    if (m_StopConsumer is ITimelineRuntimeStopCommitConsumer discardConsumer)
                        discardConsumer.DiscardStop(request);
                    request = default;
                }
            }
        }

        public bool CommitStopTimelinePlayback(TimelinePlaybackHandle handle, TimelineRuntimeStopRequest request)
        {
            TimelineRuntimePlayback playback = Require(handle);
            if (!request.IsPendingFor(playback) || !playback.CommitStop())
                return false;
            if (m_StopConsumer is ITimelineRuntimeStopCommitConsumer commitConsumer)
                commitConsumer.CommitStop(request);
            if (!playback.CompleteStop())
                throw new InvalidOperationException($"Timeline playback '{handle.Value}' Stop could not complete.");
            return true;
        }

        public bool DiscardStopTimelinePlayback(TimelinePlaybackHandle handle, TimelineRuntimeStopRequest request)
        {
            TimelineRuntimePlayback playback = Require(handle);
            if (!request.IsPendingFor(playback) || !playback.DiscardStop())
                return false;
            if (m_StopConsumer is ITimelineRuntimeStopCommitConsumer discardConsumer)
                discardConsumer.DiscardStop(request);
            return true;
        }
        public TimelineRuntimePreparationResult Prepare(
            CanonicalTextValue requestId,
            TimelineData timeline,
            TimelineExecutionIdentity executionIdentity,
            TimelinePlaybackMode playbackMode,
            FixedScalar startTime = default)
        {
            EnsureAvailable();
            var request = new TimelineRuntimePrepareRequest(
                requestId, m_RequestFactory.GetContent(timeline), executionIdentity, playbackMode, startTime);
            var result = new TimelineRuntimePreparationResult(request);
            LastFailure = string.Empty;
            return result;
        }

        public TimelineRuntimePlaybackHandle CreatePlayback(TimelineRuntimePreparationResult preparation)
        {
            EnsureAvailable();
            TimelineRuntimePlaybackHandle handle = NextHandle();
            ulong generation = NextGeneration();
            TimelineRuntimePlayback playback = CreatePlayback(preparation, handle, generation);
            m_Playbacks.Add(handle.Value, playback);
            return handle;
        }

        public bool RequestTreeClipExit(TimelineRuntimePlaybackHandle handle, CanonicalTextValue clipAuthoringId, int cycle)
        {
            return Require(handle).RequestTreeClipExit(clipAuthoringId, cycle);
        }

        public TimelineRuntimeAdvanceResult Advance(
            TimelineRuntimePlaybackHandle handle,
            ulong logicTick,
            int tickCount,
            AbilityTimelinePlaybackControl control)
        {
            TimelineRuntimePlayback playback = Require(handle);
            var request = playback.CreateAdvanceRequest(logicTick, tickCount, control);
            TimelineRuntimeAdvanceResult result = playback.Advance(request);
            var context = new TimelineRuntimeStepContext(playback, request, result);
            try
            {
                if (m_StepConsumer.Consume(context) != TimelineRuntimeStepDecision.Commit)
                    throw new InvalidOperationException("Timeline advance was rejected by its execution consumer.");
            }
            catch
            {
                playback.Discard(result);
                if (m_StepConsumer is ITimelineRuntimeStepCommitConsumer commitConsumer)
                    commitConsumer.Discard(context);
                throw;
            }
            return result;
        }

        public bool CommitAdvance(
            TimelineRuntimePlaybackHandle handle,
            TimelineRuntimeAdvanceResult advance)
        {
            TimelineRuntimePlayback playback = Require(handle);
            if (!playback.Commit(advance))
                return false;
            if (m_StepConsumer is ITimelineRuntimeStepCommitConsumer commitConsumer)
                commitConsumer.Commit(new TimelineRuntimeStepContext(playback, advance.Request, advance));
            return true;
        }

        public bool DiscardAdvance(
            TimelineRuntimePlaybackHandle handle,
            TimelineRuntimeAdvanceResult advance)
        {
            TimelineRuntimePlayback playback = Require(handle);
            if (!playback.Discard(advance))
                return false;
            if (m_StepConsumer is ITimelineRuntimeStepCommitConsumer commitConsumer)
                commitConsumer.Discard(new TimelineRuntimeStepContext(playback, advance.Request, advance));
            return true;
        }

        internal void DiscardEvaluation(TimelineRuntimePlaybackHandle handle)
        {
            TimelineRuntimePlayback playback = Require(handle);
            if (playback.HasPendingAdvance || playback.HasPendingStop)
                throw new InvalidOperationException("Timeline evaluation discard requires no pending candidates.");
            playback.DiscardEvaluation();
        }

        internal void ReleasePlayback(TimelineRuntimePlaybackHandle handle)
        {
            TimelineRuntimePlayback playback = Require(handle);
            if (playback.HasPendingAdvance || playback.HasPendingStop)
                throw new InvalidOperationException("Timeline playback release requires no pending candidates.");
            m_Playbacks.Remove(handle.Value);
            RecyclePlayback(playback);
        }

        public TimelineRuntimePlaybackSnapshot Capture(TimelineRuntimePlaybackHandle handle) =>
            new TimelineRuntimePlaybackSnapshot(RequireCaptureBoundary(handle));

        public TimelineRuntimePlaybackSnapshot CaptureRuntimeSnapshot(TimelineRuntimePlaybackHandle handle,
            out TimelineSnapshotItems<AbilityTimelineTreeClipState> treeClips)
        {
            TimelineRuntimePlayback playback = RequireCaptureBoundary(handle);
            AbilityTimelineSnapshotStorage storage = playback.SnapshotStorage.BeginWrite();
            treeClips = storage.ActiveTreeClips.Capture(playback.ActiveTreeClips);
            return new TimelineRuntimePlaybackSnapshot(playback, storage);
        }

        TimelineRuntimePlayback RequireCaptureBoundary(TimelineRuntimePlaybackHandle handle)
        {
            TimelineRuntimePlayback playback = Require(handle);
            if (playback.HasPendingAdvance || playback.HasPendingStop)
                throw new InvalidOperationException("Timeline playback Capture requires a committed step boundary.");
            return playback;
        }

        public void PrepareSnapshotStorage(int maximumSteps)
        {
            m_SnapshotMaximumSteps = maximumSteps;
            m_RequestFactory.PrepareRequestText(maximumSteps);
            foreach (TimelineRuntimePlayback playback in m_Playbacks.Values)
                playback.SnapshotStorage.PrepareCapacity(maximumSteps);
            foreach (Stack<TimelineRuntimePlayback> recycled in m_RecycledPlaybacks.Values)
                foreach (TimelineRuntimePlayback playback in recycled)
                    playback.SnapshotStorage.PrepareCapacity(maximumSteps);
        }

        public TimelineRuntimePreparationResult GetPreparation(TimelineRuntimePlaybackHandle handle)
        {
            return Require(handle).Preparation;
        }

        public TimelineRuntimeRestoreCandidate PrepareRestore(
            TimelineRuntimePlaybackSnapshot snapshot,
            TimelineRuntimePreparationResult preparation)
        {
            EnsureAvailable();
            return new TimelineRuntimeRestoreCandidate(this, snapshot, preparation);
        }

        public TimelineRuntimePlaybackHandle ApplyRestore(
            TimelineRuntimeRestoreCandidate candidate)
        {
            EnsureAvailable();
            candidate.ValidateOwnedBy(this);
            if (m_Playbacks.TryGetValue(candidate.Handle.Value, out TimelineRuntimePlayback playback))
                candidate.ApplyTo(playback);
            else
            {
                playback = candidate.CreatePlayback();
                m_Playbacks.Add(playback.Handle.Value, playback);
            }
            if (m_NextPlaybackHandle <= playback.Handle.Value)
                m_NextPlaybackHandle = checked(playback.Handle.Value + 1);
            if (m_NextGeneration <= playback.Generation)
                m_NextGeneration = checked(playback.Generation + 1);
            return playback.Handle;
        }
        public void Dispose()
        {
            if (m_Disposed)
                return;
            try
            {
                foreach (TimelineRuntimePlayback playback in m_Playbacks.Values)
                {
                    try
                    {
                        if (playback.State == TimelineRuntimePlaybackState.Prepared ||
                            playback.State == TimelineRuntimePlaybackState.Running)
                        {
                            CancelTimelinePlayback(
                                new TimelinePlaybackHandle(playback.Handle.Value),
                                new TimelinePlaybackStopContext(TimelinePlaybackStopCause.Shutdown, 0));
                        }
                    }
                    finally
                    {
                        playback.Dispose();
                    }
                }
            }
            finally
            {
                m_Disposed = true;
                m_Playbacks.Clear();
                m_RecycledPlaybacks.Clear();
                m_EvaluationStoragePool.Clear();
                m_RequestFactory.Clear();
            }
        }

        TimelineRuntimePlaybackHandle NextHandle()
        {
            if (m_NextPlaybackHandle == 0)
                throw new InvalidOperationException("Timeline playback handle space is exhausted.");
            return new TimelineRuntimePlaybackHandle(m_NextPlaybackHandle++);
        }

        ulong NextGeneration()
        {
            if (m_NextGeneration == 0)
                throw new InvalidOperationException("Timeline playback generation space is exhausted.");
            return m_NextGeneration++;
        }

        internal TimelineRuntimePlayback CreatePlayback(
            TimelineRuntimePreparationResult preparation,
            TimelineRuntimePlaybackHandle handle,
            ulong generation)
        {
            var key = (preparation.Content, preparation.PlaybackMode);
            if (m_RecycledPlaybacks.TryGetValue(key, out Stack<TimelineRuntimePlayback> recycled) &&
                recycled.Count > 0)
            {
                TimelineRuntimePlayback playback = recycled.Pop();
                playback.ResetForReuse(handle, generation, preparation);
                return playback;
            }
            TimelineRuntimePlayback created = TimelineRuntimePreparation.CreatePlayback(
                preparation,
                handle,
                generation,
                m_TickRate,
                m_EvaluationStoragePool);
            created.SnapshotStorage.PrepareCapacity(m_SnapshotMaximumSteps);
            return created;
        }

        void RecyclePlayback(TimelineRuntimePlayback playback)
        {
            playback.ReleaseForReuse();
            var key = (playback.Content, playback.PlaybackMode);
            m_RecycledPlaybacks[key].Push(playback);
        }

        bool TryGet(TimelinePlaybackHandle handle, out TimelineRuntimePlayback playback) =>
            TryGetPlayback(new TimelineRuntimePlaybackHandle(handle.Value), out playback);

        public bool TryGetPlayback(TimelineRuntimePlaybackHandle handle, out TimelineRuntimePlayback playback)
        {
            if (handle.IsValid && m_Playbacks.TryGetValue(handle.Value, out playback))
                return true;
            playback = null;
            return false;
        }

        public TimelineRuntimePlayback Require(TimelineRuntimePlaybackHandle handle)
        {
            EnsureAvailable();
            return TryGetPlayback(handle, out TimelineRuntimePlayback playback)
                ? playback
                : throw new InvalidOperationException($"Timeline playback '{handle.Value}' is not registered.");
        }

        TimelineRuntimePlayback Require(TimelinePlaybackHandle handle) =>
            Require(new TimelineRuntimePlaybackHandle(handle.Value));

        void EnsureAvailable()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(TimelineRuntimeService));
        }
    }
}
