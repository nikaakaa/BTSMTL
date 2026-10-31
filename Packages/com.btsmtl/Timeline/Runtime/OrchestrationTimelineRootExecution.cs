using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public interface IOrchestrationTimelineRootLogicSamples<TValue> where TValue : unmanaged
    {
        void Invoke(OrchestrationCandidateState<TValue> candidate, int playbackSlot,
            in OrchestrationTimelineStart<OrchestrationTime> start, ulong tick,
            OrchestrationTime previousTime, int previousCycle, OrchestrationTime time, int cycle);
    }

    public interface IOrchestrationTimelineRootBindings<TValue, TContext, THost>
        where TValue : unmanaged
        where TContext : struct
        where THost : struct, IOrchestrationHost<TValue>
    {
        void PrepareProgram(TimelineExecutionDomain domain, int programIndex,
            IOrchestrationTimelineClipExitDispatcher<TContext> clipExit);

        THost CreateHost(TimelineExecutionDomain domain, int programIndex, in TContext context,
            OrchestrationWorkbenchDiagnosticStorage<TValue> diagnostics);

        TContext CreateContext(TimelineExecutionDomain domain, int programIndex, int frameSlot,
            in OrchestrationExecutionFrameState frame, OrchestrationTimelineInvocationMetadata? invocation);

        OrchestrationFrameInput<TValue> ReadInput(TimelineExecutionDomain domain, int programIndex,
            ulong tick, in OrchestrationExecutionFrameState frame);

        ReadOnlySpan<int> CharacterValueSlots(int programIndex);

        OrchestrationOperationResult ReleaseInvocation(TimelineExecutionDomain domain,
            in OrchestrationTimelineInvocationMetadata metadata, in OrchestrationTimelineInvocationFacts invocation,
            in OrchestrationExecutionFrameState frame, ref TContext context);

        void ConsumeOutputs(TimelineExecutionDomain domain, int programIndex,
            OrchestrationTimelineInvocationMetadata? metadata, in OrchestrationExecutionFrameState frame,
            in TContext context, ReadOnlySpan<OrchestrationOutputRequest> requests,
            ReadOnlySpan<TValue> values, in OrchestrationTraceContext traceContext);

        bool SupportsPresentationSampleKind(string contractKind);

        Func<TimelineRuntimePreparedContent, OrchestrationTimelinePlaybackMode,
            IOrchestrationTimelineRootLogicSamples<TValue>> LogicSamples { get; }

        void SamplePresentation(in TimelineRuntimePresentationFrame frame);
    }

    public sealed class OrchestrationTimelineRootExecution<TValue, TNumerics, TContext, THost, TBindings> :
        IOrchestrationTimelineClipExitDispatcher<TContext>
        where TValue : unmanaged
        where TNumerics : struct, IOrchestrationNumerics<TValue>
        where TContext : struct
        where THost : struct, IOrchestrationHost<TValue>
        where TBindings : struct, IOrchestrationTimelineRootBindings<TValue, TContext, THost>
    {
        readonly OrchestrationTimelinePreparedRoot m_Root;
        readonly OrchestrationGraphPreparation<TValue, TNumerics> m_Graphs;
        readonly OrchestrationTimelineStateLayout<OrchestrationTimelinePlaybackState<OrchestrationTime>> m_TimelineLayout;
        readonly OrchestrationTimelinePreparedCapacity m_TimelineCapacity;
        readonly OrchestrationTimelineWorkspace<OrchestrationTime> m_Workspace;
        readonly OrchestrationExecutionScratchMemory<TValue>[] m_Scratches;
        readonly OrchestrationWorkbenchDiagnosticStorage<TValue>[] m_Diagnostics;
        readonly Dictionary<(bool Marker, int Ordinal, OrchestrationTimelineHook Hook), OrchestrationTimelineInvocationMetadata> m_Invocations;
        readonly TimelineRuntimePresentationDriver m_Presentation;
        readonly IOrchestrationTimelineRootLogicSamples<TValue> m_LogicSamples;
        readonly int m_SourceIdentityId;
        TBindings m_Bindings;
        TimelineExecutionDomain m_CurrentDomain;
        OrchestrationTimelineInvocationMetadata m_CurrentMetadata;
        OrchestrationTimelineInvocationFacts m_CurrentInvocation;
        OrchestrationExecutionFrameState m_CurrentFrame;
        TimelineRuntimePresentationFrame m_CurrentPresentation;
        TimelineRuntimeTreeClipRequest m_CurrentTree;

        public OrchestrationTimelineRootExecution(OrchestrationTimelinePreparedRoot root,
            OrchestrationGraphPreparation<TValue, TNumerics> graphs,
            in OrchestrationTimelineStateLayout<OrchestrationTimelinePlaybackState<OrchestrationTime>> timelineLayout,
            in OrchestrationTimelinePreparedCapacity timelineCapacity,
            OrchestrationExecutionCapacity executionCapacity, OrchestrationExecutionTickCapacity tickCapacity,
            int sourceIdentityId, TBindings bindings)
        {
            m_Root = root;
            m_Graphs = graphs;
            m_TimelineLayout = timelineLayout;
            m_TimelineCapacity = timelineCapacity;
            m_Bindings = bindings;
            m_SourceIdentityId = sourceIdentityId;
            m_Workspace = new OrchestrationTimelineWorkspace<OrchestrationTime>(
                new[] { root.Logic.Content, root.Presentation.Content }, in timelineCapacity);
            m_Invocations = new Dictionary<(bool, int, OrchestrationTimelineHook), OrchestrationTimelineInvocationMetadata>();
            foreach (ref readonly OrchestrationTimelineInvocationMetadata invocation in root.Entries.Invocations.Span)
                m_Invocations.Add((invocation.Marker, invocation.Ordinal, invocation.Hook), invocation);
            int programs = graphs.Programs.Length;
            m_Scratches = new OrchestrationExecutionScratchMemory<TValue>[programs];
            m_Diagnostics = new OrchestrationWorkbenchDiagnosticStorage<TValue>[programs];
            for (int programIndex = 0; programIndex < programs; programIndex++)
            {
                OrchestrationProgram<TValue> program = graphs.Programs.Span[programIndex];
                m_Scratches[programIndex] = new OrchestrationExecutionScratchMemory<TValue>(
                    OrchestrationExecutionScratchLayout.Prepare(program.View, executionCapacity.State.FrameCount,
                        tickCapacity.Programs.Span[programIndex].CursorOutputs));
                m_Diagnostics[programIndex] = new OrchestrationWorkbenchDiagnosticStorage<TValue>(
                    program, graphs.Graphs.Span[programIndex].Source, executionCapacity.State.FrameCount);
                if (root.Logic.TickBudgets.Span[programIndex].OuterInvocationCount != 0)
                    m_Bindings.PrepareProgram(TimelineExecutionDomain.Logic, programIndex, this);
                if (root.Presentation.TickBudgets.Span[programIndex].OuterInvocationCount != 0)
                    m_Bindings.PrepareProgram(TimelineExecutionDomain.Presentation, programIndex, this);
            }
            bool hasLogicSamples = false;
            foreach (TimelineContentClip clip in root.Prepared.Content.Clips)
            {
                if (clip.TrackMuted || clip.ContentKind != TimelineContentKind.Sampled ||
                    clip.ExecutionPolicy.Domain != TimelineExecutionDomain.Logic)
                    continue;
                hasLogicSamples = true;
            }
            if (hasLogicSamples)
            {
                var prepareLogicSamples = m_Bindings.LogicSamples;
                if (prepareLogicSamples == null)
                    throw new InvalidOperationException($"Timeline '{root.Prepared.Content.Identity}' 声明了逻辑采样片段，必须装配实际逻辑采样服务。");
                m_LogicSamples = prepareLogicSamples(root.Prepared, root.Logic.Content.Mode);
            }
            m_Presentation = new TimelineRuntimePresentationDriver(timelineCapacity.PlaybackCount, timelineCapacity.PlaybackCount);
            m_Presentation.Prepare(root.Prepared, root.Presentation.Content, sourceIdentityId,
                TimelineRuntimePresentationGraphSource.Unexecuted,
                m_Bindings.SupportsPresentationSampleKind);
        }

        OrchestrationTimelineRuntime<OrchestrationTime, OrchestrationTimeClock> Runtime =>
            new OrchestrationTimelineRuntime<OrchestrationTime, OrchestrationTimeClock>(
                m_Root.Logic.Content.View, m_TimelineLayout.Regions, in m_TimelineCapacity, m_Workspace.View);

        public TimelineRuntimePresentationDriver Presentation => m_Presentation;

        public OrchestrationWorkbenchDiagnosticStorage<TValue> Diagnostics(int programIndex) => m_Diagnostics[programIndex];

        public int Start(OrchestrationCandidateState<TValue> candidate, ulong ownerInstanceId,
            ulong sourceGeneration, ulong inputSequence, ulong tick, OrchestrationTime time)
        {
            var runtime = Runtime;
            int slot = runtime.FindFreePlayback(candidate.Timeline);
            var start = new OrchestrationTimelineStart<OrchestrationTime>
            {
                ContentIdentityId = m_Root.Prepared.ContentIdentityId,
                ContentRevisionId = m_Root.Prepared.ContentRevisionId,
                Mode = m_Root.Logic.Content.Mode,
                PlaybackIdentity = runtime.AllocatePlaybackIdentity(candidate.Timeline),
                SourceIdentityId = m_SourceIdentityId,
                SourceGeneration = sourceGeneration,
                OwnerInstanceId = ownerInstanceId,
                InputSequence = inputSequence,
                StartTick = tick,
                StartTime = time
            };
            runtime.Start(candidate.Timeline, slot, in start);
            return slot;
        }

        public OrchestrationTimelinePlaybackState<OrchestrationTime> ReadPlayback(
            OrchestrationCandidateState<TValue> candidate, int slot) => Runtime.Playback(candidate.Timeline, slot);

        public OrchestrationOperationResult AdvanceLogic(OrchestrationCandidateState<TValue> candidate,
            int slot, in OrchestrationTimelineStep<OrchestrationTime> step, in OrchestrationStopContext stopContext)
        {
            var runtime = Runtime;
            OrchestrationTimelineStart<OrchestrationTime> start = runtime.Playback(candidate.Timeline, slot).Start;
            OrchestrationTimelineBatch<OrchestrationTime> batch = runtime.BeginAdvance(candidate.Timeline, slot, step);
            OrchestrationOperationResult result = Dispatch(candidate, in start, batch, in stopContext);
            if (result == OrchestrationOperationResult.Failure)
                return result;
            if (m_LogicSamples != null)
            {
                ref readonly OrchestrationTimelineOperationWork<OrchestrationTime> work = ref m_Workspace.View.Operations[slot];
                m_LogicSamples.Invoke(candidate, slot, in start, step.Tick,
                    work.PreviousTime, work.PreviousCycle, work.Proposed.Progress.CursorTime, work.Proposed.Progress.Cycle);
            }
            runtime.Complete(candidate.Timeline, slot, in batch);
            return OrchestrationOperationResult.Success;
        }

        public OrchestrationOperationResult Stop(OrchestrationCandidateState<TValue> candidate,
            int slot, in OrchestrationTimelineStopContext stop, bool force, in OrchestrationStopContext stopContext)
        {
            var runtime = Runtime;
            OrchestrationTimelineStart<OrchestrationTime> start = runtime.Playback(candidate.Timeline, slot).Start;
            OrchestrationTimelineBatch<OrchestrationTime> batch = runtime.BeginStop(candidate.Timeline, slot, stop, force);
            OrchestrationOperationResult result = Dispatch(candidate, in start, batch, in stopContext);
            if (result == OrchestrationOperationResult.Failure)
                return result;
            runtime.Complete(candidate.Timeline, slot, in batch);
            return OrchestrationOperationResult.Success;
        }

        public bool TryPresent(OrchestrationCandidateState<TValue> candidate,
            in TimelineRuntimePresentationSource source, ulong presentationFrame,
            float deltaSeconds, float interpolationAlpha, in OrchestrationStopContext stopContext,
            out TimelineRuntimePresentationFrame frame)
        {
            if (!m_Presentation.TryPresent(m_Root.Prepared, in source, presentationFrame,
                    deltaSeconds, interpolationAlpha, out frame))
                return false;
            m_CurrentPresentation = frame;
            m_Bindings.SamplePresentation(in frame);
            for (int index = 0; index < frame.Operations.TreeClips.Count; index++)
            {
                TimelineRuntimeTreeClipRequest tree = frame.Operations.TreeClips[index];
                m_CurrentTree = tree;
                OrchestrationTimelineInvocationFacts invocation = m_Root.Entries.CreatePresentationInvocation(in frame, in tree);
                RequireSuccess(Execute(candidate, TimelineExecutionDomain.Presentation, in invocation, in stopContext), in invocation);
            }
            for (int index = 0; index < frame.Events.Count; index++)
            {
                TimelineRuntimePresentationEvent marker = frame.Events[index];
                OrchestrationTimelineInvocationFacts invocation = m_Root.Entries.CreatePresentationInvocation(in frame, in marker);
                RequireSuccess(Execute(candidate, TimelineExecutionDomain.Presentation, in invocation, in stopContext), in invocation);
            }
            if (source.Sample.EndsPlayback)
                ReleaseFrames(candidate, TimelineExecutionDomain.Presentation, in source.Start, source.Sample.LogicTick, in stopContext);
            return true;
        }

        public void ReleasePlayback(OrchestrationCandidateState<TValue> candidate, int slot)
        {
            var runtime = Runtime;
            runtime.ReleasePlayback(candidate.Timeline, runtime.Playback(candidate.Timeline, slot).Start.PlaybackIdentity);
        }

        OrchestrationOperationResult Dispatch(OrchestrationCandidateState<TValue> candidate,
            in OrchestrationTimelineStart<OrchestrationTime> start,
            OrchestrationTimelineBatch<OrchestrationTime> batch, in OrchestrationStopContext stopContext)
        {
            var workspace = m_Workspace.View;
            for (int index = 0; index < batch.Invocations.Length; index++)
            {
                workspace.EnterInvocation(batch.PlaybackSlot, index);
                try
                {
                    OrchestrationTimelineInvocationFacts invocation = OrchestrationTimelineDispatch<TValue, TContext,
                        OrchestrationTime, OrchestrationTimeClock>.ReadInvocationFacts(Runtime);
                    OrchestrationOperationResult result = Execute(candidate, TimelineExecutionDomain.Logic, in invocation, in stopContext);
                    if (result == OrchestrationOperationResult.Failure)
                        return result;
                }
                finally
                {
                    workspace.LeaveInvocation();
                }
            }
            if (batch.Status == OrchestrationTimelinePlaybackStatus.Completed || batch.Status == OrchestrationTimelinePlaybackStatus.Stopped)
                ReleaseFrames(candidate, TimelineExecutionDomain.Logic, in start,
                    m_Workspace.View.Operations[batch.PlaybackSlot].Tick, in stopContext);
            return OrchestrationOperationResult.Success;
        }

        OrchestrationOperationResult Execute(OrchestrationCandidateState<TValue> candidate,
            TimelineExecutionDomain domain, in OrchestrationTimelineInvocationFacts invocation,
            in OrchestrationStopContext stopContext)
        {
            OrchestrationTimelineInvocationToken token = invocation.Invocation.Token;
            OrchestrationTimelineInvocationMetadata metadata = m_Invocations[(token.Marker, token.ClipOrdinal, invocation.Invocation.Hook)];
            int programIndex = metadata.ProgramIndex;
            OrchestrationProgram<TValue> program = m_Graphs.Programs.Span[programIndex];
            OrchestrationExecutionFrames<TValue> frames = candidate.Frames;
            int frameSlot = frames.FindTimeline(in invocation.Start, in token);
            if (frameSlot < 0)
                frameSlot = frames.Insert<THost>(new OrchestrationExecutionFrameState
                {
                    SkillContentId = m_Graphs.PreparedProgramStorages.Span[programIndex].SkillContentId,
                    EntryOperationIndex = metadata.ExecutionFrameOwnerOperationIndex,
                    OwnerOperationIndex = metadata.ExecutionFrameOwnerOperationIndex,
                    ParentFrameIndex = -1,
                    ParentTimelineOperationIndex = -1,
                    ActionInstanceId = invocation.Start.OwnerInstanceId,
                    Generation = invocation.Start.SourceGeneration,
                    TimelineStart = invocation.Start,
                    ClipOrdinal = token.ClipOrdinal,
                    Cycle = token.Cycle,
                    Marker = token.Marker
                }, program.Content.DefaultValues.Span);
            OrchestrationExecutionFrameState header = frames.Header(frameSlot);
            TContext context = m_Bindings.CreateContext(domain, programIndex, frameSlot, in header, metadata);
            OrchestrationFrameInput<TValue> input = m_Bindings.ReadInput(domain, programIndex, invocation.Invocation.Tick, in header);
            THost host = m_Bindings.CreateHost(domain, programIndex, in context, m_Diagnostics[programIndex]);
            var cursor = candidate.CreateCursor<TNumerics, THost>(program.View, m_Scratches[programIndex].View,
                frameSlot, m_Bindings.CharacterValueSlots(programIndex), in input, m_Graphs.Graphs.Span[programIndex].Identities.Ordinals,
                host);
            TimelineExecutionDomain previousDomain = m_CurrentDomain;
            OrchestrationTimelineInvocationMetadata previousMetadata = m_CurrentMetadata;
            OrchestrationTimelineInvocationFacts previousInvocation = m_CurrentInvocation;
            OrchestrationExecutionFrameState previousFrame = m_CurrentFrame;
            m_CurrentDomain = domain;
            m_CurrentMetadata = metadata;
            m_CurrentInvocation = invocation;
            m_CurrentFrame = header;
            try
            {
                TValue time = default(TNumerics).FromTime(invocation.Invocation.Time);
                OrchestrationOperationResult result = cursor.AdvanceTimeline(in invocation, in time, in stopContext,
                    input.HasDiagnostics, out OrchestrationTraceContext traceContext);
                if (invocation.EndsExecutionFrame && !token.Marker &&
                    m_Bindings.ReleaseInvocation(domain, in metadata, in invocation, in header, ref context) != OrchestrationOperationResult.Success)
                    result = OrchestrationOperationResult.Failure;
                Consume(domain, programIndex, metadata, in header, in context, in traceContext);
                return result;
            }
            finally
            {
                m_CurrentDomain = previousDomain;
                m_CurrentMetadata = previousMetadata;
                m_CurrentInvocation = previousInvocation;
                m_CurrentFrame = previousFrame;
            }
        }

        void ReleaseFrames(OrchestrationCandidateState<TValue> candidate, TimelineExecutionDomain domain,
            in OrchestrationTimelineStart<OrchestrationTime> start, ulong tick, in OrchestrationStopContext stopContext)
        {
            OrchestrationExecutionFrames<TValue> frames = candidate.Frames;
            for (int frameSlot = 0; frameSlot < frames.Capacity; frameSlot++)
            {
                OrchestrationExecutionFrameState header = frames.Header(frameSlot);
                if (header.SkillContentId == 0 || !header.TimelineStart.Equals(start))
                    continue;
                TimelineExecutionDomain frameDomain = header.Marker
                    ? m_Root.Prepared.Content.Markers[header.ClipOrdinal].ExecutionPolicy.Domain
                    : m_Root.Prepared.Content.Clips[header.ClipOrdinal].ExecutionPolicy.Domain;
                if (frameDomain != domain)
                    continue;
                int programIndex = frames.Plan.ProgramIndex(header.SkillContentId);
                TContext context = m_Bindings.CreateContext(domain, programIndex, frameSlot, in header, null);
                OrchestrationFrameInput<TValue> input = m_Bindings.ReadInput(domain, programIndex, tick, in header);
                THost host = m_Bindings.CreateHost(domain, programIndex, in context, m_Diagnostics[programIndex]);
                var cursor = candidate.CreateCursor<TNumerics, THost>(m_Graphs.Programs.Span[programIndex].View,
                    m_Scratches[programIndex].View, frameSlot, m_Bindings.CharacterValueSlots(programIndex), in input,
                    m_Graphs.Graphs.Span[programIndex].Identities.Ordinals, host);
                cursor.ReleaseExecutionFrame(frameSlot, in stopContext);
                Consume(domain, programIndex, null, in header, in context, default);
            }
        }

        void Consume(TimelineExecutionDomain domain, int programIndex,
            OrchestrationTimelineInvocationMetadata? metadata, in OrchestrationExecutionFrameState frame,
            in TContext context, in OrchestrationTraceContext traceContext)
        {
            OrchestrationExecutionScratch<TValue> scratch = m_Scratches[programIndex].View;
            m_Bindings.ConsumeOutputs(domain, programIndex, metadata, in frame, in context,
                scratch.EmittedOutputRequests, scratch.EmittedOutputValues, in traceContext);
        }

        OrchestrationOperationResult IOrchestrationTimelineClipExitDispatcher<TContext>.RequestClipExit(
            OrchestrationStateView candidate, int operationIndex, ulong tick, ref TContext context)
        {
            if (m_CurrentDomain == TimelineExecutionDomain.Logic)
            {
                OrchestrationTimelineInvocationToken token = m_CurrentInvocation.Invocation.Token;
                Runtime.TreeClipExit(candidate, in token);
            }
            else
                m_Presentation.RequestTreeClipExit(in m_CurrentPresentation, in m_CurrentTree);
            return m_Bindings.ReleaseInvocation(m_CurrentDomain, in m_CurrentMetadata,
                in m_CurrentInvocation, in m_CurrentFrame, ref context);
        }

        static void RequireSuccess(OrchestrationOperationResult result, in OrchestrationTimelineInvocationFacts invocation)
        {
            if (result == OrchestrationOperationResult.Failure)
                throw new InvalidOperationException($"独立 Timeline 图入口 {invocation.Invocation.Entry.OperationIndex} 的 {invocation.Invocation.Hook} 返回失败。");
        }
    }
}
