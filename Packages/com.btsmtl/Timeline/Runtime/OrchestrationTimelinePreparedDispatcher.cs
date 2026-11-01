using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    // Timeline 逻辑派发核心：只经运行视图与结构体插头，托管宿主与 Burst 宿主共用同一份代码。
    public static class OrchestrationTimelineDispatch<TValue, TContext, TTime, TClock>
        where TValue : unmanaged
        where TContext : struct
        where TTime : unmanaged, IEquatable<TTime>
        where TClock : struct, IOrchestrationTimelineClock<TTime>
    {
        public static OrchestrationOperationResult Tick<TNumerics, THost, TBindings, TSamples>(
            OrchestrationTimelineRuntime<TTime, TClock> runtime, ref TBindings bindings, ref TSamples samples,
            ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, int contentIdentityId,
            ulong operationGeneration, ref ulong playbackIdentity, ReadOnlySpan<TValue> inputs,
            ulong tick, ref TContext context)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
            where TBindings : struct, IOrchestrationTimelineBindings<TTime, TValue, TContext>
            where TSamples : struct, ITimelineRuntimeLogicSamples<TValue, TContext>
        {
            int slot;
            if (playbackIdentity == 0)
            {
                OrchestrationTimelineStart<TTime> start = bindings.Start(operationIndex, contentIdentityId,
                    runtime.AllocatePlaybackIdentity(candidate), operationGeneration, tick, inputs, ref context);
                start.CallerPlaybackIdentity = cursor.ReadExecutionFrame(operationIndex)[0].TimelineStart.PlaybackIdentity;
                slot = runtime.FindFreePlayback(candidate);
                runtime.Start(candidate, slot, in start);
                playbackIdentity = start.PlaybackIdentity;
            }
            else
                slot = runtime.FindPlayback(candidate, playbackIdentity);
            ref OrchestrationTimelinePlaybackState<TTime> playback = ref runtime.Playback(candidate, slot);
            OrchestrationTimelineStep<TTime> step = bindings.Step(operationIndex,
                in playback.Start, tick, inputs, ref context);
            OrchestrationTimelineBatch<TTime> batch = runtime.BeginAdvance(candidate, slot, step);
            OrchestrationStopContext stopContext = OrchestrationStopContext.ParentStop(operationIndex);
            OrchestrationTimelineStart<OrchestrationTime> startIdentity = playback.Start.WithStartTime(
                OrchestrationTime.FromRaw(default(TClock).Raw(playback.Start.StartTime)));
            if (!ExecuteBatch(runtime, ref bindings, in batch, in startIdentity, in stopContext, ref context))
                return OrchestrationOperationResult.Failure;
            ref readonly OrchestrationTimelineOperationWork<TTime> work = ref runtime.Workspace.Operations[slot];
            if (runtime.Content.Domain == TimelineExecutionDomain.Logic)
                samples.Invoke(operationIndex, operationGeneration, tick, slot, playbackIdentity,
                    OrchestrationTime.FromRaw(default(TClock).Raw(work.PreviousTime)), work.PreviousCycle,
                    OrchestrationTime.FromRaw(default(TClock).Raw(work.Proposed.Progress.CursorTime)),
                    work.Proposed.Progress.Cycle, cursor.StateAncestors(operationIndex),
                    cursor.ReadExecutionFrame(operationIndex), ref context);
            runtime.Complete(candidate, slot, in batch);
            if (batch.Status == OrchestrationTimelinePlaybackStatus.Completed)
            {
                bindings.AcceptTerminal(operationIndex, slot, step.Tick, in playback, default, ref context);
                ReleasePlayback(runtime, candidate, ref playbackIdentity);
            }
            return Result(batch.Status);
        }

        public static OrchestrationStopStatus Stop<TNumerics, THost, TBindings>(
            OrchestrationTimelineRuntime<TTime, TClock> runtime, ref TBindings bindings,
            ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, ref ulong playbackIdentity,
            in OrchestrationStopContext stopContext, bool force, ulong tick, ref TContext context)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
            where TBindings : struct, IOrchestrationTimelineBindings<TTime, TValue, TContext>
        {
            int slot = runtime.FindPlayback(candidate, playbackIdentity);
            ref OrchestrationTimelinePlaybackState<TTime> playback = ref runtime.Playback(candidate, slot);
            var stop = new OrchestrationTimelineStopContext
            {
                Cause = stopContext.Cause,
                Tick = tick,
                SourceIdentityId = playback.Start.SourceIdentityId
            };
            OrchestrationTimelineBatch<TTime> batch = runtime.BeginStop(candidate, slot, stop, force);
            OrchestrationTimelineStart<OrchestrationTime> startIdentity = playback.Start.WithStartTime(
                OrchestrationTime.FromRaw(default(TClock).Raw(playback.Start.StartTime)));
            if (!ExecuteBatch(runtime, ref bindings, in batch, in startIdentity, in stopContext, ref context))
                return OrchestrationStopStatus.Failed;
            runtime.Complete(candidate, slot, in batch);
            bindings.AcceptTerminal(operationIndex, slot, tick, in playback, in stopContext, ref context);
            ReleasePlayback(runtime, candidate, ref playbackIdentity);
            return OrchestrationStopStatus.Completed;
        }

        public static OrchestrationTime ReadTime(OrchestrationTimelineRuntime<TTime, TClock> runtime) =>
            OrchestrationTime.FromRaw(default(TClock).Raw(runtime.Workspace.CurrentInvocation.Time));

        public static void ReleasePlayback(OrchestrationTimelineRuntime<TTime, TClock> runtime,
            OrchestrationStateView candidate, ref ulong playbackIdentity)
        {
            runtime.ReleasePlayback(candidate, playbackIdentity);
            playbackIdentity = 0;
        }

        public static OrchestrationOperationResult RequestClipExit<TBindings>(
            OrchestrationTimelineRuntime<TTime, TClock> runtime, ref TBindings bindings,
            OrchestrationStateView candidate, ref TContext context)
            where TBindings : struct, IOrchestrationTimelineBindings<TTime, TValue, TContext>
        {
            OrchestrationTimelineInvocationToken token = runtime.Workspace.CurrentInvocation.Token;
            _ = runtime.TreeClipExit(candidate, in token);
            return ReleaseInvocation(runtime, ref bindings, ref context);
        }

        static bool ExecuteBatch<TBindings>(OrchestrationTimelineRuntime<TTime, TClock> runtime,
            ref TBindings bindings, in OrchestrationTimelineBatch<TTime> batch,
            in OrchestrationTimelineStart<OrchestrationTime> start, in OrchestrationStopContext stopContext,
            ref TContext context)
            where TBindings : struct, IOrchestrationTimelineBindings<TTime, TValue, TContext>
        {
            for (int index = 0; index < batch.Invocations.Length; index++)
            {
                runtime.Workspace.EnterInvocation(batch.PlaybackSlot, index);
                try
                {
                    OrchestrationTimelineInvocationFacts invocation = ReadInvocationFacts(runtime);
                    if (bindings.ExecuteInvocation(in invocation, in stopContext, ref context) == OrchestrationOperationResult.Failure)
                        return false;
                }
                finally
                {
                    runtime.Workspace.LeaveInvocation();
                }
            }
            if (batch.Status is OrchestrationTimelinePlaybackStatus.Completed or OrchestrationTimelinePlaybackStatus.Stopped)
                bindings.ReleasePlaybackFrames(in start, in stopContext, ref context);
            return true;
        }

        public static OrchestrationTimelineInvocationFacts ReadInvocationFacts(OrchestrationTimelineRuntime<TTime, TClock> runtime)
        {
            OrchestrationTimelineWorkspaceView<TTime> workspace = runtime.Workspace;
            ref readonly OrchestrationTimelineInvocation<TTime> current = ref workspace.CurrentInvocation;
            ref readonly OrchestrationTimelineOperationWork<TTime> work =
                ref workspace.Operations[current.Token.PlaybackSlot];
            TClock clock = default;
            var invocation = new OrchestrationTimelineInvocation<OrchestrationTime>(current.Token,
                current.Entry, current.Hook, current.Tick, current.SourceIdentityId,
                OrchestrationTime.FromRaw(clock.Raw(current.Time)),
                OrchestrationTime.FromRaw(clock.Raw(current.PreviousTime)),
                OrchestrationTime.FromRaw(clock.Raw(current.WindowStartTime)),
                OrchestrationTime.FromRaw(clock.Raw(current.BodyStartFraction)),
                OrchestrationTime.FromRaw(clock.Raw(current.BodyEndFraction)));
            OrchestrationTimelineStart<OrchestrationTime> start = work.Proposed.Start.WithStartTime(
                OrchestrationTime.FromRaw(clock.Raw(work.Proposed.Start.StartTime)));
            return new OrchestrationTimelineInvocationFacts(in invocation, in start,
                OrchestrationTime.FromRaw(clock.Raw(work.PreviousTime)), work.PreviousCycle,
                OrchestrationTime.FromRaw(clock.Raw(work.Proposed.Progress.CursorTime)), work.Proposed.Progress.Cycle);
        }

        public static OrchestrationOperationResult ReleaseInvocation<TBindings>(
            OrchestrationTimelineRuntime<TTime, TClock> runtime, ref TBindings bindings, ref TContext context)
            where TBindings : struct, IOrchestrationTimelineBindings<TTime, TValue, TContext>
        {
            OrchestrationTimelineWorkspaceView<TTime> workspace = runtime.Workspace;
            return bindings.ReleaseInvocation(in workspace.CurrentStart, in workspace.CurrentInvocation, ref context);
        }

        static OrchestrationOperationResult Result(OrchestrationTimelinePlaybackStatus status)
        {
            switch (status)
            {
                case OrchestrationTimelinePlaybackStatus.Running:
                case OrchestrationTimelinePlaybackStatus.Stopping:
                    return OrchestrationOperationResult.Running;
                case OrchestrationTimelinePlaybackStatus.Completed:
                    return OrchestrationOperationResult.Success;
                case OrchestrationTimelinePlaybackStatus.Stopped:
                case OrchestrationTimelinePlaybackStatus.Failed:
                    return OrchestrationOperationResult.Failure;
                default:
                    throw new InvalidOperationException("Timeline returned an invalid execution state.");
            }
        }
    }

    // 普通插头：托管宿主持有内容、草稿与绑定对象，派发转入同一核心。
    public sealed class OrchestrationTimelinePreparedDispatcher<TValue, TContext, TTime, TClock> :
        IOrchestrationTimelineDispatcher<TValue, TContext>
        where TValue : unmanaged
        where TContext : struct
        where TTime : unmanaged, IEquatable<TTime>
        where TClock : struct, IOrchestrationTimelineClock<TTime>
    {
        readonly OrchestrationTimelinePreparedContent<TTime> m_Content;
        readonly OrchestrationTimelineStateLayout<OrchestrationTimelinePlaybackState<TTime>> m_Layout;
        readonly OrchestrationTimelinePreparedCapacity m_Capacity;
        readonly OrchestrationTimelineWorkspace<TTime> m_Workspace;
        ManagedOrchestrationTimelineBindings<TTime, TValue, TContext> m_Bindings;
        ManagedTimelineLogicSamples<TValue, TContext> m_Samples;

        public OrchestrationTimelinePreparedDispatcher(OrchestrationTimelinePreparedContent<TTime> content,
            in OrchestrationTimelineStateLayout<OrchestrationTimelinePlaybackState<TTime>> layout,
            in OrchestrationTimelinePreparedCapacity capacity,
            OrchestrationTimelineWorkspace<TTime> workspace, IOrchestrationTimelineBindings<TTime, TValue, TContext> bindings,
            ITimelineRuntimeLogicSamples<TValue, TContext> samples)
        {
            capacity.Require(content);
            if (content.Domain == TimelineExecutionDomain.Logic && samples == null)
                throw new ArgumentException("Logic Timeline 必须装配正式逻辑样本服务。", nameof(samples));
            m_Content = content;
            m_Layout = layout;
            m_Capacity = capacity;
            m_Workspace = workspace;
            m_Bindings = new ManagedOrchestrationTimelineBindings<TTime, TValue, TContext>(bindings);
            m_Samples = new ManagedTimelineLogicSamples<TValue, TContext>(samples);
        }

        OrchestrationTimelineRuntime<TTime, TClock> Runtime =>
            new OrchestrationTimelineRuntime<TTime, TClock>(m_Content.View, m_Layout.Regions, in m_Capacity, m_Workspace.View);

        public OrchestrationOperationResult Tick<TNumerics, THost>(
            ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, int contentIdentityId,
            ulong operationGeneration, ref ulong playbackIdentity, ReadOnlySpan<TValue> inputs,
            ulong tick, ref TContext context)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            OrchestrationTimelineDispatch<TValue, TContext, TTime, TClock>.Tick(Runtime, ref m_Bindings, ref m_Samples,
                ref cursor, candidate, operationIndex, contentIdentityId, operationGeneration, ref playbackIdentity,
                inputs, tick, ref context);

        public OrchestrationStopStatus Stop<TNumerics, THost>(
            ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, ref ulong playbackIdentity,
            in OrchestrationStopContext stopContext, bool force, ulong tick, ref TContext context)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            OrchestrationTimelineDispatch<TValue, TContext, TTime, TClock>.Stop(Runtime, ref m_Bindings, ref cursor,
                candidate, operationIndex, ref playbackIdentity, in stopContext, force, tick, ref context);

        public OrchestrationTime ReadTime() => OrchestrationTimelineDispatch<TValue, TContext, TTime, TClock>.ReadTime(Runtime);

        public void ReleasePlayback(OrchestrationStateView candidate, int operationIndex,
            ref ulong playbackIdentity, in TContext context) =>
            OrchestrationTimelineDispatch<TValue, TContext, TTime, TClock>.ReleasePlayback(Runtime, candidate, ref playbackIdentity);

        public OrchestrationOperationResult RequestClipExit(OrchestrationStateView candidate,
            int operationIndex, ulong tick, ref TContext context) =>
            OrchestrationTimelineDispatch<TValue, TContext, TTime, TClock>.RequestClipExit(Runtime, ref m_Bindings,
                candidate, ref context);

        public void SelectExecutionFrame(int frameSlot, ref TContext context) =>
            m_Bindings.SelectExecutionFrame(frameSlot, ref context);

        public void ReleaseExecutionFrame(int frameSlot, in OrchestrationStopContext stopContext, ref TContext context) =>
            m_Bindings.ReleaseExecutionFrame(frameSlot, in stopContext, ref context);
    }

    public readonly struct ManagedOrchestrationTimelineBindings<TTime, TValue, TContext> :
        IOrchestrationTimelineBindings<TTime, TValue, TContext>
        where TTime : unmanaged, IEquatable<TTime>
        where TValue : unmanaged
        where TContext : struct
    {
        readonly IOrchestrationTimelineBindings<TTime, TValue, TContext> m_Bindings;

        public ManagedOrchestrationTimelineBindings(IOrchestrationTimelineBindings<TTime, TValue, TContext> bindings) =>
            m_Bindings = bindings;

        public OrchestrationTimelineStart<TTime> Start(int operationIndex, int contentIdentityId,
            ulong playbackIdentity, ulong operationGeneration, ulong tick, ReadOnlySpan<TValue> inputs,
            ref TContext context) =>
            m_Bindings.Start(operationIndex, contentIdentityId, playbackIdentity, operationGeneration, tick, inputs, ref context);

        public OrchestrationTimelineStep<TTime> Step(int operationIndex,
            in OrchestrationTimelineStart<TTime> start, ulong tick, ReadOnlySpan<TValue> inputs,
            ref TContext context) => m_Bindings.Step(operationIndex, in start, tick, inputs, ref context);

        public void AcceptTerminal(int operationIndex, int playbackSlot, ulong tick,
            in OrchestrationTimelinePlaybackState<TTime> accepted,
            in OrchestrationStopContext stopContext, ref TContext context) =>
            m_Bindings.AcceptTerminal(operationIndex, playbackSlot, tick, in accepted, in stopContext, ref context);

        public OrchestrationOperationResult ExecuteInvocation(in OrchestrationTimelineInvocationFacts invocation,
            in OrchestrationStopContext stopContext, ref TContext context) =>
            m_Bindings.ExecuteInvocation(in invocation, in stopContext, ref context);

        public void ReleaseExecutionFrame(int frameSlot, in OrchestrationStopContext stopContext, ref TContext context) =>
            m_Bindings.ReleaseExecutionFrame(frameSlot, in stopContext, ref context);

        public void ReleasePlaybackFrames(in OrchestrationTimelineStart<OrchestrationTime> start,
            in OrchestrationStopContext stopContext, ref TContext context) =>
            m_Bindings.ReleasePlaybackFrames(in start, in stopContext, ref context);

        public void SelectExecutionFrame(int frameSlot, ref TContext context) =>
            m_Bindings.SelectExecutionFrame(frameSlot, ref context);

        public OrchestrationOperationResult ReleaseInvocation(in OrchestrationTimelineStart<TTime> start,
            in OrchestrationTimelineInvocation<TTime> invocation, ref TContext context) =>
            m_Bindings.ReleaseInvocation(in start, in invocation, ref context);

    }

    public readonly struct ManagedTimelineLogicSamples<TValue, TContext> : ITimelineRuntimeLogicSamples<TValue, TContext>
        where TValue : unmanaged
        where TContext : struct
    {
        readonly ITimelineRuntimeLogicSamples<TValue, TContext> m_Samples;

        public ManagedTimelineLogicSamples(ITimelineRuntimeLogicSamples<TValue, TContext> samples) => m_Samples = samples;

        public void Invoke(int operationIndex, ulong operationGeneration, ulong tick, int playbackSlot, ulong playbackIdentity,
            OrchestrationTime previousTime, int previousCycle, OrchestrationTime time, int cycle,
            ReadOnlySpan<OrchestrationStateAncestor> stateAncestors,
            ReadOnlySpan<OrchestrationExecutionFrameState> executionFrame, ref TContext context) =>
            m_Samples.Invoke(operationIndex, operationGeneration, tick, playbackSlot, playbackIdentity,
                previousTime, previousCycle, time, cycle, stateAncestors, executionFrame, ref context);
    }
}
