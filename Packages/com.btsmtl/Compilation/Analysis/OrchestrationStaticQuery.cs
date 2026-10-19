using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using BTSMTL.Timeline;

namespace BTSMTL.Compilation.Analysis
{
    // provided 只声明随 Clip 生命周期存在的状态；未声明的读取由预览假设承接。
    public interface IOrchestrationStateKeyDeclarations
    {
        bool TryDeclare(in OrchestrationExtensionReference extension, IReadOnlyDictionary<string, SemanticLiteral> fields,
            ICollection<string> provided, ICollection<string> read);
    }

    public enum OrchestrationStaticReadSource : byte
    {
        ClipState = 1,
        HostInput = 2,
        RuntimeData = 3
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
            IReadOnlyList<OrchestrationStaticRead> reads)
        {
            OperationIndex = operationIndex;
            Hook = hook;
            Reads = reads;
        }

        public int OperationIndex { get; }
        public OrchestrationTimelineHook Hook { get; }
        public IReadOnlyList<OrchestrationStaticRead> Reads { get; }
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
            IReadOnlyList<OrchestrationStaticRead> reads)
        {
            Identity = identity;
            Source = source;
            TargetOperation = targetOperation;
            Priority = priority;
            Order = order;
            ControlFlowIndex = controlFlowIndex;
            Location = location;
            Reads = reads;
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
        public IReadOnlyList<OrchestrationStaticTransition> Transitions => m_Transitions;
        internal readonly List<OrchestrationStaticTransition> m_Transitions = new();
    }

    public sealed class OrchestrationStaticQueryInput
    {
        public OrchestrationStaticQueryInput(IReadOnlyList<SemanticOperation> operations,
            IReadOnlyList<SemanticLiteral> literals, IReadOnlyList<ProgramControlFlowEdge> controlFlow,
            IReadOnlyList<ProgramSourceMapEntry> sourceMap, Func<string, TimelineContentUnit> resolveTimeline,
            IReadOnlyList<IOrchestrationStateKeyDeclarations> declarations, IReadOnlyList<ProgramReference> references)
        {
            Operations = operations;
            Literals = literals;
            ControlFlow = controlFlow;
            SourceMap = sourceMap;
            ResolveTimeline = resolveTimeline;
            Declarations = declarations;
            References = references;
        }

        public IReadOnlyList<SemanticOperation> Operations { get; }
        public IReadOnlyList<SemanticLiteral> Literals { get; }
        public IReadOnlyList<ProgramControlFlowEdge> ControlFlow { get; }
        public IReadOnlyList<ProgramSourceMapEntry> SourceMap { get; }
        public Func<string, TimelineContentUnit> ResolveTimeline { get; }
        public IReadOnlyList<IOrchestrationStateKeyDeclarations> Declarations { get; }
        public IReadOnlyList<ProgramReference> References { get; }
    }

    // 只读静态查询：只读取正式编译的语义程序与图结构，不执行任何图，不识别业务节点类型。
    public sealed class OrchestrationStaticQuery
    {
        readonly OrchestrationStaticQueryInput m_Input;
        readonly List<int>[] m_Outgoing;
        readonly List<int>[] m_ValueInputs;
        readonly List<int>[] m_Incoming;
        readonly OrchestrationStaticSourceLocation[] m_Locations;
        readonly OrchestrationStaticSourceLocation[] m_EdgeLocations;
        readonly StateKeys[] m_StateKeys;
        readonly Dictionary<int, OrchestrationStaticState> m_States = new();
        readonly Dictionary<int, int> m_MachineEntries = new();
        readonly Dictionary<int, List<OrchestrationStaticTransition>> m_InitialTransitions = new();
        readonly Dictionary<string, OrchestrationStaticTimeline> m_Timelines = new(StringComparer.Ordinal);
        readonly List<string> m_Errors = new();

        OrchestrationStaticQuery(OrchestrationStaticQueryInput input)
        {
            m_Input = input;
            int count = input.Operations.Count;
            m_Outgoing = new List<int>[count];
            m_ValueInputs = new List<int>[count];
            m_Incoming = new List<int>[count];
            m_Locations = new OrchestrationStaticSourceLocation[count];
            m_EdgeLocations = new OrchestrationStaticSourceLocation[input.ControlFlow.Count];
            m_StateKeys = new StateKeys[count];
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
                    m_Incoming[edge.Target.Value].Add(i);
            }
            for (int i = 0; i < count; i++)
                m_Outgoing[i].Sort(CompareEdges);
            foreach (ProgramSourceMapEntry entry in input.SourceMap)
            {
                ProgramExecutionSource source = entry.Source;
                if (source.TargetKind == ProgramSourceTargetKind.Operation)
                    m_Locations[source.TargetIndex] = new OrchestrationStaticSourceLocation(source.GraphId, source.NodeId,
                        source.EdgeId, entry.DisplayPath);
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
                int timeline = FindBodyTimeline(state.OperationIndex);
                if (timeline < 0)
                    continue;
                state.TimelineIdentity = Operations[timeline].Text0;
                state.PlaybackMode = (OrchestrationTimelinePlaybackMode)Operations[timeline].Integer0;
                state.Timeline = RequireTimeline(state.TimelineIdentity);
            }
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
                        var reads = new List<OrchestrationStaticRead>();
                        CollectPathReads(member, members, reads);
                        clip.RequestsSectionContinue = true;
                        clip.m_ContinueRequests.Add(new OrchestrationStaticContinueRequest(member, hook, reads));
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

        void CollectPathReads(int target, HashSet<int> members, List<OrchestrationStaticRead> reads)
        {
            var visited = new HashSet<int>();
            var pending = new Stack<int>();
            pending.Push(target);
            CollectValueReads(target, reads, new HashSet<int>());
            while (pending.Count != 0)
            {
                int index = pending.Pop();
                if (!visited.Add(index))
                    continue;
                foreach (int edgeIndex in m_Incoming[index])
                {
                    ProgramControlFlowEdge edge = m_Input.ControlFlow[edgeIndex];
                    int source = edge.Source.Value;
                    if (!members.Contains(source) || edge.Kind is not (ProgramControlFlowKind.Child or ProgramControlFlowKind.Enter))
                        continue;
                    if (edge.HasCondition)
                        CollectConditionReads(edge.Condition.Value, reads);
                    CollectValueReads(source, reads, new HashSet<int>());
                    if (Operations[source].Code is OrchestrationOperationCode.Sequence or OrchestrationOperationCode.Selector)
                        foreach (int preceding in m_Outgoing[source])
                        {
                            if (preceding == edgeIndex)
                                break;
                            ProgramControlFlowEdge prerequisite = m_Input.ControlFlow[preceding];
                            if (prerequisite.Kind != ProgramControlFlowKind.Child)
                                continue;
                            if (prerequisite.HasCondition)
                                CollectConditionReads(prerequisite.Condition.Value, reads);
                            CollectConditionReads(prerequisite.Target.Value, reads);
                        }
                    pending.Push(source);
                }
            }
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
                if (edge.HasCondition)
                    CollectConditionReads(edge.Condition.Value, reads);
                OrchestrationStaticSourceLocation edgeLocation = m_EdgeLocations[edgeIndex];
                if (initial)
                {
                    var transition = new OrchestrationStaticTransition(edge.Identity, null, edge.Target.Value,
                        edge.Priority, edge.Order, edgeIndex, edgeLocation, reads) { Target = State(edge.Target.Value) };
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
                        edge.Priority, edge.Order, edgeIndex, edgeLocation, reads) { FromAnyState = any };
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
            int result = left.Kind == ProgramControlFlowKind.Transition && right.Kind == left.Kind
                ? left.Priority.CompareTo(right.Priority) : 0;
            if (result == 0)
                result = left.Order.CompareTo(right.Order);
            if (result == 0)
                result = string.CompareOrdinal(left.SourcePort, right.SourcePort);
            return result == 0 ? string.CompareOrdinal(left.Identity, right.Identity) : result;
        }

        int CompareTransitions(OrchestrationStaticTransition left, OrchestrationStaticTransition right)
        {
            int result = right.FromAnyState.CompareTo(left.FromAnyState);
            return result == 0 ? CompareEdges(left.ControlFlowIndex, right.ControlFlowIndex) : result;
        }

        void CollectConditionReads(int condition, List<OrchestrationStaticRead> reads)
        {
            var visited = new HashSet<int>();
            var pending = new Stack<int>();
            pending.Push(condition);
            var values = new HashSet<int>();
            while (pending.Count != 0)
            {
                int index = pending.Pop();
                if (!visited.Add(index))
                    continue;
                CollectValueReads(index, reads, values, true);
                if (Operations[index].Code == OrchestrationOperationCode.Succeed)
                    continue;
                foreach (int edgeIndex in m_Outgoing[index])
                {
                    ProgramControlFlowEdge edge = m_Input.ControlFlow[edgeIndex];
                    if (edge.Kind != ProgramControlFlowKind.Child &&
                        !(Operations[index].Code == OrchestrationOperationCode.SubGraph && edge.Kind == ProgramControlFlowKind.Enter))
                        continue;
                    if (edge.HasCondition)
                        pending.Push(edge.Condition.Value);
                    pending.Push(edge.Target.Value);
                }
            }
        }

        void CollectValueReads(int index, List<OrchestrationStaticRead> reads, HashSet<int> visited, bool readsOutput = false)
        {
            if (!visited.Add(index))
                return;
            SemanticOperation operation = Operations[index];
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
                    if (operation.HostInputs.Count != 0)
                        AddHostInputs(index, operation, reads);
                    StateKeys declaration = Declare(operation);
                    OrchestrationExtensionIdentity identity = operation.Extension.Value.Identity;
                    string label = $"{identity.DomainId}/{identity.OperationId}";
                    if (!declaration.Declared)
                    {
                        if (readsOutput || operation.Extension.Value.Kind != OrchestrationExtensionKind.Action)
                            Add(reads, new OrchestrationStaticRead(index, OrchestrationStaticReadSource.RuntimeData,
                                label, true, label));
                        break;
                    }
                    foreach (string key in declaration.Read)
                        Add(reads, new OrchestrationStaticRead(index, OrchestrationStaticReadSource.ClipState, key, false, key));
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
                    break;
                }
                keys.Provided.Clear();
                keys.Read.Clear();
            }
            return keys;
        }
    }
}
