using System;

namespace ThirdPersonSimulation
{
    public sealed class Float32AbilityFinalizePassRuntimeFactory : IFloat32PipelinePassRuntimeFactory
    {
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFloat32PipelinePassContracts.CreateFactoryDescriptor(
                StandardFloat32PipelinePassContracts.AbilityFinalize);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;

        public IFloat32CompiledPipelinePassRuntime Create(Float32PipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new Float32AbilityFinalizeReadPorts(
                context.Products.BindExclusiveReader<Float32CharacterEvaluationResultBatch>(SimulationPipelineProducts.CharacterEvaluationResults),
                context.Products.BindExclusiveReader<WorldSolveBatchResult>(SimulationPipelineProducts.WorldSolveBatchResult),
                context.BindTargetPort<IFloat32CharacterRuntimePort>(Float32PipelineRuntimePortIds.CharacterRuntime),
                context.BindTargetPort<IFloat32WorkingStateReadPort>(Float32PipelineRuntimePortIds.WorkingState),
                context.BindDiagnosticsPort<IFloat32DiagnosticsRuntimePort>(Float32PipelineRuntimePortIds.Diagnostics));
            var writes = new Float32AbilityFinalizeWritePorts(
                context.Products.BindAppendWriter<SimulationActorTickResult>(SimulationPipelineProducts.FinalizedStepResult));
            return new Float32StepPassRuntimeAdapter<Float32AbilityFinalizeReadPorts, Float32AbilityFinalizeWritePorts>(
                new Float32AbilityFinalizePassRuntime(context.Pass.Descriptor),
                reads,
                writes);
        }
    }

    public sealed class Float32AbilityFinalizePassRuntime :
        Float32PipelinePassRuntimeBase,
        ISimulationStepPassRuntime<Float32AbilityFinalizeReadPorts, Float32AbilityFinalizeWritePorts>
    {
        public Float32AbilityFinalizePassRuntime(SimulationPipelinePassDescriptor descriptor)
            : base(descriptor)
        {
        }

        public void Execute(
            SimulationPipelineStepTransactionContext context,
            Float32AbilityFinalizeReadPorts readPorts,
            Float32AbilityFinalizeWritePorts writePorts)
        {
            RequireExecution();
            Float32CharacterEvaluationResultBatch evaluations = readPorts.CharacterEvaluationResults.Read();
            WorldSolveBatchResult world = readPorts.World.Read();
            Float32SimulationStep step = readPorts.WorkingState.Step ??
                throw new InvalidOperationException("Ability Finalize Pass has no current Step.");
            if (evaluations.Tick != context.Tick || world.Tick != context.Tick || step.Tick != context.Tick ||
                evaluations.Evaluations.Count != world.Results.Count ||
                evaluations.Evaluations.Count != readPorts.CharacterRuntime.Runtime.Roster.Count)
                throw new InvalidOperationException("Ability Finalize Pass inputs do not match the current Step roster.");
            for (int i = 0; i < evaluations.Evaluations.Count; i++)
            {
                Float32CharacterEvaluationResult evaluation = evaluations.Evaluations[i];
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
                    worldResult.AppliedYawDegrees);
                evaluation.Consume();
                Float32CharacterRuntimeState finalState = evaluation.CandidateState;
                Float32CharacterEvaluationOutput output = null;
                try
                {
                    evaluation.TakeOutputs(out output);
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

    public sealed class Float32AbilityFinalizeReadPorts : ISimulationPipelineReadPortSet
    {
        public Float32AbilityFinalizeReadPorts(
            IReadOnlySimulationPipelineProductPort<Float32CharacterEvaluationResultBatch> characterEvaluationResults,
            IReadOnlySimulationPipelineProductPort<WorldSolveBatchResult> world,
            IFloat32CharacterRuntimePort characterRuntime,
            IFloat32WorkingStateReadPort workingState,
            IFloat32DiagnosticsRuntimePort diagnostics)
        {
            CharacterEvaluationResults = characterEvaluationResults ?? throw new ArgumentNullException(nameof(characterEvaluationResults));
            World = world ?? throw new ArgumentNullException(nameof(world));
            CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            WorkingState = workingState ?? throw new ArgumentNullException(nameof(workingState));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public IReadOnlySimulationPipelineProductPort<Float32CharacterEvaluationResultBatch> CharacterEvaluationResults { get; }
        public IReadOnlySimulationPipelineProductPort<WorldSolveBatchResult> World { get; }
        public IFloat32CharacterRuntimePort CharacterRuntime { get; }
        public IFloat32WorkingStateReadPort WorkingState { get; }
        public IFloat32DiagnosticsRuntimePort Diagnostics { get; }
    }

    public sealed class Float32AbilityFinalizeWritePorts : ISimulationPipelineWritePortSet
    {
        public Float32AbilityFinalizeWritePorts(
            IAppendOnlySimulationPipelineProductWriter<SimulationActorTickResult> results)
        {
            Results = results ?? throw new ArgumentNullException(nameof(results));
        }

        public IAppendOnlySimulationPipelineProductWriter<SimulationActorTickResult> Results { get; }
    }

    static class Float32PipelineDiagnostics
    {
        public static void PublishOperations(
            ISimulationDiagnosticsSink sink,
            System.Collections.Generic.IReadOnlyList<SimulationTraceRecord> records,
            int start)
        {
            if (sink == null || !sink.IsEnabled || records == null)
                return;
            for (int i = Math.Max(0, start); i < records.Count; i++)
                sink.PublishOperation(records[i]);
        }
    }
}
