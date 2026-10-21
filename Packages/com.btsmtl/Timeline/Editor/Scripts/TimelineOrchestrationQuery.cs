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
        // 所选配置下的全部技能：预览起点可从任一技能根开始，不限于引用当前 Timeline 的技能。
        bool TryGetAllOrchestrationQueries(out IReadOnlyList<TimelineOrchestrationQuery> queries, out string reason);
    }

    public sealed class TimelineOrchestrationQuery
    {
        readonly Func<string, TimelineAsset> m_ResolveTimeline;
        readonly Action<string, string> m_OpenClip;
        readonly Action<OrchestrationStaticSourceLocation> m_OpenTransition;
        readonly Action<OrchestrationStaticSourceLocation> m_OpenState;
        readonly Func<OrchestrationStaticSourceLocation, string> m_DescribeState;

        public TimelineOrchestrationQuery(string owner, string label, OrchestrationStaticQuery query,
            Func<string, TimelineAsset> resolveTimeline, Action<string, string> openClip,
            Action<OrchestrationStaticSourceLocation> openTransition,
            Action<OrchestrationStaticSourceLocation> openState,
            Func<OrchestrationStaticSourceLocation, string> describeState)
        {
            if (string.IsNullOrEmpty(owner))
                throw new InvalidOperationException($"技能 '{label}' 缺少正式身份，无法归属预览数据。");
            Owner = owner;
            Label = label;
            Query = query;
            m_ResolveTimeline = resolveTimeline;
            m_OpenClip = openClip;
            m_OpenTransition = openTransition;
            m_OpenState = openState;
            m_DescribeState = describeState;
        }

        // 所属技能的正式身份，用于按入口归属预览数据。
        public string Owner { get; }
        public string Label { get; }
        public OrchestrationStaticQuery Query { get; }
        public TimelineAsset ResolveTimeline(string identity) => m_ResolveTimeline(identity);
        public void OpenClip(string timelineIdentity, string clipId) => m_OpenClip(timelineIdentity, clipId);
        public void OpenTransition(OrchestrationStaticSourceLocation location) => m_OpenTransition(location);
        public void OpenState(OrchestrationStaticSourceLocation location) => m_OpenState(location);
        public string DescribeState(OrchestrationStaticState state) =>
            state == null ? "状态机出口" : m_DescribeState(state.Location);
    }
}
