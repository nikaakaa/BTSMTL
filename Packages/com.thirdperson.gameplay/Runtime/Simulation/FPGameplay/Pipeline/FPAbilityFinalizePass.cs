using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    public sealed class FPAbilityFinalizePassRuntimeFactory : IFPPipelinePassRuntimeFactory
    {
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFPPipelinePassContracts.CreateFactoryDescriptor(
                StandardFPPipelinePassContracts.AbilityFinalize);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;

        public IFPCompiledPipelinePassRuntime Create(FPPipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new FPAbilityFinalizeReadPorts(
                context.Products.BindExclusiveReader<FPCharacterEvaluationResultBatch>(SimulationPipelineProducts.CharacterEvaluationResults),
                context.Products.BindExclusiveReader<WorldSolveBatchResult>(SimulationPipelineProducts.WorldSolveBatchResult),
                context.Products.BindExclusiveReader<FPCombatBatchResult>(SimulationPipelineProducts.CombatBatchResult),
                context.BindTargetPort<IFPCharacterRuntimePort>(FPPipelineRuntimePortIds.CharacterRuntime),
                context.BindTargetPort<IFPWorkingStateReadPort>(FPPipelineRuntimePortIds.WorkingState),
                context.BindTargetPort<IFPCombatRuntimePort>(FPPipelineRuntimePortIds.Combat),
                context.BindDiagnosticsPort<IFPDiagnosticsRuntimePort>(FPPipelineRuntimePortIds.Diagnostics));
            var writes = new FPAbilityFinalizeWritePorts(
                context.Products.BindAppendWriter<SimulationActorTickResult>(SimulationPipelineProducts.FinalizedStepResult));
            return new FPStepPassRuntimeAdapter<FPAbilityFinalizeReadPorts, FPAbilityFinalizeWritePorts>(
                new FPAbilityFinalizePassRuntime(context.Pass.Descriptor),
                reads,
                writes);
        }
    }

    public sealed class FPAbilityFinalizePassRuntime :
        FPPipelinePassRuntimeBase,
        ISimulationStepPassRuntime<FPAbilityFinalizeReadPorts, FPAbilityFinalizeWritePorts>
    {
        public FPAbilityFinalizePassRuntime(SimulationPipelinePassDescriptor descriptor)
            : base(descriptor)
        {
        }

        public void Execute(
            SimulationPipelineStepTransactionContext context,
            FPAbilityFinalizeReadPorts readPorts,
            FPAbilityFinalizeWritePorts writePorts)
        {
            RequireExecution();
            FPSimulationStep step = readPorts.WorkingState.Step;
            if (step.Tick != context.Tick)
                throw new InvalidOperationException("Ability Finalize Pass belongs to another Step.");
            FPGameplayStep.Finalize(readPorts.CharacterRuntime.Runtime, step,
                readPorts.CharacterEvaluationResults.Read(), readPorts.World.Read(),
                readPorts.Combat.Combat, readPorts.CombatResults.Read(), writePorts.Results);
        }
    }

    public sealed class FPAbilityFinalizeReadPorts : ISimulationPipelineReadPortSet
    {
        public FPAbilityFinalizeReadPorts(
            IReadOnlySimulationPipelineProductPort<FPCharacterEvaluationResultBatch> characterEvaluationResults,
            IReadOnlySimulationPipelineProductPort<WorldSolveBatchResult> world,
            IReadOnlySimulationPipelineProductPort<FPCombatBatchResult> combatResults,
            IFPCharacterRuntimePort characterRuntime,
            IFPWorkingStateReadPort workingState,
            IFPCombatRuntimePort combat,
            IFPDiagnosticsRuntimePort diagnostics)
        {
            CharacterEvaluationResults = characterEvaluationResults ?? throw new ArgumentNullException(nameof(characterEvaluationResults));
            World = world ?? throw new ArgumentNullException(nameof(world));
            CombatResults = combatResults;
            Combat = combat;
            CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            WorkingState = workingState ?? throw new ArgumentNullException(nameof(workingState));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public IReadOnlySimulationPipelineProductPort<FPCharacterEvaluationResultBatch> CharacterEvaluationResults { get; }
        public IReadOnlySimulationPipelineProductPort<WorldSolveBatchResult> World { get; }
        public IReadOnlySimulationPipelineProductPort<FPCombatBatchResult> CombatResults { get; }
        public IFPCombatRuntimePort Combat { get; }
        public IFPCharacterRuntimePort CharacterRuntime { get; }
        public IFPWorkingStateReadPort WorkingState { get; }
        public IFPDiagnosticsRuntimePort Diagnostics { get; }
    }

    public sealed class FPAbilityFinalizeWritePorts : ISimulationPipelineWritePortSet
    {
        public FPAbilityFinalizeWritePorts(
            IAppendOnlySimulationPipelineProductWriter<SimulationActorTickResult> results)
        {
            Results = results ?? throw new ArgumentNullException(nameof(results));
        }

        public IAppendOnlySimulationPipelineProductWriter<SimulationActorTickResult> Results { get; }
    }

    static class FPPipelineDiagnostics
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
