using System;
using System.Collections.Generic;
using Unity.Profiling;

namespace BTSMTL.Diagnostics.Editor
{
    internal sealed class RuntimeDebugTargetProvider : IDisposable
    {
        static readonly ProfilerMarker s_Refresh = new("RuntimeDebug.LiveSync");
        static readonly ProfilerMarker s_Read = new("RuntimeDebug.LiveRead");
        static readonly ProfilerMarker s_Apply = new("RuntimeDebug.LiveApply");
        static readonly ProfilerMarker s_Reset = new("RuntimeDebug.LiveReset");
        static readonly ProfilerMarker s_Events = new("RuntimeDebug.LiveEvents");
        static readonly ProfilerMarker s_Commit = new("RuntimeDebug.LiveCommit");
        readonly RuntimeDiagnosticsTarget m_Target;
        readonly RuntimeDebugSourceMapSnapshot m_SourceMap;
        readonly RuntimeDebugViewModel m_LiveModel;
        readonly List<RuntimeLiveStateChange> m_LiveChanges;
        readonly Dictionary<RuntimeLiveStateKey, int> m_LastLiveChanges;
        readonly List<RuntimeCaptureChange> m_CaptureChanges;
        long m_LiveCursor;
        Guid m_CaptureId;
        readonly RuntimeExecutionTimelineBuilder.SpanAccumulator m_ExecutionSpans;
        RuntimeExecutionTimeline m_ExecutionTimeline;
        RuntimeTraceChannel m_LastChannels;
        double m_LastProjectionTrim;

        public RuntimeDebugTargetProvider(RuntimeDiagnosticsTarget target)
        {
            m_Target = target;
            m_LiveChanges = new List<RuntimeLiveStateChange>(target.Store.LiveStateCapacity);
            m_LastLiveChanges = new Dictionary<RuntimeLiveStateKey, int>(target.Store.LiveStateCapacity);
            m_CaptureChanges = new List<RuntimeCaptureChange>(target.Store.CaptureEventCapacity);
            m_ExecutionSpans = new RuntimeExecutionTimelineBuilder.SpanAccumulator(target.Store.CaptureEventCapacity);
            m_SourceMap = RuntimeDebugSourceMapSnapshot.Capture(target.SourceMap);
            m_LastChannels = target.Store.EffectiveChannels;
            m_LiveModel = new RuntimeDebugViewModel(new RuntimeDebugTargetInfo(target), m_SourceMap, m_LastChannels, target.Store.LiveStateCapacity);
        }

        public void Dispose()
        {
            m_LastLiveChanges.Clear();
            m_LiveChanges.Clear();
            m_CaptureChanges.Clear();
        }

        public RuntimeDebugViewModel LiveModel => m_LiveModel;
        public RuntimeDiagnosticsTarget Target => m_Target;
        public RuntimeDebugSourceMapSnapshot SourceMap => m_SourceMap;
        public Guid CaptureId => m_CaptureId;
        public RuntimeExecutionTimeline ExecutionTimeline => m_ExecutionTimeline;
        public long CaptureVersion { get; private set; }

        public bool Refresh(IReadOnlyDictionary<RuntimeContentRevision, RuntimeDebugSourceMapSnapshot> sourceMaps)
        {
            using var profile = s_Refresh.Auto();
            m_LastLiveChanges.Clear();
            RuntimeLiveStateRead read;
            using (s_Read.Auto())
                read = m_Target.Store.CopyLiveStateSince(m_LiveCursor, m_LiveChanges);
            var capture = m_Target.Store.CaptureStatus;
            RuntimeTraceChannel channels = m_Target.Store.EffectiveChannels;
            bool stateChanged = read.Version != m_LiveCursor;
            bool captureChanged = capture.CaptureId != m_CaptureId || capture.Version != CaptureVersion;
            bool channelChanged = channels != m_LastChannels;
            if (!stateChanged && !captureChanged && !channelChanged)
                return false;

            using var applyProfile = s_Apply.Auto();
            using (s_Reset.Auto())
                m_LiveModel.BeginUpdate(stateChanged && read.RequiresFullSync);
            if (stateChanged)
            {
                using var eventProfile = s_Events.Auto();
                for (int i = 0; i < read.Changes.Count; i++)
                {
                    RuntimeLiveStateChange change = read.Changes[i];
                    if (m_LastLiveChanges.TryGetValue(change.Key, out int previous) && previous < 0)
                        continue;
                    m_LastLiveChanges[change.Key] = change.IsRemoval ? -1 : i;
                }
                for (int i = 0; i < read.Changes.Count; i++)
                {
                    RuntimeLiveStateChange change = read.Changes[i];
                    int last = m_LastLiveChanges[change.Key];
                    if (last >= 0 && last != i)
                        continue;
                    if (change.IsRemoval)
                    {
                        m_LiveModel.Remove(change.Key);
                        continue;
                    }
                    m_LiveModel.Apply(change.TraceEvent, sourceMaps[change.TraceEvent.ContentRevision]);
                }
            }
            using (s_Commit.Auto())
            {
                m_LiveModel.SetChannels(channels);
                m_LiveModel.SetCoverage(read.EvictedStates, false);
                m_LiveModel.CommitUpdate(captureChanged ? capture.Version : CaptureVersion);
            }
            m_LiveCursor = read.Version;
            m_LastChannels = channels;
            if (captureChanged)
            {
                m_CaptureId = capture.CaptureId;
                CaptureVersion = capture.Version;
            }
            return true;
        }

        public RuntimeExecutionTimeline ReadExecutionTimeline(
            IReadOnlyDictionary<RuntimeContentRevision, RuntimeDebugSourceMapSnapshot> sourceMaps,
            out bool requiresArchiveSync)
        {
            Guid projectionCaptureId = m_ExecutionTimeline?.CaptureId ?? Guid.Empty;
            long projectionVersion = m_ExecutionTimeline?.Version ?? 0;
            RuntimeCaptureRead capture = m_Target.Store.CopyCaptureSince(
                projectionCaptureId, projectionVersion, m_CaptureChanges);
            requiresArchiveSync = m_Target.Store.HasCaptureArchive && capture.RequiresFullSync && capture.Version > 0;
            if (capture.CaptureId == Guid.Empty || requiresArchiveSync)
                return m_ExecutionTimeline;
            {
                if (capture.CaptureId != projectionCaptureId || capture.RequiresFullSync)
                {
                    m_ExecutionSpans.Reset();
                    m_ExecutionTimeline = m_ExecutionSpans.Timeline;
                }
                for (int i = 0; i < m_CaptureChanges.Count; i++)
                    m_ExecutionSpans.Append(m_CaptureChanges[i].TraceEvent, m_SourceMap, sourceMaps);
                if (m_Target.Store.HasCaptureArchive && m_ExecutionSpans.LatestLogicTimeSeconds > m_LastProjectionTrim + 5)
                {
                    m_LastProjectionTrim = m_ExecutionSpans.LatestLogicTimeSeconds;
                    m_ExecutionSpans.TrimBefore(m_LastProjectionTrim - RuntimeCaptureReadWindow.LiveProjectionDurationSeconds,
                        m_ExecutionSpans.LatestPresentationTimeSeconds - RuntimeCaptureReadWindow.LiveProjectionDurationSeconds);
                }
                if (m_Target.Store.HasCaptureArchive)
                    capture = new RuntimeCaptureRead(capture.CaptureId, capture.Channels, capture.Detail,
                        capture.Version, 0, capture.RequiresFullSync, capture.Changes);
                m_ExecutionTimeline.Update(capture.CaptureId, capture.Channels, capture.Detail, capture.Version,
                    capture.EvictedEvents, m_ExecutionSpans.IsComplete(capture.EvictedEvents),
                    m_ExecutionSpans.UnmappedEventCount, m_ExecutionSpans.LatestLogicTimeSeconds,
                    m_ExecutionSpans.LatestPresentationTimeSeconds);
            }
            return m_ExecutionTimeline;
        }

        public RuntimeExecutionTimeline RestoreExecutionTimeline(RuntimeCaptureSnapshot capture,
            RuntimeCaptureReadWindow window,
            IReadOnlyDictionary<RuntimeContentRevision, RuntimeDebugSourceMapSnapshot> sourceMaps)
        {
            m_ExecutionTimeline = RuntimeExecutionTimelineBuilder.BuildWindow(capture, window,
                m_SourceMap, sourceMaps, m_ExecutionSpans);
            m_LastProjectionTrim = m_ExecutionSpans.LatestLogicTimeSeconds;
            return m_ExecutionTimeline;
        }

        public RuntimeCaptureSnapshot EndCapture()
        {
            return m_Target.Store.EndCapture();
        }

        public bool BeginCapture(RuntimeTraceChannel channels, RuntimeDiagnosticsCaptureDetail detail, out Guid captureId)
        {
            return m_Target.Store.BeginCapture(channels, detail, out captureId);
        }

        public RuntimeDebugFrozenDiagnostics Freeze(
            IReadOnlyDictionary<RuntimeContentRevision, RuntimeDebugSourceMapSnapshot> sourceMaps)
        {
            Refresh(sourceMaps);
            return new RuntimeDebugFrozenDiagnostics(
                m_LiveModel,
                m_SourceMap,
                m_Target.Store.FreezeActiveCapture(),
                sourceMaps);
        }

    }

    internal sealed class RuntimeDebugFrozenDiagnostics
    {
        readonly RuntimeDebugViewModel m_LiveModel;
        readonly RuntimeDebugSourceMapSnapshot m_SourceMap;
        readonly IReadOnlyDictionary<RuntimeContentRevision, RuntimeDebugSourceMapSnapshot> m_SourceMaps;

        public RuntimeDebugFrozenDiagnostics(
            RuntimeDebugViewModel liveModel,
            RuntimeDebugSourceMapSnapshot sourceMap,
            RuntimeCaptureSnapshot activeCapture,
            IReadOnlyDictionary<RuntimeContentRevision, RuntimeDebugSourceMapSnapshot> sourceMaps)
        {
            m_LiveModel = liveModel;
            m_SourceMap = sourceMap;
            ActiveCapture = activeCapture;
            m_SourceMaps = sourceMaps;
        }

        public RuntimeDebugViewModel LiveModel => m_LiveModel;
        public RuntimeCaptureSnapshot ActiveCapture { get; }
        public RuntimeDebugSourceMapSnapshot SourceMap => m_SourceMap;

        public RuntimeDebugTargetMatch MatchSource(RuntimeDebugTargetRequest request)
        {
            RuntimeDebugTargetMatch current = m_SourceMap.Match(request);
            if (current == RuntimeDebugTargetMatch.Exact)
                return current;
            foreach (RuntimeDebugSourceMapSnapshot sourceMap in m_SourceMaps.Values)
                if (sourceMap.Match(request) == RuntimeDebugTargetMatch.Exact)
                    return RuntimeDebugTargetMatch.Exact;
            return current;
        }

    }
}
