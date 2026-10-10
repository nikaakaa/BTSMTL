using ThirdPersonSimulation;
using System;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class FixedWorldResolveBatchPassRuntimeFactory : IFixedPipelinePassRuntimeFactory
    {
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFixedPipelinePassContracts.CreateFactoryDescriptor(
                StandardFixedPipelinePassContracts.WorldResolveBatch);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;

        public IFixedCompiledPipelinePassRuntime Create(FixedPipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new FixedWorldResolveBatchReadPorts(
                context.Products.BindExclusiveReader<FixedCanonicalInputBatch>(SimulationPipelineProducts.CanonicalInputs),
                context.Products.BindExclusiveReader<WorldSolveBatchRequest>(SimulationPipelineProducts.WorldSolveBatchRequest),
                context.Products.BindExclusiveReader<FixedCharacterEvaluationResultBatch>(SimulationPipelineProducts.CharacterEvaluationResults),
                context.BindTargetPort<IFixedCombatRuntimePort>(FixedPipelineRuntimePortIds.Combat),
                context.BindSolverPort<IFixedWorldSolverRuntimePort>(FixedPipelineRuntimePortIds.WorldSolver),
                context.BindDiagnosticsPort<IFixedDiagnosticsRuntimePort>(FixedPipelineRuntimePortIds.Diagnostics));
            var writes = new FixedWorldResolveBatchWritePorts(
                context.Products.BindExclusiveWriter<WorldSolveBatchResult>(SimulationPipelineProducts.WorldSolveBatchResult),
                context.Products.BindExclusiveWriter<FixedCombatBatchResult>(SimulationPipelineProducts.CombatBatchResult));
            return new FixedStepPassRuntimeAdapter<FixedWorldResolveBatchReadPorts, FixedWorldResolveBatchWritePorts>(
                new FixedWorldResolveBatchPassRuntime(context.Pass.Descriptor, reads.Combat.Combat, reads.Combat.MaximumCheckpointPayloadBytes),
                reads,
                writes);
        }
    }

    public sealed class FixedWorldResolveBatchPassRuntime :
        FixedPipelinePassRuntimeBase,
        ISimulationStepPassRuntime<FixedWorldResolveBatchReadPorts, FixedWorldResolveBatchWritePorts>,
        ISimulationPipelineStateParticipant,
        ISimulationPipelineStepStateStorage
    {
        readonly FixedCombatPipelineStateParticipant m_CombatState;
        public FixedWorldResolveBatchPassRuntime(SimulationPipelinePassDescriptor descriptor, FixedCombatRuntime combat, int maximumSessionCheckpointPayloadBytes)
            : base(descriptor)
        {
            m_CombatState = new FixedCombatPipelineStateParticipant(descriptor, combat, maximumSessionCheckpointPayloadBytes);
        }

        public SimulationPipelineStateParticipantIdentity StateIdentity => m_CombatState.StateIdentity;
        public int MaximumPayloadBytes => m_CombatState.MaximumPayloadBytes;
        public int MaximumTextValues => m_CombatState.MaximumTextValues;
        public SimulationPipelineStepProjectionMode StepProjectionMode => SimulationPipelineStepProjectionMode.Include;
        public void PrepareStepStorage(int snapshotCapacity) => m_CombatState.PrepareStepStorage(snapshotCapacity);
        public SimulationPipelinePassStateSnapshot CaptureStepState() => m_CombatState.CaptureStepState();
        public ISimulationPipelinePassStateCheckpoint CaptureCheckpoint() => m_CombatState.CaptureCheckpoint();
        public SimulationPipelinePassStateSnapshot CaptureState() => m_CombatState.CaptureState();
        public void WriteCheckpointState(CanonicalWriter writer) => m_CombatState.WriteCheckpointState(writer);
        public ISimulationPipelinePassRestoreTransaction PrepareRestore(SimulationPipelinePassStateSnapshot snapshot) => m_CombatState.PrepareRestore(snapshot);

        public void Execute(
            SimulationPipelineStepTransactionContext context,
            FixedWorldResolveBatchReadPorts readPorts,
            FixedWorldResolveBatchWritePorts writePorts)
        {
            RequireExecution();
            WorldSolveBatchRequest request = readPorts.Request.Read();
            if (request.Tick != context.Tick)
                throw new InvalidOperationException("World Resolve Pass request belongs to another Step.");
            ICharacterWorldSolver solver = readPorts.Solver.Solver;
            WorldSolveBatchResult result = solver.ResolveBatch(request, readPorts.Diagnostics.Sink);
            if (result.Tick != context.Tick || !ReferenceEquals(result.Request, request) ||
                !result.SolverId.Equals(solver.Descriptor.ImplementationId) ||
                !string.Equals(result.SolverVersion, solver.Descriptor.Version, StringComparison.Ordinal) ||
                result.Results.Count != request.Requests.Count)
            {
                throw new InvalidOperationException("World Solver returned a batch for another request, Tick or Solver identity.");
            }
            writePorts.Result.Write(result);
            writePorts.Combat.Write(readPorts.Combat.Combat.Resolve(readPorts.Evaluations.Read(), result, readPorts.Inputs.Read().LocalEnemies));
        }
    }

    public sealed class FixedWorldResolveBatchReadPorts : ISimulationPipelineReadPortSet
    {
        public FixedWorldResolveBatchReadPorts(
            IReadOnlySimulationPipelineProductPort<FixedCanonicalInputBatch> inputs,
            IReadOnlySimulationPipelineProductPort<WorldSolveBatchRequest> request,
            IReadOnlySimulationPipelineProductPort<FixedCharacterEvaluationResultBatch> evaluations,
            IFixedCombatRuntimePort combat,
            IFixedWorldSolverRuntimePort solver,
            IFixedDiagnosticsRuntimePort diagnostics)
        {
            Inputs = inputs;
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Evaluations = evaluations;
            Combat = combat;
            Solver = solver ?? throw new ArgumentNullException(nameof(solver));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public IReadOnlySimulationPipelineProductPort<WorldSolveBatchRequest> Request { get; }
        public IReadOnlySimulationPipelineProductPort<FixedCanonicalInputBatch> Inputs { get; }
        public IReadOnlySimulationPipelineProductPort<FixedCharacterEvaluationResultBatch> Evaluations { get; }
        public IFixedCombatRuntimePort Combat { get; }
        public IFixedWorldSolverRuntimePort Solver { get; }
        public IFixedDiagnosticsRuntimePort Diagnostics { get; }
    }

    public sealed class FixedWorldResolveBatchWritePorts : ISimulationPipelineWritePortSet
    {
        public FixedWorldResolveBatchWritePorts(
            IExclusiveSimulationPipelineProductWriter<WorldSolveBatchResult> result,
            IExclusiveSimulationPipelineProductWriter<FixedCombatBatchResult> combat)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
            Combat = combat;
        }

        public IExclusiveSimulationPipelineProductWriter<WorldSolveBatchResult> Result { get; }
        public IExclusiveSimulationPipelineProductWriter<FixedCombatBatchResult> Combat { get; }
    }
}

