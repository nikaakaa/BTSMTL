using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    public static class StandardFPLocalPipelinePassContracts
    {
        public const string ImplementationVersion = "2";
        public const string LocalInputIngressPassId = "thirdperson.simulation.fp-local-input-ingress";
        public const string LocalSingleStepSchedulePassId = "thirdperson.simulation.fp-local-single-step-schedule";
        public const string LocalImmediateOutputPassId = "thirdperson.simulation.fp-local-immediate-output";
        public const string LocalControlInputStateOwner = "fp-local-control-input";
        public const string LocalControlInputStateSchemaId = "fp-local-control-input-state";
        public const int LocalControlInputStateSchemaVersion = 3;

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
                FPLocalInputSourcePortContract.Requirement,
                Target(FPPipelineRuntimePortIds.CharacterRuntime, FPPipelineRuntimePortIds.CharacterRuntimeSchema),
                Target(FPPipelineRuntimePortIds.CommittedObservation, FPPipelineRuntimePortIds.CommittedObservationSchema)
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
                Target(FPPipelineRuntimePortIds.CharacterRuntime, FPPipelineRuntimePortIds.CharacterRuntimeSchema)
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
                Target(FPPipelineRuntimePortIds.CharacterRuntime, FPPipelineRuntimePortIds.CharacterRuntimeSchema)
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
                    StableHash.Compute("fp-local-pass-binding/1", descriptor.DescriptorHash.ToString())),
                descriptor.Phase,
                FPPassExecutionBackend.BackendId,
                FPPassExecutionBackend.SemanticVersion,
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
                StableHash.Compute("standard-fp-local-pass-config/1", passId),
                QuantumSimulationNumericProfile.Value.Id,
                QuantumSimulationNumericProfile.Value.AbiVersion,
                FPPassExecutionBackend.BackendId,
                FPPassExecutionBackend.SemanticVersion,
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
