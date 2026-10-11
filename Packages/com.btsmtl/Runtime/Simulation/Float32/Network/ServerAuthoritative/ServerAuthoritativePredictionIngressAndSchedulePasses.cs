using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    public sealed class ServerAuthoritativeOwnerInputIngressPassRuntimeFactory : IFloat32PipelinePassRuntimeFactory
    {
        readonly SimulationPipelinePassFactoryDescriptor m_Descriptor;

        public ServerAuthoritativeOwnerInputIngressPassRuntimeFactory(ServerAuthoritativeModelPolicy policy)
        {
            m_Descriptor = ServerAuthoritativePipelinePassContracts.CreateFactoryDescriptor(
                ServerAuthoritativePipelinePassContracts.OwnerInputIngress(policy));
        }

        public SimulationPipelinePassFactoryDescriptor Descriptor => m_Descriptor;

        public IFloat32CompiledPipelinePassRuntime Create(Float32PipelinePassRuntimeFactoryContext context)
        {
            var reads = new OwnerInputIngressReads(
                context.BindSourcePort<IFloat32LocalInputSourcePort>(Float32LocalInputSourcePortContract.PortId),
                context.BindTargetPort<IFloat32CharacterRuntimePort>(Float32PipelineRuntimePortIds.CharacterRuntime),
                context.BindTargetPort<ICommittedActorPoseReadPort<Float32Vector3, Float32Yaw>>(Float32PipelineRuntimePortIds.CommittedObservation));
            var writes = new OwnerInputIngressWrites(
                context.Products.BindExclusiveWriter<OwnerCanonicalInputBatch>(ServerAuthoritativeProducts.OwnerCanonicalInputBatch));
            return new Float32IngressPassRuntimeAdapter<OwnerInputIngressReads, OwnerInputIngressWrites>(
                new OwnerInputIngressRuntime(context.Pass.Descriptor),
                reads,
                writes);
        }
    }

    public sealed class OwnerInputIngressRuntime :
        Float32PipelinePassRuntimeBase,
        ISimulationIngressPassRuntime<OwnerInputIngressReads, OwnerInputIngressWrites>
    {
        ulong m_FirstOuterSourceTick;
        ulong m_LastModelSourceTick;

        public OwnerInputIngressRuntime(SimulationPipelinePassDescriptor descriptor) : base(descriptor) { }

        public void Execute(
            SimulationPipelineIngressContext context,
            OwnerInputIngressReads readPorts,
            OwnerInputIngressWrites writePorts)
        {
            RequireExecution();
            Float32CharacterRuntime characterRuntime = readPorts.CharacterRuntime.Runtime;
            if (readPorts.CharacterRuntime.Runtime.Roster.Count != 1)
                throw new InvalidOperationException("Prediction Owner Input Pass requires a one-Actor simulation roster.");
            var nextTick = new SimulationTick(checked(context.CurrentCompletedTick + 1));
            SimulationTickSourceIdentity modelSource = MapModelSource(context.Source);
            Float32LocalInputFrame frame = readPorts.Source.Read(
                modelSource,
                nextTick,
                characterRuntime.NumericProfile,
                characterRuntime.TickRate,
                readPorts.CharacterRuntime.Runtime.Roster,
                readPorts.CommittedObservation.Read());
            SimulationPipelineActorInput<Float32StepInput> input = frame.CanonicalInputs.Inputs[0];
            writePorts.OwnerInput.Write(new OwnerCanonicalInputBatch(
                input.ActorId,
                modelSource.SourceTick,
                input.Sequence,
                input.Value.Input));
        }

        SimulationTickSourceIdentity MapModelSource(SimulationTickSourceIdentity outer)
        {
            if (m_FirstOuterSourceTick == 0)
                m_FirstOuterSourceTick = outer.SourceTick;
            ulong sourceTick = checked(outer.SourceTick - m_FirstOuterSourceTick + 1);
            if (sourceTick <= m_LastModelSourceTick)
                throw new InvalidOperationException("Prediction model source Tick did not advance monotonically.");
            m_LastModelSourceTick = sourceTick;
            return new SimulationTickSourceIdentity(outer.Kind, outer.ClockId, sourceTick);
        }
    }

    public sealed class OwnerInputIngressReads : ISimulationPipelineReadPortSet
    {
        public OwnerInputIngressReads(
            IFloat32LocalInputSourcePort source,
            IFloat32CharacterRuntimePort characterRuntime,
            ICommittedActorPoseReadPort<Float32Vector3, Float32Yaw> committedObservation)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            CommittedObservation = committedObservation ?? throw new ArgumentNullException(nameof(committedObservation));
        }
        public IFloat32LocalInputSourcePort Source { get; }
        public IFloat32CharacterRuntimePort CharacterRuntime { get; }
        public ICommittedActorPoseReadPort<Float32Vector3, Float32Yaw> CommittedObservation { get; }
    }

    public sealed class OwnerInputIngressWrites : ISimulationPipelineWritePortSet
    {
        public OwnerInputIngressWrites(IExclusiveSimulationPipelineProductWriter<OwnerCanonicalInputBatch> ownerInput)
        {
            OwnerInput = ownerInput ?? throw new ArgumentNullException(nameof(ownerInput));
        }
        public IExclusiveSimulationPipelineProductWriter<OwnerCanonicalInputBatch> OwnerInput { get; }
    }

    public sealed class ServerAuthoritativeObservationIngressPassRuntimeFactory : IFloat32PipelinePassRuntimeFactory
    {
        readonly SimulationPipelinePassFactoryDescriptor m_Descriptor;

        public ServerAuthoritativeObservationIngressPassRuntimeFactory(ServerAuthoritativeModelPolicy policy)
        {
            m_Descriptor = ServerAuthoritativePipelinePassContracts.CreateFactoryDescriptor(
                ServerAuthoritativePipelinePassContracts.ObservationIngress(policy));
        }

        public SimulationPipelinePassFactoryDescriptor Descriptor => m_Descriptor;

        public IFloat32CompiledPipelinePassRuntime Create(Float32PipelinePassRuntimeFactoryContext context)
        {
            var reads = new ObservationIngressReads(
                context.BindSourcePort<IServerAuthoritativeObservationSourcePort>(ServerAuthoritativeSourcePortContracts.ObservationPortId),
                context.BindSourcePort<IServerAuthoritativePredictionStatePort>(ServerAuthoritativeSourcePortContracts.PredictionStatePortId));
            var writes = new ObservationIngressWrites(
                context.Products.BindExclusiveWriter<AuthoritativeObservationBatch>(ServerAuthoritativeProducts.AuthoritativeObservationBatch),
                context.Products.BindExclusiveWriter<RemotePresentationBatch>(ServerAuthoritativeProducts.RemotePresentationBatch));
            return new Float32IngressPassRuntimeAdapter<ObservationIngressReads, ObservationIngressWrites>(
                new ObservationIngressRuntime(context.Pass.Descriptor),
                reads,
                writes);
        }
    }

    public sealed class ObservationIngressRuntime :
        Float32PipelinePassRuntimeBase,
        ISimulationIngressPassRuntime<ObservationIngressReads, ObservationIngressWrites>
    {
        AuthoritativeObservationBatch? m_Observation;

        public ObservationIngressRuntime(SimulationPipelinePassDescriptor descriptor) : base(descriptor) { }

        public void Execute(
            SimulationPipelineIngressContext context,
            ObservationIngressReads readPorts,
            ObservationIngressWrites writePorts)
        {
            RequireExecution();
            ReleaseObservation();
            try
            {
                AuthoritativeObservationBatch batch = readPorts.Source.Drain(context.Source);
                if (!batch.IsValid)
                    throw new InvalidOperationException("Prediction observation Source returned no canonical batch.");
                m_Observation = batch;
                if (batch.RemotePresentation.Count != 1)
                    throw new InvalidOperationException("Prediction observation batch must contain the locked remote presentation Actor.");
                RemotePresentationBatch remote = batch.RemotePresentation[0];
                readPorts.PredictionState.State.ObserveRemotePresentation(remote);
                writePorts.Observations.Write(batch);
                writePorts.RemotePresentation.Write(new RemotePresentationBatch(
                    remote.ActorId,
                    Array.Empty<CharacterBodySample>(),
                    ServerAuthoritativeProductOrder.CopyToArray(remote.SampleCommands),
                    ServerAuthoritativeProductOrder.CopyToArray(remote.ReliableEvents),
                    false));
            }
            catch
            {
                ReleaseObservation();
                throw;
            }
        }

        void ReleaseObservation()
        {
            if (m_Observation.HasValue)
                m_Observation.Value.ReleaseText();
            m_Observation = null;
        }

        protected override void OnDispose() => ReleaseObservation();
    }

    public sealed class ObservationIngressReads : ISimulationPipelineReadPortSet
    {
        public ObservationIngressReads(
            IServerAuthoritativeObservationSourcePort source,
            IServerAuthoritativePredictionStatePort predictionState)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            PredictionState = predictionState ?? throw new ArgumentNullException(nameof(predictionState));
        }
        public IServerAuthoritativeObservationSourcePort Source { get; }
        public IServerAuthoritativePredictionStatePort PredictionState { get; }
    }

    public sealed class ObservationIngressWrites : ISimulationPipelineWritePortSet
    {
        public ObservationIngressWrites(
            IExclusiveSimulationPipelineProductWriter<AuthoritativeObservationBatch> observations,
            IExclusiveSimulationPipelineProductWriter<RemotePresentationBatch> remotePresentation)
        {
            Observations = observations ?? throw new ArgumentNullException(nameof(observations));
            RemotePresentation = remotePresentation ?? throw new ArgumentNullException(nameof(remotePresentation));
        }
        public IExclusiveSimulationPipelineProductWriter<AuthoritativeObservationBatch> Observations { get; }
        public IExclusiveSimulationPipelineProductWriter<RemotePresentationBatch> RemotePresentation { get; }
    }

    public sealed class ServerAuthoritativeCorrectionSchedulePassRuntimeFactory : IFloat32PipelinePassRuntimeFactory, ISimulationScheduleCapacity
    {
        readonly ServerAuthoritativeModelPolicy m_Policy;
        readonly SimulationPipelinePassFactoryDescriptor m_Descriptor;

        public ServerAuthoritativeCorrectionSchedulePassRuntimeFactory(ServerAuthoritativeModelPolicy policy)
        {
            m_Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            m_Descriptor = ServerAuthoritativePipelinePassContracts.CreateFactoryDescriptor(
                ServerAuthoritativePipelinePassContracts.CorrectionSchedule(policy),
                ServerAuthoritativePredictionPassIds.CorrectionStateSchema,
                3);
        }

        public SimulationPipelinePassFactoryDescriptor Descriptor => m_Descriptor;

        public GameplayEffectScheduleCapacity GameplayEffectCapacity => m_Policy.PredictionGameplayEffectCapacity;
        public int MaximumStepCount => GameplayEffectCapacity.MaximumStepCount;

        public IFloat32CompiledPipelinePassRuntime Create(Float32PipelinePassRuntimeFactoryContext context)
        {
            var reads = new CorrectionScheduleReads(
                context.Products.BindExclusiveReader<OwnerCanonicalInputBatch>(ServerAuthoritativeProducts.OwnerCanonicalInputBatch),
                context.Products.BindExclusiveReader<AuthoritativeObservationBatch>(ServerAuthoritativeProducts.AuthoritativeObservationBatch),
                context.BindSourcePort<IServerAuthoritativePredictionStatePort>(ServerAuthoritativeSourcePortContracts.PredictionStatePortId),
                context.BindSourcePort<IServerAuthoritativePredictionRestorePort>(ServerAuthoritativeSourcePortContracts.PredictionRestorePortId),
                context.BindTargetPort<IFloat32CharacterRuntimePort>(Float32PipelineRuntimePortIds.CharacterRuntime),
                context.BindTargetPort<IFloat32WorldSolverRuntimePort>(Float32PipelineRuntimePortIds.WorldSolver),
                context.BindDiagnosticsPort<IFloat32DiagnosticsRuntimePort>(Float32PipelineRuntimePortIds.Diagnostics));
            var writes = new CorrectionScheduleWrites(
                context.Products.BindExclusiveWriter<PredictionCorrectionDecision>(ServerAuthoritativeProducts.PredictionCorrectionDecision),
                context.Products.BindExclusiveWriter<SelectedRemoteBodyBatch>(ServerAuthoritativeProducts.SelectedRemoteBodyBatch),
                context.Products.BindExclusiveWriter<SimulationSessionExecutionPlan<Float32SimulationStep>>(SimulationPipelineProducts.ExecutionPlan));
            return new Float32SchedulePassRuntimeAdapter<CorrectionScheduleReads, CorrectionScheduleWrites>(
                new CorrectionScheduleRuntime(context.Pass.Descriptor, m_Policy, reads.PredictionState.State,
                    context.Composition.SourceClockId.Value).PrepareStateStorage(context.Composition),
                reads,
                writes);
        }
    }

    public sealed class CorrectionScheduleRuntime :
        Float32PipelinePassRuntimeBase,
        ISimulationExecutionPlanSchedulePassRuntime<CorrectionScheduleReads, CorrectionScheduleWrites>,
        ISimulationPipelineStateParticipant
    {
        public GameplayEffectScheduleCapacity GameplayEffectCapacity => m_Policy.PredictionGameplayEffectCapacity;
        public int MaximumStepCount => GameplayEffectCapacity.MaximumStepCount;
        readonly ServerAuthoritativeModelPolicy m_Policy;
        readonly ServerAuthoritativePredictionState m_State;
        ulong m_DecisionCount;
        ulong m_CorrectionCount;
        ulong m_ReplayTickCount;
        ulong m_HardRecoveryCount;
        ulong m_ZeroCurrentStepCount;
        ulong m_OneCurrentStepCount;
        ulong m_TwoCurrentStepCount;
        readonly string m_ReplayClockId;

        public CorrectionScheduleRuntime(
            SimulationPipelinePassDescriptor descriptor,
            ServerAuthoritativeModelPolicy policy,
            ServerAuthoritativePredictionState state, string sourceClockId) : base(descriptor)
        {
            m_ReplayClockId = string.Concat(sourceClockId, ".replay");
            m_Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            m_State = state ?? throw new ArgumentNullException(nameof(state));
            StateIdentity = new SimulationPipelineStateParticipantIdentity(
                descriptor.PassId,
                descriptor.ImplementationVersion,
                ServerAuthoritativePredictionPassIds.CorrectionStateOwner,
                ServerAuthoritativePredictionPassIds.CorrectionStateSchema,
                3);
        }

        ServerAuthoritativePassStateRestoreTransaction m_StateStorage;

        internal CorrectionScheduleRuntime PrepareStateStorage(SimulationSessionCompositionDescriptor composition)
        {
            m_State.PrepareWorldReadStorage(composition.MaximumSessionCheckpointPayloadBytes);
            m_StateStorage = new ServerAuthoritativePassStateRestoreTransaction(this,
                composition.MaximumSessionCheckpointPayloadBytes, MaximumTextValues,
                checked(1 + m_Policy.PredictionGameplayEffectCapacity.MaximumStepCount +
                    composition.RecordedFrameCapacity + m_Policy.HistoryCapacity), m_State.RestoreCorrectionState);
            return this;
        }

        public SimulationPipelineStateParticipantIdentity StateIdentity { get; }
        public SimulationPipelineStepProjectionMode StepProjectionMode => SimulationPipelineStepProjectionMode.Include;
        public int MaximumTextValues => m_Policy.HistoryCapacity;

        public ISimulationPipelinePassStateCheckpoint CaptureCheckpoint()
        {
            RequireCaptureOrRestore();
            ServerAuthoritativePredictionCorrectionCheckpoint checkpoint = m_State.CaptureCorrectionCheckpoint();
            return new SimulationPipelinePassStateCheckpoint(
                StateIdentity,
                () => m_State.RestoreCorrectionCheckpoint(checkpoint));
        }

        public void Execute(
            SimulationPipelineScheduleContext context,
            CorrectionScheduleReads readPorts,
            CorrectionScheduleWrites writePorts)
        {
            RequireExecution();
            OwnerCanonicalInputBatch current = readPorts.OwnerInput.Read();
            AuthoritativeObservationBatch observations = readPorts.Observations.Read();
            if (observations.RemotePresentation.Count != 1)
                throw new InvalidOperationException("Prediction Schedule requires the locked remote Actor observation stream.");
            bool observedContactEnabled =
                (readPorts.WorldSolver.Solver.Descriptor.Features & WorldFeature.ObservedKinematicActorContact) != 0;
            IObservedWorldConstraintProfileProvider observedProfile =
                readPorts.WorldSolver.Solver as IObservedWorldConstraintProfileProvider;
            if (observedContactEnabled && observedProfile == null)
            {
                throw new InvalidOperationException(
                    "Prediction World Solver declares observed kinematic Actor contact without providing its locked contact profile.");
            }
            m_State.ApplyAck(observations.OwnerAck);
            m_State.ObserveAuthorityClock(observations.AuthorityTickEstimate);
            ulong targetCompletedTick = checked(m_State.LastAuthorityClockEstimate + (ulong)m_Policy.CommandSlackTicks);
            ulong currentGap = targetCompletedTick > context.CurrentCompletedTick
                ? targetCompletedTick - context.CurrentCompletedTick
                : 0;
            int currentStepCount = checked((int)Math.Min(currentGap, 2UL));
            bool observationPriming = !m_State.IsRemoteObservationPrimed;
            if (observationPriming)
                currentStepCount = 0;
            current = WithRequests(
                current,
                m_State.ScheduleRequests(current.Input.Requests, currentStepCount > 0));
            ulong lastPredictedInputSequence = m_State.LastPredictedInputSequence;
            AuthoritativeActorBaseline baseline = FindOwnerBaseline(observations, current.ActorId);
            PredictionCorrectionDecision decision;
            SimulationRestoreDirective restore = null;
            IReadOnlyList<ServerAuthoritativePredictionHistoryRecord> replay = Array.Empty<ServerAuthoritativePredictionHistoryRecord>();
            if (!baseline.IsValid)
            {
                var next = new SimulationTick(checked(context.CurrentCompletedTick + 1));
                decision = new PredictionCorrectionDecision(
                    PredictionCorrectionDecisionKind.NoCorrection,
                    PredictionCorrectionReason.NoAuthoritativeBaseline,
                    next,
                    default,
                    default,
                    default,
                    Float32Scalar.Zero,
                    Float32Scalar.Zero);
            }
            else
            {
                decision = m_State.Decide(baseline);
                if (decision.Kind != PredictionCorrectionDecisionKind.NoCorrection)
                {
                    replay = decision.Kind == PredictionCorrectionDecisionKind.RestoreReplay
                        ? m_State.GetReplayAfter(baseline.ConfirmedInputSequence)
                        : Array.Empty<ServerAuthoritativePredictionHistoryRecord>();
                    restore = m_State.BuildRestore(baseline, decision, context.Pipeline);
                }
            }
            writePorts.Decision.Write(decision);
            SimulationSessionExecutionPlan<Float32SimulationStep> plan;
            CharacterBodySample[] selectedRemoteBodies;
            try
            {
                plan = BuildPlan(
                    context,
                    readPorts.CharacterRuntime,
                    current,
                    replay,
                    restore,
                    currentStepCount,
                    lastPredictedInputSequence,
                    observedContactEnabled,
                    observedContactEnabled
                        ? observedProfile.ObservedContactShapeConfigurationHash
                        : default,
                    out selectedRemoteBodies);
            }
            catch (InvalidOperationException exception)
            {
                if (readPorts.Diagnostics.Sink.IsEnabled)
                {
                    readPorts.Diagnostics.Sink.PublishModel(new SimulationModelTraceRecord(
                        SimulationModelTraceKind.Failure,
                        "prediction_remote_body_selection_failure",
                        exception.Message,
                        current.ActorId,
                        context.Source.SourceTick,
                        targetCompletedTick,
                        current.InputSequence,
                        m_State.ConfirmedInputSequence,
                        m_State.RemoteBodySampleCount,
                        replay.Count,
                        m_Policy.MaximumRemoteBodyExtrapolationTicks,
                        0f,
                        false));
                }
                throw;
            }
            if (decision.Kind == PredictionCorrectionDecisionKind.HardRecovery && selectedRemoteBodies.Length == 0)
            {
                if (restore == null)
                    throw new InvalidOperationException("HardRecovery requires an explicit restore Tick for the remote visual reset anchor.");
                ServerAuthoritativeRemoteBodySelectionFrame recoverySelection =
                    m_State.SelectRemoteBodyFrame(restore.Tick);
                selectedRemoteBodies = new CharacterBodySample[recoverySelection.Selections.Count];
                recoverySelection.CopyBodySamples(selectedRemoteBodies);
                if (selectedRemoteBodies.Length == 0)
                    throw new InvalidOperationException("HardRecovery produced no selected remote Body reset anchor.");
            }
            writePorts.SelectedRemoteBodies.Write(new SelectedRemoteBodyBatch(
                observations.RemotePresentation[0].ActorId,
                decision.BaselineTick,
                selectedRemoteBodies,
                decision.Kind == PredictionCorrectionDecisionKind.HardRecovery));
            writePorts.ExecutionPlan.Write(plan);
            if (readPorts.Diagnostics.Sink.IsEnabled)
            {
                for (int stepIndex = 0; stepIndex < plan.Steps.Count; stepIndex++)
                {
                    Float32SimulationStep step = plan.Steps[stepIndex];
                    ObservedWorldConstraintFrame frame = step.ObservedWorldConstraints;
                    string samples = string.Empty;
                    for (int observedIndex = 0; observedIndex < frame.Constraints.Count; observedIndex++)
                    {
                        ObservedWorldConstraint observed = frame.Constraints[observedIndex];
                        samples += $"{(observedIndex == 0 ? string.Empty : ",")}{observed.ActorId}:{observed.SamplingKind}:{observed.SourcePreviousTick}-{observed.SourceCurrentTick}";
                    }
                    readPorts.Diagnostics.Sink.PublishModel(new SimulationModelTraceRecord(
                        SimulationModelTraceKind.Queue,
                        "prediction_observed_world_frame",
                        $"execution={step.ExecutionKind};tick={step.Tick};frame={frame.FrameHash};samples={samples}",
                        current.ActorId,
                        context.Source.SourceTick,
                        step.Tick.Value,
                        current.InputSequence,
                        m_State.ConfirmedInputSequence,
                        frame.Constraints.Count));
                }
            }
            m_DecisionCount++;
            if (decision.Kind != PredictionCorrectionDecisionKind.NoCorrection)
                m_CorrectionCount++;
            m_ReplayTickCount = checked(m_ReplayTickCount + (ulong)replay.Count);
            if (decision.Kind == PredictionCorrectionDecisionKind.HardRecovery)
                m_HardRecoveryCount++;
            switch (currentStepCount)
            {
                case 0:
                    m_ZeroCurrentStepCount++;
                    break;
                case 1:
                    m_OneCurrentStepCount++;
                    break;
                case 2:
                    m_TwoCurrentStepCount++;
                    break;
            }
            if (readPorts.Diagnostics.Sink.IsEnabled)
            {
                int replayCount = decision.ReplayStart.IsValid
                    ? checked((int)(decision.ReplayEnd.Value - decision.ReplayStart.Value + 1))
                    : 0;
                string observedBaselineDifference = DescribeObservedBaselineDifference(
                    baseline,
                    observations,
                    m_State);
                readPorts.Diagnostics.Sink.PublishModel(new SimulationModelTraceRecord(
                    SimulationModelTraceKind.Correction,
                    "prediction_correction_decision",
                    $"kind={decision.Kind};reason={decision.Reason};baseline={decision.BaselineTick};restore={decision.RestoreTick};replay={decision.ReplayStart}-{decision.ReplayEnd};history={m_State.HistoryCount};stateMatch={decision.Reason != PredictionCorrectionReason.CharacterStateMismatch};remoteFrameDifference={observedBaselineDifference};positionError={decision.PositionError.Value:0.######};yawError={decision.YawError.Value:0.######};decisions={m_DecisionCount};corrections={m_CorrectionCount};correctionRate={m_CorrectionCount / (double)m_DecisionCount:0.######};replayTicks={m_ReplayTickCount};hardRecoveries={m_HardRecoveryCount}",
                    current.ActorId,
                    context.Source.SourceTick,
                    decision.BaselineTick.Value,
                    current.InputSequence,
                    m_State.ConfirmedInputSequence,
                    m_State.HistoryCount,
                    replayCount,
                    decision.PositionError.Value,
                    decision.YawError.Value,
                    true));
                readPorts.Diagnostics.Sink.PublishModel(new SimulationModelTraceRecord(
                    SimulationModelTraceKind.Correction,
                    "prediction_clock_discipline",
                    $"authorityEstimate={m_State.LastAuthorityClockEstimate};target={targetCompletedTick};completed={context.CurrentCompletedTick};currentSteps={currentStepCount};priming={observationPriming};remoteSamples={m_State.RemoteBodySampleCount};remoteCapacityPerActor={m_State.RemoteBodyCapacityPerActor};remoteFirst={m_State.RemoteBodyFirstSampleTick};remoteLast={m_State.RemoteBodyLastSampleTick};remoteEvictions={m_State.RemoteBodyEvictionCount};slack={m_Policy.CommandSlackTicks};zero={m_ZeroCurrentStepCount};one={m_OneCurrentStepCount};two={m_TwoCurrentStepCount}",
                    current.ActorId,
                    context.Source.SourceTick,
                    m_State.LastAuthorityClockEstimate,
                    current.InputSequence,
                    m_State.ConfirmedInputSequence,
                    currentStepCount));
            }
        }

        protected override void OnDispose() => m_StateStorage.ReleaseStorage();

        public void WriteCheckpointState(CanonicalWriter writer)
        {
            RequireCaptureOrRestore();
            m_State.WriteCorrectionState(writer);
        }

        public SimulationPipelinePassStateSnapshot CaptureState()
        {
            RequireCaptureOrRestore();
            byte[] payload = m_State.CaptureCorrectionState();
            return m_StateStorage.CaptureState();
        }

        public ISimulationPipelinePassRestoreTransaction PrepareRestore(SimulationPipelinePassStateSnapshot snapshot)
        {
            RequireCaptureOrRestore();
            return m_StateStorage.Prepare(snapshot);
        }

        static AuthoritativeActorBaseline FindOwnerBaseline(AuthoritativeObservationBatch batch, ActorId owner)
        {
            for (int i = batch.Baselines.Count - 1; i >= 0; i--)
            {
                if (batch.Baselines[i].ActorId == owner)
                    return batch.Baselines[i];
            }
            return default;
        }

        static string DescribeObservedBaselineDifference(
            AuthoritativeActorBaseline ownerBaseline,
            AuthoritativeObservationBatch observations,
            ServerAuthoritativePredictionState state)
        {
            if (!ownerBaseline.IsValid)
                return "unavailable:no-owner-baseline";
            if (!state.TryGetHistory(ownerBaseline.AuthorityTick, out ServerAuthoritativePredictionHistoryRecord history))
                return "unavailable:no-local-history";
            ObservedWorldConstraintFrame frame = history.ObservedWorldConstraints;
            if (frame.Constraints.Count == 0)
                return "disabled";
            for (int i = 0; i < frame.Constraints.Count; i++)
            {
                ObservedWorldConstraint observed = frame.Constraints[i];
                AuthoritativeActorBaseline remote = default;
                for (int baselineIndex = observations.Baselines.Count - 1; baselineIndex >= 0; baselineIndex--)
                {
                    AuthoritativeActorBaseline candidate = observations.Baselines[baselineIndex];
                    if (candidate.ActorId == observed.ActorId && candidate.AuthorityTick == ownerBaseline.AuthorityTick)
                    {
                        remote = candidate;
                        break;
                    }
                }
                if (!remote.IsValid)
                    return $"unavailable:{frame.FrameHash}";
                if (!WorldSolveBatchRequest.BodyEquals(observed.FinalBody, remote.Body))
                    return $"different:{frame.FrameHash}";
            }
            return $"match:{frame.FrameHash}";
        }

        SimulationSessionExecutionPlan<Float32SimulationStep> BuildPlan(
            SimulationPipelineScheduleContext context,
            IFloat32CharacterRuntimePort characterRuntime,
            OwnerCanonicalInputBatch current,
            IReadOnlyList<ServerAuthoritativePredictionHistoryRecord> replay,
            SimulationRestoreDirective restore,
            int currentStepCount,
            ulong lastPredictedInputSequence,
            bool observedContactEnabled,
            StableHash contactShapeConfigurationHash,
            out CharacterBodySample[] selectedRemoteBodies)
        {
            if (characterRuntime.Runtime.Roster.Count != 1 || characterRuntime.Runtime.Roster[0].ActorId != current.ActorId)
                throw new InvalidOperationException("Prediction Schedule owner does not match the Character roster.");
            if (currentStepCount < 0 || currentStepCount > 2)
                throw new ArgumentOutOfRangeException(nameof(currentStepCount));
            var steps = new Float32SimulationStep[replay.Count + currentStepCount];
            var mappings = new SimulationPipelineStepSourceMapping[
                (currentStepCount > 0 ? 1 : 0) + (replay.Count > 0 ? 1 : 0)];
            CharacterBodySample[] selectedBodies = currentStepCount == 0
                ? Array.Empty<CharacterBodySample>()
                : null;
            int selectedBodyCount = 0;
            string replayClock = replay.Count == 0
                ? null
                : m_ReplayClockId;
            ulong planSequence = 1;
            ulong nextTick = restore?.Tick.Value ?? context.CurrentCompletedTick;
            ulong inputSequenceFloor = Math.Max(lastPredictedInputSequence, m_State.ConfirmedInputSequence);
            int stepIndex = 0;
            int mappingIndex = 0;
            for (int i = 0; i < replay.Count; i++)
            {
                ServerAuthoritativePredictionHistoryRecord record = replay[i];
                inputSequenceFloor = Math.Max(inputSequenceFloor, record.Input.InputSequence);
                var replaySource = new SimulationTickSourceIdentity(
                    SimulationTickSourceKind.Replay,
                    replayClock,
                    record.Input.SourceTick);
                steps[stepIndex++] = CreateStep(
                    new SimulationTick(checked(++nextTick)),
                    SimulationPipelineStepExecutionKind.Replay,
                    replaySource,
                    planSequence++,
                    record.Input,
                    record.ObservedWorldConstraints);
            }
            if (currentStepCount > 0)
            {
                mappings[mappingIndex++] = new SimulationPipelineStepSourceMapping(
                    current.Input.TickSource.ClockId,
                    context.Source.ClockId,
                    current.Input.TickSource.Kind);
            }
            ulong nextInputSequence = Math.Max(current.InputSequence, checked(inputSequenceFloor + 1));
            for (int i = 0; i < currentStepCount; i++)
            {
                ulong tickValue = checked(++nextTick);
                ulong inputSequence = checked(nextInputSequence + (ulong)i);
                OwnerCanonicalInputBatch rebound = RebindCurrent(
                    current,
                    tickValue,
                    inputSequence,
                    i == 0);
                var tick = new SimulationTick(tickValue);
                ServerAuthoritativeRemoteBodySelectionFrame selection = m_State.SelectRemoteBodyFrame(tick);
                int sampleCount = selection.Selections.Count;
                if (selectedBodies == null)
                    selectedBodies = new CharacterBodySample[currentStepCount * sampleCount];
                selection.CopyBodySamples(selectedBodies.AsSpan(selectedBodyCount, sampleCount));
                selectedBodyCount += sampleCount;
                ObservedWorldConstraintFrame observed = observedContactEnabled
                    ? selection.ToObservedWorldConstraints(contactShapeConfigurationHash)
                    : ObservedWorldConstraintFrame.Empty(tick);
                steps[stepIndex++] = CreateStep(
                    tick,
                    SimulationPipelineStepExecutionKind.Current,
                    rebound.Input.TickSource,
                    planSequence++,
                    rebound,
                    observed);
            }
            if (replay.Count > 0)
            {
                mappings[mappingIndex] = new SimulationPipelineStepSourceMapping(
                    replayClock,
                    context.Source.ClockId,
                    SimulationTickSourceKind.Replay);
            }
            SimulationSessionPlanRequirement requirements = steps.Length == 0
                ? SimulationSessionPlanRequirement.WorkingState |
                  SimulationSessionPlanRequirement.OutputDisposition
                : SimulationSessionPlanRequirement.WorkingState |
                  SimulationSessionPlanRequirement.OutputDisposition |
                  SimulationSessionPlanRequirement.StateHash |
                  SimulationSessionPlanRequirement.Snapshot;
            selectedRemoteBodies = selectedBodies;
            return SimulationSessionExecutionPlan<Float32SimulationStep>.FromOwnedArrays(
                steps.Length == 0
                    ? SimulationSessionExecutionPlanStatus.NoStep
                    : SimulationSessionExecutionPlanStatus.Executable,
                context.Source,
                characterRuntime.Runtime.GameplayContentHash,
                context.Pipeline.Hash,
                characterRuntime.Runtime.RosterDescriptor,
                mappings,
                restore,
                steps,
                requirements);
        }

        static OwnerCanonicalInputBatch RebindCurrent(
            OwnerCanonicalInputBatch template,
            ulong targetTick,
            ulong inputSequence,
            bool includeRequests)
        {
            var source = new SimulationTickSourceIdentity(
                template.Input.TickSource.Kind,
                template.Input.TickSource.ClockId,
                targetTick);
            var input = new SimulationInput(
                template.Input.NumericProfile,
                source,
                template.Input.InputSourceIdentity,
                inputSequence,
                template.Input.Values,
                includeRequests
                    ? template.Input.Requests
                    : Array.Empty<SimulationInputRequest>());
            return new OwnerCanonicalInputBatch(template.ActorId, targetTick, inputSequence, input);
        }

        static OwnerCanonicalInputBatch WithRequests(
            OwnerCanonicalInputBatch template,
            IReadOnlyList<SimulationInputRequest> requests)
        {
            var input = new SimulationInput(
                template.Input.NumericProfile,
                template.Input.TickSource,
                template.Input.InputSourceIdentity,
                template.Input.Sequence,
                template.Input.Values,
                requests);
            return new OwnerCanonicalInputBatch(
                template.ActorId,
                template.SourceTick,
                template.InputSequence,
                input);
        }

        static Float32SimulationStep CreateStep(
            SimulationTick tick,
            SimulationPipelineStepExecutionKind kind,
            SimulationTickSourceIdentity source,
            ulong planSequence,
            OwnerCanonicalInputBatch input,
            ObservedWorldConstraintFrame observedWorldConstraints)
        {
            SimulationInput rebound = input.Input.TickSource.Equals(source)
                ? input.Input
                : new SimulationInput(
                    input.Input.NumericProfile,
                    source,
                    input.Input.InputSourceIdentity,
                    input.Input.Sequence,
                    input.Input.Values,
                    input.Input.Requests);
            return new Float32SimulationStep(
                tick,
                new SimulationPipelineStepProvenance(kind, source, planSequence),
                new[]
                {
                    new SimulationPipelineActorInput<Float32StepInput>(
                        input.ActorId,
                        input.InputSequence,
                        new Float32StepInput(rebound))
                },
                Array.Empty<SimulationPipelineTypedIngress<SimulationIngress>>(),
                observedWorldConstraints);
        }
    }

    public sealed class CorrectionScheduleReads : ISimulationPipelineReadPortSet
    {
        public CorrectionScheduleReads(
            IReadOnlySimulationPipelineProductPort<OwnerCanonicalInputBatch> ownerInput,
            IReadOnlySimulationPipelineProductPort<AuthoritativeObservationBatch> observations,
              IServerAuthoritativePredictionStatePort predictionState,
              IServerAuthoritativePredictionRestorePort restore,
              IFloat32CharacterRuntimePort characterRuntime,
              IFloat32WorldSolverRuntimePort worldSolver,
              IFloat32DiagnosticsRuntimePort diagnostics)
        {
            OwnerInput = ownerInput ?? throw new ArgumentNullException(nameof(ownerInput));
            Observations = observations ?? throw new ArgumentNullException(nameof(observations));
            PredictionState = predictionState ?? throw new ArgumentNullException(nameof(predictionState));
              Restore = restore ?? throw new ArgumentNullException(nameof(restore));
              CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
              WorldSolver = worldSolver ?? throw new ArgumentNullException(nameof(worldSolver));
              Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }
        public IReadOnlySimulationPipelineProductPort<OwnerCanonicalInputBatch> OwnerInput { get; }
        public IReadOnlySimulationPipelineProductPort<AuthoritativeObservationBatch> Observations { get; }
        public IServerAuthoritativePredictionStatePort PredictionState { get; }
          public IServerAuthoritativePredictionRestorePort Restore { get; }
          public IFloat32CharacterRuntimePort CharacterRuntime { get; }
          public IFloat32WorldSolverRuntimePort WorldSolver { get; }
          public IFloat32DiagnosticsRuntimePort Diagnostics { get; }
    }

    public sealed class CorrectionScheduleWrites : ISimulationPipelineWritePortSet
    {
        public CorrectionScheduleWrites(
            IExclusiveSimulationPipelineProductWriter<PredictionCorrectionDecision> decision,
            IExclusiveSimulationPipelineProductWriter<SelectedRemoteBodyBatch> selectedRemoteBodies,
            IExclusiveSimulationPipelineProductWriter<SimulationSessionExecutionPlan<Float32SimulationStep>> executionPlan)
        {
            Decision = decision ?? throw new ArgumentNullException(nameof(decision));
            SelectedRemoteBodies = selectedRemoteBodies ?? throw new ArgumentNullException(nameof(selectedRemoteBodies));
            ExecutionPlan = executionPlan ?? throw new ArgumentNullException(nameof(executionPlan));
        }
        public IExclusiveSimulationPipelineProductWriter<PredictionCorrectionDecision> Decision { get; }
        public IExclusiveSimulationPipelineProductWriter<SelectedRemoteBodyBatch> SelectedRemoteBodies { get; }
        public IExclusiveSimulationPipelineProductWriter<SimulationSessionExecutionPlan<Float32SimulationStep>> ExecutionPlan { get; }
    }
}
