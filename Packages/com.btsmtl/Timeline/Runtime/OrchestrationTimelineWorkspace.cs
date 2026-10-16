using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public readonly struct OrchestrationTimelinePreparedCapacity
    {
        public OrchestrationTimelinePreparedCapacity(int playbackCount,
            int activeTreesPerPlayback, int treeExitRequestsPerPlayback, int treeExitsPerPlayback)
        {
            PlaybackCount = playbackCount;
            ActiveTreesPerPlayback = activeTreesPerPlayback;
            TreeExitRequestsPerPlayback = treeExitRequestsPerPlayback;
            TreeExitsPerPlayback = treeExitsPerPlayback;
        }

        public int PlaybackCount { get; }
        public int ActiveTreesPerPlayback { get; }
        public int TreeExitRequestsPerPlayback { get; }
        public int TreeExitsPerPlayback { get; }

        public static OrchestrationTimelinePreparedCapacity Prepare<TTime>(
            ReadOnlySpan<OrchestrationTimelinePreparedContent<TTime>> contents,
            OrchestrationExecutionCapacity executionCapacity)
            where TTime : unmanaged, IEquatable<TTime>
        {
            int activeTrees = 0;
            int exitRequests = 0;
            int exits = 0;
            foreach (OrchestrationTimelinePreparedContent<TTime> content in contents)
            {
                activeTrees = Math.Max(activeTrees, content.RequiredActiveTrees);
                exitRequests = Math.Max(exitRequests, content.RequiredTreeExitRequests);
                exits = Math.Max(exits, content.RequiredTreeExits);
            }
            return new OrchestrationTimelinePreparedCapacity(executionCapacity.PlaybackCount,
                activeTrees, exitRequests, exits);
        }

        public OrchestrationTimelineStateLayout<TPlayback> CreateLayout<TPlayback>(
            StableHash contentIdentity, NumericProfileId numericProfile, TargetAbiVersion targetAbi,
            Func<StableHash, NumericProfileId, TargetAbiVersion, int, int, int, int,
                OrchestrationTimelineStateLayout<TPlayback>> generatedFactory)
            where TPlayback : unmanaged =>
            generatedFactory(contentIdentity, numericProfile, targetAbi, PlaybackCount, 1,
                checked(PlaybackCount * TreeExitRequestsPerPlayback),
                checked(PlaybackCount * TreeExitsPerPlayback));
    }

    public sealed class OrchestrationTimelinePreparedContent<TTime>
        where TTime : unmanaged, IEquatable<TTime>
    {
        internal OrchestrationTimelinePreparedContent(int contentIdentityId, int contentRevisionId,
            TTime duration, int tickRate, OrchestrationTimelinePlaybackMode mode,
            TimelineExecutionDomain domain,
            ReadOnlyMemory<TimelineSchedulingClip<TTime>> clips,
            ReadOnlyMemory<int> treeOrdinals,
            ReadOnlyMemory<TimelineSchedulingMarker<TTime>> markers,
            ReadOnlyMemory<TimelineSchedulingSection<TTime>> sections,
            ReadOnlyMemory<OrchestrationTimelineEntryBinding> clipEntries,
            ReadOnlyMemory<OrchestrationExecutionEntry> markerEntries,
            int requiredActiveTrees, int requiredTreeExitRequests, int requiredTreeExits,
            OrchestrationTimelineCallCapacity callCapacity,
            in OrchestrationTimelineTickContentCapacity tickCapacity)
        {
            ContentIdentityId = contentIdentityId;
            ContentRevisionId = contentRevisionId;
            Duration = duration;
            TickRate = tickRate;
            Mode = mode;
            Domain = domain;
            Clips = clips;
            TreeOrdinals = treeOrdinals;
            Markers = markers;
            Sections = sections;
            ClipEntries = clipEntries;
            MarkerEntries = markerEntries;
            RequiredActiveTrees = requiredActiveTrees;
            RequiredTreeExitRequests = requiredTreeExitRequests;
            RequiredTreeExits = requiredTreeExits;
            CallCapacity = callCapacity;
            TickCapacity = tickCapacity;
        }

        public int ContentIdentityId { get; }
        public int ContentRevisionId { get; }
        public TTime Duration { get; }
        public int TickRate { get; }
        public OrchestrationTimelinePlaybackMode Mode { get; }
        public TimelineExecutionDomain Domain { get; }
        public ReadOnlyMemory<TimelineSchedulingClip<TTime>> Clips { get; }
        public ReadOnlyMemory<int> TreeOrdinals { get; }
        public ReadOnlyMemory<TimelineSchedulingMarker<TTime>> Markers { get; }
        public ReadOnlyMemory<TimelineSchedulingSection<TTime>> Sections { get; }
        public ReadOnlyMemory<OrchestrationTimelineEntryBinding> ClipEntries { get; }
        public ReadOnlyMemory<OrchestrationExecutionEntry> MarkerEntries { get; }
        public int RequiredActiveTrees { get; }
        public int RequiredTreeExitRequests { get; }
        public int RequiredTreeExits { get; }
        public OrchestrationTimelineCallCapacity CallCapacity { get; }
        public OrchestrationTimelineTickContentCapacity TickCapacity { get; }
    }

    public sealed class OrchestrationTimelineWorkspace<TTime>
        where TTime : unmanaged, IEquatable<TTime>
    {
        internal readonly Operation[] Operations;
        internal readonly OrchestrationTimelineInvocation<TTime>[] Invocations;
        internal readonly TimelineScheduledInvocation<TTime>[] Scheduled;
        internal readonly TimelineSchedulingBoundary<TTime>[] Boundaries;
        internal readonly TimelineSchedulingBoundary<TTime>[] ActiveTrees;
        internal readonly OrchestrationTimelineTreeExitRange[] EvaluationExits;
        internal readonly OrchestrationTimelineClipOccurrence[] ProducedExits;
        internal readonly bool[] PreviousActiveClips;
        internal readonly bool[] SelectedTrees;
        readonly InvocationFrame[] m_InvocationFrames;
        int m_InvocationDepth;
        ulong m_BatchSequence;

        public OrchestrationTimelineWorkspace(
            ReadOnlySpan<OrchestrationTimelinePreparedContent<TTime>> contents,
            in OrchestrationTimelinePreparedCapacity capacity)
        {
            Capacity = capacity;
            int clips = 0;
            int boundaries = 0;
            int invocations = capacity.ActiveTreesPerPlayback;
            for (int i = 0; i < contents.Length; i++)
            {
                OrchestrationTimelinePreparedContent<TTime> content = contents[i];
                int traversals = content.Mode == OrchestrationTimelinePlaybackMode.Loop
                    ? OrchestrationTimelineLimits.MaximumCycleAdvance + 1 : 1;
                clips = Math.Max(clips, content.Clips.Length);
                boundaries = Math.Max(boundaries, checked(2 * content.Clips.Length * traversals +
                    capacity.TreeExitRequestsPerPlayback));
                invocations = Math.Max(invocations, content.TickCapacity.GraphInvocationsPerAdvance);
            }
            ClipCapacity = clips;
            BoundaryCapacity = boundaries;
            InvocationCapacity = invocations;
            EvaluationExitCapacity = checked(capacity.TreeExitsPerPlayback + capacity.TreeExitRequestsPerPlayback);
            Operations = new Operation[capacity.PlaybackCount];
            Invocations = new OrchestrationTimelineInvocation<TTime>[checked(capacity.PlaybackCount * invocations)];
            Scheduled = new TimelineScheduledInvocation<TTime>[checked(capacity.PlaybackCount * invocations)];
            Boundaries = new TimelineSchedulingBoundary<TTime>[checked(capacity.PlaybackCount * boundaries)];
            ActiveTrees = new TimelineSchedulingBoundary<TTime>[checked(capacity.PlaybackCount * capacity.ActiveTreesPerPlayback)];
            EvaluationExits = new OrchestrationTimelineTreeExitRange[checked(capacity.PlaybackCount * EvaluationExitCapacity)];
            ProducedExits = new OrchestrationTimelineClipOccurrence[checked(capacity.PlaybackCount * capacity.TreeExitRequestsPerPlayback)];
            PreviousActiveClips = new bool[checked(capacity.PlaybackCount * clips)];
            SelectedTrees = new bool[checked(capacity.PlaybackCount * clips)];
            m_InvocationFrames = new InvocationFrame[checked(capacity.PlaybackCount + 1)];
        }

        public OrchestrationTimelinePreparedCapacity Capacity { get; }
        internal int ClipCapacity { get; }
        internal int BoundaryCapacity { get; }
        internal int InvocationCapacity { get; }
        internal int EvaluationExitCapacity { get; }
        internal ulong NextBatchSequence() => checked(++m_BatchSequence);

        public bool HasCurrentInvocation => m_InvocationDepth > 0;

        public int InvocationDepth => m_InvocationDepth;

        public ref readonly OrchestrationTimelineInvocation<TTime> CurrentInvocation =>
            ref InvocationAt(m_InvocationDepth - 1);

        public ref readonly OrchestrationTimelineInvocation<TTime> InvocationAt(int stackIndex)
        {
            InvocationFrame frame = m_InvocationFrames[stackIndex];
            return ref Invocations[checked(frame.PlaybackSlot * InvocationCapacity + frame.InvocationIndex)];
        }

        public ref readonly OrchestrationTimelineStart<TTime> CurrentStart =>
            ref Operations[CurrentInvocation.Token.PlaybackSlot].Proposed.Start;

        public TTime PreviousTime => Operations[CurrentInvocation.Token.PlaybackSlot].PreviousTime;

        public int PreviousCycle => Operations[CurrentInvocation.Token.PlaybackSlot].PreviousCycle;

        internal void EnterInvocation(int playbackSlot, int invocationIndex)
        {
            m_InvocationFrames[m_InvocationDepth++] = new InvocationFrame
            {
                PlaybackSlot = playbackSlot,
                InvocationIndex = invocationIndex
            };
        }

        internal void LeaveInvocation() => m_InvocationDepth--;

        internal struct Operation
        {
            internal OrchestrationTimelinePlaybackState<TTime> Proposed;
            internal ulong BatchSequence;
            internal ulong Tick;
            internal int SourceIdentityId;
            internal bool Stop;
            internal bool Paused;
            internal int InvocationCount;
            internal int MarkerCount;
            internal int InjectedExitCount;
            internal int EvaluationExitCount;
            internal int ProducedExitCount;
            internal int ScheduledTreeOffset;
            internal int ScheduledTreeCount;
            internal TTime PreviousTime;
            internal int PreviousCycle;
            internal TTime RequestedAdvance;
        }

        struct InvocationFrame
        {
            internal int PlaybackSlot;
            internal int InvocationIndex;
        }
    }
}
