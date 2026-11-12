using System.Text.Json.Nodes;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Orchestration;
using BTSMTL.Timeline;
using BTSMTL.WebWorkbench;
using Newtonsoft.Json.Linq;

namespace BTSMTL.WebWorkbench;

// ReadMs／RouteMs／WriteMs：改时间并经正式读取器取片段范围、重推路线、写路线负载各自的耗时。
sealed record QuickRetime(TimelineStaticRoute Route, PreviewQuickPlan Plan, JsonObject Payload, double ReadMs, double RouteMs, double WriteMs);

// 快预览作业的片段时间候选：作业保留本次捕获的作者文档与编译结果。每个 Timeline 第一次候选时经正式读取器读一次，
// 留住作者模型与各片段捕获时的时间范围；每次候选先把片段时间还原到捕获值，再按网页给的起止与 ClipIn 改，
// 由 TimelineData.Init 重算相邻混合与时长并校验，写进已编译内容的时间候选，再重建静态查询推出路线；不读盘、不重编、不写包。
sealed class QuickRetimeSource(IPreviewQuickCapture capture, IPreviewQuickEnvironment actor, QuickPreviewCompilation compiled,
    QuickPreviewInputSchedule input, string? graphId, QuickPreviewInstances instances)
{
    readonly Dictionary<string, TimelineContentUnit> m_Candidates = new(StringComparer.Ordinal);
    readonly Dictionary<string, AuthoredTimeline> m_Authored = new(StringComparer.Ordinal);

    sealed record AuthoredTimeline(TimelineData Timeline, Dictionary<string, Clip> Clips,
        Dictionary<string, TimelineClipTimeRange> Captured);

    public QuickRetime Retime(PreviewQuickRetimeRequest request)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        TimelineContentUnit published = compiled.Content.Timelines.SingleOrDefault(content =>
                content.AuthoringId == request.TimelineId) ??
            throw new InvalidDataException($"Timeline '{request.TimelineId}' 不在本次快预览编译的内容里。");
        if (!m_Authored.TryGetValue(request.TimelineId, out AuthoredTimeline? authored))
        {
            // 片段资源与文档内容已在本次快预览编译时绑定进包，候选只改时间：按正式顺序绑定文档内容再初始化，不重复登记绑定。
            TimelineData source = capture.ReadTimeline(request.TimelineId);
            var authoredClips = source.Tracks.SelectMany(track => track.Clips)
                .ToDictionary(clip => clip.AuthoringId, StringComparer.Ordinal);
            authored = new AuthoredTimeline(source, authoredClips, authoredClips.ToDictionary(item => item.Key,
                item => item.Value.CaptureTimeRange(), StringComparer.Ordinal));
            m_Authored.Add(request.TimelineId, authored);
        }
        foreach (var (clipId, clip) in authored.Clips)
            clip.ApplyTimeRange(authored.Captured[clipId]);
        foreach (PreviewQuickClipTime time in request.Clips)
        {
            if (!authored.Clips.TryGetValue(time.ClipId, out Clip? clip))
                throw new InvalidDataException($"Timeline '{request.TimelineId}' 中没有片段 '{time.ClipId}'。");
            if (time.StartRaw < 0 || time.EndRaw <= time.StartRaw || time.ClipInRaw < 0)
                throw new InvalidDataException($"片段 '{time.ClipId}' 的起止或 ClipIn 无效。");
            TimelineClipTimeRange captured = authored.Captured[time.ClipId];
            var range = new TimelineClipTimeRange(OrchestrationTime.FromRaw(time.StartRaw), OrchestrationTime.FromRaw(time.EndRaw),
                captured.EaseIn, captured.EaseOut, time.ClipInRaw is { } clipIn ? OrchestrationTime.FromRaw(clipIn) : captured.ClipIn,
                captured.SourceStart, captured.SourceEnd);
            clip.ApplyTimeRange(in range);
        }
        TimelineData timeline = authored.Timeline;
        timeline.Init();
        var errors = new List<string>();
        if (!timeline.ValidateContent(capture.TimelineContracts, errors))
            throw new InvalidDataException(string.Join("\n", errors));
        var authoring = authored.Clips;
        var ranges = new TimelineClipTimeRange[published.Clips.Count];
        for (int index = 0; index < ranges.Length; index++)
            ranges[index] = authoring[published.Clips[index].AuthoringId].CaptureTimeRange();
        if (!m_Candidates.TryGetValue(published.Identity, out TimelineContentUnit? candidate))
        {
            int[] slots = Enumerable.Range(0, published.Clips.Count).ToArray();
            candidate = published.CreateTimeRangeCandidate(slots);
            m_Candidates.Add(published.Identity, candidate);
        }
        candidate.UpdateTimeRangeCandidate(ranges, timeline.DurationTime, published.SourceRevision, published.Revision);
        long read = System.Diagnostics.Stopwatch.GetTimestamp();
        TimelineStaticRoute route = compiled.Route(input, graphId, m_Candidates);
        long routed = System.Diagnostics.Stopwatch.GetTimestamp();
        JObject routeJson = capture.RoutePayload(route);
        var payload = new JsonObject
        {
            ["contentIdentity"] = published.Identity,
            ["duration"] = timeline.DurationTime.RawValue,
            ["ranges"] = new JsonArray(ranges.Select(range => (JsonNode)new JsonObject
            {
                ["start"] = range.Start.RawValue, ["end"] = range.End.RawValue,
                ["easeIn"] = range.EaseIn.RawValue, ["easeOut"] = range.EaseOut.RawValue, ["clipIn"] = range.ClipIn.RawValue,
                ["sourceStartBits"] = BitConverter.SingleToInt32Bits(range.SourceStart),
                ["sourceEndBits"] = BitConverter.SingleToInt32Bits(range.SourceEnd),
                ["otherEaseIn"] = range.OtherEaseIn.RawValue, ["otherEaseOut"] = range.OtherEaseOut.RawValue
            }).ToArray()),
            ["route"] = JsonNode.Parse(routeJson.ToString(Newtonsoft.Json.Formatting.None))
        };
        long written = System.Diagnostics.Stopwatch.GetTimestamp();
        double Ms(long from, long to) => (to - from) * 1000d / System.Diagnostics.Stopwatch.Frequency;
        return new QuickRetime(route, QuickPreviewPlanProjection.Describe(capture, actor, compiled, input, route, instances), payload, Ms(started, read), Ms(read, routed), Ms(routed, written));
    }
}
