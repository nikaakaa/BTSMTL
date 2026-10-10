using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class FixedPassPipelineRuntimeHandle : ISimulationSessionRuntimeHandle, ISimulationSessionLocalCombatClock, ICharacterFutureBodyTranslationSource,
        ISimulationSessionCheckpointRuntime, ISimulationSessionInputReplayRuntime,
        ISimulationSessionFrameHistoryRuntime, IGameplayAbilityAvailabilitySessionReadPort, IFixedSessionIngressWritePort
    {
        readonly SimulationSessionLifecycleController m_Lifecycle;
        readonly CompiledSimulationPipelinePlan m_Pipeline;
        readonly SimulationWorldStateStore m_StateStore;
        readonly FixedPipelineTransaction m_Transaction;
        readonly IReadOnlyList<IFixedCompiledPipelinePassRuntime> m_Passes;
        readonly string[] m_PassPhaseNames;
        readonly SimulationSessionResourceRegistry m_Resources;
        readonly ICharacterFutureBodyTranslationSource m_FutureBodyTranslationSource;
        readonly SimulationRuntimePortSet m_SourcePorts;
        readonly IFixedLocalInputSourcePort m_LocalInputs;
        readonly FixedCombatRuntime m_Combat;
        ulong m_LatestOuterTick;
        bool m_Disposed;

        internal FixedPassPipelineRuntimeHandle(
            SimulationSessionCompositionDescriptor descriptor,
            CompiledSimulationPipelinePlan pipeline,
            SimulationWorldStateStore stateStore,
            FixedPipelineTransaction transaction,
            IReadOnlyList<IFixedCompiledPipelinePassRuntime> passes,
            SimulationSessionResourceRegistry resources,
            ICharacterFutureBodyTranslationSource futureBodyTranslationSource,
            SimulationRuntimePortSet sourcePorts,
            FixedCombatRuntime combat)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            m_Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            m_StateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            m_Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
            m_Passes = passes ?? throw new ArgumentNullException(nameof(passes));
            m_Resources = resources ?? throw new ArgumentNullException(nameof(resources));
            m_PassPhaseNames = new string[m_Passes.Count];
            for (int i = 0; i < m_PassPhaseNames.Length; i++)
                m_PassPhaseNames[i] = m_Passes[i].Phase.ToString();
            m_FutureBodyTranslationSource = futureBodyTranslationSource;
            m_SourcePorts = sourcePorts;
            for (int i = 0; i < sourcePorts.Ports.Count; i++)
                if (sourcePorts.Ports[i] is IFixedLocalInputSourcePort localInputs)
                    m_LocalInputs = localInputs;
            m_Combat = combat;
            m_Lifecycle = new SimulationSessionLifecycleController(descriptor);
            m_Lifecycle.BeginPreparing();
            m_Lifecycle.Activate(descriptor);
        }

        public bool CanWriteRuntimeIngress => m_LocalInputs != null;
        public FixedCharacterRuntimeState ReadActorState(ActorId actor)
        {
            var actors = m_StateStore.Current.Actors;
            for (int i = 0; i < actors.Count; i++)
                if (actors[i].ActorId == actor)
                    return actors[i].State;
            throw new InvalidOperationException("输入命令的角色不属于当前 Session 的已提交状态。");
        }
        public FixedValuePortInputs GetValuePorts(ActorId actor, CharacterSkillId ability) => m_LocalInputs.ValuePorts.Require(actor, ability);
        public ulong QueueRuntimeIngress(ActorId actor, SimulationActionEventIngress actionEvent, SimulationAttributeValueIngress attribute) =>
            m_LocalInputs.QueueRuntimeIngress(actor, actionEvent, attribute);
        public bool TryTakeRuntimeIngressResult(ulong sequence, out ulong tick) =>
            m_LocalInputs.TryTakeRuntimeIngressResult(sequence, out tick);

        public void QueryAbilityAvailability(ActorId actorId, GameplayAbilityRulePhase phase,
            out GameplayAbilityAvailabilityResult result) =>
            m_Transaction.QueryAbilityAvailability(actorId, phase, out result);

        public SimulationSessionCompositionDescriptor Descriptor { get; }
        public SimulationSessionLifecycleState LifecycleState => m_Lifecycle.State;
        public SimulationSessionFailure Failure => m_Lifecycle.Failure;
        public SimulationSessionDiagnosticsSnapshot Diagnostics => BuildDiagnostics();
        float ISimulationSessionLocalCombatClock.WorldTimeScale => m_Combat.WorldTimeScale.ToSingle();
        public void PrepareRecordedFrames(int capacity) => m_Transaction.PrepareRecordedFrames(capacity);
        public ReadOnlyMemory<byte> RecordFrame(ulong presentationFrame) => m_Transaction.RecordFrame(presentationFrame);
        public void RestoreRecordedControlInput(ArraySegment<byte> state) => m_Transaction.RestoreRecordedControlInput(state);
        public void RestoreRecordedControlSources(ArraySegment<byte> state) => m_Transaction.RestoreRecordedControlSources(state);

        public bool TryGetRecordedFrameTick(ulong presentationFrame, out ulong tick)
        {
            bool found = m_StateStore.TryGetRecordedFrame(presentationFrame, out SimulationWorldStateSet state, out _);
            tick = found ? state.LastCompletedTick : 0;
            return found;
        }
        public void ClearRecordedFrames() => m_StateStore.ClearRecordedFrames();
        public SimulationSessionCheckpoint CaptureRecordedCheckpoint(ulong presentationFrame) =>
            m_Transaction.CaptureRecordedCheckpoint(presentationFrame);
        public SimulationSessionCheckpoint CaptureCheckpoint()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(FixedPassPipelineRuntimeHandle));
            return m_Transaction.CaptureCheckpoint();
        }

        public bool IsInputRecording => FixedCharacterInputTraceModule.IsRecording;

        public void RestoreCheckpoint(SimulationSessionCheckpoint checkpoint)
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(FixedPassPipelineRuntimeHandle));
            m_Transaction.RestoreCheckpoint(checkpoint);
            m_LatestOuterTick = checkpoint.Tick.Value;
        }

        public string PredictionSourceIdentity =>
            m_FutureBodyTranslationSource?.PredictionSourceIdentity ?? string.Empty;

        public bool TryGetCurrentBody(ActorId actorId, out WorldBodyState body)
        {
            for (int i = 0; i < m_StateStore.Current.WorldState.Bodies.Count; i++)
            {
                WorldBodyState candidate = m_StateStore.Current.WorldState.Bodies[i];
                if (candidate.ActorId == actorId)
                {
                    body = candidate;
                    return true;
                }
            }
            body = default;
            return false;
        }

        public bool TryStartInputRecording(out string error)
        {
            error = string.Empty;
            if (m_StateStore.Current.Actors.Count != 1)
            {
                error = "Fixed input recording requires exactly one Actor.";
                return false;
            }
            ActorId actorId = m_StateStore.Current.Actors[0].ActorId;
            if (!TryGetCurrentBody(actorId, out WorldBodyState body))
            {
                error = "Fixed input recording could not resolve the current Actor Body.";
                return false;
            }
            try
            {
                FixedCharacterInputTraceModule.PrepareRecordingFromCheckpoint(actorId, body);
                FixedCharacterInputTraceModule.StartRecording();
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public bool TryStopInputRecording(out string error)
        {
            try
            {
                using FixedCharacterInputTrace completed = FixedCharacterInputTraceModule.StopRecording();
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public bool TryCancelInputReplay(out string error)
        {
            if (!FixedCharacterInputTraceModule.IsReplayActive)
            {
                error = "Fixed input replay is not active.";
                return false;
            }
            FixedCharacterInputTraceModule.Stop();
            error = string.Empty;
            return true;
        }

        public bool TryReplayInputRange(
            SimulationSessionCheckpoint checkpoint,
            ulong fromTick,
            ulong toTick,
            out string error)
        {
            error = string.Empty;
            if (fromTick >= toTick)
            {
                error = "Fixed replay range is invalid.";
                return false;
            }
            if (HasExternalReplaySource())
            {
                error = "Fixed replay requires an external result journal for the active Pipeline Source.";
                return false;
            }
            if (FixedCharacterInputTraceModule.IsRecording)
            {
                error = "Fixed replay cannot start while input recording is active.";
                return false;
            }
            FixedCharacterInputTrace recorded = FixedCharacterInputTraceModule.LastCompletedTrace;
            if (recorded == null)
            {
                error = "No completed Fixed input trace is available.";
                return false;
            }
            if (m_StateStore.Current.Actors.Count != 1)
            {
                error = "Fixed input replay requires exactly one Actor.";
                return false;
            }
            ActorId actorId = m_StateStore.Current.Actors[0].ActorId;
            if (recorded.ActorId != actorId)
            {
                error = "Fixed input trace Actor does not match the active Session Actor.";
                return false;
            }
            var frames = new List<FixedCharacterInputTraceFrame>();
            for (int i = 0; i < recorded.Frames.Count; i++)
            {
                FixedCharacterInputTraceFrame frame = recorded.Frames[i];
                if (frame.Tick.Value > fromTick && frame.Tick.Value <= toTick)
                    frames.Add(frame);
            }
            if (frames.Count == 0 ||
                frames[0].Tick.Value != checked(fromTick + 1) ||
                frames[frames.Count - 1].Tick.Value != toTick)
            {
                error = "Fixed input trace does not cover every Tick in the requested replay range.";
                return false;
            }
            if (!TryGetCurrentBody(actorId, out WorldBodyState body))
            {
                error = "Fixed input replay could not resolve the current Actor Body.";
                return false;
            }
            SimulationSessionCheckpoint rollback = null;
            bool replayPrepared = false;
            try
            {
                rollback = m_Transaction.CaptureCheckpoint();
                RestoreCheckpoint(checkpoint);
                TryGetCurrentBody(actorId, out body);
                using var trace = new FixedCharacterInputTrace(
                    $"{recorded.TraceId}/range/{fromTick}-{toTick}",
                    actorId,
                    recorded.TickRate,
                    frames);
                FixedCharacterInputTraceModule.PrepareReplayFromCheckpoint(trace, 0, body, true);
                replayPrepared = true;
                FixedCharacterInputTraceModule.StartReplay();
                return true;
            }
            catch (Exception exception)
            {
                if (replayPrepared)
                    FixedCharacterInputTraceModule.Stop();
                if (rollback != null)
                {
                    try
                    {
                        RestoreCheckpoint(rollback);
                    }
                    catch (Exception rollbackException)
                    {
                        error = $"{exception.Message} Rollback failed: {rollbackException.Message}";
                        return false;
                    }
                }
                error = exception.Message;
                return false;
            }
            finally
            {
                rollback?.Dispose();
            }
        }

        public bool TryPredict(
            in CharacterFutureBodyTranslationRequest request,
            CharacterFutureBodyTranslation output)
        {
            if (m_Disposed || LifecycleState != SimulationSessionLifecycleState.Active ||
                m_FutureBodyTranslationSource == null)
            {
                output?.Clear();
                return false;
            }
            return m_FutureBodyTranslationSource.TryPredict(in request, output);
        }

        public void LogicTick(SimulationSessionLogicTickContext context)
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(FixedPassPipelineRuntimeHandle));
            m_Lifecycle.RequireActive(Descriptor);
            if (!CanonicalTextValue.Equals(context.Source.ClockId, Descriptor.SourceClockId.Value, StringComparison.Ordinal) ||
                context.Source.SourceTick <= m_LatestOuterTick ||
                !context.WorldRevision.Equals(m_StateStore.Current.WorldState.WorldRevision))
            {
                Fail(new SimulationSessionFailure(
                    SimulationSessionFailureStage.Runtime,
                    "outer_tick_identity_mismatch",
                    "Outer LogicTick source clock, sequence or WorldRevision does not match the active Session.",
                    Descriptor.ExecutionBackend.ToString()));
            }
            try
            {
                m_Transaction.Execute(context);
                m_LatestOuterTick = context.Source.SourceTick;
            }
            catch (SimulationSessionCompositionException exception)
            {
                if (FixedCharacterInputTraceModule.IsSessionHistoryReplaying) throw;
                m_Lifecycle.Fail(exception.Failure);
                throw;
            }
            catch (Exception exception)
            {
                if (FixedCharacterInputTraceModule.IsSessionHistoryReplaying) throw;
                var failure = new SimulationSessionFailure(
                    SimulationSessionFailureStage.Runtime,
                    "Fixed_pipeline_runtime_failed",
                    exception.Message,
                    Descriptor.ExecutionBackend.ToString());
                m_Lifecycle.Fail(failure);
                throw new SimulationSessionCompositionException(failure, exception);
            }
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;
            m_Disposed = true;
            FixedCharacterInputTraceModule.ClearCompletedTrace();
            m_LocalInputs?.ReleaseRuntimeIngress();
            try
            {
                m_Resources.Dispose();
            }
            catch (Exception exception)
            {
                var failure = new SimulationSessionFailure(
                    SimulationSessionFailureStage.Disposal,
                    "session_resource_disposal_failed",
                    exception.Message,
                    Descriptor.ExecutionBackend.ToString());
                m_Lifecycle.Fail(failure);
                m_Lifecycle.MarkDisposed();
                throw new SimulationSessionCompositionException(failure, exception);
            }
            m_Lifecycle.MarkDisposed();
        }

        void Fail(SimulationSessionFailure failure)
        {
            if (!FixedCharacterInputTraceModule.IsSessionHistoryReplaying)
                m_Lifecycle.Fail(failure);
            throw new SimulationSessionCompositionException(failure);
        }

        bool HasExternalReplaySource()
        {
            for (int i = 0; i < m_Pipeline.Passes.Count; i++)
            {
                if (m_Pipeline.Passes[i].Descriptor.StateClass == SimulationPipelinePassStateClass.ExternalSource)
                    return true;
            }
            return false;
        }

        SimulationSessionDiagnosticsSnapshot BuildDiagnostics()
        {
            var components = new SimulationSessionComponentDiagnostic[6 + m_Passes.Count];
            components[0] = Component("ExecutionBackend", Descriptor.ExecutionBackend);
            components[1] = Component("SessionSource", Descriptor.SessionSource);
            components[2] = Component("WorldSolver", Descriptor.WorldSolver);
            components[3] = Component("SnapshotCodec", Descriptor.SnapshotCodec);
            components[4] = Component("Committer", Descriptor.Committer);
            components[5] = new SimulationSessionComponentDiagnostic(
                    "Pipeline",
                    $"{m_Pipeline.Identity}/{m_Pipeline.PlanHash}",
                    DiagnosticState());
            for (int i = 0; i < m_Passes.Count; i++)
            {
                components[6 + i] = new SimulationSessionComponentDiagnostic(
                    "Pass",
                    $"{i}:{m_Passes[i].Descriptor.VersionedIdentity}",
                    DiagnosticState(),
                    m_PassPhaseNames[i]);
            }
            return new SimulationSessionDiagnosticsSnapshot(
                Descriptor,
                LifecycleState,
                SimulationSessionPreparationStatus.Ready,
                m_LatestOuterTick,
                Failure,
                components);
        }

        SimulationSessionComponentDiagnostic Component(string component, SimulationComponentIdentity identity)
        {
            return new SimulationSessionComponentDiagnostic(component, identity.ToString(), DiagnosticState());
        }

        SimulationSessionComponentDiagnosticState DiagnosticState()
        {
            return LifecycleState switch
            {
                SimulationSessionLifecycleState.Active => SimulationSessionComponentDiagnosticState.Active,
                SimulationSessionLifecycleState.Failed => SimulationSessionComponentDiagnosticState.Failed,
                SimulationSessionLifecycleState.Disposed => SimulationSessionComponentDiagnosticState.Disposed,
                SimulationSessionLifecycleState.Preparing => SimulationSessionComponentDiagnosticState.Ready,
                _ => SimulationSessionComponentDiagnosticState.Pending
            };
        }
    }
}

