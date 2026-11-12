using BTSMTL.Compilation.Analysis;
using BTSMTL.Orchestration;
using BTSMTL.Timeline;

namespace BTSMTL.WebWorkbench;

// 快档执行实例：入口实例沿根图推演；路线经过适配器声明的生成点时，被生成对象按自己的根图另起实例，
// 从生成时刻接上，活到它的路线结束或预览范围终点。子实例的捕获、编译与本地路线按生成点缓存，
// 改片段时间重推父路线时只重排生成时刻。
sealed class QuickPreviewInstances(IPreviewQuickEnvironment actor)
{
    const int DepthLimit = 3;
    const int InstanceLimit = 32;

    sealed record Child(IPreviewQuickCapture? Capture, QuickPreviewCompilation? Compiled, TimelineStaticRoute? Route, string? Error);
    readonly Dictionary<(string Document, string Graph, string Node), Child> m_Children = new();

    public sealed record Placed(PreviewRouteInstance Instance, IPreviewQuickCapture Capture, QuickPreviewCompilation? Compiled,
        TimelineStaticRoute? Route, double Offset);

    public async Task Prepare(IPreviewQuickCapture capture, TimelineStaticRoute route, double rangeEnd,
        CancellationToken cancellation, int depth = 0)
    {
        if (depth >= DepthLimit)
            return;
        foreach (PreviewQuickSpawn spawn in Hits(capture, route).Select(hit => hit.Spawn).Distinct())
        {
            var key = (capture.Identity.Authoring.DocumentPath, spawn.GraphId, spawn.NodeId);
            if (m_Children.ContainsKey(key))
                continue;
            Child child;
            try
            {
                IPreviewQuickCapture captured = await capture.CaptureSpawn(spawn, cancellation);
                var compiled = await Task.Run(() => QuickPreviewCompilation.Compile(captured), cancellation);
                var end = OrchestrationTime.FromSeconds((float)rangeEnd);
                TimelineStaticRoute local = compiled.Query == null
                    ? TimelineStaticRoute.BuildStandalone(compiled.Standalone!, end)
                    : TimelineStaticRoute.Build(compiled.Query, compiled.Machine, end,
                        new TimelineStaticReadIntervals([], [], _ => null, [], rangeEnd));
                child = new(captured, compiled, local, null);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                child = new(null, null, null, "子实例静态推演不可用：" + error.Message);
            }
            m_Children[key] = child;
            if (child.Route != null)
                await Prepare(child.Capture!, child.Route, rangeEnd, cancellation, depth + 1);
        }
    }

    public IReadOnlyList<Placed> Place(IPreviewQuickCapture capture, QuickPreviewCompilation compiled,
        TimelineStaticRoute route, double rangeEnd, Func<OrchestrationStaticSourceLocation, IPreviewQuickCapture, PreviewQuickLocation> location)
    {
        string root = compiled.Standalone?.Name ?? capture.Label;
        var placed = new List<Placed>
        {
            new(new(0, null, actor.ActorId, root, capture.Identity.Authoring.DocumentPath, 0, Seconds(route.End),
                route.Chain?.Reason, null), capture, compiled, route, 0)
        };
        for (int index = 0; index < placed.Count && placed.Count < InstanceLimit; index++)
        {
            Placed parent = placed[index];
            if (parent.Route == null || Depth(placed, parent) >= DepthLimit)
                continue;
            foreach (var (spawn, call, time) in Hits(parent.Capture, parent.Route))
            {
                double start = parent.Offset + Seconds(time);
                if (start >= rangeEnd || placed.Count >= InstanceLimit)
                    continue;
                m_Children.TryGetValue((parent.Capture.Identity.Authoring.DocumentPath, spawn.GraphId, spawn.NodeId), out Child? child);
                string? error = child?.Error ?? (child == null ? "改片段时间后新经过的生成点：重新推演后显示子路线。" : null);
                double end = child?.Route == null ? rangeEnd : Math.Min(rangeEnd, start + Seconds(child.Route.End));
                string childRoot = child?.Compiled?.Standalone?.Name ?? child?.Capture?.Label ?? Path.GetFileName(spawn.DocumentPath);
                placed.Add(new(new(placed.Count, parent.Instance.Id, spawn.Carrier, childRoot, spawn.DocumentPath, start, end,
                        error ?? child!.Route!.Chain?.Reason, location(call.Location, parent.Capture)),
                    child?.Capture ?? parent.Capture, child?.Compiled, child?.Route, start));
            }
        }
        return placed;
    }

    static int Depth(List<Placed> placed, Placed instance)
    {
        int depth = 0;
        for (int? parent = instance.Instance.Parent; parent != null; parent = placed[parent.Value].Instance.Parent)
            depth++;
        return depth;
    }

    static IEnumerable<(PreviewQuickSpawn Spawn, OrchestrationStaticExtensionCall Call, OrchestrationTime Time)> Hits(
        IPreviewQuickCapture capture, TimelineStaticRoute route)
    {
        if (capture.Spawns.Count == 0)
            return [];
        var spawns = capture.Spawns.ToDictionary(spawn => (spawn.GraphId, spawn.NodeId));
        return route.ExtensionInvocations()
            .Where(hit => hit.Call.Location.GraphId != null && hit.Call.Location.NodeId != null &&
                spawns.ContainsKey((hit.Call.Location.GraphId, hit.Call.Location.NodeId)))
            .OrderBy(hit => hit.Time)
            .Select(hit => (spawns[(hit.Call.Location.GraphId, hit.Call.Location.NodeId)], hit.Call, hit.Time));
    }

    static double Seconds(OrchestrationTime time) => time.RawValue / (double)OrchestrationTime.Scale;
}
