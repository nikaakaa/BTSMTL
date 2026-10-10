using System.Collections.Generic;
using ThirdPersonPerformance;

namespace ThirdPersonSimulation
{
    public static class SimulationPerformanceMetrics
    {
        static readonly PerformanceMetricDefinition[] s_All =
        {
            Metric("simulation.pipeline.transaction", "ThirdPerson.Simulation.Pipeline.Transaction", string.Empty, PerformanceSampleScope.LogicTick),
            Metric("simulation.pipeline.checkpoint-capture", "ThirdPerson.Simulation.Pipeline.CheckpointCapture", "simulation.pipeline.transaction", PerformanceSampleScope.LogicTick),
            Metric("simulation.pipeline.ingress", "ThirdPerson.Simulation.Pipeline.Ingress", "simulation.pipeline.transaction", PerformanceSampleScope.LogicTick),
            Metric("simulation.pipeline.schedule", "ThirdPerson.Simulation.Pipeline.Schedule", "simulation.pipeline.transaction", PerformanceSampleScope.LogicTick),
            Metric("simulation.pipeline.restore", "ThirdPerson.Simulation.Pipeline.Restore", "simulation.pipeline.transaction", PerformanceSampleScope.LogicTick),
            Metric("simulation.pipeline.evaluate", "ThirdPerson.Simulation.Pipeline.Evaluate", "simulation.pipeline.transaction", PerformanceSampleScope.LogicTick),
            Metric("simulation.pipeline.world-resolve", "ThirdPerson.Simulation.Pipeline.WorldResolve", "simulation.pipeline.transaction", PerformanceSampleScope.LogicTick),
            Metric("simulation.pipeline.finalize", "ThirdPerson.Simulation.Pipeline.Finalize", "simulation.pipeline.transaction", PerformanceSampleScope.LogicTick),
            Metric("simulation.pipeline.egress", "ThirdPerson.Simulation.Pipeline.Egress", "simulation.pipeline.transaction", PerformanceSampleScope.LogicTick),
            Metric("simulation.pipeline.commit-freeze", "ThirdPerson.Simulation.Pipeline.CommitFreeze", "simulation.pipeline.transaction", PerformanceSampleScope.LogicTick),
            Metric("simulation.pipeline.state-publish", "ThirdPerson.Simulation.Pipeline.StatePublish", "simulation.pipeline.transaction", PerformanceSampleScope.LogicTick),
            Metric("simulation.pipeline.external-commit", "ThirdPerson.Simulation.Pipeline.ExternalCommit", "simulation.pipeline.transaction", PerformanceSampleScope.LogicTick),
            Metric("simulation.pipeline.step-other", "ThirdPerson.Simulation.Pipeline.StepOther", "simulation.pipeline.transaction", PerformanceSampleScope.LogicTick),
            Metric("simulation.operation.ability-tick", "ThirdPerson.Simulation.Operation.AbilityTick", "simulation.pipeline.evaluate", PerformanceSampleScope.LogicTick),
            Metric("simulation.character.evaluate", "ThirdPerson.Simulation.Character.Evaluate", "simulation.pipeline.evaluate", PerformanceSampleScope.LogicTick),
            Metric("simulation.gameplay-effect.advance", "ThirdPerson.Simulation.GameplayEffect.Advance", "simulation.character.evaluate", PerformanceSampleScope.LogicTick),
            Metric("simulation.snapshot.world-capture", "ThirdPerson.Simulation.Snapshot.WorldCapture", string.Empty, PerformanceSampleScope.LogicTick),
            Metric("simulation.world.kcc", "ThirdPerson.Simulation.World.Kcc", "simulation.pipeline.world-resolve", PerformanceSampleScope.LogicTick)
        };

        public static IReadOnlyList<PerformanceMetricDefinition> All => s_All;

        static PerformanceMetricDefinition Metric(
            string id,
            string name,
            string parent,
            PerformanceSampleScope scope) =>
            new PerformanceMetricDefinition(
                id,
                name,
                PerformanceMetricDomain.Simulation,
                parent,
                scope,
                PerformanceMetricUnit.Nanoseconds,
                PerformanceMetricAggregation.InclusiveDuration);
    }
}
