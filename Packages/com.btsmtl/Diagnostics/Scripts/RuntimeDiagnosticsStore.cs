using System;
using System.Collections.Generic;

namespace BTSMTL.Diagnostics
{
    public enum RuntimeDiagnosticsInterestKind
    {
        LiveState,
        Capture
    }

    public enum RuntimeDiagnosticsCaptureDetail
    {
        None,
        Boundary,
        Evaluation,
        Continuous
    }

    public readonly struct RuntimeDiagnosticsInterest
    {
        public RuntimeDiagnosticsInterest(
            RuntimeDiagnosticsInterestKind kind,
            RuntimeTraceChannel channels,
            RuntimeDiagnosticsCaptureDetail captureDetail = RuntimeDiagnosticsCaptureDetail.None)
        {
            Kind = kind;
            Channels = channels & RuntimeTraceChannel.All;
            CaptureDetail = kind == RuntimeDiagnosticsInterestKind.Capture
                ? captureDetail
                : RuntimeDiagnosticsCaptureDetail.None;
        }

        public RuntimeDiagnosticsInterestKind Kind { get; }
        public RuntimeTraceChannel Channels { get; }
        public RuntimeDiagnosticsCaptureDetail CaptureDetail { get; }
        public bool IsValid => Channels != RuntimeTraceChannel.None &&
                               (Kind != RuntimeDiagnosticsInterestKind.Capture || CaptureDetail != RuntimeDiagnosticsCaptureDetail.None);
    }

    public readonly struct RuntimeDiagnosticsInterestHandle : IEquatable<RuntimeDiagnosticsInterestHandle>
    {
        internal RuntimeDiagnosticsInterestHandle(Guid value)
        {
            Value = value;
        }

        internal Guid Value { get; }
        public bool IsValid => Value != Guid.Empty;
        public bool Equals(RuntimeDiagnosticsInterestHandle other) => Value.Equals(other.Value);
        public override bool Equals(object obj) => obj is RuntimeDiagnosticsInterestHandle other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
    }

    public readonly struct RuntimeLiveStateKey : IEquatable<RuntimeLiveStateKey>
    {
        public RuntimeLiveStateKey(in RuntimeTraceEvent traceEvent)
        {
            Channel = traceEvent.Channel;
            Source = traceEvent.Source;
            Instance = traceEvent.RuntimeInstance;
            Kind = traceEvent.Kind;
            ref readonly RuntimeTracePayload payload = ref RuntimeTraceEvent.ReadPayload(in traceEvent);
            bool blackboard = Kind is RuntimeTraceEventKind.BlackboardWritten or RuntimeTraceEventKind.BlackboardSnapshot;
            BlackboardStateSlot = blackboard ? payload.BlackboardStateSlot : -1;
            BlackboardAbilityId = blackboard ? payload.SkillId : string.Empty;
            bool metadata = Kind == RuntimeTraceEventKind.ValueSampled &&
                (Channel == RuntimeTraceChannel.GameplayEffect || payload.DetailKind == RuntimeTraceDetailKind.AbilityMetadata);
            SampleIdentity = metadata ? payload.Name : default;
        }

        public RuntimeTraceChannel Channel { get; }
        public RuntimeSourceElementHandle Source { get; }
        public RuntimeInstanceKey Instance { get; }
        public RuntimeTraceEventKind Kind { get; }
        public int BlackboardStateSlot { get; }
        public string BlackboardAbilityId { get; }
        public string SampleIdentity { get; }

        public bool Equals(RuntimeLiveStateKey other)
        {
            return Channel == other.Channel && Source.Equals(other.Source) && Instance.Equals(other.Instance) && Kind == other.Kind &&
                BlackboardStateSlot == other.BlackboardStateSlot && string.Equals(BlackboardAbilityId, other.BlackboardAbilityId, StringComparison.Ordinal) &&
                string.Equals(SampleIdentity, other.SampleIdentity, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is RuntimeLiveStateKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Channel, Source, Instance, Kind, BlackboardStateSlot, BlackboardAbilityId, SampleIdentity);
    }

    public readonly struct RuntimeLiveStateChange
    {
        public RuntimeLiveStateChange(long revision, RuntimeLiveStateKey key, in RuntimeTraceEvent traceEvent, bool isRemoval)
        {
            Revision = revision;
            Key = key;
            TraceEvent = traceEvent;
            IsRemoval = isRemoval;
        }

        public long Revision { get; }
        public RuntimeLiveStateKey Key { get; }
        public RuntimeTraceEvent TraceEvent { get; }
        public bool IsRemoval { get; }
    }

    public readonly struct RuntimeLiveStateRead
    {
        public RuntimeLiveStateRead(long version, bool requiresFullSync, IReadOnlyList<RuntimeLiveStateChange> changes, long evictedStates = 0)
        {
            Version = version;
            RequiresFullSync = requiresFullSync;
            Changes = changes ?? Array.Empty<RuntimeLiveStateChange>();
            EvictedStates = evictedStates;
        }

        public long Version { get; }
        public bool RequiresFullSync { get; }
        public IReadOnlyList<RuntimeLiveStateChange> Changes { get; }
        public long EvictedStates { get; }
    }

    public readonly struct RuntimeCaptureChange
    {
        public RuntimeCaptureChange(long revision, in RuntimeTraceEvent traceEvent)
        {
            Revision = revision;
            TraceEvent = traceEvent;
        }

        public long Revision { get; }
        public RuntimeTraceEvent TraceEvent { get; }
    }

    public readonly struct RuntimeCaptureRead
    {
        public RuntimeCaptureRead(Guid captureId, RuntimeTraceChannel channels, RuntimeDiagnosticsCaptureDetail detail,
            long version, long evictedEvents, bool requiresFullSync, IReadOnlyList<RuntimeCaptureChange> changes)
        {
            CaptureId = captureId;
            Channels = channels;
            Detail = detail;
            Version = version;
            EvictedEvents = evictedEvents;
            RequiresFullSync = requiresFullSync;
            Changes = changes;
        }

        public Guid CaptureId { get; }
        public RuntimeTraceChannel Channels { get; }
        public RuntimeDiagnosticsCaptureDetail Detail { get; }
        public long Version { get; }
        public long EvictedEvents { get; }
        public bool RequiresFullSync { get; }
        public IReadOnlyList<RuntimeCaptureChange> Changes { get; }
    }

    public sealed class RuntimeCaptureSegmentSnapshot
    {
        readonly RuntimeTraceEvent[] m_Events;

        public RuntimeCaptureSegmentSnapshot(RuntimeTraceDomain domain, ulong position, RuntimeTraceEvent[] events)
        {
            Domain = domain;
            Position = position;
            m_Events = events ?? Array.Empty<RuntimeTraceEvent>();
        }

        public RuntimeTraceDomain Domain { get; }
        public ulong Position { get; }
        public ReadOnlySpan<RuntimeTraceEvent> Events => m_Events;
    }

    public readonly struct RuntimeCaptureSnapshot : IDisposable
    {
        readonly RuntimeCaptureMemorySnapshot m_Memory;
        readonly IRuntimeCaptureArchive m_Archive;
        readonly int m_SegmentCount;
        readonly double m_LogicTime;
        readonly double m_PresentationTime;

        public RuntimeCaptureSnapshot(Guid captureId, RuntimeTraceChannel channels, RuntimeDiagnosticsCaptureDetail detail,
            RuntimeCaptureSegmentSnapshot[] segments, long version, long evictedEvents = 0)
        {
            m_Memory = new RuntimeCaptureMemorySnapshot(segments);
            m_Archive = null;
            m_SegmentCount = 0;
            m_LogicTime = m_PresentationTime = 0;
            CaptureId = captureId;
            Channels = channels & RuntimeTraceChannel.All;
            Detail = detail;
            Version = version;
            EvictedEvents = evictedEvents;
        }

        public RuntimeCaptureSnapshot(IRuntimeCaptureArchive archive, Guid captureId, RuntimeTraceChannel channels,
            RuntimeDiagnosticsCaptureDetail detail, long version, int segments, double logicTime, double presentationTime)
        {
            m_Memory = null;
            m_Archive = archive;
            m_SegmentCount = segments;
            m_LogicTime = logicTime;
            m_PresentationTime = presentationTime;
            CaptureId = captureId;
            Channels = channels & RuntimeTraceChannel.All;
            Detail = detail;
            Version = version;
            EvictedEvents = 0;
        }

        public bool IsValid => m_Memory != null || m_Archive != null;
        public Guid CaptureId { get; }
        public RuntimeTraceChannel Channels { get; }
        public RuntimeDiagnosticsCaptureDetail Detail { get; }
        public long Version { get; }
        public long EvictedEvents { get; }
        public double LatestLogicTimeSeconds => m_Archive != null ? m_LogicTime : m_Memory.LatestLogicTimeSeconds;
        public double LatestPresentationTimeSeconds => m_Archive != null ? m_PresentationTime : m_Memory.LatestPresentationTimeSeconds;
        public int SegmentCount => m_Archive != null ? m_SegmentCount : m_Memory.SegmentCount;
        public ReadOnlySpan<RuntimeCaptureSegmentSnapshot> Segments => m_Archive == null
            ? m_Memory.Segments : throw new InvalidOperationException("文件历史必须通过 BeginRead / TryTakeRead 按时间读取。");

        public ReadOnlySpan<RuntimeTraceEvent> GetEvents(int historyOffset, ulong throughSequence = ulong.MaxValue) =>
            m_Archive == null ? m_Memory.GetEvents(historyOffset, throughSequence)
                : throw new InvalidOperationException("文件历史必须通过 BeginRead / TryTakeRead 按时间读取。");

        public int GetHistoryOffset(ulong throughSequence) => m_Archive == null
            ? m_Memory.GetHistoryOffset(throughSequence)
            : throw new InvalidOperationException("历史偏移由完成的读取结果提供。");

        public long BeginRead(in RuntimeCaptureQuery query, long stateVersion) => m_Archive != null
            ? m_Archive.BeginRead(in this, in query, stateVersion) : m_Memory.BeginRead(in query, Version);

        public bool TryTakeRead(long request, ref RuntimeCaptureReadWindow window,
            (double Logic, double Presentation)? latestTime, out RuntimeCaptureReadStatus status) => m_Archive != null
            ? m_Archive.TryTakeRead(request, ref window, latestTime, out status)
            : m_Memory.TryTakeRead(request, ref window, latestTime, out status);

        public void CancelRead()
        {
            if (m_Archive != null) m_Archive.CancelRead();
            else m_Memory.CancelRead();
        }

        // 文件快照只借用档案，释放由原档案 owner 完成。
        public void Dispose() => m_Memory?.Dispose();
    }

    sealed class RuntimeCaptureMemorySnapshot : IDisposable
    {
        RuntimeCaptureReadWindow m_ReadWindow;
        bool m_ReadFound;
        readonly RuntimeCaptureSegmentSnapshot[] m_Segments;
        readonly RuntimeTraceEvent[] m_Events;
        readonly int[] m_SegmentEnds;

        internal RuntimeCaptureMemorySnapshot(RuntimeCaptureSegmentSnapshot[] segments)
        {
            m_Segments = segments ?? Array.Empty<RuntimeCaptureSegmentSnapshot>();
            int eventCount = 0;
            for (int i = 0; i < m_Segments.Length; i++)
                eventCount += m_Segments[i].Events.Length;
            m_Events = new RuntimeTraceEvent[eventCount];
            m_SegmentEnds = new int[m_Segments.Length];
            int eventIndex = 0;
            for (int i = 0; i < m_Segments.Length; i++)
            {
                ReadOnlySpan<RuntimeTraceEvent> segmentEvents = m_Segments[i].Events;
                segmentEvents.CopyTo(m_Events.AsSpan(eventIndex));
                for (int eventOffset = 0; eventOffset < segmentEvents.Length; eventOffset++)
                {
                    ref readonly RuntimeTraceEvent trace = ref segmentEvents[eventOffset];
                    if (trace.Domain == RuntimeTraceDomain.Presentation)
                        LatestPresentationTimeSeconds = trace.TimeSeconds;
                    else
                        LatestLogicTimeSeconds = trace.TimeSeconds;
                }
                eventIndex += segmentEvents.Length;
                m_SegmentEnds[i] = eventIndex;
            }
        }

        internal double LatestLogicTimeSeconds { get; }
        internal double LatestPresentationTimeSeconds { get; }
        internal ReadOnlySpan<RuntimeCaptureSegmentSnapshot> Segments => m_Segments;
        internal int SegmentCount => m_Segments.Length;

        internal long BeginRead(in RuntimeCaptureQuery query, long version)
        {
            m_ReadWindow ??= new RuntimeCaptureReadWindow(4096, 32768);
            m_ReadFound = ReadQuery(in query, m_ReadWindow, version);
            return 1;
        }

        internal bool TryTakeRead(long request, ref RuntimeCaptureReadWindow window,
            (double Logic, double Presentation)? latestTime, out RuntimeCaptureReadStatus status)
        {
            status = m_ReadWindow.GetReadStatus(m_ReadFound, latestTime);
            if (status == RuntimeCaptureReadStatus.Found)
                (window, m_ReadWindow) = (m_ReadWindow, window);
            return true;
        }

        internal void CancelRead() => m_ReadWindow.Clear();

        public void Dispose()
        {
            m_ReadWindow?.Clear();
            Array.Clear(m_Events, 0, m_Events.Length);
        }

        internal ReadOnlySpan<RuntimeTraceEvent> GetEvents(int historyOffset, ulong throughSequence = ulong.MaxValue)
        {
            int visibleSegments = Math.Max(0, m_Segments.Length - Math.Max(0, historyOffset));
            int end = visibleSegments == 0 ? 0 : m_SegmentEnds[visibleSegments - 1];
            int begin = 0;
            while (begin < end)
            {
                int middle = begin + (end - begin) / 2;
                if (m_Events[middle].Sequence <= throughSequence)
                    begin = middle + 1;
                else
                    end = middle;
            }
            return m_Events.AsSpan(0, begin);
        }

        internal int GetHistoryOffset(ulong throughSequence)
        {
            int eventCount = GetEvents(0, throughSequence).Length;
            int begin = 0;
            int end = m_SegmentEnds.Length;
            while (begin < end)
            {
                int middle = begin + (end - begin) / 2;
                if (m_SegmentEnds[middle] < eventCount)
                    begin = middle + 1;
                else
                    end = middle;
            }
            return Math.Max(0, m_Segments.Length - begin - 1);
        }

        bool ReadQuery(in RuntimeCaptureQuery query, RuntimeCaptureReadWindow result, long version)
        {
            ReadOnlySpan<RuntimeTraceEvent> events = GetEvents(
                query.Kind == RuntimeCaptureQueryKind.Segment ? query.HistoryOffset : 0,
                query.Kind == RuntimeCaptureQueryKind.Event ? query.Sequence : ulong.MaxValue);
            int selected = events.Length - 1;
            if (query.Kind == RuntimeCaptureQueryKind.Time)
            {
                selected = -1;
                for (int i = 0; i < events.Length; i++)
                {
                    ref readonly RuntimeTraceEvent trace = ref events[i];
                    RuntimeTraceDomain domain = trace.Domain == RuntimeTraceDomain.Lifecycle ? RuntimeTraceDomain.Logic : trace.Domain;
                    if (domain == query.Domain && trace.TimeSeconds <= query.TimeSeconds &&
                        (!query.Branch.HasValue || trace.ExecutionBranchId == query.Branch.Value) &&
                        (!query.Epoch.HasValue || trace.RuntimeEpoch == query.Epoch.Value))
                        selected = i;
                }
            }
            if (selected < 0 || query.Kind == RuntimeCaptureQueryKind.Event && events[selected].Sequence != query.Sequence)
                return false;
            result.Clear();
            result.Selected = events[selected];
            if (query.IncludeExecutionWindow)
                result.TimelineVersion = version;
            result.HistoryOffset = GetHistoryOffset(result.Selected.Sequence);
            result.NextTimeSeconds = double.PositiveInfinity;
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly RuntimeTraceEvent trace = ref events[i];
                if (i <= selected)
                    result.ApplyState(in trace);
                RuntimeTraceDomain domain = trace.Domain == RuntimeTraceDomain.Lifecycle ? RuntimeTraceDomain.Logic : trace.Domain;
                if (domain == RuntimeTraceDomain.Presentation)
                    result.LatestPresentationTime = trace.TimeSeconds;
                else
                    result.LatestLogicTime = trace.TimeSeconds;
                if (i > selected && domain == query.Domain &&
                    (!query.Branch.HasValue || query.Branch.Value == trace.ExecutionBranchId) &&
                    (!query.Epoch.HasValue || query.Epoch.Value == trace.RuntimeEpoch))
                    result.NextTimeSeconds = Math.Min(result.NextTimeSeconds, trace.TimeSeconds);
                if (query.IncludeExecutionWindow)
                    result.TimelineSpans.Append(in trace);
            }
            result.CopyStateSince(0, result, 0);
            return true;
        }

    }

    sealed class RuntimeLiveStateStore
    {
        readonly Dictionary<RuntimeLiveStateKey, LinkedListNode<int>> m_Current;
        readonly RuntimeTraceEvent[] m_States;
        readonly RuntimeLiveStateChange[] m_Changes;
        readonly LinkedList<int> m_Recency = new();
        readonly LinkedListNode<int>[] m_RecencyNodePool;
        readonly int m_MaxChanges;
        long m_Version;
        long m_LastResetVersion;
        long m_EvictedStates;
        int m_RecencyNodePoolCount;
        int m_ChangeHead;
        int m_ChangeCount;

        public RuntimeLiveStateStore(int maxChanges = 4096)
        {
            if (maxChanges < 64)
                throw new ArgumentOutOfRangeException(nameof(maxChanges));
            m_MaxChanges = maxChanges;
            m_Current = new Dictionary<RuntimeLiveStateKey, LinkedListNode<int>>(maxChanges);
            m_States = new RuntimeTraceEvent[maxChanges];
            m_Changes = new RuntimeLiveStateChange[maxChanges];
            m_RecencyNodePool = new LinkedListNode<int>[m_MaxChanges];
            for (int i = 0; i < m_RecencyNodePool.Length; i++)
                m_RecencyNodePool[i] = new LinkedListNode<int>(i);
            m_RecencyNodePoolCount = m_RecencyNodePool.Length;
        }

        public long Version => m_Version;
        public int Capacity => m_MaxChanges;

        public bool Upsert(in RuntimeTraceEvent traceEvent)
        {
            return Write(in traceEvent, true);
        }

        bool Write(in RuntimeTraceEvent traceEvent, bool recordChange)
        {
            var key = new RuntimeLiveStateKey(in traceEvent);
            if (m_Current.TryGetValue(key, out LinkedListNode<int> recent))
            {
                if (recent.Next != null)
                {
                    m_Recency.Remove(recent);
                    m_Recency.AddLast(recent);
                }
                if (StateEquivalent(in m_States[recent.Value], in traceEvent))
                    return false;
            }
            else
            {
                if (m_Current.Count == m_MaxChanges)
                {
                    recent = m_Recency.First;
                    var oldest = new RuntimeLiveStateKey(in m_States[recent.Value]);
                    AppendChange(oldest, default, true);
                    m_Recency.RemoveFirst();
                    m_Current.Remove(oldest);
                    m_EvictedStates++;
                }
                else
                    recent = m_RecencyNodePool[--m_RecencyNodePoolCount];
                m_Recency.AddLast(recent);
                m_Current.Add(key, recent);
            }

            m_States[recent.Value] = traceEvent;
            if (recordChange)
                AppendChange(key, in traceEvent, false);
            return true;
        }

        void AppendChange(RuntimeLiveStateKey key, in RuntimeTraceEvent traceEvent, bool isRemoval)
        {
            m_Version++;
            int nextChange = (m_ChangeHead + m_ChangeCount) % m_MaxChanges;
            var change = new RuntimeLiveStateChange(m_Version, key, in traceEvent, isRemoval);
            m_Changes[nextChange] = change;
            if (m_ChangeCount == m_MaxChanges)
                m_ChangeHead = (m_ChangeHead + 1) % m_MaxChanges;
            else
                m_ChangeCount++;
        }

        public RuntimeLiveStateRead CopySince(long cursor, List<RuntimeLiveStateChange> destination)
        {
            destination.Clear();
            if (cursor >= m_Version)
                return new RuntimeLiveStateRead(m_Version, false, destination, m_EvictedStates);
            long earliestAvailable = m_Version - m_ChangeCount + 1;
            if (cursor < earliestAvailable - 1 || cursor < m_LastResetVersion)
            {
                foreach (KeyValuePair<RuntimeLiveStateKey, LinkedListNode<int>> pair in m_Current)
                {
                    var change = new RuntimeLiveStateChange(m_Version, pair.Key, in m_States[pair.Value.Value], false);
                    destination.Add(change);
                }
                return new RuntimeLiveStateRead(m_Version, true, destination, m_EvictedStates);
            }
            int first = m_ChangeCount - (int)(m_Version - cursor);
            for (int i = first; i < m_ChangeCount; i++)
            {
                RuntimeLiveStateChange change = m_Changes[(m_ChangeHead + i) % m_MaxChanges];
                destination.Add(change);
            }
            return new RuntimeLiveStateRead(m_Version, false, destination, m_EvictedStates);
        }

        public void RemoveChannels(RuntimeTraceChannel channels)
        {
            bool changed = false;
            LinkedListNode<int> node = m_Recency.First;
            while (node != null)
            {
                LinkedListNode<int> next = node.Next;
                if ((m_States[node.Value].Channel & channels) != 0)
                {
                    var key = new RuntimeLiveStateKey(in m_States[node.Value]);
                    m_Current.Remove(key);
                    m_States[node.Value] = default;
                    m_Recency.Remove(node);
                    ReturnRecencyNode(node);
                    changed = true;
                }
                node = next;
            }
            if (changed)
            {
                ClearChanges();
                m_Version++;
                m_LastResetVersion = m_Version;
            }
        }

        public void Clear()
        {
            m_Current.Clear();
            Array.Clear(m_States, 0, m_States.Length);
            ClearChanges();
            while (m_Recency.Count > 0)
            {
                LinkedListNode<int> node = m_Recency.First;
                m_Recency.RemoveFirst();
                ReturnRecencyNode(node);
            }
            m_EvictedStates = 0;
            m_Version++;
            m_LastResetVersion = m_Version;
        }

        internal void Restore(ReadOnlySpan<RuntimeTraceEvent> states)
        {
            Clear();
            for (int i = 0; i < states.Length; i++)
                Write(in states[i], false);
        }

        internal long CopyCurrent(List<RuntimeTraceEvent> destination)
        {
            destination.Clear();
            for (LinkedListNode<int> node = m_Recency.First; node != null; node = node.Next)
            {
                destination.Add(m_States[node.Value]);
            }
            return m_EvictedStates;
        }

        void ClearChanges()
        {
            Array.Clear(m_Changes, 0, m_Changes.Length);
            m_ChangeHead = 0;
            m_ChangeCount = 0;
        }

        void ReturnRecencyNode(LinkedListNode<int> node)
        {
            m_RecencyNodePool[m_RecencyNodePoolCount++] = node;
        }

        static bool StateEquivalent(in RuntimeTraceEvent left, in RuntimeTraceEvent right)
        {
            if ((left.Kind is RuntimeTraceEventKind.ValueSampled or RuntimeTraceEventKind.ValueSamplingLimited or RuntimeTraceEventKind.TraceSamplingLimited or
                RuntimeTraceEventKind.NodeRunning or RuntimeTraceEventKind.NodeWaiting or
                RuntimeTraceEventKind.EdgeSelected or RuntimeTraceEventKind.EdgeEvaluated or
                RuntimeTraceEventKind.StateTransitionSelected or RuntimeTraceEventKind.StateTransitionEvaluated) && left.Position != right.Position)
                return false;
            ref readonly RuntimeTracePayload leftPayload = ref RuntimeTraceEvent.ReadPayload(in left);
            ref readonly RuntimeTracePayload rightPayload = ref RuntimeTraceEvent.ReadPayload(in right);
            return left.Domain == right.Domain &&
                   left.ExecutionBranchId == right.ExecutionBranchId &&
                   PayloadEquivalent(in leftPayload, in rightPayload);
        }

        static bool PayloadEquivalent(in RuntimeTracePayload left, in RuntimeTracePayload right)
        {
            return string.Equals(left.Status, right.Status, StringComparison.Ordinal) &&
                   string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
                   string.Equals(left.Detail, right.Detail, StringComparison.Ordinal) &&
                   string.Equals(left.Cause, right.Cause, StringComparison.Ordinal) &&
                   string.Equals(left.SourceClockId, right.SourceClockId, StringComparison.Ordinal) &&
                   string.Equals(left.SourceTickKind, right.SourceTickKind, StringComparison.Ordinal) &&
                   string.Equals(left.AnimationChannelId, right.AnimationChannelId, StringComparison.Ordinal) &&
                   string.Equals(left.OwnerId, right.OwnerId, StringComparison.Ordinal) &&
                   string.Equals(left.RelatedElementId, right.RelatedElementId, StringComparison.Ordinal) &&
                   string.Equals(left.SkillId, right.SkillId, StringComparison.Ordinal) &&
                   left.InputSequence == right.InputSequence &&
                   string.Equals(left.CharacterStateHash, right.CharacterStateHash, StringComparison.Ordinal) &&
                   string.Equals(left.WorldHash, right.WorldHash, StringComparison.Ordinal) &&
                   left.ActionInstanceId == right.ActionInstanceId &&
                   string.Equals(left.CallSiteId, right.CallSiteId, StringComparison.Ordinal) &&
                   left.ActivationGeneration == right.ActivationGeneration &&
                   left.SkillExecutionGeneration == right.SkillExecutionGeneration &&
                   string.Equals(left.RuleSourceSkillId, right.RuleSourceSkillId, StringComparison.Ordinal) &&
                   left.RuleSourceInstanceId == right.RuleSourceInstanceId &&
                   left.RuleSourceSkillExecutionGeneration == right.RuleSourceSkillExecutionGeneration &&
                   left.RuleSourceSegmentGeneration == right.RuleSourceSegmentGeneration &&
                   left.GraphInvocationGeneration == right.GraphInvocationGeneration &&
                   left.ParentInvocationGeneration == right.ParentInvocationGeneration &&
                   string.Equals(left.ParentInvocationPath, right.ParentInvocationPath, StringComparison.Ordinal) &&
                   left.StartTick == right.StartTick &&
                   left.EndTick == right.EndTick &&
                   left.Revision == right.Revision &&
                   left.ActionPhase == right.ActionPhase &&
                   left.ActionState == right.ActionState &&
                   left.ActionResult == right.ActionResult &&
                   left.LifecycleOperation == right.LifecycleOperation &&
                   left.StackCount == right.StackCount &&
                   left.DetailKind == right.DetailKind &&
                   left.BeforeCount == right.BeforeCount &&
                   left.Time.Equals(right.Time) &&
                   left.SecondaryTime.Equals(right.SecondaryTime) &&
                   left.NormalizedTime.Equals(right.NormalizedTime) &&
                   left.Weight.Equals(right.Weight) &&
                   left.FinalWeight.Equals(right.FinalWeight) &&
                   left.Priority == right.Priority &&
                   left.Cycle == right.Cycle &&
                   left.LoopIteration == right.LoopIteration &&
                   left.BlackboardStateSlot == right.BlackboardStateSlot &&
                   left.BlackboardScope == right.BlackboardScope &&
                   left.BlackboardOwnerIndex == right.BlackboardOwnerIndex &&
                   left.BlackboardOwnerGeneration == right.BlackboardOwnerGeneration &&
                   left.BlackboardLifetime == right.BlackboardLifetime &&
                   left.BlackboardIsActive == right.BlackboardIsActive &&
                   left.TrackIndex == right.TrackIndex &&
                   left.ClipIndex == right.ClipIndex &&
                   left.Flag == right.Flag &&
                   DebugValueEquivalent(in left.Value, in right.Value) &&
                   left.MotionDetail.Equals(in right.MotionDetail) &&
                   string.Equals(left.MotionIdentityTableId, right.MotionIdentityTableId, StringComparison.Ordinal) &&
                   left.SourceEventId.Equals(right.SourceEventId) &&
                   TimelineProvenanceEquivalent(in left.TimelinePlayback, in right.TimelinePlayback) &&
                   FootPlacementEquivalent(in left.FootPlacement, in right.FootPlacement);
        }

        static bool FootPlacementEquivalent(
            in RuntimeFootPlacementTraceSnapshot left,
            in RuntimeFootPlacementTraceSnapshot right)
        {
            return left.IsAvailable == right.IsAvailable &&
                   left.FrameSequence == right.FrameSequence &&
                   left.CompletionIdentity == right.CompletionIdentity &&
                   FootPlacementFootEquivalent(in left.Left, in right.Left) &&
                   FootPlacementFootEquivalent(in left.Right, in right.Right);
        }

        static bool FootPlacementFootEquivalent(
            in RuntimeFootPlacementFootTraceSnapshot left,
            in RuntimeFootPlacementFootTraceSnapshot right)
        {
            return left.IsAvailable == right.IsAvailable &&
                   string.Equals(left.Side, right.Side, StringComparison.Ordinal) &&
                   string.Equals(left.State, right.State, StringComparison.Ordinal) &&
                   string.Equals(left.RejectReason, right.RejectReason, StringComparison.Ordinal) &&
                   string.Equals(left.StepSource, right.StepSource, StringComparison.Ordinal) &&
                   left.LandingEventIdentity == right.LandingEventIdentity &&
                   left.TrajectoryGeneration == right.TrajectoryGeneration &&
                   left.LandingConfidence.Equals(right.LandingConfidence) &&
                   left.TimeToLandingSeconds.Equals(right.TimeToLandingSeconds) &&
                   left.RootLocalLanding.Equals(right.RootLocalLanding) &&
                   left.CurrentAnimatedSole.Equals(right.CurrentAnimatedSole) &&
                   left.RawLanding.Equals(right.RawLanding) &&
                   left.QueryAvailable == right.QueryAvailable &&
                   string.Equals(left.QueryShape, right.QueryShape, StringComparison.Ordinal) &&
                   string.Equals(left.QueryPurpose, right.QueryPurpose, StringComparison.Ordinal) &&
                   left.QueryOrigin.Equals(right.QueryOrigin) &&
                   left.QueryDirection.Equals(right.QueryDirection) &&
                   left.QueryRadius.Equals(right.QueryRadius) &&
                   left.QueryMaximumDistance.Equals(right.QueryMaximumDistance) &&
                   left.QueryLayerMask == right.QueryLayerMask &&
                   left.QueryMinimumGroundNormalDot.Equals(right.QueryMinimumGroundNormalDot) &&
                   left.Accepted == right.Accepted &&
                   left.SurfaceIdentity == right.SurfaceIdentity &&
                   left.LandingPoint.Equals(right.LandingPoint) &&
                   left.LandingNormal.Equals(right.LandingNormal) &&
                   left.QueryDistance.Equals(right.QueryDistance);
        }

        static bool DebugValueEquivalent(in DebugValueSnapshot left, in DebugValueSnapshot right)
        {
            return left.Kind == right.Kind &&
                   left.Boolean == right.Boolean &&
                   left.Signed == right.Signed &&
                   left.Unsigned == right.Unsigned &&
                   left.Number.Equals(right.Number) &&
                   string.Equals(left.Text, right.Text, StringComparison.Ordinal) &&
                   left.Vector.Equals(right.Vector);
        }

        static bool TimelineProvenanceEquivalent(in RuntimeTimelinePlaybackProvenance left, in RuntimeTimelinePlaybackProvenance right)
        {
            return string.Equals(left.SourceGraphAuthoringId, right.SourceGraphAuthoringId, StringComparison.Ordinal) &&
                   string.Equals(left.SourceNodeAuthoringId, right.SourceNodeAuthoringId, StringComparison.Ordinal) &&
                   left.SourceGraphRuntimeId.Equals(right.SourceGraphRuntimeId) &&
                   left.SourceActivationGeneration == right.SourceActivationGeneration &&
                   string.Equals(left.StateMachineGraphAuthoringId, right.StateMachineGraphAuthoringId, StringComparison.Ordinal) &&
                   string.Equals(left.StateId, right.StateId, StringComparison.Ordinal) &&
                   left.StateMachineGraphRuntimeId.Equals(right.StateMachineGraphRuntimeId) &&
                   left.StateActivationGeneration == right.StateActivationGeneration &&
                   string.Equals(left.SourceName, right.SourceName, StringComparison.Ordinal) &&
                   string.Equals(left.SourceInvocationPath, right.SourceInvocationPath, StringComparison.Ordinal) &&
                   left.SourceOperationIndex == right.SourceOperationIndex &&
                   left.SkillExecutionGeneration == right.SkillExecutionGeneration;
        }
    }

    sealed class RuntimeCaptureStore
    {
        internal const int DefaultMaxEvents = 32768;
        readonly Segment[] m_Segments;
        readonly RuntimeCaptureChange[] m_Changes;
        readonly int m_MaxSegments;
        readonly int m_MaxEvents;
        Guid m_CaptureId;
        RuntimeDiagnosticsCaptureDetail m_Detail;
        long m_Version;
        long m_EvictedEvents;
        long m_LastEvictionVersion;
        int m_SegmentHead;
        int m_SegmentCount;
        int m_ChangeHead;
        int m_ChangeCount;

        public RuntimeCaptureStore(int maxSegments = 512, int maxEvents = DefaultMaxEvents)
        {
            if (maxSegments < 8)
                throw new ArgumentOutOfRangeException(nameof(maxSegments));
            if (maxEvents < 64)
                throw new ArgumentOutOfRangeException(nameof(maxEvents));
            m_MaxSegments = maxSegments;
            m_MaxEvents = maxEvents;
            m_Segments = new Segment[maxSegments + 1];
            m_Changes = new RuntimeCaptureChange[maxEvents + 1];
        }

        public Guid CaptureId => m_CaptureId;
        public Guid RecordingId { get; private set; }
        public long Version => m_Version;
        public int SegmentCount => m_SegmentCount;
        public int MaxSegments => m_MaxSegments;

        public void Begin(Guid captureId, RuntimeDiagnosticsCaptureDetail detail, long initialVersion)
        {
            m_CaptureId = captureId;
            RecordingId = Guid.NewGuid();
            m_Detail = detail;
            m_Version = initialVersion;
        }

        public void UpgradeDetail(RuntimeDiagnosticsCaptureDetail detail)
        {
            if (detail > m_Detail)
                m_Detail = detail;
        }

        public void Publish(in RuntimeTraceEvent traceEvent)
        {
            int last = (m_SegmentHead + m_SegmentCount - 1) % m_Segments.Length;
            if (m_SegmentCount == 0 || m_Segments[last].Domain != traceEvent.Domain ||
                m_Segments[last].Position != traceEvent.Position)
            {
                last = (m_SegmentHead + m_SegmentCount++) % m_Segments.Length;
                m_Segments[last] = new Segment { Domain = traceEvent.Domain, Position = traceEvent.Position };
            }
            m_Version++;
            ref Segment segment = ref m_Segments[last];
            if (segment.EventCount == m_MaxEvents)
            {
                m_EvictedEvents++;
                m_LastEvictionVersion = m_Version;
                return;
            }
            segment.EventCount++;
            segment.LastVersion = m_Version;
            m_Changes[(m_ChangeHead + m_ChangeCount++) % m_Changes.Length] = new RuntimeCaptureChange(m_Version, in traceEvent);
            while (m_SegmentCount > m_MaxSegments || m_ChangeCount > m_MaxEvents)
            {
                ref Segment removed = ref m_Segments[m_SegmentHead];
                m_EvictedEvents += removed.EventCount;
                m_LastEvictionVersion = Math.Max(m_LastEvictionVersion, removed.LastVersion);
                for (int i = 0; i < removed.EventCount; i++)
                {
                    m_Changes[m_ChangeHead] = default;
                    m_ChangeHead = (m_ChangeHead + 1) % m_Changes.Length;
                }
                m_ChangeCount -= removed.EventCount;
                removed = default;
                m_SegmentHead = (m_SegmentHead + 1) % m_Segments.Length;
                m_SegmentCount--;
            }
        }

        public RuntimeCaptureRead CopySince(Guid captureId, long cursor, RuntimeTraceChannel channels, List<RuntimeCaptureChange> destination)
        {
            destination.Clear();
            bool changed = captureId != m_CaptureId;
            if (!changed && cursor >= m_Version)
                return new RuntimeCaptureRead(m_CaptureId, channels, m_Detail, m_Version, m_EvictedEvents, false, destination);
            long earliest = m_ChangeCount > 0 ? Change(0).Revision : m_Version + 1;
            bool fullSync = changed || cursor < earliest - 1 || cursor < m_LastEvictionVersion;
            int first = 0;
            if (!fullSync)
            {
                int end = m_ChangeCount;
                while (first < end)
                {
                    int middle = first + (end - first) / 2;
                    if (Change(middle).Revision <= cursor) first = middle + 1; else end = middle;
                }
            }
            for (int i = first; i < m_ChangeCount; i++)
            {
                destination.Add(Change(i));
            }
            return new RuntimeCaptureRead(m_CaptureId, channels, m_Detail, m_Version, m_EvictedEvents, fullSync, destination);
        }

        public RuntimeCaptureSnapshot Freeze(RuntimeTraceChannel channels)
        {
            var segments = new RuntimeCaptureSegmentSnapshot[m_SegmentCount];
            int first = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                ref Segment segment = ref m_Segments[(m_SegmentHead + i) % m_Segments.Length];
                var events = new RuntimeTraceEvent[segment.EventCount];
                for (int j = 0; j < events.Length; j++)
                    events[j] = Change(first++).TraceEvent;
                segments[i] = new RuntimeCaptureSegmentSnapshot(segment.Domain, segment.Position, events);
            }
            return new RuntimeCaptureSnapshot(m_CaptureId, channels, m_Detail, segments, m_Version, m_EvictedEvents);
        }

        ref readonly RuntimeCaptureChange Change(int index) => ref m_Changes[(m_ChangeHead + index) % m_Changes.Length];

        public void Clear()
        {
            Array.Clear(m_Segments, 0, m_Segments.Length);
            Array.Clear(m_Changes, 0, m_Changes.Length);
            m_SegmentHead = m_SegmentCount = m_ChangeHead = m_ChangeCount = 0;
            m_CaptureId = Guid.Empty;
            RecordingId = Guid.Empty;
            m_Detail = RuntimeDiagnosticsCaptureDetail.None;
            m_Version = m_EvictedEvents = m_LastEvictionVersion = 0;
        }

        struct Segment
        {
            internal RuntimeTraceDomain Domain;
            internal ulong Position;
            internal int EventCount;
            internal long LastVersion;
        }
    }

    public sealed class RuntimeDiagnosticsStore : IDisposable
    {
        readonly object m_Gate = new object();
        readonly Dictionary<Guid, RuntimeDiagnosticsInterest> m_Interests = new Dictionary<Guid, RuntimeDiagnosticsInterest>();
        readonly RuntimeLiveStateStore m_LiveState = new RuntimeLiveStateStore();
        RuntimeCaptureStore m_Capture;
        IRuntimeCaptureArchive m_Archive;
        RuntimeTraceChannel m_LiveChannels;
        volatile int m_LiveChannelBits;
        long m_LiveStateGeneration;
        RuntimeTraceChannel m_CaptureChannels;
        volatile int m_CaptureChannelBits;
        volatile RuntimeDiagnosticsCaptureDetail m_CaptureDetail;
        bool m_Terminated;
        bool m_Disposed;

        public event Action CaptureStarted;

        public long LiveStateGeneration
        {
            get
            {
                lock (m_Gate)
                    return m_LiveStateGeneration;
            }
        }

        public RuntimeTraceChannel EffectiveChannels
        {
            get => (RuntimeTraceChannel)(m_LiveChannelBits | m_CaptureChannelBits);
        }
        public RuntimeTraceChannel RecordingChannels => (RuntimeTraceChannel)m_CaptureChannelBits;

        public bool IsCaptureRecording
        {
            get
            {
                lock (m_Gate)
                    return m_CaptureChannels != RuntimeTraceChannel.None;
            }
        }

        public int CaptureSegmentCount
        {
            get
            {
                lock (m_Gate)
                    return m_Archive?.SegmentCount ?? m_Capture?.SegmentCount ?? 0;
            }
        }

        public int CaptureSegmentCapacity
        {
            get
            {
                lock (m_Gate)
                    return m_CaptureChannels == RuntimeTraceChannel.None ? 0 : m_Capture.MaxSegments;
            }
        }

        public int PrepareCaptureStorage()
        {
            lock (m_Gate)
                return PrepareCaptureStorageCore().MaxSegments;
        }

        RuntimeCaptureStore PrepareCaptureStorageCore() => m_Capture ??= new RuntimeCaptureStore();

        public RuntimeDiagnosticsInterestHandle AcquireInterest(RuntimeDiagnosticsInterest interest)
        {
            if (!interest.IsValid)
                throw new ArgumentException("A diagnostics interest requires channels and a valid capture detail.", nameof(interest));

            lock (m_Gate)
            {
                if (m_Disposed || m_Terminated)
                    return default;
                Guid value = Guid.NewGuid();
                m_Interests.Add(value, interest);
                RecalculateLiveChannels();
                return new RuntimeDiagnosticsInterestHandle(value);
            }
        }

        public bool ReleaseInterest(RuntimeDiagnosticsInterestHandle handle)
        {
            if (!handle.IsValid)
                return false;

            lock (m_Gate)
            {
                if (!m_Interests.Remove(handle.Value))
                    return false;
                RecalculateLiveChannels();
                return true;
            }
        }

        public bool IsInterested(RuntimeTraceChannel channel, RuntimeTraceEventKind kind)
        {
            return ShouldPublish(channel, kind);
        }

        public bool IsLiveInterested(RuntimeTraceChannel channel) =>
            (m_LiveChannelBits & (int)channel) != 0;

        public bool BeginCapture(RuntimeTraceChannel channels, RuntimeDiagnosticsCaptureDetail detail, out Guid captureId)
        {
            channels &= RuntimeTraceChannel.All;
            if (channels == RuntimeTraceChannel.None || detail == RuntimeDiagnosticsCaptureDetail.None)
            {
                captureId = Guid.Empty;
                return false;
            }

            lock (m_Gate)
            {
                if (m_Disposed || m_Terminated)
                {
                    captureId = Guid.Empty;
                    return false;
                }

                if (m_CaptureChannels == RuntimeTraceChannel.None)
                {
                    captureId = m_Archive?.CaptureId ?? Guid.NewGuid();
                    PrepareCaptureStorageCore().Begin(captureId, detail, m_Archive?.Version ?? 0);
                    m_CaptureChannels = channels;
                    m_CaptureDetail = detail;
                    m_CaptureChannelBits = (int)channels;
                    CaptureStarted?.Invoke();
                    return true;
                }

                captureId = m_Capture.CaptureId;
                m_CaptureChannels |= channels;
                m_CaptureChannelBits = (int)m_CaptureChannels;
                if (detail > m_CaptureDetail)
                {
                    m_CaptureDetail = detail;
                    m_Capture.UpgradeDetail(detail);
                }
                return true;
            }
        }

        public void DiscardCapture()
        {
            lock (m_Gate)
            {
                if (m_CaptureChannels != RuntimeTraceChannel.None)
                    m_Capture.Clear();
                m_CaptureChannels = RuntimeTraceChannel.None;
                m_CaptureChannelBits = 0;
                m_CaptureDetail = RuntimeDiagnosticsCaptureDetail.None;
                m_Archive = null;
            }
        }

        public RuntimeCaptureSnapshot EndCapture()
        {
            lock (m_Gate)
            {
                if (m_CaptureChannels == RuntimeTraceChannel.None)
                    return default;

                RuntimeCaptureSnapshot snapshot = m_Archive?.Freeze(m_CaptureChannels, m_CaptureDetail) ?? m_Capture.Freeze(m_CaptureChannels);
                m_Capture.Clear();
                m_CaptureChannels = RuntimeTraceChannel.None;
                m_CaptureChannelBits = 0;
                m_CaptureDetail = RuntimeDiagnosticsCaptureDetail.None;
                return snapshot;
            }
        }

        public RuntimeCaptureSnapshot FreezeActiveCapture()
        {
            lock (m_Gate)
                return m_CaptureChannels == RuntimeTraceChannel.None ? default : m_Archive?.Freeze(m_CaptureChannels, m_CaptureDetail) ?? m_Capture.Freeze(m_CaptureChannels);
        }

        public void SetCaptureArchive(IRuntimeCaptureArchive archive)
        {
            lock (m_Gate)
            {
                if (m_CaptureChannels != RuntimeTraceChannel.None)
                    throw new InvalidOperationException("录制开始前必须装配历史存储。");
                m_Archive = archive;
            }
        }

        public (Guid CaptureId, long Version) CaptureStatus
        {
            get
            {
                lock (m_Gate)
                    return m_CaptureChannels == RuntimeTraceChannel.None ? (Guid.Empty, 0L) : (m_Capture.CaptureId, m_Archive?.Version ?? m_Capture.Version);
            }
        }

        public Guid CaptureRecordingId
        {
            get
            {
                lock (m_Gate)
                    return m_Capture.RecordingId;
            }
        }

        public int LiveStateCapacity => m_LiveState.Capacity;
        public bool HasCaptureArchive => m_Archive != null;
        public int CaptureEventCapacity => RuntimeCaptureStore.DefaultMaxEvents;

        public RuntimeLiveStateRead CopyLiveStateSince(long cursor, List<RuntimeLiveStateChange> destination)
        {
            lock (m_Gate)
                return m_LiveState.CopySince(cursor, destination);
        }

        public RuntimeCaptureRead CopyCaptureSince(Guid captureId, long cursor, List<RuntimeCaptureChange> destination)
        {
            lock (m_Gate)
            {
                if (m_CaptureChannels != RuntimeTraceChannel.None)
                    return m_Capture.CopySince(captureId, cursor, m_CaptureChannels, destination);
                destination.Clear();
                return new RuntimeCaptureRead(Guid.Empty, RuntimeTraceChannel.None, RuntimeDiagnosticsCaptureDetail.None, 0, 0, captureId != Guid.Empty, destination);
            }
        }

        public bool ShouldPublish(RuntimeTraceChannel channel, RuntimeTraceEventKind kind)
        {
            return IsLiveInterested(channel) || ShouldCapture(channel, kind);
        }

        public bool ShouldCapture(RuntimeTraceChannel channel, RuntimeTraceEventKind kind)
        {
            return (m_CaptureChannelBits & (int)channel) != 0 &&
                   m_CaptureDetail >= RequiredCaptureDetail(kind);
        }

        public bool Publish(in RuntimeTraceEvent traceEvent)
        {
            lock (m_Gate)
            {
                if (m_Disposed || m_Terminated)
                    return false;

                bool live = (m_LiveChannels & traceEvent.Channel) != 0;
                bool capture = (m_CaptureChannels & traceEvent.Channel) != 0 &&
                               m_CaptureDetail >= RequiredCaptureDetail(traceEvent.Kind);
                if (!live && !capture)
                    return false;
                if (live)
                    m_LiveState.Upsert(in traceEvent);
                if (capture)
                {
                    m_Archive?.Publish(in traceEvent);
                    m_Capture.Publish(in traceEvent);
                }
                return true;
            }
        }

        public void Terminate()
        {
            lock (m_Gate)
            {
                if (m_Disposed || m_Terminated)
                    return;
                m_Interests.Clear();
                m_LiveChannels = RuntimeTraceChannel.None;
                m_LiveChannelBits = 0;
                m_CaptureChannelBits = 0;
                m_Terminated = true;
            }
        }

        public void ClearLiveState()
        {
            lock (m_Gate)
            {
                if (m_Disposed)
                    return;
                m_LiveState.Clear();
                m_LiveStateGeneration++;
            }
        }

        public void Dispose()
        {
            lock (m_Gate)
            {
                if (m_Disposed)
                    return;
                m_LiveChannelBits = 0;
                m_CaptureChannelBits = 0;
                m_Disposed = true;
                m_Terminated = true;
                m_Interests.Clear();
                m_LiveChannels = RuntimeTraceChannel.None;
                m_CaptureChannels = RuntimeTraceChannel.None;
                m_CaptureDetail = RuntimeDiagnosticsCaptureDetail.None;
                m_Capture?.Clear();
                m_Capture = null;
                m_LiveState.Clear();
            }
        }

        static RuntimeDiagnosticsCaptureDetail RequiredCaptureDetail(RuntimeTraceEventKind kind)
        {
            return kind switch
            {
                RuntimeTraceEventKind.EdgeEvaluated or
                RuntimeTraceEventKind.ConditionGraphEvaluated or
                RuntimeTraceEventKind.StateTransitionEvaluated => RuntimeDiagnosticsCaptureDetail.Evaluation,
                RuntimeTraceEventKind.NodeStatus or
                RuntimeTraceEventKind.NodeRunning or
                RuntimeTraceEventKind.NodeWaiting or
                RuntimeTraceEventKind.ValueSampled or
                RuntimeTraceEventKind.ValueSamplingLimited or
                RuntimeTraceEventKind.TimelineLogicTime or
                RuntimeTraceEventKind.TimelineVisualTime or
                RuntimeTraceEventKind.TrackActive or
                RuntimeTraceEventKind.ClipActive or
                RuntimeTraceEventKind.TreeClipUpdated or
                RuntimeTraceEventKind.MotionContribution or
                RuntimeTraceEventKind.MotionResolved or
                RuntimeTraceEventKind.ActionSnapshot or
                RuntimeTraceEventKind.AnimationProducerSampled or
                RuntimeTraceEventKind.AnimationPlaybackPending or
                RuntimeTraceEventKind.AnimationPlaybackSelected or
                RuntimeTraceEventKind.AnimationPlaybackRetained or
                RuntimeTraceEventKind.AnimationPlaybackRetired or
                RuntimeTraceEventKind.AnimationPhaseSync or
                RuntimeTraceEventKind.MotionMatchingQuery or
                RuntimeTraceEventKind.MotionMatchingTrajectory or
                RuntimeTraceEventKind.MotionMatchingPoseHistory or
                RuntimeTraceEventKind.MotionMatchingAdmission or
                RuntimeTraceEventKind.MotionMatchingCandidateRejected or
                RuntimeTraceEventKind.MotionMatchingSearchTraversal or
                RuntimeTraceEventKind.MotionMatchingTopK or
                RuntimeTraceEventKind.MotionMatchingPlan or
                RuntimeTraceEventKind.MotionMatchingSelection or
                RuntimeTraceEventKind.MotionMatchingPoseSource or
                RuntimeTraceEventKind.MotionMatchingFrame or
                RuntimeTraceEventKind.PresentationInterpolated or
                RuntimeTraceEventKind.LocomotionPresentation or
                RuntimeTraceEventKind.FootPlacementSnapshot or
                RuntimeTraceEventKind.CameraSnapshot or
                RuntimeTraceEventKind.CameraRequest => RuntimeDiagnosticsCaptureDetail.Continuous,
                _ => RuntimeDiagnosticsCaptureDetail.Boundary
            };
        }

        void RecalculateLiveChannels()
        {
            RuntimeTraceChannel channels = RuntimeTraceChannel.None;
            foreach (RuntimeDiagnosticsInterest interest in m_Interests.Values)
            {
                if (interest.Kind == RuntimeDiagnosticsInterestKind.LiveState)
                    channels |= interest.Channels;
            }
            RuntimeTraceChannel added = channels & ~m_LiveChannels;
            if (added != RuntimeTraceChannel.None)
            {
                m_LiveStateGeneration++;
                if (m_LiveChannels == RuntimeTraceChannel.None)
                    m_LiveState.Clear();
                else
                    m_LiveState.RemoveChannels(RuntimeTraceChannel.All & ~m_LiveChannels);
            }
            m_LiveChannels = channels;
            m_LiveChannelBits = (int)channels;
        }
    }
}
