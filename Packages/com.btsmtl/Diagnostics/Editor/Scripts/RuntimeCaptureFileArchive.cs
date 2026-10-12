using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using Unity.Profiling;

namespace BTSMTL.Diagnostics.Editor
{
    public sealed class RuntimeCaptureFileArchive : IRuntimeCaptureArchive
    {
        const int EventsPerPage = 1024;
        const int PageCount = 8;
        const int StateCapacity = 4096;
        const int CheckpointPageInterval = 16;
        const int ReadChunkSize = 128;
        const int PageIndexSize = 60;
        enum ReadResult { Found, NotFound, Cancelled }
        static readonly ProfilerMarker s_Write = new("Workbench.EventHistory.WritePage");
        static readonly ProfilerMarker s_Read = new("Workbench.EventHistory.ReadWindow");
        readonly object m_Gate = new();
        readonly RuntimeTraceEvent[][] m_Pages = new RuntimeTraceEvent[PageCount][];
        readonly int[] m_Counts = new int[PageCount];
        readonly Queue<int> m_Free = new(PageCount);
        readonly Queue<int> m_Pending = new(PageCount);
        readonly AutoResetEvent m_Wake = new(false);
        readonly string m_Directory;
        readonly Dictionary<(RuntimeTraceDomain, Guid, ulong), TimeIndex> m_Times = new();
        readonly Queue<TimeIndex> m_UnusedTimes = new();
        readonly RuntimeCaptureReadWindow m_State = new(StateCapacity, 0);
        readonly RuntimeCaptureReadWindow m_ReadState = new(StateCapacity, 0);
        readonly List<RuntimeTraceEvent> m_StateScratch = new(StateCapacity);
        readonly RuntimeTraceEvent[] m_ReadPages = new RuntimeTraceEvent[CheckpointPageInterval * EventsPerPage];
        readonly long[] m_ReadPageFirst = new long[CheckpointPageInterval];
        readonly int[] m_ReadPageCounts = new int[CheckpointPageInterval];
        readonly RuntimeTraceEvent[][] m_PageStates = new RuntimeTraceEvent[CheckpointPageInterval][];
        readonly long[] m_PageStateFirst = new long[CheckpointPageInterval];
        readonly int[] m_PageStateCounts = new int[CheckpointPageInterval];
        readonly long[] m_PageStateEvictions = new long[CheckpointPageInterval];
        readonly RuntimeTraceEvent[] m_CheckpointState = new RuntimeTraceEvent[StateCapacity];
        readonly RuntimeCaptureSpanAccumulator m_Metadata = new(0, StateCapacity);
        RuntimeCaptureSpanFileIndex m_SpanIndex;
        RuntimeCaptureReadWindow m_Result = new(StateCapacity, 32768);
        int m_Current;
        int m_Count;
        bool m_Stop;
        bool m_Finished;
        bool m_ResetRequested;
        bool m_ResetComplete = true;
        long m_QueryVersion;
        long m_QueryStateVersion;
        long m_ResultVersion;
        bool m_Found;
        RuntimeCaptureSnapshot m_QuerySnapshot;
        RuntimeCaptureQuery m_Query;
        ExceptionDispatchInfo m_Failure;
        RuntimeTraceDomain m_LastDomain;
        ulong m_LastPosition;
        double m_LogicTime;
        double m_PresentationTime;
        RuntimeTraceChannel m_Channels;
        RuntimeDiagnosticsCaptureDetail m_Detail;
        BinaryWriter m_Events;
        BinaryWriter m_PageIndex;
        BinaryWriter m_Segments;
        BinaryWriter m_Checkpoints;
        BinaryReader m_EventReader;
        BinaryReader m_PageReader;
        BinaryReader m_SegmentReader;
        BinaryReader m_CheckpointReader;
        BinaryReader m_SymbolReader;
        RuntimeTraceBinaryCodec m_WriterCodec;
        RuntimeTraceBinaryCodec m_ReaderCodec;
        long m_Written;
        long m_WrittenPages;
        int m_WrittenSegments;
        RuntimeTraceDomain m_WrittenDomain;
        ulong m_WrittenPosition;
        Page m_Checkpoint;
        int m_ReadSlot;
        long m_CheckpointStateFirst = -1;
        long m_ReadStateOrdinal = -1;
        long m_ReadStateBaseEvictions;

        public RuntimeCaptureFileArchive()
        {
            CaptureId = Guid.NewGuid();
            m_Directory = Path.GetFullPath(Path.Combine("Library", "WorkbenchPreviewHistory", CaptureId.ToString("N")));
            Directory.CreateDirectory(m_Directory);
            Array.Fill(m_PageStates, Array.Empty<RuntimeTraceEvent>());
            for (int i = 0; i < PageCount; i++)
            {
                m_Pages[i] = new RuntimeTraceEvent[EventsPerPage];
                m_Free.Enqueue(i);
            }
            m_Current = m_Free.Dequeue();
            new Thread(Run) { IsBackground = true, Name = "Workbench event history" }.Start();
        }

        public Guid CaptureId { get; private set; }
        public long Version { get; private set; }
        public int SegmentCount { get; private set; }
        public string DirectoryPath => m_Directory;
        public long BytesWritten { get; private set; }
        public int MaximumPendingPages { get; private set; }
        public double LastReadMilliseconds { get; private set; }
        internal long WrittenEventCount => m_Written;

        public void RequestReset()
        {
            CancelRead();
            Seal();
            lock (m_Gate)
            {
                m_ResetComplete = false;
                m_ResetRequested = true;
                m_Wake.Set();
            }
        }

        public bool ResetComplete
        {
            get
            {
                Volatile.Read(ref m_Failure)?.Throw();
                lock (m_Gate) return m_ResetComplete;
            }
        }

        public void Publish(in RuntimeTraceEvent traceEvent)
        {
            Volatile.Read(ref m_Failure)?.Throw();
            m_Channels |= traceEvent.Channel;
            if (Version == 0 || m_LastDomain != traceEvent.Domain || m_LastPosition != traceEvent.Position)
                SegmentCount++;
            m_LastDomain = traceEvent.Domain;
            m_LastPosition = traceEvent.Position;
            if (traceEvent.Domain == RuntimeTraceDomain.Presentation)
                m_PresentationTime = traceEvent.TimeSeconds;
            else
                m_LogicTime = traceEvent.TimeSeconds;
            traceEvent.Retain();
            m_Pages[m_Current][m_Count++] = traceEvent;
            Version++;
            if (m_Count == EventsPerPage)
                Seal();
        }

        void Seal()
        {
            if (m_Count == 0)
                return;
            lock (m_Gate)
            {
                m_Counts[m_Current] = m_Count;
                m_Pending.Enqueue(m_Current);
                m_Count = 0;
                MaximumPendingPages = Math.Max(MaximumPendingPages, m_Pending.Count);
                m_Wake.Set();
                if (m_Free.Count == 0)
                {
                    var exception = new IOException("事件历史写入未跟上预览，已停止预览；尚未写入的事件没有被覆盖。");
                    Volatile.Write(ref m_Failure, ExceptionDispatchInfo.Capture(exception));
                    throw exception;
                }
                m_Current = m_Free.Dequeue();
            }
        }

        public RuntimeCaptureSnapshot Freeze(RuntimeTraceChannel channels, RuntimeDiagnosticsCaptureDetail detail)
        {
            Seal();
            if (detail > m_Detail) m_Detail = detail;
            return new RuntimeCaptureSnapshot(this, CaptureId, channels | m_Channels, m_Detail,
                Version, SegmentCount, m_LogicTime, m_PresentationTime);
        }

        public long BeginRead(in RuntimeCaptureSnapshot snapshot, in RuntimeCaptureQuery query, long stateVersion)
        {
            Volatile.Read(ref m_Failure)?.Throw();
            lock (m_Gate)
            {
                m_QuerySnapshot = snapshot;
                m_Query = query;
                m_QueryStateVersion = stateVersion;
                m_QueryVersion++;
                m_Wake.Set();
                return m_QueryVersion;
            }
        }

        public bool TryTakeRead(long request, ref RuntimeCaptureReadWindow result,
            (double Logic, double Presentation)? latestTime, out RuntimeCaptureReadStatus status)
        {
            Volatile.Read(ref m_Failure)?.Throw();
            lock (m_Gate)
            {
                status = RuntimeCaptureReadStatus.Missing;
                if (request != m_QueryVersion || request != m_ResultVersion)
                    return false;
                status = m_Result.GetReadStatus(m_Found, latestTime);
                if (status == RuntimeCaptureReadStatus.Found)
                    (result, m_Result) = (m_Result, result);
                m_ResultVersion = 0;
                m_QuerySnapshot = default;
                return true;
            }
        }

        public void CancelRead()
        {
            lock (m_Gate)
            {
                m_QueryVersion++;
                m_QuerySnapshot = default;
                if (!m_Finished) m_Wake.Set();
            }
        }

        void Run()
        {
            UnityEngine.Profiling.Profiler.BeginThreadProfiling("Workbench", "Event History");
            try
            {
                RunFiles();
            }
            catch (Exception error) { Volatile.Write(ref m_Failure, ExceptionDispatchInfo.Capture(error)); }
            finally
            {
                for (int page = 0; page < m_Pages.Length; page++)
                    ClearEvents(m_Pages[page]);
                ClearEvents(m_ReadPages);
                ClearPageStates();
                ClearEvents(m_CheckpointState);
                for (int i = 0; i < m_StateScratch.Count; i++)
                    m_StateScratch[i].Release();
                m_StateScratch.Clear();
                m_State.Clear();
                m_ReadState.Clear();
                m_Result.Clear();
                m_Metadata.Reset();
                lock (m_Gate) { m_Finished = true; m_Wake.Dispose(); }
                UnityEngine.Profiling.Profiler.EndThreadProfiling();
            }
        }

        FileStream Open(string name) => new(Path.Combine(m_Directory, name), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 65536);

        void RunFiles()
        {
            try
            {
                using var events = Open("events.bin");
                using var pages = Open("pages.idx");
                using var segments = Open("segments.idx");
                using var checkpoints = Open("states.bin");
                using var symbols = Open("strings.bin");
                using var spanIndex = new RuntimeCaptureSpanFileIndex(this, m_Directory);
                using var writeCodec = new RuntimeTraceBinaryCodec(symbols, StateCapacity * 3);
                using var readCodec = new RuntimeTraceBinaryCodec(Stream.Null, StateCapacity * 3);
                using var eventWriter = new BinaryWriter(events, System.Text.Encoding.UTF8, true);
                using var pageWriter = new BinaryWriter(pages, System.Text.Encoding.UTF8, true);
                using var segmentWriter = new BinaryWriter(segments, System.Text.Encoding.UTF8, true);
                using var checkpointWriter = new BinaryWriter(checkpoints, System.Text.Encoding.UTF8, true);
                using var eventReader = new BinaryReader(events, System.Text.Encoding.UTF8, true);
                using var pageReader = new BinaryReader(pages, System.Text.Encoding.UTF8, true);
                using var segmentReader = new BinaryReader(segments, System.Text.Encoding.UTF8, true);
                using var checkpointReader = new BinaryReader(checkpoints, System.Text.Encoding.UTF8, true);
                using var symbolReader = new BinaryReader(symbols, System.Text.Encoding.UTF8, true);
                m_Events = eventWriter; m_PageIndex = pageWriter; m_Segments = segmentWriter;
                m_Checkpoints = checkpointWriter; m_EventReader = eventReader; m_PageReader = pageReader;
                m_SegmentReader = segmentReader; m_CheckpointReader = checkpointReader; m_SymbolReader = symbolReader;
                m_WriterCodec = writeCodec; m_ReaderCodec = readCodec;
                m_SpanIndex = spanIndex;
                while (true)
                {
                    int page = -1;
                    long request = 0;
                    long stateVersion = 0;
                    RuntimeCaptureSnapshot snapshot = default;
                    RuntimeCaptureQuery query = default;
                    bool reset = false;
                    lock (m_Gate)
                    {
                        if (m_Stop) break;
                        if (m_Pending.Count != 0) page = m_Pending.Dequeue();
                        else if (m_ResetRequested) reset = true;
                        else if (m_QuerySnapshot.IsValid && m_ResultVersion != m_QueryVersion)
                        { request = m_QueryVersion; snapshot = m_QuerySnapshot; query = m_Query; stateVersion = m_QueryStateVersion; }
                    }
                    if (page >= 0)
                    {
                        WritePage(page);
                        ClearEvents(m_Pages[page].AsSpan(0, m_Counts[page]));
                        lock (m_Gate) m_Free.Enqueue(page);
                    }
                    else if (reset)
                    {
                        ResetFiles();
                        lock (m_Gate)
                        {
                            CaptureId = Guid.NewGuid();
                            Version = SegmentCount = 0;
                            m_LogicTime = m_PresentationTime = 0;
                            m_Channels = RuntimeTraceChannel.None;
                            m_Detail = RuntimeDiagnosticsCaptureDetail.None;
                            m_ResetRequested = false;
                            m_ResetComplete = true;
                        }
                    }
                    else if (request != 0)
                    {
                        using var profile = s_Read.Auto();
                        long started = Stopwatch.GetTimestamp();
                        ReadResult result = ReadQuery(snapshot, in query, request, stateVersion);
                        LastReadMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
                        if (result == ReadResult.Cancelled)
                            continue;
                        if (result == ReadResult.NotFound)
                            m_Result.ClearResult();
                        lock (m_Gate)
                        {
                            if (m_Stop || request != m_QueryVersion)
                                continue;
                            m_Found = result == ReadResult.Found;
                            m_ResultVersion = request;
                        }
                    }
                    else m_Wake.WaitOne();
                }
            }
            finally
            {
                foreach (TimeIndex index in m_Times.Values) index.Dispose();
                foreach (TimeIndex index in m_UnusedTimes) index.Dispose();
                Directory.Delete(m_Directory, true);
            }
        }

        void ResetFiles()
        {
            m_Events.BaseStream.SetLength(0);
            m_PageIndex.BaseStream.SetLength(0);
            m_Segments.BaseStream.SetLength(0);
            m_Checkpoints.BaseStream.SetLength(0);
            m_SymbolReader.BaseStream.SetLength(0);
            m_WriterCodec.Reset();
            m_ReaderCodec.Reset();
            m_SpanIndex.Reset();
            foreach (TimeIndex index in m_Times.Values)
            {
                index.Reset();
                m_UnusedTimes.Enqueue(index);
            }
            m_Times.Clear();
            ClearEvents(m_ReadPages);
            ClearPageStates();
            ClearEvents(m_CheckpointState);
            for (int i = 0; i < m_StateScratch.Count; i++) m_StateScratch[i].Release();
            m_StateScratch.Clear();
            m_State.Clear();
            m_ReadState.Clear();
            m_Result.Clear();
            m_Metadata.Reset();
            Array.Clear(m_ReadPageCounts, 0, m_ReadPageCounts.Length);
            m_Written = m_WrittenPages = 0;
            m_WrittenSegments = m_ReadSlot = 0;
            m_Checkpoint = default;
            m_CheckpointStateFirst = m_ReadStateOrdinal = -1;
            m_ReadStateBaseEvictions = 0;
            BytesWritten = 0;
        }

        void WritePage(int pageId)
        {
            using var profile = s_Write.Auto();
            CachePageState(m_WrittenPages, m_Written, m_State, 0);
            if (m_WrittenPages % CheckpointPageInterval == 0)
                WriteCheckpoint();
            Page page = m_Checkpoint;
            page.First = m_Written;
            page.Count = m_Counts[pageId];
            page.SymbolOffset = m_WriterCodec.BeginPage();
            m_Events.BaseStream.Position = m_Written * RuntimeTraceBinaryCodec.EventSize;
            m_Segments.BaseStream.Position = m_WrittenSegments * sizeof(long);
            for (int i = 0; i < page.Count; i++)
            {
                ref readonly RuntimeTraceEvent value = ref m_Pages[pageId][i];
                if (m_Written == 0 || value.Domain != m_WrittenDomain || value.Position != m_WrittenPosition)
                {
                    m_Segments.Write(m_Written);
                    m_WrittenSegments++;
                }
                m_WrittenDomain = value.Domain; m_WrittenPosition = value.Position;
                var key = (Normalize(value.Domain), value.ExecutionBranchId, value.RuntimeEpoch);
                if (!m_Times.TryGetValue(key, out TimeIndex index))
                {
                    index = m_UnusedTimes.Count != 0 ? m_UnusedTimes.Dequeue() :
                        new TimeIndex(Path.Combine(m_Directory, $"time-{m_Times.Count}.idx"));
                    m_Times.Add(key, index);
                }
                index.Append(value.TimeSeconds, m_Written);
                m_WriterCodec.Write(m_Events, in value);
                m_State.ApplyState(in value);
                int source = m_Metadata.AppendMetadata(in value);
                if (source >= 0)
                    m_SpanIndex.Append(in value, source);
                m_Written++;
            }
            page.SymbolCount = m_WriterCodec.SymbolCount;
            int readSlot = (int)(m_WrittenPages % CheckpointPageInterval);
            Span<RuntimeTraceEvent> readEvents = m_ReadPages.AsSpan(readSlot * EventsPerPage, EventsPerPage);
            ClearEvents(readEvents);
            CopyEvents(m_Pages[pageId].AsSpan(0, page.Count), readEvents);
            m_ReadPageFirst[readSlot] = page.First;
            m_ReadPageCounts[readSlot] = page.Count;
            m_PageIndex.BaseStream.Position = m_WrittenPages++ * PageIndexSize;
            WritePageIndex(in page);
            BytesWritten = m_Events.BaseStream.Length + m_Checkpoints.BaseStream.Length + m_SymbolReader.BaseStream.Length +
                m_PageIndex.BaseStream.Length + m_Segments.BaseStream.Length + m_SpanIndex.BytesWritten;
            foreach (TimeIndex time in m_Times.Values)
                BytesWritten += time.BytesWritten;
        }

        void CachePageState(long pageId, long first, RuntimeCaptureReadWindow state, long baseEvictions)
        {
            int slot = (int)(pageId % CheckpointPageInterval);
            m_PageStateEvictions[slot] = baseEvictions + state.CopyStateTo(m_StateScratch);
            Span<RuntimeTraceEvent> states = m_PageStates[slot];
            ClearEvents(states.Slice(0, m_PageStateCounts[slot]));
            if (states.Length < m_StateScratch.Count)
            {
                m_PageStates[slot] = new RuntimeTraceEvent[Math.Min(StateCapacity,
                    Math.Max(m_StateScratch.Count, states.Length * 2))];
                states = m_PageStates[slot];
            }
            for (int i = 0; i < m_StateScratch.Count; i++)
            {
                m_StateScratch[i].Retain();
                states[i] = m_StateScratch[i];
            }
            m_PageStateCounts[slot] = m_StateScratch.Count;
            m_PageStateFirst[slot] = first;
        }

        void WriteCheckpoint()
        {
            m_Checkpoint.CheckpointFirst = m_Written;
            m_Checkpoint.CheckpointOffset = m_Checkpoints.BaseStream.Length;
            m_Checkpoints.BaseStream.Position = m_Checkpoint.CheckpointOffset;
            m_Checkpoint.CheckpointSymbols = m_WriterCodec.BeginPage();
            m_Checkpoint.EvictedStates = m_PageStateEvictions[(int)(m_WrittenPages % CheckpointPageInterval)];
            m_Checkpoint.StateCount = m_StateScratch.Count;
            ClearEvents(m_CheckpointState);
            for (int i = 0; i < m_StateScratch.Count; i++)
            {
                m_StateScratch[i].Retain();
                m_CheckpointState[i] = m_StateScratch[i];
            }
            m_CheckpointStateFirst = m_Checkpoint.CheckpointFirst;
            for (int i = 0; i < m_StateScratch.Count; i++)
            { RuntimeTraceEvent value = m_StateScratch[i]; m_WriterCodec.Write(m_Checkpoints, in value); }
            m_Checkpoints.Write(m_Metadata.SamplingLimitCount);
            m_Checkpoints.Write(m_Metadata.LatestLogicTimeSeconds);
            m_Checkpoints.Write(m_Metadata.LatestPresentationTimeSeconds);
            m_Checkpoints.Write(m_Metadata.SourceCount);
            m_Checkpoints.Write(m_Metadata.ClockTimes.Count);
            for (int i = 0; i < m_Metadata.SourceCount; i++)
            {
                RuntimeCaptureSpanSource source = m_Metadata.GetSource(i);
                m_WriterCodec.WriteSourceMetadata(m_Checkpoints, in source);
            }
            foreach (var clock in m_Metadata.ClockTimes)
                m_WriterCodec.WriteClockMetadata(m_Checkpoints, clock.Key, clock.Value);
            m_Checkpoint.CheckpointSymbolCount = m_WriterCodec.SymbolCount;
        }

        ReadResult ReadQuery(RuntimeCaptureSnapshot snapshot, in RuntimeCaptureQuery query, long request, long stateVersion)
        {
            if (!IsCurrentRead(request))
                return ReadResult.Cancelled;
            if (snapshot.Version == 0) return ReadResult.NotFound;
            long selected;
            if (query.Kind == RuntimeCaptureQueryKind.Time)
            {
                if (!FindTime(query.Domain, query.TimeSeconds, query.Branch, query.Epoch, snapshot.Version, request, out selected))
                    return ReadResult.Cancelled;
            }
            else if (query.Kind == RuntimeCaptureQueryKind.Event)
            {
                if (!FindSequence(query.Sequence, snapshot.Version, request, out selected))
                    return ReadResult.Cancelled;
            }
            else
                selected = query.Kind == RuntimeCaptureQueryKind.Segment
                    ? SegmentEnd(Math.Max(0, snapshot.SegmentCount - query.HistoryOffset - 1), snapshot) - 1
                    : snapshot.Version - 1;
            if (selected < 0) return ReadResult.NotFound;
            if (!ReadEvent(selected, request, out RuntimeTraceEvent selectedEvent))
                return ReadResult.Cancelled;
            if (query.Kind == RuntimeCaptureQueryKind.Event && selectedEvent.Sequence != query.Sequence) return ReadResult.NotFound;
            m_Result.ClearResult();
            m_Result.Selected = selectedEvent;
            m_Result.NextTimeSeconds = double.PositiveInfinity;
            foreach (var pair in m_Times)
            {
                if (!IsCurrentRead(request))
                    return ReadResult.Cancelled;
                if (pair.Key.Item1 == query.Domain && (!query.Branch.HasValue || pair.Key.Item2 == query.Branch) &&
                    (!query.Epoch.HasValue || pair.Key.Item3 == query.Epoch))
                {
                    if (!pair.Value.NextTime(selected, snapshot.Version, this, request, out double nextTime))
                        return ReadResult.Cancelled;
                    m_Result.NextTimeSeconds = Math.Min(m_Result.NextTimeSeconds, nextTime);
                }
            }
            if (!FindSegment(selected, snapshot.SegmentCount, request, out int segment))
                return ReadResult.Cancelled;
            m_Result.HistoryOffset = snapshot.SegmentCount - segment - 1;
            m_Result.LatestLogicTime = snapshot.LatestLogicTimeSeconds;
            m_Result.LatestPresentationTime = snapshot.LatestPresentationTimeSeconds;
            if (!FindPage(selected, request, out long statePageId))
                return ReadResult.Cancelled;
            Page statePage = ReadPage(statePageId);
            int stateSlot = (int)(statePageId % CheckpointPageInterval);
            bool cached = m_PageStateFirst[stateSlot] == statePage.First;
            long first = cached ? statePage.First : statePage.CheckpointFirst;
            int count = cached ? m_PageStateCounts[stateSlot] : statePage.StateCount;
            if (selected < m_ReadStateOrdinal || first - m_ReadStateOrdinal - 1 > count)
            {
                m_ReadStateBaseEvictions = cached ? m_PageStateEvictions[stateSlot] : statePage.EvictedStates;
                if (!cached && m_CheckpointStateFirst != first)
                {
                    m_CheckpointStateFirst = -1;
                    ClearEvents(m_CheckpointState);
                    if (!ReadSymbols(statePage.CheckpointSymbols, statePage.CheckpointSymbolCount, request))
                        return ReadResult.Cancelled;
                    m_CheckpointReader.BaseStream.Position = statePage.CheckpointOffset;
                    for (int i = 0; i < count; i++)
                    {
                        if (i % ReadChunkSize == 0 && !IsCurrentRead(request))
                            return ReadResult.Cancelled;
                        m_CheckpointState[i] = m_ReaderCodec.Read(m_CheckpointReader);
                    }
                    m_CheckpointStateFirst = first;
                }
                ReadOnlySpan<RuntimeTraceEvent> state = cached
                    ? m_PageStates[stateSlot].AsSpan(0, count) : m_CheckpointState.AsSpan(0, count);
                m_ReadState.RestoreState(state);
                m_ReadStateOrdinal = first - 1;
            }
            for (long i = m_ReadStateOrdinal + 1; i <= selected; i++)
            {
                if (i % EventsPerPage == 0 && !ContinueRead(request))
                    return ReadResult.Cancelled;
                if (i == statePage.First)
                    CachePageState(statePageId, statePage.First, m_ReadState, m_ReadStateBaseEvictions);
                if (!ReadEvent(i, request, out RuntimeTraceEvent value))
                    return ReadResult.Cancelled;
                m_ReadState.ApplyState(in value);
                m_ReadStateOrdinal = i;
            }
            m_ReadState.CopyStateSince(stateVersion, m_Result, m_ReadStateBaseEvictions);
            if (!query.IncludeExecutionWindow)
                return ReadResult.Found;
            double windowStart = query.Kind == RuntimeCaptureQueryKind.LiveProjection
                ? (query.Domain == RuntimeTraceDomain.Presentation ? snapshot.LatestPresentationTimeSeconds : snapshot.LatestLogicTimeSeconds) -
                    RuntimeCaptureReadWindow.LiveProjectionDurationSeconds
                : query.WindowStart;
            long last = selected;
            if (query.Kind != RuntimeCaptureQueryKind.LiveProjection &&
                !FindTime(query.Domain, query.WindowEnd, query.Branch, query.Epoch, snapshot.Version, request, out last))
                return ReadResult.Cancelled;
            if (last < 0) return ReadResult.Found;
            m_Result.TimelineVersion = last + 1;
            if (!FindPage(last, request, out long timelinePageId))
                return ReadResult.Cancelled;
            Page timelinePage = ReadPage(timelinePageId);
            if (!ReadSymbols(timelinePage.CheckpointSymbols, timelinePage.CheckpointSymbolCount, request))
                return ReadResult.Cancelled;
            m_CheckpointReader.BaseStream.Position = timelinePage.CheckpointOffset + timelinePage.StateCount * RuntimeTraceBinaryCodec.EventSize;
            int samplingLimitCount = m_CheckpointReader.ReadInt32();
            double latestLogic = m_CheckpointReader.ReadDouble();
            double latestPresentation = m_CheckpointReader.ReadDouble();
            int sourceCount = m_CheckpointReader.ReadInt32();
            int clockCount = m_CheckpointReader.ReadInt32();
            m_Result.TimelineSpans.ImportMetadata(samplingLimitCount, latestLogic, latestPresentation);
            for (int i = 0; i < sourceCount; i++)
            {
                if (i % ReadChunkSize == 0 && !IsCurrentRead(request))
                    return ReadResult.Cancelled;
                RuntimeCaptureSpanSource source = m_ReaderCodec.ReadSourceMetadata(m_CheckpointReader);
                m_Result.TimelineSpans.ImportSource(in source);
            }
            for (int i = 0; i < clockCount; i++)
            {
                if (i % ReadChunkSize == 0 && !IsCurrentRead(request))
                    return ReadResult.Cancelled;
                m_ReaderCodec.ReadClockMetadata(m_CheckpointReader, m_Result.TimelineSpans);
            }
            for (long i = timelinePage.CheckpointFirst; i <= last; i++)
            {
                if (i % EventsPerPage == 0 && !ContinueRead(request))
                    return ReadResult.Cancelled;
                if (!ReadEvent(i, request, out RuntimeTraceEvent value))
                    return ReadResult.Cancelled;
                m_Result.TimelineSpans.AppendMetadata(in value);
            }
            double windowEnd = query.Kind == RuntimeCaptureQueryKind.LiveProjection
                ? (query.Domain == RuntimeTraceDomain.Presentation ? snapshot.LatestPresentationTimeSeconds : snapshot.LatestLogicTimeSeconds)
                : query.WindowEnd;
            if (!m_SpanIndex.BeginWindow(windowStart, windowEnd, m_Result.TimelineVersion, request))
                return ReadResult.Cancelled;
            while (true)
            {
                if (!m_SpanIndex.TryGetNextWindowCandidate(request, out long location))
                    return ReadResult.Cancelled;
                if (location < 0)
                    break;
                if (!m_SpanIndex.Read(location, m_Result.TimelineVersion, request, out RuntimeCaptureSpanReference span))
                    return ReadResult.Cancelled;
                double observedEnd = span.State == RuntimeCaptureSpanState.Open
                    ? Math.Max(span.EndTime, m_Result.TimelineSpans.ClockTimes[(span.Domain, span.Branch, span.Epoch)])
                    : span.EndTime;
                if ((float)observedEnd < windowStart)
                    continue;
                if (!ReadEvent(span.StartOrdinal, request, out RuntimeTraceEvent start) ||
                    !ReadEvent(span.EndOrdinal, request, out RuntimeTraceEvent end))
                    return ReadResult.Cancelled;
                m_Result.TimelineSpans.ImportSpan(in start, in end, span.State);
            }
            return ReadResult.Found;
        }

        internal bool IsCurrentRead(long request) => !Volatile.Read(ref m_Stop) && request == Volatile.Read(ref m_QueryVersion);

        bool ReadSymbols(long offset, int count, long request)
        {
            if (!IsCurrentRead(request))
                return false;
            m_ReaderCodec.BeginReadSymbols(m_SymbolReader, offset);
            for (int i = 0; i < count; i += ReadChunkSize)
            {
                if (!IsCurrentRead(request))
                    return false;
                m_ReaderCodec.ReadSymbols(m_SymbolReader, Math.Min(ReadChunkSize, count - i));
            }
            return IsCurrentRead(request);
        }

        internal bool ContinueRead(long request)
        {
            while (true)
            {
                int page;
                lock (m_Gate)
                {
                    if (m_Stop || request != m_QueryVersion)
                        return false;
                    if (m_Pending.Count == 0)
                        return true;
                    page = m_Pending.Dequeue();
                }
                WritePage(page);
                ClearEvents(m_Pages[page].AsSpan(0, m_Counts[page]));
                lock (m_Gate) m_Free.Enqueue(page);
            }
        }

        static RuntimeTraceDomain Normalize(RuntimeTraceDomain domain) => domain == RuntimeTraceDomain.Lifecycle ? RuntimeTraceDomain.Logic : domain;

        bool FindTime(RuntimeTraceDomain domain, double time, Guid? branch, ulong? epoch, long limit, long request, out long found)
        {
            found = -1;
            foreach (var pair in m_Times)
            {
                if (!IsCurrentRead(request))
                    return false;
                if (pair.Key.Item1 == domain && (!branch.HasValue || branch.Value == pair.Key.Item2) && (!epoch.HasValue || epoch.Value == pair.Key.Item3))
                {
                    if (!pair.Value.Find(time, limit, this, request, out long ordinal))
                        return false;
                    found = Math.Max(found, ordinal);
                }
            }
            return true;
        }

        bool FindSequence(ulong sequence, long count, long request, out long found)
        {
            found = -1;
            long first = 0, end = count;
            while (first < end)
            {
                if (!IsCurrentRead(request))
                    return false;
                long middle = first + (end - first) / 2;
                m_EventReader.BaseStream.Position = middle * RuntimeTraceBinaryCodec.EventSize;
                if (m_EventReader.ReadUInt64() <= sequence) first = middle + 1; else end = middle;
            }
            found = first - 1;
            return true;
        }

        long SegmentEnd(int segment, RuntimeCaptureSnapshot snapshot) => segment + 1 == snapshot.SegmentCount ? snapshot.Version : ReadSegment(segment + 1);
        long ReadSegment(int segment) { m_SegmentReader.BaseStream.Position = segment * sizeof(long); return m_SegmentReader.ReadInt64(); }
        bool FindSegment(long ordinal, int count, long request, out int segment)
        {
            segment = -1;
            int first = 0, end = count;
            while (first < end)
            {
                if (!IsCurrentRead(request))
                    return false;
                int middle = first + (end - first) / 2;
                if (ReadSegment(middle) <= ordinal) first = middle + 1; else end = middle;
            }
            segment = first - 1;
            return true;
        }
        bool FindPage(long ordinal, long request, out long page)
        {
            page = -1;
            long first = 0, end = m_WrittenPages;
            while (first < end)
            {
                if (!IsCurrentRead(request))
                    return false;
                long middle = first + (end - first) / 2;
                if (ReadPage(middle).First <= ordinal) first = middle + 1; else end = middle;
            }
            page = first - 1;
            return true;
        }
        bool ReadEvent(long ordinal, long request, out RuntimeTraceEvent value)
        {
            value = default;
            if (!IsCurrentRead(request))
                return false;
            if (ordinal < m_ReadPageFirst[m_ReadSlot] ||
                ordinal >= m_ReadPageFirst[m_ReadSlot] + m_ReadPageCounts[m_ReadSlot])
            {
                for (int i = 0; i < m_ReadPageCounts.Length; i++)
                    if (ordinal >= m_ReadPageFirst[i] && ordinal < m_ReadPageFirst[i] + m_ReadPageCounts[i])
                    {
                        m_ReadSlot = i;
                        value = m_ReadPages[i * EventsPerPage + (int)(ordinal - m_ReadPageFirst[i])];
                        return true;
                    }
                if (!FindPage(ordinal, request, out long pageId))
                    return false;
                Page page = ReadPage(pageId);
                int slot = (int)(pageId % CheckpointPageInterval);
                m_ReadPageCounts[slot] = 0;
                ClearEvents(m_ReadPages.AsSpan(slot * EventsPerPage, EventsPerPage));
                if (!ReadSymbols(page.SymbolOffset, page.SymbolCount, request))
                    return false;
                long end = pageId + 1 == m_WrittenPages ? m_Written : ReadPage(pageId + 1).First;
                int count = (int)(end - page.First);
                m_EventReader.BaseStream.Position = page.First * RuntimeTraceBinaryCodec.EventSize;
                for (int i = 0; i < count; i++)
                {
                    if (i % ReadChunkSize == 0 && !IsCurrentRead(request))
                        return false;
                    m_ReadPages[slot * EventsPerPage + i] = m_ReaderCodec.Read(m_EventReader);
                }
                m_ReadPageFirst[slot] = page.First;
                m_ReadPageCounts[slot] = count;
                m_ReadSlot = slot;
            }
            value = m_ReadPages[m_ReadSlot * EventsPerPage + (int)(ordinal - m_ReadPageFirst[m_ReadSlot])];
            return true;
        }
        void WritePageIndex(in Page p)
        {
            m_PageIndex.Write(p.First); m_PageIndex.Write(p.SymbolOffset); m_PageIndex.Write(p.CheckpointOffset);
            m_PageIndex.Write(p.CheckpointSymbols); m_PageIndex.Write(p.CheckpointFirst); m_PageIndex.Write(p.EvictedStates);
            m_PageIndex.Write(p.SymbolCount); m_PageIndex.Write(p.CheckpointSymbolCount);
            m_PageIndex.Write(p.StateCount);
        }
        Page ReadPage(long index)
        {
            m_PageReader.BaseStream.Position = index * PageIndexSize;
            return new Page { First = m_PageReader.ReadInt64(), SymbolOffset = m_PageReader.ReadInt64(),
                CheckpointOffset = m_PageReader.ReadInt64(), CheckpointSymbols = m_PageReader.ReadInt64(),
                CheckpointFirst = m_PageReader.ReadInt64(), EvictedStates = m_PageReader.ReadInt64(),
                SymbolCount = m_PageReader.ReadInt32(), CheckpointSymbolCount = m_PageReader.ReadInt32(),
                StateCount = m_PageReader.ReadInt32() };
        }

        void ClearPageStates()
        {
            for (int slot = 0; slot < m_PageStates.Length; slot++)
            {
                ClearEvents(m_PageStates[slot].AsSpan(0, m_PageStateCounts[slot]));
                m_PageStateCounts[slot] = 0;
            }
        }

        static void ClearEvents(Span<RuntimeTraceEvent> events)
        {
            for (int i = 0; i < events.Length; i++)
                events[i].Release();
            events.Clear();
        }

        static void CopyEvents(ReadOnlySpan<RuntimeTraceEvent> source, Span<RuntimeTraceEvent> destination)
        {
            for (int i = 0; i < source.Length; i++)
            {
                source[i].Retain();
                destination[i] = source[i];
            }
        }

        public void Dispose()
        {
            lock (m_Gate) { m_Stop = true; if (!m_Finished) m_Wake.Set(); }
        }

        struct Page
        {
            internal long First, SymbolOffset, CheckpointOffset, CheckpointSymbols, CheckpointFirst, EvictedStates;
            internal int Count, SymbolCount, CheckpointSymbolCount, StateCount;
        }

        sealed class TimeIndex : IDisposable
        {
            readonly FileStream m_File;
            readonly BinaryReader m_Reader;
            readonly BinaryWriter m_Writer;
            long m_Count;
            internal long BytesWritten => m_File.Length;
            internal TimeIndex(string path)
            { m_File = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 65536);
                m_Reader = new BinaryReader(m_File); m_Writer = new BinaryWriter(m_File); }
            internal void Append(double time, long ordinal)
            { long offset = m_Count++ * 16; if (m_File.Position != offset) m_File.Position = offset; m_Writer.Write(time); m_Writer.Write(ordinal); }
            internal void Reset() { m_Count = 0; m_File.SetLength(0); m_File.Position = 0; }
            internal bool Find(double time, long limit, RuntimeCaptureFileArchive owner, long request, out long found)
            {
                found = -1;
                long first = 0, end = m_Count;
                while (first < end)
                {
                    if (!owner.IsCurrentRead(request))
                        return false;
                    long middle = first + (end - first) / 2; m_File.Position = middle * 16;
                    double sample = m_Reader.ReadDouble(); long ordinal = m_Reader.ReadInt64();
                    if (sample <= time && ordinal < limit) first = middle + 1; else end = middle;
                }
                if (first == 0) return true;
                m_File.Position = (first - 1) * 16 + 8;
                found = m_Reader.ReadInt64();
                return true;
            }
            internal bool NextTime(long ordinal, long limit, RuntimeCaptureFileArchive owner, long request, out double nextTime)
            {
                nextTime = double.PositiveInfinity;
                long first = 0, end = m_Count;
                while (first < end)
                {
                    if (!owner.IsCurrentRead(request))
                        return false;
                    long middle = first + (end - first) / 2;
                    m_File.Position = middle * 16 + 8;
                    if (m_Reader.ReadInt64() <= ordinal) first = middle + 1; else end = middle;
                }
                if (first == m_Count) return true;
                m_File.Position = first * 16;
                double time = m_Reader.ReadDouble();
                if (m_Reader.ReadInt64() < limit)
                    nextTime = time;
                return true;
            }
            public void Dispose() { m_Writer.Dispose(); m_Reader.Dispose(); }
        }

    }
}
