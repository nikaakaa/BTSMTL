using System;
using System.Buffers;
using Photon.Deterministic;
using ThirdPersonSimulation;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BTSMTL.Diagnostics;

namespace BTSMTL.Timeline.Runtime
{
    public sealed class TimelineRuntimeCompositionHost : IDisposable
    {
        readonly TimelineRuntimeEvaluationBuffer m_EvaluationBuffer = new TimelineRuntimeEvaluationBuffer();
        readonly TimelineRuntimePresentationDriver m_PresentationDriver =
            new TimelineRuntimePresentationDriver();
        bool m_Disposed;

        public TimelineRuntimeCompositionHost(
            TimelineRuntimeNumericTarget numericTarget,
            ITimelineRuntimeCallBindingSource callBindingSource,
            ITimelineDomainBindingResolver domainResolver,
            ITimelineRuntimeDependencyResolver dependencyResolver,
            IEnumerable<ITimelineRuntimeEvaluationSink> evaluationSinks,
            ITimelineRuntimeTreeClipService treeClipService,
            ITimelineRuntimeMarkerService markerService,
            int tickRate)
        {
            if (evaluationSinks == null)
                throw new ArgumentNullException(nameof(evaluationSinks));
            var sinks = new List<ITimelineRuntimeEvaluationSink>
            {
                m_EvaluationBuffer,
                m_PresentationDriver
            };
            foreach (ITimelineRuntimeEvaluationSink sink in evaluationSinks)
            {
                if (sink == null)
                    throw new ArgumentException("Timeline runtime composition contains a null evaluation sink.", nameof(evaluationSinks));
                sinks.Add(sink);
            }
            var requestFactory = new TimelineRuntimePlaybackRequestFactory(
                numericTarget,
                domainResolver,
                dependencyResolver,
                callBindingSource);
            var consumer = new TimelineRuntimeExecutionConsumer(
                new TimelineRuntimeEvaluationFanout(sinks),
                treeClipService,
                markerService);
            Service = new TimelineRuntimeService(requestFactory, consumer, consumer, tickRate);
        }

        public TimelineRuntimeService Service { get; }

        public void InstallContent(
            TimelineData timeline,
            TimelineContentUnit content,
            IReadOnlyList<TimelinePlaybackMode> playbackModes)
        {
            TimelineRuntimePreparedContent prepared = Service.InstallContent(timeline, content, playbackModes);
            for (int index = 0; index < playbackModes.Count; index++)
                m_PresentationDriver.Prepare(prepared, playbackModes[index]);
        }

        public void RetireContent(
            TimelineData timeline,
            TimelineContentUnit content,
            IReadOnlyList<TimelinePlaybackMode> playbackModes)
        {
            Service.RetireContent(timeline, playbackModes);
            for (int index = 0; index < playbackModes.Count; index++)
                m_PresentationDriver.RetireContent(content, playbackModes[index]);
        }

        public TimelineRuntimePreparedContent InstallTimeRangeCandidate(TimelineData baseline, TimelineData timeline,
            TimelineContentUnit content, IReadOnlyList<TimelinePlaybackMode> playbackModes)
        {
            TimelineRuntimePreparedContent prepared = Service.InstallTimeRangeCandidate(baseline, timeline, content, playbackModes);
            for (int i = 0; i < playbackModes.Count; i++)
                m_PresentationDriver.Prepare(prepared, playbackModes[i]);
            return prepared;
        }

        public event Action<TimelineRuntimeCommittedEvaluation> CommittedEvaluation
        {
            add => m_EvaluationBuffer.CommittedEvaluation += value;
            remove => m_EvaluationBuffer.CommittedEvaluation -= value;
        }

        public event Action<TimelineRuntimeStopRequest> StopCommitted
        {
            add => m_EvaluationBuffer.StopCommitted += value;
            remove => m_EvaluationBuffer.StopCommitted -= value;
        }

        public bool TryPresent(
            TimelinePlaybackHandle handle,
            in TimelineRuntimePresentationSample sample,
            ulong presentationFrame,
            float presentationDeltaSeconds,
            float interpolationAlpha,
            out TimelineRuntimePresentationFrame frame)
        {
            EnsureAvailable();
            if (!Service.TryGetPlayback(new TimelineRuntimePlaybackHandle(handle.Value), out TimelineRuntimePlayback playback))
            {
                frame = default;
                return false;
            }
            return m_PresentationDriver.TryPresent(
                playback,
                in sample,
                presentationFrame,
                presentationDeltaSeconds,
                interpolationAlpha,
                out frame);
        }

        public bool RequestPresentationTreeClipExit(in TimelineRuntimePresentationFrame frame, in TimelineRuntimeTreeClipRequest request) =>
            m_PresentationDriver.RequestTreeClipExit(frame, request);

        public void CommitPresentationFrame(ulong frame) => m_PresentationDriver.CommitPresentationFrame(frame);
        public void DiscardPresentationFrame(ulong frame) => m_PresentationDriver.DiscardPresentationFrame(frame);
        public void SuspendPresentationPlayback(TimelineRuntimePlaybackHandle handle, ulong generation) => m_PresentationDriver.SuspendPresentationPlayback(handle, generation);
        public void ReleasePresentationPlayback(TimelineRuntimePlaybackHandle handle, ulong generation) => m_PresentationDriver.ReleasePresentationPlayback(handle, generation);

        public void DiscardEvaluation(TimelineRuntimePlaybackHandle handle)
        {
            Service.DiscardEvaluation(handle);
            m_EvaluationBuffer.ReleasePlayback(handle);
        }

        public void ReleasePlayback(TimelineRuntimePlaybackHandle handle)
        {
            Service.ReleasePlayback(handle);
            m_EvaluationBuffer.ReleasePlayback(handle);
        }

        public TimelineRuntimePlaybackHandle ApplyRestore(
            TimelineRuntimeRestoreCandidate candidate)
        {
            EnsureAvailable();
            TimelineRuntimePlaybackHandle handle = Service.ApplyRestore(candidate);
            m_EvaluationBuffer.ReleasePlayback(handle);
            m_PresentationDriver.RestorePlayback(Service.Require(handle));
            return handle;
        }

        public bool TryGetCommittedEvaluationResult(
            TimelinePlaybackHandle handle,
            out TimelineRuntimeEvaluationResult result)
        {
            EnsureAvailable();
            return m_EvaluationBuffer.TryGetCommitted(new TimelineRuntimePlaybackHandle(handle.Value), out result);
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;
            m_Disposed = true;
            m_PresentationDriver.Clear();
            Service.Dispose();
            m_EvaluationBuffer.Clear();
        }

        void EnsureAvailable()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(TimelineRuntimeCompositionHost));
        }
    }

    static class TimelineRuntimeIdentity
    {
        public static string Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Timeline runtime identity is required.", name);
            return value.Trim();
        }
    }

    public sealed class TimelineRuntimeCallBindingSource : ITimelineRuntimeCallBindingSource
    {
        readonly string m_OwnerIdentity;
        readonly string m_CallIdentity;
        readonly TimelinePlaybackActionContext m_ActionContext;
        readonly ReadOnlyCollection<TimelineCallBinding> m_CallBindings;
        readonly Dictionary<(CanonicalTextValue SourceId, CanonicalTextValue SourceName), string> m_CallIdentities = new();
        static readonly SpanAction<char, (string CallIdentity, string SourceId, string SourceName)> s_WriteCallIdentity = WriteCallIdentity;

        public TimelineRuntimeCallBindingSource(
            string ownerIdentity,
            string callIdentity,
            TimelinePlaybackActionContext actionContext,
            IEnumerable<TimelineCallBinding> callBindings = null)
        {
            m_OwnerIdentity = TimelineRuntimeIdentity.Require(ownerIdentity, nameof(ownerIdentity));
            m_CallIdentity = TimelineRuntimeIdentity.Require(callIdentity, nameof(callIdentity));
            m_ActionContext = actionContext;
            m_CallBindings = new ReadOnlyCollection<TimelineCallBinding>(
                new List<TimelineCallBinding>(callBindings ?? Array.Empty<TimelineCallBinding>()));
        }

        public bool TryCreateExecutionIdentity(
            string sourceId,
            string sourceName,
            TimelinePlaybackActionContext actionContext,
            ulong playbackIdentity,
            out TimelineExecutionIdentity identity,
            out string error)
        {
            identity = default;
            error = string.Empty;
            if (m_ActionContext.IsValid && actionContext.IsValid &&
                m_ActionContext.ActionInstanceId != actionContext.ActionInstanceId)
            {
                error = "timeline_action_context_mismatch";
                return false;
            }
            CanonicalTextValue normalizedSourceId = CanonicalTextValue.FromString(sourceId ?? string.Empty).Trim();
            CanonicalTextValue normalizedSourceName = CanonicalTextValue.FromString(sourceName ?? string.Empty).Trim();
            string callIdentity = m_CallIdentities[(normalizedSourceId, normalizedSourceName)];
            identity = new TimelineExecutionIdentity(m_OwnerIdentity, callIdentity, playbackIdentity);
            return true;
        }

        public void PrepareExecutionIdentity(string sourceId, string sourceName)
        {
            string normalizedSourceId = sourceId?.Trim() ?? string.Empty;
            string normalizedSourceName = sourceName?.Trim() ?? string.Empty;
            var source = (normalizedSourceId, normalizedSourceName);
            if (m_CallIdentities.ContainsKey(source))
                return;
            string callIdentity = string.Create(
                m_CallIdentity.Length + normalizedSourceId.Length + normalizedSourceName.Length + 2,
                (m_CallIdentity, normalizedSourceId, normalizedSourceName), s_WriteCallIdentity);
            m_CallIdentities.Add(source, callIdentity);
        }

        static void WriteCallIdentity(Span<char> characters,
            (string CallIdentity, string SourceId, string SourceName) state)
        {
            state.CallIdentity.AsSpan().CopyTo(characters);
            int offset = state.CallIdentity.Length;
            characters[offset++] = ':';
            state.SourceId.AsSpan().CopyTo(characters.Slice(offset));
            offset += state.SourceId.Length;
            characters[offset++] = ':';
            state.SourceName.AsSpan().CopyTo(characters.Slice(offset));
        }

        public IReadOnlyList<TimelineCallBinding> CallBindings => m_CallBindings;
    }

    public readonly struct TimelineRuntimeCommittedEvaluation
    {
        internal TimelineRuntimeCommittedEvaluation(TimelineRuntimeStepContext context)
        {
            Handle = context.Playback.Handle;
            Generation = context.Playback.Generation;
            LogicTick = context.Request.LogicTick;
            PreviousTime = context.Advance.PreviousTime;
            Time = context.Advance.Time;
            PreviousCycle = context.Advance.PreviousCycle;
            Cycle = context.Advance.Cycle;
            ContentIdentity = context.Playback.Content.Identity;
            ContentRevision = context.Playback.Content.Revision;
            ExecutionIdentity = context.Playback.ExecutionIdentity;
            Evaluation = context.Advance.Evaluation;
            ActiveClipIds = context.Advance.ActiveClipIds;
            Completes = context.Advance.Completes;
            Control = context.Advance.Control;
        }

        public TimelineRuntimePlaybackHandle Handle { get; }
        public ulong Generation { get; }
        public ulong LogicTick { get; }
        public FP PreviousTime { get; }
        public FP Time { get; }
        public int PreviousCycle { get; }
        public int Cycle { get; }
        public string ContentIdentity { get; }
        public CanonicalTextValue ContentRevision { get; }
        public TimelineExecutionIdentity ExecutionIdentity { get; }
        public readonly TimelineRuntimeEvaluationResult Evaluation;
        public TimelineRuntimeSampleView<CanonicalTextValue> ActiveClipIds { get; }
        public bool Completes { get; }
        public AbilityTimelinePlaybackControl Control { get; }
    }

    sealed class TimelineRuntimeEvaluationBuffer : ITimelineRuntimeEvaluationSink
    {
        readonly Dictionary<ulong, TimelineRuntimeCommittedEvaluation> m_Committed =
            new Dictionary<ulong, TimelineRuntimeCommittedEvaluation>();

        public event Action<TimelineRuntimeCommittedEvaluation> CommittedEvaluation;
        public event Action<TimelineRuntimeStopRequest> StopCommitted;

        public bool Consume(TimelineRuntimeStepContext context) => true;

        public void Commit(TimelineRuntimeStepContext context)
        {
            ulong handle = context.Playback.Handle.Value;
            TimelineRuntimeCommittedEvaluation committed = new TimelineRuntimeCommittedEvaluation(context);
            m_Committed[handle] = committed;
            CommittedEvaluation?.Invoke(committed);
        }

        public void Discard(TimelineRuntimeStepContext context) { }

        public bool ConsumeStop(TimelineRuntimeStopRequest request) => true;

        public void CommitStop(TimelineRuntimeStopRequest request)
        {
            m_Committed.Remove(request.Handle.Value);
            StopCommitted?.Invoke(request);
        }

        public void DiscardStop(TimelineRuntimeStopRequest request) { }

        public bool TryGetCommitted(
            TimelineRuntimePlaybackHandle handle,
            out TimelineRuntimeEvaluationResult result)
        {
            if (m_Committed.TryGetValue(handle.Value, out TimelineRuntimeCommittedEvaluation committed) &&
                committed.Evaluation.IsCurrent)
            {
                result = committed.Evaluation;
                return true;
            }
            result = default;
            return false;
        }

        public void ReleasePlayback(TimelineRuntimePlaybackHandle handle)
        {
            m_Committed.Remove(handle.Value);
        }

        public void Clear()
        {
            m_Committed.Clear();
        }
    }

    sealed class TimelineRuntimeEvaluationFanout : ITimelineRuntimeEvaluationSink
    {
        readonly IReadOnlyList<ITimelineRuntimeEvaluationSink> m_Sinks;

        public TimelineRuntimeEvaluationFanout(IReadOnlyList<ITimelineRuntimeEvaluationSink> sinks)
        {
            m_Sinks = sinks;
        }

        public bool Consume(TimelineRuntimeStepContext context)
        {
            bool accepted = true;
            for (int index = 0; index < m_Sinks.Count; index++)
                accepted &= m_Sinks[index].Consume(context);
            return accepted;
        }

        public void Commit(TimelineRuntimeStepContext context)
        {
            for (int index = 0; index < m_Sinks.Count; index++)
                m_Sinks[index].Commit(context);
        }

        public void Discard(TimelineRuntimeStepContext context)
        {
            for (int index = 0; index < m_Sinks.Count; index++)
                m_Sinks[index].Discard(context);
        }

        public bool ConsumeStop(TimelineRuntimeStopRequest request)
        {
            bool accepted = true;
            for (int index = 0; index < m_Sinks.Count; index++)
                accepted &= m_Sinks[index].ConsumeStop(request);
            return accepted;
        }

        public void CommitStop(TimelineRuntimeStopRequest request)
        {
            for (int index = 0; index < m_Sinks.Count; index++)
                m_Sinks[index].CommitStop(request);
        }

        public void DiscardStop(TimelineRuntimeStopRequest request)
        {
            for (int index = 0; index < m_Sinks.Count; index++)
                m_Sinks[index].DiscardStop(request);
        }
    }
}
