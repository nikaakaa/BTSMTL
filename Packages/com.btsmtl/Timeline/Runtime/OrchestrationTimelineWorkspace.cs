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
            OrchestrationExecutionCapacity executionCapacity, int rootPlaybackCount)
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
            return new OrchestrationTimelinePreparedCapacity(checked(executionCapacity.PlaybackCount + rootPlaybackCount),
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

        // 准备期核对内容所需区段不超过已准备容量；推进期不再检查。
        public void Require<TTime>(OrchestrationTimelinePreparedContent<TTime> content)
            where TTime : unmanaged, IEquatable<TTime>
        {
            Require(content.ContentIdentityId, "ActiveTrees scratch", content.RequiredActiveTrees, ActiveTreesPerPlayback);
            Require(content.ContentIdentityId, "TreeExitRequests", content.RequiredTreeExitRequests, TreeExitRequestsPerPlayback);
            Require(content.ContentIdentityId, "TreeExitFirstCycles", content.RequiredTreeExitWindows, TreeExitWindowsPerPlayback);
            Require(content.ContentIdentityId, "TreeExitBits", content.RequiredTreeExitWords, TreeExitWordsPerPlayback);
        }

        static void Require(int content, string region, int required, int available)
        {
            if (required > available)
                throw new InvalidOperationException($"Timeline '{content}' region '{region}' requires '{required}' elements; prepared capacity is '{available}'.");
        }
    }

    internal struct OrchestrationTimelineBoundarySections
    {
        internal OrchestrationMemorySection Starts;
        internal OrchestrationMemorySection Ends;
        internal OrchestrationMemorySection StableOrder;
        internal OrchestrationMemorySection Decisions;
        internal OrchestrationMemorySection MinStarts;
        internal OrchestrationMemorySection MaxEnds;
        internal OrchestrationMemorySection MaxLifetimeEnds;
        internal OrchestrationMemorySection StartMaxLifetimeEnds;
    }

    internal struct OrchestrationTimelineContentHeader<TTime> where TTime : unmanaged
    {
        internal int ContentIdentityId;
        internal int ContentRevisionId;
        internal TTime Duration;
        internal int TickRate;
        internal OrchestrationTimelinePlaybackMode Mode;
        internal TimelineExecutionDomain Domain;
        internal int RequiredActiveTrees;
        internal int RequiredTreeExitRequests;
        internal int RequiredTreeExitWindows;
        internal int RequiredTreeExitWords;
        internal OrchestrationMemorySection Clips;
        internal OrchestrationMemorySection TreeOrdinals;
        internal OrchestrationMemorySection Markers;
        internal OrchestrationMemorySection ClipEntries;
        internal OrchestrationMemorySection MarkerEntries;
        internal OrchestrationMemorySection TreeCycleWindows;
        internal OrchestrationTimelineBoundarySections Boundaries;
        internal OrchestrationTimelineBoundarySections Lifetimes;
    }

    public sealed class OrchestrationTimelinePreparedContent<TTime>
        where TTime : unmanaged, IEquatable<TTime>
    {
        readonly byte[] m_Image;

        internal OrchestrationTimelinePreparedContent(int contentIdentityId, int contentRevisionId,
            TTime duration, int tickRate, OrchestrationTimelinePlaybackMode mode,
            TimelineExecutionDomain domain,
            ReadOnlyMemory<TimelineSchedulingClip<TTime>> clips,
            TimelineRuntimeBoundaryIndex<TTime> boundaries,
            TimelineRuntimeBoundaryIndex<TTime> lifetimes,
            ReadOnlyMemory<int> treeOrdinals,
            ReadOnlyMemory<TimelineSchedulingMarker<TTime>> markers,
            ReadOnlyMemory<OrchestrationTimelineEntryBinding> clipEntries,
            ReadOnlyMemory<OrchestrationExecutionEntry> markerEntries,
            int requiredActiveTrees, int requiredTreeExitRequests,
            ReadOnlyMemory<OrchestrationTimelineTreeCycleWindow> treeCycleWindows,
            int requiredTreeExitWords,
            ReadOnlyMemory<OrchestrationOccurrenceCapacity> occurrences,
            in OrchestrationTimelineContentTickCapacity tickCapacity)
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
            ClipEntries = clipEntries;
            MarkerEntries = markerEntries;
            RequiredActiveTrees = requiredActiveTrees;
            RequiredTreeExitRequests = requiredTreeExitRequests;
            TreeCycleWindows = treeCycleWindows;
            RequiredTreeExitWindows = boundaries.Decisions.Length;
            RequiredTreeExitWords = requiredTreeExitWords;
            Occurrences = occurrences;
            TickCapacity = tickCapacity;
            m_Image = BuildImage();
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
        public ReadOnlyMemory<OrchestrationTimelineEntryBinding> ClipEntries { get; }
        public ReadOnlyMemory<OrchestrationExecutionEntry> MarkerEntries { get; }
        public int RequiredActiveTrees { get; }
        public int RequiredTreeExitRequests { get; }
        public ReadOnlyMemory<OrchestrationTimelineTreeCycleWindow> TreeCycleWindows { get; }
        public int RequiredTreeExitWindows { get; }
        public int RequiredTreeExitWords { get; }
        public ReadOnlyMemory<OrchestrationOccurrenceCapacity> Occurrences { get; }
        public OrchestrationTimelineContentTickCapacity TickCapacity { get; }
        // 一次推进的遍历段数：循环按可跨圈数，否则一段。
        public int Traversals => Mode == OrchestrationTimelinePlaybackMode.Loop
            ? OrchestrationTimelineLimits.MaximumCycleAdvance + 1 : 1;
        // 推进只经此镜像读取内容；Burst 宿主把同一份字节放进原生内存。
        public ReadOnlyMemory<byte> Image => m_Image;
        public OrchestrationTimelineContentView<TTime> View => new OrchestrationTimelineContentView<TTime>(m_Image);

        byte[] BuildImage()
        {
            var writer = new OrchestrationMemoryImageWriter(
                OrchestrationMemoryImageWriter.SizeOf<OrchestrationTimelineContentHeader<TTime>>());
            var header = new OrchestrationTimelineContentHeader<TTime>
            {
                ContentIdentityId = ContentIdentityId,
                ContentRevisionId = ContentRevisionId,
                Duration = Duration,
                TickRate = TickRate,
                Mode = Mode,
                Domain = Domain,
                RequiredActiveTrees = RequiredActiveTrees,
                RequiredTreeExitRequests = RequiredTreeExitRequests,
                RequiredTreeExitWindows = RequiredTreeExitWindows,
                RequiredTreeExitWords = RequiredTreeExitWords
            };
            header.Clips = writer.Add(Clips.Span);
            header.TreeOrdinals = writer.Add(TreeOrdinals.Span);
            header.Markers = writer.Add(Markers.Span);
            header.ClipEntries = writer.Add(ClipEntries.Span);
            header.MarkerEntries = writer.Add(MarkerEntries.Span);
            header.TreeCycleWindows = writer.Add(TreeCycleWindows.Span);
            header.Boundaries = AddBoundaries(writer, Boundaries);
            header.Lifetimes = AddBoundaries(writer, Lifetimes);
            return writer.Finish(in header);
        }

        static OrchestrationTimelineBoundarySections AddBoundaries(OrchestrationMemoryImageWriter writer,
            TimelineRuntimeBoundaryIndex<TTime> index) => new OrchestrationTimelineBoundarySections
        {
            Starts = writer.Add(index.Starts.Span),
            Ends = writer.Add(index.Ends.Span),
            StableOrder = writer.Add(index.StableOrder.Span),
            Decisions = writer.Add(index.Decisions.Span),
            MinStarts = writer.Add<TTime>(index.MinStarts),
            MaxEnds = writer.Add<TTime>(index.MaxEnds),
            MaxLifetimeEnds = writer.Add<TTime>(index.MaxLifetimeEnds),
            StartMaxLifetimeEnds = writer.Add<TTime>(index.StartMaxLifetimeEnds)
        };
    }

    public readonly ref struct OrchestrationTimelineContentView<TTime> where TTime : unmanaged, IEquatable<TTime>
    {
        readonly ReadOnlySpan<byte> m_Memory;

        public OrchestrationTimelineContentView(ReadOnlySpan<byte> image) => m_Memory = image;

        ref readonly OrchestrationTimelineContentHeader<TTime> Header =>
            ref OrchestrationMemoryImageWriter.Header<OrchestrationTimelineContentHeader<TTime>>(m_Memory);

        ReadOnlySpan<T> Section<T>(OrchestrationMemorySection section) where T : unmanaged =>
            OrchestrationMemoryImageWriter.Section<T>(m_Memory, section);

        public int ContentIdentityId => Header.ContentIdentityId;
        public int ContentRevisionId => Header.ContentRevisionId;
        public TTime Duration => Header.Duration;
        public int TickRate => Header.TickRate;
        public OrchestrationTimelinePlaybackMode Mode => Header.Mode;
        public TimelineExecutionDomain Domain => Header.Domain;
        public int RequiredActiveTrees => Header.RequiredActiveTrees;
        public int RequiredTreeExitRequests => Header.RequiredTreeExitRequests;
        public int RequiredTreeExitWindows => Header.RequiredTreeExitWindows;
        public int RequiredTreeExitWords => Header.RequiredTreeExitWords;
        public ReadOnlySpan<TimelineSchedulingClip<TTime>> Clips => Section<TimelineSchedulingClip<TTime>>(Header.Clips);
        public ReadOnlySpan<int> TreeOrdinals => Section<int>(Header.TreeOrdinals);
        public ReadOnlySpan<TimelineSchedulingMarker<TTime>> Markers => Section<TimelineSchedulingMarker<TTime>>(Header.Markers);
        public ReadOnlySpan<OrchestrationTimelineEntryBinding> ClipEntries =>
            Section<OrchestrationTimelineEntryBinding>(Header.ClipEntries);
        public ReadOnlySpan<OrchestrationExecutionEntry> MarkerEntries =>
            Section<OrchestrationExecutionEntry>(Header.MarkerEntries);
        public ReadOnlySpan<OrchestrationTimelineTreeCycleWindow> TreeCycleWindows =>
            Section<OrchestrationTimelineTreeCycleWindow>(Header.TreeCycleWindows);
        public TimelineRuntimeBoundaryIndexView<TTime> Boundaries => Index(in Header.Boundaries);
        public TimelineRuntimeBoundaryIndexView<TTime> Lifetimes => Index(in Header.Lifetimes);

        public bool Matches(in OrchestrationTimelineStart<TTime> start) =>
            ContentIdentityId == start.ContentIdentityId &&
            ContentRevisionId == start.ContentRevisionId && Mode == start.Mode;

        TimelineRuntimeBoundaryIndexView<TTime> Index(in OrchestrationTimelineBoundarySections sections) =>
            new TimelineRuntimeBoundaryIndexView<TTime>(Clips, Section<int>(sections.Starts),
                Section<int>(sections.Ends), Section<int>(sections.StableOrder), Section<int>(sections.Decisions),
                Section<TTime>(sections.MinStarts), Section<TTime>(sections.MaxEnds),
                Section<TTime>(sections.MaxLifetimeEnds), Section<TTime>(sections.StartMaxLifetimeEnds));
    }

    internal struct OrchestrationTimelineOperationWork<TTime> where TTime : unmanaged, IEquatable<TTime>
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

    internal struct OrchestrationTimelineInvocationFrame
    {
        internal int PlaybackSlot;
        internal int InvocationIndex;
    }

    internal struct OrchestrationTimelineWorkspaceHeader
    {
        internal OrchestrationTimelinePreparedCapacity Capacity;
        internal int ClipCapacity;
        internal int BoundaryCapacity;
        internal int SyntheticBoundaryCapacity;
        internal int InvocationCapacity;
        internal int InvocationDepth;
        internal ulong BatchSequence;
        internal OrchestrationMemorySection Operations;
        internal OrchestrationMemorySection Invocations;
        internal OrchestrationMemorySection Scheduled;
        internal OrchestrationMemorySection Boundaries;
        internal OrchestrationMemorySection SyntheticBoundaries;
        internal OrchestrationMemorySection ActiveTrees;
        internal OrchestrationMemorySection EvaluationFirstCycles;
        internal OrchestrationMemorySection EvaluationExitBits;
        internal OrchestrationMemorySection ProducedExits;
        internal OrchestrationMemorySection SelectedTrees;
        internal OrchestrationMemorySection SelectedTreeOrdinals;
        internal OrchestrationMemorySection SelectedTreeCounts;
        internal OrchestrationMemorySection InvocationFrames;
    }

    // Timeline 推进草稿：准备期按内容与容量算好布局；托管宿主持有数组，Burst 宿主可用同一布局的原生内存。
    public sealed class OrchestrationTimelineWorkspace<TTime>
        where TTime : unmanaged, IEquatable<TTime>
    {
        readonly byte[] m_Memory;

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
            int playbacks = capacity.PlaybackCount;
            var builder = new OrchestrationMemoryLayoutBuilder(
                OrchestrationMemoryImageWriter.SizeOf<OrchestrationTimelineWorkspaceHeader>());
            var header = new OrchestrationTimelineWorkspaceHeader
            {
                Capacity = capacity,
                ClipCapacity = clips,
                BoundaryCapacity = boundaries,
                SyntheticBoundaryCapacity = syntheticBoundaries,
                InvocationCapacity = invocations,
                Operations = builder.Add<OrchestrationTimelineOperationWork<TTime>>(playbacks),
                Invocations = builder.Add<OrchestrationTimelineInvocation<TTime>>(checked(playbacks * invocations)),
                Scheduled = builder.Add<TimelineScheduledInvocation<TTime>>(checked(playbacks * invocations)),
                Boundaries = builder.Add<TimelineSchedulingBoundary<TTime>>(checked(playbacks * boundaries)),
                SyntheticBoundaries = builder.Add<TimelineSchedulingBoundary<TTime>>(checked(playbacks * syntheticBoundaries)),
                ActiveTrees = builder.Add<TimelineSchedulingBoundary<TTime>>(checked(playbacks * capacity.ActiveTreesPerPlayback)),
                EvaluationFirstCycles = builder.Add<long>(checked(playbacks * capacity.TreeExitWindowsPerPlayback)),
                EvaluationExitBits = builder.Add<ulong>(checked(playbacks * capacity.TreeExitWordsPerPlayback)),
                ProducedExits = builder.Add<OrchestrationTimelineClipOccurrence>(checked(playbacks * capacity.TreeExitRequestsPerPlayback)),
                SelectedTrees = builder.Add<bool>(checked(playbacks * clips)),
                SelectedTreeOrdinals = builder.Add<int>(checked(playbacks * clips)),
                SelectedTreeCounts = builder.Add<int>(playbacks),
                InvocationFrames = builder.Add<OrchestrationTimelineInvocationFrame>(checked(playbacks + 1))
            };
            m_Memory = new byte[builder.SizeBytes];
            OrchestrationMemoryImageWriter.Header<OrchestrationTimelineWorkspaceHeader>(m_Memory) = header;
        }

        public OrchestrationTimelinePreparedCapacity Capacity { get; }
        public Span<byte> Memory => m_Memory;
        public OrchestrationTimelineWorkspaceView<TTime> View => new OrchestrationTimelineWorkspaceView<TTime>(m_Memory);
        public bool HasCurrentInvocation => View.HasCurrentInvocation;
    }

    public readonly ref struct OrchestrationTimelineWorkspaceView<TTime>
        where TTime : unmanaged, IEquatable<TTime>
    {
        readonly Span<byte> m_Memory;

        public OrchestrationTimelineWorkspaceView(Span<byte> memory) => m_Memory = memory;

        ref OrchestrationTimelineWorkspaceHeader Header =>
            ref OrchestrationMemoryImageWriter.Header<OrchestrationTimelineWorkspaceHeader>(m_Memory);

        Span<T> Section<T>(OrchestrationMemorySection section) where T : unmanaged =>
            OrchestrationMemoryImageWriter.Section<T>(m_Memory, section);

        public OrchestrationTimelinePreparedCapacity Capacity => Header.Capacity;
        internal int ClipCapacity => Header.ClipCapacity;
        internal int BoundaryCapacity => Header.BoundaryCapacity;
        internal int SyntheticBoundaryCapacity => Header.SyntheticBoundaryCapacity;
        internal int InvocationCapacity => Header.InvocationCapacity;
        internal Span<OrchestrationTimelineOperationWork<TTime>> Operations =>
            Section<OrchestrationTimelineOperationWork<TTime>>(Header.Operations);
        internal Span<OrchestrationTimelineInvocation<TTime>> Invocations =>
            Section<OrchestrationTimelineInvocation<TTime>>(Header.Invocations);
        internal Span<TimelineScheduledInvocation<TTime>> Scheduled =>
            Section<TimelineScheduledInvocation<TTime>>(Header.Scheduled);
        internal Span<TimelineSchedulingBoundary<TTime>> Boundaries =>
            Section<TimelineSchedulingBoundary<TTime>>(Header.Boundaries);
        internal Span<TimelineSchedulingBoundary<TTime>> SyntheticBoundaries =>
            Section<TimelineSchedulingBoundary<TTime>>(Header.SyntheticBoundaries);
        internal Span<TimelineSchedulingBoundary<TTime>> ActiveTrees =>
            Section<TimelineSchedulingBoundary<TTime>>(Header.ActiveTrees);
        internal Span<long> EvaluationFirstCycles => Section<long>(Header.EvaluationFirstCycles);
        internal Span<ulong> EvaluationExitBits => Section<ulong>(Header.EvaluationExitBits);
        internal Span<OrchestrationTimelineClipOccurrence> ProducedExits =>
            Section<OrchestrationTimelineClipOccurrence>(Header.ProducedExits);
        internal Span<bool> SelectedTrees => Section<bool>(Header.SelectedTrees);
        internal Span<int> SelectedTreeOrdinals => Section<int>(Header.SelectedTreeOrdinals);
        internal Span<int> SelectedTreeCounts => Section<int>(Header.SelectedTreeCounts);

        internal ulong NextBatchSequence() => checked(++Header.BatchSequence);

        public bool HasCurrentInvocation => Header.InvocationDepth > 0;

        public int InvocationDepth => Header.InvocationDepth;

        public ref readonly OrchestrationTimelineInvocation<TTime> CurrentInvocation =>
            ref InvocationAt(Header.InvocationDepth - 1);

        public ref readonly OrchestrationTimelineInvocation<TTime> InvocationAt(int stackIndex)
        {
            OrchestrationTimelineInvocationFrame frame =
                Section<OrchestrationTimelineInvocationFrame>(Header.InvocationFrames)[stackIndex];
            return ref Invocations[checked(frame.PlaybackSlot * InvocationCapacity + frame.InvocationIndex)];
        }

        public ref readonly OrchestrationTimelineStart<TTime> CurrentStart =>
            ref Operations[CurrentInvocation.Token.PlaybackSlot].Proposed.Start;

        public TTime PreviousTime => Operations[CurrentInvocation.Token.PlaybackSlot].PreviousTime;

        public int PreviousCycle => Operations[CurrentInvocation.Token.PlaybackSlot].PreviousCycle;

        internal void EnterInvocation(int playbackSlot, int invocationIndex)
        {
            ref OrchestrationTimelineWorkspaceHeader header = ref Header;
            Section<OrchestrationTimelineInvocationFrame>(header.InvocationFrames)[header.InvocationDepth++] =
                new OrchestrationTimelineInvocationFrame
                {
                    PlaybackSlot = playbackSlot,
                    InvocationIndex = invocationIndex
                };
        }

        internal void LeaveInvocation() => Header.InvocationDepth--;
    }
}
