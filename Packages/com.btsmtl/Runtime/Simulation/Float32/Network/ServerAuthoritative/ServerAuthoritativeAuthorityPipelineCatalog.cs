using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    public sealed class ServerAuthoritativeAuthorityPipelineCatalogSet
    {
        internal ServerAuthoritativeAuthorityPipelineCatalogSet(
            SimulationPipelineDescriptor descriptor,
            SimulationPipelinePassFactoryCatalog passFactories,
            Float32PipelinePassRuntimeFactoryCatalog runtimeFactories,
            Float32PipelineProductRuntimeCatalog productFactories)
        {
            RuntimePackage = new Float32SimulationPipelineRuntimePackage(
                descriptor ?? throw new ArgumentNullException(nameof(descriptor)),
                passFactories ?? throw new ArgumentNullException(nameof(passFactories)),
                runtimeFactories ?? throw new ArgumentNullException(nameof(runtimeFactories)),
                productFactories ?? throw new ArgumentNullException(nameof(productFactories)));
        }

        public Float32SimulationPipelineRuntimePackage RuntimePackage { get; }
        public SimulationPipelineDescriptor Descriptor => RuntimePackage.Pipeline;
    }

    public static class ServerAuthoritativeAuthorityPipelineCatalog
    {
        public static ServerAuthoritativeAuthorityPipelineCatalogSet Create(
            ServerAuthoritativeModelPolicy policy,
            ServerAuthoritativeReplicationPolicy replicationPolicy)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            if (replicationPolicy == null)
                throw new ArgumentNullException(nameof(replicationPolicy));

            SimulationPipelinePassDescriptor accepted =
                ServerAuthoritativePipelinePassContracts.AcceptedInputIngress(policy);
            SimulationPipelinePassDescriptor schedule =
                ServerAuthoritativePipelinePassContracts.AuthorityTickSchedule(policy);
            SimulationPipelinePassDescriptor replication =
                ServerAuthoritativePipelinePassContracts.AuthorityReplicationEgress(policy, replicationPolicy);
            var descriptor = new SimulationPipelineDescriptor(
                new SimulationPipelineId(ServerAuthoritativePipelineIdentity.AuthorityPipelineId),
                new SimulationPipelineRevision(ServerAuthoritativePipelineIdentity.Revision),
                new SimulationPipelineSchemaVersion(ServerAuthoritativePipelineIdentity.SchemaVersion),
                new[] { accepted },
                new[] { schedule },
                new[]
                {
                    StandardFloat32PipelinePassContracts.AbilityEvaluate,
                    StandardFloat32PipelinePassContracts.WorldResolveBatch,
                    StandardFloat32PipelinePassContracts.AbilityFinalize
                },
                new[] { replication });

            var runtimeFactories = new Float32PipelinePassRuntimeFactoryCatalog(
                new IFloat32PipelinePassRuntimeFactory[]
                {
                    new AuthorityAcceptedInputIngressPassRuntimeFactory(policy),
                    new AuthorityTickSchedulePassRuntimeFactory(policy),
                    new Float32AbilityEvaluatePassRuntimeFactory(),
                    new Float32WorldResolveBatchPassRuntimeFactory(),
                    new Float32AbilityFinalizePassRuntimeFactory(),
                    new AuthorityReplicationEgressPassRuntimeFactory(policy, replicationPolicy)
                });
            IReadOnlyList<IFloat32PipelineProductSlotFactory> productSlots = CreateProductSlots();
            var products = new List<SimulationPipelineProductContract>(SimulationPipelineProducts.All);
            products.AddRange(ServerAuthoritativeProducts.All);
            var factoryDescriptors = new SimulationPipelinePassFactoryDescriptor[runtimeFactories.Factories.Count];
            for (int i = 0; i < factoryDescriptors.Length; i++)
                factoryDescriptors[i] = runtimeFactories.Factories[i].Descriptor;
            var passFactories = new SimulationPipelinePassFactoryCatalog(
                Float32PassExecutionBackend.Descriptor.Identity,
                factoryDescriptors,
                products);
            return new ServerAuthoritativeAuthorityPipelineCatalogSet(
                descriptor,
                passFactories,
                runtimeFactories,
                Float32PassExecutionBackend.CreateProductRuntimeCatalog(productSlots));
        }

        public static IReadOnlyList<IFloat32PipelineProductSlotFactory> CreateProductSlots()
        {
            return new IFloat32PipelineProductSlotFactory[]
            {
                new Float32ExclusiveProductSlotFactory<OwnerCanonicalInputBatch>(ServerAuthoritativeProducts.OwnerCanonicalInputBatch, Float32PipelineProductLifetime.OuterTransaction),
                new Float32ExclusiveProductSlotFactory<AuthoritativeObservationBatch>(ServerAuthoritativeProducts.AuthoritativeObservationBatch, Float32PipelineProductLifetime.OuterTransaction),
                new Float32ExclusiveProductSlotFactory<AuthoritativeActorBaseline>(ServerAuthoritativeProducts.AuthoritativeActorBaseline, Float32PipelineProductLifetime.OuterTransaction),
                new Float32ExclusiveProductSlotFactory<PredictionCorrectionDecision>(ServerAuthoritativeProducts.PredictionCorrectionDecision, Float32PipelineProductLifetime.OuterTransaction),
                new Float32ExclusiveProductSlotFactory<AcceptedAuthorityInputBatch>(ServerAuthoritativeProducts.AcceptedAuthorityInputBatch, Float32PipelineProductLifetime.OuterTransaction),
                new Float32ExclusiveProductSlotFactory<AuthorityReplicationBatch>(ServerAuthoritativeProducts.AuthorityReplicationBatch, Float32PipelineProductLifetime.OuterTransaction),
                new Float32ExclusiveProductSlotFactory<RemotePresentationBatch>(ServerAuthoritativeProducts.RemotePresentationBatch, Float32PipelineProductLifetime.OuterTransaction),
                new Float32ExclusiveProductSlotFactory<SelectedRemoteBodyBatch>(ServerAuthoritativeProducts.SelectedRemoteBodyBatch, Float32PipelineProductLifetime.OuterTransaction)
            };
        }

        public static void ValidateRuntimePackage(
            Float32SimulationPipelineRuntimePackage runtimePackage,
            ServerAuthoritativeModelPolicy policy)
        {
            if (runtimePackage == null)
                throw new ArgumentNullException(nameof(runtimePackage));
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            SimulationPipelineDescriptor descriptor = runtimePackage.Pipeline;
            if (!CanonicalTextValue.Equals(descriptor.PipelineId.Value, ServerAuthoritativePipelineIdentity.AuthorityPipelineId, StringComparison.Ordinal) ||
                !CanonicalTextValue.Equals(descriptor.Revision.Value, ServerAuthoritativePipelineIdentity.Revision, StringComparison.Ordinal) ||
                descriptor.SchemaVersion.Value != ServerAuthoritativePipelineIdentity.SchemaVersion)
            {
                throw new InvalidOperationException("Authority Pipeline runtime package identity is not canonical.");
            }
            RequirePhase(
                descriptor,
                SimulationPipelinePhase.Ingress,
                ServerAuthoritativePipelinePassContracts.AcceptedInputIngress(policy));
            RequirePhase(
                descriptor,
                SimulationPipelinePhase.Schedule,
                ServerAuthoritativePipelinePassContracts.AuthorityTickSchedule(policy));
            RequirePhase(
                descriptor,
                SimulationPipelinePhase.Step,
                StandardFloat32PipelinePassContracts.AbilityEvaluate,
                StandardFloat32PipelinePassContracts.WorldResolveBatch,
                StandardFloat32PipelinePassContracts.AbilityFinalize);
            IReadOnlyList<SimulationPipelinePassDescriptor> egress = descriptor.GetPhase(SimulationPipelinePhase.Egress);
            if (egress.Count != 1 ||
                !egress[0].PassId.Equals(ServerAuthoritativeAuthorityPassIds.ReplicationEgress) ||
                !CanonicalTextValue.Equals(egress[0].ImplementationVersion.Value, ServerAuthoritativeAuthorityPassIds.ImplementationVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Authority Pipeline replication egress contract is not canonical.");
            }
            var products = new List<SimulationPipelineProductContract>(SimulationPipelineProducts.All);
            products.AddRange(ServerAuthoritativeProducts.All);
            products.Sort((left, right) => left.ProductId.CompareTo(right.ProductId));
            if (runtimePackage.PassFactories.Products.Count != products.Count)
                throw new InvalidOperationException("Authority Pipeline Product contract count is not canonical.");
            for (int i = 0; i < products.Count; i++)
            {
                if (!runtimePackage.PassFactories.Products[i].Equals(products[i]))
                    throw new InvalidOperationException($"Authority Pipeline Product '{products[i].ProductId}' is not canonical.");
            }
        }

        static void RequirePhase(
            SimulationPipelineDescriptor descriptor,
            SimulationPipelinePhase phase,
            params SimulationPipelinePassDescriptor[] expected)
        {
            IReadOnlyList<SimulationPipelinePassDescriptor> actual = descriptor.GetPhase(phase);
            if (actual.Count != expected.Length)
                throw new InvalidOperationException($"Authority Pipeline phase '{phase}' has another Pass count.");
            for (int i = 0; i < expected.Length; i++)
            {
                if (!actual[i].DescriptorHash.Equals(expected[i].DescriptorHash))
                    throw new InvalidOperationException($"Authority Pipeline phase '{phase}' Pass {i} is not canonical.");
            }
        }
    }
}
