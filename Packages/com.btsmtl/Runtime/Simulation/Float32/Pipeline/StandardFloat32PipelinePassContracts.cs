using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public static class StandardFloat32PipelinePassContracts
    {
        public const string ImplementationVersion = "4";
        public const string LocalControlInputStateSchemaId = "float32-local-control-input-state";
        public const int LocalControlInputStateSchemaVersion = 3;
        public const string LocalInputIngressPassId = "thirdperson.simulation.local-input-ingress";
        public const string LocalSingleStepSchedulePassId = "thirdperson.simulation.local-single-step-schedule";
        public const string AbilityEvaluatePassId = "thirdperson.simulation.float32-ability-evaluate";
        public const string WorldResolveBatchPassId = "thirdperson.simulation.float32-world-resolve-batch";
        public const string AbilityFinalizePassId = "thirdperson.simulation.float32-ability-finalize";
        public const string LocalImmediateOutputPassId = "thirdperson.simulation.local-immediate-output";

        static readonly SimulationPipelineExecutionSupport s_AllExecution =
            SimulationPipelineExecutionSupport.Forward |
            SimulationPipelineExecutionSupport.Replay |
            SimulationPipelineExecutionSupport.Restore |
            SimulationPipelineExecutionSupport.Authoritative;

        static readonly SimulationPipelinePassDescriptor s_LocalInputIngress = Create(
            LocalInputIngressPassId,
            SimulationPipelinePhase.Ingress,
            SimulationPipelineExecutionSupport.Forward,
            SimulationPipelinePassStateClass.SnapshotParticipant,
            "local-control-input",
            new[]
            {
                Produce(SimulationPipelineProducts.CanonicalInputs),
                Produce(SimulationPipelineProducts.TypedIngress)
            },
            new[]
            {
                Float32LocalInputSourcePortContract.Requirement,
                Target(Float32PipelineRuntimePortIds.CommittedObservation, Float32PipelineRuntimePortIds.CommittedObservationSchema)
            });

        static readonly SimulationPipelinePassDescriptor s_LocalSingleStepSchedule = Create(
            LocalSingleStepSchedulePassId,
            SimulationPipelinePhase.Schedule,
            SimulationPipelineExecutionSupport.Forward,
            SimulationPipelinePassStateClass.Stateless,
            string.Empty,
            new[]
            {
                Consume(SimulationPipelineProducts.CanonicalInputs),
                Consume(SimulationPipelineProducts.TypedIngress),
                Produce(SimulationPipelineProducts.ExecutionPlan)
            },
            Array.Empty<SimulationPipelinePortRequirement>());

        static readonly SimulationPipelinePassDescriptor s_AbilityEvaluate = Create(
            AbilityEvaluatePassId,
            SimulationPipelinePhase.Step,
            s_AllExecution,
            SimulationPipelinePassStateClass.Stateless,
            string.Empty,
            new[]
            {
                Produce(SimulationPipelineProducts.CharacterEvaluationResults),
                Produce(SimulationPipelineProducts.WorldSolveBatchRequest)
            },
            new[]
            {
                Target(Float32PipelineRuntimePortIds.CharacterRuntime, Float32PipelineRuntimePortIds.CharacterRuntimeSchema),
                Target(Float32PipelineRuntimePortIds.WorkingState, Float32PipelineRuntimePortIds.WorkingStateSchema),
                Diagnostics()
            });

        static readonly SimulationPipelinePassDescriptor s_WorldResolveBatch = Create(
            WorldResolveBatchPassId,
            SimulationPipelinePhase.Step,
            s_AllExecution,
            SimulationPipelinePassStateClass.Stateless,
            string.Empty,
            new[]
            {
                Consume(SimulationPipelineProducts.WorldSolveBatchRequest),
                Produce(SimulationPipelineProducts.WorldSolveBatchResult)
            },
            new[]
            {
                Solver(),
                Diagnostics()
            });

        static readonly SimulationPipelinePassDescriptor s_AbilityFinalize = Create(
            AbilityFinalizePassId,
            SimulationPipelinePhase.Step,
            s_AllExecution,
            SimulationPipelinePassStateClass.Stateless,
            string.Empty,
            new[]
            {
                Consume(SimulationPipelineProducts.CharacterEvaluationResults),
                Consume(SimulationPipelineProducts.WorldSolveBatchResult),
                Append(SimulationPipelineProducts.FinalizedStepResult)
            },
            new[]
            {
                Target(Float32PipelineRuntimePortIds.CharacterRuntime, Float32PipelineRuntimePortIds.CharacterRuntimeSchema),
                Target(Float32PipelineRuntimePortIds.WorkingState, Float32PipelineRuntimePortIds.WorkingStateSchema),
                Diagnostics()
            });

        static readonly SimulationPipelinePassDescriptor s_LocalImmediateOutput = Create(
            LocalImmediateOutputPassId,
            SimulationPipelinePhase.Egress,
            SimulationPipelineExecutionSupport.Forward,
            SimulationPipelinePassStateClass.Stateless,
            string.Empty,
            new[]
            {
                Consume(SimulationPipelineProducts.FinalizedStepResult),
                Produce(SimulationPipelineProducts.OutputDispositionSet)
            },
            new[]
            {
                Target(Float32PipelineRuntimePortIds.CharacterRuntime, Float32PipelineRuntimePortIds.CharacterRuntimeSchema)
            });

        public static SimulationPipelinePassDescriptor LocalInputIngress => s_LocalInputIngress;
        public static SimulationPipelinePassDescriptor LocalSingleStepSchedule => s_LocalSingleStepSchedule;
        public static SimulationPipelinePassDescriptor AbilityEvaluate => s_AbilityEvaluate;
        public static SimulationPipelinePassDescriptor WorldResolveBatch => s_WorldResolveBatch;
        public static SimulationPipelinePassDescriptor AbilityFinalize => s_AbilityFinalize;
        public static SimulationPipelinePassDescriptor LocalImmediateOutput => s_LocalImmediateOutput;

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
                    StableHash.Compute("float32-standard-pass-binding/1", descriptor.DescriptorHash.ToString())),
                descriptor.Phase,
                Float32PassExecutionBackend.BackendId,
                Float32PassExecutionBackend.SemanticVersion,
                descriptor.ConfigurationHash,
                descriptor.ExecutionSupport,
                false,
                stateful,
                stateful,
                false,
                stateful ? LocalControlInputStateSchemaId : string.Empty,
                stateful ? LocalControlInputStateSchemaVersion : 0);
        }

        static SimulationPipelinePassDescriptor Create(
            string passId,
            SimulationPipelinePhase phase,
            SimulationPipelineExecutionSupport executionSupport,
            SimulationPipelinePassStateClass stateClass,
            string stateOwner,
            IEnumerable<SimulationPipelineProductAccess> products,
            IEnumerable<SimulationPipelinePortRequirement> ports)
        {
            return new SimulationPipelinePassDescriptor(
                new SimulationPipelinePassId(passId),
                new SimulationPipelinePassImplementationVersion(ImplementationVersion),
                phase,
                StableHash.Compute("standard-float32-pass-config/2", passId),
                Float32SimulationNumericProfile.Value.Id,
                Float32SimulationNumericProfile.Value.AbiVersion,
                Float32PassExecutionBackend.BackendId,
                Float32PassExecutionBackend.SemanticVersion,
                WorldCapability.None,
                executionSupport,
                stateClass,
                stateOwner,
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
                Float32PipelineRuntimePortIds.WorldSolver,
                Float32PipelineRuntimePortIds.WorldSolverSchema,
                1,
                SimulationPortDirection.Input);

        static SimulationPipelinePortRequirement Diagnostics() =>
            new SimulationPipelinePortRequirement(
                SimulationPipelineBindingPortRole.Diagnostics,
                Float32PipelineRuntimePortIds.Diagnostics,
                Float32PipelineRuntimePortIds.DiagnosticsSchema,
                1,
                SimulationPortDirection.Input);
    }

}
