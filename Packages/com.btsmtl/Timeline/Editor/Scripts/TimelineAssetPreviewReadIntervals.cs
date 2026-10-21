using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;

namespace BTSMTL.Timeline.Editor
{
    public sealed class TimelineAssetPreviewReadIntervals : ITimelineStaticReadIntervals
    {
        readonly TimelineAssetPreviewPreferences m_Preferences;
        readonly Dictionary<string, IReadOnlyList<TimelineStaticInterval>> m_Intervals = new(StringComparer.Ordinal);

        public TimelineAssetPreviewReadIntervals(TimelineAssetPreviewPreferences preferences,
            IReadOnlyList<GraphHostInputDescriptor> descriptors,
            IReadOnlyList<TimelinePreviewInputRequest> requests)
        {
            m_Preferences = preferences;
            var requestInputs = requests.ToDictionary(request => request.InputId, StringComparer.Ordinal);
            foreach (var input in preferences.Inputs.GroupBy(point => point.InputId))
            {
                TimelinePreviewInputPoint[] points = input.OrderBy(point => point.Time).ToArray();
                GraphHostInputDescriptor descriptor = descriptors.FirstOrDefault(value => value.InputId == input.Key);
                if (descriptor == null)
                    throw new TimelineAssetPreviewInputUnavailableException($"预览日程中的输入 '{input.Key}' 已不在当前角色的正式输入声明中，请删除该日程项。");
                var intervals = new List<TimelineStaticInterval>();
                if (requestInputs.TryGetValue(input.Key, out TimelinePreviewInputRequest request))
                {
                    foreach (TimelinePreviewInputPoint point in points)
                        if (point.Boolean)
                            intervals.Add(new TimelineStaticInterval(OrchestrationTime.FromSeconds(point.Time),
                                OrchestrationTime.FromSeconds(point.Time + request.DurationSeconds)));
                }
                else
                    for (int i = 0; i < points.Length; i++)
                        if (descriptor.ValueKind != GraphHostValueKind.Bool || points[i].Boolean)
                            intervals.Add(new TimelineStaticInterval(OrchestrationTime.FromSeconds(points[i].Time),
                                OrchestrationTime.FromSeconds(i + 1 < points.Length ? points[i + 1].Time : preferences.RangeEnd)));
                m_Intervals.Add(input.Key, intervals);
            }
        }

        public IReadOnlyList<TimelineStaticInterval> HostInput(string inputId) =>
            m_Intervals.TryGetValue(inputId, out IReadOnlyList<TimelineStaticInterval> intervals)
                ? intervals : Array.Empty<TimelineStaticInterval>();

        public bool RuntimeData(in OrchestrationStaticRead read)
        {
            string identity = ConditionIdentity(in read);
            foreach (TimelinePreviewConditionAssumption condition in m_Preferences.Conditions)
                if (condition.Identity == identity)
                    return condition.Satisfied;
            return DefaultSatisfied(in read);
        }

        // 资产预览只运行本链上的 Clip：没有 Clip 提供的 Clip 状态默认未成立，其它运行数据默认成立。
        public static bool DefaultSatisfied(in OrchestrationStaticRead read) =>
            read.Source != OrchestrationStaticReadSource.ClipState;

        public static string ConditionIdentity(in OrchestrationStaticRead read) => $"{read.OperationIndex}:{read.Key}";

    }
}
