using System;
using System.Collections.Generic;

namespace BTSMTL.Diagnostics
{
    public interface IRuntimeCaptureArchive : IDisposable
    {
        Guid CaptureId { get; }
        long Version { get; }
        int SegmentCount { get; }
        void Publish(in RuntimeTraceEvent traceEvent);
        RuntimeCaptureSnapshot Freeze(RuntimeTraceChannel channels, RuntimeDiagnosticsCaptureDetail detail);
        long BeginRead(in RuntimeCaptureSnapshot snapshot, in RuntimeCaptureQuery query, long stateVersion);
        bool TryTakeRead(long request, ref RuntimeCaptureReadWindow window,
            (double Logic, double Presentation)? latestTime, out RuntimeCaptureReadStatus status);
        void CancelRead();
    }

    public enum RuntimeCaptureQueryKind
    {
        Time,
        Event,
        Segment,
        Latest,
        LiveProjection
    }

    public enum RuntimeCaptureReadStatus
    {
        Missing,
        Found,
        OutsideTimeRange
    }

    public readonly struct RuntimeCaptureQuery
    {
        public RuntimeCaptureQuery(RuntimeCaptureQueryKind kind, RuntimeTraceDomain domain,
            double timeSeconds, ulong sequence, int historyOffset, double windowStart, double windowEnd,
            bool includeExecutionWindow, Guid? branch = null, ulong? epoch = null)
        {
            Kind = kind;
            Domain = domain;
            TimeSeconds = timeSeconds;
            Sequence = sequence;
            HistoryOffset = historyOffset;
            WindowStart = windowStart;
            WindowEnd = windowEnd;
            IncludeExecutionWindow = includeExecutionWindow;
            Branch = branch;
            Epoch = epoch;
        }

        public RuntimeCaptureQueryKind Kind { get; }
        public RuntimeTraceDomain Domain { get; }
        public double TimeSeconds { get; }
        public ulong Sequence { get; }
        public int HistoryOffset { get; }
        public double WindowStart { get; }
        public double WindowEnd { get; }
        public bool IncludeExecutionWindow { get; }
        public Guid? Branch { get; }
        public ulong? Epoch { get; }
    }

    public sealed class RuntimeCaptureReadWindow
    {
        public const double LiveProjectionDurationSeconds = 30;
        readonly RuntimeLiveStateStore m_State;
        readonly List<RuntimeLiveStateChange> m_StateChanges;
        public RuntimeLiveStateRead State { get; private set; }
        public RuntimeCaptureSpanAccumulator TimelineSpans;
        public long TimelineVersion;
        public RuntimeTraceEvent Selected { get; set; }
        public int HistoryOffset;
        public long EvictedStates;
        public double LatestLogicTime;
        public double LatestPresentationTime;
        public double NextTimeSeconds;

        public RuntimeCaptureReadWindow(int stateCapacity, int eventCapacity)
        {
            m_State = new RuntimeLiveStateStore(stateCapacity);
            m_StateChanges = new List<RuntimeLiveStateChange>(stateCapacity);
            TimelineSpans = new RuntimeCaptureSpanAccumulator(eventCapacity);
        }

        public void Clear()
        {
            m_State.Clear();
            ClearResult();
        }

        public void ClearResult()
        {
            m_StateChanges.Clear();
            State = default;
            TimelineSpans.Reset();
            TimelineVersion = 0;
            EvictedStates = 0;
            Selected = default;
            HistoryOffset = 0;
            LatestLogicTime = 0;
            LatestPresentationTime = 0;
            NextTimeSeconds = double.PositiveInfinity;
        }

        public void ApplyState(in RuntimeTraceEvent traceEvent) => m_State.Upsert(in traceEvent);

        public void RestoreState(ReadOnlySpan<RuntimeTraceEvent> states) => m_State.Restore(states);

        public RuntimeCaptureReadStatus GetReadStatus(bool found, (double Logic, double Presentation)? latestTime)
        {
            if (!found)
                return RuntimeCaptureReadStatus.Missing;
            if (latestTime is { } limit && Selected.TimeSeconds >
                (Selected.Domain == RuntimeTraceDomain.Presentation ? limit.Presentation : limit.Logic))
                return RuntimeCaptureReadStatus.OutsideTimeRange;
            return RuntimeCaptureReadStatus.Found;
        }

        public void CopyStateSince(long cursor, RuntimeCaptureReadWindow destination, long baseEvictions)
        {
            destination.State = m_State.CopySince(cursor, destination.m_StateChanges);
            destination.EvictedStates = baseEvictions + destination.State.EvictedStates;
            if (!destination.State.RequiresFullSync)
                return;
            int count = destination.m_StateChanges.Count;
            for (int root = count / 2 - 1; root >= 0; root--)
                destination.SiftState(root, count);
            for (int end = count - 1; end > 0; end--)
            {
                RuntimeLiveStateChange value = destination.m_StateChanges[0];
                destination.m_StateChanges[0] = destination.m_StateChanges[end];
                destination.m_StateChanges[end] = value;
                destination.SiftState(0, end);
            }
        }

        void SiftState(int root, int count)
        {
            RuntimeLiveStateChange value = m_StateChanges[root];
            for (int child = root * 2 + 1; child < count; child = root * 2 + 1)
            {
                if (child + 1 < count && m_StateChanges[child].TraceEvent.Sequence < m_StateChanges[child + 1].TraceEvent.Sequence)
                    child++;
                if (value.TraceEvent.Sequence >= m_StateChanges[child].TraceEvent.Sequence)
                    break;
                m_StateChanges[root] = m_StateChanges[child];
                root = child;
            }
            m_StateChanges[root] = value;
        }

        public long CopyStateTo(List<RuntimeTraceEvent> destination) => m_State.CopyCurrent(destination);
    }
    public enum RuntimeCaptureSpanState { Open, Completed, MissingStart }

    public enum RuntimeCaptureSpanSlot { ClipSample, OpenTree }

    public readonly struct RuntimeCaptureSpanKey : IEquatable<RuntimeCaptureSpanKey>
    {
        public RuntimeCaptureSpanKey(in RuntimeTraceEvent value)
        {
            Source = value.Source;
            RuntimeInstanceKey instance = value.RuntimeInstance;
            Instance = value.Kind is RuntimeTraceEventKind.StateScopeEntered or RuntimeTraceEventKind.StateScopeExited
                ? new RuntimeInstanceKey(RuntimeInstanceKind.StateActivation, instance.CharacterRuntimeId,
                    instance.GraphRuntimeId, instance.StateId, value.Payload.ActivationGeneration, 0, -1,
                    value.Payload.ActionInstanceId, instance.CallSiteId, instance.InvocationGeneration)
                : instance;
            ExecutionBranchId = value.ExecutionBranchId;
            RuntimeEpoch = value.RuntimeEpoch;
            ContentRevision = value.ContentRevision;
            Domain = value.Domain == RuntimeTraceDomain.Lifecycle ? RuntimeTraceDomain.Logic : value.Domain;
        }

        public readonly RuntimeSourceElementHandle Source;
        public readonly RuntimeInstanceKey Instance;
        public readonly Guid ExecutionBranchId;
        public readonly ulong RuntimeEpoch;
        public readonly RuntimeContentRevision ContentRevision;
        public readonly RuntimeTraceDomain Domain;

        public bool Equals(RuntimeCaptureSpanKey other) =>
            Domain == other.Domain && Source.Equals(other.Source) && Instance.Equals(other.Instance) &&
            ExecutionBranchId == other.ExecutionBranchId && RuntimeEpoch == other.RuntimeEpoch &&
            ContentRevision.Equals(other.ContentRevision);
        public override bool Equals(object obj) => obj is RuntimeCaptureSpanKey other && Equals(other);
        public override int GetHashCode()
        {
            int hash = HashCode.Combine((int)Source.Kind, (int)Domain, Source, Instance, ExecutionBranchId);
            hash = hash * 397 + RuntimeEpoch.GetHashCode();
            return hash * 397 + ContentRevision.GetHashCode();
        }
    }

    public readonly struct RuntimeCaptureSpanToken
    {
        public RuntimeCaptureSpanToken(long location, ulong endPosition, int endCycle)
        {
            Location = location;
            EndPosition = endPosition;
            EndCycle = endCycle;
        }

        public readonly long Location;
        public readonly ulong EndPosition;
        public readonly int EndCycle;
    }

    public interface IRuntimeCaptureSpanStorage
    {
        bool TryGetCurrent(in RuntimeCaptureSpanKey key, RuntimeCaptureSpanSlot slot, out RuntimeCaptureSpanToken token);
        long Create(in RuntimeTraceEvent value, int sourceGroup, RuntimeCaptureSpanState state);
        void SetEnd(long location, in RuntimeTraceEvent value, RuntimeCaptureSpanState state);
        void Complete(long location, in RuntimeTraceEvent cause);
        void SetCurrent(in RuntimeCaptureSpanKey key, RuntimeCaptureSpanSlot slot, in RuntimeCaptureSpanToken current);
        void RemoveCurrent(in RuntimeCaptureSpanKey key, RuntimeCaptureSpanSlot slot);
    }

    public static class RuntimeCaptureSpanTransitions
    {
        public static long Append<TStorage>(TStorage storage, in RuntimeTraceEvent value, int sourceGroup)
            where TStorage : class, IRuntimeCaptureSpanStorage
        {
            if (value.Kind is RuntimeTraceEventKind.StateTransitionSelected or RuntimeTraceEventKind.TimelineSectionJumped or
                RuntimeTraceEventKind.TimelineLogicTime or RuntimeTraceEventKind.TimelineVisualTime or
                RuntimeTraceEventKind.TimelineStarted or RuntimeTraceEventKind.TimelineCompleted or
                RuntimeTraceEventKind.TimelineCancelled or RuntimeTraceEventKind.TimelineStopped)
                return storage.Create(in value, sourceGroup, RuntimeCaptureSpanState.Completed);
            var key = new RuntimeCaptureSpanKey(in value);
            if (value.Kind == RuntimeTraceEventKind.ClipActive)
            {
                if (storage.TryGetCurrent(in key, RuntimeCaptureSpanSlot.ClipSample, out RuntimeCaptureSpanToken sample) &&
                    value.Position <= sample.EndPosition + 1 && value.GetCycle() == sample.EndCycle)
                {
                    storage.SetEnd(sample.Location, in value, RuntimeCaptureSpanState.Completed);
                    return -1;
                }
                long location = storage.Create(in value, sourceGroup, RuntimeCaptureSpanState.Completed);
                var current = new RuntimeCaptureSpanToken(location, value.Position, value.GetCycle());
                storage.SetCurrent(in key, RuntimeCaptureSpanSlot.ClipSample, in current);
                return location;
            }
            if (value.Kind is RuntimeTraceEventKind.TreeClipEntered or RuntimeTraceEventKind.StateScopeEntered)
            {
                if (storage.TryGetCurrent(in key, RuntimeCaptureSpanSlot.OpenTree, out RuntimeCaptureSpanToken previous))
                {
                    storage.Complete(previous.Location, in value);
                    storage.RemoveCurrent(in key, RuntimeCaptureSpanSlot.OpenTree);
                }
                long location = storage.Create(in value, sourceGroup, RuntimeCaptureSpanState.Open);
                var current = new RuntimeCaptureSpanToken(location, value.Position, value.GetCycle());
                storage.SetCurrent(in key, RuntimeCaptureSpanSlot.OpenTree, in current);
                return location;
            }
            if (storage.TryGetCurrent(in key, RuntimeCaptureSpanSlot.OpenTree, out RuntimeCaptureSpanToken active))
            {
                bool completed = value.Kind is RuntimeTraceEventKind.TreeClipExited or RuntimeTraceEventKind.TreeClipDestroyed or
                    RuntimeTraceEventKind.StateScopeExited;
                storage.SetEnd(active.Location, in value,
                    completed ? RuntimeCaptureSpanState.Completed : RuntimeCaptureSpanState.Open);
                if (completed)
                    storage.RemoveCurrent(in key, RuntimeCaptureSpanSlot.OpenTree);
            }
            else if (value.Kind is RuntimeTraceEventKind.TreeClipExited or RuntimeTraceEventKind.StateScopeExited)
                return storage.Create(in value, sourceGroup, RuntimeCaptureSpanState.MissingStart);
            return -1;
        }
    }

    public struct RuntimeCaptureSpanData
    {
        public RuntimeTraceEvent Start;
        public RuntimeTraceEvent End;
        public RuntimeCaptureSpanState State;
        public int SourceGroup;
    }

    public struct RuntimeCaptureSpanSource
    {
        public RuntimeSourceElementHandle Handle;
        public RuntimeContentRevision Revision;
        public int EventCount;
    }

    public sealed class RuntimeCaptureSpanAccumulator : IRuntimeCaptureSpanStorage
    {
        const int SpansPerPage = 256;
        readonly List<RuntimeCaptureSpanData[]> m_Pages;
        readonly Dictionary<RuntimeCaptureSpanKey, int> m_Open;
        readonly Dictionary<RuntimeCaptureSpanKey, int> m_ClipSamples;
        readonly Dictionary<(RuntimeSourceElementHandle, RuntimeContentRevision), int> m_SourceIndices;
        readonly List<RuntimeCaptureSpanSource> m_Sources;
        public readonly Dictionary<(RuntimeTraceDomain, Guid, ulong), double> ClockTimes;
        int m_Count;
        int m_MissingStartCount;
        int m_SamplingLimitCount;
        public int Count => m_Count;
        public int SourceCount => m_Sources.Count;
        public int SamplingLimitCount => m_SamplingLimitCount;
        public double LatestLogicTimeSeconds { get; private set; }
        public double LatestPresentationTimeSeconds { get; private set; }

        public RuntimeCaptureSpanAccumulator(int capacity) : this(capacity, capacity)
        {
        }

        public RuntimeCaptureSpanAccumulator(int capacity, int sourceCapacity)
        {
            int pages = (capacity + SpansPerPage - 1) / SpansPerPage;
            m_Pages = new List<RuntimeCaptureSpanData[]>(pages);
            for (int i = 0; i < pages; i++)
                m_Pages.Add(new RuntimeCaptureSpanData[SpansPerPage]);
            m_Open = new Dictionary<RuntimeCaptureSpanKey, int>(capacity);
            m_ClipSamples = new Dictionary<RuntimeCaptureSpanKey, int>(capacity);
            m_SourceIndices = new Dictionary<(RuntimeSourceElementHandle, RuntimeContentRevision), int>(sourceCapacity);
            m_Sources = new List<RuntimeCaptureSpanSource>(sourceCapacity);
            ClockTimes = new Dictionary<(RuntimeTraceDomain, Guid, ulong), double>(sourceCapacity);
        }

        public ref readonly RuntimeCaptureSpanData GetSpan(int index) => ref Writable(index);
        ref RuntimeCaptureSpanData Writable(int index) => ref m_Pages[index / SpansPerPage][index % SpansPerPage];
        public RuntimeCaptureSpanSource GetSource(int index) => m_Sources[index];

        public void ImportSource(in RuntimeCaptureSpanSource source)
        {
            m_SourceIndices.Add((source.Handle, source.Revision), m_Sources.Count);
            m_Sources.Add(source);
        }

        public void ImportClock(RuntimeTraceDomain domain, Guid branch, ulong epoch, double time) =>
            ClockTimes.Add((domain, branch, epoch), time);

        public void ImportMetadata(int samplingLimitCount, double latestLogic, double latestPresentation)
        {
            m_SamplingLimitCount = samplingLimitCount;
            LatestLogicTimeSeconds = latestLogic;
            LatestPresentationTimeSeconds = latestPresentation;
        }

        public void ImportSpan(in RuntimeTraceEvent start, in RuntimeTraceEvent end, RuntimeCaptureSpanState state)
        {
            int index = AddSpan(in start, m_SourceIndices[(start.Source, start.ContentRevision)], state);
            Writable(index).End = end;
            IndexSpan(index);
        }

        public void Reset()
        {
            ClearSpans(0, m_Count);
            m_Open.Clear();
            m_ClipSamples.Clear();
            m_SourceIndices.Clear();
            m_Sources.Clear();
            m_Count = 0;
            ClockTimes.Clear();
            m_MissingStartCount = 0;
            m_SamplingLimitCount = 0;
            LatestLogicTimeSeconds = 0;
            LatestPresentationTimeSeconds = 0;
        }

        public bool IsComplete(long evictedEvents, int displayedSpans, int unmappedEvents) =>
            evictedEvents == 0 && displayedSpans != 0 && unmappedEvents == 0 &&
            m_MissingStartCount == 0 && m_SamplingLimitCount == 0 && m_Open.Count == 0;

        public int AppendMetadata(in RuntimeTraceEvent value)
        {
            RuntimeTraceDomain domain = value.Domain == RuntimeTraceDomain.Lifecycle ? RuntimeTraceDomain.Logic : value.Domain;
            SetClock(domain, value.ExecutionBranchId, value.RuntimeEpoch, value.TimeSeconds);

            if (value.Kind is RuntimeTraceEventKind.TraceSamplingLimited or RuntimeTraceEventKind.ValueSamplingLimited)
                m_SamplingLimitCount++;
            bool clip = value.Kind is RuntimeTraceEventKind.ClipActive or RuntimeTraceEventKind.TreeClipEntered or
                RuntimeTraceEventKind.TreeClipUpdated or RuntimeTraceEventKind.TreeClipExited or RuntimeTraceEventKind.TreeClipDestroyed;
            bool execution = value.Kind is RuntimeTraceEventKind.StateScopeEntered or RuntimeTraceEventKind.StateScopeExited or
                RuntimeTraceEventKind.StateTransitionSelected or RuntimeTraceEventKind.TimelineSectionJumped or
                RuntimeTraceEventKind.TimelineLogicTime or RuntimeTraceEventKind.TimelineVisualTime or
                RuntimeTraceEventKind.TimelineStarted or RuntimeTraceEventKind.TimelineCompleted or
                RuntimeTraceEventKind.TimelineCancelled or RuntimeTraceEventKind.TimelineStopped;
            if (!execution && (!clip ||
                value.Source.Kind is not RuntimeSourceElementKind.Clip and not RuntimeSourceElementKind.TreeClip))
                return -1;
            var key = (value.Source, value.ContentRevision);
            if (!m_SourceIndices.TryGetValue(key, out int index))
            {
                index = m_Sources.Count;
                m_SourceIndices.Add(key, index);
                m_Sources.Add(new RuntimeCaptureSpanSource { Handle = value.Source, Revision = value.ContentRevision });
            }
            RuntimeCaptureSpanSource source = m_Sources[index];
            source.EventCount++;
            m_Sources[index] = source;
            return index;
        }

        public void Append(in RuntimeTraceEvent value)
        {
            int source = AppendMetadata(in value);
            if (source >= 0)
                AppendTrack(in value, source);
        }

        public long AppendTrack(in RuntimeTraceEvent value, int source) =>
            RuntimeCaptureSpanTransitions.Append(this, in value, source);

        bool IRuntimeCaptureSpanStorage.TryGetCurrent(in RuntimeCaptureSpanKey key, RuntimeCaptureSpanSlot slot,
            out RuntimeCaptureSpanToken token)
        {
            Dictionary<RuntimeCaptureSpanKey, int> entries = slot == RuntimeCaptureSpanSlot.ClipSample ? m_ClipSamples : m_Open;
            if (entries.TryGetValue(key, out int index))
            {
                ref readonly RuntimeCaptureSpanData span = ref GetSpan(index);
                token = new RuntimeCaptureSpanToken(index, span.End.Position, span.End.GetCycle());
                return true;
            }
            token = default;
            return false;
        }

        long IRuntimeCaptureSpanStorage.Create(in RuntimeTraceEvent value, int source, RuntimeCaptureSpanState state)
        {
            if (state == RuntimeCaptureSpanState.MissingStart)
                m_MissingStartCount++;
            return AddSpan(in value, source, state);
        }

        void IRuntimeCaptureSpanStorage.SetEnd(long location, in RuntimeTraceEvent value, RuntimeCaptureSpanState state)
        {
            ref RuntimeCaptureSpanData span = ref Writable((int)location);
            span.End = value;
            span.State = state;
        }

        void IRuntimeCaptureSpanStorage.Complete(long location, in RuntimeTraceEvent cause) =>
            Writable((int)location).State = RuntimeCaptureSpanState.Completed;

        void IRuntimeCaptureSpanStorage.SetCurrent(in RuntimeCaptureSpanKey key, RuntimeCaptureSpanSlot slot,
            in RuntimeCaptureSpanToken current)
        {
            if (slot == RuntimeCaptureSpanSlot.ClipSample)
                m_ClipSamples[key] = (int)current.Location;
            else
                m_Open.Add(key, (int)current.Location);
        }

        void IRuntimeCaptureSpanStorage.RemoveCurrent(in RuntimeCaptureSpanKey key, RuntimeCaptureSpanSlot slot)
        {
            if (slot == RuntimeCaptureSpanSlot.ClipSample)
                m_ClipSamples.Remove(key);
            else
                m_Open.Remove(key);
        }

        void SetClock(RuntimeTraceDomain domain, Guid branch, ulong epoch, double time)
        {
            ClockTimes[(domain, branch, epoch)] = time;
            if (domain == RuntimeTraceDomain.Presentation)
                LatestPresentationTimeSeconds = time;
            else
                LatestLogicTimeSeconds = time;
        }

        void ClearSpans(int start, int count)
        {
            while (count > 0)
            {
                int first = start % SpansPerPage;
                int length = Math.Min(count, SpansPerPage - first);
                RuntimeCaptureSpanData[] page = m_Pages[start / SpansPerPage];
                Array.Clear(page, first, length);
                start += length;
                count -= length;
            }
        }

        int AddSpan(in RuntimeTraceEvent value, int source, RuntimeCaptureSpanState state)
        {
            int index = m_Count;
            if (index / SpansPerPage == m_Pages.Count)
                m_Pages.Add(new RuntimeCaptureSpanData[SpansPerPage]);
            ref RuntimeCaptureSpanData span = ref Writable(index);
            span.Start = value;
            span.End = value;
            span.SourceGroup = source;
            span.State = state;
            m_Count++;
            return index;
        }

        void RebuildIndices()
        {
            m_Open.Clear();
            m_ClipSamples.Clear();
            m_MissingStartCount = 0;
            for (int i = 0; i < m_Count; i++)
                IndexSpan(i);
        }

        void IndexSpan(int index)
        {
            ref readonly RuntimeCaptureSpanData span = ref GetSpan(index);
            var key = new RuntimeCaptureSpanKey(in span.Start);
            if (span.State == RuntimeCaptureSpanState.Open)
                m_Open.Add(key, index);
            if (span.Start.Kind == RuntimeTraceEventKind.ClipActive)
                m_ClipSamples[key] = index;
            if (span.State == RuntimeCaptureSpanState.MissingStart)
                m_MissingStartCount++;
        }

        public bool TrimBefore(double logicTime, double presentationTime)
        {
            int write = 0;
            for (int i = 0; i < m_Count; i++)
            {
                ref readonly RuntimeCaptureSpanData span = ref GetSpan(i);
                double first = span.Start.Domain == RuntimeTraceDomain.Presentation ? presentationTime : logicTime;
                if (span.State == RuntimeCaptureSpanState.Open || span.End.TimeSeconds >= first)
                {
                    CopySpan(write, i);
                    write++;
                }
            }
            if (write == m_Count)
                return false;
            ClearSpans(write, m_Count - write);
            m_Count = write;
            RebuildIndices();
            return true;
        }

        void CopySpan(int destination, int source)
        {
            ref readonly RuntimeCaptureSpanData value = ref GetSpan(source);
            ref RuntimeCaptureSpanData target = ref Writable(destination);
            target = value;
        }

        public void KeepSpans(IReadOnlyList<int> indices)
        {
            for (int i = 0; i < indices.Count; i++)
                if (indices[i] != i)
                    CopySpan(i, indices[i]);
            ClearSpans(indices.Count, m_Count - indices.Count);
            m_Count = indices.Count;
            RebuildIndices();
        }

    }

}
