using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;
using System;

namespace ThirdPersonSimulation.FPGameplay
{
    public sealed class FPWorldResolveBatchPassRuntimeFactory : IFPPipelinePassRuntimeFactory
    {
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFPPipelinePassContracts.CreateFactoryDescriptor(
                StandardFPPipelinePassContracts.WorldResolveBatch);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;

        public IFPCompiledPipelinePassRuntime Create(FPPipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new FPWorldResolveBatchReadPorts(
                context.Products.BindExclusiveReader<FPCanonicalInputBatch>(SimulationPipelineProducts.CanonicalInputs),
                context.Products.BindExclusiveReader<WorldSolveBatchRequest>(SimulationPipelineProducts.WorldSolveBatchRequest),
                context.Products.BindExclusiveReader<FPCharacterEvaluationResultBatch>(SimulationPipelineProducts.CharacterEvaluationResults),
                context.BindTargetPort<IFPCombatRuntimePort>(FPPipelineRuntimePortIds.Combat),
                context.BindSolverPort<IFPWorldSolverRuntimePort>(FPPipelineRuntimePortIds.WorldSolver),
                context.BindDiagnosticsPort<IFPDiagnosticsRuntimePort>(FPPipelineRuntimePortIds.Diagnostics));
            var writes = new FPWorldResolveBatchWritePorts(
                context.Products.BindExclusiveWriter<WorldSolveBatchResult>(SimulationPipelineProducts.WorldSolveBatchResult),
                context.Products.BindExclusiveWriter<FPCombatBatchResult>(SimulationPipelineProducts.CombatBatchResult));
            return new FPStepPassRuntimeAdapter<FPWorldResolveBatchReadPorts, FPWorldResolveBatchWritePorts>(
                new FPWorldResolveBatchPassRuntime(context.Pass.Descriptor, reads.Combat.Combat),
                reads,
                writes);
        }
    }

    public sealed class FPWorldResolveBatchPassRuntime :
        FPPipelinePassRuntimeBase,
        ISimulationStepPassRuntime<FPWorldResolveBatchReadPorts, FPWorldResolveBatchWritePorts>,
        ISimulationPipelineFrameStateParticipant,
        ISimulationPipelineStepStateStorage
    {
        readonly FPCombatPipelineStateParticipant m_CombatState;
        public FPWorldResolveBatchPassRuntime(SimulationPipelinePassDescriptor descriptor, FPCombatRuntime combat)
            : base(descriptor)
        {
            m_CombatState = new FPCombatPipelineStateParticipant(descriptor, combat);
        }

        public SimulationPipelineStateParticipantIdentity StateIdentity => m_CombatState.StateIdentity;
        public SimulationPipelineStepProjectionMode StepProjectionMode => SimulationPipelineStepProjectionMode.Include;
        public void PrepareStepStorage(int snapshotCapacity) => m_CombatState.PrepareStepStorage(snapshotCapacity);
        public SimulationPipelinePassStateSnapshot CaptureStepState() => m_CombatState.CaptureStepState();
        public ISimulationPipelinePassStateCheckpoint CaptureCheckpoint() => m_CombatState.CaptureCheckpoint();
        public ISimulationPipelineFrameState CreateFrameState() => m_CombatState.CreateFrameState();
        public SimulationPipelinePassStateSnapshot CaptureState() => m_CombatState.CaptureState();
        public ISimulationPipelinePassRestoreTransaction PrepareRestore(SimulationPipelinePassStateSnapshot snapshot) => m_CombatState.PrepareRestore(snapshot);

        public void Execute(
            SimulationPipelineStepTransactionContext context,
            FPWorldResolveBatchReadPorts readPorts,
            FPWorldResolveBatchWritePorts writePorts)
        {
            RequireExecution();
            WorldSolveBatchRequest request = readPorts.Request.Read();
            if (request.Tick != context.Tick)
                throw new InvalidOperationException("World Resolve Pass request belongs to another Step.");
            WorldSolveBatchResult result = FPGameplayStep.WorldResolveBatch(
                readPorts.Solver.Solver, readPorts.Diagnostics.Sink, request,
                readPorts.Evaluations.Read(), readPorts.Combat.Combat, readPorts.Inputs.Read().LocalEnemies,
                out FPCombatBatchResult combatResults);
            writePorts.Result.Write(result);
            writePorts.Combat.Write(combatResults);
        }
    }

    public sealed class FPWorldResolveBatchReadPorts : ISimulationPipelineReadPortSet
    {
        public FPWorldResolveBatchReadPorts(
            IReadOnlySimulationPipelineProductPort<FPCanonicalInputBatch> inputs,
            IReadOnlySimulationPipelineProductPort<WorldSolveBatchRequest> request,
            IReadOnlySimulationPipelineProductPort<FPCharacterEvaluationResultBatch> evaluations,
            IFPCombatRuntimePort combat,
            IFPWorldSolverRuntimePort solver,
            IFPDiagnosticsRuntimePort diagnostics)
        {
            Inputs = inputs;
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Evaluations = evaluations;
            Combat = combat;
            Solver = solver ?? throw new ArgumentNullException(nameof(solver));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public IReadOnlySimulationPipelineProductPort<WorldSolveBatchRequest> Request { get; }
        public IReadOnlySimulationPipelineProductPort<FPCanonicalInputBatch> Inputs { get; }
        public IReadOnlySimulationPipelineProductPort<FPCharacterEvaluationResultBatch> Evaluations { get; }
        public IFPCombatRuntimePort Combat { get; }
        public IFPWorldSolverRuntimePort Solver { get; }
        public IFPDiagnosticsRuntimePort Diagnostics { get; }
    }

    public sealed class FPWorldResolveBatchWritePorts : ISimulationPipelineWritePortSet
    {
        public FPWorldResolveBatchWritePorts(
            IExclusiveSimulationPipelineProductWriter<WorldSolveBatchResult> result,
            IExclusiveSimulationPipelineProductWriter<FPCombatBatchResult> combat)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
            Combat = combat;
        }

        public IExclusiveSimulationPipelineProductWriter<WorldSolveBatchResult> Result { get; }
        public IExclusiveSimulationPipelineProductWriter<FPCombatBatchResult> Combat { get; }
    }
}
