using System;
using System.Collections.Generic;
using System.Globalization;
using BTSMTL.Timeline;
using BTSMTL.Timeline.Runtime;
using ThirdPersonSimulation.Fixed;

namespace ThirdPersonSimulation
{
    public sealed class TimelineAbilityRuntime : IAbilityTimelineRuntime, IAbilityTimelineLogicMotionReader,
        IAbilityTimelineMotionSourceReader, IAbilityTreeClipInvokerHost
    {
        sealed class Playback
        {
            internal TimelineRuntimePlayback Runtime;
            internal AbilityTimelineStartRequest Request;
            internal TimelineRuntimeAdvanceResult Advance;
            internal AbilityTimelineAdvancePending Pending;
            internal AbilityTimelineStopPending Stop;
            internal readonly CanonicalTextReferences Text;
            internal Playback(CanonicalTextStorage storage) => Text = new CanonicalTextReferences(1, storage);
            internal void SetRequest(in AbilityTimelineStartRequest request)
            {
                request.Retain();
                Request.Release();
                Request = request;
            }
        }

        readonly TimelineContentArtifact m_Artifact;
        readonly int m_TickRate;
        readonly TimelinePlaybackMode[] m_CapacityModes;
        readonly Dictionary<string, TimelineRuntimePreparedContent> m_Contents = new(StringComparer.Ordinal);
        readonly Dictionary<ulong, Playback> m_Playbacks = new();
        readonly HashSet<ulong> m_RetainedPlaybackIds = new(AbilityTimelineRuntimeLimits.MaximumPlaybackCount);
        readonly TimelineRuntimeEvaluationStoragePool m_Evaluations = new();
        readonly char[] m_RequestCharacters = new char[29];
        Playback[] m_Pool;
        int m_AcquiredPoolLength;
        IAbilityTreeClipInvoker m_Invoker;
        ulong m_NextSequence;

        public TimelineAbilityRuntime(TimelineContentArtifact artifact, CharacterControlRuntimeBinding controlBinding,
            int tickRate, IReadOnlyList<Float32GameplayAbilityExecutionData> abilityData)
        {
            m_Artifact = artifact;
            m_TickRate = tickRate;
            m_CapacityModes = new TimelinePlaybackMode[artifact.Timelines.Count];
            for (int i = 0; i < artifact.Timelines.Count; i++)
            {
                m_CapacityModes[i] = TimelinePlaybackMode.Once;
                for (int program = 0; program < abilityData.Count; program++)
                    for (int op = 0; op < abilityData[program].OperationDefinitions.Count; op++)
                    {
                        SimulationOperationDefinition operation = abilityData[program].OperationDefinitions[op];
                        if (operation.Code == SimulationOperationCode.Timeline && operation.Text0 == artifact.Timelines[i].Identity &&
                            operation.Integer0 == (int)AbilityTimelinePlaybackMode.Loop)
                            m_CapacityModes[i] = TimelinePlaybackMode.Loop;
                    }
            }
            var graphs = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < abilityData.Count; i++)
                for (int j = 0; j < abilityData[i].SourceMap.Count; j++)
                {
                    ProgramSourceMapEntry source = abilityData[i].SourceMap[j];
                    string identity = "tree:" + source.GraphId;
                    if (graphs.TryGetValue(identity, out string revision) && revision != source.ContentHash)
                        throw new InvalidOperationException($"Timeline graph '{identity}' has conflicting installed revisions.");
                    graphs[identity] = source.ContentHash;
                }
            for (int i = 0; i < artifact.Timelines.Count; i++)
            {
                TimelineContentUnit content = artifact.Timelines[i];
                for (int j = 0; j < content.Clips.Count; j++)
                {
                    TimelineContentClip clip = content.Clips[j];
                    if (clip.TrackMuted || !clip.ExecutionPolicy.IsLogic)
                        continue;
                    if (clip.Parameters is TimelineContentMotion motion)
                        controlBinding.MotionBindings.Require(motion.ResourceId);
                    if (clip.Parameters is TimelineContentTree)
                        RequireGraph(graphs, clip.GraphBinding.GraphId, clip.GraphBinding.Revision);
                }
                for (int j = 0; j < content.Markers.Count; j++)
                    if (!content.Markers[j].TrackMuted && content.Markers[j].ExecutionPolicy.IsLogic)
                        RequireGraph(graphs, content.Markers[j].GraphId, content.Markers[j].GraphRevision);
                var plan = new TimelineBindingPlan(content, TimelineExecutionDomainMask.Logic);
                if (plan.Bindings.Count != 0)
                    throw new InvalidOperationException($"Authority Timeline '{content.Identity}' requires unavailable external domain bindings.");
                var input = new TimelineCallInput(plan, Array.Empty<TimelineCallBinding>());
                var bindings = TimelineBindingPreparation.Prepare(plan, input, new AuthorityDomainBindings(), new List<string>());
                var dependencies = new List<TimelineContentDependency>();
                var handles = new List<TimelineRuntimeDependencyHandle>();
                for (int j = 0; j < content.Dependencies.Count; j++)
                {
                    TimelineContentDependency dependency = content.Dependencies[j];
                    if (dependency.Kind != "timeline.tree" || !graphs.ContainsKey(dependency.Identity))
                        continue;
                    RequireGraph(graphs, dependency.Identity, dependency.ContentHash);
                    dependencies.Add(dependency);
                    handles.Add(new TimelineRuntimeDependencyHandle(handles.Count + 1));
                }
                m_Contents.Add(content.Identity, TimelineRuntimePreparedContent.Ready(TimelineRuntimeNumericTarget.Float32,
                    content, plan, input, bindings, new TimelineRuntimePreparedDependencies(dependencies, handles)));
            }
        }

        static void RequireGraph(Dictionary<string, string> graphs, string identity, string revision)
        {
            if (!graphs.TryGetValue(identity, out string installed) || installed != revision)
                throw new InvalidOperationException($"Timeline graph '{identity}' revision '{revision}' is not installed.");
        }

        sealed class AuthorityDomainBindings : ITimelineDomainBindingResolver
        {
            public bool TryResolve(TimelineBindingDeclaration declaration, TimelineBindingValue value,
                out TimelineBindingHandle handle, out string error)
            {
                handle = default;
                error = $"Authority host has no binding for '{declaration.BindingId}'.";
                return false;
            }
        }

        public int MaximumMotionContributionsPerAdvance => m_Evaluations.MaximumMotionContributionsPerAdvance;

        public void PrepareSnapshotStorage(in GameplayEffectSessionCapacity capacity, int maximumSessionCheckpointPayloadBytes)
        {
            int count = AbilityTimelineRuntimeLimits.MaximumPlaybackCount;
            var storage = new CanonicalTextStorage(checked(count * (capacity.StatePageCount + 1)),
                checked(count * (capacity.StatePageCount + 1) * m_RequestCharacters.Length));
            m_Playbacks.EnsureCapacity(count);
            if (m_Artifact.Timelines.Count == 0)
            {
                m_Pool = Array.Empty<Playback>();
                return;
            }
            m_Evaluations.PrepareShared(m_Artifact.Timelines, m_CapacityModes, count);
            GetSnapshotCapacity(out int clips, out _, out int pending, out _);
            m_Pool = new Playback[count];
            TimelineContentUnit first = m_Artifact.Timelines[0];
            for (int i = 0; i < count; i++)
            {
                var runtime = new TimelineRuntimePlayback(first, m_CapacityModes[0],
                    m_TickRate, m_Evaluations, clips, pending);
                runtime.SnapshotStorage.PrepareCapacity(capacity.StatePageCount);
                m_Pool[i] = new Playback(storage) { Runtime = runtime };
            }
        }

        public void GetSnapshotCapacity(out int clips, out int exits, out int pendingExits, out int trees)
        {
            clips = exits = pendingExits = trees = 0;
            for (int i = 0; i < m_Artifact.Timelines.Count; i++)
            {
                TimelineRuntimePlayback.GetSnapshotCapacity(m_Artifact.Timelines[i], m_CapacityModes[i],
                    out int c, out int e, out int p, out int t);
                clips = Math.Max(clips, c); exits = Math.Max(exits, e);
                pendingExits = Math.Max(pendingExits, p); trees = Math.Max(trees, t);
            }
        }

        Playback Acquire()
        {
            for (int i = 0; i < m_AcquiredPoolLength; i++)
                if (m_Pool[i].Runtime.State == TimelineRuntimePlaybackState.Disposed)
                    return m_Pool[i];
            if (m_AcquiredPoolLength < m_Pool.Length)
                return m_Pool[m_AcquiredPoolLength++];
            throw new InvalidOperationException("Timeline playbacks exceed the installed execution capacity.");
        }

        public ulong Start(in AbilityTimelineStartRequest request)
        {
            TimelineContentUnit content = m_Artifact.Require(request.TimelineId);
            Playback playback = Acquire();
            playback.SetRequest(in request);
            playback.Text.Reset();
            "timeline:".AsSpan().CopyTo(m_RequestCharacters);
            request.PlaybackIdentity.TryFormat(m_RequestCharacters.AsSpan(9), out int digits, provider: CultureInfo.InvariantCulture);
            CanonicalTextValue requestId = playback.Text.CaptureCharacters(m_RequestCharacters.AsSpan(0, 9 + digits));
            var preparation = new TimelineRuntimePreparationResult(new TimelineRuntimePrepareRequest(requestId,
                m_Contents[content.Identity], new TimelineExecutionIdentity(request.ActionContext.ActionId,
                    request.InvocationSource.OperationExecutionPath, request.PlaybackIdentity),
                (TimelinePlaybackMode)request.PlaybackMode, request.StartTime));
            playback.Runtime.ResetForReuse(new TimelineRuntimePlaybackHandle(request.PlaybackIdentity), request.PlaybackIdentity, preparation);
            playback.Runtime.Start();
            m_Playbacks.Add(request.PlaybackIdentity, playback);
            return request.PlaybackIdentity;
        }

        public AbilityTimelineTickResult Tick(ulong runtimeHandle, ulong logicTick, int tickCount, AbilityTimelinePlaybackControl control)
        {
            Playback playback = m_Playbacks[runtimeHandle];
            TimelineRuntimePlayback runtime = playback.Runtime;
            if (runtime.State != TimelineRuntimePlaybackState.Running)
                return new AbilityTimelineTickResult(Status(runtime.State), default);
            playback.Advance = runtime.Advance(runtime.CreateAdvanceRequest(logicTick, tickCount, control));
            TimelineRuntimeAdvanceResult advance = playback.Advance;
            playback.Pending = new AbilityTimelineAdvancePending(runtimeHandle, checked(++m_NextSequence),
                Progress(playback, logicTick, advance.PreviousTime, advance.Time, advance.PreviousCycle, advance.Cycle,
                    advance.Completes ? AbilityTimelineProgressState.Completed : AbilityTimelineProgressState.Active, control));
            try
            {
                InvokeMarkers(playback);
                for (int i = 0; i < advance.Evaluation.TreeClips.Count; i++)
                    InvokeTree(playback, advance.Evaluation.TreeClips[i]);
            }
            catch
            {
                runtime.Discard(advance);
                playback.Pending = default;
                throw;
            }
            return new AbilityTimelineTickResult(advance.Completes ? AbilityTimelineRuntimeStatus.Succeeded : AbilityTimelineRuntimeStatus.Running,
                playback.Pending);
        }

        void InvokeMarkers(Playback playback)
        {
            for (int i = 0; i < playback.Advance.Evaluation.Markers.Count; i++)
            {
                TimelineRuntimeMarkerRequest marker = playback.Advance.Evaluation.Markers[i];
                var context = new TimelineRuntimeStepContext(playback.Runtime, playback.Advance.Request, playback.Advance);
                if (!TimelineRuntimeTreeClipLifecycle.ConsumeMarker(m_Invoker, playback.Request.ActionContext.InstanceId, marker, context))
                    throw new InvalidOperationException($"Timeline Marker '{marker.MarkerAuthoringId}' execution was rejected.");
            }
        }

        void InvokeTree(Playback playback, in TimelineRuntimeTreeClipRequest request)
        {
            var context = new TimelineRuntimeStepContext(playback.Runtime, playback.Advance.Request, playback.Advance);
            if (!TimelineRuntimeTreeClipLifecycle.Consume(m_Invoker, playback.Request.ActionContext.InstanceId, request, context))
                throw new InvalidOperationException($"Timeline TreeClip '{request.ClipAuthoringId}' execution was rejected.");
        }

        public void Commit(AbilityTimelineAdvancePending pending)
        {
            Playback playback = Require(pending);
            playback.Runtime.Commit(playback.Advance);
            TimelineRuntimeTreeClipLifecycle.Commit(new TimelineRuntimeStepContext(
                playback.Runtime, playback.Advance.Request, playback.Advance));
            playback.Pending = default;
        }

        public void Discard(AbilityTimelineAdvancePending pending)
        {
            Playback playback = Require(pending);
            playback.Runtime.Discard(playback.Advance);
            playback.Pending = default;
        }

        Playback Require(AbilityTimelineAdvancePending pending)
        {
            Playback playback = m_Playbacks[pending.RuntimeHandle];
            if (playback.Pending.Sequence != pending.Sequence)
                throw new InvalidOperationException("Timeline advance candidate is not pending.");
            return playback;
        }

        public AbilityTimelineStopResult Stop(ulong runtimeHandle, ulong logicTick)
        {
            Playback playback = m_Playbacks[runtimeHandle];
            if (!playback.Runtime.RequestStop(new TimelinePlaybackStopContext(TimelinePlaybackStopCause.ExplicitParentStop, logicTick)))
                return new AbilityTimelineStopResult(Status(playback.Runtime.State), default, playback.Request.ActionContext.InstanceId);
            playback.Stop = new AbilityTimelineStopPending(runtimeHandle, checked(++m_NextSequence),
                Progress(playback, logicTick, playback.Runtime.CursorTime, playback.Runtime.CursorTime,
                    playback.Runtime.Cycle, playback.Runtime.Cycle, AbilityTimelineProgressState.Stopped, playback.Runtime.Control));
            try
            {
                if (!TimelineRuntimeTreeClipLifecycle.ConsumeStop(playback.Runtime, TimelinePlaybackStopCause.ExplicitParentStop,
                        m_Invoker, playback.Request.ActionContext.InstanceId))
                    throw new InvalidOperationException("Timeline TreeClip stop was rejected.");
            }
            catch
            {
                playback.Runtime.DiscardStop();
                playback.Stop = default;
                throw;
            }
            return new AbilityTimelineStopResult(AbilityTimelineRuntimeStatus.Running, playback.Stop, playback.Request.ActionContext.InstanceId);
        }

        public void CommitStop(AbilityTimelineStopPending pending)
        {
            Playback playback = RequireStop(pending);
            playback.Runtime.CommitStop();
            playback.Runtime.CompleteStop();
            TimelineRuntimeTreeClipLifecycle.Clear(playback.Runtime);
            playback.Stop = default;
        }

        public void DiscardStop(AbilityTimelineStopPending pending)
        {
            Playback playback = RequireStop(pending);
            playback.Runtime.DiscardStop();
            playback.Stop = default;
        }

        Playback RequireStop(AbilityTimelineStopPending pending)
        {
            Playback playback = m_Playbacks[pending.RuntimeHandle];
            if (playback.Stop.Sequence != pending.Sequence)
                throw new InvalidOperationException("Timeline stop candidate is not pending.");
            return playback;
        }

        public AbilityTimelineRuntimeSnapshot Capture(ulong runtimeHandle) => CaptureRuntimeSnapshot(runtimeHandle);
        public AbilityTimelineRuntimeSnapshot CaptureRuntimeSnapshot(ulong runtimeHandle)
        {
            Playback playback = m_Playbacks[runtimeHandle];
            if (playback.Runtime.HasPendingAdvance || playback.Runtime.HasPendingStop)
                throw new InvalidOperationException("Timeline Capture requires a committed boundary.");
            AbilityTimelineSnapshotStorage storage = playback.Runtime.SnapshotStorage.BeginWrite();
            return TimelineAbilitySnapshotCodec.Capture(new TimelineRuntimePlaybackSnapshot(playback.Runtime, storage),
                playback.Request, storage.ActiveTreeClips.Capture(playback.Runtime.ActiveTreeClips));
        }

        public ulong ApplyRestore(AbilityTimelineRuntimeSnapshot snapshot, ulong logicTick)
        {
            TimelineContentUnit content = m_Artifact.Require(snapshot.TimelineId);
            if (!content.Revision.Equals(snapshot.ContentRevision))
                throw new InvalidOperationException("Timeline snapshot content does not match the loaded artifact.");
            if (!m_Playbacks.TryGetValue(snapshot.RuntimeHandle, out Playback playback))
            {
                playback = Acquire();
                m_Playbacks.Add(snapshot.RuntimeHandle, playback);
            }
            else
            {
                playback.Runtime.ReleaseForReuse();
            }
            playback.SetRequest(new AbilityTimelineStartRequest(snapshot.TimelineId, snapshot.RuntimeHandle,
                (AbilityTimelinePlaybackMode)snapshot.PlaybackMode, snapshot.ActionContext, snapshot.InvocationSource,
                snapshot.InputSequence, snapshot.StartTick, snapshot.StartTime));
            var identity = new TimelineExecutionIdentity(snapshot.OwnerIdentity, snapshot.CallIdentity, snapshot.ExecutionInstanceId);
            var preparation = new TimelineRuntimePreparationResult(new TimelineRuntimePrepareRequest(snapshot.RequestId,
                m_Contents[content.Identity], identity, (TimelinePlaybackMode)snapshot.PlaybackMode, snapshot.StartTime));
            playback.Runtime.ResetForReuse(new TimelineRuntimePlaybackHandle(snapshot.RuntimeHandle), snapshot.Generation, preparation);
            TimelineRuntimePlaybackSnapshot native = TimelineAbilitySnapshotCodec.Restore(snapshot, playback.Runtime.Handle,
                snapshot.Generation, identity, (TimelinePlaybackMode)snapshot.PlaybackMode, TimelineRuntimeNumericTarget.Float32, m_TickRate);
            if (!playback.Runtime.RestoreCommittedState(native.State, native.CursorTime, native.Cycle, native.TimeCarry,
                    native.Control, native.SectionId, native.ActiveClipIds.Span, native.TreeDecisionExits.Span,
                    native.PendingTreeDecisionExits.Span, native.HasStopContext, native.StopContext, native.InitialBoundaryPending))
                throw new InvalidOperationException("Timeline restore candidate is not a committed state.");
            TimelineRuntimeTreeClipLifecycle.Restore(playback.Runtime, snapshot.ActiveTreeClips);
            playback.Pending = default;
            playback.Stop = default;
            return snapshot.RuntimeHandle;
        }

        public bool RequestTreeClipExit(ulong handle, CanonicalTextValue clipId, int cycle) =>
            m_Playbacks[handle].Runtime.RequestTreeClipExit(clipId, cycle);

        public IAbilityTreeClipInvoker ExchangeTreeClipInvoker(IAbilityTreeClipInvoker invoker)
        {
            IAbilityTreeClipInvoker previous = m_Invoker;
            m_Invoker = invoker;
            return previous;
        }

        public void CopyPendingMotion(ulong handle, List<AbilityTimelineLogicMotion> results)
        {
            results.Clear();
            Playback playback = m_Playbacks[handle];
            if (playback.Pending.Sequence == 0 || playback.Pending.Progress.Control.IsPaused) return;
            var motions = playback.Advance.Evaluation.MotionContributions;
            SimulationExecutionSource source = ReadMotionSource(handle);
            for (int i = 0; i < motions.Count; i++)
            {
                TimelineMotionCurveContribution motion = motions[i];
                results.Add(new AbilityTimelineLogicMotion(source, motion.ClipAuthoringId, playback.Request.ActionContext.SkillId,
                    playback.Request.InvocationSource.InvocationGeneration, motion.DisplacementX, motion.DisplacementY,
                    motion.DisplacementZ, motion.YawDegrees, motion.Weight, motion.Priority, (AbilityTimelineMotionSpace)motion.Space,
                    motion.Channel switch
                    {
                        TimelineMotionChannel.Action => AbilityTimelineMotionChannel.Action,
                        TimelineMotionChannel.GameplayResult => AbilityTimelineMotionChannel.GameplayResult,
                        _ => throw new InvalidOperationException("Authority Timeline motion cannot own the locomotion channel.")
                    }, (AbilityTimelineMotionBlendMode)motion.BlendMode, motion.ConsumeLowerChannels));
            }
        }

        public SimulationExecutionSource ReadMotionSource(ulong handle)
        {
            AbilityTimelineInvocationSource source = m_Playbacks[handle].Request.InvocationSource;
            return SimulationExecutionSource.FromSkillOperation(new OperationHandle(source.OperationIndex), source.OperationExecutionPath);
        }

        public AbilityTimelineMotionSourcePose SampleMotionSource(ulong handle, string clipId, FixedScalar time)
        {
            TimelineRuntimeMotionPosition pose = m_Playbacks[handle].Runtime.SampleMotionSource(clipId, time);
            return new AbilityTimelineMotionSourcePose(pose.X, pose.Y, pose.Z, pose.Yaw);
        }

        public AbilityTimelineMotionSourcePose SampleMotionPrefix(ulong handle, string clipId, int cycle, FixedScalar time) =>
            m_Playbacks[handle].Runtime.SampleMotionPrefix(clipId, cycle, time);

        public void DiscardUnpublishedPlaybacks(IReadOnlyList<AbilityTimelineRuntimeSnapshot> snapshots) => RemoveUnreferenced(snapshots);
        public void ReleaseUnreferencedPlaybacks(IReadOnlyList<AbilityTimelineRuntimeSnapshot> snapshots, ulong committedTick) => RemoveUnreferenced(snapshots);

        void RemoveUnreferenced(IReadOnlyList<AbilityTimelineRuntimeSnapshot> snapshots)
        {
            m_RetainedPlaybackIds.Clear();
            for (int i = 0; i < snapshots.Count; i++)
                m_RetainedPlaybackIds.Add(snapshots[i].RuntimeHandle);
            int retainedPoolLength = 0;
            for (int i = 0; i < m_AcquiredPoolLength; i++)
            {
                Playback playback = m_Pool[i];
                if (playback.Runtime.State == TimelineRuntimePlaybackState.Disposed) continue;
                if (m_RetainedPlaybackIds.Contains(playback.Runtime.Handle.Value))
                {
                    retainedPoolLength = i + 1;
                    continue;
                }
                m_Playbacks.Remove(playback.Runtime.Handle.Value);
                playback.Runtime.ReleaseForReuse();
                playback.Request.Release();
                playback.Request = default;
                playback.Pending = default;
                playback.Stop = default;
                playback.Text.Reset();
            }
            m_AcquiredPoolLength = retainedPoolLength;
        }

        static AbilityTimelineRuntimeStatus Status(TimelineRuntimePlaybackState state) => state switch
        {
            TimelineRuntimePlaybackState.Completed => AbilityTimelineRuntimeStatus.Succeeded,
            TimelineRuntimePlaybackState.Stopped => AbilityTimelineRuntimeStatus.Cancelled,
            TimelineRuntimePlaybackState.Failed => AbilityTimelineRuntimeStatus.Failed,
            _ => AbilityTimelineRuntimeStatus.Running
        };

        static AbilityTimelineProgress Progress(Playback playback, ulong tick, FixedScalar previous, FixedScalar time,
            int previousCycle, int cycle, AbilityTimelineProgressState state, AbilityTimelinePlaybackControl control) =>
            new AbilityTimelineProgress(playback.Runtime.Content.Identity, playback.Runtime.ContentRevision,
                playback.Runtime.Generation, tick, playback.Runtime.Content.Duration, previous, time, previousCycle,
                cycle, playback.Runtime.PlaybackMode == TimelinePlaybackMode.Loop, state, control);
    }
}
