using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    public static class StandardFixedPipelinePassContracts
    {
        public const string ImplementationVersion = "4";
        public const string CombatStateSchemaId = "fixed-combat-state/4";
        public const int CombatStateSchemaVersion = 5;
        public const string AbilityEvaluatePassId = "thirdperson.simulation.fixed-ability-evaluate";
        public const string WorldResolveBatchPassId = "thirdperson.simulation.fixed-world-resolve-batch";
        public const string AbilityFinalizePassId = "thirdperson.simulation.fixed-ability-finalize";

        static readonly SimulationPipelineExecutionSupport s_AllExecution =
            SimulationPipelineExecutionSupport.Forward |
            SimulationPipelineExecutionSupport.Replay |
            SimulationPipelineExecutionSupport.Restore |
            SimulationPipelineExecutionSupport.Authoritative;

        static readonly SimulationPipelinePassDescriptor s_WorldResolveBatch = Create(
            WorldResolveBatchPassId,
            new[]
            {
                Consume(SimulationPipelineProducts.CanonicalInputs),
                Consume(SimulationPipelineProducts.WorldSolveBatchRequest),
                Consume(SimulationPipelineProducts.CharacterEvaluationResults),
                Produce(SimulationPipelineProducts.WorldSolveBatchResult),
                Produce(SimulationPipelineProducts.CombatBatchResult)
            },
            new[]
            {
                Solver(),
                Target(FixedPipelineRuntimePortIds.Combat, FixedPipelineRuntimePortIds.CombatSchema),
                Diagnostics()
            });

        static readonly SimulationPipelinePassDescriptor s_AbilityEvaluate = Create(
            AbilityEvaluatePassId,
            new[]
            {
                Consume(SimulationPipelineProducts.CanonicalInputs),
                Produce(SimulationPipelineProducts.CharacterEvaluationResults),
                Produce(SimulationPipelineProducts.WorldSolveBatchRequest)
            },
            new[]
            {
                Target(FixedPipelineRuntimePortIds.CharacterRuntime, FixedPipelineRuntimePortIds.CharacterRuntimeSchema),
                Target(FixedPipelineRuntimePortIds.WorkingState, FixedPipelineRuntimePortIds.WorkingStateSchema),
                Target(FixedPipelineRuntimePortIds.Combat, FixedPipelineRuntimePortIds.CombatSchema),
                Diagnostics()
            });

        static readonly SimulationPipelinePassDescriptor s_AbilityFinalize = Create(
            AbilityFinalizePassId,
            new[]
            {
                Consume(SimulationPipelineProducts.CharacterEvaluationResults),
                Consume(SimulationPipelineProducts.WorldSolveBatchResult),
                Consume(SimulationPipelineProducts.CombatBatchResult),
                Append(SimulationPipelineProducts.FinalizedStepResult)
            },
            new[]
            {
                Target(FixedPipelineRuntimePortIds.CharacterRuntime, FixedPipelineRuntimePortIds.CharacterRuntimeSchema),
                Target(FixedPipelineRuntimePortIds.WorkingState, FixedPipelineRuntimePortIds.WorkingStateSchema),
                Target(FixedPipelineRuntimePortIds.Combat, FixedPipelineRuntimePortIds.CombatSchema),
                Diagnostics()
            });

        public static SimulationPipelinePassDescriptor AbilityEvaluate => s_AbilityEvaluate;
        public static SimulationPipelinePassDescriptor WorldResolveBatch => s_WorldResolveBatch;
        public static SimulationPipelinePassDescriptor AbilityFinalize => s_AbilityFinalize;

        public static SimulationPipelinePassFactoryDescriptor CreateFactoryDescriptor(
            SimulationPipelinePassDescriptor descriptor)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            bool stateful = descriptor.StateClass == SimulationPipelinePassStateClass.SnapshotParticipant;
            return new SimulationPipelinePassFactoryDescriptor(
                new SimulationPipelinePassFactoryIdentity(
                    descriptor.PassId,
                    descriptor.ImplementationVersion,
                    "1",
                    StableHash.Compute("fixed-standard-pass-binding/1", descriptor.DescriptorHash.ToString())),
                descriptor.Phase,
                FixedPassExecutionBackend.BackendId,
                FixedPassExecutionBackend.SemanticVersion,
                descriptor.ConfigurationHash,
                descriptor.ExecutionSupport,
                true,
                stateful,
                stateful,
                false,
                stateful ? CombatStateSchemaId : string.Empty,
                stateful ? CombatStateSchemaVersion : 0);
        }

        static SimulationPipelinePassDescriptor Create(
            string passId,
            IEnumerable<SimulationPipelineProductAccess> products,
            IEnumerable<SimulationPipelinePortRequirement> ports)
        {
            return new SimulationPipelinePassDescriptor(
                new SimulationPipelinePassId(passId),
                new SimulationPipelinePassImplementationVersion(ImplementationVersion),
                SimulationPipelinePhase.Step,
                StableHash.Compute("standard-fixed-pass-config/1", passId),
                FixedSimulationNumericProfile.Value.Id,
                FixedSimulationNumericProfile.Value.AbiVersion,
                FixedPassExecutionBackend.BackendId,
                FixedPassExecutionBackend.SemanticVersion,
                WorldCapability.None,
                s_AllExecution,
                passId == WorldResolveBatchPassId ? SimulationPipelinePassStateClass.SnapshotParticipant : SimulationPipelinePassStateClass.Stateless,
                passId == WorldResolveBatchPassId ? "thirdperson.combat" : string.Empty,
                products,
                ports);
        }

        static SimulationPipelineProductAccess Produce(SimulationPipelineProductContract product) =>
            new SimulationPipelineProductAccess(product, SimulationPipelineProductAccessKind.ExclusiveProducer);

        static SimulationPipelineProductAccess Consume(SimulationPipelineProductContract product) =>
            new SimulationPipelineProductAccess(product, SimulationPipelineProductAccessKind.ReadOnlyConsumer);

        static SimulationPipelineProductAccess Append(SimulationPipelineProductContract product) =>
            new SimulationPipelineProductAccess(product, SimulationPipelineProductAccessKind.AppendOnlyProducer);

        static SimulationPipelinePortRequirement Target(string portId, string schemaId) =>
            new SimulationPipelinePortRequirement(
                SimulationPipelineBindingPortRole.Target,
                portId,
                schemaId,
                1,
                SimulationPortDirection.Input);

        static SimulationPipelinePortRequirement Solver() =>
            new SimulationPipelinePortRequirement(
                SimulationPipelineBindingPortRole.Solver,
                FixedPipelineRuntimePortIds.WorldSolver,
                FixedPipelineRuntimePortIds.WorldSolverSchema,
                1,
                SimulationPortDirection.Input);

        static SimulationPipelinePortRequirement Diagnostics() =>
            new SimulationPipelinePortRequirement(
                SimulationPipelineBindingPortRole.Diagnostics,
                FixedPipelineRuntimePortIds.Diagnostics,
                FixedPipelineRuntimePortIds.DiagnosticsSchema,
                1,
                SimulationPortDirection.Input);
    }
}
