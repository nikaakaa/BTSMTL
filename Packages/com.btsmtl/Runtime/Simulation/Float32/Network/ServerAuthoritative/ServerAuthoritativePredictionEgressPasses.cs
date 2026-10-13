using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    static class PredictionCompletedStepInput
    {
        public static OwnerCanonicalInputBatch Read(Float32CompletedSimulationStep step)
        {
            if (!step.IsValid || step.Step.Inputs.Count != 1)
                throw new InvalidOperationException("Prediction Step must contain exactly one owner input.");
            SimulationPipelineActorInput<Float32StepInput> actorInput = step.Step.Inputs[0];
            return new OwnerCanonicalInputBatch(
                actorInput.ActorId,
                step.Step.Source.SourceTick,
                actorInput.Sequence,
                actorInput.Value.Input);
        }
    }

    public sealed class PredictionHistoryEgressPassRuntimeFactory : IFloat32PipelinePassRuntimeFactory
    {
        readonly SimulationPipelinePassFactoryDescriptor m_Descriptor;

        public PredictionHistoryEgressPassRuntimeFactory(ServerAuthoritativeModelPolicy policy)
        {
            m_Descriptor = ServerAuthoritativePipelinePassContracts.CreateFactoryDescriptor(
                ServerAuthoritativePipelinePassContracts.HistoryEgress(policy),
                ServerAuthoritativePredictionPassIds.HistoryStateSchema);
        }

        public SimulationPipelinePassFactoryDescriptor Descriptor => m_Descriptor;

        public IFloat32CompiledPipelinePassRuntime Create(Float32PipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new PredictionHistoryEgressReads(
                context.Products.BindExclusiveReader<OwnerCanonicalInputBatch>(ServerAuthoritativeProducts.OwnerCanonicalInputBatch),
                context.BindSourcePort<IServerAuthoritativePredictionStatePort>(ServerAuthoritativeSourcePortContracts.PredictionStatePortId),
                context.BindTargetPort<IFloat32CompletedStepReadPort>(Float32PipelineRuntimePortIds.CompletedSteps),
                context.BindDiagnosticsPort<IFloat32DiagnosticsRuntimePort>(Float32PipelineRuntimePortIds.Diagnostics));
            return new Float32EgressPassRuntimeAdapter<PredictionHistoryEgressReads, EmptyServerAuthoritativeWritePorts>(
                new PredictionHistoryEgressPassRuntime(context.Pass.Descriptor, reads.PredictionState.State).PrepareStateStorage(context.Composition),
                reads,
                EmptyServerAuthoritativeWritePorts.Instance);
        }
    }

    public sealed class PredictionHistoryEgressPassRuntime :
        Float32PipelinePassRuntimeBase,
        ISimulationEgressPassRuntime<PredictionHistoryEgressReads, EmptyServerAuthoritativeWritePorts>,
        ISimulationPipelineStateParticipant,
        ISimulationPipelineSnapshotRetentionCapacity
    {
        readonly ServerAuthoritativePredictionState m_State;
        readonly SimulationPipelinePassStateCheckpoint m_Checkpoint;
        ServerAuthoritativePredictionHistoryCheckpoint m_CapturedHistory;

        public PredictionHistoryEgressPassRuntime(
            SimulationPipelinePassDescriptor descriptor,
            ServerAuthoritativePredictionState state) : base(descriptor)
        {
            m_State = state ?? throw new ArgumentNullException(nameof(state));
            StateIdentity = new SimulationPipelineStateParticipantIdentity(
                descriptor.PassId,
                descriptor.ImplementationVersion,
                ServerAuthoritativePredictionPassIds.HistoryStateOwner,
                ServerAuthoritativePredictionPassIds.HistoryStateSchema,
                ServerAuthoritativePredictionPassIds.HistoryStateSchemaVersion);
            m_Checkpoint = new SimulationPipelinePassStateCheckpoint(StateIdentity, RestoreCheckpoint, ReleaseCheckpoint);
        }

        ServerAuthoritativePassStateRestoreTransaction m_StateStorage;

        internal PredictionHistoryEgressPassRuntime PrepareStateStorage(SimulationSessionCompositionDescriptor composition)
        {
            m_StateStorage = new ServerAuthoritativePassStateRestoreTransaction(this,
                composition.MaximumSessionCheckpointPayloadBytes, MaximumTextValues,
                checked(1 + m_State.Policy.PredictionGameplayEffectCapacity.MaximumStepCount +
                    composition.RecordedFrameCapacity + m_State.Policy.HistoryCapacity), m_State.RestoreHistoryState);
            return this;
        }

        public SimulationPipelineStateParticipantIdentity StateIdentity { get; }
        public SimulationPipelineStepProjectionMode StepProjectionMode => SimulationPipelineStepProjectionMode.ReconstructForRestore;
        public int MaximumTextValues => m_State.MaximumHistoryTextValues;
        public int MaximumRetainedStepSnapshots => m_State.Policy.HistoryCapacity;

        public ISimulationPipelinePassStateCheckpoint CaptureCheckpoint()
        {
            RequireCaptureOrRestore();
            m_Checkpoint.Begin();
            try
            {
                m_CapturedHistory = m_State.CaptureHistoryCheckpoint();
                return m_Checkpoint;
            }
            catch
            {
                m_Checkpoint.Dispose();
                throw;
            }
        }

        void RestoreCheckpoint() => m_State.RestoreHistoryCheckpoint(m_CapturedHistory);

        void ReleaseCheckpoint()
        {
            m_CapturedHistory?.Dispose();
            m_CapturedHistory = null;
        }

        public void Execute(
            SimulationPipelineEgressContext context,
            PredictionHistoryEgressReads readPorts,
            EmptyServerAuthoritativeWritePorts writePorts)
        {
            RequireExecution();
            OwnerCanonicalInputBatch currentInput = readPorts.OwnerInput.Read();
            int currentCount = 0;
            for (int i = 0; i < readPorts.Completed.Steps.Count; i++)
            {
                Float32CompletedSimulationStep step = readPorts.Completed.Steps[i];
                if (step.Step.ExecutionKind == SimulationPipelineStepExecutionKind.Current)
                {
                    if (++currentCount > 2)
                        throw new InvalidOperationException("Prediction transaction contains more than two Current Steps.");
                }
                else if (step.Step.ExecutionKind != SimulationPipelineStepExecutionKind.Replay)
                    throw new InvalidOperationException("Prediction history accepts only Replay and Current Steps.");
                OwnerCanonicalInputBatch input = PredictionCompletedStepInput.Read(step);
                m_State.AddHistory(input, step);
            }
            if (readPorts.Diagnostics.Sink.IsEnabled)
            {
                readPorts.Diagnostics.Sink.PublishModel(new SimulationModelTraceRecord(
                    SimulationModelTraceKind.Queue,
                    "prediction_history_commit",
                    $"history={m_State.HistoryCount};journal={m_State.JournalCount};cursor={m_State.JournalCursor};lastBaseline={m_State.LastBaselineTick};lastAckTick={m_State.LastAuthorityAckTick}",
                    currentInput.ActorId,
                    currentInput.SourceTick,
                    m_State.LastBaselineTick,
                    currentInput.InputSequence,
                    m_State.ConfirmedInputSequence,
                    m_State.HistoryCount));
            }
        }

        protected override void OnDispose()
        {
            m_Checkpoint.Dispose();
            m_StateStorage.ReleaseStorage();
            m_State.ReleaseHistoryStorage();
        }

        public void WriteCheckpointState(CanonicalWriter writer)
        {
            RequireCaptureOrRestore();
            m_State.WriteHistoryState(writer);
        }

        public SimulationPipelinePassStateSnapshot CaptureState()
        {
            RequireCaptureOrRestore();
            return m_StateStorage.CaptureState();
        }

        public ISimulationPipelinePassRestoreTransaction PrepareRestore(SimulationPipelinePassStateSnapshot snapshot)
        {
            RequireCaptureOrRestore();
            return m_StateStorage.Prepare(snapshot);
        }
    }

    public sealed class PredictionHistoryEgressReads : ISimulationPipelineReadPortSet
    {
        public PredictionHistoryEgressReads(
            IReadOnlySimulationPipelineProductPort<OwnerCanonicalInputBatch> ownerInput,
            IServerAuthoritativePredictionStatePort predictionState,
            IFloat32CompletedStepReadPort completed,
            IFloat32DiagnosticsRuntimePort diagnostics)
        {
            OwnerInput = ownerInput ?? throw new ArgumentNullException(nameof(ownerInput));
            PredictionState = predictionState ?? throw new ArgumentNullException(nameof(predictionState));
            Completed = completed ?? throw new ArgumentNullException(nameof(completed));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public IReadOnlySimulationPipelineProductPort<OwnerCanonicalInputBatch> OwnerInput { get; }
        public IServerAuthoritativePredictionStatePort PredictionState { get; }
        public IFloat32CompletedStepReadPort Completed { get; }
        public IFloat32DiagnosticsRuntimePort Diagnostics { get; }
    }

    public sealed class PredictionOutputDispositionPassRuntimeFactory : IFloat32PipelinePassRuntimeFactory
    {
        readonly SimulationPipelinePassFactoryDescriptor m_Descriptor;

        public PredictionOutputDispositionPassRuntimeFactory(ServerAuthoritativeModelPolicy policy)
        {
            m_Descriptor = ServerAuthoritativePipelinePassContracts.CreateFactoryDescriptor(
                ServerAuthoritativePipelinePassContracts.OutputDisposition(policy),
                ServerAuthoritativePredictionPassIds.JournalStateSchema,
                ServerAuthoritativePredictionPassIds.JournalStateSchemaVersion);
        }

        public SimulationPipelinePassFactoryDescriptor Descriptor => m_Descriptor;

        public IFloat32CompiledPipelinePassRuntime Create(Float32PipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new PredictionOutputDispositionReads(
                context.Products.BindExclusiveReader<PredictionCorrectionDecision>(ServerAuthoritativeProducts.PredictionCorrectionDecision),
                context.Products.BindAppendReader<SimulationActorTickResult>(SimulationPipelineProducts.FinalizedStepResult),
                context.BindSourcePort<IServerAuthoritativePredictionStatePort>(ServerAuthoritativeSourcePortContracts.PredictionStatePortId),
                context.BindTargetPort<IFloat32CompletedStepReadPort>(Float32PipelineRuntimePortIds.CompletedSteps),
                context.BindDiagnosticsPort<IFloat32DiagnosticsRuntimePort>(Float32PipelineRuntimePortIds.Diagnostics));
            var writes = new PredictionOutputDispositionWrites(
                context.Products.BindExclusiveWriter<SimulationPipelineOutputDispositionSet>(SimulationPipelineProducts.OutputDispositionSet));
            IFloat32CharacterRuntimePort characterRuntime =
                context.BindTargetPort<IFloat32CharacterRuntimePort>(Float32PipelineRuntimePortIds.CharacterRuntime);
            int stepOutputCapacity = 0;
            for (int actor = 0; actor < characterRuntime.Runtime.Roster.Count; actor++)
                stepOutputCapacity = checked(stepOutputCapacity +
                    characterRuntime.Runtime.Roster[actor].GameplayOutputCapacity);
            int outputCapacity = checked(stepOutputCapacity *
                reads.PredictionState.State.Policy.PredictionGameplayEffectCapacity.MaximumStepCount);
            return new Float32EgressPassRuntimeAdapter<PredictionOutputDispositionReads, PredictionOutputDispositionWrites>(
                new PredictionOutputDispositionPassRuntime(context.Pass.Descriptor, reads.PredictionState.State,
                    outputCapacity).PrepareStateStorage(context.Composition),
                reads,
                writes);
        }
    }

    public sealed class PredictionOutputDispositionPassRuntime :
        Float32PipelinePassRuntimeBase,
        ISimulationEgressPassRuntime<PredictionOutputDispositionReads, PredictionOutputDispositionWrites>,
        ISimulationPipelineStateParticipant
    {
        readonly ServerAuthoritativePredictionState m_State;
        readonly SimulationOutputDispositionBuffer m_Dispositions;
        readonly SimulationPipelinePassStateCheckpoint m_Checkpoint;
        ServerAuthoritativePredictionJournalCheckpoint m_CapturedJournal;

        public PredictionOutputDispositionPassRuntime(
            SimulationPipelinePassDescriptor descriptor,
            ServerAuthoritativePredictionState state,
            int outputCapacity) : base(descriptor)
        {
            m_State = state ?? throw new ArgumentNullException(nameof(state));
            m_Dispositions = new SimulationOutputDispositionBuffer(outputCapacity);
            StateIdentity = new SimulationPipelineStateParticipantIdentity(
                descriptor.PassId,
                descriptor.ImplementationVersion,
                ServerAuthoritativePredictionPassIds.JournalStateOwner,
                ServerAuthoritativePredictionPassIds.JournalStateSchema,
                ServerAuthoritativePredictionPassIds.JournalStateSchemaVersion);
            m_Checkpoint = new SimulationPipelinePassStateCheckpoint(StateIdentity, RestoreCheckpoint, ReleaseCheckpoint);
        }

        ServerAuthoritativePassStateRestoreTransaction m_StateStorage;

        internal PredictionOutputDispositionPassRuntime PrepareStateStorage(SimulationSessionCompositionDescriptor composition)
        {
            m_StateStorage = new ServerAuthoritativePassStateRestoreTransaction(this,
                composition.MaximumSessionCheckpointPayloadBytes, MaximumTextValues,
                checked(1 + m_State.Policy.PredictionGameplayEffectCapacity.MaximumStepCount +
                    composition.RecordedFrameCapacity + m_State.Policy.HistoryCapacity), m_State.RestoreJournalState);
            return this;
        }

        public SimulationPipelineStateParticipantIdentity StateIdentity { get; }
        public SimulationPipelineStepProjectionMode StepProjectionMode => SimulationPipelineStepProjectionMode.Include;
        public int MaximumTextValues => 0;

        public ISimulationPipelinePassStateCheckpoint CaptureCheckpoint()
        {
            RequireCaptureOrRestore();
            m_Checkpoint.Begin();
            try
            {
                m_CapturedJournal = m_State.CaptureJournalCheckpoint();
                return m_Checkpoint;
            }
            catch
            {
                m_Checkpoint.Dispose();
                throw;
            }
        }

        void RestoreCheckpoint() => m_State.RestoreJournalCheckpoint(m_CapturedJournal);
        void ReleaseCheckpoint()
        {
            m_CapturedJournal?.Dispose();
            m_CapturedJournal = null;
        }

        public void Execute(
            SimulationPipelineEgressContext context,
            PredictionOutputDispositionReads readPorts,
            PredictionOutputDispositionWrites writePorts)
        {
            RequireExecution();
            m_Dispositions.Begin();
            PredictionCorrectionDecision decision = readPorts.Decision.Read();
            int actorResultCount = 0;
            SimulationTick currentTick = default;
            for (int stepIndex = 0; stepIndex < readPorts.Completed.Steps.Count; stepIndex++)
            {
                Float32CompletedSimulationStep completed = readPorts.Completed.Steps[stepIndex];
                if (completed.Step.ExecutionKind == SimulationPipelineStepExecutionKind.Current)
                {
                    if (currentTick.IsValid && completed.Step.Tick.Value != currentTick.Value + 1)
                        throw new InvalidOperationException("Prediction Current Steps are not contiguous.");
                    currentTick = completed.Step.Tick;
                }
                actorResultCount += completed.Result.Actors.Count;
            }
            if (actorResultCount != readPorts.Results.Count)
                throw new InvalidOperationException("Prediction finalized result count does not match completed Steps.");

            int publishedCount = 0;
            int duplicateCount = 0;
            for (int stepIndex = 0; stepIndex < readPorts.Completed.Steps.Count; stepIndex++)
            {
                Float32CompletedSimulationStep completed = readPorts.Completed.Steps[stepIndex];
                bool replay = completed.Step.ExecutionKind == SimulationPipelineStepExecutionKind.Replay;
                for (int actorIndex = 0; actorIndex < completed.Result.Actors.Count; actorIndex++)
                {
                    SimulationActorTickResult actor = completed.Result.Actors[actorIndex];
                    AddGameplayFacts(actor.GameplayFacts, actor.ActorId, replay, ref publishedCount, ref duplicateCount);
                    AddPresentationCommands(actor.PresentationCommands, actor.ActorId, replay, ref publishedCount, ref duplicateCount);
                }
            }
            if (currentTick.IsValid)
                m_State.SealHistoryJournalCursor(currentTick);
            ActorId diagnosticActorId = m_Dispositions.GetCount(m_Dispositions.Version) == 0
                ? default : m_Dispositions.Get(0, m_Dispositions.Version).ActorId;
            writePorts.Dispositions.Write(SimulationPipelineOutputDispositionSet.FromBufferedDispositions(
                context.TransactionIdentity,
                m_Dispositions));
            if (readPorts.Diagnostics.Sink.IsEnabled)
            {
                readPorts.Diagnostics.Sink.PublishModel(new SimulationModelTraceRecord(
                    SimulationModelTraceKind.OutputDisposition,
                    "prediction_output_disposition",
                    $"decision={decision.Kind};published={publishedCount};suppressedDuplicate={duplicateCount};predictedRejected={m_State.LastRejectedCount};journal={m_State.JournalCount};cursor={m_State.JournalCursor}",
                    diagnosticActorId,
                    context.Source.SourceTick,
                    decision.BaselineTick.Value,
                    0,
                    m_State.ConfirmedInputSequence,
                    0,
                    context.CompletedStepCount));
            }
        }

        void AddGameplayFacts(
            IReadOnlyList<GameplayFact> values,
            ActorId actorId,
            bool replay,
            ref int publishedCount,
            ref int duplicateCount)
        {
            for (int i = 0; i < values.Count; i++)
            {
                AddDisposition(values[i].Header, actorId, replay, ref publishedCount, ref duplicateCount);
            }
        }

        void AddPresentationCommands(
            IReadOnlyList<PresentationCommand> values,
            ActorId actorId,
            bool replay,
            ref int publishedCount,
            ref int duplicateCount)
        {
            for (int i = 0; i < values.Count; i++)
            {
                AddDisposition(values[i].Header, actorId, replay, ref publishedCount, ref duplicateCount);
            }
        }

        void AddDisposition(
            SimulationEventHeader eventHeader,
            ActorId actorId,
            bool replay,
            ref int publishedCount,
            ref int duplicateCount)
        {
            EventId eventId = eventHeader.EventId;
            bool duplicate = replay && m_State.WasCommitted(eventId);
            if (duplicate)
                duplicateCount++;
            else
                publishedCount++;
            m_Dispositions.Add(new SimulationOutputDisposition(
                eventId,
                actorId,
                duplicate ? SimulationOutputDispositionKind.Suppress : SimulationOutputDispositionKind.Publish));
            m_State.Record(
                eventHeader,
                duplicate
                    ? ServerAuthoritativeEventDisposition.SuppressedDuplicate
                    : ServerAuthoritativeEventDisposition.PredictedCommitted);
        }

        protected override void OnDispose()
        {
            m_Checkpoint.Dispose();
            m_StateStorage.ReleaseStorage();
            m_Dispositions.Begin();
        }

        public void WriteCheckpointState(CanonicalWriter writer)
        {
            RequireCaptureOrRestore();
            m_State.WriteJournalState(writer);
        }

        public SimulationPipelinePassStateSnapshot CaptureState()
        {
            RequireCaptureOrRestore();
            return m_StateStorage.CaptureState();
        }

        public ISimulationPipelinePassRestoreTransaction PrepareRestore(SimulationPipelinePassStateSnapshot snapshot)
        {
            RequireCaptureOrRestore();
            return m_StateStorage.Prepare(snapshot);
        }
    }

    public sealed class PredictionOutputDispositionReads : ISimulationPipelineReadPortSet
    {
        public PredictionOutputDispositionReads(
            IReadOnlySimulationPipelineProductPort<PredictionCorrectionDecision> decision,
            IReadOnlySimulationPipelineAppendPort<SimulationActorTickResult> results,
            IServerAuthoritativePredictionStatePort predictionState,
            IFloat32CompletedStepReadPort completed,
            IFloat32DiagnosticsRuntimePort diagnostics)
        {
            Decision = decision ?? throw new ArgumentNullException(nameof(decision));
            Results = results ?? throw new ArgumentNullException(nameof(results));
            PredictionState = predictionState ?? throw new ArgumentNullException(nameof(predictionState));
            Completed = completed ?? throw new ArgumentNullException(nameof(completed));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public IReadOnlySimulationPipelineProductPort<PredictionCorrectionDecision> Decision { get; }
        public IReadOnlySimulationPipelineAppendPort<SimulationActorTickResult> Results { get; }
        public IServerAuthoritativePredictionStatePort PredictionState { get; }
        public IFloat32CompletedStepReadPort Completed { get; }
        public IFloat32DiagnosticsRuntimePort Diagnostics { get; }
    }

    public sealed class PredictionOutputDispositionWrites : ISimulationPipelineWritePortSet
    {
        public PredictionOutputDispositionWrites(
            IExclusiveSimulationPipelineProductWriter<SimulationPipelineOutputDispositionSet> dispositions)
        {
            Dispositions = dispositions ?? throw new ArgumentNullException(nameof(dispositions));
        }

        public IExclusiveSimulationPipelineProductWriter<SimulationPipelineOutputDispositionSet> Dispositions { get; }
    }

    public sealed class PredictionInputCommandEgressPassRuntimeFactory : IFloat32PipelinePassRuntimeFactory
    {
        readonly SimulationPipelinePassFactoryDescriptor m_Descriptor;
        readonly int m_MaximumStepCount;

        public PredictionInputCommandEgressPassRuntimeFactory(ServerAuthoritativeModelPolicy policy)
        {
            m_MaximumStepCount = policy.PredictionGameplayEffectCapacity.MaximumStepCount;
            m_Descriptor = ServerAuthoritativePipelinePassContracts.CreateFactoryDescriptor(
                ServerAuthoritativePipelinePassContracts.InputCommandEgress(policy));
        }

        public SimulationPipelinePassFactoryDescriptor Descriptor => m_Descriptor;

        public IFloat32CompiledPipelinePassRuntime Create(Float32PipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new PredictionInputCommandEgressReads(
                context.Products.BindExclusiveReader<OwnerCanonicalInputBatch>(ServerAuthoritativeProducts.OwnerCanonicalInputBatch),
                context.BindTargetPort<IFloat32CompletedStepReadPort>(Float32PipelineRuntimePortIds.CompletedSteps),
                context.BindSourcePort<IServerAuthoritativeNetworkSendPort>(ServerAuthoritativeSourcePortContracts.PredictionSendPortId));
            var writes = new ServerAuthoritativeSourceEgressWrites(
                context.Products.BindAppendWriter<Float32SourceEgressRecord>(SimulationPipelineProducts.SourceEgress));
            ServerAuthoritativePredictionState state = context.BindSourcePort<IServerAuthoritativePredictionStatePort>(
                ServerAuthoritativeSourcePortContracts.PredictionStatePortId).State;
            return new Float32EgressPassRuntimeAdapter<PredictionInputCommandEgressReads, ServerAuthoritativeSourceEgressWrites>(
                new PredictionInputCommandEgressPassRuntime(context.Pass.Descriptor,
                    context.Composition, m_MaximumStepCount, state.MaximumOwnerInputTextValues),
                reads,
                writes);
        }
    }

    public sealed class PredictionInputCommandEgressPassRuntime :
        Float32PipelinePassRuntimeBase,
        ISimulationEgressPassRuntime<PredictionInputCommandEgressReads, ServerAuthoritativeSourceEgressWrites>
    {
        readonly CommandOutput[] m_Outputs;

        public PredictionInputCommandEgressPassRuntime(SimulationPipelinePassDescriptor descriptor,
            SimulationSessionCompositionDescriptor composition, int maximumStepCount, int maximumInputTextValues) : base(descriptor)
        {
            int bytes = composition.MaximumSessionCheckpointPayloadBytes;
            if (bytes <= 0)
                throw new InvalidOperationException("Prediction input egress requires MaximumSessionCheckpointPayloadBytes.");
            int texts = SimulationSessionCheckpointPayloadLayout.MaximumNonEmptyTextValues(bytes,
                maximumInputTextValues, 0, 0);
            m_Outputs = new CommandOutput[maximumStepCount];
            for (int index = 0; index < m_Outputs.Length; index++) m_Outputs[index] = new CommandOutput(bytes, texts);
        }

        sealed class CommandOutput
        {
            internal CommandOutput(int bytes, int texts)
            {
                Text = new CanonicalTextReferences(texts, null);
                var buffer = new byte[bytes];
                Writer = new CanonicalWriter(buffer);
                Writer.SetTextCapture(Text);
                CanonicalScratch = new CanonicalWriter(new byte[bytes]);
                Record = new Float32SourceEgressRecord(ServerAuthoritativeEgressChannels.ClientInput,
                    ServerAuthoritativeEgressChannels.ClientInputSchema, ServerAuthoritativeEgressChannels.ClientInputSchemaVersion,
                    buffer, Text, new CanonicalReader(Array.Empty<byte>()),
                    new CanonicalReader(Array.Empty<byte>()), CanonicalScratch);
            }
            internal readonly CanonicalTextReferences Text;
            internal readonly CanonicalWriter Writer;
            internal readonly CanonicalWriter CanonicalScratch;
            internal readonly Float32SourceEgressRecord Record;
            internal void Begin() => Writer.Reset();
        }

        protected override void OnDispose()
        {
            for (int index = 0; index < m_Outputs.Length; index++)
            {
                m_Outputs[index].Text.Reset();
                m_Outputs[index].Writer.Dispose();
                m_Outputs[index].CanonicalScratch.Dispose();
            }
        }

        public void Execute(
            SimulationPipelineEgressContext context,
            PredictionInputCommandEgressReads readPorts,
            ServerAuthoritativeSourceEgressWrites writePorts)
        {
            RequireExecution();
            _ = readPorts.Input.Read();
            for (int index = 0; index < m_Outputs.Length; index++) m_Outputs[index].Begin();
            int outputIndex = 0;
            for (int i = 0; i < readPorts.Completed.Steps.Count; i++)
            {
                Float32CompletedSimulationStep step = readPorts.Completed.Steps[i];
                if (step.Step.ExecutionKind != SimulationPipelineStepExecutionKind.Current)
                    continue;
                OwnerCanonicalInputBatch input = PredictionCompletedStepInput.Read(step);
                CommandOutput output = m_Outputs[outputIndex++];
                try
                {
                    ServerAuthoritativeEgressCodec.WriteOwnerInput(output.Writer, input);
                    output.Record.Publish(input.ActorId, new SimulationTick(input.SourceTick), checked((int)output.Writer.Length));
                    writePorts.Egress.Append(
                        new SimulationPipelineAppendEntryIdentity(
                            input.ActorId,
                            new SimulationTick(input.SourceTick),
                            input.InputSequence,
                            context.Source),
                        output.Record);
                }
                catch
                {
                    output.Text.Reset();
                    throw;
                }
            }
        }
    }

    public sealed class PredictionInputCommandEgressReads : ISimulationPipelineReadPortSet
    {
        public PredictionInputCommandEgressReads(
            IReadOnlySimulationPipelineProductPort<OwnerCanonicalInputBatch> input,
            IFloat32CompletedStepReadPort completed,
            IServerAuthoritativeNetworkSendPort send)
        {
            Input = input ?? throw new ArgumentNullException(nameof(input));
            Completed = completed ?? throw new ArgumentNullException(nameof(completed));
            Send = send ?? throw new ArgumentNullException(nameof(send));
        }

        public IReadOnlySimulationPipelineProductPort<OwnerCanonicalInputBatch> Input { get; }
        public IFloat32CompletedStepReadPort Completed { get; }
        public IServerAuthoritativeNetworkSendPort Send { get; }
    }

    public sealed class RemotePresentationEgressPassRuntimeFactory : IFloat32PipelinePassRuntimeFactory
    {
        readonly SimulationPipelinePassFactoryDescriptor m_Descriptor;

        public RemotePresentationEgressPassRuntimeFactory(ServerAuthoritativeModelPolicy policy)
        {
            m_Descriptor = ServerAuthoritativePipelinePassContracts.CreateFactoryDescriptor(
                ServerAuthoritativePipelinePassContracts.RemotePresentationEgress(policy));
        }

        public SimulationPipelinePassFactoryDescriptor Descriptor => m_Descriptor;

        public IFloat32CompiledPipelinePassRuntime Create(Float32PipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new RemotePresentationEgressReads(
                context.Products.BindExclusiveReader<RemotePresentationBatch>(ServerAuthoritativeProducts.RemotePresentationBatch),
                context.Products.BindExclusiveReader<SelectedRemoteBodyBatch>(ServerAuthoritativeProducts.SelectedRemoteBodyBatch));
            var writes = new ServerAuthoritativeSourceEgressWrites(
                context.Products.BindAppendWriter<Float32SourceEgressRecord>(SimulationPipelineProducts.SourceEgress));
            return new Float32EgressPassRuntimeAdapter<RemotePresentationEgressReads, ServerAuthoritativeSourceEgressWrites>(
                new RemotePresentationEgressPassRuntime(context.Pass.Descriptor, context.Composition),
                reads,
                writes);
        }
    }

    public sealed class RemotePresentationEgressPassRuntime :
        Float32PipelinePassRuntimeBase,
        ISimulationEgressPassRuntime<RemotePresentationEgressReads, ServerAuthoritativeSourceEgressWrites>
    {
        readonly CanonicalTextReferences m_Text;
        readonly CanonicalWriter m_Writer;
        readonly Float32SourceEgressRecord m_Record;

        public RemotePresentationEgressPassRuntime(SimulationPipelinePassDescriptor descriptor,
            SimulationSessionCompositionDescriptor composition) : base(descriptor)
        {
            int bytes = composition.MaximumSessionCheckpointPayloadBytes;
            if (bytes <= 0)
                throw new InvalidOperationException("Remote presentation egress requires MaximumSessionCheckpointPayloadBytes.");
            int texts = SimulationSessionCheckpointPayloadLayout.MaximumNonEmptyTextValues(bytes,
                ServerAuthoritativeEgressCodec.MaximumRemoteTextValues, 0, 0);
            m_Text = new CanonicalTextReferences(texts, null);
            var buffer = new byte[bytes];
            m_Writer = new CanonicalWriter(buffer);
            m_Writer.SetTextCapture(m_Text);
            m_Record = new Float32SourceEgressRecord(ServerAuthoritativeEgressChannels.RemotePresentation,
                ServerAuthoritativeEgressChannels.RemotePresentationSchema,
                ServerAuthoritativeEgressChannels.RemotePresentationSchemaVersion,
                buffer, m_Text, new CanonicalReader(Array.Empty<byte>()),
                new CanonicalReader(Array.Empty<byte>()), new CanonicalWriter(new byte[bytes]));
        }

        protected override void OnDispose()
        {
            m_Text.Reset();
            m_Writer.Dispose();
            m_Record.CanonicalScratch.Dispose();
        }

        public void Execute(
            SimulationPipelineEgressContext context,
            RemotePresentationEgressReads readPorts,
            ServerAuthoritativeSourceEgressWrites writePorts)
        {
            RequireExecution();
            RemotePresentationBatch events = readPorts.Batch.Read();
            SelectedRemoteBodyBatch selected = readPorts.SelectedBodies.Read();
            if (events.ActorId != selected.ActorId)
                throw new InvalidOperationException("Remote Presentation event and selected body products target different Actors.");
            RemotePresentationBatch batch = RemotePresentationBatch.FromPreparedOutputs(
                events.ActorId,
                selected.BodySamples,
                events.SampleCommands,
                events.ReliableEvents,
                selected.ResetStream);
            if (!batch.ResetBodyStream && batch.BodySamples.Count == 0 && batch.SampleCommands.Count == 0 && batch.ReliableEvents.Count == 0)
                return;
            SimulationTick tick = LatestTick(batch);
            if (!tick.IsValid)
                tick = selected.Tick;
            m_Writer.Reset();
            try
            {
                ServerAuthoritativeEgressCodec.WriteRemotePresentation(m_Writer, batch);
                m_Record.Publish(batch.ActorId, tick, checked((int)m_Writer.Length));
                writePorts.Egress.Append(
                    new SimulationPipelineAppendEntryIdentity(batch.ActorId, tick, 1, context.Source),
                    m_Record);
            }
            catch
            {
                m_Text.Reset();
                throw;
            }
        }

        static SimulationTick LatestTick(RemotePresentationBatch batch)
        {
            SimulationTick tick = default;
            if (batch.BodySamples.Count > 0)
                tick = batch.BodySamples[batch.BodySamples.Count - 1].Tick;
            for (int i = 0; i < batch.SampleCommands.Count; i++)
            {
                if (!tick.IsValid || batch.SampleCommands[i].Header.Tick.CompareTo(tick) > 0)
                    tick = batch.SampleCommands[i].Header.Tick;
            }
            if (batch.ReliableEvents.Count > 0 &&
                (!tick.IsValid || batch.ReliableEvents[batch.ReliableEvents.Count - 1].Header.Tick.CompareTo(tick) > 0))
            {
                tick = batch.ReliableEvents[batch.ReliableEvents.Count - 1].Header.Tick;
            }
            return tick;
        }
    }

    public sealed class RemotePresentationEgressReads : ISimulationPipelineReadPortSet
    {
        public RemotePresentationEgressReads(
            IReadOnlySimulationPipelineProductPort<RemotePresentationBatch> batch,
            IReadOnlySimulationPipelineProductPort<SelectedRemoteBodyBatch> selectedBodies)
        {
            Batch = batch ?? throw new ArgumentNullException(nameof(batch));
            SelectedBodies = selectedBodies ?? throw new ArgumentNullException(nameof(selectedBodies));
        }

        public IReadOnlySimulationPipelineProductPort<RemotePresentationBatch> Batch { get; }
        public IReadOnlySimulationPipelineProductPort<SelectedRemoteBodyBatch> SelectedBodies { get; }
    }

    public sealed class ServerAuthoritativeSourceEgressWrites : ISimulationPipelineWritePortSet
    {
        public ServerAuthoritativeSourceEgressWrites(
            IAppendOnlySimulationPipelineProductWriter<Float32SourceEgressRecord> egress)
        {
            Egress = egress ?? throw new ArgumentNullException(nameof(egress));
        }

        public IAppendOnlySimulationPipelineProductWriter<Float32SourceEgressRecord> Egress { get; }
    }

    public sealed class EmptyServerAuthoritativeWritePorts : ISimulationPipelineWritePortSet
    {
        public static readonly EmptyServerAuthoritativeWritePorts Instance = new EmptyServerAuthoritativeWritePorts();
        EmptyServerAuthoritativeWritePorts() { }
    }
}
