using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    public sealed class FPPassPipelineRuntimeHandle : ISimulationSessionRuntimeHandle, ISimulationSessionLocalCombatClock, ICharacterFutureBodyTranslationSource,
        ISimulationSessionCheckpointRuntime, ISimulationSessionInputReplayRuntime,
        ISimulationSessionFrameHistoryRuntime, IGameplayAbilityAvailabilitySessionReadPort, IFPActorStateReadPort
    {
        readonly SimulationSessionLifecycleController m_Lifecycle;
        readonly CompiledSimulationPipelinePlan m_Pipeline;
        readonly SimulationWorldStateStore m_StateStore;
        readonly FPPipelineTransaction m_Transaction;
        readonly IReadOnlyList<IFPCompiledPipelinePassRuntime> m_Passes;
        readonly string[] m_PassPhaseNames;
        readonly SimulationSessionResourceRegistry m_Resources;
        readonly ICharacterFutureBodyTranslationSource m_FutureBodyTranslationSource;
        readonly SimulationRuntimePortSet m_SourcePorts;
        readonly IFPLocalInputSourcePort m_LocalInputs;
        readonly FPCombatRuntime m_Combat;
        ulong m_LatestOuterTick;
        bool m_Disposed;

        internal FPPassPipelineRuntimeHandle(
            SimulationSessionCompositionDescriptor descriptor,
            CompiledSimulationPipelinePlan pipeline,
            SimulationWorldStateStore stateStore,
            FPPipelineTransaction transaction,
            IReadOnlyList<IFPCompiledPipelinePassRuntime> passes,
            SimulationSessionResourceRegistry resources,
            ICharacterFutureBodyTranslationSource futureBodyTranslationSource,
            SimulationRuntimePortSet sourcePorts,
            FPCombatRuntime combat,
            GraphBatchExecution graphBatchExecution)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            GraphBatchExecution = graphBatchExecution;
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
                if (sourcePorts.Ports[i] is IFPLocalInputSourcePort localInputs)
                    m_LocalInputs = localInputs;
            m_Combat = combat;
            m_Lifecycle = new SimulationSessionLifecycleController(descriptor);
            m_Lifecycle.BeginPreparing();
            m_Lifecycle.Activate(descriptor);
        }

        public FPCharacterRuntimeState ReadActorState(ActorId actor)
        {
            var actors = m_StateStore.Current.Actors;
            for (int i = 0; i < actors.Count; i++)
                if (actors[i].ActorId == actor)
                    return actors[i].State;
            throw new InvalidOperationException("输入命令的角色不属于当前 Session 的已提交状态。");
        }

        public void QueryAbilityAvailability(ActorId actorId, GameplayAbilityRulePhase phase,
            out GameplayAbilityAvailabilityResult result)
        {
            GraphBatchExecution.RequireIdle();
            m_Transaction.QueryAbilityAvailability(actorId, phase, out result);
        }

        public SimulationSessionCompositionDescriptor Descriptor { get; }
        public SimulationSessionLifecycleState LifecycleState => m_Lifecycle.State;
        public SimulationSessionFailure Failure => m_Lifecycle.Failure;
        public SimulationSessionDiagnosticsSnapshot Diagnostics => BuildDiagnostics();
        public ulong LastCompletedSimulationTick => m_StateStore.Current.LastCompletedTick;
        public GraphBatchExecution GraphBatchExecution { get; }
        float ISimulationSessionLocalCombatClock.WorldTimeScale => m_Combat.WorldTimeScale.AsFloat;
        public void PrepareRecordedFrames(int capacity) => m_Transaction.PrepareRecordedFrames(capacity);
        public IValueResources RecordFrame(ulong presentationFrame) => m_Transaction.RecordFrame(presentationFrame);
        public void RestoreRecordedControlInput(IValueResources state) => m_Transaction.RestoreRecordedControlInput(state);
        public void RestoreRecordedControlSources(IValueResources state) => m_Transaction.RestoreRecordedControlSources(state);

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
                throw new ObjectDisposedException(nameof(FPPassPipelineRuntimeHandle));
            GraphBatchExecution.RequireIdle();
            return m_Transaction.CaptureCheckpoint();
        }

        public bool IsInputRecording => FPCharacterInputTraceModule.IsRecording;
        public bool IsInputReplaying => FPCharacterInputTraceModule.IsSessionHistoryReplaying;

        public void RestoreCheckpoint(SimulationSessionCheckpoint checkpoint)
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(FPPassPipelineRuntimeHandle));
            GraphBatchExecution.RequireIdle();
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
                error = "FP input recording requires exactly one Actor.";
                return false;
            }
            ActorId actorId = m_StateStore.Current.Actors[0].ActorId;
            if (!TryGetCurrentBody(actorId, out WorldBodyState body))
            {
                error = "FP input recording could not resolve the current Actor Body.";
                return false;
            }
            try
            {
                FPCharacterInputTraceModule.PrepareRecordingFromCheckpoint(actorId, body);
                FPCharacterInputTraceModule.StartRecording();
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
                using FPCharacterInputTrace completed = FPCharacterInputTraceModule.StopRecording();
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
            if (!FPCharacterInputTraceModule.IsReplayActive)
            {
                error = "FP input replay is not active.";
                return false;
            }
            FPCharacterInputTraceModule.Stop();
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
                error = "FP replay range is invalid.";
                return false;
            }
            if (HasExternalReplaySource())
            {
                error = "FP replay requires an external result journal for the active Pipeline Source.";
                return false;
            }
            if (FPCharacterInputTraceModule.IsRecording)
            {
                error = "FP replay cannot start while input recording is active.";
                return false;
            }
            FPCharacterInputTrace recorded = FPCharacterInputTraceModule.LastCompletedTrace;
            if (recorded == null)
            {
                error = "No completed FP input trace is available.";
                return false;
            }
            if (m_StateStore.Current.Actors.Count != 1)
            {
                error = "FP input replay requires exactly one Actor.";
                return false;
            }
            ActorId actorId = m_StateStore.Current.Actors[0].ActorId;
            if (recorded.ActorId != actorId)
            {
                error = "FP input trace Actor does not match the active Session Actor.";
                return false;
            }
            var frames = new List<FPCharacterInputTraceFrame>();
            for (int i = 0; i < recorded.Frames.Count; i++)
            {
                FPCharacterInputTraceFrame frame = recorded.Frames[i];
                if (frame.Tick.Value > fromTick && frame.Tick.Value <= toTick)
                    frames.Add(frame);
            }
            if (frames.Count == 0 ||
                frames[0].Tick.Value != checked(fromTick + 1) ||
                frames[frames.Count - 1].Tick.Value != toTick)
            {
                error = "FP input trace does not cover every Tick in the requested replay range.";
                return false;
            }
            if (!TryGetCurrentBody(actorId, out WorldBodyState body))
            {
                error = "FP input replay could not resolve the current Actor Body.";
                return false;
            }
            SimulationSessionCheckpoint rollback = null;
            bool replayPrepared = false;
            try
            {
                rollback = CaptureCheckpoint();
                RestoreCheckpoint(checkpoint);
                TryGetCurrentBody(actorId, out body);
                using var trace = new FPCharacterInputTrace(
                    $"{recorded.TraceId}/range/{fromTick}-{toTick}",
                    actorId,
                    recorded.TickRate,
                    frames);
                FPCharacterInputTraceModule.PrepareReplayFromCheckpoint(trace, 0, body, true);
                replayPrepared = true;
                FPCharacterInputTraceModule.StartReplay();
                return true;
            }
            catch (Exception exception)
            {
                if (replayPrepared)
                    FPCharacterInputTraceModule.Stop();
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
                throw new ObjectDisposedException(nameof(FPPassPipelineRuntimeHandle));
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
                if (FPCharacterInputTraceModule.IsSessionHistoryReplaying) throw;
                m_Lifecycle.Fail(exception.Failure);
                throw;
            }
            catch (Exception exception)
            {
                if (FPCharacterInputTraceModule.IsSessionHistoryReplaying) throw;
                var failure = new SimulationSessionFailure(
                    SimulationSessionFailureStage.Runtime,
                    "FP_pipeline_runtime_failed",
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
            GraphBatchExecution.RequireIdle();
            m_Disposed = true;
            FPCharacterInputTraceModule.ClearCompletedTrace();
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
            if (!FPCharacterInputTraceModule.IsSessionHistoryReplaying)
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
            var components = new SimulationSessionComponentDiagnostic[7 + m_Passes.Count];
            components[0] = Component("ExecutionBackend", Descriptor.ExecutionBackend);
            components[1] = Component("SessionSource", Descriptor.SessionSource);
            components[2] = Component("WorldSolver", Descriptor.WorldSolver);
            components[3] = Component("SnapshotProvider", Descriptor.SnapshotProvider);
            components[4] = Component("Committer", Descriptor.Committer);
            components[5] = new SimulationSessionComponentDiagnostic(
                    "Pipeline",
                    $"{m_Pipeline.Identity}/{m_Pipeline.PlanHash}",
                    DiagnosticState());
            for (int i = 0; i < m_Passes.Count; i++)
            {
                components[7 + i] = new SimulationSessionComponentDiagnostic(
                    "Pass",
                    $"{i}:{m_Passes[i].Descriptor.VersionedIdentity}",
                    DiagnosticState(),
                    m_PassPhaseNames[i]);
            }
            components[6] = new SimulationSessionComponentDiagnostic(
                "GraphBatch", Descriptor.SessionId.ToString(), DiagnosticState(),
                $"请求={GraphBatchExecution.RequestedMode};执行={GraphBatchExecution.ResolvedMode};实例={GraphBatchExecution.IndependentInstanceCount};能力={GraphBatchExecution.ExecutionUnits};在途={GraphBatchExecution.IsInFlight};失败实例={GraphBatchExecution.FailureInstanceIndex};原因={GraphBatchExecution.LastFailure?.Message}");
            return new SimulationSessionDiagnosticsSnapshot(
                Descriptor,
                LifecycleState,
                SimulationSessionPreparationStatus.Ready,
                m_LatestOuterTick,
                LastCompletedSimulationTick,
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

