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
            FixedCharacterEvaluationResultBatch evaluations = readPorts.CharacterEvaluationResults.Read();
            WorldSolveBatchResult world = readPorts.World.Read();
            FixedSimulationStep step = readPorts.WorkingState.Step ??
                throw new InvalidOperationException("Ability Finalize Pass has no current Step.");
            if (evaluations.Tick != context.Tick || world.Tick != context.Tick || step.Tick != context.Tick ||
                evaluations.Evaluations.Count != world.Results.Count ||
                evaluations.Evaluations.Count != readPorts.CharacterRuntime.Runtime.Roster.Count)
                throw new InvalidOperationException("Ability Finalize Pass inputs do not match the current Step roster.");
            for (int i = 0; i < evaluations.Evaluations.Count; i++)
            {
                FixedCharacterEvaluationResult evaluation = evaluations.Evaluations[i];
                CharacterWorldSolveResult worldResult = world.Results[i];
                SimulationActorBinding actor = readPorts.CharacterRuntime.Runtime.Roster[i];
                if (!evaluation.ActorId.Equals(actor.ActorId) || !worldResult.ActorId.Equals(actor.ActorId))
                    throw new InvalidOperationException("Ability Finalize Pass Actor order does not match the locked roster.");
                CharacterWorldSolveRequest expected = world.Request.Requests[i];
                if (worldResult.NumericProfile != readPorts.CharacterRuntime.Runtime.NumericProfile ||
                    !worldResult.RequestId.Equals(expected.RequestId) ||
                    worldResult.Tick != expected.Tick ||
                    !worldResult.SolverId.Equals(world.SolverId) ||
                    worldResult.FinalBody.ActorId != evaluation.ActorId)
                {
                    throw new InvalidOperationException(
                        $"World result '{worldResult.RequestId}' does not match character evaluation request '{expected.RequestId}'.");
                }
                var bodySample = new CharacterBodySample(
                    evaluation.ActorId,
                    evaluation.Tick,
                    expected.BeforeBody,
                    worldResult.FinalBody,
                    worldResult.AppliedDisplacement,
                    worldResult.AppliedYawDegrees,
                    readPorts.Combat.Combat.WorldTimeScale,
                    readPorts.Combat.Combat.OwnerTimeScale(actor.ActorId));
                evaluation.Consume();
                FixedCharacterRuntimeState finalState = evaluation.CandidateState;
                FixedCharacterEvaluationOutput output = null;
                try
                {
                    evaluation.TakeOutputs(out output);
                    readPorts.Combat.Combat.Project(i, readPorts.CombatResults.Read(), output);
                    SimulationActorTickResult result = SimulationActorTickResult.FromOwnedOutputs(
                        evaluation.ActorId,
                        evaluation.Tick,
                        finalState,
                        bodySample,
                        expected.Motion,
                        output,
                        actor);
                    writePorts.Results.Append(
                        new SimulationPipelineAppendEntryIdentity(
                            actor.ActorId,
                            context.Tick,
                            1,
                            step.Source),
                        result);
                    output = null;
                }
                finally
                {
                    if (output != null)
                        actor.ReturnEvaluationOutput(output);
                }

            }
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
