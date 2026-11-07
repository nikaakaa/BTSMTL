using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using ConditionTime = BTSMTL.Compilation.Analysis.TimelineStaticConditionEvaluator.ConditionTime;

namespace BTSMTL.Compilation.Analysis
{
    public enum TimelineStaticChainStopReason : byte
    {
        TimelineCompleted,
        StateMachineExited,
        NeedsSession,
        NoInitialState,
        ZeroTimeCycle,
        PreviewEnd
    }

    public sealed class TimelineStaticChainSpan
    {
        internal TimelineStaticChainSpan(OrchestrationStaticState state,
            OrchestrationTime start, OrchestrationTime end, OrchestrationTime localStart,
            OrchestrationTime localEnd, int cycle, bool playbackCompleted)
        {
            State = state;
            Start = start;
            End = end;
            LocalStart = localStart;
            LocalEnd = localEnd;
            Cycle = cycle;
            PlaybackCompleted = playbackCompleted;
        }

        public OrchestrationStaticState State { get; }
        public OrchestrationTime Start { get; }
        public OrchestrationTime End { get; }
        public OrchestrationTime LocalStart { get; }
        public OrchestrationTime LocalEnd { get; }
        public int Cycle { get; }
        // 本段播到 Timeline 终点（Once）；后续沿"完成"转移继续。
        public bool PlaybackCompleted { get; }
        // 被转移打断：本段没播到终点，[LocalEnd, Duration) 为未播部分。
        public bool Interrupted => !PlaybackCompleted && State.Timeline != null && LocalEnd < State.Timeline.Duration;
    }

    public sealed class TimelineStaticChainMarker
    {
        internal TimelineStaticChainMarker(OrchestrationStaticState source, OrchestrationStaticTransition transition,
            IReadOnlyList<OrchestrationStaticClip> providers, OrchestrationTime time,
            OrchestrationTime fromLocal, OrchestrationTime toLocal)
        {
            Source = source;
            Transition = transition;
            Providers = providers;
            Time = time;
            FromLocal = fromLocal;
            ToLocal = toLocal;
        }

        public OrchestrationStaticState Source { get; }
        public OrchestrationStaticTransition Transition { get; }
        // 转移时刻生效、提供转移所读状态的 Clip。
        public IReadOnlyList<OrchestrationStaticClip> Providers { get; }
        public OrchestrationTime Time { get; }
        public OrchestrationTime FromLocal { get; }
        public OrchestrationTime ToLocal { get; }
    }

    // 并行分支：所属状态 Root 中的状态机在 [Start, End) 内与所属状态的 Timeline 同时推进，时间同为预览全局时间。
    public sealed class TimelineStaticChainBranch
    {
        internal TimelineStaticChainBranch(OrchestrationStaticState owner, int machineOperationIndex,
            OrchestrationTime start, OrchestrationTime end, TimelineStaticChain chain)
        {
            Owner = owner;
            MachineOperationIndex = machineOperationIndex;
            Start = start;
            End = end;
            Chain = chain;
        }

        public OrchestrationStaticState Owner { get; }
        public int MachineOperationIndex { get; }
        public OrchestrationTime Start { get; }
        public OrchestrationTime End { get; }
        public TimelineStaticChain Chain { get; }
    }

    public sealed class TimelineStaticChain
    {
        readonly List<TimelineStaticChainSpan> m_Spans = new();
        readonly List<TimelineStaticChainMarker> m_Markers = new();

        public IReadOnlyList<TimelineStaticChainSpan> Spans => m_Spans;
        public IReadOnlyList<TimelineStaticChainMarker> Markers => m_Markers;
        public TimelineStaticChainStopReason StopReason { get; private set; }
        public string Reason { get; private set; }
        public OrchestrationStaticState LastState { get; private set; }

        // 状态 Root 中并行运行的状态机：每台各推导一条分支，从所属状态进入时起，到所属状态退出时止。
        public IReadOnlyList<TimelineStaticChainBranch> Branches => m_Branches;
        readonly List<TimelineStaticChainBranch> m_Branches = new();
        OrchestrationStaticQuery m_Query;
        ITimelineStaticReadIntervals m_Inputs;
        HashSet<int> m_ActiveMachines;
        OrchestrationStaticState m_Stay;
        OrchestrationTime m_Entered;
        OrchestrationTime m_Now;

        public static TimelineStaticChain Build(OrchestrationStaticQuery query, int machineOperationIndex,
            OrchestrationTime start, OrchestrationTime previewEnd, ITimelineStaticReadIntervals inputs) =>
            BuildMachine(query, machineOperationIndex, start, previewEnd, inputs, new HashSet<int>());

        static TimelineStaticChain BuildMachine(OrchestrationStaticQuery query, int machineOperationIndex,
            OrchestrationTime start, OrchestrationTime previewEnd, ITimelineStaticReadIntervals inputs,
            HashSet<int> activeMachines)
        {
            foreach (OrchestrationStaticTransition initial in query.InitialTransitions(machineOperationIndex))
                if (TimelineStaticConditionEvaluator.Earliest(initial.Condition, new ConditionTime(null, start,
                    OrchestrationTime.Zero, start, true, false), inputs,
                    out OrchestrationTime time, out _, out _) && time == start)
                {
                    if (initial.Target == null)
                        return new TimelineStaticChain
                        {
                            StopReason = TimelineStaticChainStopReason.NoInitialState,
                            Reason = "入口选中的目标不是状态，需在会话中验证。"
                        };
                    activeMachines.Add(machineOperationIndex);
                    TimelineStaticChain chain = Build(query, initial.Target, start, previewEnd, inputs, activeMachines);
                    activeMachines.Remove(machineOperationIndex);
                    return chain;
                }
            return new TimelineStaticChain
            {
                StopReason = TimelineStaticChainStopReason.NoInitialState,
                Reason = "入口状态的条件在预览起点没有成立。"
            };
        }

        // 只沿状态转移推导：出边按成立区间的最早时刻与正式比较器选择，无出边成立时播到终点并按"完成"转移继续。
        // 起播时刻读黑板变量时取该变量此刻的值：路线上已写入的值优先，否则用声明默认值；都没有则仍需会话验证。
        static void ResolveStart(OrchestrationStaticQuery query, OrchestrationStaticState state,
            Dictionary<string, double> blackboard, ref OrchestrationTime local, ref bool known)
        {
            if (known || state == null || state.TimelineStartVariable.Length == 0)
                return;
            if (!blackboard.TryGetValue(state.TimelineStartVariable, out double seconds) &&
                !query.TryNumberDefault(state.TimelineStartVariable, out seconds))
                return;
            local = OrchestrationTime.FromSeconds(seconds);
            known = true;
        }

        static TimelineStaticChain Build(OrchestrationStaticQuery query, OrchestrationStaticState entryState,
            OrchestrationTime start, OrchestrationTime previewEnd, ITimelineStaticReadIntervals inputs,
            HashSet<int> activeMachines)
        {
            var result = new TimelineStaticChain
            {
                m_Query = query,
                m_Inputs = inputs,
                m_ActiveMachines = activeMachines,
                m_Stay = entryState,
                m_Entered = start,
                m_Now = start
            };
            OrchestrationStaticState state = entryState;
            OrchestrationTime time = start;
            OrchestrationTime local = state.TimelineStartTime;
            bool startKnown = state.HasStaticTimelineStartTime;
            // 本次激活的数值黑板：声明默认值打底，路线经过的 Clip 起点写入覆盖。
            var blackboard = new Dictionary<string, double>(StringComparer.Ordinal);
            ResolveStart(query, state, blackboard, ref local, ref startKnown);
            var visited = new HashSet<(int State, long Local, long Time)>();
            int cycle = 0;
            while (time < previewEnd)
            {
                result.LastState = state;
                result.m_Now = time;
                if (state.Timeline == null)
                    return result.Stop(TimelineStaticChainStopReason.NeedsSession, "状态没有唯一绑定的 Timeline，需在会话中验证。");
                if (!state.TimelineDeterminesRootCompletion || !state.HasStaticTimelineClock)
                    return result.Stop(TimelineStaticChainStopReason.NeedsSession,
                        "当前静态链仅展开单个 Timeline 与不暂停的 1 倍常量时钟，其余 Root 或时钟需在会话中验证。");
                if (!startKnown)
                    return result.Stop(TimelineStaticChainStopReason.NeedsSession,
                        "Timeline 起播时刻来自运行数据，需在会话中验证。");
                if (state.PlaybackMode == OrchestrationTimelinePlaybackMode.HoldLastFrame)
                    return result.Stop(TimelineStaticChainStopReason.NeedsSession,
                        "HoldLastFrame 在终点前保持游标，需在会话中验证后续停留与转移。");
                if (!visited.Add((state.OperationIndex, local.RawValue, time.RawValue)))
                    return result.Stop(TimelineStaticChainStopReason.ZeroTimeCycle, "去向形成零时长循环，需在会话中验证逐 Tick 的结果。");
                OrchestrationStaticTimeline timeline = state.Timeline;
                OrchestrationTime endLocal = timeline.Duration;
                OrchestrationTime end = OrchestrationTime.Min(previewEnd, time + endLocal - local);
                bool completed = state.PlaybackMode == OrchestrationTimelinePlaybackMode.Once &&
                    end == time + endLocal - local;
                Candidate selected = Select(state, time, local, end, completed, inputs,
                    out string unavailableReason, out bool needsSession);
                if (needsSession)
                    return result.Stop(TimelineStaticChainStopReason.NeedsSession, unavailableReason);
                OrchestrationTime spanEnd = selected.Exists ? selected.Time : end;
                OrchestrationTime spanEndLocal = local + spanEnd - time;
                bool playbackCompleted = completed && spanEnd == end && selected.Transition?.FromAnyState != true;
                result.m_Spans.Add(new TimelineStaticChainSpan(state, time, spanEnd, local,
                    spanEndLocal, cycle, playbackCompleted));
                foreach (OrchestrationStaticClip clip in timeline.Clips)
                    if (clip.Start >= local && clip.Start < spanEndLocal)
                        foreach ((string variable, double value) in clip.StartWrites)
                            blackboard[variable] = value;
                if (selected.Exists)
                {
                    OrchestrationStaticState target = selected.Transition.Target;
                    OrchestrationTime targetLocal = target?.TimelineStartTime ?? OrchestrationTime.Zero;
                    bool targetKnown = target?.HasStaticTimelineStartTime ?? true;
                    ResolveStart(query, target, blackboard, ref targetLocal, ref targetKnown);
                    result.m_Markers.Add(new TimelineStaticChainMarker(state, selected.Transition,
                        Providers(state, selected.Transition, spanEndLocal), spanEnd, spanEndLocal, targetLocal));
                    time = spanEnd;
                    result.m_Now = time;
                    result.LeaveStay();
                    state = selected.Transition.Target;
                    result.m_Stay = state;
                    result.m_Entered = time;
                    cycle = 0;
                    if (state == null)
                        return result.Stop(TimelineStaticChainStopReason.StateMachineExited, "已到达状态机出口。");
                    local = targetLocal;
                    startKnown = targetKnown;
                    continue;
                }
                time = end;
                result.m_Now = time;
                local = spanEndLocal;
                if (local == timeline.Duration)
                {
                    if (state.PlaybackMode != OrchestrationTimelinePlaybackMode.Loop)
                        return result.Stop(TimelineStaticChainStopReason.TimelineCompleted, unavailableReason);
                    local = OrchestrationTime.Zero;
                    cycle++;
                }
                if (time == previewEnd)
                    return result.Stop(TimelineStaticChainStopReason.PreviewEnd, "已到预览显示范围的终点。");
            }
            return result.Stop(TimelineStaticChainStopReason.PreviewEnd, "已到预览显示范围的终点。");
        }

        TimelineStaticChain Stop(TimelineStaticChainStopReason reason, string message)
        {
            LeaveStay();
            StopReason = reason;
            Reason = message;
            m_Query = null;
            m_Inputs = null;
            m_ActiveMachines = null;
            return this;
        }

        // 所属状态在 [m_Entered, m_Now) 期间停留：其 Root 中的状态机同时运行，退出时一起结束。
        void LeaveStay()
        {
            OrchestrationStaticState state = m_Stay;
            m_Stay = null;
            if (state == null || m_Now <= m_Entered)
                return;
            foreach (int machine in state.ConcurrentMachineOperations)
            {
                if (m_ActiveMachines.Contains(machine))
                    continue;
                TimelineStaticChain chain = BuildMachine(m_Query, machine, m_Entered, m_Now, m_Inputs, m_ActiveMachines);
                m_Branches.Add(new TimelineStaticChainBranch(state, machine, m_Entered, m_Now, chain));
            }
        }

        readonly struct Candidate
        {
            internal Candidate(OrchestrationTime time, OrchestrationStaticTransition transition)
            {
                Exists = true;
                Time = time;
                Transition = transition;
            }

            internal bool Exists { get; }
            internal OrchestrationTime Time { get; }
            internal OrchestrationStaticTransition Transition { get; }
        }

        static Candidate Select(OrchestrationStaticState state,
            OrchestrationTime start, OrchestrationTime local, OrchestrationTime end, bool completed,
            ITimelineStaticReadIntervals inputs, out string unavailableReason, out bool needsSession)
        {
            Candidate selected = default;
            OrchestrationStaticRead? unavailable = null;
            needsSession = false;
            // 出边已按正式比较器排序（AnyState 优先，再按优先级与顺序）；同一时刻先成立的胜出。
            foreach (OrchestrationStaticTransition transition in state.Transitions)
            {
                var context = new ConditionTime(state.Timeline?.Clips, start, local, end, completed,
                    completed && !transition.FromAnyState);
                if (TimelineStaticConditionEvaluator.Earliest(transition.Condition, in context, inputs, out OrchestrationTime time,
                    out OrchestrationStaticRead? blocked, out bool unresolved))
                {
                    if (!selected.Exists || time < selected.Time)
                        selected = new Candidate(time, transition);
                }
                else if (!unavailable.HasValue)
                    unavailable = blocked;
                needsSession |= unresolved;
            }
            bool nextTickAny = false;
            if (completed && !selected.Exists)
                foreach (OrchestrationStaticTransition transition in state.Transitions)
                {
                    if (!transition.FromAnyState)
                        continue;
                    bool readsCompletion = false;
                    foreach (OrchestrationStaticRead read in transition.Reads)
                        readsCompletion |= read.Source == OrchestrationStaticReadSource.StateCompletion;
                    if (!readsCompletion)
                        continue;
                    var context = new ConditionTime(state.Timeline?.Clips, end, local + end - start, end, true, true);
                    if (TimelineStaticConditionEvaluator.Earliest(transition.Condition, in context, inputs, out _, out _, out _))
                        nextTickAny = true;
                }
            needsSession |= nextTickAny;
            unavailableReason = nextTickAny ? "AnyState 要在下一次 FSM Tick 才读取到主体完成，需在会话中验证具体时刻。" :
                needsSession ? "条件查询了当前静态遍历之外的 Timeline 播放实例，需在会话中验证。" : UnavailableReason(unavailable);
            return selected;
        }

        public static bool EvaluateConditionAt(OrchestrationStaticCondition condition,
            OrchestrationStaticState state, OrchestrationTime localTime, OrchestrationTime previewTime, bool rootCompleted,
            ITimelineStaticReadIntervals inputs, out OrchestrationStaticRead? unavailable, out bool needsSession) =>
            TimelineStaticConditionEvaluator.EvaluateConditionAt(condition, state?.Timeline?.Clips, localTime,
                previewTime, rootCompleted, inputs, out unavailable, out needsSession);

        static IReadOnlyList<OrchestrationStaticClip> Providers(OrchestrationStaticState state,
            OrchestrationStaticTransition transition, OrchestrationTime local)
        {
            var providers = new List<OrchestrationStaticClip>();
            foreach (OrchestrationStaticClip clip in state.Timeline.Clips)
                if (clip.Start <= local && local < clip.End)
                    foreach (OrchestrationStaticRead read in transition.Reads)
                        if (read.Source == OrchestrationStaticReadSource.ClipState && !providers.Contains(clip) &&
                            TimelineStaticConditionEvaluator.ProvidesState(clip, read.Key))
                            providers.Add(clip);
            return providers;
        }

        static string UnavailableReason(OrchestrationStaticRead? unavailable)
        {
            if (unavailable.HasValue)
            {
                OrchestrationStaticRead read = unavailable.Value;
                switch (read.Source)
                {
                    case OrchestrationStaticReadSource.HostInput:
                        return $"输入不在所需区间内：{read.Label}。";
                    case OrchestrationStaticReadSource.ClipState:
                        return $"Clip 提供的状态没有共同成立区间：{read.Label}。";
                    default:
                        return $"运行数据假设未成立：{read.Label}。";
                }
            }
            return "没有成立的去向，链在此结束。";
        }
    }
}
