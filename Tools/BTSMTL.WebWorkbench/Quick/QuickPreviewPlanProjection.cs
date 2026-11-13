using BTSMTL.Compilation.Analysis;
using BTSMTL.Orchestration;
using BTSMTL.Timeline;

namespace BTSMTL.WebWorkbench;

static class QuickPreviewPlanProjection
{
    public static PreviewQuickPlan Describe(IPreviewQuickCapture capture, IPreviewQuickEnvironment actor,
        QuickPreviewCompilation compiled, QuickPreviewInputSchedule input, TimelineStaticRoute route, QuickPreviewInstances instances)
    {
        var (conditions, suggestions) = route.Conditions(input.Intervals);
        var dependencies = new QuickPreviewConditionDependencies(route, input.Conditions);
        IReadOnlyList<QuickPreviewInstances.Placed> placed = instances.Place(capture, compiled, route, input.RangeEnd, Location);
        // 子实例的段按生成时刻平移到全局时间，超出预览范围的部分截掉。
        PreviewQuickSegment[] segments = placed.Where(item => item.Route != null).SelectMany(item =>
            item.Route!.OrderedSpans().Select(entry =>
            {
                TimelineStaticRouteSpan span = entry.Span;
                IPreviewQuickCapture owner = item.Capture;
                TimelineContentUnit timeline = item.Compiled!.Content.Timelines.Single(unit => unit.Identity == span.TimelineIdentity);
                double start = item.Offset + Seconds(span.Start), end = Math.Min(input.RangeEnd, item.Offset + Seconds(span.End));
                double localEnd = Seconds(span.LocalStart) + end - start;
                return (Root: item.Instance.Id == 0, Start: start, Segment: new PreviewQuickSegment(item.Instance.Id, timeline.Name,
                    timeline.AuthoringId, owner.TimelineOwner(timeline.AuthoringId),
                    State(span.State), span.State == null ? null : Location(span.State.Location, owner), start, end,
                    Seconds(span.LocalStart), item.Instance.Id == 0 ? Seconds(span.LocalEnd) : localEnd, span.Playback, span.Cycle,
                    entry.Occurrence, span.PlaybackCompleted && end == item.Offset + Seconds(span.End), span.CallerPlayback,
                    span.Call?.CallerElementId ?? "")
                {
                    ConditionDependencies = item.Instance.Id == 0 ? dependencies.Segment(span) : []
                });
            }).Where(value => value.Root || value.Start < input.RangeEnd).Select(value => value.Segment)).ToArray();
        string root = placed[0].Instance.Root;
        return new(root, "staticInference", new("seconds", 0, "Player", actor.TickRate),
            input.Scheme.Source, input.Scheme.Revision, input.Revision, input.Conditions,
            ["按作者文档和本次输入轨计算会播哪些 Timeline。", "位移未经玩法碰撞求解，运行条件按快预览输入取值。",
                .. placed.Count > 1 ? ["被生成对象按自己的根图从生成时刻推演，运行条件取默认值。"] : Array.Empty<string>()],
            Seconds(route.End), route.Chain?.StopReason.ToString(), route.Chain?.Reason, "已生成",
            placed.Select(item => item.Instance).ToArray(), segments,
            conditions.Select(read => new PreviewQuickCondition(read.Key, read.Label, read.Source.ToString(), read.OperationIndex, read.Undeterminable)).ToArray(),
            suggestions.Select(suggestion => new PreviewQuickSuggestion(suggestion.State?.Name ?? "入口", State(suggestion.Transition.Target),
                suggestion.Transition.Identity, suggestion.State == null ? null : Location(suggestion.State.Location, capture),
                suggestion.Transition.Target == null ? null : Location(suggestion.Transition.Target.Location, capture),
                Location(suggestion.Transition.Location, capture), Seconds(suggestion.Start), Seconds(suggestion.End),
                suggestion.Missing.Select(read => new PreviewQuickMissingCondition(read.Key, read.Label)).ToArray(),
                suggestion.Branch.ToString())).ToArray(),
            actor.Inputs.Select(descriptor => new PreviewQuickInputDeclaration(descriptor.InputId, descriptor.ValueKind.ToString(),
                descriptor.ValueType.Name)).ToArray(), input.Points, input.InputSource);
    }

    static PreviewQuickLocation Location(OrchestrationStaticSourceLocation location, IPreviewQuickCapture capture) =>
        new(string.IsNullOrEmpty(location.GraphId) ? capture.Identity.Authoring.DocumentPath : capture.GraphOwner(location.GraphId),
            location.GraphId ?? "", location.NodeId ?? "", location.EdgeId ?? "", location.DisplayPath ?? "");

    static string State(OrchestrationStaticState? state) => state?.Name ?? "出口";
    static double Seconds(OrchestrationTime time) => time.RawValue / (double)OrchestrationTime.Scale;
}
