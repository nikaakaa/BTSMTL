using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    public static class StandardFixedLocalPipelinePassContracts
    {
        public const string ImplementationVersion = "2";
        public const string LocalInputIngressPassId = "thirdperson.simulation.fixed-local-input-ingress";
        public const string LocalSingleStepSchedulePassId = "thirdperson.simulation.fixed-local-single-step-schedule";
        public const string LocalImmediateOutputPassId = "thirdperson.simulation.fixed-local-immediate-output";
        public const string LocalControlInputStateOwner = "fixed-local-control-input";
        public const string LocalControlInputStateSchemaId = "fixed-local-control-input-state";
        public const int LocalControlInputStateSchemaVersion = 2;

        static readonly SimulationPipelinePassDescriptor s_LocalInputIngress = Create(
            LocalInputIngressPassId,
            SimulationPipelinePhase.Ingress,
            SimulationPipelinePassStateClass.SnapshotParticipant,
            LocalControlInputStateOwner,
            new[]
            {
                Produce(SimulationPipelineProducts.CanonicalInputs),
                Produce(SimulationPipelineProducts.TypedIngress)
            },
            new[]
            {
                FixedLocalInputSourcePortContract.Requirement,
                Target(FixedPipelineRuntimePortIds.CharacterRuntime, FixedPipelineRuntimePortIds.CharacterRuntimeSchema),
                Target(FixedPipelineRuntimePortIds.CommittedObservation, FixedPipelineRuntimePortIds.CommittedObservationSchema)
            });

        static readonly SimulationPipelinePassDescriptor s_LocalSingleStepSchedule = Create(
            LocalSingleStepSchedulePassId,
            SimulationPipelinePhase.Schedule,
            SimulationPipelinePassStateClass.Stateless,
            string.Empty,
            new[]
            {
                Consume(SimulationPipelineProducts.CanonicalInputs),
                Consume(SimulationPipelineProducts.TypedIngress),
                Produce(SimulationPipelineProducts.ExecutionPlan)
            },
            new[]
            {
                Target(FixedPipelineRuntimePortIds.CharacterRuntime, FixedPipelineRuntimePortIds.CharacterRuntimeSchema)
            });

        static readonly SimulationPipelinePassDescriptor s_LocalImmediateOutput = Create(
            LocalImmediateOutputPassId,
            SimulationPipelinePhase.Egress,
            SimulationPipelinePassStateClass.Stateless,
            string.Empty,
            new[]
            {
                Consume(SimulationPipelineProducts.FinalizedStepResult),
                Produce(SimulationPipelineProducts.OutputDispositionSet)
            },
            new[]
            {
                Target(FixedPipelineRuntimePortIds.CharacterRuntime, FixedPipelineRuntimePortIds.CharacterRuntimeSchema)
            });

        public static SimulationPipelinePassDescriptor LocalInputIngress => s_LocalInputIngress;
        public static SimulationPipelinePassDescriptor LocalSingleStepSchedule => s_LocalSingleStepSchedule;
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
                    StableHash.Compute("fixed-local-pass-binding/1", descriptor.DescriptorHash.ToString())),
                descriptor.Phase,
                FixedPassExecutionBackend.BackendId,
                FixedPassExecutionBackend.SemanticVersion,
                descriptor.ConfigurationHash,
                descriptor.ExecutionSupport,
                true,
                stateful,
                stateful,
                false,
                stateful ? LocalControlInputStateSchemaId : string.Empty,
                stateful ? LocalControlInputStateSchemaVersion : 0);
        }

        static SimulationPipelinePassDescriptor Create(
            string passId,
            SimulationPipelinePhase phase,
            SimulationPipelinePassStateClass stateClass,
            string stateOwner,
            IEnumerable<SimulationPipelineProductAccess> products,
            IEnumerable<SimulationPipelinePortRequirement> ports)
        {
            return new SimulationPipelinePassDescriptor(
                new SimulationPipelinePassId(passId),
                new SimulationPipelinePassImplementationVersion(ImplementationVersion),
                phase,
                StableHash.Compute("standard-fixed-local-pass-config/1", passId),
                FixedSimulationNumericProfile.Value.Id,
                FixedSimulationNumericProfile.Value.AbiVersion,
                FixedPassExecutionBackend.BackendId,
                FixedPassExecutionBackend.SemanticVersion,
                WorldCapability.None,
                SimulationPipelineExecutionSupport.Forward,
                stateClass,
                stateOwner,
                products,
                ports);
        }

        static SimulationPipelineProductAccess Produce(SimulationPipelineProductContract product) =>
            new SimulationPipelineProductAccess(product, SimulationPipelineProductAccessKind.ExclusiveProducer);

        static SimulationPipelineProductAccess Consume(SimulationPipelineProductContract product) =>
            new SimulationPipelineProductAccess(product, SimulationPipelineProductAccessKind.ReadOnlyConsumer);

        static SimulationPipelinePortRequirement Target(string portId, string schemaId) =>
            new SimulationPipelinePortRequirement(
                SimulationPipelineBindingPortRole.Target,
                portId,
                schemaId,
                1,
                SimulationPortDirection.Input);
    }
}
