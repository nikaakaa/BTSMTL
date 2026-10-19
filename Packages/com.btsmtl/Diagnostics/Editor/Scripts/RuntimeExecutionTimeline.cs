using System;
using System.Collections.Generic;

namespace BTSMTL.Diagnostics.Editor
{
    public enum RuntimeExecutionSpanKind
    {
        TreeClip,
        Clip,
        State,
        Transition,
        SectionJump,
        PlaybackSample,
        PlaybackBoundary
    }

    public enum RuntimeExecutionSpanState
    {
        Open,
        Completed,
        MissingStart
    }

    public readonly struct RuntimeExecutionSpan
    {
        public RuntimeExecutionSpan(
            RuntimeExecutionSpanKind kind,
            in RuntimeTraceEvent start,
            in RuntimeTraceEvent end,
            RuntimeSourceElementHandle sourceHandle,
            RuntimeSourceElementKey source,
            bool hasSource,
            RuntimeExecutionSpanState state)
        {
            Kind = kind;
            Start = start;
            End = end;
            SourceHandle = sourceHandle;
            Source = source;
            HasSource = hasSource;
            State = state;
        }

        public RuntimeExecutionSpanKind Kind { get; }
        public RuntimeTraceEvent Start { get; }
        public RuntimeTraceEvent End { get; }
        public RuntimeSourceElementHandle SourceHandle { get; }
        public RuntimeSourceElementKey Source { get; }
        public bool HasSource { get; }
        public RuntimeExecutionSpanState State { get; }
        public bool Completed => State == RuntimeExecutionSpanState.Completed;
        public bool IsOpen => State == RuntimeExecutionSpanState.Open;
        public RuntimeInstanceKey Instance => Start.RuntimeInstance;
        public Guid ExecutionBranchId => Start.ExecutionBranchId;
        public RuntimeTraceDomain Domain => Start.Domain;
        public ulong StartPosition => Start.Position;
        public ulong EndPosition => End.Position;
        public ulong StartSequence => Start.Sequence;
        public ulong EndSequence => End.Sequence;
        public ulong RuntimeEpoch => Start.RuntimeEpoch;
        public RuntimeContentRevision ContentRevision => Start.ContentRevision;
        public ulong ActionInstanceId => Start.Payload.ActionInstanceId;
        public string CallSiteId => Start.Payload.CallSiteId;
        public ulong ActivationGeneration => Start.Payload.ActivationGeneration;
        public ulong SkillExecutionGeneration => Start.Payload.SkillExecutionGeneration;
        public ulong GraphInvocationGeneration => Start.Payload.GraphInvocationGeneration;
        public ulong ParentInvocationGeneration => Start.Payload.ParentInvocationGeneration;
        public int Cycle => Math.Max(Start.Payload.Cycle, End.Payload.Cycle);
        public int LoopIteration => Start.Payload.LoopIteration;
        public RuntimeExecutionPlaybackKey Playback => new(Start.RuntimeInstance, ExecutionBranchId,
            RuntimeEpoch, ContentRevision, Domain);
    }

    public sealed class RuntimeExecutionTimeline
    {
        readonly RuntimeExecutionTimelineBuilder.SpanAccumulator.ProjectedSpans m_Spans;
        readonly RuntimeExecutionCursorIndex m_Cursors = new();

        internal RuntimeExecutionTimeline(RuntimeExecutionTimelineBuilder.SpanAccumulator.ProjectedSpans spans)
        {
            m_Spans = spans;
        }

        public Guid CaptureId { get; private set; }
        public long ProjectionRevision => m_Spans.Revision;
        public RuntimeTraceChannel Channels { get; private set; }
        public RuntimeDiagnosticsCaptureDetail Detail { get; private set; }
        public long Version { get; private set; }
        public long EvictedEvents { get; private set; }
        public bool IsComplete { get; private set; }
        public int UnmappedEventCount { get; private set; }
        public double LatestLogicTimeSeconds { get; private set; }
        public double LatestPresentationTimeSeconds { get; private set; }
        public IReadOnlyList<RuntimeExecutionSpan> Spans => m_Spans;
        public RuntimeExecutionCursorIndex Cursors => m_Cursors;
        public bool TryGetClockTime(RuntimeTraceDomain domain, Guid branch, ulong epoch, out double time) =>
            m_Spans.TryGetClockTime(domain, branch, epoch, out time);
        public ref readonly RuntimeCaptureSpanData GetSpanData(int index) => ref m_Spans.GetSpanData(index);
        public ref readonly RuntimeSourceElementKey GetSpanSource(int index) => ref m_Spans.GetSpanSource(index);

        public double GetObservedEndTime(int index)
        {
            ref readonly RuntimeCaptureSpanData span = ref GetSpanData(index);
            if (span.State != RuntimeCaptureSpanState.Open)
                return span.End.TimeSeconds;
            RuntimeTraceDomain domain = span.Start.Domain;
            if (domain == RuntimeTraceDomain.Lifecycle)
                domain = RuntimeTraceDomain.Logic;
            return Math.Max(span.End.TimeSeconds, m_Spans.GetClockTime(domain, span.Start.ExecutionBranchId, span.Start.RuntimeEpoch));
        }

        internal void Update(Guid captureId, RuntimeTraceChannel channels, RuntimeDiagnosticsCaptureDetail detail,
            long version, long evictedEvents, bool complete, int unmappedEventCount,
            double latestLogicTimeSeconds, double latestPresentationTimeSeconds)
        {
            CaptureId = captureId;
            Channels = channels;
            Detail = detail;
            Version = version;
            EvictedEvents = evictedEvents;
            IsComplete = complete;
            UnmappedEventCount = unmappedEventCount;
            LatestLogicTimeSeconds = latestLogicTimeSeconds;
            LatestPresentationTimeSeconds = latestPresentationTimeSeconds;
            m_Cursors.Rebuild(this);
        }
    }

    public readonly struct RuntimeExecutionSourceClock : IEquatable<RuntimeExecutionSourceClock>
    {
        public RuntimeExecutionSourceClock(string clockId, string tickKind)
        {
            if (string.IsNullOrWhiteSpace(clockId) || string.IsNullOrWhiteSpace(tickKind))
                throw new ArgumentException("Runtime execution source clock identity is incomplete.");
            ClockId = clockId;
            TickKind = tickKind;
        }

        public string ClockId { get; }
        public string TickKind { get; }
        public bool Equals(RuntimeExecutionSourceClock other) =>
            string.Equals(ClockId, other.ClockId, StringComparison.Ordinal) &&
            string.Equals(TickKind, other.TickKind, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is RuntimeExecutionSourceClock other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ClockId, TickKind);
        public override string ToString() => $"{TickKind}:{ClockId}";
        public static bool operator ==(RuntimeExecutionSourceClock left, RuntimeExecutionSourceClock right) => left.Equals(right);
        public static bool operator !=(RuntimeExecutionSourceClock left, RuntimeExecutionSourceClock right) => !left.Equals(right);
    }

    public sealed class RuntimeExecutionTickRecord
    {
        readonly RuntimeTraceEvent[] m_Events;
        readonly RuntimeTraceEvent[] m_ExternalResults;
        readonly Guid[] m_SessionIds;
        readonly Guid[] m_ExecutionBranchIds;
        readonly RuntimeContentRevision[] m_ContentRevisions;
        readonly ulong[] m_RuntimeEpochs;
        readonly RuntimeExecutionSourceClock[] m_SourceClocks;
        readonly Guid[] m_CharacterRuntimeIds;
        readonly RuntimeInstanceKey[] m_RuntimeInstances;
        readonly ulong[] m_ActionInstanceIds;
        readonly string[] m_CallSiteIds;
        readonly ulong[] m_InputSequences;

        internal RuntimeExecutionTickRecord(
            ulong tick,
            Guid executionBranchId,
            RuntimeTraceEvent[] events)
        {
            Tick = tick;
            ExecutionBranchId = executionBranchId;
            m_Events = events;
            Collector<RuntimeTraceEvent> externalResults = default;
            Collector<Guid> sessionIds = default;
            Collector<Guid> executionBranchIds = default;
            Collector<RuntimeContentRevision> contentRevisions = default;
            Collector<ulong> runtimeEpochs = default;
            Collector<RuntimeExecutionSourceClock> sourceClocks = default;
            Collector<Guid> characterRuntimeIds = default;
            Collector<RuntimeInstanceKey> runtimeInstances = default;
            Collector<ulong> actionInstanceIds = default;
            Collector<string> callSiteIds = default;
            Collector<ulong> inputSequences = default;
            for (int i = 0; i < m_Events.Length; i++)
            {
                RuntimeTraceEvent traceEvent = m_Events[i];
                if (traceEvent.Kind == RuntimeTraceEventKind.SimulationNetworkModel)
                    externalResults.Add(traceEvent);
                if (traceEvent.SessionId != Guid.Empty)
                    sessionIds.AddDistinct(traceEvent.SessionId);
                if (traceEvent.ExecutionBranchId != Guid.Empty)
                    executionBranchIds.AddDistinct(traceEvent.ExecutionBranchId);
                if (traceEvent.ContentRevision.IsValid)
                    contentRevisions.AddDistinct(traceEvent.ContentRevision);
                if (traceEvent.RuntimeEpoch != 0)
                    runtimeEpochs.AddDistinct(traceEvent.RuntimeEpoch);
                if (!string.IsNullOrEmpty(traceEvent.Payload.SourceClockId) &&
                    !string.IsNullOrEmpty(traceEvent.Payload.SourceTickKind))
                {
                    var sourceClock = new RuntimeExecutionSourceClock(
                        traceEvent.Payload.SourceClockId,
                        traceEvent.Payload.SourceTickKind);
                    sourceClocks.AddDistinct(sourceClock);
                }
                if (traceEvent.RuntimeInstance.CharacterRuntimeId != Guid.Empty)
                    characterRuntimeIds.AddDistinct(traceEvent.RuntimeInstance.CharacterRuntimeId);
                if (traceEvent.RuntimeInstance.IsValid)
                    runtimeInstances.AddDistinct(traceEvent.RuntimeInstance);
                if (traceEvent.Payload.ActionInstanceId != 0)
                    actionInstanceIds.AddDistinct(traceEvent.Payload.ActionInstanceId);
                if (!string.IsNullOrEmpty(traceEvent.Payload.CallSiteId))
                    callSiteIds.AddDistinct(traceEvent.Payload.CallSiteId);
                if (traceEvent.Payload.InputSequence != 0)
                    inputSequences.AddDistinct(traceEvent.Payload.InputSequence);
            }
            m_ExternalResults = externalResults.ToArray();
            m_SessionIds = sessionIds.ToArray();
            m_ExecutionBranchIds = executionBranchIds.ToArray();
            m_ContentRevisions = contentRevisions.ToArray();
            m_RuntimeEpochs = runtimeEpochs.ToArray();
            m_SourceClocks = sourceClocks.ToArray();
            m_CharacterRuntimeIds = characterRuntimeIds.ToArray();
            m_RuntimeInstances = runtimeInstances.ToArray();
            m_ActionInstanceIds = actionInstanceIds.ToArray();
            m_CallSiteIds = callSiteIds.ToArray();
            m_InputSequences = inputSequences.ToArray();
        }

        public ulong Tick { get; }
        public Guid ExecutionBranchId { get; }
        public IReadOnlyList<RuntimeTraceEvent> Events => m_Events;
        public IReadOnlyList<RuntimeTraceEvent> ExternalResults => m_ExternalResults;
        public IReadOnlyList<Guid> SessionIds => m_SessionIds;
        public IReadOnlyList<Guid> ExecutionBranchIds => m_ExecutionBranchIds;
        public IReadOnlyList<RuntimeContentRevision> ContentRevisions => m_ContentRevisions;
        public IReadOnlyList<ulong> RuntimeEpochs => m_RuntimeEpochs;
        public IReadOnlyList<RuntimeExecutionSourceClock> SourceClocks => m_SourceClocks;
        public IReadOnlyList<Guid> CharacterRuntimeIds => m_CharacterRuntimeIds;
        public IReadOnlyList<RuntimeInstanceKey> RuntimeInstances => m_RuntimeInstances;
        public IReadOnlyList<ulong> ActionInstanceIds => m_ActionInstanceIds;
        public IReadOnlyList<string> CallSiteIds => m_CallSiteIds;
        public IReadOnlyList<ulong> InputSequences => m_InputSequences;
        public bool HasSimulationTick => Contains(RuntimeTraceEventKind.SimulationTick);
        public bool HasStatePublished => Contains(RuntimeTraceEventKind.SimulationStatePublished);
        public bool HasExternalResult => m_ExternalResults.Length != 0;

        struct Collector<T>
        {
            T[] m_Values;
            int m_Count;

            public void Add(T value)
            {
                if (m_Count == (m_Values?.Length ?? 0))
                    Array.Resize(ref m_Values, m_Count == 0 ? 4 : m_Count * 2);
                m_Values[m_Count++] = value;
            }

            public void AddDistinct(T value)
            {
                for (int i = 0; i < m_Count; i++)
                    if (EqualityComparer<T>.Default.Equals(m_Values[i], value))
                        return;
                Add(value);
            }

            public T[] ToArray()
            {
                if (m_Count == 0)
                    return Array.Empty<T>();
                if (m_Count == m_Values.Length)
                {
                    T[] exact = m_Values;
                    m_Values = null;
                    return exact;
                }
                var result = new T[m_Count];
                Array.Copy(m_Values, result, m_Count);
                m_Values = null;
                return result;
            }
        }

        bool Contains(RuntimeTraceEventKind kind)
        {
            for (int i = 0; i < m_Events.Length; i++)
                if (m_Events[i].Kind == kind)
                    return true;
            return false;
        }

    }

    public sealed class RuntimeExecutionPresentationFrame
    {
        readonly RuntimeTraceEvent[] m_Events;

        internal RuntimeExecutionPresentationFrame(
            ulong frame,
            Guid executionBranchId,
            RuntimeTraceEvent[] events)
        {
            Frame = frame;
            ExecutionBranchId = executionBranchId;
            m_Events = events;
        }

        public ulong Frame { get; }
        public Guid ExecutionBranchId { get; }
        public IReadOnlyList<RuntimeTraceEvent> Events => m_Events;
    }

    public sealed class RuntimeExecutionCheckpoint
    {
        internal RuntimeExecutionCheckpoint(
            ulong tick,
            RuntimeTraceEvent traceEvent,
            bool historyComplete)
        {
            Tick = tick;
            TraceEvent = traceEvent;
            SnapshotIdentity = traceEvent.Payload.RelatedElementId ?? string.Empty;
            HistoryComplete = historyComplete;
        }

        public ulong Tick { get; }
        public RuntimeTraceEvent TraceEvent { get; }
        public string SnapshotIdentity { get; }
        public RuntimeContentRevision Revision => TraceEvent.ContentRevision;
        public ulong RuntimeEpoch => TraceEvent.RuntimeEpoch;
        public Guid ExecutionBranchId => TraceEvent.ExecutionBranchId;
        public RuntimeInstanceKey Instance => TraceEvent.RuntimeInstance;
        public bool HistoryComplete { get; }
        public bool CanRestore => HistoryComplete && !string.IsNullOrEmpty(SnapshotIdentity);
    }

    public sealed class RuntimeExecutionHistory
    {
        readonly RuntimeExecutionTickRecord[] m_Ticks;
        readonly RuntimeExecutionCheckpoint[] m_Checkpoints;
        readonly RuntimeExecutionPresentationFrame[] m_PresentationFrames;

        internal RuntimeExecutionHistory(
            Guid captureId,
            RuntimeTraceChannel channels,
            RuntimeDiagnosticsCaptureDetail detail,
            long version,
            long evictedEvents,
            bool complete,
            int unmappedEventCount,
            RuntimeExecutionTickRecord[] ticks,
            RuntimeExecutionCheckpoint[] checkpoints,
            RuntimeExecutionPresentationFrame[] presentationFrames)
        {
            CaptureId = captureId;
            Channels = channels;
            Detail = detail;
            Version = version;
            EvictedEvents = evictedEvents;
            IsComplete = complete;
            UnmappedEventCount = unmappedEventCount;
            m_Ticks = ticks;
            m_Checkpoints = checkpoints;
            m_PresentationFrames = presentationFrames;
        }

        public Guid CaptureId { get; }
        public RuntimeTraceChannel Channels { get; }
        public RuntimeDiagnosticsCaptureDetail Detail { get; }
        public long Version { get; }
        public long EvictedEvents { get; }
        public bool IsComplete { get; }
        public int UnmappedEventCount { get; }
        public IReadOnlyList<RuntimeExecutionTickRecord> Ticks => m_Ticks;
        public IReadOnlyList<RuntimeExecutionCheckpoint> Checkpoints => m_Checkpoints;
        public IReadOnlyList<RuntimeExecutionPresentationFrame> PresentationFrames => m_PresentationFrames;
        public bool HasExternalResults
        {
            get
            {
                for (int i = 0; i < m_Ticks.Length; i++)
                    if (m_Ticks[i].HasExternalResult)
                        return true;
                return false;
            }
        }
        public int ExternalResultCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < m_Ticks.Length; i++)
                    count += m_Ticks[i].ExternalResults.Count;
                return count;
            }
        }
        public bool HasPresentationFrames => m_PresentationFrames.Length > 0;
    }

    internal static class RuntimeExecutionTimelineBuilder
    {
        static readonly Comparison<RuntimeTraceEvent> CompareEventsComparer = CompareEvents;
        static readonly Comparison<RuntimeTraceEvent> CompareGroupedEventsComparer = CompareGroupedEvents;
        static readonly SelectionScratch Selection = new();
        static readonly HashSet<CheckpointKey> CheckpointKeys = new();
        static readonly HashSet<ulong> BoundarySequences = new();
        static readonly HashSet<Guid> BoundaryBranches = new();
        static readonly List<EventGroup<TickKey>> HistoryGroups = new();
        static readonly List<EventGroup<PresentationFrameKey>> PresentationGroups = new();
        static readonly List<RuntimeExecutionTickRecord> TickResults = new();
        static readonly List<RuntimeExecutionCheckpoint> CheckpointResults = new();
        static readonly List<RuntimeExecutionPresentationFrame> PresentationFrameResults = new();

        internal static RuntimeExecutionTimeline BuildWindow(RuntimeCaptureSnapshot capture,
            RuntimeCaptureReadWindow window, RuntimeDebugSourceMapSnapshot sourceMap,
            IReadOnlyDictionary<RuntimeContentRevision, RuntimeDebugSourceMapSnapshot> sourceMaps,
            SpanAccumulator spans)
        {
            spans.Import(ref window.TimelineSpans, sourceMap, sourceMaps);
            spans.Timeline.Update(capture.CaptureId, capture.Channels, capture.Detail,
                window.TimelineVersion, capture.EvictedEvents, spans.IsComplete(capture.EvictedEvents),
                spans.UnmappedEventCount, window.LatestLogicTime, window.LatestPresentationTime);
            return spans.Timeline;
        }

        internal static RuntimeExecutionTimeline Build(
            RuntimeCaptureSnapshot capture,
            RuntimeDebugSourceMapSnapshot sourceMap,
            int historyOffset = 0,
            RuntimeInstanceKey instance = default,
            ulong throughSequence = ulong.MaxValue)
        {
            return BuildCore(capture, sourceMap, null, historyOffset, instance, throughSequence);
        }

        internal static RuntimeExecutionTimeline Build(
            RuntimeCaptureSnapshot capture,
            RuntimeDebugSourceMapSnapshot sourceMap,
            IReadOnlyDictionary<RuntimeContentRevision, RuntimeDebugSourceMapSnapshot> sourceMaps,
            int historyOffset = 0,
            RuntimeInstanceKey instance = default,
            ulong throughSequence = ulong.MaxValue)
        {
            return BuildCore(capture, sourceMap, sourceMaps, historyOffset, instance, throughSequence);
        }

        static RuntimeExecutionTimeline BuildCore(
            RuntimeCaptureSnapshot capture,
            RuntimeDebugSourceMapSnapshot sourceMap,
            IReadOnlyDictionary<RuntimeContentRevision, RuntimeDebugSourceMapSnapshot> sourceMaps,
            int historyOffset,
            RuntimeInstanceKey instance,
            ulong throughSequence)
        {
            if (!capture.IsValid)
                throw new ArgumentNullException(nameof(capture));
            if (!sourceMap.IsValid)
                throw new ArgumentNullException(nameof(sourceMap));
            if (historyOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(historyOffset));

            ReadOnlySpan<RuntimeTraceEvent> events = capture.GetEvents(historyOffset, throughSequence);
            List<RuntimeTraceEvent> selected = SelectEvents(events, instance);
            selected.Sort(CompareEventsComparer);

            var spans = new SpanAccumulator();
            for (int i = 0; i < selected.Count; i++)
                spans.Append(selected[i], sourceMap, sourceMaps);
            spans.Timeline.Update(
                capture.CaptureId, capture.Channels, capture.Detail, capture.Version, capture.EvictedEvents,
                spans.IsComplete(capture.EvictedEvents), spans.UnmappedEventCount,
                spans.LatestLogicTimeSeconds, spans.LatestPresentationTimeSeconds);
            return spans.Timeline;
        }

        internal sealed class SpanAccumulator
        {
            struct SourceBinding
            {
                internal RuntimeSourceElementKey Source;
                internal bool HasSource;
            }

            RuntimeCaptureSpanAccumulator m_Facts;
            readonly List<int> m_Displayed;
            readonly ProjectedSpans m_Spans;
            SourceBinding[] m_Sources;
            int m_Processed;
            internal ProjectedSpans Spans => m_Spans;
            internal RuntimeExecutionTimeline Timeline { get; }
            internal long ProjectionRevision { get; private set; }
            internal int UnmappedEventCount { get; private set; }
            internal double LatestLogicTimeSeconds => m_Facts.LatestLogicTimeSeconds;
            internal double LatestPresentationTimeSeconds => m_Facts.LatestPresentationTimeSeconds;

            internal SpanAccumulator(int capacity = 0)
            {
                m_Facts = new RuntimeCaptureSpanAccumulator(capacity);
                m_Displayed = new List<int>(capacity);
                m_Sources = new SourceBinding[capacity];
                m_Spans = new ProjectedSpans(this);
                Timeline = new RuntimeExecutionTimeline(m_Spans);
            }

            internal void Reset()
            {
                ClearProjection();
                m_Facts.Reset();
            }

            void ClearProjection()
            {
                ProjectionRevision++;
                Array.Clear(m_Sources, 0, m_Facts.SourceCount);
                m_Displayed.Clear();
                m_Processed = 0;
                UnmappedEventCount = 0;
            }

            internal bool IsComplete(long evictedEvents) => m_Facts.IsComplete(evictedEvents, Spans.Count, UnmappedEventCount);

            internal void TrimBefore(double logicTime, double presentationTime)
            {
                if (!m_Facts.TrimBefore(logicTime, presentationTime))
                    return;
                ProjectionRevision++;
                m_Displayed.Clear();
                m_Processed = 0;
                ProjectNewSpans();
            }

            SourceBinding Resolve(RuntimeSourceElementHandle handle, RuntimeContentRevision revision,
                RuntimeDebugSourceMapSnapshot sourceMap,
                IReadOnlyDictionary<RuntimeContentRevision, RuntimeDebugSourceMapSnapshot> sourceMaps)
            {
                RuntimeDebugSourceMapSnapshot map = default;
                bool hasRevision = sourceMaps != null ? sourceMaps.TryGetValue(revision, out map) : sourceMap.Revision.Equals(revision);
                if (sourceMaps == null && hasRevision)
                    map = sourceMap;
                RuntimeSourceElementKey source = default;
                bool hasSource = hasRevision && handle.IsValid && map.TryResolve(handle, out source, out _);
                return new SourceBinding { Source = source, HasSource = hasSource };
            }

            void SetSource(int index, SourceBinding source)
            {
                if (index >= m_Sources.Length)
                    Array.Resize(ref m_Sources, Math.Max(index + 1, m_Sources.Length * 2));
                m_Sources[index] = source;
            }

            void ProjectNewSpans()
            {
                while (m_Processed < m_Facts.Count)
                {
                    m_Displayed.Add(m_Processed);
                    m_Processed++;
                }
            }

            internal void Append(RuntimeTraceEvent sourceValue, RuntimeDebugSourceMapSnapshot sourceMap,
                IReadOnlyDictionary<RuntimeContentRevision, RuntimeDebugSourceMapSnapshot> sourceMaps)
            {
                int group = m_Facts.AppendMetadata(in sourceValue);
                if (group < 0)
                    return;
                SourceBinding source = Resolve(sourceValue.Source, sourceValue.ContentRevision, sourceMap, sourceMaps);
                SetSource(group, source);
                if (!source.HasSource)
                    UnmappedEventCount++;
                m_Facts.AppendTrack(in sourceValue, group);
                ProjectNewSpans();
            }

            internal void Import(ref RuntimeCaptureSpanAccumulator facts, RuntimeDebugSourceMapSnapshot sourceMap,
                IReadOnlyDictionary<RuntimeContentRevision, RuntimeDebugSourceMapSnapshot> sourceMaps)
            {
                ClearProjection();
                (m_Facts, facts) = (facts, m_Facts);
                for (int i = 0; i < m_Facts.SourceCount; i++)
                {
                    RuntimeCaptureSpanSource group = m_Facts.GetSource(i);
                    SourceBinding binding = Resolve(group.Handle, group.Revision, sourceMap, sourceMaps);
                    SetSource(i, binding);
                    if (!binding.HasSource)
                        UnmappedEventCount += group.EventCount;
                }
                ProjectNewSpans();
                m_Facts.KeepSpans(m_Displayed);
                for (int i = 0; i < m_Displayed.Count; i++)
                    m_Displayed[i] = i;
                m_Processed = m_Facts.Count;
            }

            internal sealed class ProjectedSpans : IReadOnlyList<RuntimeExecutionSpan>
            {
                readonly SpanAccumulator m_Owner;
                internal ProjectedSpans(SpanAccumulator owner) => m_Owner = owner;
                public int Count => m_Owner.m_Displayed.Count;
                internal long Revision => m_Owner.ProjectionRevision;
                internal ref readonly RuntimeCaptureSpanData GetSpanData(int index) =>
                    ref m_Owner.m_Facts.GetSpan(m_Owner.m_Displayed[index]);
                internal ref readonly RuntimeSourceElementKey GetSpanSource(int index) =>
                    ref m_Owner.m_Sources[GetSpanData(index).SourceGroup].Source;
                internal bool TryGetClockTime(RuntimeTraceDomain domain, Guid branch, ulong epoch, out double time) =>
                    m_Owner.m_Facts.ClockTimes.TryGetValue((domain, branch, epoch), out time);
                internal double GetClockTime(RuntimeTraceDomain domain, Guid branch, ulong epoch) =>
                    m_Owner.m_Facts.ClockTimes[(domain, branch, epoch)];
                public RuntimeExecutionSpan this[int index]
                {
                    get
                    {
                        ref readonly RuntimeCaptureSpanData span = ref GetSpanData(index);
                        ref readonly RuntimeSourceElementKey source = ref GetSpanSource(index);
                        RuntimeExecutionSpanKind kind = span.Start.Kind switch
                        {
                            RuntimeTraceEventKind.StateScopeEntered or RuntimeTraceEventKind.StateScopeExited => RuntimeExecutionSpanKind.State,
                            RuntimeTraceEventKind.StateTransitionSelected => RuntimeExecutionSpanKind.Transition,
                            RuntimeTraceEventKind.TimelineSectionJumped => RuntimeExecutionSpanKind.SectionJump,
                            RuntimeTraceEventKind.TimelineLogicTime or RuntimeTraceEventKind.TimelineVisualTime => RuntimeExecutionSpanKind.PlaybackSample,
                            RuntimeTraceEventKind.TimelineStarted or RuntimeTraceEventKind.TimelineCompleted or
                                RuntimeTraceEventKind.TimelineCancelled or RuntimeTraceEventKind.TimelineStopped => RuntimeExecutionSpanKind.PlaybackBoundary,
                            _ => span.Start.Source.Kind == RuntimeSourceElementKind.Clip ? RuntimeExecutionSpanKind.Clip : RuntimeExecutionSpanKind.TreeClip
                        };
                        return new RuntimeExecutionSpan(kind, in span.Start, in span.End, span.Start.Source,
                            source, m_Owner.m_Sources[span.SourceGroup].HasSource, (RuntimeExecutionSpanState)span.State);
                    }
                }
                public IEnumerator<RuntimeExecutionSpan> GetEnumerator()
                {
                    for (int i = 0; i < Count; i++)
                        yield return this[i];
                }
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }
        }

        internal static RuntimeExecutionHistory BuildHistory(
            RuntimeCaptureSnapshot capture,
            int historyOffset = 0,
            RuntimeInstanceKey instance = default,
            ulong throughSequence = ulong.MaxValue)
        {
            return BuildHistory(capture, default, null, historyOffset, instance, throughSequence);
        }

        internal static RuntimeExecutionHistory BuildHistory(
            RuntimeCaptureSnapshot capture,
            RuntimeDebugSourceMapSnapshot sourceMap,
            IReadOnlyDictionary<RuntimeContentRevision, RuntimeDebugSourceMapSnapshot> sourceMaps,
            int historyOffset = 0,
            RuntimeInstanceKey instance = default,
            ulong throughSequence = ulong.MaxValue)
        {
            if (!capture.IsValid)
                throw new ArgumentNullException(nameof(capture));
            if (historyOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(historyOffset));

            ReadOnlySpan<RuntimeTraceEvent> allEvents = capture.GetEvents(historyOffset, throughSequence);
            List<RuntimeTraceEvent> selectedEvents = SelectEvents(allEvents, instance);
            if (instance.IsValid && selectedEvents.Count > 0)
                AddSessionBoundaryEvents(
                    allEvents,
                    selectedEvents,
                    selectedEvents);

            selectedEvents.Sort(CompareGroupedEventsComparer);
            List<EventGroup<TickKey>> grouped = HistoryGroups;
            List<EventGroup<PresentationFrameKey>> presentation = PresentationGroups;
            grouped.Clear();
            presentation.Clear();
            for (int i = 0; i < selectedEvents.Count; i++)
            {
                RuntimeTraceEvent value = selectedEvents[i];
                if (value.Domain == RuntimeTraceDomain.Presentation)
                {
                    var presentationKey = new PresentationFrameKey(value.Position, value.ExecutionBranchId);
                    if (presentation.Count == 0 ||
                        !EqualityComparer<PresentationFrameKey>.Default.Equals(
                            presentation[presentation.Count - 1].Key,
                            presentationKey))
                    {
                        presentation.Add(new EventGroup<PresentationFrameKey>(presentationKey));
                    }
                    EventGroup<PresentationFrameKey> presentationGroup = presentation[presentation.Count - 1];
                    presentationGroup.AddCount();
                    presentation[presentation.Count - 1] = presentationGroup;
                    continue;
                }
                TickKey key = new TickKey(value.Position, value.ExecutionBranchId);
                if (grouped.Count == 0 ||
                    !EqualityComparer<TickKey>.Default.Equals(
                        grouped[grouped.Count - 1].Key,
                        key))
                {
                    grouped.Add(new EventGroup<TickKey>(key));
                }
                EventGroup<TickKey> tickGroup = grouped[grouped.Count - 1];
                tickGroup.AddCount();
                grouped[grouped.Count - 1] = tickGroup;
            }

            for (int i = 0; i < grouped.Count; i++)
            {
                EventGroup<TickKey> group = grouped[i];
                group.PrepareEvents();
                grouped[i] = group;
            }
            for (int i = 0; i < presentation.Count; i++)
            {
                EventGroup<PresentationFrameKey> group = presentation[i];
                group.PrepareEvents();
                presentation[i] = group;
            }

            int tickGroupIndex = 0;
            int presentationGroupIndex = 0;
            int unmappedEventCount = 0;
            bool samplingLimited = false;
            bool checkSourceCoverage = sourceMap.IsValid || sourceMaps != null;
            for (int i = 0; i < selectedEvents.Count; i++)
            {
                RuntimeTraceEvent value = selectedEvents[i];
                samplingLimited |= value.Kind is RuntimeTraceEventKind.TraceSamplingLimited or RuntimeTraceEventKind.ValueSamplingLimited;
                if (checkSourceCoverage && value.Source.IsValid)
                {
                    RuntimeDebugSourceMapSnapshot resolvedMap = sourceMap;
                    bool hasRevision = sourceMaps != null
                        ? sourceMaps.TryGetValue(value.ContentRevision, out resolvedMap)
                        : sourceMap.IsValid && sourceMap.Revision.Equals(value.ContentRevision);
                    if (!hasRevision || !resolvedMap.IsValid || !resolvedMap.TryGet(value.Source, out _))
                        unmappedEventCount++;
                }
                if (value.Domain == RuntimeTraceDomain.Presentation)
                {
                    EventGroup<PresentationFrameKey> presentationWriter = presentation[presentationGroupIndex];
                    presentationWriter.Write(value);
                    presentation[presentationGroupIndex] = presentationWriter;
                    if (presentationWriter.IsFull)
                        presentationGroupIndex++;
                    continue;
                }

                EventGroup<TickKey> tickWriter = grouped[tickGroupIndex];
                tickWriter.Write(value);
                grouped[tickGroupIndex] = tickWriter;
                if (tickWriter.IsFull)
                    tickGroupIndex++;
            }

            List<RuntimeExecutionTickRecord> ticks = TickResults;
            List<RuntimeExecutionCheckpoint> checkpoints = CheckpointResults;
            List<RuntimeExecutionPresentationFrame> presentationFrames = PresentationFrameResults;
            ticks.Clear();
            checkpoints.Clear();
            presentationFrames.Clear();
            HashSet<CheckpointKey> checkpointKeys = CheckpointKeys;
            checkpointKeys.Clear();
            bool complete = capture.EvictedEvents == 0 && grouped.Count != 0 && !samplingLimited;
            foreach (EventGroup<TickKey> group in grouped)
            {
                var record = new RuntimeExecutionTickRecord(
                    group.Key.Tick,
                    group.Key.ExecutionBranchId,
                    group.Events);
                complete &= record.HasSimulationTick && record.HasStatePublished;
                ticks.Add(record);
            }
            bool historyComplete = complete && unmappedEventCount == 0;
            foreach (EventGroup<TickKey> group in grouped)
            {
                for (int i = 0; i < group.Events.Length; i++)
                {
                    RuntimeTraceEvent traceEvent = group.Events[i];
                    if (traceEvent.Kind != RuntimeTraceEventKind.SimulationCheckpointCaptured)
                        continue;
                    var checkpointKey = new CheckpointKey(
                        group.Key.Tick,
                        group.Key.ExecutionBranchId,
                        traceEvent.RuntimeEpoch,
                        traceEvent.ContentRevision,
                        traceEvent.Payload.RelatedElementId);
                    if (!checkpointKeys.Add(checkpointKey))
                        continue;
                    RuntimeExecutionCheckpoint checkpoint = new RuntimeExecutionCheckpoint(
                        group.Key.Tick,
                        traceEvent,
                        historyComplete);
                    checkpoints.Add(checkpoint);
                }
            }
            foreach (EventGroup<PresentationFrameKey> group in presentation)
            {
                presentationFrames.Add(new RuntimeExecutionPresentationFrame(
                    group.Key.Frame,
                    group.Key.ExecutionBranchId,
                    group.Events));
            }
            RuntimeExecutionHistory history = new(
                capture.CaptureId,
                capture.Channels,
                capture.Detail,
                capture.Version,
                capture.EvictedEvents,
                historyComplete,
                unmappedEventCount,
                ticks.ToArray(),
                checkpoints.ToArray(),
                presentationFrames.ToArray());
            grouped.Clear();
            presentation.Clear();
            ticks.Clear();
            checkpoints.Clear();
            presentationFrames.Clear();
            return history;
        }

        static void AddSessionBoundaryEvents(
            ReadOnlySpan<RuntimeTraceEvent> allEvents,
            IReadOnlyList<RuntimeTraceEvent> selectedEvents,
            List<RuntimeTraceEvent> historyEvents)
        {
            ulong first = ulong.MaxValue;
            ulong last = 0;
            for (int i = 0; i < selectedEvents.Count; i++)
            {
                RuntimeTraceEvent value = selectedEvents[i];
                if (value.Domain == RuntimeTraceDomain.Presentation || value.Position == 0)
                    continue;
                first = Math.Min(first, value.Position);
                last = Math.Max(last, value.Position);
            }
            if (first == ulong.MaxValue)
                return;

            HashSet<ulong> selectedSequences = BoundarySequences;
            HashSet<Guid> selectedBranches = BoundaryBranches;
            selectedSequences.Clear();
            selectedBranches.Clear();
            for (int i = 0; i < historyEvents.Count; i++)
            {
                selectedSequences.Add(historyEvents[i].Sequence);
                selectedBranches.Add(historyEvents[i].ExecutionBranchId);
            }
            bool hasBaselineCheckpoint = false;
            ulong baselineCheckpointTick = 0;
            for (int i = 0; i < allEvents.Length; i++)
            {
                RuntimeTraceEvent value = allEvents[i];
                if (value.Kind != RuntimeTraceEventKind.SimulationCheckpointCaptured ||
                    value.Position > first ||
                    !selectedBranches.Contains(value.ExecutionBranchId) ||
                    hasBaselineCheckpoint && value.Position <= baselineCheckpointTick)
                    continue;
                hasBaselineCheckpoint = true;
                baselineCheckpointTick = value.Position;
            }
            if (hasBaselineCheckpoint)
                first = baselineCheckpointTick;
            for (int i = 0; i < allEvents.Length; i++)
            {
                RuntimeTraceEvent value = allEvents[i];
                if (value.Position < first || value.Position > last ||
                    !IsSessionBoundaryEvent(value.Kind) ||
                    !selectedBranches.Contains(value.ExecutionBranchId) ||
                    !selectedSequences.Add(value.Sequence))
                {
                    continue;
                }
                historyEvents.Add(value);
            }
        }

        static bool IsSessionBoundaryEvent(RuntimeTraceEventKind kind)
        {
            return kind is RuntimeTraceEventKind.SimulationTick or
                RuntimeTraceEventKind.SimulationRestore or
                RuntimeTraceEventKind.SimulationFailure or
                RuntimeTraceEventKind.SimulationNetworkModel or
                RuntimeTraceEventKind.SimulationStatePublished or
                RuntimeTraceEventKind.SimulationCheckpointCaptured;
        }

        struct EventGroup<TKey>
        {
            TKey m_Key;
            RuntimeTraceEvent[] m_Events;
            int m_Count;

            internal EventGroup(TKey key)
            {
                m_Key = key;
                m_Events = Array.Empty<RuntimeTraceEvent>();
                m_Count = 0;
            }

            internal TKey Key => m_Key;
            internal RuntimeTraceEvent[] Events => m_Events;
            internal bool IsFull => m_Count == m_Events.Length;

            internal void AddCount()
            {
                m_Count++;
            }

            internal void PrepareEvents()
            {
                m_Events = new RuntimeTraceEvent[m_Count];
                m_Count = 0;
            }

            internal void Write(RuntimeTraceEvent value)
            {
                m_Events[m_Count++] = value;
            }
        }

        static List<RuntimeTraceEvent> SelectEvents(
            ReadOnlySpan<RuntimeTraceEvent> events,
            RuntimeInstanceKey instance)
        {
            List<RuntimeTraceEvent> selected = Selection.Events;
            selected.Clear();
            if (!instance.IsValid)
            {
                if (selected.Capacity < events.Length)
                    selected.Capacity = events.Length;
                for (int i = 0; i < events.Length; i++)
                    selected.Add(events[i]);
                return selected;
            }

            HashSet<ulong> selectedSequences = Selection.Sequences;
            HashSet<GraphBranchKey> relatedGraphs = Selection.RelatedGraphs;
            HashSet<PresentationFrameKey> selectedPresentationFrames = Selection.PresentationFrames;
            selectedSequences.Clear();
            relatedGraphs.Clear();
            selectedPresentationFrames.Clear();
            Guid selectedBranch = ResolveSelectionBranch(events, instance);
            for (int i = 0; i < events.Length; i++)
            {
                RuntimeTraceEvent value = events[i];
                if (selectedBranch != Guid.Empty && value.ExecutionBranchId != selectedBranch)
                    continue;
                RuntimeInstanceKey candidate = value.RuntimeInstance;
                bool actionMatch = MatchesAction(instance, value);
                if (IsTimelineFamily(instance.Kind) &&
                    candidate.TimelinePlaybackId != 0)
                {
                    actionMatch = MatchesTimelineRuntime(instance, candidate);
                }
                bool include = candidate.Equals(instance) || actionMatch;
                if (actionMatch && value.Domain == RuntimeTraceDomain.Presentation)
                    selectedPresentationFrames.Add(new PresentationFrameKey(value.Position, value.ExecutionBranchId));
                if (actionMatch)
                    AddRelatedGraph(relatedGraphs, candidate, value.ExecutionBranchId);
                if (instance.Kind == RuntimeInstanceKind.TimelinePlayback &&
                    candidate.SourceOperationIndex == instance.SourceOperationIndex &&
                    candidate.TimelinePlaybackId == instance.TimelinePlaybackId &&
                    (instance.ActionInstanceId == 0 ||
                     candidate.ActionInstanceId == instance.ActionInstanceId) &&
                    candidate.TimelinePlaybackId != 0)
                {
                    include = true;
                    AddRelatedGraph(relatedGraphs, candidate, value.ExecutionBranchId);
                }
                else if (instance.Kind == RuntimeInstanceKind.TreeClip &&
                         candidate.Kind == RuntimeInstanceKind.TreeClip &&
                         candidate.SourceOperationIndex == instance.SourceOperationIndex &&
                         candidate.TreeClipOperationIndex == instance.TreeClipOperationIndex &&
                         candidate.TimelinePlaybackId == instance.TimelinePlaybackId &&
                         candidate.TreeClipCycle == instance.TreeClipCycle &&
                         (instance.ActionInstanceId == 0 ||
                          candidate.ActionInstanceId == instance.ActionInstanceId) &&
                         candidate.TimelinePlaybackId != 0)
                {
                    include = true;
                    AddRelatedGraph(relatedGraphs, candidate, value.ExecutionBranchId);
                }

                if (include && value.Domain == RuntimeTraceDomain.Presentation)
                    selectedPresentationFrames.Add(new PresentationFrameKey(value.Position, value.ExecutionBranchId));

                if (include && selectedSequences.Add(value.Sequence))
                    selected.Add(value);
            }

            for (int i = 0; i < events.Length; i++)
            {
                RuntimeTraceEvent value = events[i];
                RuntimeInstanceKey candidate = value.RuntimeInstance;
                bool presentationMatch =
                    value.Domain == RuntimeTraceDomain.Presentation &&
                    candidate.CharacterRuntimeId == instance.CharacterRuntimeId &&
                    selectedPresentationFrames.Contains(new PresentationFrameKey(value.Position, value.ExecutionBranchId));
                if (presentationMatch &&
                    IsTimelineFamily(instance.Kind) &&
                    candidate.TimelinePlaybackId != 0 &&
                    !MatchesTimelineRuntime(instance, candidate))
                {
                    presentationMatch = false;
                }
                bool graphMatch = candidate.CharacterRuntimeId == instance.CharacterRuntimeId &&
                    relatedGraphs.Contains(new GraphBranchKey(candidate.GraphRuntimeId, value.ExecutionBranchId));
                if (graphMatch &&
                    IsTimelineFamily(instance.Kind) &&
                    candidate.TimelinePlaybackId != 0 &&
                    !MatchesTimelineRuntime(instance, candidate))
                {
                    graphMatch = false;
                }
                if ((!presentationMatch && !graphMatch) ||
                    !selectedSequences.Add(value.Sequence))
                {
                    continue;
                }
                selected.Add(value);
            }

            return selected;
        }

        sealed class SelectionScratch
        {
            internal readonly List<RuntimeTraceEvent> Events = new();
            internal readonly HashSet<ulong> Sequences = new();
            internal readonly HashSet<GraphBranchKey> RelatedGraphs = new();
            internal readonly HashSet<PresentationFrameKey> PresentationFrames = new();
        }

        static Guid ResolveSelectionBranch(
            ReadOnlySpan<RuntimeTraceEvent> events,
            RuntimeInstanceKey instance)
        {
            Guid branch = Guid.Empty;
            ulong latestSequence = 0;
            for (int i = 0; i < events.Length; i++)
            {
                RuntimeTraceEvent value = events[i];
                bool matchesInstance = value.RuntimeInstance.Equals(instance) || MatchesAction(instance, value);
                if (value.ExecutionBranchId == Guid.Empty ||
                    !matchesInstance ||
                    branch != Guid.Empty && value.Sequence <= latestSequence)
                    continue;
                branch = value.ExecutionBranchId;
                latestSequence = value.Sequence;
            }
            return branch;
        }

        static void AddRelatedGraph(
            HashSet<GraphBranchKey> relatedGraphs,
            RuntimeInstanceKey instance,
            Guid executionBranchId)
        {
            if (instance.GraphRuntimeId != Guid.Empty)
                relatedGraphs.Add(new GraphBranchKey(instance.GraphRuntimeId, executionBranchId));
        }

        static bool MatchesAction(RuntimeInstanceKey instance, RuntimeTraceEvent value)
        {
            if (instance.ActionInstanceId == 0 ||
                value.Payload.ActionInstanceId != instance.ActionInstanceId &&
                value.RuntimeInstance.ActionInstanceId != instance.ActionInstanceId)
            {
                return false;
            }
            if (instance.Kind != RuntimeInstanceKind.SkillExecution)
                return true;
            if (!string.IsNullOrEmpty(instance.CallSiteId) &&
                !string.IsNullOrEmpty(value.Payload.CallSiteId) &&
                !string.Equals(instance.CallSiteId, value.Payload.CallSiteId, StringComparison.Ordinal))
            {
                return false;
            }
            if (instance.InvocationGeneration != 0 &&
                value.RuntimeInstance.InvocationGeneration != 0 &&
                instance.InvocationGeneration != value.RuntimeInstance.InvocationGeneration &&
                instance.InvocationGeneration != value.Payload.GraphInvocationGeneration)
            {
                return false;
            }
            return true;
        }

        static bool IsTimelineFamily(RuntimeInstanceKind kind) =>
            kind == RuntimeInstanceKind.TimelinePlayback ||
            kind == RuntimeInstanceKind.TreeClip;

        static bool MatchesTimelineRuntime(
            RuntimeInstanceKey selected,
            RuntimeInstanceKey candidate)
        {
            if (!IsTimelineFamily(selected.Kind) ||
                candidate.TimelinePlaybackId == 0 ||
                selected.CharacterRuntimeId != candidate.CharacterRuntimeId ||
                selected.SourceOperationIndex != candidate.SourceOperationIndex ||
                selected.TimelinePlaybackId == 0 ||
                selected.TimelinePlaybackId != candidate.TimelinePlaybackId ||
                selected.ActionInstanceId != 0 &&
                candidate.ActionInstanceId != 0 &&
                selected.ActionInstanceId != candidate.ActionInstanceId)
            {
                return false;
            }
            if (selected.Kind == RuntimeInstanceKind.TreeClip && candidate.TreeClipCycle >= 0 &&
                selected.TreeClipCycle != candidate.TreeClipCycle)
                return false;
            if (selected.Kind == RuntimeInstanceKind.TreeClip && candidate.Kind == RuntimeInstanceKind.TreeClip)
                return selected.TreeClipOperationIndex == candidate.TreeClipOperationIndex;
            return true;
        }

        static int CompareEvents(RuntimeTraceEvent left, RuntimeTraceEvent right)
        {
            return left.Sequence.CompareTo(right.Sequence);
        }

        static int CompareGroupedEvents(RuntimeTraceEvent left, RuntimeTraceEvent right)
        {
            int position = left.Position.CompareTo(right.Position);
            if (position != 0)
                return position;
            int branch = left.ExecutionBranchId.CompareTo(right.ExecutionBranchId);
            return branch != 0 ? branch : left.Sequence.CompareTo(right.Sequence);
        }

        readonly struct TickKey : IEquatable<TickKey>, IComparable<TickKey>
        {
            public TickKey(ulong tick, Guid executionBranchId)
            {
                Tick = tick;
                ExecutionBranchId = executionBranchId;
            }

            public ulong Tick { get; }
            public Guid ExecutionBranchId { get; }

            public int CompareTo(TickKey other)
            {
                int tick = Tick.CompareTo(other.Tick);
                return tick != 0 ? tick : ExecutionBranchId.CompareTo(other.ExecutionBranchId);
            }

            public bool Equals(TickKey other) =>
                Tick == other.Tick && ExecutionBranchId == other.ExecutionBranchId;

            public override bool Equals(object obj) => obj is TickKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Tick, ExecutionBranchId);
        }

        readonly struct PresentationFrameKey : IEquatable<PresentationFrameKey>, IComparable<PresentationFrameKey>
        {
            public PresentationFrameKey(ulong frame, Guid executionBranchId)
            {
                Frame = frame;
                ExecutionBranchId = executionBranchId;
            }

            public ulong Frame { get; }
            public Guid ExecutionBranchId { get; }

            public int CompareTo(PresentationFrameKey other)
            {
                int frame = Frame.CompareTo(other.Frame);
                return frame != 0 ? frame : ExecutionBranchId.CompareTo(other.ExecutionBranchId);
            }

            public bool Equals(PresentationFrameKey other) =>
                Frame == other.Frame && ExecutionBranchId == other.ExecutionBranchId;

            public override bool Equals(object obj) => obj is PresentationFrameKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Frame, ExecutionBranchId);
        }

        readonly struct CheckpointKey : IEquatable<CheckpointKey>
        {
            public CheckpointKey(
                ulong tick,
                Guid executionBranchId,
                ulong runtimeEpoch,
                RuntimeContentRevision contentRevision,
                string snapshotIdentity)
            {
                Tick = tick;
                ExecutionBranchId = executionBranchId;
                RuntimeEpoch = runtimeEpoch;
                ContentRevision = contentRevision;
                SnapshotIdentity = snapshotIdentity ?? string.Empty;
            }

            readonly ulong Tick;
            readonly Guid ExecutionBranchId;
            readonly ulong RuntimeEpoch;
            readonly RuntimeContentRevision ContentRevision;
            readonly string SnapshotIdentity;

            public bool Equals(CheckpointKey other) =>
                Tick == other.Tick &&
                ExecutionBranchId == other.ExecutionBranchId &&
                RuntimeEpoch == other.RuntimeEpoch &&
                ContentRevision.Equals(other.ContentRevision) &&
                string.Equals(SnapshotIdentity, other.SnapshotIdentity, StringComparison.Ordinal);
            public override bool Equals(object obj) => obj is CheckpointKey other && Equals(other);
            public override int GetHashCode()
            {
                int hash = HashCode.Combine(Tick, ExecutionBranchId, RuntimeEpoch, ContentRevision);
                return hash * 397 ^ (SnapshotIdentity?.GetHashCode() ?? 0);
            }
        }

        readonly struct GraphBranchKey : IEquatable<GraphBranchKey>
        {
            public GraphBranchKey(Guid graphRuntimeId, Guid executionBranchId)
            {
                GraphRuntimeId = graphRuntimeId;
                ExecutionBranchId = executionBranchId;
            }

            public Guid GraphRuntimeId { get; }
            public Guid ExecutionBranchId { get; }

            public bool Equals(GraphBranchKey other) =>
                GraphRuntimeId == other.GraphRuntimeId && ExecutionBranchId == other.ExecutionBranchId;

            public override bool Equals(object obj) => obj is GraphBranchKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(GraphRuntimeId, ExecutionBranchId);
        }



    }
}
