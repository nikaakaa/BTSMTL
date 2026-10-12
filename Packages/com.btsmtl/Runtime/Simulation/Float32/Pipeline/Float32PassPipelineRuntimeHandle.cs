using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public sealed class Float32PassPipelineRuntimeHandle : ISimulationSessionRuntimeHandle, ISimulationSessionCheckpointRuntime, ISimulationSessionInputReplayRuntime, IGameplayAbilityAvailabilitySessionReadPort
    {
        readonly SimulationSessionLifecycleController m_Lifecycle;
        readonly CompiledSimulationPipelinePlan m_Pipeline;
        readonly SimulationWorldStateStore m_StateStore;
        readonly Float32PipelineTransaction m_Transaction;
        readonly IReadOnlyList<IFloat32CompiledPipelinePassRuntime> m_Passes;
        readonly string[] m_PassPhaseNames;
        readonly SimulationSessionResourceRegistry m_Resources;
        ulong m_LatestOuterTick;
        bool m_Disposed;

        internal Float32PassPipelineRuntimeHandle(
            SimulationSessionCompositionDescriptor descriptor,
            CompiledSimulationPipelinePlan pipeline,
            SimulationWorldStateStore stateStore,
            Float32PipelineTransaction transaction,
            IReadOnlyList<IFloat32CompiledPipelinePassRuntime> passes,
            SimulationSessionResourceRegistry resources,
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
            m_Lifecycle = new SimulationSessionLifecycleController(descriptor);
            m_Lifecycle.BeginPreparing();
            m_Lifecycle.Activate(descriptor);
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
        public SimulationSessionCheckpoint CaptureCheckpoint()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(Float32PassPipelineRuntimeHandle));
            GraphBatchExecution.RequireIdle();
            return m_Transaction.CaptureCheckpoint();
        }

        public bool IsInputRecording => Float32CharacterInputTraceModule.IsRecording;

        public void RestoreCheckpoint(SimulationSessionCheckpoint checkpoint)
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(Float32PassPipelineRuntimeHandle));
            GraphBatchExecution.RequireIdle();
            m_Transaction.RestoreCheckpoint(checkpoint);
            m_LatestOuterTick = checkpoint.Tick.Value;
        }

        public bool TryStartInputRecording(out string error)
        {
            error = string.Empty;
            if (m_StateStore.Current.Actors.Count != 1)
            {
                error = "Float32 input recording requires exactly one Actor.";
                return false;
            }
            try
            {
                m_Transaction.PrepareInputRecording(m_StateStore.Current.Actors[0].ActorId);
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
                Float32CharacterInputTraceModule.StopRecording();
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
            if (!Float32CharacterInputTraceModule.IsReplayActive)
            {
                error = "Float32 input replay is not active.";
                return false;
            }
            Float32CharacterInputTraceModule.Stop();
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
                error = "Float32 replay range is invalid.";
                return false;
            }
            if (HasExternalReplaySource())
            {
                error = "Float32 replay requires an external result journal for the active Pipeline Source.";
                return false;
            }
            if (Float32CharacterInputTraceModule.IsRecording)
            {
                error = "Float32 replay cannot start while input recording is active.";
                return false;
            }
            Float32CharacterInputTrace recorded = Float32CharacterInputTraceModule.LastCompletedTrace;
            if (recorded == null)
            {
                error = "No completed Float32 input trace is available.";
                return false;
            }
            if (m_StateStore.Current.Actors.Count != 1 ||
                recorded.ActorId != m_StateStore.Current.Actors[0].ActorId)
            {
                error = "Float32 input trace Actor does not match the active Session Actor.";
                return false;
            }
            var frames = new List<Float32CharacterInputTraceFrame>();
            for (int i = 0; i < recorded.Frames.Count; i++)
            {
                Float32CharacterInputTraceFrame frame = recorded.Frames[i];
                if (frame.Tick.Value > fromTick && frame.Tick.Value <= toTick)
                    frames.Add(frame);
            }
            if (frames.Count == 0 ||
                frames[0].Tick.Value != checked(fromTick + 1) ||
                frames[frames.Count - 1].Tick.Value != toTick)
            {
                error = "Float32 input trace does not cover every Tick in the requested replay range.";
                return false;
            }
            SimulationSessionCheckpoint rollback = null;
            bool replayPrepared = false;
            try
            {
                rollback = CaptureCheckpoint();
                RestoreCheckpoint(checkpoint);
                using (var replay = new Float32CharacterInputTrace($"{recorded.TraceId}/range/{fromTick}-{toTick}", recorded.ActorId, frames))
                    Float32CharacterInputTraceModule.PrepareReplay(replay);
                replayPrepared = true;
                return true;
            }
            catch (Exception exception)
            {
                if (replayPrepared)
                    Float32CharacterInputTraceModule.Stop();
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

        public void LogicTick(SimulationSessionLogicTickContext context)
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(Float32PassPipelineRuntimeHandle));
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
                m_Lifecycle.Fail(exception.Failure);
                throw;
            }
            catch (Exception exception)
            {
                var failure = new SimulationSessionFailure(
                    SimulationSessionFailureStage.Runtime,
                    "float32_pipeline_runtime_failed",
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
            Float32CharacterInputTraceModule.ClearCompletedTrace();
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
            components[3] = Component("SnapshotCodec", Descriptor.SnapshotCodec);
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
