using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using BTSMTL.Timeline;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation.Analysis
{
    // provided 只声明随 Clip 生命周期存在的状态；未声明的读取由预览假设承接。
    public interface IOrchestrationStateKeyDeclarations
    {
        bool TryDeclare(in OrchestrationExtensionReference extension, IReadOnlyDictionary<string, SemanticLiteral> fields,
            ICollection<string> provided, ICollection<string> read);
    }

    public interface IOrchestrationStatePredicateDeclarations : IOrchestrationStateKeyDeclarations
    {
        bool TryDescribePredicate(in OrchestrationExtensionReference extension,
            IReadOnlyDictionary<string, SemanticLiteral> fields, int operationIndex,
            out OrchestrationStaticCondition condition);
    }

    public enum OrchestrationStaticReadSource : byte
    {
        ClipState = 1,
        HostInput = 2,
        RuntimeData = 3,
        StateCompletion = 4,
        TimelineSection = 5,
        TimelineTime = 6
    }

    public readonly struct OrchestrationStaticRead : IEquatable<OrchestrationStaticRead>
    {
        public OrchestrationStaticRead(int operationIndex, OrchestrationStaticReadSource source, string key,
            bool undeterminable, string label)
        {
            OperationIndex = operationIndex;
            Source = source;
            Key = key;
            Undeterminable = undeterminable;
            Label = label;
        }

        public int OperationIndex { get; }
        public OrchestrationStaticReadSource Source { get; }
        // 状态键、宿主输入身份，或运行数据的稳定描述。
        public string Key { get; }
        public bool Undeterminable { get; }
        public string Label { get; }

        public bool Equals(OrchestrationStaticRead other) => OperationIndex == other.OperationIndex &&
            Source == other.Source && string.Equals(Key, other.Key, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is OrchestrationStaticRead other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(OperationIndex, (int)Source, Key);
    }

    public readonly struct OrchestrationStaticSourceLocation
    {
        public OrchestrationStaticSourceLocation(string graphId, string nodeId, string edgeId, string displayPath)
        {
            GraphId = graphId;
            NodeId = nodeId;
            EdgeId = edgeId;
            DisplayPath = displayPath;
        }

        public string GraphId { get; }
        public string NodeId { get; }
        public string EdgeId { get; }
        public string DisplayPath { get; }
    }

    public enum OrchestrationStaticConditionKind : byte
    {
        Constant,
        All,
        Any,
        Not,
        Read,
        StateRootCompleted,
        TimelineSection,
        // 当前 Timeline 本地时刻与常量比较：本地时刻 Comparison Threshold。
        TimelineTime
    }

    public sealed class OrchestrationStaticCondition
    {
        internal OrchestrationStaticCondition(OrchestrationStaticConditionKind kind,
            IReadOnlyList<OrchestrationStaticCondition> children, OrchestrationStaticRead read = default,
            bool constantValue = false, string timelineIdentity = "", string sectionId = "", string frameIdentity = "",
            OrchestrationComparisonKind comparison = default, OrchestrationTime threshold = default)
        {
            Kind = kind;
            Children = children;
            Read = read;
            ConstantValue = constantValue;
            TimelineIdentity = timelineIdentity;
            SectionId = sectionId;
            FrameIdentity = frameIdentity;
            Comparison = comparison;
            Threshold = threshold;
        }

        public OrchestrationComparisonKind Comparison { get; }
        public OrchestrationTime Threshold { get; }

        public OrchestrationStaticConditionKind Kind { get; }
        public IReadOnlyList<OrchestrationStaticCondition> Children { get; }
        public OrchestrationStaticRead Read { get; }
        public bool ConstantValue { get; }
        public string TimelineIdentity { get; }
        public string SectionId { get; }
        internal string FrameIdentity { get; }

        public static OrchestrationStaticCondition Constant(bool value) =>
            new(OrchestrationStaticConditionKind.Constant, Array.Empty<OrchestrationStaticCondition>(), constantValue: value);

        public static OrchestrationStaticCondition All(params OrchestrationStaticCondition[] children) => Combine(true, children);
        public static OrchestrationStaticCondition Any(params OrchestrationStaticCondition[] children) => Combine(false, children);
        public static OrchestrationStaticCondition Not(OrchestrationStaticCondition condition) => Negate(condition);
        public static OrchestrationStaticCondition StateKey(int operationIndex, string key) =>
            new(OrchestrationStaticConditionKind.Read, Array.Empty<OrchestrationStaticCondition>(),
                new OrchestrationStaticRead(operationIndex, OrchestrationStaticReadSource.ClipState, key, false, key));

        internal static OrchestrationStaticCondition Combine(bool all, params OrchestrationStaticCondition[] children) =>
            Combine(all, (IReadOnlyList<OrchestrationStaticCondition>)children);

        internal static OrchestrationStaticCondition Combine(bool all, IReadOnlyList<OrchestrationStaticCondition> children)
        {
            var result = new List<OrchestrationStaticCondition>();
            foreach (OrchestrationStaticCondition child in children)
            {
                if (child.Kind == OrchestrationStaticConditionKind.Constant)
                {
                    if (child.ConstantValue != all)
                        return Constant(!all);
                    continue;
                }
                result.Add(child);
            }
            return result.Count == 0 ? Constant(all) : result.Count == 1 ? result[0] :
                new OrchestrationStaticCondition(all ? OrchestrationStaticConditionKind.All : OrchestrationStaticConditionKind.Any, result);
        }

        internal static OrchestrationStaticCondition Negate(OrchestrationStaticCondition condition) =>
            condition.Kind == OrchestrationStaticConditionKind.Constant ? Constant(!condition.ConstantValue) :
            condition.Kind == OrchestrationStaticConditionKind.Not ? condition.Children[0] :
            new OrchestrationStaticCondition(OrchestrationStaticConditionKind.Not, new[] { condition });
    }

    public sealed class OrchestrationStaticSection
    {
        internal OrchestrationStaticSection(int ordinal, string authoringId, OrchestrationTime start,
            OrchestrationTime end, string nextSectionId)
        {
            Ordinal = ordinal;
            AuthoringId = authoringId;
            Start = start;
            End = end;
            NextSectionId = nextSectionId;
        }

        public int Ordinal { get; }
        public string AuthoringId { get; }
        public OrchestrationTime Start { get; }
        public OrchestrationTime End { get; }
        public string NextSectionId { get; }
        public bool Continuable => NextSectionId.Length != 0;
    }

    public sealed class OrchestrationStaticClip
    {
        internal OrchestrationStaticClip(string timelineIdentity, string clipId, string trackId,
            OrchestrationTime start, OrchestrationTime end, TimelineExecutionDomain domain, bool muted)
        {
            TimelineIdentity = timelineIdentity;
            ClipId = clipId;
            TrackId = trackId;
            Start = start;
            End = end;
            Domain = domain;
            Muted = muted;
        }

        public string TimelineIdentity { get; }
        public string ClipId { get; }
        public string TrackId { get; }
        public OrchestrationTime Start { get; }
        public OrchestrationTime End { get; }
        public TimelineExecutionDomain Domain { get; }
        public bool Muted { get; }
        public bool HasGraph { get; internal set; }
        // 入口无条件执行的状态提供操作给出的状态键。
        public IReadOnlyList<string> ProvidedStateKeys => m_Provided;
        public bool RequestsSectionContinue { get; internal set; }
        // "进入下一分段"请求所依赖的条件读取。
        public IReadOnlyList<OrchestrationStaticRead> ContinueReads => m_ContinueReads;
        public IReadOnlyList<OrchestrationStaticContinueRequest> ContinueRequests => m_ContinueRequests;
        public IReadOnlyList<OrchestrationStaticRead> Reads => m_Reads;
        internal readonly List<string> m_Provided = new();
        internal readonly List<OrchestrationStaticRead> m_ContinueReads = new();
        internal readonly List<OrchestrationStaticContinueRequest> m_ContinueRequests = new();
        internal readonly List<OrchestrationStaticRead> m_Reads = new();
    }

    public sealed class OrchestrationStaticContinueRequest
    {
        internal OrchestrationStaticContinueRequest(int operationIndex, OrchestrationTimelineHook hook,
            int timelineOperationIndex, IReadOnlyList<OrchestrationStaticRead> reads, OrchestrationStaticCondition condition)
        {
            OperationIndex = operationIndex;
            Hook = hook;
            TimelineOperationIndex = timelineOperationIndex;
            Reads = reads;
            Condition = condition;
        }

        public int OperationIndex { get; }
        public OrchestrationTimelineHook Hook { get; }
        public int TimelineOperationIndex { get; }
        public IReadOnlyList<OrchestrationStaticRead> Reads { get; }
        public OrchestrationStaticCondition Condition { get; }
    }

    public sealed class OrchestrationStaticTimeline
    {
        internal OrchestrationStaticTimeline(TimelineContentUnit content,
            IReadOnlyList<OrchestrationStaticSection> sections, IReadOnlyList<OrchestrationStaticClip> clips)
        {
            Content = content;
            Sections = sections;
            Clips = clips;
        }

        public TimelineContentUnit Content { get; }
        public string Identity => Content.Identity;
        public OrchestrationTime Duration => Content.Duration;
        public bool Loop => Content.Loop;
        public IReadOnlyList<OrchestrationStaticSection> Sections { get; }
        public IReadOnlyList<OrchestrationStaticClip> Clips { get; }

        public OrchestrationStaticSection SectionAt(OrchestrationTime time)
        {
            foreach (OrchestrationStaticSection section in Sections)
                if (section.Start <= time && time < section.End)
                    return section;
            return null;
        }

        public OrchestrationStaticSection Section(string authoringId)
        {
            foreach (OrchestrationStaticSection section in Sections)
                if (string.Equals(section.AuthoringId, authoringId, StringComparison.Ordinal))
                    return section;
            return null;
        }
    }

    public sealed class OrchestrationStaticTransition
    {
        internal OrchestrationStaticTransition(string identity, OrchestrationStaticState source, int targetOperation,
            int priority, int order, int controlFlowIndex, OrchestrationStaticSourceLocation location,
            IReadOnlyList<OrchestrationStaticRead> reads, OrchestrationStaticCondition condition)
        {
            Identity = identity;
            Source = source;
            TargetOperation = targetOperation;
            Priority = priority;
            Order = order;
            ControlFlowIndex = controlFlowIndex;
            Location = location;
            Reads = reads;
            Condition = condition;
        }

        public string Identity { get; }
        public OrchestrationStaticState Source { get; }
        public OrchestrationStaticState Target { get; internal set; }
        internal int TargetOperation { get; }
        public int TargetOperationIndex => TargetOperation;
        public int Priority { get; }
        public int Order { get; }
        internal int ControlFlowIndex { get; }
        public bool FromAnyState { get; internal set; }
        public OrchestrationStaticSourceLocation Location { get; }
        public IReadOnlyList<OrchestrationStaticRead> Reads { get; }
        public OrchestrationStaticCondition Condition { get; }
    }

    public sealed class OrchestrationStaticState
    {
        internal OrchestrationStaticState(int operationIndex, OrchestrationStaticSourceLocation location)
        {
            OperationIndex = operationIndex;
            Location = location;
        }

        public int OperationIndex { get; }
        public OrchestrationStaticSourceLocation Location { get; }
        public string Name => Location.DisplayPath.Length != 0 ? Location.DisplayPath : Location.NodeId;
        public int MachineOperationIndex { get; internal set; } = -1;
        public bool IsInitial { get; internal set; }
        public string TimelineIdentity { get; internal set; } = string.Empty;
        public OrchestrationTimelinePlaybackMode PlaybackMode { get; internal set; }
        public OrchestrationStaticTimeline Timeline { get; internal set; }
        public int TimelineOperationIndex { get; internal set; }
        public bool TimelineDeterminesRootCompletion { get; internal set; }
        public bool HasStaticTimelineClock { get; internal set; }
        public bool HasStaticTimelineStartTime { get; internal set; }
        public OrchestrationTime TimelineStartTime { get; internal set; }
        internal string FrameIdentity { get; set; }
        public IReadOnlyList<OrchestrationStaticTransition> Transitions => m_Transitions;
        internal readonly List<OrchestrationStaticTransition> m_Transitions = new();
    }

    public sealed class OrchestrationStaticQueryInput
    {
        public OrchestrationStaticQueryInput(IReadOnlyList<SemanticOperation> operations,
            IReadOnlyList<SemanticLiteral> literals, IReadOnlyList<ProgramControlFlowEdge> controlFlow,
            IReadOnlyList<ProgramSourceMapEntry> sourceMap, Func<string, TimelineContentUnit> resolveTimeline,
            IReadOnlyList<IOrchestrationStateKeyDeclarations> declarations, IReadOnlyList<ProgramReference> references,
            IReadOnlyList<SemanticConstantInputBinding> constantInputs, OrchestrationIdentityTable identities)
        {
            Operations = operations;
            Literals = literals;
            ControlFlow = controlFlow;
            SourceMap = sourceMap;
            ResolveTimeline = resolveTimeline;
            Declarations = declarations;
            References = references;
            ConstantInputs = constantInputs;
            Identities = identities;
        }

        public IReadOnlyList<SemanticOperation> Operations { get; }
        public IReadOnlyList<SemanticLiteral> Literals { get; }
        public IReadOnlyList<ProgramControlFlowEdge> ControlFlow { get; }
        public IReadOnlyList<ProgramSourceMapEntry> SourceMap { get; }
        public Func<string, TimelineContentUnit> ResolveTimeline { get; }
        public IReadOnlyList<IOrchestrationStateKeyDeclarations> Declarations { get; }
        public IReadOnlyList<ProgramReference> References { get; }
        public IReadOnlyList<SemanticConstantInputBinding> ConstantInputs { get; }
        public OrchestrationIdentityTable Identities { get; }
    }

    // 只读静态查询：只读取正式编译的语义程序与图结构，不执行任何图，不识别业务节点类型。
    public sealed class OrchestrationStaticQuery
    {
        readonly OrchestrationStaticQueryInput m_Input;
        readonly OrchestrationFlowEdge[] m_ExecutionEdges;
        readonly List<int>[] m_Outgoing;
        readonly List<int>[] m_ValueInputs;
        readonly List<int>[] m_Incoming;
        readonly OrchestrationStaticSourceLocation[] m_Locations;
        readonly OrchestrationStaticSourceLocation[] m_EdgeLocations;
        readonly StateKeys[] m_StateKeys;
        readonly string[] m_OperationInvocations;
        readonly Dictionary<string, (string Parent, bool Timeline)> m_Invocations = new(StringComparer.Ordinal);
        readonly Dictionary<(int Operation, string Port), int> m_ConstantInputs = new();
        readonly Dictionary<int, OrchestrationStaticState> m_States = new();
        readonly Dictionary<int, int> m_MachineEntries = new();
        readonly Dictionary<int, List<OrchestrationStaticTransition>> m_InitialTransitions = new();
        readonly Dictionary<string, OrchestrationStaticTimeline> m_Timelines = new(StringComparer.Ordinal);
        readonly List<string> m_Errors = new();

        OrchestrationStaticQuery(OrchestrationStaticQueryInput input)
        {
            m_Input = input;
            m_ExecutionEdges = new OrchestrationFlowEdge[input.ControlFlow.Count];
            int count = input.Operations.Count;
            m_Outgoing = new List<int>[count];
            m_ValueInputs = new List<int>[count];
            m_Incoming = new List<int>[count];
            m_Locations = new OrchestrationStaticSourceLocation[count];
            m_EdgeLocations = new OrchestrationStaticSourceLocation[input.ControlFlow.Count];
            m_StateKeys = new StateKeys[count];
            m_OperationInvocations = new string[count];
            for (int i = 0; i < count; i++)
            {
                m_Outgoing[i] = new List<int>();
                m_ValueInputs[i] = new List<int>();
                m_Incoming[i] = new List<int>();
                m_Locations[i] = new OrchestrationStaticSourceLocation(string.Empty, string.Empty, string.Empty, string.Empty);
            }
            for (int i = 0; i < input.ControlFlow.Count; i++)
            {
                ProgramControlFlowEdge edge = input.ControlFlow[i];
                m_Outgoing[edge.Source.Value].Add(i);
                if (edge.Kind == ProgramControlFlowKind.Value)
                    m_ValueInputs[edge.Target.Value].Add(i);
                else
                {
                    m_Incoming[edge.Target.Value].Add(i);
                    m_ExecutionEdges[i] = new OrchestrationFlowEdge(input.Identities.Bind(edge.Identity),
                        edge.Source.Value, edge.Target.Value, input.Identities.Bind(edge.SourcePort),
                        input.Identities.Bind(edge.TargetPort), (OrchestrationFlowKind)edge.Kind,
                        edge.Order, edge.Priority, edge.AbortPolicy, edge.HasCondition ? edge.Condition.Value : -1);
                }
            }
            for (int i = 0; i < count; i++)
                m_Outgoing[i].Sort(CompareEdges);
            foreach (SemanticConstantInputBinding binding in input.ConstantInputs)
                m_ConstantInputs.Add((binding.TargetOperation.Value, binding.TargetPort), binding.ConstantIndex);
            foreach (ProgramSourceMapEntry entry in input.SourceMap)
            {
                ProgramExecutionSource source = entry.Source;
                if (source.SourceInvocationPath.Length != 0)
                {
                    bool timeline = source.TargetKind == ProgramSourceTargetKind.GraphInvocation &&
                        source.InvocationCallerKind is ProgramInvocationCallerKind.TimelineClip or
                            ProgramInvocationCallerKind.PresentationMarker or ProgramInvocationCallerKind.PresentationTreeClip;
                    if (m_Invocations.TryGetValue(source.SourceInvocationPath, out var invocation))
                        timeline |= invocation.Timeline;
                    m_Invocations[source.SourceInvocationPath] = (source.ParentInvocationPath, timeline);
                }
                if (source.TargetKind == ProgramSourceTargetKind.Operation)
                {
                    m_OperationInvocations[source.TargetIndex] = source.GraphInvocationPath;
                    m_Locations[source.TargetIndex] = new OrchestrationStaticSourceLocation(source.GraphId, source.NodeId,
                        source.EdgeId, entry.DisplayPath);
                }
                else if (source.TargetKind == ProgramSourceTargetKind.Reference && source.EdgeId.Length != 0)
                    m_EdgeLocations[source.TargetIndex] = new OrchestrationStaticSourceLocation(source.GraphId, source.NodeId,
                        source.EdgeId, entry.DisplayPath);
            }
        }

        public IEnumerable<OrchestrationStaticState> States => m_States.Values;
        public IEnumerable<OrchestrationStaticTimeline> Timelines => m_Timelines.Values;
        public IReadOnlyList<string> Errors => m_Errors;

        public OrchestrationStaticState State(int operationIndex) =>
            m_States.TryGetValue(operationIndex, out OrchestrationStaticState state) ? state : null;

        public OrchestrationStaticTimeline Timeline(string identity) =>
            identity != null && m_Timelines.TryGetValue(identity, out OrchestrationStaticTimeline timeline) ? timeline : null;

        public IReadOnlyList<OrchestrationStaticTransition> InitialTransitions(int machineOperationIndex) =>
            m_InitialTransitions[machineOperationIndex];

        public static OrchestrationStaticQuery Build(OrchestrationStaticQueryInput input)
        {
            var query = new OrchestrationStaticQuery(input);
            query.BuildStates();
            query.BuildClipGraphs();
            query.BuildTransitions();
            return query;
        }

        IReadOnlyList<SemanticOperation> Operations => m_Input.Operations;

        void BuildStates()
        {
            for (int i = 0; i < Operations.Count; i++)
                if (Operations[i].Code == OrchestrationOperationCode.State)
                    m_States.Add(i, new OrchestrationStaticState(i, m_Locations[i]));
            foreach (ProgramReference reference in m_Input.References)
                if (reference.Kind == ProgramReferenceKind.Operation && reference.HasSourceOperation &&
                    Operations[reference.SourceOperation.Value].Code == OrchestrationOperationCode.State &&
                    Operations[reference.TargetIndex].Code == OrchestrationOperationCode.StateMachine)
                    m_States[reference.SourceOperation.Value].MachineOperationIndex = reference.TargetIndex;
            for (int i = 0; i < Operations.Count; i++)
            {
                if (Operations[i].Code != OrchestrationOperationCode.StateMachine)
                    continue;
                m_InitialTransitions.Add(i, new List<OrchestrationStaticTransition>());
                foreach (int edgeIndex in m_Outgoing[i])
                {
                    ProgramControlFlowEdge edge = m_Input.ControlFlow[edgeIndex];
                    if (edge.Kind != ProgramControlFlowKind.Enter)
                        continue;
                    int entry = edge.Target.Value;
                    if (Operations[entry].Code is not (OrchestrationOperationCode.StateEnter or OrchestrationOperationCode.StateAny))
                        continue;
                    m_MachineEntries[entry] = i;
                }
            }
            foreach (OrchestrationStaticState state in m_States.Values)
            {
                state.FrameIdentity = FrameIdentity(state.OperationIndex);
                int timeline = FindBodyTimeline(state.OperationIndex);
                if (timeline < 0)
                    continue;
                state.TimelineIdentity = Operations[timeline].Text0;
                state.TimelineOperationIndex = timeline;
                state.PlaybackMode = (OrchestrationTimelinePlaybackMode)Operations[timeline].Integer0;
                state.Timeline = RequireTimeline(state.TimelineIdentity);
                foreach (int edgeIndex in m_Outgoing[state.OperationIndex])
                {
                    ProgramControlFlowEdge edge = m_Input.ControlFlow[edgeIndex];
                    if (edge.Kind == ProgramControlFlowKind.Enter && edge.SourcePort == "Root")
                        state.TimelineDeterminesRootCompletion = IsTimelineRoot(edge.Target.Value, timeline);
                }
                bool rateKnown = TryLiteral(Input(timeline, "m_PlaybackRate"), out SemanticLiteral rate) &&
                    rate.Kind == SemanticLiteralKind.Number && rate.Number.Value == 1d;
                bool pauseKnown = TryLiteral(Input(timeline, "m_Paused"), out SemanticLiteral paused) &&
                    paused.Kind == SemanticLiteralKind.Boolean && !paused.Boolean;
                bool startKnown = TryLiteral(Input(timeline, "m_StartTime"), out SemanticLiteral start) &&
                    start.Kind == SemanticLiteralKind.Number;
                state.HasStaticTimelineClock = rateKnown && pauseKnown;
                state.HasStaticTimelineStartTime = startKnown;
                if (startKnown)
                    state.TimelineStartTime = OrchestrationTime.FromSeconds(start.Number.Value);
            }
        }

        string FrameIdentity(int index)
        {
            string path = m_OperationInvocations[index];
            while (m_Invocations[path].Parent.Length != 0 && !m_Invocations[path].Timeline)
                path = m_Invocations[path].Parent;
            return path;
        }

        bool IsTimelineRoot(int index, int timeline)
        {
            if (index == timeline)
                return true;
            if (Operations[index].Code is not (OrchestrationOperationCode.Root or OrchestrationOperationCode.SubGraph or
                OrchestrationOperationCode.Sequence))
                return false;
            int child = -1;
            foreach (int edgeIndex in m_Outgoing[index])
            {
                ProgramControlFlowEdge edge = m_Input.ControlFlow[edgeIndex];
                if (edge.Kind != (Operations[index].Code == OrchestrationOperationCode.SubGraph
                    ? ProgramControlFlowKind.Enter : ProgramControlFlowKind.Child))
                    continue;
                if (edge.HasCondition || child >= 0)
                    return false;
                child = edge.Target.Value;
            }
            return child >= 0 && IsTimelineRoot(child, timeline);
        }

        int FindBodyTimeline(int stateOperation)
        {
            int found = -1;
            foreach (int edgeIndex in m_Outgoing[stateOperation])
            {
                ProgramControlFlowEdge edge = m_Input.ControlFlow[edgeIndex];
                if (edge.Kind != ProgramControlFlowKind.Enter || edge.SourcePort != "Root")
                    continue;
                foreach (int target in Reachable(edge.Target.Value))
                {
                    if (Operations[target].Code != OrchestrationOperationCode.Timeline)
                        continue;
                    if (found >= 0 && found != target)
                    {
                        m_Errors.Add($"状态 '{m_Locations[stateOperation].DisplayPath}' 的 Root 包含多个 Timeline，需在会话中验证其选择与时长。");
                        return -1;
                    }
                    found = target;
                }
            }
            return found;
        }

        OrchestrationStaticTimeline RequireTimeline(string identity)
        {
            if (string.IsNullOrEmpty(identity))
                return null;
            if (m_Timelines.TryGetValue(identity, out OrchestrationStaticTimeline existing))
                return existing;
            TimelineContentUnit content = m_Input.ResolveTimeline(identity);
            if (content == null)
            {
                m_Errors.Add($"Timeline '{identity}' 无法解析，相关状态不显示时间条。");
                m_Timelines.Add(identity, null);
                return null;
            }
            var order = new List<TimelineContentSection>(content.Sections);
            order.Sort((left, right) => left.Time.CompareTo(right.Time));
            var sections = new List<OrchestrationStaticSection>(order.Count);
            for (int i = 0; i < order.Count; i++)
                sections.Add(new OrchestrationStaticSection(i, order[i].AuthoringId, order[i].Time,
                    i + 1 < order.Count ? order[i + 1].Time : content.Duration, order[i].NextSectionId));
            var clips = new List<OrchestrationStaticClip>(content.Clips.Count);
            foreach (TimelineContentClip clip in content.Clips)
                clips.Add(new OrchestrationStaticClip(identity, clip.AuthoringId, clip.TrackAuthoringId,
                    clip.StartTime, clip.EndTime, clip.ExecutionPolicy.Domain, clip.TrackMuted));
            var timeline = new OrchestrationStaticTimeline(content, sections, clips);
            m_Timelines.Add(identity, timeline);
            return timeline;
        }

        void BuildClipGraphs()
        {
            foreach (ProgramSourceMapEntry sourceEntry in m_Input.SourceMap)
            {
                ProgramExecutionSource source = sourceEntry.Source;
                if (source.TargetKind != ProgramSourceTargetKind.GraphInvocation ||
                    source.InvocationCallerKind is not (ProgramInvocationCallerKind.TimelineClip or
                        ProgramInvocationCallerKind.PresentationTreeClip))
                    continue;
                OrchestrationStaticTimeline timeline = RequireTimeline(Operations[source.InvocationCallerOperationIndex].Text0);
                if (timeline == null)
                    continue;
                OrchestrationStaticClip clip = null;
                foreach (OrchestrationStaticClip candidate in timeline.Clips)
                    if (string.Equals(candidate.ClipId, source.InvocationCallerClipId, StringComparison.Ordinal))
                        clip = candidate;
                if (clip == null)
                    throw new InvalidOperationException($"Timeline '{timeline.Identity}' 未声明图调用所属的 Clip '{source.InvocationCallerClipId}'。");
                clip.HasGraph = true;
                int entry = source.InvocationEntryOperationIndex;
                SemanticOperation owner = Operations[source.TargetIndex];
                OrchestrationTimelineHook hook = owner.Code == OrchestrationOperationCode.TimelineEnter
                    ? (OrchestrationTimelineHook)owner.Integer0 : OrchestrationTimelineHook.Root;
                HashSet<int> members = Reachable(entry);
                foreach (int member in members)
                {
                    CollectValueReads(member, clip.m_Reads, new HashSet<int>());
                    foreach (int edgeIndex in m_Outgoing[member])
                        if (m_Input.ControlFlow[edgeIndex].HasCondition)
                            CollectConditionReads(m_Input.ControlFlow[edgeIndex].Condition.Value, clip.m_Reads);
                }
                if (clip.Muted || clip.Domain != TimelineExecutionDomain.Logic ||
                    hook is not (OrchestrationTimelineHook.Enable or OrchestrationTimelineHook.Root))
                    continue;
                CollectUnconditionalProvides(entry, clip.m_Provided, new HashSet<int>(),
                    hook == OrchestrationTimelineHook.Enable);
                foreach (int member in members)
                    if (Operations[member].Code == OrchestrationOperationCode.TimelineSectionContinueRequest)
                    {
                        OrchestrationStaticCondition condition = OrchestrationStaticCondition.Combine(true,
                            DescribePath(entry, member, members, new HashSet<int>()), DescribeInput(member, "m_When"));
                        var reads = new List<OrchestrationStaticRead>();
                        CollectReads(condition, reads);
                        clip.RequestsSectionContinue = true;
                        clip.m_ContinueRequests.Add(new OrchestrationStaticContinueRequest(member, hook,
                            source.InvocationCallerOperationIndex, reads, condition));
                        foreach (OrchestrationStaticRead read in reads)
                            Add(clip.m_ContinueReads, read);
                    }
            }
        }

        HashSet<int> Reachable(int entry)
        {
            var visited = new HashSet<int>();
            var pending = new Stack<int>();
            pending.Push(entry);
            while (pending.Count != 0)
            {
                int index = pending.Pop();
                if (!visited.Add(index))
                    continue;
                if (Operations[index].Code is OrchestrationOperationCode.Succeed or OrchestrationOperationCode.State or
                    OrchestrationOperationCode.StateMachine or OrchestrationOperationCode.Timeline)
                    continue;
                foreach (int edgeIndex in m_Outgoing[index])
                {
                    ProgramControlFlowEdge edge = m_Input.ControlFlow[edgeIndex];
                    if (edge.Kind == ProgramControlFlowKind.Child ||
                        Operations[index].Code == OrchestrationOperationCode.SubGraph && edge.Kind == ProgramControlFlowKind.Enter)
                        pending.Push(edge.Target.Value);
                }
            }
            return visited;
        }

        bool CollectUnconditionalProvides(int index, List<string> provided, HashSet<int> visited, bool complete)
        {
            if (!visited.Add(index))
                return false;
            SemanticOperation operation = Operations[index];
            if (operation.Code == OrchestrationOperationCode.Extension)
            {
                if (operation.Extension.Value.Kind == OrchestrationExtensionKind.Action)
                {
                    StateKeys declaration = Declare(operation);
                    if (declaration.Declared)
                        foreach (string key in declaration.Provided)
                            if (!provided.Contains(key))
                                provided.Add(key);
                }
                return false;
            }
            if (operation.Code == OrchestrationOperationCode.Succeed)
                return true;
            if (operation.Code is OrchestrationOperationCode.Branch or OrchestrationOperationCode.Cycle or
                OrchestrationOperationCode.StateMachine or OrchestrationOperationCode.State or OrchestrationOperationCode.Timeline)
                return false;
            bool composite = operation.Code is OrchestrationOperationCode.Root or OrchestrationOperationCode.TimelineEnter or
                OrchestrationOperationCode.SubGraph or OrchestrationOperationCode.Sequence or OrchestrationOperationCode.Parallel or
                OrchestrationOperationCode.Selector or OrchestrationOperationCode.Loop;
            if (!composite && operation.Code is not (OrchestrationOperationCode.BlackboardSet or OrchestrationOperationCode.EmitOutput))
                return false;
            if (!complete && !composite)
                return true;
            bool parallel = operation.Code == OrchestrationOperationCode.Parallel;
            bool allSucceed = true;
            foreach (int edgeIndex in m_Outgoing[index])
            {
                ProgramControlFlowEdge edge = m_Input.ControlFlow[edgeIndex];
                if (edge.Kind != (operation.Code == OrchestrationOperationCode.SubGraph
                    ? ProgramControlFlowKind.Enter : ProgramControlFlowKind.Child))
                    continue;
                bool succeeds = !edge.HasCondition && CollectUnconditionalProvides(edge.Target.Value, provided, visited, complete);
                allSucceed &= succeeds;
                if (!parallel && !succeeds || parallel && operation.Integer0 == 2 ||
                    operation.Code is OrchestrationOperationCode.Selector or OrchestrationOperationCode.Loop)
                    break;
            }
            return allSucceed && operation.Code is not (OrchestrationOperationCode.Selector or OrchestrationOperationCode.Loop);
        }

        OrchestrationStaticCondition DescribePath(int entry, int target, HashSet<int> members, HashSet<int> active)
        {
            if (target == entry)
                return OrchestrationStaticCondition.Constant(true);
            if (!active.Add(target))
                return UnknownPredicate(target);
            var paths = new List<OrchestrationStaticCondition>();
            foreach (int edgeIndex in m_Incoming[target])
            {
                ProgramControlFlowEdge edge = m_Input.ControlFlow[edgeIndex];
                int source = edge.Source.Value;
                if (!members.Contains(source) || edge.Kind is not (ProgramControlFlowKind.Child or ProgramControlFlowKind.Enter))
                    continue;
                var terms = new List<OrchestrationStaticCondition> { DescribePath(entry, source, members, active) };
                if (edge.HasCondition)
                    terms.Add(DescribeControl(edge.Condition.Value, new HashSet<int>()));
                OrchestrationOperationCode code = Operations[source].Code;
                if (code is OrchestrationOperationCode.Sequence or OrchestrationOperationCode.Selector or OrchestrationOperationCode.Root)
                {
                    foreach (int preceding in m_Outgoing[source])
                    {
                        if (preceding == edgeIndex)
                            break;
                        ProgramControlFlowEdge prerequisite = m_Input.ControlFlow[preceding];
                        if (prerequisite.Kind != ProgramControlFlowKind.Child)
                            continue;
                        OrchestrationStaticCondition predicate = DescribeEdge(prerequisite, new HashSet<int>());
                        terms.Add(code == OrchestrationOperationCode.Selector
                            ? OrchestrationStaticCondition.Negate(predicate) : predicate);
                    }
                }
                else if (code == OrchestrationOperationCode.Branch && edge.SourcePort is "True" or "False")
                {
                    OrchestrationStaticCondition predicate = DescribeInput(source, "Condition");
                    terms.Add(edge.SourcePort == "True" ? predicate : OrchestrationStaticCondition.Negate(predicate));
                }
                else if (code is not (OrchestrationOperationCode.SubGraph or OrchestrationOperationCode.TimelineEnter))
                    terms.Add(DescribeControl(source, new HashSet<int>()));
                paths.Add(OrchestrationStaticCondition.Combine(true, terms));
            }
            active.Remove(target);
            return OrchestrationStaticCondition.Combine(false, paths);
        }

        void BuildTransitions()
        {
            for (int edgeIndex = 0; edgeIndex < m_Input.ControlFlow.Count; edgeIndex++)
            {
                ProgramControlFlowEdge edge = m_Input.ControlFlow[edgeIndex];
                if (edge.Kind != ProgramControlFlowKind.Transition)
                    continue;
                int sourceOperation = edge.Source.Value;
                bool any = Operations[sourceOperation].Code == OrchestrationOperationCode.StateAny;
                bool initial = Operations[sourceOperation].Code == OrchestrationOperationCode.StateEnter;
                if (!any && !initial && !m_States.ContainsKey(sourceOperation))
                    continue;
                var reads = new List<OrchestrationStaticRead>();
                OrchestrationStaticCondition condition = edge.HasCondition
                    ? DescribeControl(edge.Condition.Value, new HashSet<int>()) : OrchestrationStaticCondition.Constant(true);
                CollectReads(condition, reads);
                OrchestrationStaticSourceLocation edgeLocation = m_EdgeLocations[edgeIndex];
                if (initial)
                {
                    var transition = new OrchestrationStaticTransition(edge.Identity, null, edge.Target.Value,
                        edge.Priority, edge.Order, edgeIndex, edgeLocation, reads, condition) { Target = State(edge.Target.Value) };
                    m_InitialTransitions[m_MachineEntries[sourceOperation]].Add(transition);
                    if (transition.Target != null)
                        transition.Target.IsInitial = true;
                    continue;
                }
                int machine = any ? m_MachineEntries[sourceOperation] : -1;
                foreach (OrchestrationStaticState source in m_States.Values)
                {
                    if (any ? source.MachineOperationIndex != machine || source.OperationIndex == edge.Target.Value
                        : source.OperationIndex != sourceOperation)
                        continue;
                    var transition = new OrchestrationStaticTransition(edge.Identity, source, edge.Target.Value,
                        edge.Priority, edge.Order, edgeIndex, edgeLocation, reads, condition) { FromAnyState = any };
                    transition.Target = State(edge.Target.Value);
                    source.m_Transitions.Add(transition);
                }
            }
            foreach (List<OrchestrationStaticTransition> initial in m_InitialTransitions.Values)
                initial.Sort((left, right) => CompareEdges(left.ControlFlowIndex, right.ControlFlowIndex));
            foreach (OrchestrationStaticState state in m_States.Values)
                state.m_Transitions.Sort(CompareTransitions);
        }

        int CompareEdges(int leftIndex, int rightIndex)
        {
            ProgramControlFlowEdge left = m_Input.ControlFlow[leftIndex];
            ProgramControlFlowEdge right = m_Input.ControlFlow[rightIndex];
            int kind = left.Kind.CompareTo(right.Kind);
            if (kind != 0)
                return kind;
            return left.Kind == ProgramControlFlowKind.Value ? leftIndex.CompareTo(rightIndex) :
                OrchestrationFlowEdge.CompareOrder(in m_ExecutionEdges[leftIndex], in m_ExecutionEdges[rightIndex]);
        }

        int CompareTransitions(OrchestrationStaticTransition left, OrchestrationStaticTransition right)
        {
            int result = right.FromAnyState.CompareTo(left.FromAnyState);
            return result == 0 ? CompareEdges(left.ControlFlowIndex, right.ControlFlowIndex) : result;
        }

        public OrchestrationStaticCondition DescribeCondition(int operationIndex) =>
            DescribeControl(operationIndex, new HashSet<int>());

        public OrchestrationStaticCondition DescribeInvocationPath(int entryOperationIndex, int targetOperationIndex) =>
            DescribePath(entryOperationIndex, targetOperationIndex, Reachable(entryOperationIndex), new HashSet<int>());

        void CollectConditionReads(int condition, List<OrchestrationStaticRead> reads)
        {
            CollectReads(DescribeControl(condition, new HashSet<int>()), reads);
        }

        static void CollectReads(OrchestrationStaticCondition condition, List<OrchestrationStaticRead> reads)
        {
            if (condition.Kind is OrchestrationStaticConditionKind.Read or
                OrchestrationStaticConditionKind.StateRootCompleted or OrchestrationStaticConditionKind.TimelineSection)
                Add(reads, condition.Read);
            foreach (OrchestrationStaticCondition child in condition.Children)
                CollectReads(child, reads);
        }

        OrchestrationStaticCondition DescribeControl(int index, HashSet<int> active)
        {
            if (!active.Add(index))
                return UnknownPredicate(index);
            OrchestrationOperationCode code = Operations[index].Code;
            OrchestrationStaticCondition result;
            if (code == OrchestrationOperationCode.Succeed)
                result = OrchestrationStaticCondition.Constant(true);
            else if (code is OrchestrationOperationCode.Root or OrchestrationOperationCode.Sequence or
                OrchestrationOperationCode.Selector or OrchestrationOperationCode.SubGraph or OrchestrationOperationCode.TimelineEnter)
            {
                var children = new List<OrchestrationStaticCondition>();
                foreach (int edgeIndex in m_Outgoing[index])
                {
                    ProgramControlFlowEdge edge = m_Input.ControlFlow[edgeIndex];
                    if (edge.Kind != (code == OrchestrationOperationCode.SubGraph
                        ? ProgramControlFlowKind.Enter : ProgramControlFlowKind.Child))
                        continue;
                    children.Add(DescribeEdge(edge, active));
                }
                result = OrchestrationStaticCondition.Combine(code != OrchestrationOperationCode.Selector, children);
            }
            else if (code == OrchestrationOperationCode.ConditionResult)
                result = DescribeInput(index, "m_Result");
            else if (code is OrchestrationOperationCode.TimelineSectionContinueRequest or OrchestrationOperationCode.TimelineClipExitRequest)
                result = OrchestrationStaticCondition.Constant(true);
            else
                result = DescribeValue(new StaticValue(index, string.Empty, null), new HashSet<int>());
            active.Remove(index);
            return result;
        }

        OrchestrationStaticCondition DescribeEdge(ProgramControlFlowEdge edge, HashSet<int> active) =>
            OrchestrationStaticCondition.Combine(true,
                edge.HasCondition ? DescribeControl(edge.Condition.Value, active) : OrchestrationStaticCondition.Constant(true),
                DescribeControl(edge.Target.Value, active));

        readonly struct StaticValue
        {
            internal StaticValue(int operation, string port, SemanticLiteral literal, int inputOwner = -1)
            {
                Operation = operation;
                Port = port;
                Literal = literal;
                InputOwner = inputOwner;
            }

            internal int Operation { get; }
            internal string Port { get; }
            internal SemanticLiteral Literal { get; }
            internal int InputOwner { get; }
        }

        StaticValue Input(int index, string port)
        {
            foreach (int edgeIndex in m_ValueInputs[index])
            {
                ProgramControlFlowEdge edge = m_Input.ControlFlow[edgeIndex];
                if (edge.TargetPort == port)
                    return new StaticValue(edge.Source.Value, edge.SourcePort, null);
            }
            return m_ConstantInputs.TryGetValue((index, port), out int literal)
                ? new StaticValue(-1, port, m_Input.Literals[literal]) : new StaticValue(-1, port, null, index);
        }

        StaticValue Unwrap(StaticValue value)
        {
            while (value.Operation >= 0 && Operations[value.Operation].Code == OrchestrationOperationCode.IdentityValue)
                value = Input(value.Operation, "m_Value");
            return value;
        }

        bool TryLiteral(StaticValue source, out SemanticLiteral literal)
        {
            StaticValue value = Unwrap(source);
            literal = value.Literal;
            if (value.Operation < 0)
                return literal != null;
            SemanticOperation operation = Operations[value.Operation];
            foreach (SemanticConstantOutputBinding output in operation.ConstantOutputs)
                if (output.OutputPortId == value.Port)
                {
                    literal = m_Input.Literals[output.LiteralIndex];
                    return true;
                }
            if (operation.Code != OrchestrationOperationCode.Constant || operation.LiteralReferences.Count == 0)
                return false;
            literal = m_Input.Literals[operation.LiteralReferences[0]];
            return true;
        }

        OrchestrationStaticCondition DescribeInput(int index, string port) =>
            DescribeValue(Input(index, port), new HashSet<int>());

        OrchestrationStaticCondition DescribeValue(StaticValue source, HashSet<int> active)
        {
            if (TryLiteral(source, out SemanticLiteral literal))
                return OrchestrationStaticCondition.Constant(LiteralBoolean(literal));
            StaticValue value = Unwrap(source);
            int index = value.Operation;
            if (index < 0)
                return UnknownPredicate(value.InputOwner);
            if (!active.Add(index))
                return UnknownPredicate(index);
            SemanticOperation operation = Operations[index];
            OrchestrationStaticCondition result;
            switch (operation.Code)
            {
                case OrchestrationOperationCode.And:
                case OrchestrationOperationCode.Or:
                    result = OrchestrationStaticCondition.Combine(operation.Code == OrchestrationOperationCode.And,
                        DescribeValue(Input(index, "m_Input1"), active), DescribeValue(Input(index, "m_Input2"), active));
                    break;
                case OrchestrationOperationCode.Not:
                    result = OrchestrationStaticCondition.Negate(DescribeValue(Input(index, "m_Input"), active));
                    break;
                case OrchestrationOperationCode.ConditionResult:
                    result = DescribeValue(Input(index, "m_Result"), active);
                    break;
                case OrchestrationOperationCode.StateRootCompleted:
                    result = new OrchestrationStaticCondition(OrchestrationStaticConditionKind.StateRootCompleted,
                        Array.Empty<OrchestrationStaticCondition>(), new OrchestrationStaticRead(index,
                            OrchestrationStaticReadSource.StateCompletion, "StateRootCompleted", false, "状态主体自然完成"));
                    break;
                case OrchestrationOperationCode.Constant:
                    result = OrchestrationStaticCondition.Constant(false);
                    break;
                case OrchestrationOperationCode.TimelineSection:
                    result = OrchestrationStaticCondition.Negate(SectionCondition(index, index, string.Empty));
                    break;
                case OrchestrationOperationCode.Calculate when (GraphCalculationKind)operation.Integer0 == GraphCalculationKind.TextEqual:
                    result = DescribeTextEqual(index);
                    break;
                case OrchestrationOperationCode.Compare:
                    result = DescribeTimelineTimeCompare(index);
                    break;
                case OrchestrationOperationCode.InputBoolean:
                case OrchestrationOperationCode.InputScalar:
                case OrchestrationOperationCode.InputVector2Magnitude:
                case OrchestrationOperationCode.InputRequest:
                case OrchestrationOperationCode.HostInput:
                {
                    var reads = new List<OrchestrationStaticRead>();
                    if (operation.HostInputs.Count == 0)
                        AddHostInputs(index, operation, reads);
                    else
                        for (int i = 0; i < operation.HostInputs.Count; i++)
                        {
                            SemanticHostInputPort input = operation.HostInputs[i];
                            if (value.Port.Length == 0 ? i == 0 : input.OutputPortId == value.Port)
                                Add(reads, new OrchestrationStaticRead(index, OrchestrationStaticReadSource.HostInput,
                                    input.InputId, false, input.InputId));
                        }
                    var terms = new List<OrchestrationStaticCondition>();
                    foreach (OrchestrationStaticRead read in reads)
                        terms.Add(ReadCondition(in read));
                    result = terms.Count != 0 ? OrchestrationStaticCondition.Combine(true, terms) : UnknownPredicate(index);
                    break;
                }
                case OrchestrationOperationCode.Extension:
                    result = Declare(operation).Predicate ?? UnknownPredicate(index);
                    break;
                default:
                    result = UnknownPredicate(index);
                    break;
            }
            active.Remove(index);
            return result;
        }

        static bool LiteralBoolean(SemanticLiteral literal) => literal.Kind switch
        {
            SemanticLiteralKind.Boolean => literal.Boolean,
            SemanticLiteralKind.Int32 => literal.Int32 != 0,
            SemanticLiteralKind.UInt64 => literal.UInt64 != 0,
            SemanticLiteralKind.Number => literal.Number.Value != 0d,
            SemanticLiteralKind.String => literal.String.Length != 0,
            _ => false
        };

        OrchestrationStaticCondition DescribeTextEqual(int index)
        {
            StaticValue a = Unwrap(Input(index, "a"));
            StaticValue b = Unwrap(Input(index, "b"));
            bool aConstant = TryLiteral(a, out SemanticLiteral left) && left.Kind == SemanticLiteralKind.String;
            bool bConstant = TryLiteral(b, out SemanticLiteral right) && right.Kind == SemanticLiteralKind.String;
            if (aConstant && bConstant)
                return OrchestrationStaticCondition.Constant(string.Equals(left.String, right.String, StringComparison.Ordinal));
            if (a.Operation >= 0 && Operations[a.Operation].Code == OrchestrationOperationCode.TimelineSection && bConstant)
                return SectionCondition(index, a.Operation, right.String);
            if (b.Operation >= 0 && Operations[b.Operation].Code == OrchestrationOperationCode.TimelineSection && aConstant)
                return SectionCondition(index, b.Operation, left.String);
            return UnknownPredicate(index);
        }

        OrchestrationStaticCondition DescribeTimelineTimeCompare(int index)
        {
            StaticValue a = Unwrap(Input(index, "m_InputValue1"));
            StaticValue b = Unwrap(Input(index, "m_InputValue2"));
            var comparison = (OrchestrationComparisonKind)Operations[index].Integer0;
            if (IsTimelineTime(a) && TryLiteralSeconds(b, out double seconds))
                return TimelineTimeCondition(index, comparison, seconds);
            if (IsTimelineTime(b) && TryLiteralSeconds(a, out seconds))
                return TimelineTimeCondition(index, Mirror(comparison), seconds);
            return UnknownPredicate(index);
        }

        bool IsTimelineTime(StaticValue value) =>
            value.Operation >= 0 && Operations[value.Operation].Code == OrchestrationOperationCode.TimelineTime;

        bool TryLiteralSeconds(StaticValue value, out double seconds)
        {
            seconds = 0d;
            if (!TryLiteral(value, out SemanticLiteral literal))
                return false;
            switch (literal.Kind)
            {
                case SemanticLiteralKind.Number: seconds = literal.Number.Value; return true;
                case SemanticLiteralKind.Int32: seconds = literal.Int32; return true;
                default: return false;
            }
        }

        static OrchestrationComparisonKind Mirror(OrchestrationComparisonKind comparison) => comparison switch
        {
            OrchestrationComparisonKind.LessThan => OrchestrationComparisonKind.GreaterThan,
            OrchestrationComparisonKind.LessThanOrEqual => OrchestrationComparisonKind.GreaterThanOrEqual,
            OrchestrationComparisonKind.GreaterThanOrEqual => OrchestrationComparisonKind.LessThanOrEqual,
            OrchestrationComparisonKind.GreaterThan => OrchestrationComparisonKind.LessThan,
            _ => comparison
        };

        static OrchestrationStaticCondition TimelineTimeCondition(int index, OrchestrationComparisonKind comparison, double seconds)
        {
            string symbol = comparison switch
            {
                OrchestrationComparisonKind.Equal => "=",
                OrchestrationComparisonKind.NotEqual => "≠",
                OrchestrationComparisonKind.LessThan => "<",
                OrchestrationComparisonKind.LessThanOrEqual => "≤",
                OrchestrationComparisonKind.GreaterThanOrEqual => "≥",
                _ => ">"
            };
            return new OrchestrationStaticCondition(OrchestrationStaticConditionKind.TimelineTime,
                Array.Empty<OrchestrationStaticCondition>(), new OrchestrationStaticRead(index,
                    OrchestrationStaticReadSource.TimelineTime, $"TimelineTime:{(int)comparison}:{seconds:R}", false,
                    $"Timeline 时刻 {symbol} {seconds:0.###} 秒"), comparison: comparison,
                threshold: OrchestrationTime.FromSeconds(seconds));
        }

        OrchestrationStaticCondition SectionCondition(int index, int queryIndex, string sectionId) =>
            new(OrchestrationStaticConditionKind.TimelineSection, Array.Empty<OrchestrationStaticCondition>(),
                new OrchestrationStaticRead(index, OrchestrationStaticReadSource.TimelineSection,
                    $"TimelineSection:{Operations[queryIndex].Text0}:{sectionId}", false, $"Timeline 来源分段 = {sectionId}"),
                timelineIdentity: Operations[queryIndex].Text0, sectionId: sectionId, frameIdentity: FrameIdentity(queryIndex));

        static OrchestrationStaticCondition ReadCondition(in OrchestrationStaticRead read) =>
            new(OrchestrationStaticConditionKind.Read, Array.Empty<OrchestrationStaticCondition>(), read);

        OrchestrationStaticCondition UnknownPredicate(int index)
        {
            SemanticOperation operation = Operations[index];
            string label = m_Locations[index].DisplayPath.Length != 0 ? m_Locations[index].DisplayPath : operation.Code.ToString();
            var read = new OrchestrationStaticRead(index, OrchestrationStaticReadSource.RuntimeData,
                $"Predicate:{operation.TemplateIdentity}", true, $"谓词假设：{label}");
            return ReadCondition(in read);
        }

        void CollectValueReads(int index, List<OrchestrationStaticRead> reads, HashSet<int> visited, bool readsOutput = false)
        {
            if (!visited.Add(index))
                return;
            SemanticOperation operation = Operations[index];
            if (readsOutput && operation.Code is OrchestrationOperationCode.Compare or OrchestrationOperationCode.Calculate)
            {
                CollectReads(DescribeValue(new StaticValue(index, string.Empty, null), new HashSet<int>()), reads);
                return;
            }
            Classify(index, operation, reads, readsOutput);
            foreach (int operand in operation.Operands)
                CollectValueReads(operand, reads, visited, true);
            foreach (int edgeIndex in m_ValueInputs[index])
                CollectValueReads(m_Input.ControlFlow[edgeIndex].Source.Value, reads, visited, true);
            foreach (SemanticBlackboardPortBinding binding in operation.BlackboardInputs)
                Add(reads, new OrchestrationStaticRead(index, OrchestrationStaticReadSource.RuntimeData,
                    $"{binding.OwnerId}/{binding.DeclarationId}", false, $"{binding.OwnerId}/{binding.DeclarationId}"));
            foreach (SemanticDerivedInput derived in operation.DerivedInputs)
                Add(reads, new OrchestrationStaticRead(index, OrchestrationStaticReadSource.RuntimeData,
                    derived.PortId, false, derived.PortId));
        }

        void Classify(int index, SemanticOperation operation, List<OrchestrationStaticRead> reads, bool readsOutput)
        {
            switch (operation.Code)
            {
                case OrchestrationOperationCode.Root:
                case OrchestrationOperationCode.Loop:
                case OrchestrationOperationCode.Parallel:
                case OrchestrationOperationCode.Sequence:
                case OrchestrationOperationCode.Selector:
                case OrchestrationOperationCode.Succeed:
                case OrchestrationOperationCode.Branch:
                case OrchestrationOperationCode.ConditionResult:
                case OrchestrationOperationCode.Compare:
                case OrchestrationOperationCode.And:
                case OrchestrationOperationCode.Or:
                case OrchestrationOperationCode.Not:
                case OrchestrationOperationCode.Constant:
                case OrchestrationOperationCode.Calculate:
                case OrchestrationOperationCode.SelectValue:
                case OrchestrationOperationCode.IdentityValue:
                case OrchestrationOperationCode.EmitOutput:
                case OrchestrationOperationCode.BlackboardSet:
                case OrchestrationOperationCode.TimelineEnter:
                case OrchestrationOperationCode.TimelineClipExitRequest:
                case OrchestrationOperationCode.TimelineSectionContinueRequest:
                    break;
                case OrchestrationOperationCode.SubGraph:
                case OrchestrationOperationCode.Cycle:
                    if (readsOutput)
                        Add(reads, new OrchestrationStaticRead(index, OrchestrationStaticReadSource.RuntimeData,
                            operation.Code.ToString(), false, operation.Code.ToString()));
                    break;
                case OrchestrationOperationCode.TimelineSection:
                    Add(reads, new OrchestrationStaticRead(index, OrchestrationStaticReadSource.TimelineSection,
                        $"TimelineSection:{operation.Text0}", false, $"Timeline 当前或已完成分段：{operation.Text0}"));
                    break;
                case OrchestrationOperationCode.StateRootCompleted:
                    Add(reads, new OrchestrationStaticRead(index, OrchestrationStaticReadSource.StateCompletion,
                        "StateRootCompleted", false, "状态主体自然完成"));
                    break;
                case OrchestrationOperationCode.InputBoolean:
                case OrchestrationOperationCode.InputScalar:
                case OrchestrationOperationCode.InputVector2:
                case OrchestrationOperationCode.InputVector2Magnitude:
                case OrchestrationOperationCode.InputRequest:
                case OrchestrationOperationCode.HostInput:
                    AddHostInputs(index, operation, reads);
                    break;
                case OrchestrationOperationCode.Extension:
                {
                    StateKeys declaration = Declare(operation);
                    if (readsOutput || operation.Extension.Value.Kind != OrchestrationExtensionKind.Action)
                        CollectReads(declaration.Predicate ?? UnknownPredicate(index), reads);
                    break;
                }
                default:
                {
                    if (operation.HostInputs.Count != 0)
                    {
                        AddHostInputs(index, operation, reads);
                        break;
                    }
                    string label = operation.Text0.Length != 0 ? $"{operation.Code}:{operation.Text0}" : operation.Code.ToString();
                    Add(reads, new OrchestrationStaticRead(index, OrchestrationStaticReadSource.RuntimeData, label, false, label));
                    break;
                }
            }
        }

        void AddHostInputs(int index, SemanticOperation operation, List<OrchestrationStaticRead> reads)
        {
            if (operation.HostInputs.Count == 0)
            {
                Add(reads, new OrchestrationStaticRead(index, OrchestrationStaticReadSource.HostInput,
                    operation.Text0, false, operation.Text0));
                return;
            }
            foreach (SemanticHostInputPort input in operation.HostInputs)
                Add(reads, new OrchestrationStaticRead(index, OrchestrationStaticReadSource.HostInput,
                    input.InputId, false, input.InputId));
        }

        static void Add(List<OrchestrationStaticRead> reads, in OrchestrationStaticRead read)
        {
            if (!reads.Contains(read))
                reads.Add(read);
        }

        sealed class StateKeys
        {
            internal bool Declared;
            internal OrchestrationStaticCondition Predicate;
            internal readonly List<string> Provided = new();
            internal readonly List<string> Read = new();
        }

        StateKeys Declare(SemanticOperation operation)
        {
            int index = operation.Handle.Value;
            if (m_StateKeys[index] != null)
                return m_StateKeys[index];
            var keys = new StateKeys();
            m_StateKeys[index] = keys;
            OrchestrationExtensionReference extension = operation.Extension.Value;
            var fields = new Dictionary<string, SemanticLiteral>(StringComparer.Ordinal);
            foreach (SemanticPayloadField field in operation.PayloadFields)
                fields.Add(field.Name, m_Input.Literals[field.LiteralIndex]);
            foreach (IOrchestrationStateKeyDeclarations declarations in m_Input.Declarations)
            {
                if (declarations.TryDeclare(in extension, fields, keys.Provided, keys.Read))
                {
                    keys.Declared = true;
                    if (declarations is IOrchestrationStatePredicateDeclarations predicates &&
                        predicates.TryDescribePredicate(in extension, fields, index, out OrchestrationStaticCondition predicate))
                        keys.Predicate = predicate;
                    break;
                }
                keys.Provided.Clear();
                keys.Read.Clear();
            }
            return keys;
        }
    }
}
