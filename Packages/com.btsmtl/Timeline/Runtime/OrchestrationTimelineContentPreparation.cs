using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;

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
            OrchestrationTimelineInvocationMetadata[] metadata,
            OrchestrationOccurrenceCapacity[] occurrences)
        {
            ClipEntries = clips;
            MarkerEntries = markers;
            Invocations = metadata;
            Occurrences = occurrences;
        }

        public ReadOnlyMemory<OrchestrationTimelineEntryBinding> ClipEntries { get; }
        public ReadOnlyMemory<OrchestrationExecutionEntry> MarkerEntries { get; }
        public ReadOnlyMemory<OrchestrationTimelineInvocationMetadata> Invocations { get; }
        internal ReadOnlyMemory<OrchestrationOccurrenceCapacity> Occurrences { get; }
    }

    public enum OrchestrationTimelineCapacityIssueKind : byte
    {
        DecisionExitLifetimeUnresolved = 1,
        FrameBoundaryOutsideCycle = 2
    }

    public readonly struct OrchestrationTimelineCapacityIssue
    {
        internal OrchestrationTimelineCapacityIssue(int clipOrdinal,
            OrchestrationTimelineCapacityIssueKind kind)
        {
            ClipOrdinal = clipOrdinal;
            Kind = kind;
        }

        public int ClipOrdinal { get; }
        public OrchestrationTimelineCapacityIssueKind Kind { get; }
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
            var occurrences = new List<OrchestrationOccurrenceCapacity>();
            foreach (int ordinal in prepared.SchedulingTreeOrdinals.Span)
            {
                TimelineContentClip clip = content.Clips[ordinal];
                string graphId = clip.GraphBinding.GraphId;
                OrchestrationExecutionEntry enable = BindEntry(ordinal, false, OrchestrationTimelineHook.Enable,
                    sources[(clip.AuthoringId, graphId, "OnEnable")], graph, program, localToShared, metadata);
                OrchestrationExecutionEntry root = BindEntry(ordinal, false, OrchestrationTimelineHook.Root,
                    sources[(clip.AuthoringId, graphId, "Root")], graph, program, localToShared, metadata);
                OrchestrationExecutionEntry disable = BindEntry(ordinal, false, OrchestrationTimelineHook.Disable,
                    sources[(clip.AuthoringId, graphId, "OnDisable")], graph, program, localToShared, metadata);
                OrchestrationExecutionEntry destroy = BindEntry(ordinal, false, OrchestrationTimelineHook.Destroy,
                    sources[(clip.AuthoringId, graphId, "OnDestroy")], graph, program, localToShared, metadata);
                clips[ordinal] = new OrchestrationTimelineEntryBinding(enable, root, disable, destroy);
                occurrences.Add(new OrchestrationOccurrenceCapacity(
                    program.Operation(root.OperationIndex).ExecutionFrameOwnerOperationIndex, 1));
            }
            foreach (ref readonly TimelineSchedulingMarker<OrchestrationTime> marker in prepared.SchedulingMarkers.Span)
            {
                TimelineContentMarker value = content.Markers[marker.Ordinal];
                OrchestrationExecutionEntry entry = BindEntry(marker.Ordinal, true, OrchestrationTimelineHook.Enable,
                    sources[(value.MarkerId, value.GraphId, "OnEnable")], graph, program, localToShared, metadata);
                markers[marker.Ordinal] = entry;
                occurrences.Add(new OrchestrationOccurrenceCapacity(
                    program.Operation(entry.OperationIndex).ExecutionFrameOwnerOperationIndex, 1));
            }
            return new OrchestrationTimelinePreparedEntries(clips, markers, metadata.ToArray(), occurrences.ToArray());
        }

        public static bool TryCalculateCapacity<TValue>(int programIndex, int operationIndex,
            OrchestrationProgram<TValue> program, TimelineRuntimePreparedContent prepared,
            OrchestrationTimelinePreparedEntries entries, int tickRate,
            out OrchestrationTimelinePreparedContent<OrchestrationTime> content,
            out ReadOnlyMemory<OrchestrationTimelineCapacityIssue> unresolved) where TValue : unmanaged
        {
            OrchestrationTimelinePlaybackMode mode =
                (OrchestrationTimelinePlaybackMode)program.Operation(operationIndex).Integer0;
            var issues = new List<OrchestrationTimelineCapacityIssue>();
            int activeTrees = 0;
            int decisions = 0;
            foreach (ref readonly TimelineSchedulingClip<OrchestrationTime> clip in prepared.SchedulingClips.Span)
                if (clip.Logic && clip.Tree && prepared.Content.Duration > OrchestrationTime.Zero)
                    activeTrees++;
            for (int ordinal = 0; ordinal < prepared.SchedulingClips.Length; ordinal++)
            {
                ref readonly TimelineSchedulingClip<OrchestrationTime> clip = ref prepared.SchedulingClips.Span[ordinal];
                if (!clip.Logic || !clip.Tree || prepared.Content.Duration == OrchestrationTime.Zero)
                    continue;
                if (clip.TreeDecision)
                    decisions++;
                if (mode == OrchestrationTimelinePlaybackMode.Loop)
                {
                    if (clip.TreeDecision)
                        issues.Add(new OrchestrationTimelineCapacityIssue(ordinal,
                            OrchestrationTimelineCapacityIssueKind.DecisionExitLifetimeUnresolved));
                    else if (clip.End > prepared.Content.Duration)
                        issues.Add(new OrchestrationTimelineCapacityIssue(ordinal,
                            OrchestrationTimelineCapacityIssueKind.FrameBoundaryOutsideCycle));
                }
            }
            unresolved = issues.ToArray();
            if (issues.Count != 0)
            {
                content = null;
                return false;
            }
            var call = new OrchestrationTimelineCallCapacity(programIndex, operationIndex,
                prepared.Content.Duration == OrchestrationTime.Zero
                    ? ReadOnlyMemory<OrchestrationOccurrenceCapacity>.Empty : entries.Occurrences);
            int trees = 0;
            foreach (ref readonly TimelineSchedulingClip<OrchestrationTime> clip in prepared.SchedulingClips.Span)
                if (clip.Logic && clip.Tree)
                    trees++;
            int sampledClips = 0;
            foreach (TimelineContentClip clip in prepared.Content.Clips)
                if (!clip.TrackMuted && clip.ExecutionPolicy.IsLogic && clip.ContentKind == TimelineContentKind.Sampled)
                    sampledClips++;
            int traversals = mode == OrchestrationTimelinePlaybackMode.Loop
                ? OrchestrationTimelineLimits.MaximumCycleAdvance + 1 : 1;
            var tickCapacity = new OrchestrationTimelineTickContentCapacity(programIndex, operationIndex,
                checked((prepared.SchedulingMarkers.Length + 3 * trees) * traversals + decisions), activeTrees,
                sampledClips, checked(sampledClips * traversals));
            content = new OrchestrationTimelinePreparedContent<OrchestrationTime>(
                program.Operation(operationIndex).TimelineContentIdentityId, prepared.ContentRevisionId, prepared.Content.Duration,
                tickRate,
                mode, prepared.SchedulingClips, prepared.SchedulingTreeOrdinals,
                prepared.SchedulingMarkers, prepared.SchedulingSections,
                entries.ClipEntries, entries.MarkerEntries, activeTrees, decisions, decisions, call, in tickCapacity);
            return true;
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
