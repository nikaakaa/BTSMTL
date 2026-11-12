using BTSMTL.Compilation.Analysis;
using BTSMTL.Orchestration;
using BTSMTL.Timeline;

namespace BTSMTL.WebWorkbench;

sealed record QuickPreviewCompilation(IPreviewQuickCompiledContent Content,
    IReadOnlyDictionary<string, OrchestrationStaticQuery> Queries, QuickPreviewProgramPublication Publication, OrchestrationStaticQuery? Query,
    int Machine, TimelineContentUnit? Standalone, PreviewQuickMachine[] Machines)
{
    public static QuickPreviewCompilation Compile(IPreviewQuickCapture capture)
    {
        IPreviewQuickCompiledContent content = capture.Compile();
        var queries = content.Graphs.ToDictionary(pair => pair.Key, pair => OrchestrationStaticQuery.Build(pair.Value), StringComparer.Ordinal);
        QuickPreviewProgramPublication publication = QuickPreviewProgramPublication.Build(content, queries);
        PreviewQuickAuthoringTarget root = capture.Identity.Authoring;
        if (root.Entry is PreviewQuickTimelineEntry timeline)
            return new(content, queries, publication, null, -1, content.Timelines.Single(unit => unit.AuthoringId == timeline.TimelineId), []);
        var graph = (PreviewQuickGraphEntry)root.Entry;
        OrchestrationStaticQuery query = queries[graph.GraphId];
        var timelines = content.Timelines.ToDictionary(unit => unit.Identity, StringComparer.Ordinal);
        PreviewQuickMachine[] machines = query.RootMachineOperations.Select((operation, index) =>
        {
            TimelineContentUnit? entry = query.InitialTransitions(operation).Select(transition => transition.Target)
                .Where(state => state != null && state.TimelineIdentity.Length != 0)
                .Select(state => timelines[state.TimelineIdentity]).FirstOrDefault();
            return new PreviewQuickMachine(root with { Entry = graph with { Machine = index } }, operation, entry?.Name, entry?.AuthoringId);
        }).ToArray();
        if (machines.Length == 0)
            throw new ArgumentException("所选入口图还没有根状态机：在节点图里给入口接上状态机，或在快预览入口里改选「单看」某条 Timeline。");
        if (graph.Machine < 0 || graph.Machine >= machines.Length)
            throw new ArgumentException($"所选图有 {machines.Length} 个根状态机，machine={graph.Machine} 越界。");
        if (machines[graph.Machine].EntryTimelineId == null)
            throw new InvalidDataException("所选状态机的起始状态没有正式 Timeline，无法生成快预览画面。");
        return new(content, queries, publication, query, machines[graph.Machine].Operation, null, machines);
    }

    public TimelineStaticRoute Route(QuickPreviewInputSchedule input)
    {
        string[] missing = input.Conditions.Where(point => !Publication.RuntimeConditions.ContainsKey(point.Key))
            .Select(point => $"{point.Element ?? "试用条件"}：条件 '{point.Key}' 不属于本次入口编译的运行数据读取。").ToArray();
        if (missing.Length != 0) throw new InvalidDataException(string.Join("\n", missing));
        var end = OrchestrationTime.FromSeconds((float)input.RangeEnd);
        return Query == null ? TimelineStaticRoute.BuildStandalone(Standalone!, end)
            : TimelineStaticRoute.Build(Query, Machine, end, input.Intervals);
    }

    public TimelineStaticRoute Route(QuickPreviewInputSchedule input, string? graphId,
        IReadOnlyDictionary<string, TimelineContentUnit> candidates)
    {
        var end = OrchestrationTime.FromSeconds((float)input.RangeEnd);
        if (Query == null)
            return TimelineStaticRoute.BuildStandalone(candidates.GetValueOrDefault(Standalone!.Identity) ?? Standalone, end);
        var inputs = new Dictionary<string, OrchestrationStaticQueryInput>(StringComparer.Ordinal);
        OrchestrationStaticQueryInput Input(string id)
        {
            if (inputs.TryGetValue(id, out var existing)) return existing;
            var source = Content.Graphs[id];
            var value = new OrchestrationStaticQueryInput(source.Operations, source.Literals, source.ControlFlow, source.SourceMap,
                identity => candidates.GetValueOrDefault(identity) ?? source.ResolveTimeline(identity),
                nested => Content.Graphs.ContainsKey(nested) ? Input(nested) : null!, source.Declarations,
                source.References, source.ConstantInputs, source.Identities, source.NumberDefaults);
            inputs.Add(id, value);
            return value;
        }
        return TimelineStaticRoute.Build(OrchestrationStaticQuery.Build(Input(graphId!)), Machine, end, input.Intervals);
    }
}
