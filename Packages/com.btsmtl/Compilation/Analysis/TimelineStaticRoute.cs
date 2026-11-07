using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Orchestration;
using BTSMTL.Timeline;

namespace BTSMTL.Compilation.Analysis
{
    public readonly struct TimelineStaticRouteSpan
    {
        internal TimelineStaticRouteSpan(string timelineIdentity, OrchestrationTime start, OrchestrationTime end,
            OrchestrationTime localStart, OrchestrationTime localEnd, int cycle, OrchestrationStaticTransition transition,
            OrchestrationStaticState state, bool playbackCompleted, int branch, int playback,
            OrchestrationStaticTimelineCall call = null, int callerPlayback = -1)
        {
            TimelineIdentity = timelineIdentity;
            Start = start;
            End = end;
            LocalStart = localStart;
            LocalEnd = localEnd;
            Cycle = cycle;
            Transition = transition;
            State = state;
            PlaybackCompleted = playbackCompleted;
            Branch = branch;
            Playback = playback;
            Call = call;
            CallerPlayback = callerPlayback;
        }

        public string TimelineIdentity { get; }
        public OrchestrationTime Start { get; }
        public OrchestrationTime End { get; }
        public OrchestrationTime LocalStart { get; }
        public OrchestrationTime LocalEnd { get; }
        public int Cycle { get; }
        public OrchestrationStaticTransition Transition { get; }
        public OrchestrationStaticState State { get; }
        public bool PlaybackCompleted { get; }
        public int Branch { get; }
        public int Playback { get; }
        public OrchestrationStaticTimelineCall Call { get; }
        public int CallerPlayback { get; }
    }

    public sealed class TimelineStaticRoute
    {
        readonly List<TimelineStaticRouteSpan> m_Main = new();
        readonly List<TimelineStaticRouteSpan> m_Parallel = new();

        TimelineStaticRoute(OrchestrationTime rootStart, TimelineStaticChain chain)
        {
            RootStart = rootStart;
            Chain = chain;
        }

        public OrchestrationTime RootStart { get; }
        public OrchestrationTime End { get; private set; }
        public TimelineStaticChain Chain { get; }
        public IReadOnlyList<TimelineStaticRouteSpan> Main => m_Main;
        public IReadOnlyList<TimelineStaticRouteSpan> Parallel => m_Parallel;

        public static TimelineStaticRoute Build(OrchestrationStaticQuery query, int machineOperationIndex,
            OrchestrationTime previewEnd, TimelineStaticReadIntervals inputs)
        {
            if (inputs.RootStart >= previewEnd)
                throw new TimelineStaticInputUnavailableException(
                    $"输入方案在 {inputs.RootStart.AsSingle:0.###} 秒才起手根，已超出预览范围终点。");
            TimelineStaticChain chain = TimelineStaticChain.Build(query, machineOperationIndex, inputs.RootStart, previewEnd, inputs);
            var route = new TimelineStaticRoute(inputs.RootStart, chain);
            int branch = 0;
            int playback = -1;
            AddChain(chain, 0, route.m_Main, ref playback);
            route.AddBranches(chain, ref branch, ref playback);
            foreach (var caller in route.m_Main.Concat(route.m_Parallel).GroupBy(value => value.Playback).ToArray())
                route.AddCalls(caller.ToArray(), new HashSet<string>(StringComparer.Ordinal), previewEnd, ref branch, ref playback);
            route.End = route.m_Main.Concat(route.m_Parallel).Aggregate(OrchestrationTime.Zero,
                (end, span) => OrchestrationTime.Max(end, span.End));
            return route;
        }

        public static TimelineStaticRoute BuildStandalone(TimelineContentUnit content, OrchestrationTime previewEnd)
        {
            if (content.Loop && content.Duration <= OrchestrationTime.Zero)
                throw new InvalidOperationException("循环资产预览的 Timeline 时长必须大于零。");
            var route = new TimelineStaticRoute(OrchestrationTime.Zero, null);
            OrchestrationTime global = OrchestrationTime.Zero;
            int cycle = 0;
            do
            {
                OrchestrationTime end = OrchestrationTime.Min(global + content.Duration, previewEnd);
                OrchestrationTime localEnd = end - global;
                route.m_Main.Add(new TimelineStaticRouteSpan(content.Identity,
                    global, end, OrchestrationTime.Zero, localEnd, cycle, null, null,
                    !content.Loop && localEnd == content.Duration, 0, 0));
                global = end;
                if (!content.Loop)
                    break;
                cycle++;
            } while (global < previewEnd);
            route.End = OrchestrationTime.Max(OrchestrationTime.Zero, global);
            return route;
        }

        public IEnumerable<(TimelineStaticRouteSpan Span, int Occurrence)> OrderedSpans()
        {
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            var playbacks = new Dictionary<int, int>();
            foreach (TimelineStaticRouteSpan span in m_Main.Concat(m_Parallel).OrderBy(value => value.Start).ThenBy(value => value.Branch))
            {
                if (!playbacks.TryGetValue(span.Playback, out int occurrence))
                {
                    occurrences.TryGetValue(span.TimelineIdentity, out occurrence);
                    occurrences[span.TimelineIdentity] = occurrence + 1;
                    playbacks.Add(span.Playback, occurrence);
                }
                yield return (span, occurrence);
            }
        }

        static void AddChain(TimelineStaticChain chain, int branch, List<TimelineStaticRouteSpan> result, ref int playback)
        {
            OrchestrationStaticTransition entry = null;
            string previous = null;
            foreach (TimelineStaticChainSpan span in chain.Spans)
            {
                string identity = span.State.TimelineIdentity;
                if (previous == null || entry != null || identity != previous)
                    playback++;
                result.Add(new TimelineStaticRouteSpan(identity, span.Start, span.End, span.LocalStart, span.LocalEnd,
                    span.Cycle, entry, span.State, span.PlaybackCompleted, branch, playback));
                previous = identity;
                entry = null;
                foreach (TimelineStaticChainMarker marker in chain.Markers)
                    if (marker.Time == span.End)
                        entry = marker.Transition;
            }
        }

        void AddBranches(TimelineStaticChain chain, ref int branch, ref int playback)
        {
            foreach (TimelineStaticChainBranch value in chain.Branches)
            {
                AddChain(value.Chain, ++branch, m_Parallel, ref playback);
                AddBranches(value.Chain, ref branch, ref playback);
            }
        }

        // Clip/Marker 回调的子播放沿调用方全局时刻展开，寿命受该回调与父播放共同限制。
        void AddCalls(TimelineStaticRouteSpan[] caller, HashSet<string> chain, OrchestrationTime previewEnd,
            ref int branch, ref int playback)
        {
            TimelineStaticRouteSpan first = caller[0];
            OrchestrationStaticTimeline timeline = first.Call != null ? first.Call.Callee : first.State?.Timeline;
            if (timeline == null)
                return;
            int operation = first.Call != null ? first.Call.OperationIndex : first.State.TimelineOperationIndex;
            OrchestrationTime playbackEnd = OrchestrationTime.Zero;
            foreach (TimelineStaticRouteSpan span in caller)
                playbackEnd = OrchestrationTime.Max(playbackEnd, span.End);
            chain.Add(timeline.Identity);
            foreach (OrchestrationStaticTimelineCall call in timeline.Calls)
            {
                if (call.CallerOperationIndex != operation || !call.StartsOnCallback || call.Callee == null ||
                    chain.Contains(call.CalleeIdentity))
                    continue;
                for (int callerIndex = 0; callerIndex < caller.Length; callerIndex++)
                {
                    TimelineStaticRouteSpan span = caller[callerIndex];
                    bool destroy = call.Hook == OrchestrationTimelineHook.Destroy;
                    if (destroy && callerIndex != caller.Length - 1)
                        continue;
                    OrchestrationTime offset = destroy ? span.LocalEnd : call.Offset;
                    if (call.Hook == OrchestrationTimelineHook.Disable && callerIndex == caller.Length - 1 &&
                        call.Clip.Start <= span.LocalEnd && call.Clip.End > span.LocalEnd)
                        offset = span.LocalEnd;
                    if (offset < span.LocalStart || offset > span.LocalEnd ||
                        offset == span.LocalEnd && !span.PlaybackCompleted && !call.IsInstantaneous)
                        continue;
                    OrchestrationTime start = span.Start + offset - span.LocalStart;
                    OrchestrationTime end = OrchestrationTime.Min(playbackEnd, previewEnd);
                    if (!call.IsInstantaneous && call.Limit is { } limit)
                        end = OrchestrationTime.Min(end, span.Start + limit - span.LocalStart);
                    if (end < start || end == start && !call.IsInstantaneous)
                        continue;
                    int nestedPlayback = ++playback;
                    int nestedBranch = ++branch;
                    OrchestrationTime duration = call.Callee.Duration;
                    OrchestrationTime local = call.CalleeStartTime;
                    OrchestrationTime time = start;
                    int cycle = 0;
                    var nested = new List<TimelineStaticRouteSpan>();
                    if (call.IsInstantaneous)
                        nested.Add(new TimelineStaticRouteSpan(call.CalleeIdentity, start, start, local, local,
                            cycle, null, null, false, nestedBranch, nestedPlayback, call, span.Playback));
                    while (!call.IsInstantaneous && time < end)
                    {
                        if (local >= duration)
                        {
                            if (call.PlaybackMode == OrchestrationTimelinePlaybackMode.HoldLastFrame)
                            {
                                nested.Add(new TimelineStaticRouteSpan(call.CalleeIdentity, time, end,
                                    duration, duration, cycle, null, null, false, nestedBranch, nestedPlayback, call,
                                    span.Playback));
                                break;
                            }
                            if (call.PlaybackMode != OrchestrationTimelinePlaybackMode.Loop || duration <= OrchestrationTime.Zero)
                                break;
                            local = OrchestrationTime.Zero;
                            cycle++;
                        }
                        OrchestrationTime spanEnd = OrchestrationTime.Min(end, time + duration - local);
                        bool completed = call.PlaybackMode == OrchestrationTimelinePlaybackMode.Once &&
                            spanEnd == time + duration - local;
                        nested.Add(new TimelineStaticRouteSpan(call.CalleeIdentity, time, spanEnd, local,
                            local + spanEnd - time, cycle, null, null, completed, nestedBranch, nestedPlayback, call,
                            span.Playback));
                        local += spanEnd - time;
                        time = spanEnd;
                        if (completed)
                            break;
                    }
                    if (nested.Count == 0)
                        continue;
                    m_Parallel.AddRange(nested);
                    AddCalls(nested.ToArray(), chain, previewEnd, ref branch, ref playback);
                }
            }
            chain.Remove(timeline.Identity);
        }
    }
}
