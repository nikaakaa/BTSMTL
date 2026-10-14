using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    public static class FPGameplayStep
    {
        public static void AdoptTimelineBaseline(
            IReadOnlyList<SimulationActorBinding> roster,
            in SimulationWorldStateSet state,
            bool discardUnpublishedPlaybacks)
        {
            for (int actorIndex = 0; actorIndex < state.Actors.Count; actorIndex++)
            {
                IReadOnlyList<AbilityTimelineRuntimeSnapshot> snapshots = state.Actors[actorIndex].State.TimelineSnapshots;
                IAbilityTimelineRuntime timeline = roster[actorIndex].TimelineRuntime;
                for (int index = 0; index < snapshots.Count; index++)
                    timeline.ApplyRestore(snapshots[index], state.LastCompletedTick);
                if (discardUnpublishedPlaybacks)
                    timeline?.DiscardUnpublishedPlaybacks(snapshots);
            }
        }

        public static WorldSolveBatchResult WorldResolveBatch(
            ICharacterWorldBatchSolver solver,
            ISimulationDiagnosticsSink diagnostics,
            WorldSolveBatchRequest request,
            FPCharacterEvaluationResultBatch evaluations,
            FPCombatRuntime combat,
            IReadOnlyList<FPLocalEnemyInput> localEnemies,
            out FPCombatBatchResult combatResults)
        {
            WorldSolveBatchResult result = solver.ResolveBatch(request, diagnostics);
            if (result.Tick != request.Tick || !ReferenceEquals(result.Request, request) ||
                !result.SolverId.Equals(solver.Descriptor.ImplementationId) ||
                !string.Equals(result.SolverVersion, solver.Descriptor.Version, StringComparison.Ordinal) ||
                result.Results.Count != request.Requests.Count)
            {
                throw new InvalidOperationException("World Solver returned a batch for another request, Tick or Solver identity.");
            }
            combatResults = combat.Resolve(evaluations, result, localEnemies);
            return result;
        }

        public static void Finalize(
            FPCharacterRuntime characters,
            FPSimulationStep step,
            FPCharacterEvaluationResultBatch evaluations,
            WorldSolveBatchResult world,
            FPCombatRuntime combat,
            FPCombatBatchResult combatResults,
            IAppendOnlySimulationPipelineProductWriter<SimulationActorTickResult> results)
        {
            if (evaluations.Tick != step.Tick || world.Tick != step.Tick ||
                evaluations.Evaluations.Count != world.Results.Count ||
                evaluations.Evaluations.Count != characters.Roster.Count)
                throw new InvalidOperationException("角色 Finalize 输入与当前 Step roster 不匹配。");
            for (int i = 0; i < evaluations.Evaluations.Count; i++)
            {
                FPCharacterEvaluationResult evaluation = evaluations.Evaluations[i];
                CharacterWorldSolveResult worldResult = world.Results[i];
                SimulationActorBinding actor = characters.Roster[i];
                if (!evaluation.ActorId.Equals(actor.ActorId) || !worldResult.ActorId.Equals(actor.ActorId))
                    throw new InvalidOperationException("角色 Finalize 的 Actor 顺序与已锁定 roster 不匹配。");
                CharacterWorldSolveRequest expected = world.Request.Requests[i];
                if (worldResult.NumericProfile != characters.NumericProfile ||
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
                    combat.WorldTimeScale,
                    combat.OwnerTimeScale(actor.ActorId));
                evaluation.Consume();
                FPCharacterRuntimeState finalState = evaluation.CandidateState;
                FPCharacterEvaluationOutput output = null;
                try
                {
                    evaluation.TakeOutputs(out output);
                    combat.Project(i, combatResults, output);
                    SimulationActorTickResult result = SimulationActorTickResult.FromOwnedOutputs(
                        evaluation.ActorId,
                        evaluation.Tick,
                        finalState,
                        bodySample,
                        expected.Motion,
                        output,
                        actor);
                    results.Append(
                        new SimulationPipelineAppendEntryIdentity(
                            actor.ActorId,
                            step.Tick,
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
}
