using System;
using ThirdPersonSimulation;
using System.Collections.Generic;
using UnityEditor;

namespace BTSMTL.Diagnostics.Editor
{
    [InitializeOnLoad]
    public sealed class RuntimeDebugSession : IDisposable
    {
        const double CaptureRefreshIntervalSeconds = 0.1d;
        const double LiveRefreshIntervalSeconds = 1d / 30d;

        enum CaptureReadPurpose
        {
            History,
            Window,
            LiveProjection
        }

        static readonly RuntimeDebugSession s_Shared;

        readonly Dictionary<object, LiveInterestLease> m_LiveInterests = new Dictionary<object, LiveInterestLease>();
        readonly Dictionary<RuntimeContentRevision, RuntimeDebugSourceMapSnapshot> m_SourceMaps = new Dictionary<RuntimeContentRevision, RuntimeDebugSourceMapSnapshot>();
        RuntimeDiagnosticsTarget m_Target;
        RuntimeDebugTargetProvider m_Provider;
        RuntimeDebugFrozenDiagnostics m_Frozen;
        RuntimeDebugViewModel m_ViewModel = RuntimeDebugViewModel.Detached;
        RuntimeCaptureSnapshot m_CaptureSnapshot;
        RuntimeDebugAttachmentState m_AttachmentState;
        (RuntimeTraceDomain Domain, double TimeSeconds, ulong Sequence)? m_HistoryCursor;
        int m_CaptureSegmentLimit;
        long m_TargetRevision;
        double m_NextCaptureRefreshTime;
        double m_NextLiveRefreshTime;
        bool m_Disposed;
        RuntimeCaptureReadWindow m_ReadWindow = new(4096, 32768);
        readonly RuntimeExecutionTimelineBuilder.SpanAccumulator m_WindowSpans = new(32768);
        RuntimeCaptureSnapshot m_PendingSnapshot;
        RuntimeCaptureQuery m_PendingQuery;
        RuntimeExecutionTimeline m_WindowTimeline;
        RuntimeDebugViewModel m_HistoryModel;
        (Guid CaptureId, long Version) m_HistoryStateCursor;
        long m_ReadRequest;
        CaptureReadPurpose m_ReadPurpose;
        bool m_HasHistoryEvent;
        int m_HistoryOffset;
        double m_WindowStart;
        double m_WindowEnd = 10;
        double m_WindowPadding;
        RuntimeTraceDomain m_WindowDomain;
        bool m_WindowChanged;
        bool m_ArchiveProjection;
        bool m_BrowsingPastExecution;
        double m_LogicTimeOrigin;
        double m_PresentationTimeOrigin;
        (double Logic, double Presentation)? m_InvalidHistoryStart;
        bool m_HistoryNavigationEnabled = true;
        bool m_TimeReplay;
        readonly List<(Guid Branch, ulong Epoch, double Logic, double Presentation)> m_HistoryBranches = new();
        public long HistoryBranchRevision { get; private set; }
        double ReadWindowStart => Math.Max(0, m_WindowStart - m_WindowPadding);
        public long ProjectionVersion { get; private set; }
        public double GetExecutionTimeOrigin(RuntimeTraceDomain domain) => domain == RuntimeTraceDomain.Presentation
            ? m_PresentationTimeOrigin : m_LogicTimeOrigin;
        public bool IsReadingHistory => m_ReadRequest != 0 && m_ReadPurpose == CaptureReadPurpose.History;
        public bool IsReadingExecution => m_ReadRequest != 0 && m_ReadPurpose != CaptureReadPurpose.History;
        public string HistoryError { get; private set; } = string.Empty;
        public double? GetInvalidHistoryStart(RuntimeTraceDomain domain) => m_InvalidHistoryStart.HasValue
            ? domain == RuntimeTraceDomain.Presentation ? m_InvalidHistoryStart.Value.Presentation
                : m_InvalidHistoryStart.Value.Logic : null;

        public void InvalidateHistory(double logicTime, double presentationTime)
        {
            m_InvalidHistoryStart = (logicTime, presentationTime);
            NotifyChanged();
        }

        public void CompleteHistoryRegeneration()
        {
            m_InvalidHistoryStart = null;
            NotifyChanged();
        }

        public void SetHistoryNavigationEnabled(bool enabled)
        {
            m_HistoryNavigationEnabled = enabled;
            if (!enabled && IsReadingHistory)
            {
                m_PendingSnapshot.CancelRead();
                m_ReadRequest = 0;
                m_HasHistoryEvent = m_HistoryCursor.HasValue;
            }
        }

        static RuntimeDebugSession()
        {
            s_Shared = new RuntimeDebugSession();
        }

        RuntimeDebugSession()
        {
            RuntimeDiagnosticsTargetRegistry.TargetRegistered += OnTargetChanged;
            RuntimeDiagnosticsTargetRegistry.TargetUnregistered += OnTargetUnregistered;
            EditorApplication.update += Update;
        }

        public static RuntimeDebugSession Shared => s_Shared;
        public event Action Changed;
        public event Action HistoryCursorChanged;
        public RuntimeDebugViewModel ViewModel => m_ViewModel;
        public RuntimeDebugAttachmentState AttachmentState => m_AttachmentState;
        public long TargetRevision => m_TargetRevision;
        public bool CanControlLiveTarget => m_AttachmentState == RuntimeDebugAttachmentState.Live && m_Target != null;
        public bool CanResumeLiveTarget => m_HistoryNavigationEnabled && !m_InvalidHistoryStart.HasValue &&
            m_Target != null && (m_BrowsingPastExecution || IsReadingHistory ||
            m_AttachmentState is RuntimeDebugAttachmentState.Frozen or RuntimeDebugAttachmentState.CaptureHistory);
        public bool IsFollowingExecution => m_AttachmentState == RuntimeDebugAttachmentState.Live &&
            !m_BrowsingPastExecution;
        public bool CanStartCapture => CanControlLiveTarget;
        public bool CanStopCapture => m_Target != null && m_Target.Store.IsCaptureRecording;
        public bool IsCaptureRecording => m_Target != null && m_Target.Store.IsCaptureRecording;
        public RuntimeTraceChannel RecordingChannels => m_Target?.Store.RecordingChannels ?? RuntimeTraceChannel.None;
        public RuntimeCaptureSnapshot CaptureSnapshot => m_CaptureSnapshot;
        public bool HasCaptureHistory => m_CaptureSnapshot.IsValid;
        public int HistoryOffset => m_HistoryOffset;
        public ulong HistorySequence => m_HistoryCursor?.Sequence ?? ulong.MaxValue;
        public (RuntimeTraceDomain Domain, double TimeSeconds, Guid? Branch, ulong? Epoch)? HistoryCursor
        {
            get
            {
                if (m_AttachmentState is not (RuntimeDebugAttachmentState.CaptureHistory or RuntimeDebugAttachmentState.Ended))
                    return null;
                if (m_ReadPurpose == CaptureReadPurpose.History && m_PendingQuery.Kind == RuntimeCaptureQueryKind.Time &&
                    !m_HasHistoryEvent)
                    return (m_PendingQuery.Domain, m_PendingQuery.TimeSeconds, m_PendingQuery.Branch, m_PendingQuery.Epoch);
                if (m_HistoryCursor is not { } cursor)
                    return null;
                RuntimeTraceEvent selected = m_ReadWindow.Selected;
                return (cursor.Domain, cursor.TimeSeconds, selected.ExecutionBranchId, selected.RuntimeEpoch);
            }
        }
        public int ExecutionSpanCapacity => 32768;
        public Guid CaptureId => m_AttachmentState is RuntimeDebugAttachmentState.Frozen or RuntimeDebugAttachmentState.CaptureHistory or RuntimeDebugAttachmentState.Ended
            ? m_CaptureSnapshot.CaptureId
            : m_Provider?.CaptureId ?? Guid.Empty;
        public long CaptureVersion => m_AttachmentState is RuntimeDebugAttachmentState.Frozen or RuntimeDebugAttachmentState.CaptureHistory or RuntimeDebugAttachmentState.Ended
            ? m_CaptureSnapshot.Version
            : m_Provider?.CaptureVersion ?? 0;

        public void GetExecutionAuthoringCapacity(out int timelines, out int tracks)
        {
            timelines = 0;
            tracks = 0;
            foreach (RuntimeDebugSourceMapSnapshot sourceMap in m_SourceMaps.Values)
            {
                timelines += sourceMap.TimelineCount;
                tracks += sourceMap.TrackCount;
            }
        }

        public RuntimeExecutionTimeline BuildExecutionTimeline()
        {
            if (m_TimeReplay)
                return m_WindowTimeline ?? m_Provider.ExecutionTimeline;
            if (m_ArchiveProjection || m_AttachmentState != RuntimeDebugAttachmentState.Live)
                return m_WindowTimeline;
            if (m_Provider == null)
                return null;
            if (m_ReadRequest != 0 && m_ReadPurpose == CaptureReadPurpose.LiveProjection)
                return m_Provider.ExecutionTimeline ?? m_WindowTimeline;
            CaptureSourceMaps();
            RuntimeExecutionTimeline timeline = m_Provider.ReadExecutionTimeline(m_SourceMaps, out bool requiresArchiveSync);
            requiresArchiveSync |= !IsCaptureRecording && m_CaptureSnapshot.IsValid &&
                (timeline?.Version ?? 0) < m_CaptureSnapshot.Version;
            if (requiresArchiveSync && m_ReadRequest == 0 && HistoryError.Length == 0)
            {
                RuntimeCaptureSnapshot snapshot = GetExecutionCapture();
                QueueRead(snapshot, new RuntimeCaptureQuery(RuntimeCaptureQueryKind.LiveProjection,
                    m_WindowDomain, 0, 0, 0, 0, 0, true), CaptureReadPurpose.LiveProjection);
            }
            return timeline ?? m_WindowTimeline;
        }

        public void SetExecutionWindow(RuntimeTraceDomain domain, double start, double end, double padding,
            bool userNavigation)
        {
            start = Math.Max(0, start);
            end = Math.Max(start, end);
            if (domain == m_WindowDomain && start == m_WindowStart && end == m_WindowEnd && padding == m_WindowPadding)
                return;
            if (userNavigation && CanControlLiveTarget)
                m_BrowsingPastExecution = end < GetLiveExecutionTime(domain) - 1;
            m_WindowDomain = domain;
            m_WindowStart = start;
            m_WindowEnd = end;
            m_WindowPadding = padding;
            m_WindowChanged = true;
        }

        public double GetLiveExecutionTime(RuntimeTraceDomain domain) => domain == RuntimeTraceDomain.Presentation
            ? m_Target.Context.PresentationTimeSeconds
            : m_Target.Context.LogicTimeSeconds;

        public bool TryGetHistoryEvent(out RuntimeTraceEvent traceEvent)
        {
            traceEvent = m_ReadWindow.Selected;
            return m_HasHistoryEvent && m_AttachmentState is RuntimeDebugAttachmentState.CaptureHistory or RuntimeDebugAttachmentState.Ended;
        }

        public (Guid? Branch, ulong? Epoch) GetHistoryExecutionIdentity(RuntimeTraceDomain domain, double timeSeconds)
        {
            if (m_HistoryBranches.Count == 0)
                return default;
            int index = m_HistoryBranches.Count - 1;
            while (index > 0 && timeSeconds <= (domain == RuntimeTraceDomain.Logic
                ? m_HistoryBranches[index].Logic : m_HistoryBranches[index].Presentation))
                index--;
            return (m_HistoryBranches[index].Branch, m_HistoryBranches[index].Epoch);
        }

        public bool TrySeekExecutionTime(RuntimeTraceDomain domain, double timeSeconds, Guid? branch = null, ulong? epoch = null)
        {
            if (!m_HistoryNavigationEnabled || GetInvalidHistoryStart(domain) is { } invalidStart && timeSeconds > invalidStart)
                return false;
            if (m_HistoryBranches.Count != 0)
            {
                if (!branch.HasValue)
                    (branch, epoch) = GetHistoryExecutionIdentity(domain, timeSeconds);
                else if (!TryGetHistoryTimeRange(branch.Value, domain, timeSeconds, timeSeconds, out _, out _))
                    return false;
            }
            RuntimeTraceEvent selected = m_ReadWindow.Selected;
            if (m_ReadRequest == 0 && m_HasHistoryEvent && m_PendingQuery.Kind == RuntimeCaptureQueryKind.Time &&
                m_HistoryCursor?.Domain == domain &&
                domain == m_PendingQuery.Domain && branch == m_PendingQuery.Branch && epoch == m_PendingQuery.Epoch &&
                timeSeconds >= selected.TimeSeconds && timeSeconds < m_ReadWindow.NextTimeSeconds)
            {
                m_HistoryCursor = (domain, timeSeconds, selected.Sequence);
                HistoryCursorChanged?.Invoke();
                return true;
            }
            RuntimeCaptureSnapshot snapshot = GetExecutionCapture();
            if (!snapshot.IsValid)
                return false;
            QueueRead(snapshot, new RuntimeCaptureQuery(RuntimeCaptureQueryKind.Time, domain, timeSeconds,
                0, 0, ReadWindowStart, m_WindowEnd,
                m_AttachmentState == RuntimeDebugAttachmentState.Live || m_WindowTimeline == null || m_WindowChanged,
                branch, epoch), CaptureReadPurpose.History);
            return true;
        }

        public bool TryResolveHistoricalSource(
            RuntimeContentRevision revision,
            RuntimeSourceElementHandle handle,
            out RuntimeSourceElementKey source,
            out DebugSourceMapEntry entry)
        {
            if (m_SourceMaps.TryGetValue(revision, out RuntimeDebugSourceMapSnapshot sourceMap) &&
                sourceMap.TryGet(handle, out entry))
            {
                source = entry.Source;
                return source.IsValid;
            }
            source = default;
            entry = default;
            return false;
        }

        public int CaptureSegmentCount
        {
            get
            {
                if (m_Target != null && m_Target.Store.IsCaptureRecording)
                    return m_Target.Store.CaptureSegmentCount;
                return m_CaptureSnapshot.IsValid ? m_CaptureSnapshot.SegmentCount : 0;
            }
        }

        public int CaptureSegmentCapacity
        {
            get
            {
                if (m_Target != null && m_Target.Store.IsCaptureRecording)
                    return m_Target.Store.CaptureSegmentCapacity;
                return 0;
            }
        }

        public IReadOnlyList<RuntimeDebugTargetInfo> Targets
        {
            get
            {
                IReadOnlyList<RuntimeDiagnosticsTarget> targets = RuntimeDiagnosticsTargetRegistry.Targets;
                RuntimeDebugTargetInfo[] result = targets.Count == 0
                    ? Array.Empty<RuntimeDebugTargetInfo>()
                    : new RuntimeDebugTargetInfo[targets.Count];
                for (int i = 0; i < targets.Count; i++)
                    result[i] = new RuntimeDebugTargetInfo(targets[i]);
                return result;
            }
        }

        public bool AttachToTarget(Guid characterRuntimeId)
        {
            if (!RuntimeDiagnosticsTargetRegistry.TryGet(characterRuntimeId, out RuntimeDiagnosticsTarget target))
                return false;
            Attach(target);
            return true;
        }

        public bool AttachToHost(int hostInstanceId)
        {
            if (!TryFindTargetByHost(hostInstanceId, out RuntimeDiagnosticsTarget target))
                return false;
            Attach(target);
            return true;
        }

        public void ClearTarget()
        {
            m_TimeReplay = false;
            m_HistoryBranches.Clear();
            HistoryBranchRevision++;
            m_InvalidHistoryStart = null;
            m_HistoryNavigationEnabled = true;
            ClearRead();
            ReleaseLiveHandles();
            m_Target = null;
            m_Provider?.Dispose();
            m_Provider = null;
            ClearSourceMaps();
            m_Frozen = null;
            m_ViewModel = RuntimeDebugViewModel.Detached;
            m_CaptureSnapshot = default;
            m_HistoryCursor = null;
            m_CaptureSegmentLimit = 0;
            m_NextCaptureRefreshTime = 0d;
            m_NextLiveRefreshTime = 0d;
            m_LogicTimeOrigin = 0;
            m_PresentationTimeOrigin = 0;
            m_AttachmentState = RuntimeDebugAttachmentState.Detached;
            m_TargetRevision++;
            HistoryCursorChanged?.Invoke();
            NotifyChanged();
        }

        public void EnsureLiveInterest(object owner, RuntimeTraceChannel channels)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));

            channels &= RuntimeTraceChannel.All;
            if (channels == RuntimeTraceChannel.None)
            {
                ReleaseLiveInterest(owner);
                return;
            }

            if (!m_LiveInterests.TryGetValue(owner, out LiveInterestLease lease))
            {
                lease = new LiveInterestLease();
                m_LiveInterests.Add(owner, lease);
            }

            bool changed = lease.Channels != channels;
            if (changed)
            {
                ReleaseLiveHandle(lease);
                lease.Channels = channels;
            }

            if ((CanControlLiveTarget || m_TimeReplay) && !lease.Handle.IsValid)
            {
                lease.Handle = m_Target.Store.AcquireInterest(new RuntimeDiagnosticsInterest(RuntimeDiagnosticsInterestKind.LiveState, lease.Channels));
                changed = true;
            }

            if (!changed)
                return;

            if (!RefreshProvider())
                NotifyChanged();
        }

        public void ReleaseLiveInterest(object owner)
        {
            if (owner == null || !m_LiveInterests.TryGetValue(owner, out LiveInterestLease lease))
                return;

            ReleaseLiveHandle(lease);
            m_LiveInterests.Remove(owner);
            if (!RefreshProvider())
                NotifyChanged();
        }

        public void FreezeLive()
        {
            if (!CanControlLiveTarget)
                return;

            RefreshProvider();
            m_WindowTimeline = BuildExecutionTimeline();
            m_CaptureSnapshot = GetExecutionCapture();
            if (m_ReadRequest != 0 && m_ReadPurpose == CaptureReadPurpose.LiveProjection)
                QueueRead(m_CaptureSnapshot, m_PendingQuery, CaptureReadPurpose.Window);
            ReleaseLiveHandles();
            m_ViewModel = m_Provider.LiveModel;
            m_AttachmentState = RuntimeDebugAttachmentState.Frozen;
            NotifyChanged();
        }

        public void ResumeLive()
        {
            if (!CanResumeLiveTarget)
                return;

            SelectLiveTarget();
        }

        public void BeginTimeReplay()
        {
            m_SourceMaps.EnsureCapacity(m_Target.Context.SourceMapCapacity);
            m_CaptureSnapshot = GetExecutionCapture();
            if (m_ReadRequest != 0) m_PendingSnapshot.CancelRead();
            m_ReadRequest = 0;
            m_TimeReplay = true;
            RebindLiveInterests();
            SetHistoryNavigationEnabled(false);
        }

        public void AcceptTimeReplay(Guid branch, ulong epoch)
        {
            m_TimeReplay = false;
            ClearSourceMaps();
            m_HistoryBranches.Clear();
            m_HistoryBranches.Add((branch, epoch, double.NegativeInfinity, double.NegativeInfinity));
            HistoryBranchRevision++;
            ProjectionVersion++;
            m_CaptureSnapshot = default;
            m_WindowTimeline = null;
            m_InvalidHistoryStart = null;
            SetHistoryNavigationEnabled(true);
            SelectLiveTarget();
        }

        public void CancelTimeReplay()
        {
            m_TimeReplay = false;
            SetHistoryNavigationEnabled(true);
            NotifyChanged();
        }

        public void BeginLiveHistoryRegeneration(Guid sourceBranch, Guid branch, ulong epoch,
            double logicTime, double presentationTime)
        {
            if (m_HistoryBranches.Count == 0)
                m_HistoryBranches.Add((sourceBranch, epoch, double.NegativeInfinity, double.NegativeInfinity));
            int source = FindHistoryBranch(sourceBranch);
            if (source < 0)
                throw new InvalidOperationException("历史修改来源不在当前有效分支中。");
            m_HistoryBranches.RemoveRange(source + 1, m_HistoryBranches.Count - source - 1);
            m_HistoryBranches.Add((branch, epoch, logicTime, presentationTime));
            HistoryBranchRevision++;
            ProjectionVersion++;
            BeginLiveHistoryRegeneration();
        }

        int FindHistoryBranch(Guid branch)
        {
            for (int i = 0; i < m_HistoryBranches.Count; i++)
                if (m_HistoryBranches[i].Branch == branch)
                    return i;
            return -1;
        }

        public bool TryGetHistoryTimeRange(Guid branch, RuntimeTraceDomain domain,
            double start, double end, out double from, out double to)
        {
            from = start;
            to = end;
            if (m_HistoryBranches.Count == 0)
                return true;
            int index = FindHistoryBranch(branch);
            if (index < 0)
                return false;
            var current = m_HistoryBranches[index];
            double first = domain == RuntimeTraceDomain.Logic ? current.Logic : current.Presentation;
            double last = index + 1 == m_HistoryBranches.Count ? double.PositiveInfinity
                : domain == RuntimeTraceDomain.Logic ? m_HistoryBranches[index + 1].Logic : m_HistoryBranches[index + 1].Presentation;
            from = Math.Max(start, first);
            to = Math.Min(end, last);
            return to >= from;
        }

        public double GetLatestHistoryTime(RuntimeExecutionTimeline timeline, RuntimeTraceDomain domain)
        {
            if (m_HistoryBranches.Count == 0)
                return domain == RuntimeTraceDomain.Logic ? timeline.LatestLogicTimeSeconds : timeline.LatestPresentationTimeSeconds;
            var current = m_HistoryBranches[m_HistoryBranches.Count - 1];
            double first = domain == RuntimeTraceDomain.Logic ? current.Logic : current.Presentation;
            return timeline.TryGetClockTime(domain, current.Branch, current.Epoch, out double time)
                ? Math.Max(first, time) : first;
        }

        public void BeginLiveHistoryRegeneration()
        {
            SetHistoryNavigationEnabled(false);
            SelectLiveTarget();
        }

        void SelectLiveTarget()
        {
            if (m_ReadRequest != 0)
                m_PendingSnapshot.CancelRead();
            m_ReadRequest = 0;
            m_HasHistoryEvent = false;
            m_ArchiveProjection = false;
            m_BrowsingPastExecution = false;
            m_WindowChanged = false;
            m_AttachmentState = RuntimeDebugAttachmentState.Live;
            m_HistoryCursor = null;
            HistoryError = string.Empty;
            m_CaptureSegmentLimit = 0;
            m_NextCaptureRefreshTime = 0d;
            m_NextLiveRefreshTime = 0d;
            m_ViewModel = m_Provider.LiveModel;
            RebindLiveInterests();
            RefreshProvider();
            HistoryCursorChanged?.Invoke();
            NotifyChanged();
        }

        RuntimeCaptureSnapshot GetExecutionCapture()
        {
            if (m_AttachmentState is RuntimeDebugAttachmentState.Frozen or RuntimeDebugAttachmentState.CaptureHistory ||
                !IsCaptureRecording)
                return m_CaptureSnapshot;
            CaptureSourceMaps();
            return m_Target.Store.FreezeActiveCapture();
        }

        public bool BeginCapture(RuntimeTraceChannel channels, RuntimeDiagnosticsCaptureDetail detail)
        {
            return BeginCaptureCore(channels, detail, 0);
        }

        public bool IncludeCaptureChannels(RuntimeTraceChannel channels)
        {
            if (!IsCaptureRecording)
                return false;
            return m_Provider.BeginCapture(channels, RuntimeDiagnosticsCaptureDetail.Continuous, out _);
        }

        public bool BeginBoundedCapture(
            RuntimeTraceChannel channels,
            RuntimeDiagnosticsCaptureDetail detail,
            int maximumSegments)
        {
            if (maximumSegments <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumSegments));
            return BeginCaptureCore(channels, detail, maximumSegments);
        }

        bool BeginCaptureCore(
            RuntimeTraceChannel channels,
            RuntimeDiagnosticsCaptureDetail detail,
            int maximumSegments)
        {
            if (!CanStartCapture)
                return false;

            Guid previousCapture = m_CaptureSnapshot.IsValid ? m_CaptureSnapshot.CaptureId : m_Provider.CaptureId;
            if (!m_Provider.BeginCapture(channels, detail, out Guid captureId))
                return false;

            if (captureId != previousCapture)
            {
                m_HistoryBranches.Clear();
                HistoryBranchRevision++;
                ClearRead();
                m_LogicTimeOrigin = m_Target.Context.LogicTimeSeconds;
                m_PresentationTimeOrigin = m_Target.Context.PresentationTimeSeconds;
                m_HistoryBranches.Add((m_Target.Context.ExecutionBranchId, m_Target.Context.RuntimeEpoch,
                    m_LogicTimeOrigin, m_PresentationTimeOrigin));
                m_WindowStart = GetExecutionTimeOrigin(m_WindowDomain);
                m_WindowEnd = m_WindowStart + 2;
                m_WindowPadding = 0;
                m_WindowChanged = false;
            }

            m_CaptureSnapshot = default;
            m_HistoryCursor = null;
            m_CaptureSegmentLimit = maximumSegments;
            m_NextCaptureRefreshTime = 0d;
            RefreshProvider();
            HistoryCursorChanged?.Invoke();
            NotifyChanged();
            return true;
        }

        public void ResetExecutionTimeOrigin()
        {
            double previous = GetExecutionTimeOrigin(m_WindowDomain);
            m_LogicTimeOrigin = m_Target.Context.LogicTimeSeconds;
            m_PresentationTimeOrigin = m_Target.Context.PresentationTimeSeconds;
            double shift = GetExecutionTimeOrigin(m_WindowDomain) - previous;
            m_WindowStart += shift;
            m_WindowEnd += shift;
            m_WindowChanged = true;
            m_HistoryCursor = null;
            HistoryCursorChanged?.Invoke();
            NotifyChanged();
        }

        public bool EndCapture()
        {
            if (!CanStopCapture)
                return false;

            CaptureSourceMaps();
            RuntimeCaptureSnapshot snapshot = m_Provider.EndCapture();
            if (!snapshot.IsValid)
                return false;

            m_CaptureSnapshot = snapshot;
            m_HistoryCursor = null;
            m_CaptureSegmentLimit = 0;
            m_NextCaptureRefreshTime = 0d;
            ReleaseLiveHandles();
            QueueRead(snapshot, new RuntimeCaptureQuery(RuntimeCaptureQueryKind.Latest, m_WindowDomain, 0, 0, 0,
                ReadWindowStart, m_WindowEnd, true), CaptureReadPurpose.History);
            return true;
        }

        public bool TrySeekExecutionEvent(Guid captureId, ulong sequence)
        {
            if (!m_HistoryNavigationEnabled)
                return false;
            RuntimeCaptureSnapshot snapshot = GetExecutionCapture();
            if (!snapshot.IsValid || snapshot.CaptureId != captureId)
                return false;
            QueueRead(snapshot, new RuntimeCaptureQuery(RuntimeCaptureQueryKind.Event, m_WindowDomain, 0,
                sequence, 0, ReadWindowStart, m_WindowEnd,
                m_AttachmentState == RuntimeDebugAttachmentState.Live || m_WindowTimeline == null || m_WindowChanged),
                CaptureReadPurpose.History);
            return true;
        }

        public void SetHistoryOffset(int offset)
        {
            if (!m_HistoryNavigationEnabled || !m_CaptureSnapshot.IsValid)
                return;
            int clamped = Math.Max(0, Math.Min(offset, m_CaptureSnapshot.SegmentCount - 1));
            QueueRead(m_CaptureSnapshot, new RuntimeCaptureQuery(RuntimeCaptureQueryKind.Segment,
                m_WindowDomain, 0, 0, clamped, ReadWindowStart, m_WindowEnd,
                m_WindowTimeline == null || m_WindowChanged), CaptureReadPurpose.History);
        }

        void QueueRead(RuntimeCaptureSnapshot snapshot, RuntimeCaptureQuery query, CaptureReadPurpose purpose)
        {
            HistoryError = string.Empty;
            m_PendingSnapshot = snapshot;
            m_PendingQuery = query;
            m_ReadPurpose = purpose;
            m_ReadRequest = 0;
            try
            {
                long stateVersion = snapshot.CaptureId == m_HistoryStateCursor.CaptureId ? m_HistoryStateCursor.Version : 0;
                m_ReadRequest = snapshot.BeginRead(in query, stateVersion);
            }
            catch (Exception error)
            {
                HistoryError = error.Message;
            }
            if (query.IncludeExecutionWindow && purpose != CaptureReadPurpose.LiveProjection)
                m_WindowChanged = false;
            m_WindowTimeline ??= m_Provider?.ExecutionTimeline;
            if (purpose == CaptureReadPurpose.History)
            {
                ReleaseLiveHandles();
                m_HasHistoryEvent = false;
                m_CaptureSnapshot = snapshot;
                RuntimeDebugAttachmentState state = m_Target == null
                    ? RuntimeDebugAttachmentState.Ended : RuntimeDebugAttachmentState.CaptureHistory;
                if (m_AttachmentState != state)
                {
                    m_AttachmentState = state;
                    NotifyChanged();
                }
                HistoryCursorChanged?.Invoke();
            }
        }

        void CompleteRead()
        {
            if (m_ReadRequest == 0)
                return;
            RuntimeCaptureReadStatus status;
            try
            {
                var latestTime = m_ReadPurpose == CaptureReadPurpose.History &&
                    m_PendingQuery.Kind != RuntimeCaptureQueryKind.Time ? m_InvalidHistoryStart : null;
                if (!m_PendingSnapshot.TryTakeRead(m_ReadRequest, ref m_ReadWindow, latestTime, out status))
                    return;
            }
            catch (Exception error)
            {
                m_ReadRequest = 0;
                m_HasHistoryEvent = false;
                HistoryError = error.Message;
                NotifyChanged();
                return;
            }
            m_ReadRequest = 0;
            if (status == RuntimeCaptureReadStatus.Missing)
            {
                m_HasHistoryEvent = false;
                HistoryError = "此位置没有已记录的事件。";
                NotifyChanged();
                return;
            }
            if (status == RuntimeCaptureReadStatus.OutsideTimeRange)
            {
                m_HasHistoryEvent = m_HistoryCursor.HasValue;
                HistoryError = "该位置的缓存已标脏，请播放重新记录。";
                NotifyChanged();
                return;
            }
            RuntimeDebugSourceMapSnapshot map = m_Target != null
                ? m_SourceMaps[m_Target.Revision] : m_Frozen.SourceMap;
            if (m_ReadPurpose == CaptureReadPurpose.LiveProjection)
            {
                m_WindowTimeline = m_Provider.RestoreExecutionTimeline(m_PendingSnapshot, m_ReadWindow, m_SourceMaps);
                ProjectionVersion++;
                NotifyChanged();
                return;
            }
            if (m_PendingQuery.IncludeExecutionWindow)
            {
                m_WindowTimeline = RuntimeExecutionTimelineBuilder.BuildWindow(
                    m_PendingSnapshot, m_ReadWindow, map, m_SourceMaps, m_WindowSpans);
                ProjectionVersion++;
            }
            if (m_ReadPurpose == CaptureReadPurpose.History)
            {
                RuntimeTraceEvent selected = m_ReadWindow.Selected;
                m_HistoryOffset = m_ReadWindow.HistoryOffset;
                m_HistoryCursor = m_PendingQuery.Kind == RuntimeCaptureQueryKind.Time
                    ? (m_PendingQuery.Domain, m_PendingQuery.TimeSeconds, selected.Sequence)
                    : (selected.Domain, selected.TimeSeconds, selected.Sequence);
                m_HasHistoryEvent = true;
                m_HistoryModel ??= new RuntimeDebugViewModel(m_ViewModel.Target, map, m_PendingSnapshot.Channels, 4096);
                m_HistoryModel.SetChannels(m_PendingSnapshot.Channels);
                m_HistoryModel.SetCoverage(m_ReadWindow.EvictedStates, m_PendingSnapshot.EvictedEvents != 0);
                m_HistoryModel.ApplyRecordedState(m_ReadWindow.State, m_SourceMaps);
                m_HistoryStateCursor = (m_PendingSnapshot.CaptureId, m_ReadWindow.State.Version);
                m_ViewModel = m_HistoryModel;
                HistoryCursorChanged?.Invoke();
            }
            NotifyChanged();
        }

        void ClearRead()
        {
            if (m_ReadRequest != 0)
                m_PendingSnapshot.CancelRead();
            m_ReadRequest = 0;
            m_HasHistoryEvent = false;
            m_PendingSnapshot = default;
            m_WindowTimeline = null;
            m_ArchiveProjection = false;
            m_BrowsingPastExecution = false;
            m_HistoryModel = null;
            m_HistoryStateCursor = default;
            m_HistoryOffset = 0;
            HistoryError = string.Empty;
            m_WindowSpans.Reset();
            m_ReadWindow.Clear();
        }

        public IReadOnlyList<RuntimeDebugTargetCandidate> GetTargetCandidates(RuntimeDebugTargetRequest request)
        {
            IReadOnlyList<RuntimeDiagnosticsTarget> targets = RuntimeDiagnosticsTargetRegistry.Targets;
            RuntimeDebugTargetCandidate[] candidates = targets.Count == 0
                ? Array.Empty<RuntimeDebugTargetCandidate>()
                : new RuntimeDebugTargetCandidate[targets.Count];
            for (int i = 0; i < targets.Count; i++)
                candidates[i] = new RuntimeDebugTargetCandidate(new RuntimeDebugTargetInfo(targets[i]), MatchTarget(targets[i], request));
            return candidates;
        }

        public RuntimeDebugTargetResolution ResolveTarget(RuntimeDebugTargetRequest request)
        {
            RuntimeDebugSceneSelection explicitSelection = RuntimeDebugSceneSelectionRegistry.Resolve();
            if (m_AttachmentState == RuntimeDebugAttachmentState.Ended)
            {
                if (explicitSelection.HasExplicitHost && TryFindTargetByHost(explicitSelection.HostInstanceId, out RuntimeDiagnosticsTarget replacement))
                {
                    RuntimeDebugTargetMatch replacementMatch = MatchTarget(replacement, request);
                    if (replacementMatch == RuntimeDebugTargetMatch.Exact)
                    {
                        Attach(replacement);
                        return new RuntimeDebugTargetResolution(RuntimeDebugTargetResolutionStatus.Attached);
                    }
                    return new RuntimeDebugTargetResolution(RuntimeDebugTargetResolutionStatus.ExplicitHostSourceMissing, GetTargetCandidates(request));
                }

                return CreateEndedResolution(request);
            }

            if (explicitSelection.HasExplicitHost)
            {
                if (!TryFindTargetByHost(explicitSelection.HostInstanceId, out RuntimeDiagnosticsTarget selectedTarget))
                    return new RuntimeDebugTargetResolution(RuntimeDebugTargetResolutionStatus.ExplicitHostUnregistered, GetTargetCandidates(request));

                RuntimeDebugTargetMatch selectedMatch = MatchTarget(selectedTarget, request);
                if (selectedMatch != RuntimeDebugTargetMatch.Exact)
                    return new RuntimeDebugTargetResolution(RuntimeDebugTargetResolutionStatus.ExplicitHostSourceMissing, GetTargetCandidates(request));

                Attach(selectedTarget);
                return new RuntimeDebugTargetResolution(RuntimeDebugTargetResolutionStatus.Attached);
            }

            if (m_Target != null && MatchTarget(m_Target, request) == RuntimeDebugTargetMatch.Exact)
                return new RuntimeDebugTargetResolution(RuntimeDebugTargetResolutionStatus.Attached);

            IReadOnlyList<RuntimeDebugTargetCandidate> candidates = GetTargetCandidates(request);
            RuntimeDiagnosticsTarget exactTarget = null;
            int exactCount = 0;
            IReadOnlyList<RuntimeDiagnosticsTarget> targets = RuntimeDiagnosticsTargetRegistry.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                if (!candidates[i].IsExact)
                    continue;
                exactTarget = targets[i];
                exactCount++;
            }

            if (exactCount == 1)
            {
                Attach(exactTarget);
                return new RuntimeDebugTargetResolution(RuntimeDebugTargetResolutionStatus.Attached, candidates);
            }

            return new RuntimeDebugTargetResolution(
                exactCount == 0
                    ? RuntimeDebugTargetResolutionStatus.NoExactTarget
                    : RuntimeDebugTargetResolutionStatus.MultipleExactTargets,
                candidates);
        }

        public RuntimeDebugTargetResolution ResolvePinnedTarget(Guid characterRuntimeId)
        {
            if (m_ViewModel.Target.CharacterRuntimeId != characterRuntimeId)
                return new RuntimeDebugTargetResolution(RuntimeDebugTargetResolutionStatus.PinnedTargetNotAttached);
            if (m_AttachmentState == RuntimeDebugAttachmentState.Ended)
                return new RuntimeDebugTargetResolution(RuntimeDebugTargetResolutionStatus.Ended);
            if (m_Target == null || m_Target.CharacterRuntimeId != characterRuntimeId)
                return new RuntimeDebugTargetResolution(RuntimeDebugTargetResolutionStatus.PinnedTargetNotAttached);
            return new RuntimeDebugTargetResolution(RuntimeDebugTargetResolutionStatus.Attached);
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;
            m_Disposed = true;
            RuntimeDiagnosticsTargetRegistry.TargetRegistered -= OnTargetChanged;
            RuntimeDiagnosticsTargetRegistry.TargetUnregistered -= OnTargetUnregistered;
            EditorApplication.update -= Update;
            ClearTarget();
            m_LiveInterests.Clear();
        }

        void Attach(RuntimeDiagnosticsTarget target)
        {
            if (target == null || ReferenceEquals(m_Target, target))
                return;

            ClearRead();
            ReleaseLiveHandles();
            m_HistoryBranches.Clear();
            HistoryBranchRevision++;
            m_Target = target;
            m_Provider?.Dispose();
            m_Provider = new RuntimeDebugTargetProvider(target);
            ClearSourceMaps();
            AddSourceMap(m_Provider.SourceMap);
            m_Frozen = null;
            m_ViewModel = m_Provider.LiveModel;
            m_CaptureSnapshot = default;
            m_HistoryCursor = null;
            m_AttachmentState = RuntimeDebugAttachmentState.Live;
            m_NextLiveRefreshTime = 0d;
            RebindLiveInterests();
            RefreshProvider();
            m_TargetRevision++;
            HistoryCursorChanged?.Invoke();
            NotifyChanged();
        }

        void Update()
        {
            if (m_Disposed || m_TimeReplay)
                return;
            CompleteRead();
            if (m_WindowChanged && m_ReadRequest == 0 && m_CaptureSnapshot.IsValid &&
                m_AttachmentState is RuntimeDebugAttachmentState.Frozen or RuntimeDebugAttachmentState.CaptureHistory or RuntimeDebugAttachmentState.Ended)
            {
                RuntimeCaptureQueryKind kind = m_HistoryCursor.HasValue
                    ? RuntimeCaptureQueryKind.Event : RuntimeCaptureQueryKind.Latest;
                QueueRead(m_CaptureSnapshot, new RuntimeCaptureQuery(kind,
                    m_WindowDomain, 0, HistorySequence, 0, ReadWindowStart, m_WindowEnd, true), CaptureReadPurpose.Window);
            }
            if (!CanControlLiveTarget)
                return;

            double now = EditorApplication.timeSinceStartup;
            if (IsCaptureRecording)
            {
                if (m_CaptureSegmentLimit > 0 && CaptureSegmentCount >= m_CaptureSegmentLimit)
                {
                    EndCapture();
                    return;
                }

                if (now < m_NextCaptureRefreshTime)
                    return;
                m_NextCaptureRefreshTime = now + CaptureRefreshIntervalSeconds;
            }
            else
            {
                if (now < m_NextLiveRefreshTime)
                    return;
                m_NextLiveRefreshTime = now + LiveRefreshIntervalSeconds;
            }
            if (m_Target.Store.HasCaptureArchive && (IsCaptureRecording || m_CaptureSnapshot.IsValid) &&
                (m_ReadRequest == 0 || m_ReadPurpose == CaptureReadPurpose.LiveProjection))
            {
                double liveTime = GetLiveExecutionTime(m_WindowDomain);
                bool wasArchive = m_ArchiveProjection;
                m_ArchiveProjection = m_BrowsingPastExecution || ReadWindowStart <
                    Math.Max(0, liveTime - RuntimeCaptureReadWindow.LiveProjectionDurationSeconds);
                bool refreshArchive = m_WindowChanged || !wasArchive ||
                    !m_BrowsingPastExecution && m_Provider.CaptureVersion > m_WindowTimeline.Version;
                if (m_ArchiveProjection && refreshArchive)
                {
                    RuntimeCaptureSnapshot snapshot = GetExecutionCapture();
                    QueueRead(snapshot, new RuntimeCaptureQuery(RuntimeCaptureQueryKind.Latest, m_WindowDomain, 0, 0, 0,
                        ReadWindowStart, m_WindowEnd, true), CaptureReadPurpose.Window);
                }
                else if (wasArchive && !m_ArchiveProjection)
                {
                    ProjectionVersion++;
                    NotifyChanged();
                }
                m_WindowChanged = false;
            }
            if (RefreshProvider())
                NotifyChanged();
        }

        bool RefreshProvider()
        {
            if (m_TimeReplay || m_Provider == null || m_AttachmentState != RuntimeDebugAttachmentState.Live)
                return false;

            if (m_Target != null &&
                !m_Provider.LiveModel.Target.Revision.Equals(m_Target.Revision))
            {
                AddSourceMap(m_Provider.SourceMap);
                m_Provider.Dispose();
                m_Provider = new RuntimeDebugTargetProvider(m_Target);
                AddSourceMap(m_Provider.SourceMap);
                m_ViewModel = m_Provider.LiveModel;
                m_TargetRevision++;
                NotifyChanged();
            }

            CaptureSourceMaps();
            bool changed = m_Provider.Refresh(m_SourceMaps);
            if (m_AttachmentState == RuntimeDebugAttachmentState.Live)
                m_ViewModel = m_Provider.LiveModel;
            return changed;
        }

        void CaptureSourceMaps()
        {
            if (m_TimeReplay || m_Target == null)
                return;
            foreach (IDebugSourceMap sourceMap in m_Target.Context.SourceMaps)
                AddSourceMap(RuntimeDebugSourceMapSnapshot.Capture(sourceMap));
        }

        void AddSourceMap(RuntimeDebugSourceMapSnapshot sourceMap)
        {
            m_SourceMaps.TryAdd(sourceMap.Revision, sourceMap);
        }

        void ClearSourceMaps()
        {
            m_SourceMaps.Clear();
        }

        void RebindLiveInterests()
        {
            if (!CanControlLiveTarget && !m_TimeReplay)
                return;

            foreach (LiveInterestLease lease in m_LiveInterests.Values)
            {
                if (lease.Channels != RuntimeTraceChannel.None && !lease.Handle.IsValid)
                    lease.Handle = m_Target.Store.AcquireInterest(new RuntimeDiagnosticsInterest(RuntimeDiagnosticsInterestKind.LiveState, lease.Channels));
            }
        }

        void ReleaseLiveHandles()
        {
            foreach (LiveInterestLease lease in m_LiveInterests.Values)
                ReleaseLiveHandle(lease);
        }

        void ReleaseLiveHandle(LiveInterestLease lease)
        {
            if (lease == null || !lease.Handle.IsValid)
                return;

            if (m_Target != null)
                m_Target.Store.ReleaseInterest(lease.Handle);
            lease.Handle = default;
        }

        void FreezeTarget(RuntimeDiagnosticsTarget target)
        {
            if (target == null)
                return;

            ReleaseLiveHandles();
            CaptureSourceMaps();
            m_Frozen = m_AttachmentState == RuntimeDebugAttachmentState.Frozen
                ? new RuntimeDebugFrozenDiagnostics(m_ViewModel, m_Provider.SourceMap, m_CaptureSnapshot, m_SourceMaps)
                : m_Provider?.Freeze(m_SourceMaps);
            RuntimeCaptureSnapshot activeCapture = m_Frozen?.ActiveCapture ?? default;
            if (m_AttachmentState != RuntimeDebugAttachmentState.CaptureHistory && activeCapture.IsValid)
                m_CaptureSnapshot = activeCapture;
            m_ViewModel = m_AttachmentState == RuntimeDebugAttachmentState.CaptureHistory && m_HistoryModel != null
                ? m_HistoryModel : m_Frozen?.LiveModel ?? RuntimeDebugViewModel.Detached;
            if (m_AttachmentState == RuntimeDebugAttachmentState.Live && !m_ArchiveProjection &&
                m_CaptureSnapshot.IsValid && m_CaptureSnapshot.Version > 0 &&
                (m_ReadRequest == 0 || m_ReadPurpose == CaptureReadPurpose.LiveProjection))
            {
                m_WindowTimeline = m_Provider.ExecutionTimeline ?? m_WindowTimeline;
                QueueRead(m_CaptureSnapshot, new RuntimeCaptureQuery(RuntimeCaptureQueryKind.LiveProjection,
                    m_WindowDomain, 0, 0, 0, 0, 0, true), CaptureReadPurpose.Window);
            }
            m_Target = null;
            m_Provider?.Dispose();
            m_Provider = null;
            m_AttachmentState = RuntimeDebugAttachmentState.Ended;
            m_TargetRevision++;
            NotifyChanged();
        }

        RuntimeDebugTargetResolution CreateEndedResolution(RuntimeDebugTargetRequest request)
        {
            RuntimeDebugTargetMatch match = m_Frozen?.MatchSource(request) ?? RuntimeDebugTargetMatch.SourceMissing;
            if (match == RuntimeDebugTargetMatch.Exact)
                return new RuntimeDebugTargetResolution(RuntimeDebugTargetResolutionStatus.Ended);
            return new RuntimeDebugTargetResolution(RuntimeDebugTargetResolutionStatus.SourceMissing);
        }

        static RuntimeDebugTargetMatch MatchTarget(RuntimeDiagnosticsTarget target, RuntimeDebugTargetRequest request)
        {
            foreach (IDebugSourceMap sourceMap in target.Context.SourceMaps)
            {
                IReadOnlyList<RuntimeSourceElementHandle> handles = sourceMap.FindHandles(request.Source);
                if (handles.Count != 0)
                    return RuntimeDebugTargetMatch.Exact;
            }
            return RuntimeDebugTargetMatch.SourceMissing;
        }

        static bool TryFindTargetByHost(int hostInstanceId, out RuntimeDiagnosticsTarget target)
        {
            IReadOnlyList<RuntimeDiagnosticsTarget> targets = RuntimeDiagnosticsTargetRegistry.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].HostInstanceId == hostInstanceId)
                {
                    target = targets[i];
                    return true;
                }
            }

            target = null;
            return false;
        }

        void OnTargetChanged(RuntimeDiagnosticsTarget target)
        {
            m_TargetRevision++;
            NotifyChanged();
        }

        void OnTargetUnregistered(RuntimeDiagnosticsTarget target)
        {
            if (ReferenceEquals(m_Target, target))
                FreezeTarget(target);
            else
            {
                m_TargetRevision++;
                NotifyChanged();
            }
        }

        void NotifyChanged()
        {
            Changed?.Invoke();
        }

        sealed class LiveInterestLease
        {
            public RuntimeTraceChannel Channels;
            public RuntimeDiagnosticsInterestHandle Handle;
        }
    }
}
