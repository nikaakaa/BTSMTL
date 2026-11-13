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

    // 分岔口的一条去向，以及它只因按默认成立才可走的运行数据读取。
    public sealed class TimelineStaticChainBranchChoice
    {
        internal TimelineStaticChainBranchChoice(OrchestrationStaticTransition transition, IReadOnlyList<OrchestrationStaticRead> assumed)
        {
            Transition = transition;
            Assumed = assumed;
        }

        public OrchestrationStaticTransition Transition { get; }
        public IReadOnlyList<OrchestrationStaticRead> Assumed { get; }
    }

    // 路线经过的分岔口：多条去向同时可走，路线按正式顺序走了 Choices[0]，但它依赖未写入、按默认成立的运行数据。
    // State 为空表示状态机入口。
    public sealed class TimelineStaticChainBranchPoint
    {
        internal TimelineStaticChainBranchPoint(OrchestrationStaticState state, OrchestrationTime time,
            IReadOnlyList<TimelineStaticChainBranchChoice> choices)
        {
            State = state;
            Time = time;
            Choices = choices;
        }

        public OrchestrationStaticState State { get; }
        public OrchestrationTime Time { get; }
        public IReadOnlyList<TimelineStaticChainBranchChoice> Choices { get; }
    }

    public sealed class TimelineStaticChainSpan
    {
        internal TimelineStaticChainSpan(OrchestrationStaticState state,
            OrchestrationTime start, OrchestrationTime end, OrchestrationTime localStart,
            OrchestrationTime localEnd, int cycle, bool playbackCompleted, IReadOnlyList<OrchestrationStaticRead> runtimeReads)
        {
            State = state;
            Start = start;
            End = end;
            LocalStart = localStart;
            LocalEnd = localEnd;
            Cycle = cycle;
            PlaybackCompleted = playbackCompleted;
            RuntimeReads = runtimeReads;
        }

        public OrchestrationStaticState State { get; }
        public OrchestrationTime Start { get; }
        public OrchestrationTime End { get; }
        public OrchestrationTime LocalStart { get; }
        public OrchestrationTime LocalEnd { get; }
        public int Cycle { get; }
        // 本段播到 Timeline 终点（Once）；后续沿"完成"转移继续。
        public bool PlaybackCompleted { get; }
        public IReadOnlyList<OrchestrationStaticRead> RuntimeReads { get; }
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
        public IReadOnlyList<OrchestrationStaticRead> InitialReads { get; private set; } = Array.Empty<OrchestrationStaticRead>();

        public IReadOnlyList<TimelineStaticChainBranchPoint> BranchPoints => m_BranchPoints;
        readonly List<TimelineStaticChainBranchPoint> m_BranchPoints = new();

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
            // 运行时按顺序取第一条成立的入口，读取依赖只记到胜出者为止；其后的入口另行求值，只用来记录分岔口。
            var trace = new ReadTrace(inputs);
            var others = new ReadTrace(inputs);
            var satisfied = new List<OrchestrationStaticTransition>();
            foreach (OrchestrationStaticTransition initial in query.InitialTransitions(machineOperationIndex))
                if (TimelineStaticConditionEvaluator.Earliest(initial.Condition, new ConditionTime(null, start,
                    OrchestrationTime.Zero, start, true, false), satisfied.Count == 0 ? trace : others,
                    out OrchestrationTime time, out _, out _) && time == start)
                    satisfied.Add(initial);
            if (satisfied.Count == 0)
                return new TimelineStaticChain
                {
                    StopReason = TimelineStaticChainStopReason.NoInitialState,
                    Reason = "入口状态的条件在预览起点没有成立。"
                };
            IReadOnlyList<TimelineStaticChainBranchChoice> choices = Undetermined(null, satisfied, start, inputs);
            OrchestrationStaticTransition selected = satisfied[0];
            if (selected.Target == null)
                return new TimelineStaticChain
                {
                    StopReason = TimelineStaticChainStopReason.NoInitialState,
                    Reason = "入口选中的目标不是状态，需在会话中验证。"
                };
            activeMachines.Add(machineOperationIndex);
            TimelineStaticChain chain = Build(query, selected.Target, start, previewEnd, inputs, activeMachines);
            chain.InitialReads = trace.Reads;
            if (choices != null)
                chain.m_BranchPoints.Insert(0, new TimelineStaticChainBranchPoint(null, start, choices));
            activeMachines.Remove(machineOperationIndex);
            return chain;
        }

        // 同一时刻有两条以上去向可走、且按顺序胜出的那条依赖未写入、宿主未声明含义（只能假设）的读取时，
        // 路线照正式顺序走胜出者，但记下这个分岔口，作者可在角色输入轨上用预览条件改走别的去向。胜出者的读取都已写入时不记。
        static IReadOnlyList<TimelineStaticChainBranchChoice> Undetermined(OrchestrationStaticState state,
            IReadOnlyList<OrchestrationStaticTransition> competing, OrchestrationTime time, ITimelineStaticReadIntervals inputs)
        {
            if (competing.Count < 2 || Assumed(state, competing[0], time, inputs).Count == 0)
                return null;
            var choices = new List<TimelineStaticChainBranchChoice>(competing.Count);
            foreach (OrchestrationStaticTransition transition in competing)
                choices.Add(new TimelineStaticChainBranchChoice(transition, Assumed(state, transition, time, inputs)));
            return choices;
        }

        static IReadOnlyList<OrchestrationStaticRead> Assumed(OrchestrationStaticState state,
            OrchestrationStaticTransition transition, OrchestrationTime time, ITimelineStaticReadIntervals inputs)
        {
            var assumed = new List<OrchestrationStaticRead>();
            foreach (OrchestrationStaticRead read in transition.Reads)
            {
                OrchestrationStaticReadSource source = state?.Timeline == null
                    ? read.Source : OrchestrationStaticReads.EffectiveSource(state.Timeline, in read);
                if (source == OrchestrationStaticReadSource.RuntimeData && read.Undeterminable &&
                    !inputs.Written(read.Key, time) && !assumed.Exists(value => value.Key == read.Key))
                    assumed.Add(read);
            }
            return assumed;
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
                var trace = new ReadTrace(inputs);
                Candidate selected = Select(state, time, local, end, completed, trace,
                    out string unavailableReason, out bool needsSession, out IReadOnlyList<TimelineStaticChainBranchChoice> branch);
                if (needsSession)
                    return result.Stop(TimelineStaticChainStopReason.NeedsSession, unavailableReason);
                OrchestrationTime spanEnd = selected.Exists ? selected.Time : end;
                OrchestrationTime spanEndLocal = local + spanEnd - time;
                bool playbackCompleted = completed && spanEnd == end && selected.Transition?.FromAnyState != true;
                result.m_Spans.Add(new TimelineStaticChainSpan(state, time, spanEnd, local,
                    spanEndLocal, cycle, playbackCompleted, trace.Reads));
                if (branch != null)
                    result.m_BranchPoints.Add(new TimelineStaticChainBranchPoint(state, spanEnd, branch));
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

        sealed class ReadTrace : ITimelineStaticReadIntervals
        {
            readonly ITimelineStaticReadIntervals m_Inputs;
            readonly Dictionary<string, OrchestrationStaticRead> m_Reads = new(StringComparer.Ordinal);

            public ReadTrace(ITimelineStaticReadIntervals inputs) => m_Inputs = inputs;
            public IReadOnlyList<OrchestrationStaticRead> Reads => new List<OrchestrationStaticRead>(m_Reads.Values);
            public IReadOnlyList<TimelineStaticInterval> HostInput(string inputId) => m_Inputs.HostInput(inputId);
            public IReadOnlyList<TimelineStaticInterval> RuntimeData(in OrchestrationStaticRead read)
            {
                m_Reads[read.Key] = read;
                return m_Inputs.RuntimeData(in read);
            }

            public bool Written(string key, OrchestrationTime time) => m_Inputs.Written(key, time);
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
            ITimelineStaticReadIntervals inputs, out string unavailableReason, out bool needsSession,
            out IReadOnlyList<TimelineStaticChainBranchChoice> branch)
        {
            Candidate selected = default;
            OrchestrationStaticRead? unavailable = null;
            needsSession = false;
            var satisfied = new List<Candidate>();
            // 出边已按正式比较器排序（AnyState 优先，再按优先级与顺序）；同一时刻先成立的胜出。
            foreach (OrchestrationStaticTransition transition in state.Transitions)
            {
                var context = new ConditionTime(state.Timeline?.Clips, start, local, end, completed,
                    completed && !transition.FromAnyState);
                if (TimelineStaticConditionEvaluator.Earliest(transition.Condition, in context, inputs, out OrchestrationTime time,
                    out OrchestrationStaticRead? blocked, out bool unresolved))
                {
                    satisfied.Add(new Candidate(time, transition));
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
            branch = null;
            if (selected.Exists)
            {
                var competing = new List<OrchestrationStaticTransition>();
                foreach (Candidate candidate in satisfied)
                    if (candidate.Time == selected.Time)
                        competing.Add(candidate.Transition);
                branch = Undetermined(state, competing, selected.Time, inputs);
            }
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
