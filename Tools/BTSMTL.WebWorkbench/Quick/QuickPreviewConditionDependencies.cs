using BTSMTL.Compilation.Analysis;
using BTSMTL.Orchestration;

namespace BTSMTL.WebWorkbench;

sealed class QuickPreviewConditionDependencies
{
    readonly Dictionary<(OrchestrationStaticState State, long Start, int Cycle), PreviewQuickConditionDependency[]> m_States = new();
    readonly Dictionary<int, List<(long Start, PreviewQuickConditionDependency[] Dependencies)>> m_Playbacks = new();
    readonly Dictionary<string, (long Start, long End)[]> m_Written;

    public QuickPreviewConditionDependencies(TimelineStaticRoute route, PreviewQuickConditionPoint[] conditions)
    {
        m_Written = conditions.GroupBy(point => point.Key).ToDictionary(group => group.Key, group =>
        {
            PreviewQuickConditionPoint[] points = group.OrderBy(point => point.Time).ToArray();
            return points.Select((point, index) => (Point: point, End: index + 1 < points.Length
                    ? Raw(points[index + 1].Time) : long.MaxValue))
                .Where(item => !item.Point.RestoreDefault)
                .Select(item => (Start: Raw(item.Point.Time), item.End)).ToArray();
        }, StringComparer.Ordinal);
        if (route.Chain != null)
            AddChain(route.Chain, []);
    }

    void AddChain(TimelineStaticChain chain, PreviewQuickConditionDependency[] inherited)
    {
        var dependencies = inherited.ToDictionary(item => item.Key, StringComparer.Ordinal);
        if (chain.Spans.Count > 0)
            AddReads(dependencies, chain.InitialReads, chain.Spans[0].Start, chain.Spans[0].Start);
        foreach (TimelineStaticChainSpan span in chain.Spans)
        {
            AddReads(dependencies, span.RuntimeReads, span.Start, span.End);
            m_States.Add((span.State, span.Start.RawValue, span.Cycle), dependencies.Values.ToArray());
        }
        foreach (TimelineStaticChainBranch branch in chain.Branches)
            AddChain(branch.Chain, m_States[(branch.Owner, branch.Start.RawValue, 0)]);
    }

    void AddReads(Dictionary<string, PreviewQuickConditionDependency> dependencies,
        IReadOnlyList<OrchestrationStaticRead> reads, OrchestrationTime start, OrchestrationTime end)
    {
        foreach (OrchestrationStaticRead read in reads)
        {
            bool written = Written(read.Key, start.RawValue, end.RawValue);
            if (dependencies.TryGetValue(read.Key, out var previous))
                written |= previous.Written;
            dependencies[read.Key] = new(read.Key, read.Label, written);
        }
    }

    bool Written(string key, long start, long end)
    {
        if (!m_Written.TryGetValue(key, out var intervals))
            return false;
        int first = 0, last = intervals.Length;
        while (first < last)
        {
            int middle = (first + last) / 2;
            if (intervals[middle].End <= start) first = middle + 1;
            else last = middle;
        }
        return first < intervals.Length && intervals[first].Start <= end;
    }

    public PreviewQuickConditionDependency[] Segment(TimelineStaticRouteSpan span)
    {
        PreviewQuickConditionDependency[] dependencies = span.State != null
            ? m_States[(span.State, span.Start.RawValue, span.Cycle)]
            : span.CallerPlayback >= 0
                ? m_Playbacks[span.CallerPlayback].Last(item => item.Start <= span.Start.RawValue).Dependencies
                : [];
        if (!m_Playbacks.TryGetValue(span.Playback, out var playback))
            m_Playbacks.Add(span.Playback, playback = []);
        playback.Add((span.Start.RawValue, dependencies));
        return dependencies;
    }

    static long Raw(double seconds) => OrchestrationTime.FromSeconds(seconds).RawValue;
}
