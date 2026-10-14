using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;
using System;

namespace ThirdPersonSimulation.FPGameplay
{
    public sealed class FPAbilityEvaluatePassRuntimeFactory : IFPPipelinePassRuntimeFactory
    {
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFPPipelinePassContracts.CreateFactoryDescriptor(
                StandardFPPipelinePassContracts.AbilityEvaluate);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;

        public IFPCompiledPipelinePassRuntime Create(FPPipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new FPAbilityEvaluateReadPorts(
                context.Products.BindExclusiveReader<FPCanonicalInputBatch>(SimulationPipelineProducts.CanonicalInputs),
                context.BindTargetPort<IFPCharacterRuntimePort>(FPPipelineRuntimePortIds.CharacterRuntime),
                context.BindTargetPort<IFPWorkingStateReadPort>(FPPipelineRuntimePortIds.WorkingState),
                context.BindTargetPort<IFPCombatRuntimePort>(FPPipelineRuntimePortIds.Combat),
                context.BindDiagnosticsPort<IFPDiagnosticsRuntimePort>(FPPipelineRuntimePortIds.Diagnostics));
            var writes = new FPAbilityEvaluateWritePorts(
                context.Products.BindExclusiveWriter<FPCharacterEvaluationResultBatch>(SimulationPipelineProducts.CharacterEvaluationResults),
                context.Products.BindExclusiveWriter<WorldSolveBatchRequest>(SimulationPipelineProducts.WorldSolveBatchRequest));
            return new FPStepPassRuntimeAdapter<FPAbilityEvaluateReadPorts, FPAbilityEvaluateWritePorts>(
                new FPAbilityEvaluatePassRuntime(context.Pass.Descriptor, reads.CharacterRuntime.Runtime, reads.Combat.Combat, context.GraphBatchExecution),
                reads,
                writes);
        }
    }

    public sealed class FPAbilityEvaluatePassRuntime :
        FPPipelinePassRuntimeBase,
        ISimulationStepPassRuntime<FPAbilityEvaluateReadPorts, FPAbilityEvaluateWritePorts>
    {
        readonly FPCharacterBatchEvaluation m_Evaluation;

        public FPAbilityEvaluatePassRuntime(
            SimulationPipelinePassDescriptor descriptor,
            FPCharacterRuntime characters,
            FPCombatRuntime combat,
            GraphBatchExecution batchExecution)
            : base(descriptor)
        {
            m_Evaluation = new FPCharacterBatchEvaluation(characters, combat, batchExecution);
        }

        public void Execute(
            SimulationPipelineStepTransactionContext context,
            FPAbilityEvaluateReadPorts readPorts,
            FPAbilityEvaluateWritePorts writePorts)
        {
            RequireExecution();
            FPSimulationStep step = readPorts.WorkingState.Step;
            if (step.Tick != context.Tick)
                throw new InvalidOperationException("Ability Evaluate Pass belongs to another Step.");
            m_Evaluation.Evaluate(readPorts.WorkingState.Current, step,
                readPorts.Inputs.Read().KinematicBodies, readPorts.Diagnostics.Sink,
                out FPCharacterEvaluationResultBatch evaluations, out WorldSolveBatchRequest worldRequests);
            try
            {
                writePorts.CharacterEvaluationResults.Write(evaluations);
                writePorts.WorldBatch.Write(worldRequests);
            }
            catch
            {
                evaluations.DiscardUnconsumed();
                throw;
            }
        }
    }

    public sealed class FPAbilityEvaluateReadPorts : ISimulationPipelineReadPortSet
    {
        public FPAbilityEvaluateReadPorts(
            IReadOnlySimulationPipelineProductPort<FPCanonicalInputBatch> inputs,
            IFPCharacterRuntimePort characterRuntime,
            IFPWorkingStateReadPort workingState,
            IFPCombatRuntimePort combat,
            IFPDiagnosticsRuntimePort diagnostics)
        {
            Inputs = inputs;
            CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            WorkingState = workingState ?? throw new ArgumentNullException(nameof(workingState));
            Combat = combat;
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public IFPCharacterRuntimePort CharacterRuntime { get; }
        public IReadOnlySimulationPipelineProductPort<FPCanonicalInputBatch> Inputs { get; }
        public IFPWorkingStateReadPort WorkingState { get; }
        public IFPCombatRuntimePort Combat { get; }
        public IFPDiagnosticsRuntimePort Diagnostics { get; }
    }

    public sealed class FPAbilityEvaluateWritePorts : ISimulationPipelineWritePortSet
    {
        public FPAbilityEvaluateWritePorts(
            IExclusiveSimulationPipelineProductWriter<FPCharacterEvaluationResultBatch> characterEvaluationResults,
            IExclusiveSimulationPipelineProductWriter<WorldSolveBatchRequest> worldBatch)
        {
            CharacterEvaluationResults = characterEvaluationResults ?? throw new ArgumentNullException(nameof(characterEvaluationResults));
            WorldBatch = worldBatch ?? throw new ArgumentNullException(nameof(worldBatch));
        }

        public IExclusiveSimulationPipelineProductWriter<FPCharacterEvaluationResultBatch> CharacterEvaluationResults { get; }
        public IExclusiveSimulationPipelineProductWriter<WorldSolveBatchRequest> WorldBatch { get; }
    }
}
