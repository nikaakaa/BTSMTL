using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;

namespace BTSMTL.Timeline.Runtime
{
    public sealed class TimelineRuntimeCompositionHost : IDisposable
    {
        readonly TimelineRuntimeNumericTarget m_NumericTarget;
        readonly IReadOnlyList<TimelineCallBinding> m_CallBindings;
        readonly ITimelineDomainBindingResolver m_DomainResolver;
        readonly ITimelineRuntimeDependencyResolver m_DependencyResolver;
        readonly Predicate<string> m_SupportsSampleKind;
        readonly Dictionary<TimelineData, TimelineRuntimePreparedContent> m_Contents = new();
        readonly TimelineRuntimePresentationDriver m_PresentationDriver;

        public TimelineRuntimeCompositionHost(
            TimelineRuntimeNumericTarget numericTarget,
            IReadOnlyList<TimelineCallBinding> callBindings,
            ITimelineDomainBindingResolver domainResolver,
            ITimelineRuntimeDependencyResolver dependencyResolver,
            Predicate<string> supportsSampleKind,
            int playbackCapacity)
        {
            m_NumericTarget = numericTarget;
            m_CallBindings = callBindings;
            m_DomainResolver = domainResolver;
            m_DependencyResolver = dependencyResolver;
            m_SupportsSampleKind = supportsSampleKind;
            m_PresentationDriver = new TimelineRuntimePresentationDriver(playbackCapacity);
        }

        public TimelineRuntimePreparedContent InstallContent(
            TimelineData timeline,
            TimelineContentUnit content,
            OrchestrationIdentityTable identities,
            IReadOnlyList<OrchestrationTimelinePlaybackMode> playbackModes)
        {
            TimelineRuntimePreparedContent prepared = TimelineRuntimePreparation.PrepareContent(
                content, m_NumericTarget, m_CallBindings, m_DomainResolver, m_DependencyResolver, identities);
            if (!prepared.IsReady)
                throw new InvalidOperationException(string.Join(" | ", prepared.Errors));
            for (int index = 0; index < playbackModes.Count; index++)
                m_PresentationDriver.Prepare(prepared, playbackModes[index], m_SupportsSampleKind);
            m_Contents.Add(timeline, prepared);
            return prepared;
        }

        public TimelineRuntimePreparedContent GetContent(TimelineData timeline) => m_Contents[timeline];

        public void RetireContent(
            TimelineData timeline,
            TimelineContentUnit content,
            IReadOnlyList<OrchestrationTimelinePlaybackMode> playbackModes)
        {
            m_Contents.Remove(timeline);
            for (int index = 0; index < playbackModes.Count; index++)
                m_PresentationDriver.RetireContent(content, playbackModes[index]);
        }

        public TimelineRuntimePreparedContent InstallTimeRangeCandidate(TimelineData baseline, TimelineData timeline,
            TimelineContentUnit content, OrchestrationIdentityTable identities,
            IReadOnlyList<OrchestrationTimelinePlaybackMode> playbackModes)
        {
            var prepared = new TimelineRuntimePreparedContent(m_Contents[baseline], content, identities);
            for (int index = 0; index < playbackModes.Count; index++)
                m_PresentationDriver.Prepare(prepared, playbackModes[index], m_SupportsSampleKind);
            m_Contents.Add(timeline, prepared);
            return prepared;
        }

        public bool TryPresent(
            TimelineData timeline,
            int playbackSlot,
            in OrchestrationTimelineStart<OrchestrationTime> start,
            in TimelineRuntimePresentationSample sample,
            ulong presentationFrame,
            float presentationDeltaSeconds,
            float interpolationAlpha,
            out TimelineRuntimePresentationFrame frame) =>
            m_PresentationDriver.TryPresent(m_Contents[timeline], playbackSlot, in start, in sample,
                presentationFrame, presentationDeltaSeconds, interpolationAlpha, out frame);

        public bool RequestPresentationTreeClipExit(in TimelineRuntimePresentationFrame frame, in TimelineRuntimeTreeClipRequest request) =>
            m_PresentationDriver.RequestTreeClipExit(in frame, in request);

        public void CommitPresentationFrame(ulong frame) => m_PresentationDriver.CommitPresentationFrame(frame);
        public void DiscardPresentationFrame(ulong frame) => m_PresentationDriver.DiscardPresentationFrame(frame);
        public void SuspendPresentationPlayback(ulong playbackIdentity, ulong sourceGeneration) =>
            m_PresentationDriver.SuspendPresentationPlayback(playbackIdentity, sourceGeneration);
        public void ReleasePresentationPlayback(ulong playbackIdentity, ulong sourceGeneration) =>
            m_PresentationDriver.ReleasePresentationPlayback(playbackIdentity, sourceGeneration);

        public void Dispose()
        {
            m_PresentationDriver.Clear();
            m_Contents.Clear();
        }
    }
}
