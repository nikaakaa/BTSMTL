using ThirdPersonSimulation;
using System;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class FixedAbilityEvaluatePassRuntimeFactory : IFixedPipelinePassRuntimeFactory
    {
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFixedPipelinePassContracts.CreateFactoryDescriptor(
                StandardFixedPipelinePassContracts.AbilityEvaluate);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;

        public IFixedCompiledPipelinePassRuntime Create(FixedPipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new FixedAbilityEvaluateReadPorts(
                context.Products.BindExclusiveReader<FixedCanonicalInputBatch>(SimulationPipelineProducts.CanonicalInputs),
                context.BindTargetPort<IFixedCharacterRuntimePort>(FixedPipelineRuntimePortIds.CharacterRuntime),
                context.BindTargetPort<IFixedWorkingStateReadPort>(FixedPipelineRuntimePortIds.WorkingState),
                context.BindTargetPort<IFixedCombatRuntimePort>(FixedPipelineRuntimePortIds.Combat),
                context.BindDiagnosticsPort<IFixedDiagnosticsRuntimePort>(FixedPipelineRuntimePortIds.Diagnostics));
            var writes = new FixedAbilityEvaluateWritePorts(
                context.Products.BindExclusiveWriter<FixedCharacterEvaluationResultBatch>(SimulationPipelineProducts.CharacterEvaluationResults),
                context.Products.BindExclusiveWriter<WorldSolveBatchRequest>(SimulationPipelineProducts.WorldSolveBatchRequest));
            return new FixedStepPassRuntimeAdapter<FixedAbilityEvaluateReadPorts, FixedAbilityEvaluateWritePorts>(
                new FixedAbilityEvaluatePassRuntime(context.Pass.Descriptor, reads.CharacterRuntime.Runtime, reads.Combat.Combat, context.GraphBatchExecution),
                reads,
                writes);
        }
    }

    public sealed class FixedAbilityEvaluatePassRuntime :
        FixedPipelinePassRuntimeBase,
        ISimulationStepPassRuntime<FixedAbilityEvaluateReadPorts, FixedAbilityEvaluateWritePorts>
    {
        readonly FixedCharacterBatchEvaluation m_Evaluation;

        public FixedAbilityEvaluatePassRuntime(
            SimulationPipelinePassDescriptor descriptor,
            FixedCharacterRuntime characters,
            FixedCombatRuntime combat,
            GraphBatchExecution batchExecution)
            : base(descriptor)
        {
            m_Evaluation = new FixedCharacterBatchEvaluation(characters, combat, batchExecution);
        }

        public void Execute(
            SimulationPipelineStepTransactionContext context,
            FixedAbilityEvaluateReadPorts readPorts,
            FixedAbilityEvaluateWritePorts writePorts)
        {
            RequireExecution();
            FixedSimulationStep step = readPorts.WorkingState.Step;
            if (step.Tick != context.Tick)
                throw new InvalidOperationException("Ability Evaluate Pass belongs to another Step.");
            m_Evaluation.Evaluate(readPorts.WorkingState.Current, step,
                readPorts.Inputs.Read().KinematicBodies, readPorts.Diagnostics.Sink,
                out FixedCharacterEvaluationResultBatch evaluations, out WorldSolveBatchRequest worldRequests);
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

    public sealed class FixedAbilityEvaluateReadPorts : ISimulationPipelineReadPortSet
    {
        public FixedAbilityEvaluateReadPorts(
            IReadOnlySimulationPipelineProductPort<FixedCanonicalInputBatch> inputs,
            IFixedCharacterRuntimePort characterRuntime,
            IFixedWorkingStateReadPort workingState,
            IFixedCombatRuntimePort combat,
            IFixedDiagnosticsRuntimePort diagnostics)
        {
            Inputs = inputs;
            CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            WorkingState = workingState ?? throw new ArgumentNullException(nameof(workingState));
            Combat = combat;
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public IFixedCharacterRuntimePort CharacterRuntime { get; }
        public IReadOnlySimulationPipelineProductPort<FixedCanonicalInputBatch> Inputs { get; }
        public IFixedWorkingStateReadPort WorkingState { get; }
        public IFixedCombatRuntimePort Combat { get; }
        public IFixedDiagnosticsRuntimePort Diagnostics { get; }
    }

    public sealed class FixedAbilityEvaluateWritePorts : ISimulationPipelineWritePortSet
    {
        public FixedAbilityEvaluateWritePorts(
            IExclusiveSimulationPipelineProductWriter<FixedCharacterEvaluationResultBatch> characterEvaluationResults,
            IExclusiveSimulationPipelineProductWriter<WorldSolveBatchRequest> worldBatch)
        {
            CharacterEvaluationResults = characterEvaluationResults ?? throw new ArgumentNullException(nameof(characterEvaluationResults));
            WorldBatch = worldBatch ?? throw new ArgumentNullException(nameof(worldBatch));
        }

        public IExclusiveSimulationPipelineProductWriter<FixedCharacterEvaluationResultBatch> CharacterEvaluationResults { get; }
        public IExclusiveSimulationPipelineProductWriter<WorldSolveBatchRequest> WorldBatch { get; }
    }
}
