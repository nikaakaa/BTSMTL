using System;
using System.Collections.Generic;
using System.Globalization;

namespace ThirdPersonSimulation
{
    public static class SimulationPipelineCompiler
    {
        sealed class AccessSite
        {
            public AccessSite(SimulationPipelinePassDescriptor pass, SimulationPipelineProductAccess access, int index)
            {
                Pass = pass;
                Access = access;
                Index = index;
            }

            public SimulationPipelinePassDescriptor Pass { get; }
            public SimulationPipelineProductAccess Access { get; }
            public int Index { get; }
        }

        sealed class ProductUsage
        {
            public ProductUsage(SimulationPipelineProductContract product) { Product = product; }
            public SimulationPipelineProductContract Product { get; }
            public List<AccessSite> Producers { get; } = new List<AccessSite>();
            public List<AccessSite> Consumers { get; } = new List<AccessSite>();
        }

        public static SimulationPipelineCompilationResult Compile(
            SimulationPipelineDescriptor descriptor,
            SimulationPipelinePassFactoryCatalog catalog,
            SimulationExecutionTargetManifest executionTarget,
            WorldCapability executionTargetRequiredWorldCapabilities,
            SimulationPipelineExecutionSupport executionTargetExecutionSupport,
            bool executionTargetDeterministic,
            SimulationExecutionBackendDescriptor backend,
            SimulationSessionSourceDescriptor source,
            IEnumerable<SimulationPortDescriptor> sourcePorts,
            SimulationWorldSolverDefinitionDescriptor solver,
            SimulationComponentIdentity snapshotCodec,
            SimulationPipelineExecutionSupport snapshotExecutionSupport,
            bool snapshotDeterministic)
        {
            if (executionTarget == null)
                throw new ArgumentNullException(nameof(executionTarget));
            if (backend == null)
                throw new ArgumentNullException(nameof(backend));
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (solver == null)
                throw new ArgumentNullException(nameof(solver));
            SimulationExecutionBackendTargetSupport backendTarget = backend.RequireTarget(
                executionTarget.NumericProfile.Id,
                executionTarget.NumericProfile.AbiVersion,
                descriptor?.SchemaVersion ?? throw new ArgumentNullException(nameof(descriptor)));
            var context = new SimulationPipelineCompilationContext(
                executionTarget,
                executionTargetRequiredWorldCapabilities,
                executionTargetExecutionSupport,
                executionTargetDeterministic,
                backend.Identity,
                backendTarget.ExecutionSupport,
                backendTarget.Deterministic,
                source.Identity,
                sourcePorts,
                source.RequiredSolverCapabilities,
                source.RequiredPipelinePasses,
                source.RequiredPipelineSourcePorts,
                solver.Identity,
                solver.Capabilities,
                solver.ExecutionSupport,
                solver.Deterministic,
                snapshotCodec,
                snapshotExecutionSupport,
                snapshotDeterministic,
                source.ExecutionSupport,
                source.Deterministic);
            return Compile(descriptor, catalog, context);
        }

        public static SimulationPipelineCompilationResult Compile(
            SimulationPipelineDescriptor descriptor,
            SimulationPipelinePassFactoryCatalog catalog,
            SimulationPipelineCompilationContext context)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var errors = new List<SimulationPipelineCompileError>();
            ValidateGlobalCapabilities(catalog, context, errors);
            var compiledPasses = new List<CompiledSimulationPipelinePass>();
            var usages = new Dictionary<SimulationPipelineProductId, ProductUsage>();
            var phaseIndexes = new int[5];
            WorldCapability requiredSolverCapabilities =
                context.ExecutionTargetRequiredWorldCapabilities | context.SourceRequiredWorldCapabilities;

            ValidateSourceRequirements(descriptor, context, errors);

            for (int index = 0; index < descriptor.Passes.Count; index++)
            {
                SimulationPipelinePassDescriptor pass = descriptor.Passes[index];
                int phaseIndex = phaseIndexes[(int)pass.Phase]++;
                ValidatePassTarget(pass, context, errors);
                ValidateSourcePorts(pass, context, errors);
                SimulationPipelinePassFactoryDescriptor factory = ResolveFactory(pass, catalog, context, errors);
                if (factory != null)
                {
                    ValidateFactoryCapabilities(pass, factory, context, errors);
                    ValidateStateOwnership(pass, factory, errors);
                    compiledPasses.Add(new CompiledSimulationPipelinePass(pass, factory, index, phaseIndex));
                }
                requiredSolverCapabilities |= pass.RequiredSolverCapabilities;
                CollectProductUsage(pass, index, catalog, usages, errors);
            }

            if ((context.SolverCapabilities & requiredSolverCapabilities) != requiredSolverCapabilities)
            {
                errors.Add(new SimulationPipelineCompileError(
                    SimulationPipelineCompileErrorCode.SolverCapabilityMismatch,
                    $"Solver capabilities '{context.SolverCapabilities}' do not cover required '{requiredSolverCapabilities}'.",
                    componentIdentity: context.WorldSolver.ToString()));
            }

            ValidateProducts(descriptor.Passes.Count, usages, errors);
            if (errors.Count != 0)
                return new SimulationPipelineCompilationResult(null, errors);

            var products = new List<SimulationPipelineProductContract>();
            foreach (ProductUsage usage in usages.Values)
                products.Add(usage.Product);
            products.Sort((left, right) => left.ProductId.CompareTo(right.ProductId));
            SimulationPipelineHash pipelineHash = ComputePipelineHash(descriptor, catalog.Backend, compiledPasses, products, context);
            var identity = new SimulationPipelineIdentity(descriptor.PipelineId, descriptor.Revision, descriptor.SchemaVersion, pipelineHash);
            StableHash planHash = ComputePlanHash(identity, catalog.Backend, compiledPasses);
            var plan = new CompiledSimulationPipelinePlan(
                descriptor,
                identity,
                catalog.Backend,
                context.RequiredExecutionSupport,
                context.RequiresDeterministic,
                planHash,
                compiledPasses,
                products);
            return new SimulationPipelineCompilationResult(plan, null);
        }

        static void ValidateGlobalCapabilities(
            SimulationPipelinePassFactoryCatalog catalog,
            SimulationPipelineCompilationContext context,
            List<SimulationPipelineCompileError> errors)
        {
            if (!catalog.Backend.Equals(context.Backend))
            {
                errors.Add(new SimulationPipelineCompileError(
                    SimulationPipelineCompileErrorCode.BackendCatalogMismatch,
                    $"Factory catalog Backend '{catalog.Backend}' does not match selected Backend '{context.Backend}'.",
                    componentIdentity: catalog.Backend.ToString()));
            }
            RequireExecutionSupport("Execution Target", context.ExecutionTargetExecutionSupport, context.RequiredExecutionSupport, context.ExecutionTarget.Identity.ToString(), errors);
            RequireExecutionSupport("Execution Backend", context.BackendExecutionSupport, context.RequiredExecutionSupport, context.Backend.ToString(), errors);
            RequireExecutionSupport("World Solver", context.SolverExecutionSupport, context.RequiredExecutionSupport, context.WorldSolver.ToString(), errors);
            RequireExecutionSupport("Snapshot Codec", context.SnapshotExecutionSupport, context.RequiredExecutionSupport, context.SnapshotCodec.ToString(), errors);
            if (context.RequiresDeterministic &&
                (!context.ExecutionTargetDeterministic || !context.BackendDeterministic || !context.SolverDeterministic || !context.SnapshotDeterministic))
            {
                errors.Add(new SimulationPipelineCompileError(
                    SimulationPipelineCompileErrorCode.DeterministicSupportMismatch,
                    "Deterministic Pipeline requirement is not supported by Execution Target, Backend, Solver and Snapshot Codec together."));
            }
        }

        static void RequireExecutionSupport(
            string component,
            SimulationPipelineExecutionSupport available,
            SimulationPipelineExecutionSupport required,
            string identity,
            List<SimulationPipelineCompileError> errors)
        {
            if ((available & required) != required)
            {
                errors.Add(new SimulationPipelineCompileError(
                    SimulationPipelineCompileErrorCode.ExecutionSupportMismatch,
                    $"{component} supports '{available}', required '{required}'.",
                    componentIdentity: identity));
            }
        }

        static void ValidatePassTarget(
            SimulationPipelinePassDescriptor pass,
            SimulationPipelineCompilationContext context,
            List<SimulationPipelineCompileError> errors)
        {
            if (!pass.NumericProfileId.Equals(context.NumericProfileId))
            {
                errors.Add(new SimulationPipelineCompileError(
                    SimulationPipelineCompileErrorCode.NumericProfileMismatch,
                    $"Pass '{pass.PassId}' NumericProfile '{pass.NumericProfileId}' does not match '{context.NumericProfileId}'.",
                    pass.PassId));
            }
            if (!pass.TargetAbiVersion.Equals(context.TargetAbiVersion))
            {
                errors.Add(new SimulationPipelineCompileError(
                    SimulationPipelineCompileErrorCode.TargetAbiMismatch,
                    $"Pass '{pass.PassId}' Target ABI '{pass.TargetAbiVersion}' does not match '{context.TargetAbiVersion}'.",
                    pass.PassId));
            }
            if (!string.Equals(pass.BackendId, context.Backend.ComponentId, StringComparison.Ordinal) ||
                !string.Equals(pass.BackendSemanticVersion, context.Backend.SemanticVersion, StringComparison.Ordinal))
            {
                errors.Add(new SimulationPipelineCompileError(
                    SimulationPipelineCompileErrorCode.BackendMismatch,
                    $"Pass '{pass.PassId}' Backend '{pass.BackendId}@{pass.BackendSemanticVersion}' does not match '{context.Backend.ComponentId}@{context.Backend.SemanticVersion}'.",
                    pass.PassId,
                    componentIdentity: context.Backend.ToString()));
            }
            if ((pass.ExecutionSupport & context.RequiredExecutionSupport) != context.RequiredExecutionSupport)
            {
                errors.Add(new SimulationPipelineCompileError(
                    SimulationPipelineCompileErrorCode.ExecutionSupportMismatch,
                    $"Pass '{pass.PassId}' supports '{pass.ExecutionSupport}', required '{context.RequiredExecutionSupport}'.",
                    pass.PassId));
            }
        }

        static void ValidateSourcePorts(
            SimulationPipelinePassDescriptor pass,
            SimulationPipelineCompilationContext context,
            List<SimulationPipelineCompileError> errors)
        {
            IReadOnlyList<SimulationPipelinePortRequirement> requirements =
                pass.GetPortRequirements(SimulationPipelineBindingPortRole.Source);
            for (int i = 0; i < requirements.Count; i++)
            {
                SimulationPipelinePortRequirement requirement = requirements[i];
                SimulationPortDescriptor? found = null;
                for (int port = 0; port < context.SourcePorts.Count; port++)
                {
                    if (string.Equals(context.SourcePorts[port].PortId, requirement.PortId, StringComparison.Ordinal))
                    {
                        found = context.SourcePorts[port];
                        break;
                    }
                }
                if (!found.HasValue)
                {
                    errors.Add(new SimulationPipelineCompileError(
                        SimulationPipelineCompileErrorCode.MissingSourcePort,
                        $"Pass '{pass.PassId}' requires missing Source port '{requirement.PortId}'.",
                        pass.PassId,
                        componentIdentity: context.SessionSource.ToString()));
                    continue;
                }
                SimulationPortDescriptor actual = found.Value;
                if (!string.Equals(actual.SchemaId, requirement.SchemaId, StringComparison.Ordinal) ||
                    actual.SchemaVersion != requirement.SchemaVersion || actual.Direction != requirement.Direction ||
                    !string.Equals(actual.OwnerComponentId, context.SessionSource.ComponentId, StringComparison.Ordinal))
                {
                    errors.Add(new SimulationPipelineCompileError(
                        SimulationPipelineCompileErrorCode.SourcePortMismatch,
                        $"Source port '{requirement.PortId}' does not match Pass '{pass.PassId}' schema, direction or owner requirement.",
                        pass.PassId,
                        componentIdentity: context.SessionSource.ToString()));
                }
            }
        }

        static void ValidateSourceRequirements(
            SimulationPipelineDescriptor descriptor,
            SimulationPipelineCompilationContext context,
            List<SimulationPipelineCompileError> errors)
        {
            for (int requiredIndex = 0; requiredIndex < context.SourceRequiredPasses.Count; requiredIndex++)
            {
                SimulationPipelinePassRequirement requirement = context.SourceRequiredPasses[requiredIndex];
                bool found = false;
                for (int passIndex = 0; passIndex < descriptor.Passes.Count; passIndex++)
                {
                    SimulationPipelinePassDescriptor pass = descriptor.Passes[passIndex];
                    if (pass.PassId.Equals(requirement.PassId) &&
                        pass.ImplementationVersion.Equals(requirement.ImplementationVersion) &&
                        pass.Phase == requirement.Phase)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    errors.Add(new SimulationPipelineCompileError(
                        SimulationPipelineCompileErrorCode.MissingSourceRequiredPass,
                        $"Session Source requires missing Pipeline Pass '{requirement}'.",
                        requirement.PassId,
                        componentIdentity: context.SessionSource.ToString()));
                }
            }

            for (int requiredIndex = 0; requiredIndex < context.SourceRequiredPorts.Count; requiredIndex++)
            {
                SimulationPipelinePortRequirement requirement = context.SourceRequiredPorts[requiredIndex];
                SimulationPortDescriptor? actual = null;
                for (int portIndex = 0; portIndex < context.SourcePorts.Count; portIndex++)
                {
                    if (string.Equals(context.SourcePorts[portIndex].PortId, requirement.PortId, StringComparison.Ordinal))
                    {
                        actual = context.SourcePorts[portIndex];
                        break;
                    }
                }
                if (!actual.HasValue)
                {
                    errors.Add(new SimulationPipelineCompileError(
                        SimulationPipelineCompileErrorCode.MissingSourcePort,
                        $"Session Source declares missing runtime port '{requirement.PortId}'.",
                        componentIdentity: context.SessionSource.ToString()));
                    continue;
                }
                if (!Matches(actual.Value, requirement) ||
                    !string.Equals(actual.Value.OwnerComponentId, context.SessionSource.ComponentId, StringComparison.Ordinal))
                {
                    errors.Add(new SimulationPipelineCompileError(
                        SimulationPipelineCompileErrorCode.SourcePortMismatch,
                        $"Session Source runtime port '{requirement.PortId}' does not match its declared requirement.",
                        componentIdentity: context.SessionSource.ToString()));
                    continue;
                }

                bool consumed = false;
                for (int passIndex = 0; passIndex < descriptor.Passes.Count && !consumed; passIndex++)
                {
                    IReadOnlyList<SimulationPipelinePortRequirement> passRequirements =
                        descriptor.Passes[passIndex].GetPortRequirements(SimulationPipelineBindingPortRole.Source);
                    for (int portIndex = 0; portIndex < passRequirements.Count; portIndex++)
                    {
                        if (Matches(passRequirements[portIndex], requirement))
                        {
                            consumed = true;
                            break;
                        }
                    }
                }
                if (!consumed)
                {
                    errors.Add(new SimulationPipelineCompileError(
                        SimulationPipelineCompileErrorCode.UnconsumedSourceRequiredPort,
                        $"Session Source requires Pipeline consumption of port '{requirement.PortId}', but no Pass declares it.",
                        componentIdentity: context.SessionSource.ToString()));
                }
            }
        }

        static bool Matches(SimulationPortDescriptor actual, SimulationPipelinePortRequirement requirement)
        {
            return string.Equals(actual.PortId, requirement.PortId, StringComparison.Ordinal) &&
                   string.Equals(actual.SchemaId, requirement.SchemaId, StringComparison.Ordinal) &&
                   actual.SchemaVersion == requirement.SchemaVersion &&
                   actual.Direction == requirement.Direction;
        }

        static bool Matches(
            SimulationPipelinePortRequirement left,
            SimulationPipelinePortRequirement right)
        {
            return left.Role == right.Role &&
                   string.Equals(left.PortId, right.PortId, StringComparison.Ordinal) &&
                   string.Equals(left.SchemaId, right.SchemaId, StringComparison.Ordinal) &&
                   left.SchemaVersion == right.SchemaVersion &&
                   left.Direction == right.Direction;
        }

        static SimulationPipelinePassFactoryDescriptor ResolveFactory(
            SimulationPipelinePassDescriptor pass,
            SimulationPipelinePassFactoryCatalog catalog,
            SimulationPipelineCompilationContext context,
            List<SimulationPipelineCompileError> errors)
        {
            IReadOnlyList<SimulationPipelinePassFactoryDescriptor> candidates = catalog.FindFactories(pass.PassId);
            if (candidates.Count == 0)
            {
                errors.Add(new SimulationPipelineCompileError(
                    SimulationPipelineCompileErrorCode.UnknownPassFactory,
                    $"No installed factory exists for Pass '{pass.PassId}'.",
                    pass.PassId,
                    componentIdentity: context.Backend.ToString()));
                return null;
            }
            SimulationPipelinePassFactoryDescriptor factory = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Identity.ImplementationVersion.Equals(pass.ImplementationVersion))
                {
                    factory = candidates[i];
                    break;
                }
            }
            if (factory == null)
            {
                errors.Add(new SimulationPipelineCompileError(
                    SimulationPipelineCompileErrorCode.UnsupportedPassVersion,
                    $"Installed factory versions do not include '{pass.PassId}@{pass.ImplementationVersion}'.",
                    pass.PassId,
                    componentIdentity: context.Backend.ToString()));
                return null;
            }
            if (factory.Phase != pass.Phase)
            {
                errors.Add(new SimulationPipelineCompileError(
                    SimulationPipelineCompileErrorCode.FactoryPhaseMismatch,
                    $"Factory for Pass '{pass.PassId}' declares phase '{factory.Phase}', descriptor uses '{pass.Phase}'.",
                    pass.PassId));
                return null;
            }
            if (!factory.SupportedConfigurationHash.Equals(pass.ConfigurationHash))
            {
                errors.Add(new SimulationPipelineCompileError(
                    SimulationPipelineCompileErrorCode.PassConfigurationMismatch,
                    $"Factory for Pass '{pass.PassId}' does not support configuration '{pass.ConfigurationHash}'.",
                    pass.PassId));
                return null;
            }
            return factory;
        }

        static void ValidateFactoryCapabilities(
            SimulationPipelinePassDescriptor pass,
            SimulationPipelinePassFactoryDescriptor factory,
            SimulationPipelineCompilationContext context,
            List<SimulationPipelineCompileError> errors)
        {
            if (!string.Equals(factory.BackendId, context.Backend.ComponentId, StringComparison.Ordinal) ||
                !string.Equals(factory.BackendSemanticVersion, context.Backend.SemanticVersion, StringComparison.Ordinal) ||
                (factory.ExecutionSupport & pass.ExecutionSupport) != pass.ExecutionSupport ||
                (factory.ExecutionSupport & context.RequiredExecutionSupport) != context.RequiredExecutionSupport)
            {
                errors.Add(new SimulationPipelineCompileError(
                    SimulationPipelineCompileErrorCode.FactoryCapabilityMismatch,
                    $"Factory capability for Pass '{pass.PassId}' does not match Backend or execution requirements.",
                    pass.PassId,
                    componentIdentity: context.Backend.ToString()));
            }
            if (context.RequiresDeterministic && !factory.Deterministic)
            {
                errors.Add(new SimulationPipelineCompileError(
                    SimulationPipelineCompileErrorCode.DeterministicSupportMismatch,
                    $"Factory for Pass '{pass.PassId}' is not deterministic.",
                    pass.PassId));
            }
        }

        static void ValidateStateOwnership(
            SimulationPipelinePassDescriptor pass,
            SimulationPipelinePassFactoryDescriptor factory,
            List<SimulationPipelineCompileError> errors)
        {
            bool valid;
            switch (pass.StateClass)
            {
                case SimulationPipelinePassStateClass.Stateless:
                    valid = !factory.SupportsSnapshotCapture && !factory.SupportsSnapshotRestore && !factory.SupportsReconstruction;
                    break;
                case SimulationPipelinePassStateClass.Reconstructible:
                    valid = factory.SupportsReconstruction && !factory.SupportsSnapshotCapture && !factory.SupportsSnapshotRestore;
                    break;
                case SimulationPipelinePassStateClass.SnapshotParticipant:
                    valid = factory.SupportsSnapshotCapture && factory.SupportsSnapshotRestore && !factory.SupportsReconstruction;
                    break;
                case SimulationPipelinePassStateClass.ExternalSource:
                    valid = (pass.Phase == SimulationPipelinePhase.Ingress || pass.Phase == SimulationPipelinePhase.Egress) &&
                            !factory.SupportsSnapshotCapture && !factory.SupportsSnapshotRestore && !factory.SupportsReconstruction;
                    break;
                default:
                    valid = false;
                    break;
            }
            if (!valid)
            {
                errors.Add(new SimulationPipelineCompileError(
                    SimulationPipelineCompileErrorCode.InvalidPassStateOwnership,
                    $"Pass '{pass.PassId}' state class '{pass.StateClass}' does not match factory capture, restore or reconstruct ownership.",
                    pass.PassId));
            }
        }

        static void CollectProductUsage(
            SimulationPipelinePassDescriptor pass,
            int passIndex,
            SimulationPipelinePassFactoryCatalog catalog,
            Dictionary<SimulationPipelineProductId, ProductUsage> usages,
            List<SimulationPipelineCompileError> errors)
        {
            for (int i = 0; i < pass.ProductAccesses.Count; i++)
            {
                SimulationPipelineProductAccess access = pass.ProductAccesses[i];
                if (!catalog.TryGetProduct(access.Product.ProductId, out SimulationPipelineProductContract installed))
                {
                    errors.Add(new SimulationPipelineCompileError(
                        SimulationPipelineCompileErrorCode.UnknownProduct,
                        $"Pass '{pass.PassId}' references unknown Product '{access.Product.ProductId}'.",
                        pass.PassId,
                        access.Product.ProductId));
                    continue;
                }
                if (!installed.Equals(access.Product))
                {
                    errors.Add(new SimulationPipelineCompileError(
                        SimulationPipelineCompileErrorCode.ProductContractMismatch,
                        $"Pass '{pass.PassId}' Product '{access.Product.ProductId}' schema or contract does not match the installed version.",
                        pass.PassId,
                        access.Product.ProductId));
                    continue;
                }
                bool producer = access.IsProducer;
                SimulationPipelinePhaseMask allowed = producer ? installed.ProducerPhases : installed.ConsumerPhases;
                if ((allowed & ToMask(pass.Phase)) == 0)
                {
                    errors.Add(new SimulationPipelineCompileError(
                        SimulationPipelineCompileErrorCode.IllegalProductPhase,
                        $"Pass '{pass.PassId}' cannot {(producer ? "produce" : "consume")} Product '{installed.ProductId}' in phase '{pass.Phase}'.",
                        pass.PassId,
                        installed.ProductId));
                }
                if (!usages.TryGetValue(installed.ProductId, out ProductUsage usage))
                {
                    usage = new ProductUsage(installed);
                    usages.Add(installed.ProductId, usage);
                }
                var site = new AccessSite(pass, access, passIndex);
                if (producer)
                    usage.Producers.Add(site);
                else
                    usage.Consumers.Add(site);
            }
        }

        static void ValidateProducts(
            int passCount,
            Dictionary<SimulationPipelineProductId, ProductUsage> usages,
            List<SimulationPipelineCompileError> errors)
        {
            int executionPlanProducers = 0;
            var graph = new List<int>[passCount];
            for (int i = 0; i < graph.Length; i++)
                graph[i] = new List<int>();

            foreach (ProductUsage usage in usages.Values)
            {
                if (usage.Product.ProductId.Equals(SimulationPipelineProducts.ExecutionPlan.ProductId))
                    executionPlanProducers = usage.Producers.Count;
                if (usage.Product.Multiplicity == SimulationPipelineProductMultiplicity.Exclusive)
                {
                    if (usage.Producers.Count == 0)
                    {
                        errors.Add(new SimulationPipelineCompileError(
                            SimulationPipelineCompileErrorCode.MissingExclusiveProducer,
                            $"Exclusive Product '{usage.Product.ProductId}' has no producer.",
                            productId: usage.Product.ProductId));
                    }
                    else if (usage.Producers.Count != 1)
                    {
                        errors.Add(new SimulationPipelineCompileError(
                            SimulationPipelineCompileErrorCode.DuplicateExclusiveProducer,
                            $"Exclusive Product '{usage.Product.ProductId}' has {usage.Producers.Count} producers.",
                            productId: usage.Product.ProductId));
                    }
                }
                else
                {
                    if (usage.Product.ProvenanceFields != SimulationPipelineProvenanceFields.All ||
                        !Enum.IsDefined(typeof(SimulationPipelineAppendOrdering), usage.Product.AppendOrdering))
                    {
                        errors.Add(new SimulationPipelineCompileError(
                            SimulationPipelineCompileErrorCode.InvalidAppendOrdering,
                            $"Append-only Product '{usage.Product.ProductId}' has no complete stable provenance ordering.",
                            productId: usage.Product.ProductId));
                    }
                    for (int i = 1; i < usage.Producers.Count; i++)
                    {
                        if (usage.Producers[i - 1].Index >= usage.Producers[i].Index)
                        {
                            errors.Add(new SimulationPipelineCompileError(
                                SimulationPipelineCompileErrorCode.InvalidAppendOrdering,
                                $"Append-only Product '{usage.Product.ProductId}' producer order is unstable.",
                                productId: usage.Product.ProductId));
                            break;
                        }
                    }
                }

                if (usage.Product.Consumption == SimulationPipelineProductConsumption.InternalRequired &&
                    usage.Producers.Count != 0 && usage.Consumers.Count == 0)
                {
                    errors.Add(new SimulationPipelineCompileError(
                        SimulationPipelineCompileErrorCode.UnconsumedRequiredProduct,
                        $"Required Product '{usage.Product.ProductId}' is produced but never consumed.",
                        productId: usage.Product.ProductId));
                }

                for (int consumerIndex = 0; consumerIndex < usage.Consumers.Count; consumerIndex++)
                {
                    AccessSite consumer = usage.Consumers[consumerIndex];
                    if (consumer.Access.Required && usage.Producers.Count == 0)
                    {
                        errors.Add(new SimulationPipelineCompileError(
                            SimulationPipelineCompileErrorCode.ProductUseBeforeProduce,
                            $"Pass '{consumer.Pass.PassId}' requires Product '{usage.Product.ProductId}' with no producer.",
                            consumer.Pass.PassId,
                            usage.Product.ProductId));
                    }
                    for (int producerIndex = 0; producerIndex < usage.Producers.Count; producerIndex++)
                    {
                        AccessSite producer = usage.Producers[producerIndex];
                        if (consumer.Access.Required && producer.Index >= consumer.Index)
                        {
                            errors.Add(new SimulationPipelineCompileError(
                                SimulationPipelineCompileErrorCode.ProductUseBeforeProduce,
                                $"Pass '{consumer.Pass.PassId}' consumes Product '{usage.Product.ProductId}' before producer '{producer.Pass.PassId}'.",
                                consumer.Pass.PassId,
                                usage.Product.ProductId));
                        }
                        AddEdge(graph, producer.Index, consumer.Index);
                    }
                }
            }

            if (executionPlanProducers == 0)
            {
                errors.Add(new SimulationPipelineCompileError(
                    SimulationPipelineCompileErrorCode.MissingExecutionPlanProducer,
                    "Schedule phase has no ExecutionPlan producer.",
                    productId: SimulationPipelineProducts.ExecutionPlan.ProductId));
            }
            else if (executionPlanProducers != 1)
            {
                errors.Add(new SimulationPipelineCompileError(
                    SimulationPipelineCompileErrorCode.DuplicateExecutionPlanProducer,
                    $"Schedule phase has {executionPlanProducers} ExecutionPlan producers.",
                    productId: SimulationPipelineProducts.ExecutionPlan.ProductId));
            }

            if (HasCycle(graph))
            {
                errors.Add(new SimulationPipelineCompileError(
                    SimulationPipelineCompileErrorCode.ProductDependencyCycle,
                    "Pipeline Product dependencies contain a cycle."));
            }
        }

        static void AddEdge(List<int>[] graph, int source, int target)
        {
            if (source < 0 || source >= graph.Length || target < 0 || target >= graph.Length)
                return;
            List<int> edges = graph[source];
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i] == target)
                    return;
            }
            edges.Add(target);
        }

        static bool HasCycle(List<int>[] graph)
        {
            var state = new byte[graph.Length];
            for (int i = 0; i < graph.Length; i++)
            {
                if (state[i] == 0 && Visit(i, graph, state))
                    return true;
            }
            return false;
        }

        static bool Visit(int node, List<int>[] graph, byte[] state)
        {
            state[node] = 1;
            for (int i = 0; i < graph[node].Count; i++)
            {
                int target = graph[node][i];
                if (state[target] == 1 || state[target] == 0 && Visit(target, graph, state))
                    return true;
            }
            state[node] = 2;
            return false;
        }

        static SimulationPipelineHash ComputePipelineHash(
            SimulationPipelineDescriptor descriptor,
            SimulationComponentIdentity backend,
            IReadOnlyList<CompiledSimulationPipelinePass> passes,
            IReadOnlyList<SimulationPipelineProductContract> products,
            SimulationPipelineCompilationContext context)
        {
            var values = new List<string>
            {
                "simulation-pipeline-hash/1",
                descriptor.PipelineId.Value,
                descriptor.Revision.Value,
                descriptor.SchemaVersion.ToString(),
                descriptor.DescriptorHash.ToString(),
                backend.ComponentId,
                backend.SemanticVersion,
                backend.ConfigurationHash.ToString(),
                context.ExecutionTarget.Identity.ToString(),
                context.NumericProfileId.Value,
                context.TargetAbiVersion.ToString(),
                ((int)context.RequiredExecutionSupport).ToString(CultureInfo.InvariantCulture),
                context.RequiresDeterministic ? "1" : "0"
            };
            for (int i = 0; i < passes.Count; i++)
            {
                CompiledSimulationPipelinePass pass = passes[i];
                values.Add($"pass:{i}:{pass.Descriptor.DescriptorHash}:{pass.Factory.Identity.FactoryVersion}:{pass.Factory.Identity.BindingSchemaHash}:{pass.Factory.SupportedConfigurationHash}:{pass.Factory.StateSchemaId}:{pass.Factory.StateSchemaVersion}");
            }
            for (int i = 0; i < products.Count; i++)
            {
                SimulationPipelineProductContract product = products[i];
                values.Add($"product:{product.VersionedIdentity}:{product.Owner}:{(int)product.Multiplicity}:{product.CanonicalIdentity}:{product.DiagnosticsShape}:{(int)product.ProducerPhases}:{(int)product.ConsumerPhases}:{(int)product.Consumption}:{(int)product.ProvenanceFields}:{(int)product.AppendOrdering}");
            }
            return new SimulationPipelineHash(StableHash.Compute(values.ToArray()));
        }

        static StableHash ComputePlanHash(
            SimulationPipelineIdentity identity,
            SimulationComponentIdentity backend,
            IReadOnlyList<CompiledSimulationPipelinePass> passes)
        {
            var values = new string[passes.Count + 4];
            values[0] = "compiled-simulation-pipeline-plan/1";
            values[1] = identity.ToString();
            values[2] = backend.ToString();
            values[3] = passes.Count.ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < passes.Count; i++)
                values[i + 4] = $"{i}:{passes[i].Descriptor.VersionedIdentity}:{passes[i].Factory.Identity.FactoryVersion}:{passes[i].Factory.Identity.BindingSchemaHash}:{passes[i].Factory.StateSchemaId}:{passes[i].Factory.StateSchemaVersion}";
            return StableHash.Compute(values);
        }

        static SimulationPipelinePhaseMask ToMask(SimulationPipelinePhase phase)
        {
            switch (phase)
            {
                case SimulationPipelinePhase.Ingress: return SimulationPipelinePhaseMask.Ingress;
                case SimulationPipelinePhase.Schedule: return SimulationPipelinePhaseMask.Schedule;
                case SimulationPipelinePhase.Step: return SimulationPipelinePhaseMask.Step;
                case SimulationPipelinePhase.Egress: return SimulationPipelinePhaseMask.Egress;
                default: throw new ArgumentOutOfRangeException(nameof(phase));
            }
        }
    }
}
