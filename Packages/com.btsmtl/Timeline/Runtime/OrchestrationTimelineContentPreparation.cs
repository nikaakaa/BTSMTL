using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public readonly struct OrchestrationTimelineInvocationMetadata
    {
        internal OrchestrationTimelineInvocationMetadata(int ordinal, bool marker,
            OrchestrationTimelineHook hook, OrchestrationExecutionEntry entry,
            int executionFrameOwnerOperationIndex, int graphOwnerOperationIndex,
            int callerOperationIndex, int graphIdentityId, int sourceIdentityId)
        {
            Ordinal = ordinal;
            Marker = marker;
            Hook = hook;
            Entry = entry;
            ExecutionFrameOwnerOperationIndex = executionFrameOwnerOperationIndex;
            GraphOwnerOperationIndex = graphOwnerOperationIndex;
            CallerOperationIndex = callerOperationIndex;
            GraphIdentityId = graphIdentityId;
            SourceIdentityId = sourceIdentityId;
        }

        public int Ordinal { get; }
        public bool Marker { get; }
        public OrchestrationTimelineHook Hook { get; }
        public OrchestrationExecutionEntry Entry { get; }
        public int ExecutionFrameOwnerOperationIndex { get; }
        public int GraphOwnerOperationIndex { get; }
        public int CallerOperationIndex { get; }
        public int GraphIdentityId { get; }
        public int SourceIdentityId { get; }
    }

    public sealed class OrchestrationTimelinePreparedEntries
    {
        internal OrchestrationTimelinePreparedEntries(
            OrchestrationTimelineEntryBinding[] clips, OrchestrationExecutionEntry[] markers,
            OrchestrationTimelineInvocationMetadata[] metadata)
        {
            ClipEntries = clips;
            MarkerEntries = markers;
            Invocations = metadata;
        }

        public ReadOnlyMemory<OrchestrationTimelineEntryBinding> ClipEntries { get; }
        public ReadOnlyMemory<OrchestrationExecutionEntry> MarkerEntries { get; }
        public ReadOnlyMemory<OrchestrationTimelineInvocationMetadata> Invocations { get; }

        public OrchestrationTimelineInvocationFacts CreatePresentationInvocation(
            in TimelineRuntimePresentationFrame frame, in TimelineRuntimeTreeClipRequest request)
        {
            OrchestrationTimelineHook hook = request.EventKind switch
            {
                TimelineRuntimeTreeClipEventKind.Enter => OrchestrationTimelineHook.Enable,
                TimelineRuntimeTreeClipEventKind.Update => OrchestrationTimelineHook.Root,
                TimelineRuntimeTreeClipEventKind.Exit => OrchestrationTimelineHook.Disable,
                TimelineRuntimeTreeClipEventKind.Destroy => OrchestrationTimelineHook.Destroy,
                _ => throw new ArgumentOutOfRangeException(nameof(request))
            };
            var invocation = new OrchestrationTimelineInvocation<OrchestrationTime>(request.Token,
                ClipEntries.Span[request.Token.ClipOrdinal].Entry(hook), hook, frame.LogicTick,
                frame.Start.SourceIdentityId, request.Time, request.PreviousTime, request.WindowStartTime,
                OrchestrationTime.Zero, OrchestrationTime.One);
            return new OrchestrationTimelineInvocationFacts(in invocation, in frame.Start,
                request.PreviousTime, request.Token.Cycle, frame.Time, frame.Cycle);
        }

        public OrchestrationTimelineInvocationFacts CreatePresentationInvocation(
            in TimelineRuntimePresentationFrame frame, in TimelineRuntimePresentationEvent marker)
        {
            var invocation = new OrchestrationTimelineInvocation<OrchestrationTime>(marker.Token,
                MarkerEntries.Span[marker.Token.ClipOrdinal], OrchestrationTimelineHook.Enable, frame.LogicTick,
                marker.Start.SourceIdentityId, marker.Time, marker.Time, marker.Time,
                OrchestrationTime.Zero, OrchestrationTime.One);
            return new OrchestrationTimelineInvocationFacts(in invocation, in marker.Start,
                marker.Time, marker.Token.Cycle, frame.Time, frame.Cycle);
        }
    }

    public enum OrchestrationTimelineCapacityIssueKind : byte
    {
        DecisionExitLifetimeUnresolved = 1,
        FrameBoundaryOutsideCycle = 2,
        LoopDurationNotPositive = 3,
        SectionJumpInLoop = 4,
        SectionContinueOutsideDoor = 5
    }

    public readonly struct OrchestrationTimelineCapacityIssue
    {
        internal OrchestrationTimelineCapacityIssue(int clipOrdinal,
            OrchestrationTimelineCapacityIssueKind kind, int operationIndex)
        {
            ClipOrdinal = clipOrdinal;
            Kind = kind;
            OperationIndex = operationIndex;
        }

        public int ClipOrdinal { get; }
        public OrchestrationTimelineCapacityIssueKind Kind { get; }
        public int OperationIndex { get; }
    }

    public static class OrchestrationTimelineContentPreparation
    {
        public static OrchestrationTimelinePreparedEntries PrepareEntries<TValue>(
            TimelineRuntimePreparedContent prepared, OrchestrationGraphContent<TValue> graph,
            OrchestrationProgram<TValue> program, ReadOnlySpan<int> localToShared,
            int operationIndex) where TValue : unmanaged
        {
            var sources = new Dictionary<(string Clip, string Graph, string Hook), OrchestrationGraphSource>();
            foreach (OrchestrationGraphSource source in graph.Sources.Span)
            {
                OrchestrationGraphExecutionSource invocation = source.Source;
                if (invocation.TargetKind == OrchestrationGraphSourceTargetKind.GraphInvocation &&
                    invocation.InvocationCallerOperationIndex == operationIndex)
                    sources.Add((invocation.InvocationCallerClipId, invocation.GraphId,
                        invocation.InvocationCallerId), source);
            }
            TimelineContentUnit content = prepared.Content;
            var clips = new OrchestrationTimelineEntryBinding[content.Clips.Count];
            var markers = new OrchestrationExecutionEntry[content.Markers.Count];
            var metadata = new List<OrchestrationTimelineInvocationMetadata>();
            for (int domainIndex = 0; domainIndex < 2; domainIndex++)
            {
                TimelineRuntimeDomainIndex domain = domainIndex == 0 ? prepared.Index.Logic : prepared.Index.Presentation;
                foreach (TimelineRuntimeIndexedClip indexed in domain.Trees)
                {
                    int ordinal = indexed.ContentIndex;
                    TimelineContentClip clip = content.Clips[ordinal];
                    string graphId = TimelineGraphBinding.GraphAuthoringId(clip.GraphBinding.GraphId);
                    OrchestrationExecutionEntry enable = BindEntry(ordinal, false, OrchestrationTimelineHook.Enable,
                        sources[(clip.AuthoringId, graphId, "OnEnable")], graph, program, localToShared, metadata);
                    OrchestrationExecutionEntry root = BindEntry(ordinal, false, OrchestrationTimelineHook.Root,
                        sources[(clip.AuthoringId, graphId, "Root")], graph, program, localToShared, metadata);
                    OrchestrationExecutionEntry disable = BindEntry(ordinal, false, OrchestrationTimelineHook.Disable,
                        sources[(clip.AuthoringId, graphId, "OnDisable")], graph, program, localToShared, metadata);
                    OrchestrationExecutionEntry destroy = BindEntry(ordinal, false, OrchestrationTimelineHook.Destroy,
                        sources[(clip.AuthoringId, graphId, "OnDestroy")], graph, program, localToShared, metadata);
                    clips[ordinal] = new OrchestrationTimelineEntryBinding(enable, root, disable, destroy);
                }
                for (int markerIndex = 0; markerIndex < domain.Markers.Count; markerIndex++)
                {
                    int ordinal = domain.Markers.ContentIndexAt(markerIndex);
                    TimelineContentMarker marker = content.Markers[ordinal];
                    markers[ordinal] = BindEntry(ordinal, true, OrchestrationTimelineHook.Enable,
                        sources[(marker.MarkerId, TimelineGraphBinding.GraphAuthoringId(marker.GraphId), "OnEnable")], graph, program, localToShared, metadata);
                }
            }
            return new OrchestrationTimelinePreparedEntries(clips, markers, metadata.ToArray());
        }

        public static bool TryCalculateCapacity<TValue, TScalar>(int programIndex, int operationIndex,
            OrchestrationProgram<TValue> program, TimelineRuntimePreparedContent prepared,
            TimelineExecutionDomain domain,
            OrchestrationTimelinePreparedEntries entries,
            ReadOnlyMemory<OrchestrationExtensionReference> extensions,
            IOrchestrationScalarValueAccess<TValue, TScalar> values, IScalarMath<TScalar> math,
            IOrchestrationTimelineTimeProjection<OrchestrationTime, TScalar> projection, int tickRate,
            out OrchestrationTimelinePreparedContent<OrchestrationTime> content,
            out ReadOnlyMemory<OrchestrationTimelineCapacityIssue> unresolved)
            where TValue : unmanaged
            where TScalar : unmanaged
        {
            TimelineRuntimeSchedulingContent scheduling = prepared.Scheduling(domain);
            OrchestrationTimelinePlaybackMode mode =
                (OrchestrationTimelinePlaybackMode)program.Operation(operationIndex).Integer0;
            if (mode == OrchestrationTimelinePlaybackMode.Loop && prepared.Content.Duration <= OrchestrationTime.Zero)
            {
                content = null;
                unresolved = new[] { new OrchestrationTimelineCapacityIssue(-1,
                    OrchestrationTimelineCapacityIssueKind.LoopDurationNotPositive, operationIndex) };
                return false;
            }
            var issues = new List<OrchestrationTimelineCapacityIssue>();
            bool sectionJumps = ValidateSectionJumps(program, scheduling, entries, mode, operationIndex, issues);
            if (issues.Count != 0)
            {
                content = null;
                unresolved = issues.ToArray();
                return false;
            }
            var occurrences = new List<OrchestrationOccurrenceCapacity>();
            var clipInstances = new int[scheduling.Clips.Length];
            var exits = new OrchestrationTimelineExitAnalysis<TValue, TScalar>(program, extensions, values, math, projection);
            int activeTrees = 0;
            int decisions = 0;
            int decisionInstances = 0;
            int traversals = mode == OrchestrationTimelinePlaybackMode.Loop
                ? OrchestrationTimelineLimits.MaximumCycleAdvance + 1 : sectionJumps ? 2 : 1;
            for (int ordinal = 0; ordinal < scheduling.Clips.Length; ordinal++)
            {
                ref readonly TimelineSchedulingClip<OrchestrationTime> clip = ref scheduling.Clips.Span[ordinal];
                if (!clip.Enabled || !clip.Tree || prepared.Content.Duration == OrchestrationTime.Zero)
                    continue;
                int instances = 1;
                if (mode == OrchestrationTimelinePlaybackMode.Loop)
                {
                    if (clip.TreeDecision)
                    {
                        OrchestrationExecutionEntry root = entries.ClipEntries.Span[ordinal].Root;
                        if (!exits.TryProve(in root, in clip, out OrchestrationTime exitTime, out int blocked))
                        {
                            issues.Add(new OrchestrationTimelineCapacityIssue(ordinal,
                                OrchestrationTimelineCapacityIssueKind.DecisionExitLifetimeUnresolved, blocked));
                            continue;
                        }
                        long lifetimeCycles = (exitTime.RawValue - clip.Start.RawValue) / prepared.Content.Duration.RawValue;
                        // 退出在下一步消费，本次跨过的新周期仍可同时占槽。
                        instances = checked((int)lifetimeCycles + 1 + traversals);
                    }
                    else if (clip.End > prepared.Content.Duration)
                        issues.Add(new OrchestrationTimelineCapacityIssue(ordinal,
                            OrchestrationTimelineCapacityIssueKind.FrameBoundaryOutsideCycle, operationIndex));
                }
                activeTrees = checked(activeTrees + instances);
                clipInstances[ordinal] = instances;
                if (clip.TreeDecision)
                {
                    decisions++;
                    decisionInstances = checked(decisionInstances + instances);
                }
                occurrences.Add(new OrchestrationOccurrenceCapacity(
                    program.Operation(entries.ClipEntries.Span[ordinal].Root.OperationIndex).ExecutionFrameOwnerOperationIndex,
                    instances));
            }
            unresolved = issues.ToArray();
            if (issues.Count != 0)
            {
                content = null;
                return false;
            }
            foreach (ref readonly TimelineSchedulingMarker<OrchestrationTime> marker in scheduling.Markers.Span)
                occurrences.Add(new OrchestrationOccurrenceCapacity(
                    program.Operation(entries.MarkerEntries.Span[marker.Ordinal].OperationIndex).ExecutionFrameOwnerOperationIndex, 1));
            var call = new OrchestrationTimelineCallCapacity(programIndex, operationIndex,
                prepared.Content.Duration == OrchestrationTime.Zero
                    ? ReadOnlyMemory<OrchestrationOccurrenceCapacity>.Empty : occurrences.ToArray());
            int trees = 0;
            foreach (ref readonly TimelineSchedulingClip<OrchestrationTime> clip in scheduling.Clips.Span)
                if (clip.Enabled && clip.Tree)
                    trees++;
            int sampledClips = 0;
            foreach (TimelineContentClip clip in prepared.Content.Clips)
                if (!clip.TrackMuted && clip.ExecutionPolicy.Domain == domain && clip.ContentKind == TimelineContentKind.Sampled)
                    sampledClips++;
            var graphEntries = new List<OrchestrationTimelineEntryTickCapacity>();
            foreach (int ordinal in scheduling.TreeOrdinals.Span)
            {
                if (prepared.Content.Duration == OrchestrationTime.Zero)
                    continue;
                OrchestrationTimelineEntryBinding entry = entries.ClipEntries.Span[ordinal];
                ref readonly TimelineSchedulingClip<OrchestrationTime> clip = ref scheduling.Clips.Span[ordinal];
                int instances = clipInstances[ordinal];
                int retained = clip.TreeDecision ? instances : 0;
                graphEntries.Add(new OrchestrationTimelineEntryTickCapacity(entry.Enable.OperationIndex, traversals, 0));
                graphEntries.Add(new OrchestrationTimelineEntryTickCapacity(entry.Root.OperationIndex,
                    checked(traversals + retained), 0));
                graphEntries.Add(new OrchestrationTimelineEntryTickCapacity(entry.Disable.OperationIndex,
                    checked(traversals + retained), 0));
                graphEntries.Add(new OrchestrationTimelineEntryTickCapacity(entry.Destroy.OperationIndex,
                    mode == OrchestrationTimelinePlaybackMode.Loop ? 0 : instances, instances));
            }
            foreach (ref readonly TimelineSchedulingMarker<OrchestrationTime> marker in scheduling.Markers.Span)
                graphEntries.Add(new OrchestrationTimelineEntryTickCapacity(
                    entries.MarkerEntries.Span[marker.Ordinal].OperationIndex, traversals, 0));
            var tickCapacity = new OrchestrationTimelineTickContentCapacity(programIndex, operationIndex,
                checked((scheduling.Markers.Length + 3 * trees) * traversals +
                    (mode == OrchestrationTimelinePlaybackMode.Loop ? 2 * decisionInstances : decisionInstances)), activeTrees,
                sampledClips, checked(sampledClips * traversals), graphEntries.ToArray());
            content = new OrchestrationTimelinePreparedContent<OrchestrationTime>(
                program.Operation(operationIndex).TimelineContentIdentityId, prepared.ContentRevisionId, prepared.Content.Duration,
                tickRate,
                mode, domain, scheduling.Clips, scheduling.TreeOrdinals,
                scheduling.Markers, scheduling.Sections,
                entries.ClipEntries, entries.MarkerEntries, activeTrees, decisionInstances,
                mode == OrchestrationTimelinePlaybackMode.Loop ? checked(decisionInstances + decisions) : decisions,
                call, in tickCapacity);
            return true;
        }

        // 分段跳转只允许线性推进：循环播放不得配置；"进入下一分段"只在可继续分段内、有声明终点的逻辑门 Clip 的入口与 Root 中。
        static bool ValidateSectionJumps<TValue>(OrchestrationProgram<TValue> program,
            TimelineRuntimeSchedulingContent scheduling, OrchestrationTimelinePreparedEntries entries,
            OrchestrationTimelinePlaybackMode mode, int operationIndex, List<OrchestrationTimelineCapacityIssue> issues)
            where TValue : unmanaged
        {
            ReadOnlySpan<TimelineSchedulingSection<OrchestrationTime>> sections = scheduling.Sections.Span;
            bool jumps = false;
            foreach (ref readonly TimelineSchedulingSection<OrchestrationTime> section in sections)
                jumps |= section.Continuable;
            if (jumps && mode == OrchestrationTimelinePlaybackMode.Loop)
                issues.Add(new OrchestrationTimelineCapacityIssue(-1,
                    OrchestrationTimelineCapacityIssueKind.SectionJumpInLoop, operationIndex));
            var visited = new bool[program.OperationCount];
            for (int ordinal = 0; ordinal < scheduling.Clips.Length; ordinal++)
            {
                ref readonly TimelineSchedulingClip<OrchestrationTime> clip = ref scheduling.Clips.Span[ordinal];
                if (!clip.Enabled || !clip.Tree)
                    continue;
                OrchestrationTimelineEntryBinding binding = entries.ClipEntries.Span[ordinal];
                bool open = Reaches(program, binding.Enable.OperationIndex, visited) |
                    Reaches(program, binding.Root.OperationIndex, visited);
                bool closing = Reaches(program, binding.Disable.OperationIndex, visited) |
                    Reaches(program, binding.Destroy.OperationIndex, visited);
                if (!open && !closing)
                    continue;
                int section = TimelineRuntimeScheduling<OrchestrationTime, OrchestrationTimeClock>.SectionAt(sections, clip.Start);
                if (closing || scheduling.Domain != TimelineExecutionDomain.Logic || !clip.DeclaredEnd ||
                    section < 0 || !sections[section].Continuable || clip.End > sections[section].End)
                    issues.Add(new OrchestrationTimelineCapacityIssue(ordinal,
                        OrchestrationTimelineCapacityIssueKind.SectionContinueOutsideDoor, operationIndex));
            }
            return jumps;
        }

        static bool Reaches<TValue>(OrchestrationProgram<TValue> program, int operationIndex, bool[] visited)
            where TValue : unmanaged
        {
            Array.Clear(visited, 0, visited.Length);
            var pending = new Stack<int>();
            pending.Push(operationIndex);
            while (pending.Count != 0)
            {
                int index = pending.Pop();
                if (visited[index])
                    continue;
                visited[index] = true;
                if (program.Operation(index).Code == OrchestrationOperationCode.TimelineSectionContinueRequest)
                    return true;
                foreach (int edge in program.Outgoing(index, OrchestrationFlowKind.Child))
                    pending.Push(program.Edge(edge).TargetOperationIndex);
                foreach (int edge in program.Outgoing(index, OrchestrationFlowKind.Enter))
                    pending.Push(program.Edge(edge).TargetOperationIndex);
            }
            return false;
        }

        static OrchestrationExecutionEntry BindEntry<TValue>(int ordinal, bool marker,
            OrchestrationTimelineHook hook, OrchestrationGraphSource source,
            OrchestrationGraphContent<TValue> graph, OrchestrationProgram<TValue> program,
            ReadOnlySpan<int> localToShared, List<OrchestrationTimelineInvocationMetadata> metadata)
            where TValue : unmanaged
        {
            OrchestrationGraphExecutionSource invocation = source.Source;
            OrchestrationExecutionEntry original = invocation.InvocationEntry;
            var entry = new OrchestrationExecutionEntry(original.OperationIndex, original.Contract,
                localToShared[original.InputPortId]);
            int graphIdentityId = localToShared[graph.Identities.Bind(invocation.GraphId)];
            int sourceIdentityId = localToShared[invocation.SourceIdentityId];
            metadata.Add(new OrchestrationTimelineInvocationMetadata(ordinal, marker, hook, entry,
                program.Operation(entry.OperationIndex).ExecutionFrameOwnerOperationIndex,
                invocation.TargetIndex, invocation.InvocationCallerOperationIndex,
                graphIdentityId, sourceIdentityId));
            return entry;
        }
    }
}
