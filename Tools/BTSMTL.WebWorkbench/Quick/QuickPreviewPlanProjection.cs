using BTSMTL.Compilation.Analysis;
using BTSMTL.Orchestration;
using BTSMTL.Timeline;

namespace BTSMTL.WebWorkbench;

static class QuickPreviewPlanProjection
{
    public static PreviewQuickPlan Describe(IPreviewQuickCapture capture, IPreviewQuickEnvironment actor,
        QuickPreviewCompilation compiled, QuickPreviewInputSchedule input, TimelineStaticRoute route)
    {
        var (conditions, suggestions) = route.Conditions(input.Intervals);
        var dependencies = new QuickPreviewConditionDependencies(route, input.Conditions);
        PreviewQuickSegment[] segments = route.OrderedSpans().Select(item =>
        {
            TimelineStaticRouteSpan span = item.Span;
            TimelineContentUnit timeline = compiled.Content.Timelines.Single(unit => unit.Identity == span.TimelineIdentity);
            return new PreviewQuickSegment(timeline.Name, timeline.AuthoringId, capture.TimelineOwner(timeline.AuthoringId),
                State(span.State), span.State == null ? null : Location(span.State.Location), Seconds(span.Start), Seconds(span.End),
                Seconds(span.LocalStart), Seconds(span.LocalEnd), span.Playback, span.Cycle, item.Occurrence,
                span.PlaybackCompleted, span.CallerPlayback, span.Call?.CallerElementId ?? "")
            {
                ConditionDependencies = dependencies.Segment(span)
            };
        }).ToArray();
        return new(compiled.Standalone?.Name ?? capture.Label, "staticInference", new("seconds", 0, "Player", actor.TickRate),
            input.Scheme.Source, input.Scheme.Revision, input.Revision, input.Conditions,
            ["按作者文档和本次输入轨计算会播哪些 Timeline。", "位移未经玩法碰撞求解，运行条件按快预览输入取值。"],
            Seconds(route.End), route.Chain?.StopReason.ToString(), route.Chain?.Reason, "已生成", segments,
            conditions.Select(read => new PreviewQuickCondition(read.Key, read.Label, read.Source.ToString(), read.OperationIndex, read.Undeterminable)).ToArray(),
            suggestions.Select(suggestion => new PreviewQuickSuggestion(State(suggestion.State), State(suggestion.Transition.Target),
                suggestion.Transition.Identity, suggestion.State == null ? null : Location(suggestion.State.Location),
                suggestion.Transition.Target == null ? null : Location(suggestion.Transition.Target.Location), Location(suggestion.Transition.Location),
                Seconds(suggestion.Start), Seconds(suggestion.End),
                suggestion.Missing.Select(read => new PreviewQuickMissingCondition(read.Key, read.Label)).ToArray())).ToArray(),
            actor.Inputs.Select(descriptor => new PreviewQuickInputDeclaration(descriptor.InputId, descriptor.ValueKind.ToString(),
                descriptor.ValueType.Name)).ToArray(), input.Points, input.InputSource);

        PreviewQuickLocation Location(OrchestrationStaticSourceLocation location) =>
            new(string.IsNullOrEmpty(location.GraphId) ? capture.Identity.Authoring.DocumentPath : capture.GraphOwner(location.GraphId),
                location.GraphId ?? "", location.NodeId ?? "", location.EdgeId ?? "", location.DisplayPath ?? "");
    }

    static string State(OrchestrationStaticState? state) => state?.Name ?? "出口";
    static double Seconds(OrchestrationTime time) => time.RawValue / (double)OrchestrationTime.Scale;
}
