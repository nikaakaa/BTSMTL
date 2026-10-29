using System;
using System.Collections.Generic;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Diagnostics.Editor;

namespace BTSMTL.Timeline.Editor
{
    // 真实档的一段播放：保留执行记录的正式身份，并解析到作者 Timeline 与静态状态；状态不在当前查询中时 State 为 null。
    public sealed class TimelinePreviewRealSegment
    {
        internal TimelinePreviewRealSegment(in BtsmtlExecutionRecord.RecordedPlayback record, TimelineAsset source,
            OrchestrationStaticState state)
        {
            Record = record;
            Source = source;
            State = state;
        }

        public BtsmtlExecutionRecord.RecordedPlayback Record { get; }
        public TimelineAsset Source { get; }
        public OrchestrationStaticState State { get; }
        // Clip 或 Marker 调用拉起的播放：记录中调用方播放的首段；调用方没有记录时为 null，CallerNote 写明原因。
        public TimelinePreviewRealSegment Caller { get; internal set; }
        public string CallerNote { get; internal set; } = string.Empty;
    }

    // 真实档的一次正式转移：按转移连线的作者身份解析静态转移，源状态按本次激活身份解析；
    // 源状态这次激活中有播放记录、且正式游标能换算时给出所在播放与本地时刻；否则 LocalNote 写明原因。
    public sealed class TimelinePreviewRealTransition
    {
        internal TimelinePreviewRealTransition(in BtsmtlExecutionRecord.RecordedTransition record,
            OrchestrationStaticTransition transition, OrchestrationStaticState source, TimelinePreviewRealSegment playback,
            float localTime, string localNote)
        {
            Record = record;
            Transition = transition;
            Source = source;
            Playback = playback;
            LocalTime = localTime;
            LocalNote = localNote;
        }

        // 转移发生时源状态所在的那段播放；无法换算本地时刻时为 null。
        public TimelinePreviewRealSegment Playback { get; }
        // 停顿吸附或无法换算的说明。
        public string LocalNote { get; }

        public BtsmtlExecutionRecord.RecordedTransition Record { get; }
        // 连线不在当前查询中（重演后作者内容已改）时为 null。
        public OrchestrationStaticTransition Transition { get; }
        public OrchestrationStaticState Source { get; }
        public OrchestrationStaticState Target => Transition?.Target;
        public float Time => (float)Record.Time;
        public TimelineAsset SourceTimeline => Playback?.Source;
        public float LocalTime { get; }
    }

    // 真实档路线：执行记录按正式身份解析到作者内容，并逐状态机与快速推导对比。
    public sealed class TimelinePreviewRealRoute
    {
        const double TimeTolerance = 1e-6;
        readonly BtsmtlExecutionRecord m_Record;
        readonly List<TimelinePreviewRealSegment> m_Segments = new();
        readonly List<TimelinePreviewRealTransition> m_Transitions = new();
        readonly List<TimelinePreviewDifference> m_Differences = new();

        public TimelinePreviewRealRoute(RuntimeDebugSession session) => m_Record = new BtsmtlExecutionRecord(session);

        public IReadOnlyList<TimelinePreviewRealSegment> Segments => m_Segments;
        public IReadOnlyList<TimelinePreviewRealTransition> Transitions => m_Transitions;
        public IReadOnlyList<TimelinePreviewDifference> Differences => m_Differences;
        // 真实执行进入本根之后，首次播放根外状态的时刻；此后超出快速档范围，不参与对比。
        public double? LeftRootAt { get; private set; }

        public void Apply(RuntimeExecutionTimeline timeline, TimelineOrchestrationQuery query,
            ITimelineAssetPreviewProvider provider)
        {
            m_Record.Apply(timeline);
            m_Segments.Clear();
            m_Transitions.Clear();
            LeftRootAt = null;
            foreach (BtsmtlExecutionRecord.RecordedPlayback played in m_Record.Recorded)
                if (played.HasLocal && provider.TryResolveRealTimeline(played.TimelineAuthoringId, out TimelineAsset source))
                    m_Segments.Add(new TimelinePreviewRealSegment(in played, source,
                        FindState(query, played.Owner.GraphAuthoringId, played.Owner.StateId)));
            double? entered = null;
            int entryMachine = -1;
            foreach (TimelinePreviewRealSegment segment in m_Segments)
                if (!segment.Record.IsNested && segment.State != null && (entered == null || segment.Record.Start < entered))
                {
                    entered = segment.Record.Start;
                    entryMachine = segment.State.MachineOperationIndex;
                }
            if (entered != null)
                foreach (TimelinePreviewRealSegment segment in m_Segments)
                    if (!segment.Record.IsNested && segment.State == null && segment.Record.Start >= entered &&
                        (LeftRootAt == null || segment.Record.Start < LeftRootAt))
                        LeftRootAt = segment.Record.Start;
            // 嵌套播放按记录的调用方身份挂到调用方播放下，不按时间重叠或名称推断。
            foreach (TimelinePreviewRealSegment nested in m_Segments)
            {
                if (!nested.Record.IsNested)
                    continue;
                foreach (TimelinePreviewRealSegment caller in m_Segments)
                    if (nested.Record.IsCalledBy(caller.Record))
                    {
                        nested.Caller = caller;
                        break;
                    }
                if (nested.Caller == null)
                    nested.CallerNote = "调用方播放没有可换算的执行记录，无法归属到调用 Clip。";
            }
            foreach (BtsmtlExecutionRecord.RecordedTransition recorded in m_Record.Transitions)
            {
                TimelinePreviewRealSegment source = null;
                double local = 0;
                string message = "源状态这次激活没有可换算的播放记录。";
                foreach (TimelinePreviewRealSegment segment in m_Segments)
                {
                    BtsmtlExecutionRecord.RecordedPlayback played = segment.Record;
                    // 状态转移发生在状态体 Timeline 上，Clip 调用拉起的嵌套播放不作为转移所在播放。
                    if (played.IsNested || !played.Owner.Equals(recorded.Source) || recorded.Time < played.Start - TimeTolerance ||
                        recorded.Time > played.End + TimeTolerance)
                        continue;
                    if (played.TryLocal(Math.Clamp(recorded.Time, played.Start, played.End), segment.Source.Data.Duration,
                        out local, out message))
                    {
                        source = segment;
                        break;
                    }
                }
                OrchestrationStaticState state = FindState(query, recorded.Source.GraphAuthoringId, recorded.Source.StateId);
                m_Transitions.Add(new TimelinePreviewRealTransition(in recorded,
                    FindTransition(state, recorded.GraphId, recorded.EdgeId), state, source, (float)local, message));
            }
            // 根的入口状态机转到出口即本次根执行结束（出口转移本身仍参与对比）；之后同一根再次起手是新的执行，
            // 从出口后的第一次播放起不在快速档范围内。
            double? exited = null;
            if (entered != null)
                foreach (TimelinePreviewRealTransition transition in m_Transitions)
                    if (transition.Source != null && transition.Transition != null && transition.Target == null &&
                        transition.Source.MachineOperationIndex == entryMachine && transition.Record.Time >= entered &&
                        (exited == null || transition.Record.Time < exited))
                        exited = transition.Record.Time;
            if (exited != null)
                foreach (TimelinePreviewRealSegment segment in m_Segments)
                    if (!segment.Record.IsNested && segment.Record.Start > exited &&
                        (LeftRootAt == null || segment.Record.Start < LeftRootAt))
                        LeftRootAt = segment.Record.Start;
        }

        public void Clear()
        {
            m_Record.Clear();
            m_Segments.Clear();
            m_Transitions.Clear();
            m_Differences.Clear();
            LeftRootAt = null;
        }

        // 每台状态机内按发生顺序配对真实转移与快速推导的转移：目标不同、未发生或时刻偏差超过一个逻辑 Tick 记一处差异；
        // 目标一旦不同该状态机后续已分叉，不再继续配对。进入状态机的初始转移不参与对比；离开本根之后的两侧转移都不参与。
        public void Compare(TimelineStaticChain fast, double tick, Func<OrchestrationStaticState, string> describe)
        {
            m_Differences.Clear();
            if (fast == null)
                return;
            double until = LeftRootAt ?? double.PositiveInfinity;
            var machines = new SortedDictionary<int, (List<TimelinePreviewRealTransition> Real, List<TimelineStaticChainMarker> Fast)>();
            foreach (TimelinePreviewRealTransition real in m_Transitions)
            {
                // 源不是状态（状态机入口的初始转移）不参与对比。
                if (real.Source == null || real.Record.Time >= until)
                    continue;
                if (real.Transition == null)
                {
                    m_Differences.Add(new TimelinePreviewDifference(real.Time, real.Source,
                        $"第 {real.Time:0.000} 秒的真实转移（连线 {real.Record.EdgeId}）不在当前状态机中：重演后作者内容已修改，请重新进入真实档。"));
                    continue;
                }
                Machine(machines, real.Source.MachineOperationIndex).Real.Add(real);
            }
            CollectFast(fast, until, machines);
            foreach ((List<TimelinePreviewRealTransition> real, List<TimelineStaticChainMarker> markers) in machines.Values)
            {
                markers.Sort((left, right) => left.Time.CompareTo(right.Time));
                CompareMachine(real, markers, tick, describe);
            }
            m_Differences.Sort((left, right) => left.Time.CompareTo(right.Time));
        }

        void CompareMachine(List<TimelinePreviewRealTransition> real, List<TimelineStaticChainMarker> fast, double tick,
            Func<OrchestrationStaticState, string> describe)
        {
            int count = Math.Max(real.Count, fast.Count);
            for (int k = 0; k < count; k++)
            {
                if (k >= fast.Count)
                {
                    m_Differences.Add(new TimelinePreviewDifference(real[k].Time, real[k].Source,
                        $"快速推导没有这次转移：真实在第 {real[k].Time:0.000} 秒从 {describe(real[k].Source)} 转到 {describe(real[k].Target)}。"));
                    return;
                }
                TimelineStaticChainMarker marker = fast[k];
                string basis = FastBasis(marker);
                if (k >= real.Count)
                {
                    m_Differences.Add(new TimelinePreviewDifference(marker.Time.AsSingle, marker.Source,
                        $"真实重演没有发生：快速推导在第 {marker.Time.AsSingle:0.000} 秒 {describe(marker.Source)} → " +
                        $"{describe(marker.Transition.Target)}。{basis}"));
                    return;
                }
                if (real[k].Transition != marker.Transition || real[k].Source != marker.Source)
                {
                    m_Differences.Add(new TimelinePreviewDifference(real[k].Time, real[k].Source,
                        $"走了不同的转移：真实 {describe(real[k].Source)} → {describe(real[k].Target)}（第 {real[k].Time:0.000} 秒），" +
                        $"快速 {describe(marker.Source)} → {describe(marker.Transition.Target)}（第 {marker.Time.AsSingle:0.000} 秒）。{basis}"));
                    return;
                }
                double offset = real[k].Record.Time - marker.Time.AsSingle;
                if (Math.Abs(offset) > tick + TimeTolerance)
                    m_Differences.Add(new TimelinePreviewDifference(real[k].Time, real[k].Source,
                        $"时刻偏差 {offset * 1000:+0;-0} ms：真实第 {real[k].Time:0.000} 秒，快速第 {marker.Time.AsSingle:0.000} 秒。{basis}"));
            }
        }

        static void CollectFast(TimelineStaticChain chain, double until,
            SortedDictionary<int, (List<TimelinePreviewRealTransition> Real, List<TimelineStaticChainMarker> Fast)> machines)
        {
            foreach (TimelineStaticChainMarker marker in chain.Markers)
                if (marker.Source != null && marker.Time.AsSingle < until)
                    Machine(machines, marker.Source.MachineOperationIndex).Fast.Add(marker);
            foreach (TimelineStaticChainBranch branch in chain.Branches)
                CollectFast(branch.Chain, until, machines);
        }

        static (List<TimelinePreviewRealTransition> Real, List<TimelineStaticChainMarker> Fast) Machine(
            SortedDictionary<int, (List<TimelinePreviewRealTransition> Real, List<TimelineStaticChainMarker> Fast)> machines,
            int machine)
        {
            if (!machines.TryGetValue(machine, out var lists))
                machines.Add(machine, lists = (new List<TimelinePreviewRealTransition>(), new List<TimelineStaticChainMarker>()));
            return lists;
        }

        static string FastBasis(TimelineStaticChainMarker marker)
        {
            OrchestrationTransitionWindow window = OrchestrationStateTransitionWindows.Build(marker.Source).Of(marker.Transition);
            return window == null ? string.Empty : $"快速依据：{window.RowLabel}，可走 {window.EdgeLabel}。";
        }

        static OrchestrationStaticState FindState(TimelineOrchestrationQuery query, string graphId, string stateId)
        {
            if (query != null)
                foreach (OrchestrationStaticState state in query.Query.States)
                    if (state.Location.GraphId == graphId && state.Location.NodeId == stateId)
                        return state;
            return null;
        }

        // 运行时以当前激活状态作为转移的源（Any State 连线也是），静态查询为每个源状态各展开一条，故只在源状态的出边中找。
        static OrchestrationStaticTransition FindTransition(OrchestrationStaticState source, string graphId, string edgeId)
        {
            if (source != null)
                foreach (OrchestrationStaticTransition transition in source.Transitions)
                    if (transition.Location.GraphId == graphId && transition.Location.EdgeId == edgeId)
                        return transition;
            return null;
        }
    }
}
