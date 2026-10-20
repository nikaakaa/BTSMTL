using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;

namespace BTSMTL.Compilation.Analysis
{
    // 一条转移的可走时段：同一状态键取各提供 Clip 区间的并集，多个状态键再取交集；时间为所在状态 Timeline 的本地时刻，左闭右开。
    public sealed class OrchestrationTransitionWindow
    {
        readonly List<TimelineStaticInterval> m_Intervals = new();
        readonly List<OrchestrationStaticClip> m_Providers = new();
        readonly List<string> m_StateKeys = new();
        readonly List<OrchestrationStaticRead> m_HostInputs = new();

        internal OrchestrationTransitionWindow(OrchestrationStaticTransition transition) => Transition = transition;

        public OrchestrationStaticTransition Transition { get; }
        // 条件不读取任何 Clip 提供的状态：从状态进入起随时可走。
        public bool Anytime => m_StateKeys.Count == 0;
        public IReadOnlyList<TimelineStaticInterval> Intervals => m_Intervals;
        public IReadOnlyList<OrchestrationStaticClip> Providers => m_Providers;
        public IReadOnlyList<string> StateKeys => m_StateKeys;
        public IReadOnlyList<OrchestrationStaticRead> HostInputs => m_HostInputs;
        public string InputLabel
        {
            get
            {
                var labels = new List<string>(m_HostInputs.Count);
                foreach (OrchestrationStaticRead read in m_HostInputs)
                    if (!labels.Contains(read.Label))
                        labels.Add(read.Label);
                return string.Join(" + ", labels);
            }
        }

        public string TargetLabel => Transition.Target?.Name ?? "出口";

        // 节点时间条每行："按键 → 目标"；不读宿主输入时只写目标。
        public string RowLabel => m_HostInputs.Count != 0 ? $"{InputLabel} → {TargetLabel}" : TargetLabel;

        // 连线标签：有时段写"按键 · 起–止 s"，随时可走写"按键 · 随时"。
        public string EdgeLabel
        {
            get
            {
                string input = m_HostInputs.Count != 0 ? InputLabel : TargetLabel;
                if (Anytime)
                    return $"{input} · 随时";
                if (m_Intervals.Count == 0)
                    return $"{input} · 无共同时段";
                TimelineStaticInterval first = m_Intervals[0];
                TimelineStaticInterval last = m_Intervals[m_Intervals.Count - 1];
                return $"{input} · {first.Start.AsSingle:0.##}–{last.End.AsSingle:0.##} s";
            }
        }

        internal static OrchestrationTransitionWindow Build(OrchestrationStaticTransition transition,
            OrchestrationStaticTimeline timeline, List<(OrchestrationStaticTransition, OrchestrationStaticRead)> unresolved)
        {
            var window = new OrchestrationTransitionWindow(transition);
            List<TimelineStaticInterval> intervals = null;
            foreach (OrchestrationStaticRead read in transition.Reads)
            {
                if (read.Source == OrchestrationStaticReadSource.HostInput)
                {
                    window.m_HostInputs.Add(read);
                    continue;
                }
                if (read.Source != OrchestrationStaticReadSource.ClipState || window.m_StateKeys.Contains(read.Key))
                    continue;
                var union = new List<TimelineStaticInterval>();
                if (timeline != null)
                    foreach (OrchestrationStaticClip clip in timeline.Clips)
                        if (Contains(clip.ProvidedStateKeys, read.Key))
                        {
                            union.Add(new TimelineStaticInterval(clip.Start, clip.End));
                            if (!window.m_Providers.Contains(clip))
                                window.m_Providers.Add(clip);
                        }
                if (union.Count == 0)
                {
                    unresolved.Add((transition, OrchestrationStaticReads.EffectiveRead(timeline, in read)));
                    continue;
                }
                window.m_StateKeys.Add(read.Key);
                Normalize(union);
                intervals = intervals == null ? union : Intersect(intervals, union);
            }
            if (intervals != null)
                window.m_Intervals.AddRange(intervals);
            return window;
        }

        static void Normalize(List<TimelineStaticInterval> values)
        {
            values.Sort((left, right) => left.Start.CompareTo(right.Start));
            int count = 0;
            for (int i = 0; i < values.Count; i++)
            {
                TimelineStaticInterval value = values[i];
                if (value.Start >= value.End)
                    continue;
                if (count != 0 && value.Start <= values[count - 1].End)
                    values[count - 1] = new TimelineStaticInterval(values[count - 1].Start,
                        OrchestrationTime.Max(values[count - 1].End, value.End));
                else
                    values[count++] = value;
            }
            values.RemoveRange(count, values.Count - count);
        }

        static List<TimelineStaticInterval> Intersect(List<TimelineStaticInterval> left, List<TimelineStaticInterval> right)
        {
            var result = new List<TimelineStaticInterval>();
            int i = 0;
            int j = 0;
            while (i < left.Count && j < right.Count)
            {
                OrchestrationTime start = OrchestrationTime.Max(left[i].Start, right[j].Start);
                OrchestrationTime end = OrchestrationTime.Min(left[i].End, right[j].End);
                if (start < end)
                    result.Add(new TimelineStaticInterval(start, end));
                if (left[i].End <= right[j].End)
                    i++;
                else
                    j++;
            }
            return result;
        }

        internal static bool Contains(IReadOnlyList<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values[i], value, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }

    // 单个状态各出边的可走时段：由静态查询即时推导，不保存副本；内容变化后重新查询。
    public sealed class OrchestrationStateTransitionWindows
    {
        readonly List<OrchestrationTransitionWindow> m_Windows = new();
        readonly List<(OrchestrationStaticTransition Transition, OrchestrationStaticRead Read)> m_UnresolvedReads = new();

        OrchestrationStateTransitionWindows(OrchestrationStaticState state) => State = state;

        public OrchestrationStaticState State { get; }
        public OrchestrationStaticTimeline Timeline => State.Timeline;
        public IReadOnlyList<OrchestrationTransitionWindow> Windows => m_Windows;
        // 读取了状态键但本 Timeline 没有 Clip 在入口无条件提供它：计入其它运行数据，标无法静态判断。
        public IReadOnlyList<(OrchestrationStaticTransition Transition, OrchestrationStaticRead Read)> UnresolvedStateReads => m_UnresolvedReads;

        public OrchestrationTransitionWindow Of(OrchestrationStaticTransition transition)
        {
            foreach (OrchestrationTransitionWindow window in m_Windows)
                if (window.Transition == transition)
                    return window;
            return null;
        }

        public static OrchestrationStateTransitionWindows Build(OrchestrationStaticState state)
        {
            var result = new OrchestrationStateTransitionWindows(state);
            var unresolved = new List<(OrchestrationStaticTransition, OrchestrationStaticRead)>();
            foreach (OrchestrationStaticTransition transition in state.Transitions)
                result.m_Windows.Add(OrchestrationTransitionWindow.Build(transition, state.Timeline, unresolved));
            result.m_UnresolvedReads.AddRange(unresolved);
            return result;
        }
    }

    // 读取分类给推导与界面：Clip 提供的状态未被任何 Clip 提供时，按其它运行数据处理。
    public static class OrchestrationStaticReads
    {
        public static OrchestrationStaticReadSource EffectiveSource(OrchestrationStaticTimeline timeline,
            in OrchestrationStaticRead read)
        {
            if (read.Source != OrchestrationStaticReadSource.ClipState)
                return read.Source;
            if (timeline != null)
                foreach (OrchestrationStaticClip clip in timeline.Clips)
                    if (OrchestrationTransitionWindow.Contains(clip.ProvidedStateKeys, read.Key))
                        return OrchestrationStaticReadSource.ClipState;
            return OrchestrationStaticReadSource.RuntimeData;
        }

        public static OrchestrationStaticRead EffectiveRead(OrchestrationStaticTimeline timeline,
            in OrchestrationStaticRead read)
        {
            OrchestrationStaticReadSource source = EffectiveSource(timeline, in read);
            return source == read.Source ? read : new OrchestrationStaticRead(read.OperationIndex,
                source, read.Key, true, read.Label);
        }
    }
}
