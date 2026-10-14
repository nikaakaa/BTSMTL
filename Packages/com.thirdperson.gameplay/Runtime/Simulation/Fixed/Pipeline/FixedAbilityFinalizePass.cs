using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class FixedAbilityFinalizePassRuntimeFactory : IFixedPipelinePassRuntimeFactory
    {
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFixedPipelinePassContracts.CreateFactoryDescriptor(
                StandardFixedPipelinePassContracts.AbilityFinalize);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;

        public IFixedCompiledPipelinePassRuntime Create(FixedPipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new FixedAbilityFinalizeReadPorts(
                context.Products.BindExclusiveReader<FixedCharacterEvaluationResultBatch>(SimulationPipelineProducts.CharacterEvaluationResults),
                context.Products.BindExclusiveReader<WorldSolveBatchResult>(SimulationPipelineProducts.WorldSolveBatchResult),
                context.Products.BindExclusiveReader<FixedCombatBatchResult>(SimulationPipelineProducts.CombatBatchResult),
                context.BindTargetPort<IFixedCharacterRuntimePort>(FixedPipelineRuntimePortIds.CharacterRuntime),
                context.BindTargetPort<IFixedWorkingStateReadPort>(FixedPipelineRuntimePortIds.WorkingState),
                context.BindTargetPort<IFixedCombatRuntimePort>(FixedPipelineRuntimePortIds.Combat),
                context.BindDiagnosticsPort<IFixedDiagnosticsRuntimePort>(FixedPipelineRuntimePortIds.Diagnostics));
            var writes = new FixedAbilityFinalizeWritePorts(
                context.Products.BindAppendWriter<SimulationActorTickResult>(SimulationPipelineProducts.FinalizedStepResult));
            return new FixedStepPassRuntimeAdapter<FixedAbilityFinalizeReadPorts, FixedAbilityFinalizeWritePorts>(
                new FixedAbilityFinalizePassRuntime(context.Pass.Descriptor),
                reads,
                writes);
        }
    }

    public sealed class FixedAbilityFinalizePassRuntime :
        FixedPipelinePassRuntimeBase,
        ISimulationStepPassRuntime<FixedAbilityFinalizeReadPorts, FixedAbilityFinalizeWritePorts>
    {
        public FixedAbilityFinalizePassRuntime(SimulationPipelinePassDescriptor descriptor)
            : base(descriptor)
        {
        }

        public void Execute(
            SimulationPipelineStepTransactionContext context,
            FixedAbilityFinalizeReadPorts readPorts,
            FixedAbilityFinalizeWritePorts writePorts)
        {
            RequireExecution();
            FixedSimulationStep step = readPorts.WorkingState.Step;
            if (step.Tick != context.Tick)
                throw new InvalidOperationException("Ability Finalize Pass belongs to another Step.");
            FixedGameplayStep.Finalize(readPorts.CharacterRuntime.Runtime, step,
                readPorts.CharacterEvaluationResults.Read(), readPorts.World.Read(),
                readPorts.Combat.Combat, readPorts.CombatResults.Read(), writePorts.Results);
        }
    }

    public sealed class FixedAbilityFinalizeReadPorts : ISimulationPipelineReadPortSet
    {
        public FixedAbilityFinalizeReadPorts(
            IReadOnlySimulationPipelineProductPort<FixedCharacterEvaluationResultBatch> characterEvaluationResults,
            IReadOnlySimulationPipelineProductPort<WorldSolveBatchResult> world,
            IReadOnlySimulationPipelineProductPort<FixedCombatBatchResult> combatResults,
            IFixedCharacterRuntimePort characterRuntime,
            IFixedWorkingStateReadPort workingState,
            IFixedCombatRuntimePort combat,
            IFixedDiagnosticsRuntimePort diagnostics)
        {
            CharacterEvaluationResults = characterEvaluationResults ?? throw new ArgumentNullException(nameof(characterEvaluationResults));
            World = world ?? throw new ArgumentNullException(nameof(world));
            CombatResults = combatResults;
            Combat = combat;
            CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            WorkingState = workingState ?? throw new ArgumentNullException(nameof(workingState));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public IReadOnlySimulationPipelineProductPort<FixedCharacterEvaluationResultBatch> CharacterEvaluationResults { get; }
        public IReadOnlySimulationPipelineProductPort<WorldSolveBatchResult> World { get; }
        public IReadOnlySimulationPipelineProductPort<FixedCombatBatchResult> CombatResults { get; }
        public IFixedCombatRuntimePort Combat { get; }
        public IFixedCharacterRuntimePort CharacterRuntime { get; }
        public IFixedWorkingStateReadPort WorkingState { get; }
        public IFixedDiagnosticsRuntimePort Diagnostics { get; }
    }

    public sealed class FixedAbilityFinalizeWritePorts : ISimulationPipelineWritePortSet
    {
        public FixedAbilityFinalizeWritePorts(
            IAppendOnlySimulationPipelineProductWriter<SimulationActorTickResult> results)
        {
            Results = results ?? throw new ArgumentNullException(nameof(results));
        }

        public IAppendOnlySimulationPipelineProductWriter<SimulationActorTickResult> Results { get; }
    }

    static class FixedPipelineDiagnostics
    {
        public static void PublishOperations(
            ISimulationDiagnosticsSink sink,
            IReadOnlyList<SimulationTraceRecord> records,
            int start)
        {
            if (sink == null || !sink.IsEnabled || records == null)
                return;
            for (int i = Math.Max(0, start); i < records.Count; i++)
                sink.PublishOperation(records[i]);
        }
    }
}
