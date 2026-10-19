using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public sealed class OrchestrationTimelinePreparedDispatcher<TValue, TScalar, TVector2,
        TVector3, TQuaternion, TMath, TValues, TContext, TTime, TClock, TProjection> :
        IOrchestrationTimelineDispatcher<TValue, TScalar, TVector2, TVector3, TQuaternion, TMath, TValues, TContext>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TQuaternion>
        where TContext : struct
        where TTime : unmanaged, IEquatable<TTime>
        where TClock : struct, IOrchestrationTimelineClock<TTime>
        where TProjection : struct, IOrchestrationTimelineTimeProjection<TTime, TScalar>
    {
        readonly OrchestrationTimelineRuntime<TTime, TClock> m_Runtime;
        readonly OrchestrationTimelinePreparedContent<TTime> m_Content;
        readonly OrchestrationTimelineWorkspace<TTime> m_Workspace;
        readonly IOrchestrationTimelineBindings<TTime, TValue, TContext> m_Bindings;
        readonly ITimelineRuntimeLogicSamples<TValue, TContext> m_Samples;
        readonly TValue[] m_SampleInputs;

        public OrchestrationTimelinePreparedDispatcher(OrchestrationTimelinePreparedContent<TTime> content,
            in OrchestrationTimelineStateLayout<OrchestrationTimelinePlaybackState<TTime>> layout,
            in OrchestrationTimelinePreparedCapacity capacity,
            OrchestrationTimelineWorkspace<TTime> workspace, IOrchestrationTimelineBindings<TTime, TValue, TContext> bindings,
            ITimelineRuntimeLogicSamples<TValue, TContext> samples)
        {
            m_Content = content;
            m_Runtime = new OrchestrationTimelineRuntime<TTime, TClock>(new[] { content }, in layout, in capacity);
            m_Workspace = workspace;
            m_Bindings = bindings;
            m_Samples = samples;
            if (content.Domain == TimelineExecutionDomain.Logic)
            {
                if (samples == null)
                    throw new ArgumentException("Logic Timeline 必须装配正式逻辑样本服务。", nameof(samples));
                m_SampleInputs = new TValue[samples.InputValueCapacity];
            }
            else
                m_SampleInputs = Array.Empty<TValue>();
        }

        public OrchestrationOperationResult Tick(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TQuaternion, TMath, TValues, TContext> cursor,
            OrchestrationStateView candidate, int operationIndex, int contentIdentityId,
            ulong operationGeneration, ref ulong playbackIdentity, ReadOnlySpan<TValue> inputs,
            ulong tick, ref TContext context)
        {
            int slot;
            if (playbackIdentity == 0)
            {
                OrchestrationTimelineStart<TTime> start = m_Bindings.Start(operationIndex, contentIdentityId,
                    m_Runtime.AllocatePlaybackIdentity(candidate), operationGeneration, tick, inputs, ref context);
                slot = m_Runtime.FindFreePlayback(candidate);
                m_Runtime.Start(candidate, slot, in start);
                playbackIdentity = start.PlaybackIdentity;
            }
            else
                slot = m_Runtime.FindPlayback(candidate, playbackIdentity);
            ref OrchestrationTimelinePlaybackState<TTime> playback = ref m_Runtime.Playback(candidate, slot);
            OrchestrationTimelineStep<TTime> step = m_Bindings.Step(operationIndex,
                in playback.Start, tick, inputs, ref context);
            OrchestrationTimelineBatch<TTime> batch = m_Runtime.BeginAdvance(candidate, slot, step, m_Workspace);
            OrchestrationStopContext stopContext = OrchestrationStopContext.ParentStop(operationIndex);
            OrchestrationTimelineStart<OrchestrationTime> startIdentity = playback.Start.WithStartTime(
                OrchestrationTime.FromRaw(default(TClock).Raw(playback.Start.StartTime)));
            cursor.ExecuteTimelineBatch(batch, in startIdentity, in stopContext);
            ref readonly OrchestrationTimelineWorkspace<TTime>.Operation work = ref m_Workspace.Operations[slot];
            if (m_Content.Domain == TimelineExecutionDomain.Logic)
                m_Samples.Invoke(operationIndex, operationGeneration, tick, slot, playbackIdentity,
                    OrchestrationTime.FromRaw(default(TClock).Raw(work.PreviousTime)), work.PreviousCycle,
                    OrchestrationTime.FromRaw(default(TClock).Raw(work.Proposed.Progress.CursorTime)),
                    work.Proposed.Progress.Cycle, m_SampleInputs, cursor.StateAncestors(operationIndex),
                    cursor.ReadExecutionFrame(operationIndex), ref context);
            m_Runtime.Complete(candidate, slot, in batch, m_Workspace);
            if (batch.Status == OrchestrationTimelinePlaybackStatus.Completed)
            {
                m_Bindings.AcceptTerminal(operationIndex, slot, step.Tick, in playback, default, ref context);
                if (!m_Content.RetainsCompletedPlayback)
                    ReleasePlayback(candidate, operationIndex, ref playbackIdentity, in context);
            }
            return Result(batch.Status);
        }

        public OrchestrationStopStatus Stop(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TQuaternion, TMath, TValues, TContext> cursor,
            OrchestrationStateView candidate, int operationIndex, ref ulong playbackIdentity,
            in OrchestrationStopContext stopContext, bool force, ulong tick, ref TContext context)
        {
            int slot = m_Runtime.FindPlayback(candidate, playbackIdentity);
            ref OrchestrationTimelinePlaybackState<TTime> playback = ref m_Runtime.Playback(candidate, slot);
            if (playback.Progress.State == OrchestrationTimelinePlaybackStatus.Completed)
            {
                ReleasePlayback(candidate, operationIndex, ref playbackIdentity, in context);
                return OrchestrationStopStatus.Completed;
            }
            var stop = new OrchestrationTimelineStopContext
            {
                Cause = stopContext.Cause,
                Tick = tick,
                SourceIdentityId = playback.Start.SourceIdentityId
            };
            OrchestrationTimelineBatch<TTime> batch = m_Runtime.BeginStop(candidate, slot, stop, force, m_Workspace);
            OrchestrationTimelineStart<OrchestrationTime> startIdentity = playback.Start.WithStartTime(
                OrchestrationTime.FromRaw(default(TClock).Raw(playback.Start.StartTime)));
            cursor.ExecuteTimelineBatch(batch, in startIdentity, in stopContext);
            m_Runtime.Complete(candidate, slot, in batch, m_Workspace);
            m_Bindings.AcceptTerminal(operationIndex, slot, tick, in playback, in stopContext, ref context);
            ReleasePlayback(candidate, operationIndex, ref playbackIdentity, in context);
            return OrchestrationStopStatus.Completed;
        }

        public TValue ReadTime() => default(TValues).FromScalar(
            default(TProjection).Project(m_Workspace.CurrentInvocation.Time));

        public int ReadSection(OrchestrationStateView candidate, int operationIndex,
            ulong playbackIdentity, in TContext context)
        {
            int slot = m_Runtime.FindPlayback(candidate, playbackIdentity);
            int section = m_Runtime.Playback(candidate, slot).Section;
            return section == 0 ? 0 : m_Content.SectionIdentityIds.Span[
                m_Content.Sections.Span[section - 1].SectionIndex];
        }

        public void ReleasePlayback(OrchestrationStateView candidate, int operationIndex,
            ref ulong playbackIdentity, in TContext context)
        {
            m_Runtime.ReleasePlayback(candidate, playbackIdentity);
            playbackIdentity = 0;
        }

        public OrchestrationOperationResult RequestClipExit(OrchestrationStateView candidate,
            int operationIndex, ulong tick, ref TContext context)
        {
            OrchestrationTimelineInvocationToken token = m_Workspace.CurrentInvocation.Token;
            _ = m_Runtime.TreeClipExit(candidate, m_Workspace, in token);
            return ReleaseInvocation(ref context);
        }

        public OrchestrationOperationResult RequestSectionContinue(OrchestrationStateView candidate,
            int operationIndex, ulong tick, ref TContext context)
        {
            m_Runtime.SectionContinue(m_Workspace);
            return OrchestrationOperationResult.Success;
        }

        public int EnterInvocation(int playbackSlot, int invocationIndex,
            ref TContext context)
        {
            m_Workspace.EnterInvocation(playbackSlot, invocationIndex);
            return m_Bindings.EnterInvocation(in m_Workspace.CurrentStart, in m_Workspace.CurrentInvocation, ref context);
        }

        public void SelectExecutionFrame(int frameSlot, ref TContext context) =>
            m_Bindings.SelectExecutionFrame(frameSlot, ref context);

        public OrchestrationTimelineInvocationFacts ReadInvocationFacts()
        {
            ref readonly OrchestrationTimelineInvocation<TTime> current = ref m_Workspace.CurrentInvocation;
            ref readonly OrchestrationTimelineWorkspace<TTime>.Operation work =
                ref m_Workspace.Operations[current.Token.PlaybackSlot];
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

        public OrchestrationOperationResult ReleaseInvocation(ref TContext context) =>
            m_Bindings.ReleaseInvocation(in m_Workspace.CurrentStart, in m_Workspace.CurrentInvocation, ref context);

        public void LeaveInvocation(ref TContext context)
        {
            try
            {
                m_Bindings.LeaveInvocation(ref context);
            }
            finally
            {
                m_Workspace.LeaveInvocation();
            }
        }

        static OrchestrationOperationResult Result(OrchestrationTimelinePlaybackStatus status) => status switch
        {
            OrchestrationTimelinePlaybackStatus.Running => OrchestrationOperationResult.Running,
            OrchestrationTimelinePlaybackStatus.Stopping => OrchestrationOperationResult.Running,
            OrchestrationTimelinePlaybackStatus.Completed => OrchestrationOperationResult.Success,
            OrchestrationTimelinePlaybackStatus.Stopped => OrchestrationOperationResult.Failure,
            OrchestrationTimelinePlaybackStatus.Failed => OrchestrationOperationResult.Failure,
            _ => throw new InvalidOperationException($"Timeline returned invalid execution state '{status}'.")
        };
    }
}
