using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;

namespace BTSMTL.Diagnostics
{
    public delegate DebugValueSnapshot RuntimeOrchestrationValueCapture<TValue>(in TValue value) where TValue : unmanaged;

    // 把编排执行 Trace 转为图、状态机与黑板诊断。来源、调用路径和事件种类在准备阶段解析，转发时只查表。
    public sealed class RuntimeOrchestrationTraceForwarder<TValue> where TValue : unmanaged
    {
        static readonly string[] s_WaitReasons = Enum.GetNames(typeof(OrchestrationWaitReason));
        static readonly string[] s_Results = { string.Empty, "Running", "Success", "Failure" };

        readonly OrchestrationProgram<TValue> m_Program;
        readonly RuntimeDiagnosticsContext m_Diagnostics;
        readonly RuntimeOrchestrationValueCapture<TValue> m_Capture;
        readonly string m_ProgramId;
        readonly RuntimeSourceElementHandle[] m_Operations;
        readonly string[] m_OperationNodeIds;
        readonly RuntimeSourceElementHandle[] m_Edges;
        readonly Dictionary<int, RuntimeSourceElementHandle> m_Blackboards = new();
        readonly string[] m_InvocationPaths;
        readonly string[] m_ParentInvocationPaths;

        public RuntimeOrchestrationTraceForwarder(OrchestrationProgram<TValue> program,
            ReadOnlySpan<OrchestrationGraphSource> sources, OrchestrationIdentityTable identities, string programId,
            RuntimeDiagnosticsContext diagnostics, RuntimeOrchestrationValueCapture<TValue> capture)
        {
            m_Program = program ?? throw new ArgumentNullException(nameof(program));
            m_Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            m_Capture = capture ?? throw new ArgumentNullException(nameof(capture));
            m_ProgramId = programId ?? string.Empty;
            m_Operations = new RuntimeSourceElementHandle[program.OperationCount];
            m_OperationNodeIds = new string[program.OperationCount];
            m_InvocationPaths = new string[sources.Length];
            m_ParentInvocationPaths = new string[sources.Length];
            var edgePaths = new Dictionary<string, RuntimeSourceElementHandle>(StringComparer.Ordinal);
            for (int index = 0; index < sources.Length; index++)
            {
                OrchestrationGraphExecutionSource source = sources[index].Source;
                m_InvocationPaths[index] = source.GraphInvocationPath ?? string.Empty;
                m_ParentInvocationPaths[index] = source.ParentInvocationPath ?? string.Empty;
                switch (source.TargetKind)
                {
                    case OrchestrationGraphSourceTargetKind.Operation:
                        if (source.TargetIndex < m_Operations.Length && !m_Operations[source.TargetIndex].IsValid)
                        {
                            m_Operations[source.TargetIndex] = diagnostics.ResolveSourceHandle(SourceKey(source));
                            m_OperationNodeIds[source.TargetIndex] = source.NodeId;
                        }
                        break;
                    case OrchestrationGraphSourceTargetKind.Reference when !string.IsNullOrEmpty(source.EdgeId):
                        // 控制流身份与来源显示路径同为作者路由；Value 连线不进入运行边表。
                        string path = string.Join("/", sources[index].DisplayPathSegments.ToArray());
                        if (!edgePaths.TryAdd(path, diagnostics.ResolveSourceHandle(SourceKey(source))))
                            throw new InvalidOperationException($"编排连线来源路径重复：{path}");
                        break;
                    case OrchestrationGraphSourceTargetKind.BlackboardValue when !string.IsNullOrEmpty(source.DeclarationId):
                        m_Blackboards.TryAdd(source.TargetIndex, diagnostics.ResolveSourceHandle(SourceKey(source)));
                        break;
                }
            }
            m_Edges = new RuntimeSourceElementHandle[program.EdgeCount];
            for (int edge = 0; edge < m_Edges.Length; edge++)
                if (edgePaths.TryGetValue(identities.Resolve(program.Edge(edge).IdentityId), out RuntimeSourceElementHandle handle))
                    m_Edges[edge] = handle;
        }

        // 注册图节点、连线、黑板声明与子图调用；宿主在封存来源表前调用。
        public static void AddSources(DebugSourceMap map, ReadOnlySpan<OrchestrationGraphSource> sources)
        {
            var graphByPath = new Dictionary<string, string>(StringComparer.Ordinal);
            var callers = new Dictionary<(string Path, string Kind, string Id), RuntimeSourceElementKey>();
            for (int index = 0; index < sources.Length; index++)
            {
                OrchestrationGraphExecutionSource source = sources[index].Source;
                if (!string.IsNullOrEmpty(source.NodeId))
                    callers.TryAdd((source.SourceInvocationPath, "Node", source.NodeId),
                        RuntimeSourceElementKey.Node(source.GraphId, source.NodeId));
                if (!string.IsNullOrEmpty(source.EdgeId))
                    callers.TryAdd((source.SourceInvocationPath, "Edge", source.EdgeId),
                        RuntimeSourceElementKey.Edge(source.GraphId, source.EdgeId));
                if (source.TargetKind != OrchestrationGraphSourceTargetKind.GraphInvocation)
                    continue;
                graphByPath.TryAdd(source.SourceInvocationPath, source.GraphId);
                graphByPath.TryAdd(source.GraphInvocationPath, source.GraphId);
            }
            var added = new HashSet<RuntimeSourceElementKey>();
            for (int index = 0; index < sources.Length; index++)
            {
                OrchestrationGraphSource entry = sources[index];
                OrchestrationGraphExecutionSource source = entry.Source;
                if (!Registered(source) || string.IsNullOrEmpty(source.GraphId))
                    continue;
                RuntimeSourceElementKey key = SourceKey(source);
                if (added.Add(key))
                {
                    ReadOnlySpan<string> segments = entry.DisplayPathSegments.Span;
                    map.Add(key, default, segments.Length == 0 ? source.GraphId : segments[segments.Length - 1],
                        string.Empty, RuntimeSourceTarget.Source);
                }
                if (source.TargetKind == OrchestrationGraphSourceTargetKind.GraphInvocation &&
                    !map.TryGetInvocation(source.GraphInvocationPath, out _))
                    map.AddGraphInvocation(new RuntimeGraphInvocation(source.GraphInvocationPath, source.GraphId,
                        source.ParentInvocationPath, Caller(source, graphByPath, callers),
                        source.InvocationCallerClipId, source.InvocationCallerId));
            }
        }

        static bool Registered(OrchestrationGraphExecutionSource source) => source.TargetKind is
            OrchestrationGraphSourceTargetKind.Operation or OrchestrationGraphSourceTargetKind.OperationPort or
            OrchestrationGraphSourceTargetKind.GraphInvocation or OrchestrationGraphSourceTargetKind.BlackboardValue or
            OrchestrationGraphSourceTargetKind.AuthorStateSlot ||
            source.TargetKind == OrchestrationGraphSourceTargetKind.Reference && !string.IsNullOrEmpty(source.EdgeId);

        static RuntimeSourceElementKey Caller(OrchestrationGraphExecutionSource invocation,
            Dictionary<string, string> graphByPath,
            Dictionary<(string Path, string Kind, string Id), RuntimeSourceElementKey> callers)
        {
            if (string.IsNullOrEmpty(invocation.ParentInvocationPath))
                return RuntimeSourceElementKey.Graph(invocation.GraphId);
            if (!graphByPath.TryGetValue(invocation.ParentInvocationPath, out string parentGraphId))
                throw new InvalidOperationException($"子图调用 '{invocation.GraphInvocationPath}' 缺少父图来源。");
            return invocation.InvocationCallerKind switch
            {
                "Node" => callers[(invocation.ParentInvocationPath, "Node", invocation.InvocationCallerId)],
                "Edge" => callers[(invocation.ParentInvocationPath, "Edge", invocation.InvocationCallerId)],
                "TimelineClip" or "PresentationMarker" or "PresentationTreeClip" =>
                    RuntimeSourceElementKey.Node(parentGraphId, TimelineCallerNode(invocation.SourceInvocationPath)),
                _ => RuntimeSourceElementKey.Graph(parentGraphId)
            };
        }

        static string TimelineCallerNode(string path)
        {
            int timeline = path.LastIndexOf("/timeline:", StringComparison.Ordinal);
            int node = timeline < 0 ? -1 : path.LastIndexOf("/node:", timeline, StringComparison.Ordinal);
            int start = node + "/node:".Length;
            if (node < 0 || start >= timeline)
                throw new InvalidOperationException($"Timeline 子图调用路径缺少作者节点：{path}");
            return path.Substring(start, timeline - start);
        }

        static RuntimeSourceElementKey SourceKey(OrchestrationGraphExecutionSource source)
        {
            if (source.TargetKind == OrchestrationGraphSourceTargetKind.GraphInvocation)
                return RuntimeSourceElementKey.Graph(source.GraphId);
            if (source.TargetKind == OrchestrationGraphSourceTargetKind.OperationPort &&
                !string.IsNullOrEmpty(source.NodeId) && !string.IsNullOrEmpty(source.PortId))
                return RuntimeSourceElementKey.Port(source.GraphId, source.NodeId, source.PortId);
            if (!string.IsNullOrEmpty(source.EdgeId))
                return RuntimeSourceElementKey.Edge(source.GraphId, source.EdgeId);
            if (!string.IsNullOrEmpty(source.DeclarationId))
                return RuntimeSourceElementKey.Declaration(source.GraphId, source.DeclarationId);
            if (!string.IsNullOrEmpty(source.NodeId))
                return RuntimeSourceElementKey.Node(source.GraphId, source.NodeId);
            return RuntimeSourceElementKey.Graph(source.GraphId);
        }

        // 事件与上下文为同一 batch 的切片；调用方已设置本帧逻辑 Tick。
        public void Publish(ReadOnlySpan<OrchestrationTraceEvent<TValue>> events,
            ReadOnlySpan<OrchestrationTraceContext> contexts)
        {
            for (int index = 0; index < events.Length; index++)
            {
                ref readonly OrchestrationTraceEvent<TValue> item = ref events[index];
                Classify(in item, out RuntimeTraceChannel channel, out RuntimeTraceEventKind kind);
                if (!m_Diagnostics.ShouldPublish(channel, kind))
                    continue;
                ref readonly OrchestrationTraceContext context = ref contexts[item.ContextIndex];
                string path = m_InvocationPaths[context.InvocationSourceIndex];
                var payload = new RuntimeTracePayload
                {
                    CallSiteId = path,
                    ParentInvocationPath = m_ParentInvocationPaths[context.InvocationSourceIndex],
                    ActionInstanceId = context.ActionInstanceId,
                    ActivationGeneration = item.Generation,
                    SkillExecutionGeneration = context.SkillExecutionGeneration,
                    GraphInvocationGeneration = context.GraphInvocationGeneration,
                    ParentInvocationGeneration = context.ParentInvocationGeneration,
                    Cycle = context.Cycle,
                    ClipIndex = context.ClipOrdinal,
                    Flag = item.Passed
                };
                RuntimeSourceElementHandle source = m_Operations[item.OperationIndex];
                switch (item.Kind)
                {
                    case OrchestrationTraceKind.Complete:
                        payload.Detail = s_Results[(int)item.Result];
                        break;
                    case OrchestrationTraceKind.Waiting:
                        payload.Detail = s_WaitReasons[(int)item.WaitReason];
                        break;
                    case OrchestrationTraceKind.Edge:
                        payload.OwnerId = m_OperationNodeIds[item.OperationIndex];
                        if (m_Edges[item.EdgeIndex].IsValid)
                            source = m_Edges[item.EdgeIndex];
                        payload.Status = item.Selected ? "Selected" : "Evaluated";
                        break;
                    case OrchestrationTraceKind.Blackboard:
                        if (m_Blackboards.TryGetValue(item.ValueSlot, out RuntimeSourceElementHandle declaration))
                            source = declaration;
                        payload.Value = m_Capture(in item.Value);
                        payload.BlackboardStateSlot = item.ValueSlot;
                        payload.BlackboardScope = (int)item.Blackboard.Owner.ScopeKind;
                        payload.BlackboardOwnerIndex = item.Blackboard.Owner.CompiledOwnerIndex;
                        payload.BlackboardOwnerGeneration = item.Blackboard.Owner.Generation;
                        payload.BlackboardLifetime = (int)item.Blackboard.Lifetime;
                        payload.BlackboardIsActive = true;
                        break;
                }
                RuntimeInstanceKey instance = RuntimeInstanceKey.SkillExecution(m_Diagnostics.CharacterRuntimeId,
                    m_Diagnostics.AbilityExecutionRuntimeId, m_ProgramId, context.ActionInstanceId, path,
                    context.SkillExecutionGeneration, context.GraphInvocationGeneration);
                m_Diagnostics.Publish(channel, RuntimeTraceDomain.Logic, kind, source, instance, in payload);
            }
        }

        void Classify(in OrchestrationTraceEvent<TValue> item, out RuntimeTraceChannel channel,
            out RuntimeTraceEventKind kind)
        {
            bool state = item.Kind != OrchestrationTraceKind.Edge && item.Kind != OrchestrationTraceKind.Blackboard &&
                m_Program.Operation(item.OperationIndex).Code == OrchestrationOperationCode.State;
            switch (item.Kind)
            {
                case OrchestrationTraceKind.Edge:
                    bool transition = m_Program.Edge(item.EdgeIndex).Kind == OrchestrationFlowKind.Transition;
                    channel = transition ? RuntimeTraceChannel.StateMachine : RuntimeTraceChannel.Graph;
                    kind = transition
                        ? item.Selected ? RuntimeTraceEventKind.StateTransitionSelected : RuntimeTraceEventKind.StateTransitionEvaluated
                        : item.Selected ? RuntimeTraceEventKind.EdgeSelected : RuntimeTraceEventKind.EdgeEvaluated;
                    return;
                case OrchestrationTraceKind.Blackboard:
                    channel = RuntimeTraceChannel.Blackboard;
                    kind = RuntimeTraceEventKind.BlackboardWritten;
                    return;
            }
            channel = state ? RuntimeTraceChannel.StateMachine : RuntimeTraceChannel.Graph;
            kind = item.Kind switch
            {
                OrchestrationTraceKind.Enter => state ? RuntimeTraceEventKind.StateScopeEntered : RuntimeTraceEventKind.NodeEntered,
                OrchestrationTraceKind.Running => RuntimeTraceEventKind.NodeRunning,
                OrchestrationTraceKind.Complete => state ? RuntimeTraceEventKind.StateScopeExited : RuntimeTraceEventKind.NodeCompleted,
                OrchestrationTraceKind.StopRequested => RuntimeTraceEventKind.NodeStopRequested,
                OrchestrationTraceKind.Stopped => state ? RuntimeTraceEventKind.StateScopeExited : RuntimeTraceEventKind.NodeStopped,
                OrchestrationTraceKind.ForceStopped => state ? RuntimeTraceEventKind.StateScopeExited : RuntimeTraceEventKind.NodeForceStopped,
                OrchestrationTraceKind.Waiting => RuntimeTraceEventKind.NodeWaiting,
                OrchestrationTraceKind.LoopIterationEnter => RuntimeTraceEventKind.LoopIterationEntered,
                OrchestrationTraceKind.LoopIterationComplete => RuntimeTraceEventKind.LoopIterationCompleted,
                _ => throw new InvalidOperationException($"编排 Trace 种类 '{item.Kind}' 没有诊断事件。")
            };
        }
    }
}
