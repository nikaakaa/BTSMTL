using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    // Timeline 逻辑派发核心：只经运行视图与结构体插头，托管宿主与 Burst 宿主共用同一份代码。
    public static class OrchestrationTimelineDispatch<TValue, TScalar, TVector2,
        TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TTime, TClock, TProjection>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
        where TContext : struct
        where TTime : unmanaged, IEquatable<TTime>
        where TClock : struct, IOrchestrationTimelineClock<TTime>
        where TProjection : struct, IOrchestrationTimelineTimeProjection<TTime, TScalar>
    {
        public static OrchestrationOperationResult Tick<TDiagnostics, TStorage, THost, TBindings, TSamples>(
            OrchestrationTimelineRuntime<TTime, TClock> runtime, ref TBindings bindings, ref TSamples samples,
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, int contentIdentityId,
            ulong operationGeneration, ref ulong playbackIdentity, ReadOnlySpan<TValue> inputs,
            ulong tick, ref TContext context)
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext>
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
            cursor.ExecuteTimelineBatch(batch, in startIdentity, in stopContext);
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

        public static OrchestrationStopStatus Stop<TDiagnostics, TStorage, THost, TBindings>(
            OrchestrationTimelineRuntime<TTime, TClock> runtime, ref TBindings bindings,
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, ref ulong playbackIdentity,
            in OrchestrationStopContext stopContext, bool force, ulong tick, ref TContext context)
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext>
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
            cursor.ExecuteTimelineBatch(batch, in startIdentity, in stopContext);
            runtime.Complete(candidate, slot, in batch);
            bindings.AcceptTerminal(operationIndex, slot, tick, in playback, in stopContext, ref context);
            ReleasePlayback(runtime, candidate, ref playbackIdentity);
            return OrchestrationStopStatus.Completed;
        }

        public static TValue ReadTime(OrchestrationTimelineRuntime<TTime, TClock> runtime) =>
            default(TValues).FromScalar(default(TProjection).Project(runtime.Workspace.CurrentInvocation.Time));

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

        public static int EnterInvocation<TBindings>(OrchestrationTimelineRuntime<TTime, TClock> runtime,
            ref TBindings bindings, int playbackSlot, int invocationIndex, ref TContext context)
            where TBindings : struct, IOrchestrationTimelineBindings<TTime, TValue, TContext>
        {
            OrchestrationTimelineWorkspaceView<TTime> workspace = runtime.Workspace;
            workspace.EnterInvocation(playbackSlot, invocationIndex);
            return bindings.EnterInvocation(in workspace.CurrentStart, in workspace.CurrentInvocation, ref context);
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

        public static void LeaveInvocation<TBindings>(OrchestrationTimelineRuntime<TTime, TClock> runtime,
            ref TBindings bindings, ref TContext context)
            where TBindings : struct, IOrchestrationTimelineBindings<TTime, TValue, TContext>
        {
            try
            {
                bindings.LeaveInvocation(ref context);
            }
            finally
            {
                runtime.Workspace.LeaveInvocation();
            }
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
    public sealed class OrchestrationTimelinePreparedDispatcher<TValue, TScalar, TVector2,
        TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TTime, TClock, TProjection> :
        IOrchestrationTimelineDispatcher<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
        where TContext : struct
        where TTime : unmanaged, IEquatable<TTime>
        where TClock : struct, IOrchestrationTimelineClock<TTime>
        where TProjection : struct, IOrchestrationTimelineTimeProjection<TTime, TScalar>
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
            new OrchestrationTimelineRuntime<TTime, TClock>(m_Content.View, in m_Layout, in m_Capacity, m_Workspace.View);

        public OrchestrationOperationResult Tick<TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, int contentIdentityId,
            ulong operationGeneration, ref ulong playbackIdentity, ReadOnlySpan<TValue> inputs,
            ulong tick, ref TContext context)
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext> =>
            OrchestrationTimelineDispatch<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext,
                TTime, TClock, TProjection>.Tick(Runtime, ref m_Bindings, ref m_Samples, ref cursor, candidate,
                operationIndex, contentIdentityId, operationGeneration, ref playbackIdentity, inputs, tick, ref context);

        public OrchestrationStopStatus Stop<TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, ref ulong playbackIdentity,
            in OrchestrationStopContext stopContext, bool force, ulong tick, ref TContext context)
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext> =>
            OrchestrationTimelineDispatch<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext,
                TTime, TClock, TProjection>.Stop(Runtime, ref m_Bindings, ref cursor, candidate, operationIndex,
                ref playbackIdentity, in stopContext, force, tick, ref context);

        public TValue ReadTime() => OrchestrationTimelineDispatch<TValue, TScalar, TVector2, TVector3, TYaw,
            TQuaternion, TMath, TValues, TContext, TTime, TClock, TProjection>.ReadTime(Runtime);

        public void ReleasePlayback(OrchestrationStateView candidate, int operationIndex,
            ref ulong playbackIdentity, in TContext context) =>
            OrchestrationTimelineDispatch<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext,
                TTime, TClock, TProjection>.ReleasePlayback(Runtime, candidate, ref playbackIdentity);

        public OrchestrationOperationResult RequestClipExit(OrchestrationStateView candidate,
            int operationIndex, ulong tick, ref TContext context) =>
            OrchestrationTimelineDispatch<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext,
                TTime, TClock, TProjection>.RequestClipExit(Runtime, ref m_Bindings, candidate, ref context);

        public int EnterInvocation(int playbackSlot, int invocationIndex, ref TContext context) =>
            OrchestrationTimelineDispatch<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext,
                TTime, TClock, TProjection>.EnterInvocation(Runtime, ref m_Bindings, playbackSlot, invocationIndex, ref context);

        public void SelectExecutionFrame(int frameSlot, ref TContext context) =>
            m_Bindings.SelectExecutionFrame(frameSlot, ref context);

        public OrchestrationTimelineInvocationFacts ReadInvocationFacts() =>
            OrchestrationTimelineDispatch<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext,
                TTime, TClock, TProjection>.ReadInvocationFacts(Runtime);

        public OrchestrationOperationResult ReleaseInvocation(ref TContext context) =>
            OrchestrationTimelineDispatch<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext,
                TTime, TClock, TProjection>.ReleaseInvocation(Runtime, ref m_Bindings, ref context);

        public void LeaveInvocation(ref TContext context) =>
            OrchestrationTimelineDispatch<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext,
                TTime, TClock, TProjection>.LeaveInvocation(Runtime, ref m_Bindings, ref context);
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

        public int EnterInvocation(in OrchestrationTimelineStart<TTime> start,
            in OrchestrationTimelineInvocation<TTime> invocation, ref TContext context) =>
            m_Bindings.EnterInvocation(in start, in invocation, ref context);

        public void SelectExecutionFrame(int frameSlot, ref TContext context) =>
            m_Bindings.SelectExecutionFrame(frameSlot, ref context);

        public OrchestrationOperationResult ReleaseInvocation(in OrchestrationTimelineStart<TTime> start,
            in OrchestrationTimelineInvocation<TTime> invocation, ref TContext context) =>
            m_Bindings.ReleaseInvocation(in start, in invocation, ref context);

        public void LeaveInvocation(ref TContext context) => m_Bindings.LeaveInvocation(ref context);
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
