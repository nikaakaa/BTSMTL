using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public readonly struct OrchestrationTimelinePreparedCapacity
    {
        public OrchestrationTimelinePreparedCapacity(int playbackCount,
            int activeTreesPerPlayback, int treeExitRequestsPerPlayback,
            int treeExitWindowsPerPlayback, int treeExitWordsPerPlayback)
        {
            PlaybackCount = playbackCount;
            ActiveTreesPerPlayback = activeTreesPerPlayback;
            TreeExitRequestsPerPlayback = treeExitRequestsPerPlayback;
            TreeExitWindowsPerPlayback = treeExitWindowsPerPlayback;
            TreeExitWordsPerPlayback = treeExitWordsPerPlayback;
        }

        public int PlaybackCount { get; }
        public int ActiveTreesPerPlayback { get; }
        public int TreeExitRequestsPerPlayback { get; }
        public int TreeExitWindowsPerPlayback { get; }
        public int TreeExitWordsPerPlayback { get; }

        public static OrchestrationTimelinePreparedCapacity Prepare<TTime>(
            ReadOnlySpan<OrchestrationTimelinePreparedContent<TTime>> contents,
            OrchestrationExecutionCapacity executionCapacity)
            where TTime : unmanaged, IEquatable<TTime>
        {
            int activeTrees = 0;
            int exitRequests = 0;
            int windows = 0;
            int words = 0;
            foreach (OrchestrationTimelinePreparedContent<TTime> content in contents)
            {
                activeTrees = Math.Max(activeTrees, content.RequiredActiveTrees);
                exitRequests = Math.Max(exitRequests, content.RequiredTreeExitRequests);
                windows = Math.Max(windows, content.RequiredTreeExitWindows);
                words = Math.Max(words, content.RequiredTreeExitWords);
            }
            return new OrchestrationTimelinePreparedCapacity(executionCapacity.PlaybackCount,
                activeTrees, exitRequests, windows, words);
        }

        public OrchestrationTimelineStateLayout<TPlayback> CreateLayout<TPlayback>(
            StableHash contentIdentity, NumericProfileId numericProfile, TargetAbiVersion targetAbi,
            Func<StableHash, NumericProfileId, TargetAbiVersion, int, int, int, int, int,
                OrchestrationTimelineStateLayout<TPlayback>> generatedFactory)
            where TPlayback : unmanaged =>
            generatedFactory(contentIdentity, numericProfile, targetAbi, PlaybackCount, 1,
                checked(PlaybackCount * TreeExitWordsPerPlayback),
                checked(PlaybackCount * TreeExitWindowsPerPlayback),
                checked(PlaybackCount * TreeExitRequestsPerPlayback));
    }

    public sealed class OrchestrationTimelinePreparedContent<TTime>
        where TTime : unmanaged, IEquatable<TTime>
    {
        internal OrchestrationTimelinePreparedContent(int contentIdentityId, int contentRevisionId,
            TTime duration, int tickRate, OrchestrationTimelinePlaybackMode mode,
            TimelineExecutionDomain domain,
            ReadOnlyMemory<TimelineSchedulingClip<TTime>> clips,
            TimelineRuntimeBoundaryIndex<TTime> boundaries,
            TimelineRuntimeBoundaryIndex<TTime> lifetimes,
            ReadOnlyMemory<int> treeOrdinals,
            ReadOnlyMemory<TimelineSchedulingMarker<TTime>> markers,
            ReadOnlyMemory<TimelineSchedulingSection<TTime>> sections,
            ReadOnlyMemory<OrchestrationTimelineEntryBinding> clipEntries,
            ReadOnlyMemory<OrchestrationExecutionEntry> markerEntries,
            int requiredActiveTrees, int requiredTreeExitRequests,
            ReadOnlyMemory<OrchestrationTimelineTreeCycleWindow> treeCycleWindows,
            int requiredTreeExitWords,
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
            Boundaries = boundaries;
            Lifetimes = lifetimes;
            TreeOrdinals = treeOrdinals;
            Markers = markers;
            Sections = sections;
            ClipEntries = clipEntries;
            MarkerEntries = markerEntries;
            RequiredActiveTrees = requiredActiveTrees;
            RequiredTreeExitRequests = requiredTreeExitRequests;
            TreeCycleWindows = treeCycleWindows;
            RequiredTreeExitWindows = boundaries.Decisions.Length;
            RequiredTreeExitWords = requiredTreeExitWords;
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
        public TimelineRuntimeBoundaryIndex<TTime> Boundaries { get; }
        public TimelineRuntimeBoundaryIndex<TTime> Lifetimes { get; }
        public ReadOnlyMemory<int> TreeOrdinals { get; }
        public ReadOnlyMemory<TimelineSchedulingMarker<TTime>> Markers { get; }
        public ReadOnlyMemory<TimelineSchedulingSection<TTime>> Sections { get; }
        public ReadOnlyMemory<OrchestrationTimelineEntryBinding> ClipEntries { get; }
        public ReadOnlyMemory<OrchestrationExecutionEntry> MarkerEntries { get; }
        public int RequiredActiveTrees { get; }
        public int RequiredTreeExitRequests { get; }
        public ReadOnlyMemory<OrchestrationTimelineTreeCycleWindow> TreeCycleWindows { get; }
        public int RequiredTreeExitWindows { get; }
        public int RequiredTreeExitWords { get; }
        public OrchestrationTimelineCallCapacity CallCapacity { get; }
        public OrchestrationTimelineTickContentCapacity TickCapacity { get; }
        // 一次推进的遍历段数：循环按可跨圈数，否则一段。
        public int Traversals => Mode == OrchestrationTimelinePlaybackMode.Loop
            ? OrchestrationTimelineLimits.MaximumCycleAdvance + 1 : 1;
    }

    public sealed class OrchestrationTimelineWorkspace<TTime>
        where TTime : unmanaged, IEquatable<TTime>
    {
        internal readonly Operation[] Operations;
        internal readonly OrchestrationTimelineInvocation<TTime>[] Invocations;
        internal readonly TimelineScheduledInvocation<TTime>[] Scheduled;
        internal readonly TimelineSchedulingBoundary<TTime>[] Boundaries;
        internal readonly TimelineSchedulingBoundary<TTime>[] SyntheticBoundaries;
        internal readonly TimelineSchedulingBoundary<TTime>[] ActiveTrees;
        internal readonly long[] EvaluationFirstCycles;
        internal readonly ulong[] EvaluationExitBits;
        internal readonly OrchestrationTimelineClipOccurrence[] ProducedExits;
        internal readonly bool[] SelectedTrees;
        internal readonly int[] SelectedTreeOrdinals;
        internal readonly int[] SelectedTreeCounts;
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
            int syntheticBoundaries = 0;
            int invocations = capacity.ActiveTreesPerPlayback;
            for (int i = 0; i < contents.Length; i++)
            {
                OrchestrationTimelinePreparedContent<TTime> content = contents[i];
                int traversals = content.Traversals;
                clips = Math.Max(clips, content.Clips.Length);
                boundaries = Math.Max(boundaries, checked(2 * content.TreeOrdinals.Length * traversals +
                    capacity.TreeExitRequestsPerPlayback));
                syntheticBoundaries = Math.Max(syntheticBoundaries, checked(2 * content.TreeOrdinals.Length));
                invocations = Math.Max(invocations, content.TickCapacity.GraphInvocationsPerAdvance);
            }
            ClipCapacity = clips;
            BoundaryCapacity = boundaries;
            SyntheticBoundaryCapacity = syntheticBoundaries;
            InvocationCapacity = invocations;
            Operations = new Operation[capacity.PlaybackCount];
            Invocations = new OrchestrationTimelineInvocation<TTime>[checked(capacity.PlaybackCount * invocations)];
            Scheduled = new TimelineScheduledInvocation<TTime>[checked(capacity.PlaybackCount * invocations)];
            Boundaries = new TimelineSchedulingBoundary<TTime>[checked(capacity.PlaybackCount * boundaries)];
            SyntheticBoundaries = new TimelineSchedulingBoundary<TTime>[checked(capacity.PlaybackCount * syntheticBoundaries)];
            ActiveTrees = new TimelineSchedulingBoundary<TTime>[checked(capacity.PlaybackCount * capacity.ActiveTreesPerPlayback)];
            EvaluationFirstCycles = new long[checked(capacity.PlaybackCount * capacity.TreeExitWindowsPerPlayback)];
            EvaluationExitBits = new ulong[checked(capacity.PlaybackCount * capacity.TreeExitWordsPerPlayback)];
            ProducedExits = new OrchestrationTimelineClipOccurrence[checked(capacity.PlaybackCount * capacity.TreeExitRequestsPerPlayback)];
            SelectedTrees = new bool[checked(capacity.PlaybackCount * clips)];
            SelectedTreeOrdinals = new int[checked(capacity.PlaybackCount * clips)];
            SelectedTreeCounts = new int[capacity.PlaybackCount];
            m_InvocationFrames = new InvocationFrame[checked(capacity.PlaybackCount + 1)];
        }

        public OrchestrationTimelinePreparedCapacity Capacity { get; }
        internal int ClipCapacity { get; }
        internal int BoundaryCapacity { get; }
        internal int SyntheticBoundaryCapacity { get; }
        internal int InvocationCapacity { get; }
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
