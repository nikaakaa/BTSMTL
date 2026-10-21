using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;
using UnityEngine;

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
                        if (Holds(descriptor, points[i]))
                            intervals.Add(new TimelineStaticInterval(OrchestrationTime.FromSeconds(points[i].Time),
                                OrchestrationTime.FromSeconds(i + 1 < points.Length ? points[i + 1].Time : preferences.RangeEnd)));
                m_Intervals.Add(input.Key, intervals);
            }
        }

        // 输入作为条件读取时：开关为真、数值与向量非零才算成立（如"有移动输入"读方向键非零）。
        static bool Holds(GraphHostInputDescriptor descriptor, TimelinePreviewInputPoint point) => descriptor.ValueKind switch
        {
            GraphHostValueKind.Bool => point.Boolean,
            GraphHostValueKind.Int32 or GraphHostValueKind.Enum => point.Integer != 0,
            GraphHostValueKind.Float32 => point.Scalar != 0f,
            GraphHostValueKind.Vector2 => point.Vector2 != Vector2.zero,
            GraphHostValueKind.Vector3 => point.Vector3 != Vector3.zero,
            _ => true
        };

        public IReadOnlyList<TimelineStaticInterval> HostInput(string inputId) =>
            m_Intervals.TryGetValue(inputId, out IReadOnlyList<TimelineStaticInterval> intervals)
                ? intervals : Array.Empty<TimelineStaticInterval>();

        // 运行数据按输入 Timeline 上"预览条件"的写入分段：没写过取默认值，写入后保持到同一条件的下一次写入。
        public IReadOnlyList<TimelineStaticInterval> RuntimeData(in OrchestrationStaticRead read)
        {
            bool fallback = DefaultSatisfied(in read);
            bool value = fallback;
            var result = new List<TimelineStaticInterval>();
            OrchestrationTime start = OrchestrationTime.Zero;
            foreach (TimelinePreviewConditionPoint point in m_Preferences.ConditionPoints)
            {
                bool next = point.RestoreDefault ? fallback : point.Satisfied;
                if (point.Key != read.Key || next == value)
                    continue;
                OrchestrationTime at = OrchestrationTime.FromSeconds(point.Time);
                if (value && at > start)
                    result.Add(new TimelineStaticInterval(start, at));
                value = next;
                start = at;
            }
            if (value)
                result.Add(new TimelineStaticInterval(start, OrchestrationTime.MaxValue));
            return result;
        }

        // 资产预览只运行本链上的 Clip：没有 Clip 提供的 Clip 状态默认未成立，其它运行数据默认成立。
        public static bool DefaultSatisfied(in OrchestrationStaticRead read) =>
            read.Source != OrchestrationStaticReadSource.ClipState;

        // 某时刻条件的取值与来源（输入 Timeline 写入或默认）。
        public bool ValueAt(in OrchestrationStaticRead read, OrchestrationTime time, out bool written)
        {
            bool fallback = DefaultSatisfied(in read);
            bool value = fallback;
            written = false;
            foreach (TimelinePreviewConditionPoint point in m_Preferences.ConditionPoints)
                if (point.Key == read.Key && OrchestrationTime.FromSeconds(point.Time) <= time)
                {
                    value = point.RestoreDefault ? fallback : point.Satisfied;
                    written = !point.RestoreDefault;
                }
            return value;
        }
    }
}
