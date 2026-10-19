using System;
using System.Collections.Generic;
using BTSMTL.Compilation.Analysis;

namespace BTSMTL.Timeline.Editor
{
    public interface ITimelineOrchestrationAuthoringHost
    {
        event Action OrchestrationQueriesChanged;
        bool TryGetOrchestrationQueries(TimelineData timeline,
            out IReadOnlyList<TimelineOrchestrationQuery> queries, out string reason);
    }

    public sealed class TimelineOrchestrationQuery
    {
        readonly Func<string, TimelineAsset> m_ResolveTimeline;
        readonly Action<string, string> m_OpenClip;
        readonly Action<OrchestrationStaticSourceLocation> m_OpenTransition;
        readonly Action<OrchestrationStaticSourceLocation> m_OpenState;

        public TimelineOrchestrationQuery(string label, OrchestrationStaticQuery query,
            Func<string, TimelineAsset> resolveTimeline, Action<string, string> openClip,
            Action<OrchestrationStaticSourceLocation> openTransition,
            Action<OrchestrationStaticSourceLocation> openState)
        {
            Label = label;
            Query = query;
            m_ResolveTimeline = resolveTimeline;
            m_OpenClip = openClip;
            m_OpenTransition = openTransition;
            m_OpenState = openState;
        }

        public string Label { get; }
        public OrchestrationStaticQuery Query { get; }
        public TimelineAsset ResolveTimeline(string identity) => m_ResolveTimeline(identity);
        public void OpenClip(string timelineIdentity, string clipId) => m_OpenClip(timelineIdentity, clipId);
        public void OpenTransition(OrchestrationStaticSourceLocation location) => m_OpenTransition(location);
        public void OpenState(OrchestrationStaticSourceLocation location) => m_OpenState(location);
    }
}
