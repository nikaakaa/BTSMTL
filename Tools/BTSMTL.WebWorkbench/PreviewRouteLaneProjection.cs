namespace BTSMTL.WebWorkbench;

// 把整份档案的逐帧播放记录按执行对象分道：对象按首次出现编号，同一对象同一身份的播放在连续帧上合成一段。
// 某帧不再出现即在该帧结束，本地终点按经过的秒数外推；档案末仍在播的段止于最后一帧。
public static class PreviewRouteLaneProjection
{
    sealed class Lane(int id, string carrier, string root, string? parentKey, double start)
    {
        public int Id { get; } = id;
        public string Carrier { get; } = carrier;
        public string Root { get; } = root;
        public string? ParentKey { get; } = parentKey;
        public double Start { get; } = start;
        public double End { get; set; } = start;
    }

    sealed class Open(int instance, string timelineId, double start, double localStart)
    {
        public int Instance { get; } = instance;
        public string TimelineId { get; } = timelineId;
        public double Start { get; } = start;
        public double LocalStart { get; } = localStart;
        public double Last { get; set; } = start;
        public double LocalLast { get; set; } = localStart;
    }

    public static PreviewRouteLanes Project(IReadOnlyList<PreviewRouteFrame> frames)
    {
        var lanes = new Dictionary<string, Lane>(StringComparer.Ordinal);
        var open = new Dictionary<string, Open>(StringComparer.Ordinal);
        var segments = new List<PreviewRouteSegment>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (PreviewRouteFrame frame in frames)
        {
            seen.Clear();
            foreach (PreviewRoutePlayback playback in frame.Playbacks)
            {
                if (playback.Call is not { } call) continue;
                string owner = ObjectKey(call);
                if (!lanes.TryGetValue(owner, out Lane? lane))
                    lanes.Add(owner, lane = new Lane(lanes.Count, Carrier(call), Root(call).ProgramId, ParentObject(call, owner), frame.Seconds));
                lane.End = frame.Seconds;
                string key = string.Join('\n', owner, playback.DocumentId, call.PlaybackId, call.ExecutionGeneration,
                    call.InvocationGeneration, playback.Domain ?? "");
                if (!seen.Add(key)) continue;
                if (open.TryGetValue(key, out Open? current))
                {
                    current.Last = frame.Seconds;
                    current.LocalLast = playback.CursorSeconds;
                }
                else open.Add(key, new Open(lane.Id, playback.DocumentId, frame.Seconds, playback.CursorSeconds));
            }
            foreach (string key in open.Keys.Where(key => !seen.Contains(key)).ToArray())
            {
                Open ended = open[key];
                open.Remove(key);
                segments.Add(new(ended.Instance, ended.TimelineId, ended.Start, frame.Seconds,
                    ended.LocalStart, ended.LocalLast + frame.Seconds - ended.Last));
            }
        }
        segments.AddRange(open.Values.Select(ended => new PreviewRouteSegment(ended.Instance, ended.TimelineId,
            ended.Start, ended.Last, ended.LocalStart, ended.LocalLast)));
        PreviewRouteInstance[] instances = lanes.Values.OrderBy(lane => lane.Id).Select(lane => new PreviewRouteInstance(lane.Id,
            lane.ParentKey != null && lanes.TryGetValue(lane.ParentKey, out Lane? parent) ? parent.Id : null,
            lane.Carrier, lane.Root, null, lane.Start, lane.End, null, null)).ToArray();
        return new(instances, segments.OrderBy(segment => segment.Start).ThenBy(segment => segment.Instance).ToArray(),
            frames.Select(frame => frame.Seconds).ToArray());
    }

    static string ObjectKey(PreviewExecutionCall call) =>
        call.ObjectHandle ?? (call.ActorId != null ? "actor:" + call.ActorId : call.ObjectSlot is { } slot ? "slot:" + slot : "unknown");

    static string Carrier(PreviewExecutionCall call) => call.ActorId ?? call.ObjectKind ?? call.ObjectHandle ?? "未记录对象";

    static PreviewExecutionCall Root(PreviewExecutionCall call)
    {
        while (call.ParentCall is { } parent) call = parent;
        return call;
    }

    // 调用链上第一个属于其它对象的父调用，即生成这个对象的那一方。
    static string? ParentObject(PreviewExecutionCall call, string owner)
    {
        for (PreviewExecutionCall? parent = call.ParentCall; parent != null; parent = parent.ParentCall)
            if (ObjectKey(parent) != owner) return ObjectKey(parent);
        return null;
    }
}
