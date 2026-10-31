using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public readonly struct OrchestrationTimelineInvocationMetadata
    {
        internal OrchestrationTimelineInvocationMetadata(int programIndex, int ordinal, bool marker,
            OrchestrationTimelineHook hook, OrchestrationExecutionEntry entry,
            int executionFrameOwnerOperationIndex, int graphOwnerOperationIndex,
            int? callerOperationIndex, int graphIdentityId, int sourceIdentityId)
        {
            ProgramIndex = programIndex;
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

        public int ProgramIndex { get; }
        public int Ordinal { get; }
        public bool Marker { get; }
        public OrchestrationTimelineHook Hook { get; }
        public OrchestrationExecutionEntry Entry { get; }
        public int ExecutionFrameOwnerOperationIndex { get; }
        public int GraphOwnerOperationIndex { get; }
        public int? CallerOperationIndex { get; }
        public int GraphIdentityId { get; }
        public int SourceIdentityId { get; }
    }

    public sealed class OrchestrationTimelinePreparedEntries
    {
        internal OrchestrationTimelinePreparedEntries(
            OrchestrationTimelineEntryBinding[] clips, OrchestrationExecutionEntry[] markers,
            int[] clipPrograms, int[] markerPrograms,
            OrchestrationTimelineInvocationMetadata[] metadata)
        {
            ClipEntries = clips;
            MarkerEntries = markers;
            ClipPrograms = clipPrograms;
            MarkerPrograms = markerPrograms;
            Invocations = metadata;
        }

        public ReadOnlyMemory<OrchestrationTimelineEntryBinding> ClipEntries { get; }
        public ReadOnlyMemory<OrchestrationExecutionEntry> MarkerEntries { get; }
        public ReadOnlyMemory<int> ClipPrograms { get; }
        public ReadOnlyMemory<int> MarkerPrograms { get; }
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

    public readonly struct OrchestrationTimelineGraphEntryBinding
    {
        internal OrchestrationTimelineGraphEntryBinding(OrchestrationExecutionEntry entry,
            int executionFrameOwnerOperationIndex, int graphOwnerOperationIndex, int sourceIdentityId)
        {
            Entry = entry;
            ExecutionFrameOwnerOperationIndex = executionFrameOwnerOperationIndex;
            GraphOwnerOperationIndex = graphOwnerOperationIndex;
            SourceIdentityId = sourceIdentityId;
        }

        public OrchestrationExecutionEntry Entry { get; }
        public int ExecutionFrameOwnerOperationIndex { get; }
        public int GraphOwnerOperationIndex { get; }
        public int SourceIdentityId { get; }
    }

    public readonly struct OrchestrationTimelineGraphBinding
    {
        readonly string m_GraphId;
        readonly Dictionary<OrchestrationTimelineHook, OrchestrationTimelineGraphEntryBinding> m_Entries;

        OrchestrationTimelineGraphBinding(int programIndex, string graphId, int graphIdentityId,
            Dictionary<OrchestrationTimelineHook, OrchestrationTimelineGraphEntryBinding> entries)
        {
            ProgramIndex = programIndex;
            m_GraphId = graphId;
            GraphIdentityId = graphIdentityId;
            m_Entries = entries;
        }

        public int ProgramIndex { get; }
        public int GraphIdentityId { get; }

        public OrchestrationTimelineGraphEntryBinding Entry(OrchestrationTimelineHook hook)
        {
            if (m_Entries.TryGetValue(hook, out OrchestrationTimelineGraphEntryBinding entry))
                return entry;
            throw new InvalidOperationException($"Timeline 图 '{m_GraphId}' 缺少 '{hook}' 生命周期入口。");
        }

        public static OrchestrationTimelineGraphBinding Prepare<TValue, TNumerics>(
            int programIndex, PreparedOrchestrationGraph<TValue, TNumerics> graph)
            where TValue : unmanaged
            where TNumerics : struct, IOrchestrationNumerics<TValue>
        {
            string graphId = graph.Source.Identity.Source.GraphId;
            var sources = new Dictionary<string, OrchestrationGraphExecutionSource>(StringComparer.Ordinal);
            foreach (OrchestrationGraphSource source in graph.Sources.Span)
            {
                OrchestrationGraphExecutionSource invocation = source.Source;
                if (invocation.TargetKind == OrchestrationGraphSourceTargetKind.GraphInvocation &&
                    invocation.GraphId == graphId && invocation.InvocationCallerKind == "None")
                    sources.Add(graphId + "/entry:" + invocation.InvocationCallerId, invocation);
            }
            var entries = new Dictionary<OrchestrationTimelineHook, OrchestrationTimelineGraphEntryBinding>();
            foreach (ref readonly OrchestrationGraphEntry entry in graph.Entries.Span)
            {
                string entryId = graph.Identities.Table.Resolve(entry.EntryIdentityId);
                OrchestrationGraphExecutionSource invocation = sources[entryId];
                OrchestrationTimelineHook hook = invocation.InvocationCallerId switch
                {
                    "OnEnable" => OrchestrationTimelineHook.Enable,
                    "Root" => OrchestrationTimelineHook.Root,
                    "OnDisable" => OrchestrationTimelineHook.Disable,
                    "OnDestroy" => OrchestrationTimelineHook.Destroy,
                    _ => throw new InvalidOperationException($"Timeline 图 '{graphId}' 的入口 '{entryId}' 不是生命周期入口。")
                };
                entries.Add(hook, new OrchestrationTimelineGraphEntryBinding(entry.Execution,
                    graph.Program.Operations.Span[entry.Execution.OperationIndex].ExecutionFrameOwnerOperationIndex,
                    invocation.TargetIndex, invocation.SourceIdentityId));
            }
            return new OrchestrationTimelineGraphBinding(programIndex, graphId,
                graph.Identities.Table.Bind(graphId), entries);
        }
    }

    public sealed class OrchestrationTimelinePreparedDomainRoot
    {
        internal OrchestrationTimelinePreparedDomainRoot(
            OrchestrationTimelinePreparedContent<OrchestrationTime> content, int programCount)
        {
            Content = content;
            var admissions = new OrchestrationRootAdmission[content.Occurrences.Length];
            for (int index = 0; index < admissions.Length; index++)
            {
                ref readonly OrchestrationOccurrenceCapacity occurrence = ref content.Occurrences.Span[index];
                admissions[index] = new OrchestrationRootAdmission(new[] {
                    new OrchestrationExecutionRoot(occurrence.ProgramIndex, occurrence.OwnerOperationIndex)
                }, occurrence.MaximumInstances);
            }
            Admissions = admissions;
            var invocations = new int[programCount];
            foreach (ref readonly OrchestrationTimelineEntryTickCapacity entry in content.TickCapacity.GraphEntries.Span)
                invocations[entry.ProgramIndex] = checked(invocations[entry.ProgramIndex] +
                    entry.AdvanceInvocations + entry.StopInvocations);
            var budgets = new OrchestrationProgramTickBudget[programCount];
            for (int programIndex = 0; programIndex < budgets.Length; programIndex++)
                budgets[programIndex] = new OrchestrationProgramTickBudget(invocations[programIndex], 0);
            TickBudgets = budgets;
        }

        public OrchestrationTimelinePreparedContent<OrchestrationTime> Content { get; }
        public ReadOnlyMemory<OrchestrationRootAdmission> Admissions { get; }
        public ReadOnlyMemory<OrchestrationProgramTickBudget> TickBudgets { get; }

        public OrchestrationTimelinePreparedCapacity PrepareTimelineCapacity(
            OrchestrationExecutionCapacity executionCapacity,
            ReadOnlySpan<OrchestrationTimelinePreparedContent<OrchestrationTime>> nestedContents)
        {
            var contents = new OrchestrationTimelinePreparedContent<OrchestrationTime>[nestedContents.Length + 1];
            contents[0] = Content;
            nestedContents.CopyTo(contents.AsSpan(1));
            return OrchestrationTimelinePreparedCapacity.Prepare<OrchestrationTime>(contents, executionCapacity, 1);
        }
    }

    public sealed class OrchestrationTimelinePreparedRoot
    {
        internal OrchestrationTimelinePreparedRoot(TimelinePreviewRootIdentity identity,
            TimelineRuntimePreparedContent prepared, OrchestrationTimelinePreparedEntries entries,
            OrchestrationTimelinePreparedDomainRoot logic, OrchestrationTimelinePreparedDomainRoot presentation)
        {
            Identity = identity;
            Prepared = prepared;
            Entries = entries;
            Logic = logic;
            Presentation = presentation;
        }

        public TimelinePreviewRootIdentity Identity { get; }
        public TimelineRuntimePreparedContent Prepared { get; }
        public OrchestrationTimelinePreparedEntries Entries { get; }
        public OrchestrationTimelinePreparedDomainRoot Logic { get; }
        public OrchestrationTimelinePreparedDomainRoot Presentation { get; }
    }

    public enum OrchestrationTimelineCapacityIssueKind : byte
    {
        DecisionExitLifetimeUnresolved = 1,
        FrameBoundaryOutsideCycle = 2,
        LoopDurationNotPositive = 3
    }

    public readonly struct OrchestrationTimelineCapacityIssue
    {
        internal OrchestrationTimelineCapacityIssue(TimelineExecutionDomain domain, int? programIndex,
            int clipOrdinal, OrchestrationTimelineCapacityIssueKind kind, int operationIndex)
        {
            Domain = domain;
            ProgramIndex = programIndex;
            ClipOrdinal = clipOrdinal;
            Kind = kind;
            OperationIndex = operationIndex;
        }

        public TimelineExecutionDomain Domain { get; }
        public int? ProgramIndex { get; }
        public int ClipOrdinal { get; }
        public OrchestrationTimelineCapacityIssueKind Kind { get; }
        public int OperationIndex { get; }
    }

    public static class OrchestrationTimelineContentPreparation
    {
        public static OrchestrationTimelinePreparedEntries PrepareRootEntries(
            TimelineRuntimePreparedContent prepared,
            IReadOnlyDictionary<string, OrchestrationTimelineGraphBinding> graphs)
        {
            TimelineContentUnit content = prepared.Content;
            var clips = new OrchestrationTimelineEntryBinding[content.Clips.Count];
            var markers = new OrchestrationExecutionEntry[content.Markers.Count];
            var clipPrograms = new int[clips.Length];
            var markerPrograms = new int[markers.Length];
            var metadata = new List<OrchestrationTimelineInvocationMetadata>();
            for (int domainIndex = 0; domainIndex < 2; domainIndex++)
            {
                TimelineRuntimeDomainIndex domain = domainIndex == 0 ? prepared.Index.Logic : prepared.Index.Presentation;
                foreach (TimelineRuntimeIndexedClip indexed in domain.Trees)
                {
                    int ordinal = indexed.ContentIndex;
                    TimelineContentClip clip = content.Clips[ordinal];
                    OrchestrationTimelineGraphBinding graph = graphs[TimelineGraphBinding.GraphAuthoringId(clip.GraphBinding.GraphId)];
                    clipPrograms[ordinal] = graph.ProgramIndex;
                    OrchestrationExecutionEntry enable = BindRootEntry(ordinal, false, OrchestrationTimelineHook.Enable, in graph, metadata);
                    OrchestrationExecutionEntry root = BindRootEntry(ordinal, false, OrchestrationTimelineHook.Root, in graph, metadata);
                    OrchestrationExecutionEntry disable = BindRootEntry(ordinal, false, OrchestrationTimelineHook.Disable, in graph, metadata);
                    OrchestrationExecutionEntry destroy = BindRootEntry(ordinal, false, OrchestrationTimelineHook.Destroy, in graph, metadata);
                    clips[ordinal] = new OrchestrationTimelineEntryBinding(enable, root, disable, destroy);
                }
                for (int markerIndex = 0; markerIndex < domain.Markers.Count; markerIndex++)
                {
                    int ordinal = domain.Markers.ContentIndexAt(markerIndex);
                    TimelineContentMarker marker = content.Markers[ordinal];
                    OrchestrationTimelineGraphBinding graph = graphs[TimelineGraphBinding.GraphAuthoringId(marker.GraphId)];
                    markerPrograms[ordinal] = graph.ProgramIndex;
                    markers[ordinal] = BindRootEntry(ordinal, true, OrchestrationTimelineHook.Enable, in graph, metadata);
                }
            }
            return new OrchestrationTimelinePreparedEntries(clips, markers, clipPrograms, markerPrograms, metadata.ToArray());
        }

        public static bool TryPrepareRoot<TValue, TScalar>(TimelinePreviewRootIdentity identity,
            TimelineRuntimePreparedContent prepared, OrchestrationTimelinePlaybackMode mode,
            OrchestrationTimelinePreparedEntries entries,
            ReadOnlySpan<OrchestrationProgram<TValue>> programs,
            ReadOnlySpan<ReadOnlyMemory<OrchestrationExtensionReference>> extensions,
            IOrchestrationScalarValueAccess<TValue, TScalar> values, IScalarMath<TScalar> math,
            IOrchestrationTimelineTimeProjection<OrchestrationTime, TScalar> projection, int tickRate,
            out OrchestrationTimelinePreparedRoot root,
            out ReadOnlyMemory<OrchestrationTimelineCapacityIssue> unresolved)
            where TValue : unmanaged
            where TScalar : unmanaged
        {
            if (!string.Equals(identity.Timeline, prepared.Content.AuthoringId, StringComparison.Ordinal))
                throw new ArgumentException("独立 Timeline 根必须指向本次准备内容的作者身份。", nameof(identity));
            root = null;
            if (!TryCalculateCapacity(prepared, mode, TimelineExecutionDomain.Logic, entries, programs, extensions,
                values, math, projection, tickRate, out var logic, out unresolved))
                return false;
            if (!TryCalculateCapacity(prepared, mode, TimelineExecutionDomain.Presentation, entries, programs, extensions,
                values, math, projection, tickRate, out var presentation, out unresolved))
                return false;
            root = new OrchestrationTimelinePreparedRoot(identity, prepared, entries,
                new OrchestrationTimelinePreparedDomainRoot(logic, programs.Length),
                new OrchestrationTimelinePreparedDomainRoot(presentation, programs.Length));
            return true;
        }

        public static OrchestrationTimelinePreparedEntries PrepareEntries<TValue>(
            TimelineRuntimePreparedContent prepared, OrchestrationGraphContent<TValue> graph,
            OrchestrationProgram<TValue> program, ReadOnlySpan<int> localToShared,
            int programIndex, int operationIndex) where TValue : unmanaged
        {
            var sources = new Dictionary<(string Clip, string Graph, string Hook), OrchestrationGraphSource>();
            foreach (OrchestrationGraphSource source in graph.Sources.Bind(graph.Structure.Sources.Span).Span)
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
            var clipPrograms = new int[clips.Length];
            var markerPrograms = new int[markers.Length];
            Array.Fill(clipPrograms, programIndex);
            Array.Fill(markerPrograms, programIndex);
            var metadata = new List<OrchestrationTimelineInvocationMetadata>();
            for (int domainIndex = 0; domainIndex < 2; domainIndex++)
            {
                TimelineRuntimeDomainIndex domain = domainIndex == 0 ? prepared.Index.Logic : prepared.Index.Presentation;
                foreach (TimelineRuntimeIndexedClip indexed in domain.Trees)
                {
                    int ordinal = indexed.ContentIndex;
                    TimelineContentClip clip = content.Clips[ordinal];
                    string graphId = TimelineGraphBinding.GraphAuthoringId(clip.GraphBinding.GraphId);
                    OrchestrationExecutionEntry enable = BindEntry(programIndex, ordinal, false, OrchestrationTimelineHook.Enable,
                        sources[(clip.AuthoringId, graphId, "OnEnable")], graph, program, localToShared, metadata);
                    OrchestrationExecutionEntry root = BindEntry(programIndex, ordinal, false, OrchestrationTimelineHook.Root,
                        sources[(clip.AuthoringId, graphId, "Root")], graph, program, localToShared, metadata);
                    OrchestrationExecutionEntry disable = BindEntry(programIndex, ordinal, false, OrchestrationTimelineHook.Disable,
                        sources[(clip.AuthoringId, graphId, "OnDisable")], graph, program, localToShared, metadata);
                    OrchestrationExecutionEntry destroy = BindEntry(programIndex, ordinal, false, OrchestrationTimelineHook.Destroy,
                        sources[(clip.AuthoringId, graphId, "OnDestroy")], graph, program, localToShared, metadata);
                    clips[ordinal] = new OrchestrationTimelineEntryBinding(enable, root, disable, destroy);
                }
                for (int markerIndex = 0; markerIndex < domain.Markers.Count; markerIndex++)
                {
                    int ordinal = domain.Markers.ContentIndexAt(markerIndex);
                    TimelineContentMarker marker = content.Markers[ordinal];
                    markers[ordinal] = BindEntry(programIndex, ordinal, true, OrchestrationTimelineHook.Enable,
                        sources[(marker.MarkerId, TimelineGraphBinding.GraphAuthoringId(marker.GraphId), "OnEnable")], graph, program, localToShared, metadata);
                }
            }
            return new OrchestrationTimelinePreparedEntries(clips, markers, clipPrograms, markerPrograms, metadata.ToArray());
        }

        public static bool TryCalculateCapacity<TValue, TScalar>(
            TimelineRuntimePreparedContent prepared, OrchestrationTimelinePlaybackMode mode,
            TimelineExecutionDomain domain,
            OrchestrationTimelinePreparedEntries entries,
            ReadOnlySpan<OrchestrationProgram<TValue>> programs,
            ReadOnlySpan<ReadOnlyMemory<OrchestrationExtensionReference>> extensions,
            IOrchestrationScalarValueAccess<TValue, TScalar> values, IScalarMath<TScalar> math,
            IOrchestrationTimelineTimeProjection<OrchestrationTime, TScalar> projection, int tickRate,
            out OrchestrationTimelinePreparedContent<OrchestrationTime> content,
            out ReadOnlyMemory<OrchestrationTimelineCapacityIssue> unresolved)
            where TValue : unmanaged
            where TScalar : unmanaged
        {
            TimelineRuntimeSchedulingContent scheduling = prepared.Scheduling(domain);
            if (mode == OrchestrationTimelinePlaybackMode.Loop && prepared.Content.Duration <= OrchestrationTime.Zero)
            {
                content = null;
                unresolved = new[] { new OrchestrationTimelineCapacityIssue(domain, null, -1,
                    OrchestrationTimelineCapacityIssueKind.LoopDurationNotPositive, -1) };
                return false;
            }
            var issues = new List<OrchestrationTimelineCapacityIssue>();
            var exits = new OrchestrationTimelineExitAnalysis<TValue, TScalar>[programs.Length];
            var occurrences = new List<OrchestrationOccurrenceCapacity>();
            var clipInstances = new int[scheduling.Clips.Length];
            foreach (int ordinal in scheduling.Boundaries.Decisions.Span)
                clipInstances[ordinal] = 1;
            int activeTrees = 0;
            int decisionInstances = 0;
            int traversals = mode == OrchestrationTimelinePlaybackMode.Loop
                ? OrchestrationTimelineLimits.MaximumCycleAdvance + 1 : 1;
            for (int ordinal = 0; ordinal < scheduling.Clips.Length; ordinal++)
            {
                ref readonly TimelineSchedulingClip<OrchestrationTime> clip = ref scheduling.Clips.Span[ordinal];
                if (!clip.Enabled || !clip.Tree || prepared.Content.Duration == OrchestrationTime.Zero)
                    continue;
                int instances = 1;
                int programIndex = entries.ClipPrograms.Span[ordinal];
                OrchestrationProgram<TValue> program = programs[programIndex];
                if (mode == OrchestrationTimelinePlaybackMode.Loop)
                {
                    if (clip.TreeDecision)
                    {
                        OrchestrationExecutionEntry root = entries.ClipEntries.Span[ordinal].Root;
                        OrchestrationTimelineExitAnalysis<TValue, TScalar> exit = exits[programIndex] ??
                            (exits[programIndex] = new OrchestrationTimelineExitAnalysis<TValue, TScalar>(
                                program, extensions[programIndex], values, math, projection));
                        if (!exit.TryProve(in root, in clip, out OrchestrationTime exitTime, out int blocked))
                        {
                            issues.Add(new OrchestrationTimelineCapacityIssue(domain, programIndex, ordinal,
                                OrchestrationTimelineCapacityIssueKind.DecisionExitLifetimeUnresolved, blocked));
                            continue;
                        }
                        long lifetimeCycles = (exitTime.RawValue - clip.Start.RawValue) / prepared.Content.Duration.RawValue;
                        // 退出在下一步消费，本次跨过的新周期仍可同时占槽。
                        instances = checked((int)lifetimeCycles + 1 + traversals);
                    }
                    else if (clip.End > prepared.Content.Duration)
                        issues.Add(new OrchestrationTimelineCapacityIssue(domain, programIndex, ordinal,
                            OrchestrationTimelineCapacityIssueKind.FrameBoundaryOutsideCycle,
                            entries.ClipEntries.Span[ordinal].Root.OperationIndex));
                }
                activeTrees = checked(activeTrees + instances);
                clipInstances[ordinal] = instances;
                if (clip.TreeDecision)
                {
                    decisionInstances = checked(decisionInstances + instances);
                }
                occurrences.Add(new OrchestrationOccurrenceCapacity(
                    programIndex,
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
            {
                int programIndex = entries.MarkerPrograms.Span[marker.Ordinal];
                occurrences.Add(new OrchestrationOccurrenceCapacity(
                    programIndex, programs[programIndex].Operation(entries.MarkerEntries.Span[marker.Ordinal].OperationIndex).ExecutionFrameOwnerOperationIndex, 1));
            }
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
                int programIndex = entries.ClipPrograms.Span[ordinal];
                ref readonly TimelineSchedulingClip<OrchestrationTime> clip = ref scheduling.Clips.Span[ordinal];
                int instances = clipInstances[ordinal];
                int retained = clip.TreeDecision ? instances : 0;
                graphEntries.Add(new OrchestrationTimelineEntryTickCapacity(programIndex, entry.Enable.OperationIndex, traversals, 0));
                graphEntries.Add(new OrchestrationTimelineEntryTickCapacity(programIndex, entry.Root.OperationIndex,
                    checked(traversals + retained), 0));
                graphEntries.Add(new OrchestrationTimelineEntryTickCapacity(programIndex, entry.Disable.OperationIndex,
                    checked(traversals + retained), 0));
                graphEntries.Add(new OrchestrationTimelineEntryTickCapacity(programIndex, entry.Destroy.OperationIndex,
                    mode == OrchestrationTimelinePlaybackMode.Loop ? 0 : instances, instances));
            }
            foreach (ref readonly TimelineSchedulingMarker<OrchestrationTime> marker in scheduling.Markers.Span)
                graphEntries.Add(new OrchestrationTimelineEntryTickCapacity(
                    entries.MarkerPrograms.Span[marker.Ordinal], entries.MarkerEntries.Span[marker.Ordinal].OperationIndex, traversals, 0));
            var tickCapacity = new OrchestrationTimelineContentTickCapacity(
                checked((scheduling.Markers.Length + 3 * trees) * traversals +
                    (mode == OrchestrationTimelinePlaybackMode.Loop ? 2 * decisionInstances : decisionInstances)), activeTrees,
                sampledClips, checked(sampledClips * traversals), graphEntries.ToArray());
            ReadOnlyMemory<OrchestrationTimelineTreeCycleWindow> cycleWindows =
                scheduling.Boundaries.PrepareCycleWindows(clipInstances, out int exitWords);
            content = new OrchestrationTimelinePreparedContent<OrchestrationTime>(
                prepared.ContentIdentityId, prepared.ContentRevisionId, prepared.Content.Duration,
                tickRate,
                mode, domain, scheduling.Clips, scheduling.Boundaries, scheduling.Lifetimes, scheduling.TreeOrdinals,
                scheduling.Markers,
                entries.ClipEntries, entries.MarkerEntries, activeTrees, decisionInstances, cycleWindows, exitWords,
                prepared.Content.Duration == OrchestrationTime.Zero
                    ? ReadOnlyMemory<OrchestrationOccurrenceCapacity>.Empty : occurrences.ToArray(), in tickCapacity);
            return true;
        }

        static OrchestrationExecutionEntry BindRootEntry(int ordinal, bool marker, OrchestrationTimelineHook hook,
            in OrchestrationTimelineGraphBinding graph, List<OrchestrationTimelineInvocationMetadata> metadata)
        {
            OrchestrationTimelineGraphEntryBinding binding = graph.Entry(hook);
            metadata.Add(new OrchestrationTimelineInvocationMetadata(graph.ProgramIndex, ordinal, marker, hook, binding.Entry,
                binding.ExecutionFrameOwnerOperationIndex, binding.GraphOwnerOperationIndex,
                null, graph.GraphIdentityId, binding.SourceIdentityId));
            return binding.Entry;
        }

        static OrchestrationExecutionEntry BindEntry<TValue>(int programIndex, int ordinal, bool marker,
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
            metadata.Add(new OrchestrationTimelineInvocationMetadata(programIndex, ordinal, marker, hook, entry,
                program.Operation(entry.OperationIndex).ExecutionFrameOwnerOperationIndex,
                invocation.TargetIndex, invocation.InvocationCallerOperationIndex,
                graphIdentityId, sourceIdentityId));
            return entry;
        }
    }
}
