using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;

namespace ThirdPersonSimulation
{
    public sealed class Float32SimulationPipelineRuntimePackage
    {
        public Float32SimulationPipelineRuntimePackage(
            SimulationPipelineDescriptor pipeline,
            SimulationPipelinePassFactoryCatalog passFactories,
            Float32PipelinePassRuntimeFactoryCatalog passRuntimeFactories,
            Float32PipelineProductRuntimeCatalog productRuntimeFactories)
        {
            Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            PassFactories = passFactories ?? throw new ArgumentNullException(nameof(passFactories));
            PassRuntimeFactories = passRuntimeFactories ?? throw new ArgumentNullException(nameof(passRuntimeFactories));
            ProductRuntimeFactories = productRuntimeFactories ?? throw new ArgumentNullException(nameof(productRuntimeFactories));
            Validate();
            PackageIdentity = BuildIdentity();
        }

        public SimulationPipelineDescriptor Pipeline { get; }
        public SimulationPipelinePassFactoryCatalog PassFactories { get; }
        public Float32PipelinePassRuntimeFactoryCatalog PassRuntimeFactories { get; }
        public Float32PipelineProductRuntimeCatalog ProductRuntimeFactories { get; }
        public StableHash PackageIdentity { get; }
        public ISimulationScheduleCapacity ScheduleCapacity
        {
            get
            {
                for (int index = 0; index < PassRuntimeFactories.Factories.Count; index++)
                {
                    IFloat32PipelinePassRuntimeFactory factory = PassRuntimeFactories.Factories[index];
                    if (factory.Descriptor.Phase == SimulationPipelinePhase.Schedule)
                        return (ISimulationScheduleCapacity)factory;
                }
                throw new InvalidOperationException("Float32 Pipeline runtime package has no Schedule capacity owner.");
            }
        }

        void Validate()
        {
            if (!PassFactories.Backend.Equals(Float32PassExecutionBackend.Descriptor.Identity))
                throw new ArgumentException("Float32 Pipeline runtime package targets another Execution Backend.");
            if (Pipeline.Passes.Count != PassFactories.Factories.Count ||
                Pipeline.Passes.Count != PassRuntimeFactories.Factories.Count)
                throw new ArgumentException("Float32 Pipeline runtime package Pass catalogs do not match the descriptor.");
            for (int i = 0; i < Pipeline.Passes.Count; i++)
            {
                SimulationPipelinePassDescriptor pass = Pipeline.Passes[i];
                SimulationPipelinePassFactoryDescriptor portable = FindFactory(PassFactories.Factories, pass);
                SimulationPipelinePassFactoryDescriptor runtime = FindRuntimeFactory(PassRuntimeFactories.Factories, pass);
                RequireFactoryMatchesPass(portable, pass);
                RequireFactoriesEqual(portable, runtime);
            }
            if (PassFactories.Products.Count != ProductRuntimeFactories.Factories.Count)
                throw new ArgumentException("Float32 Pipeline runtime package Product catalogs have another size.");
            for (int i = 0; i < PassFactories.Products.Count; i++)
                if (!ProductRuntimeFactories.Factories[i].Contract.Equals(PassFactories.Products[i]))
                    throw new ArgumentException($"Float32 Pipeline Product runtime '{PassFactories.Products[i].ProductId}' has another contract.");
        }

        StableHash BuildIdentity()
        {
            var values = new List<string>
            {
                "float32-pipeline-runtime-package/1",
                Float32PassExecutionBackend.Descriptor.Identity.ToString(),
                Pipeline.DescriptorHash.ToString()
            };
            for (int i = 0; i < PassFactories.Factories.Count; i++)
            {
                SimulationPipelinePassFactoryDescriptor factory = PassFactories.Factories[i];
                values.Add(string.Join(":",
                    "pass",
                    factory.Identity.PassId.Value,
                    factory.Identity.ImplementationVersion.Value,
                    factory.Identity.FactoryVersion,
                    factory.Identity.BindingSchemaHash.ToString(),
                    ((int)factory.Phase).ToString(CultureInfo.InvariantCulture),
                    factory.SupportedConfigurationHash.ToString(),
                    ((int)factory.ExecutionSupport).ToString(CultureInfo.InvariantCulture),
                    factory.Deterministic.ToString(),
                    factory.SupportsSnapshotCapture.ToString(),
                    factory.SupportsSnapshotRestore.ToString(),
                    factory.SupportsReconstruction.ToString(),
                    factory.StateSchemaId,
                    factory.StateSchemaVersion.ToString(CultureInfo.InvariantCulture)));
            }
            for (int i = 0; i < ProductRuntimeFactories.Factories.Count; i++)
            {
                IFloat32PipelineProductSlotFactory factory = ProductRuntimeFactories.Factories[i];
                SimulationPipelineProductContract product = factory.Contract;
                values.Add(string.Join(":",
                    "product",
                    product.VersionedIdentity,
                    product.Owner,
                    ((int)product.Multiplicity).ToString(CultureInfo.InvariantCulture),
                    product.CanonicalIdentity,
                    product.DiagnosticsShape,
                    ((int)product.ProducerPhases).ToString(CultureInfo.InvariantCulture),
                    ((int)product.ConsumerPhases).ToString(CultureInfo.InvariantCulture),
                    ((int)product.Consumption).ToString(CultureInfo.InvariantCulture),
                    ((int)product.ProvenanceFields).ToString(CultureInfo.InvariantCulture),
                    ((int)product.AppendOrdering).ToString(CultureInfo.InvariantCulture),
                    ((int)factory.Lifetime).ToString(CultureInfo.InvariantCulture)));
            }
            return StableHash.Compute(values.ToArray());
        }

        static SimulationPipelinePassFactoryDescriptor FindFactory(
            IReadOnlyList<SimulationPipelinePassFactoryDescriptor> factories,
            SimulationPipelinePassDescriptor pass)
        {
            for (int i = 0; i < factories.Count; i++)
            {
                SimulationPipelinePassFactoryDescriptor factory = factories[i];
                if (factory.Identity.PassId.Equals(pass.PassId) &&
                    factory.Identity.ImplementationVersion.Equals(pass.ImplementationVersion))
                    return factory;
            }
            throw new ArgumentException($"Float32 Pipeline Pass '{pass.PassId}@{pass.ImplementationVersion}' has no portable factory.");
        }

        static SimulationPipelinePassFactoryDescriptor FindRuntimeFactory(
            IReadOnlyList<IFloat32PipelinePassRuntimeFactory> factories,
            SimulationPipelinePassDescriptor pass)
        {
            for (int i = 0; i < factories.Count; i++)
            {
                SimulationPipelinePassFactoryDescriptor factory = factories[i].Descriptor;
                if (factory.Identity.PassId.Equals(pass.PassId) &&
                    factory.Identity.ImplementationVersion.Equals(pass.ImplementationVersion))
                    return factory;
            }
            throw new ArgumentException($"Float32 Pipeline Pass '{pass.PassId}@{pass.ImplementationVersion}' has no runtime factory.");
        }

        static void RequireFactoryMatchesPass(
            SimulationPipelinePassFactoryDescriptor factory,
            SimulationPipelinePassDescriptor pass)
        {
            if (factory.Phase != pass.Phase ||
                !factory.SupportedConfigurationHash.Equals(pass.ConfigurationHash) ||
                !CanonicalTextValue.Equals(factory.BackendId, pass.BackendId, StringComparison.Ordinal) ||
                !CanonicalTextValue.Equals(factory.BackendSemanticVersion, pass.BackendSemanticVersion, StringComparison.Ordinal))
                throw new ArgumentException($"Float32 Pipeline Pass '{pass.PassId}' descriptor and portable factory do not match.");
        }

        static void RequireFactoriesEqual(
            SimulationPipelinePassFactoryDescriptor expected,
            SimulationPipelinePassFactoryDescriptor actual)
        {
            if (!actual.Identity.PassId.Equals(expected.Identity.PassId) ||
                !actual.Identity.ImplementationVersion.Equals(expected.Identity.ImplementationVersion) ||
                !CanonicalTextValue.Equals(actual.Identity.FactoryVersion, expected.Identity.FactoryVersion, StringComparison.Ordinal) ||
                !actual.Identity.BindingSchemaHash.Equals(expected.Identity.BindingSchemaHash) ||
                actual.Phase != expected.Phase ||
                !CanonicalTextValue.Equals(actual.BackendId, expected.BackendId, StringComparison.Ordinal) ||
                !CanonicalTextValue.Equals(actual.BackendSemanticVersion, expected.BackendSemanticVersion, StringComparison.Ordinal) ||
                !actual.SupportedConfigurationHash.Equals(expected.SupportedConfigurationHash) ||
                actual.ExecutionSupport != expected.ExecutionSupport ||
                actual.Deterministic != expected.Deterministic ||
                actual.SupportsSnapshotCapture != expected.SupportsSnapshotCapture ||
                actual.SupportsSnapshotRestore != expected.SupportsSnapshotRestore ||
                actual.SupportsReconstruction != expected.SupportsReconstruction ||
                !CanonicalTextValue.Equals(actual.StateSchemaId, expected.StateSchemaId, StringComparison.Ordinal) ||
                actual.StateSchemaVersion != expected.StateSchemaVersion)
                throw new ArgumentException($"Float32 runtime factory '{expected.Identity.PassId}' does not match its portable factory.");
        }
    }

    public sealed class Float32SimulationSessionRuntimeLauncherDescriptor
    {
        public Float32SimulationSessionRuntimeLauncherDescriptor(
            string launcherId,
            string semanticVersion,
            NumericProfileId numericProfileId,
            TargetAbiVersion targetAbiVersion,
            StableHash configurationHash)
        {
            LauncherId = SimulationIdentity.Require(launcherId, nameof(launcherId));
            SemanticVersion = SimulationIdentity.Require(semanticVersion, nameof(semanticVersion));
            if (!numericProfileId.IsValid || !targetAbiVersion.IsValid || !configurationHash.IsValid)
                throw new ArgumentException("Float32 Runtime Launcher descriptor is incomplete.");
            NumericProfileId = numericProfileId;
            TargetAbiVersion = targetAbiVersion;
            ConfigurationHash = configurationHash;
        }

        public string LauncherId { get; }
        public string SemanticVersion { get; }
        public NumericProfileId NumericProfileId { get; }
        public TargetAbiVersion TargetAbiVersion { get; }
        public StableHash ConfigurationHash { get; }
    }

    public interface IFloat32SimulationSessionRuntimeLauncher
    {
        Float32SimulationSessionRuntimeLauncherDescriptor Descriptor { get; }
        Float32PassBackendCompositionResult Launch(Float32SimulationSessionCompositionRequest request);
    }

    public sealed class Float32StandardSessionRuntimeLauncher : IFloat32SimulationSessionRuntimeLauncher
    {
        const string LauncherId = "thirdperson.simulation.runtime-launcher.float32-standard";
        readonly SimulationComponentIdentity m_SourceIdentity;
        int m_Launched;

        public Float32StandardSessionRuntimeLauncher(SimulationSessionSourceDescriptor source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            m_SourceIdentity = source.Identity;
            Descriptor = new Float32SimulationSessionRuntimeLauncherDescriptor(
                LauncherId,
                "1",
                source.NumericProfileId,
                source.TargetAbiVersion,
                StableHash.Compute(
                    "float32-standard-session-runtime-launcher/1",
                    source.Identity.ToString(),
                    source.NumericProfileId.Value,
                    source.TargetAbiVersion.Value.ToString(CultureInfo.InvariantCulture)));
        }

        public Float32SimulationSessionRuntimeLauncherDescriptor Descriptor { get; }

        public Float32PassBackendCompositionResult Launch(Float32SimulationSessionCompositionRequest request)
        {
            if (Interlocked.Exchange(ref m_Launched, 1) != 0)
                throw Failure("runtime_launcher_already_used", "Float32 Standard Runtime Launcher is single-use.");
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (!request.Source.Identity.Equals(m_SourceIdentity) ||
                !request.Source.NumericProfileId.Equals(Descriptor.NumericProfileId) ||
                !request.Source.TargetAbiVersion.Equals(Descriptor.TargetAbiVersion) ||
                !request.CharacterRuntime.NumericProfile.Id.Equals(Descriptor.NumericProfileId) ||
                !request.CharacterRuntime.NumericProfile.AbiVersion.Equals(Descriptor.TargetAbiVersion))
                throw Failure("runtime_launcher_target_mismatch", "Float32 Runtime Launcher, Session Source, and Character Runtime target do not match.");
            return Float32SimulationSessionComposer.Compose(request);
        }

        static SimulationSessionCompositionException Failure(string code, string message) =>
            new SimulationSessionCompositionException(new SimulationSessionFailure(
                SimulationSessionFailureStage.Composition,
                code,
                message,
                LauncherId));
    }

    public sealed class Float32SimulationSessionCompositionRequest
    {
        readonly ReadOnlyCollection<SimulationOutputRouteDescriptor> m_OutputRoutes;
        readonly ReadOnlyCollection<IDisposable> m_SourceResources;
        readonly ReadOnlyCollection<IDisposable> m_ActorResources;

        public Float32SimulationSessionCompositionRequest(
            SimulationSessionId sessionId,
            SimulationWorldId worldId,
            SimulationSourceClockId sourceClockId,
            int tickRate,
            int recordedFrameCapacity,
            int maximumSessionCheckpointPayloadBytes,
            Float32CharacterRuntime characterRuntime,
            SimulationExecutionBackendDescriptor backend,
            Float32SimulationPipelineRuntimePackage pipelineRuntimePackage,
            SimulationSessionSourceDescriptor source,
            SimulationRuntimePortSet sourcePorts,
            IFloat32SimulationRestoreSource restoreSource,
            SimulationWorldSolverDefinitionDescriptor solverDefinition,
            ICharacterWorldSolver solver,
            WorldFeature requiredSolverFeatures,
            SimulationWorldStateSet initialState,
            SimulationPipelineInitialStateSource pipelineInitialState,
            IFloat32SimulationCommitter committer,
            SimulationComponentIdentity diagnosticsIdentity,
            ISimulationDiagnosticsSink diagnostics,
            IEnumerable<SimulationOutputRouteDescriptor> outputRoutes,
            IEnumerable<IDisposable> sourceResources = null,
            IEnumerable<IDisposable> actorResources = null,
            GraphBatchExecutionOptions graphBatchOptions = default,
            IGraphBatchExecutionResource graphBatchResource = null)
        {
            if (!sessionId.IsValid || !worldId.IsValid || !sourceClockId.IsValid || tickRate <= 0)
                throw new ArgumentException("Float32 Session composition identity is incomplete.");
            SessionId = sessionId;
            WorldId = worldId;
            SourceClockId = sourceClockId;
            TickRate = tickRate;
            RecordedFrameCapacity = recordedFrameCapacity;
            MaximumSessionCheckpointPayloadBytes = maximumSessionCheckpointPayloadBytes;
            CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            GraphBatchOptions = graphBatchOptions;
            GraphBatchResource = graphBatchResource;
            Backend = backend ?? throw new ArgumentNullException(nameof(backend));
            PipelineRuntimePackage = pipelineRuntimePackage ?? throw new ArgumentNullException(nameof(pipelineRuntimePackage));
            Source = source ?? throw new ArgumentNullException(nameof(source));
            SourcePorts = sourcePorts ?? throw new ArgumentNullException(nameof(sourcePorts));
            RestoreSource = restoreSource;
            SolverDefinition = solverDefinition ?? throw new ArgumentNullException(nameof(solverDefinition));
            Solver = solver ?? throw new ArgumentNullException(nameof(solver));
            RequiredSolverFeatures = requiredSolverFeatures;
            InitialState = initialState ?? throw new ArgumentNullException(nameof(initialState));
            PipelineInitialState = pipelineInitialState ?? throw new ArgumentNullException(nameof(pipelineInitialState));
            Committer = committer ?? throw new ArgumentNullException(nameof(committer));
            if (!diagnosticsIdentity.IsValid || diagnosticsIdentity.Role != SimulationComponentRole.Diagnostics)
                throw new ArgumentException("Diagnostics identity is invalid.", nameof(diagnosticsIdentity));
            DiagnosticsIdentity = diagnosticsIdentity;
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            m_OutputRoutes = Freeze(outputRoutes, nameof(outputRoutes));
            m_SourceResources = Freeze(sourceResources, nameof(sourceResources));
            m_ActorResources = Freeze(actorResources, nameof(actorResources));
        }

        public SimulationSessionId SessionId { get; }
        public SimulationWorldId WorldId { get; }
        public SimulationSourceClockId SourceClockId { get; }
        public int TickRate { get; }
        public int RecordedFrameCapacity { get; }
        public int MaximumSessionCheckpointPayloadBytes { get; }
        public Float32CharacterRuntime CharacterRuntime { get; }
        public GraphBatchExecutionOptions GraphBatchOptions { get; }
        public IGraphBatchExecutionResource GraphBatchResource { get; }
        public SimulationExecutionBackendDescriptor Backend { get; }
        public Float32SimulationPipelineRuntimePackage PipelineRuntimePackage { get; }
        public SimulationSessionSourceDescriptor Source { get; }
        public SimulationRuntimePortSet SourcePorts { get; }
        public IFloat32SimulationRestoreSource RestoreSource { get; }
        public SimulationWorldSolverDefinitionDescriptor SolverDefinition { get; }
        public ICharacterWorldSolver Solver { get; }
        public WorldFeature RequiredSolverFeatures { get; }
        public SimulationWorldStateSet InitialState { get; }
        public SimulationPipelineInitialStateSource PipelineInitialState { get; }
        public IFloat32SimulationCommitter Committer { get; }
        public SimulationComponentIdentity DiagnosticsIdentity { get; }
        public ISimulationDiagnosticsSink Diagnostics { get; }
        public IReadOnlyList<SimulationOutputRouteDescriptor> OutputRoutes => m_OutputRoutes;
        public IReadOnlyList<IDisposable> SourceResources => m_SourceResources;
        public IReadOnlyList<IDisposable> ActorResources => m_ActorResources;

        static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> source, string parameter)
        {
            var values = source == null ? new List<T>() : new List<T>(source);
            for (int i = 0; i < values.Count; i++)
                if (values[i] == null)
                    throw new ArgumentException("Composition collection contains a missing value.", parameter);
            return values.AsReadOnly();
        }
    }

    public static class Float32SimulationSessionComposer
    {
        const string SnapshotCodecId = "thirdperson.simulation.snapshot-codec.float32-session";

        public static Float32PassBackendCompositionResult Compose(Float32SimulationSessionCompositionRequest request) =>
            Compose(request, default, false);

        public static Float32PassBackendCompositionResult Compose(
            Float32SimulationSessionCompositionRequest request,
            SimulationPipelineIdentity expectedPipeline)
        {
            if (!expectedPipeline.IsValid)
                throw new ArgumentException("Expected Pipeline identity is incomplete.", nameof(expectedPipeline));
            return Compose(request, expectedPipeline, true);
        }

        static Float32PassBackendCompositionResult Compose(
            Float32SimulationSessionCompositionRequest request,
            SimulationPipelineIdentity expectedPipeline,
            bool requireExpectedPipeline)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            Validate(request);
            SimulationExecutionTargetManifest target = Float32SimulationTarget.Manifest.ExecutionTarget;
            SimulationExecutionBackendTargetSupport targetSupport = request.Backend.RequireTarget(
                target.NumericProfile.Id,
                target.NumericProfile.AbiVersion,
                request.PipelineRuntimePackage.Pipeline.SchemaVersion);
            SimulationComponentIdentity snapshotIdentity = BuildSnapshotCodecIdentity(target, request.Backend);
            SimulationPipelineCompilationResult compilation = SimulationPipelineCompiler.Compile(
                request.PipelineRuntimePackage.Pipeline,
                request.PipelineRuntimePackage.PassFactories,
                target,
                request.CharacterRuntime.RequiredWorldCapabilities,
                targetSupport.ExecutionSupport,
                targetSupport.Deterministic,
                request.Backend,
                request.Source,
                PortDescriptors(request.SourcePorts),
                request.SolverDefinition,
                snapshotIdentity,
                targetSupport.ExecutionSupport,
                targetSupport.Deterministic);
            if (!compilation.IsValid)
                throw PipelineFailure(compilation.Errors);
            CompiledSimulationPipelinePlan pipeline = compilation.Plan;
            if (requireExpectedPipeline && !pipeline.Identity.Equals(expectedPipeline))
                throw Failure(
                    "pipeline_identity_constraint_mismatch",
                    $"Compiled Pipeline '{pipeline.Identity}' does not match expected identity '{expectedPipeline}'.");
            var descriptor = new SimulationSessionCompositionDescriptor(
                request.SessionId,
                request.WorldId,
                request.SourceClockId,
                request.TickRate,
                request.RecordedFrameCapacity,
                request.MaximumSessionCheckpointPayloadBytes,
                target,
                request.Backend.Identity,
                pipeline.Identity,
                request.CharacterRuntime.GameplayContentHash,
                request.CharacterRuntime.RosterDescriptor,
                request.Source.Identity,
                request.SolverDefinition.Identity,
                request.SolverDefinition.ImplementationId,
                request.SolverDefinition.Capabilities,
                request.SolverDefinition.Features,
                snapshotIdentity,
                request.Committer.Identity,
                request.Source.Model,
                request.Source.Endpoint,
                request.Source.Protocol);
            var snapshotCodec = new Float32SimulationSessionSnapshotCodec(snapshotIdentity);
            var backendRequest = new Float32PassBackendCompositionRequest(
                descriptor,
                request.Backend,
                pipeline,
                request.CharacterRuntime,
                request.InitialState,
                request.PipelineInitialState,
                request.SourcePorts,
                PortDescriptors(request.SourcePorts),
                request.RestoreSource,
                request.Solver,
                snapshotCodec,
                request.Committer,
                request.DiagnosticsIdentity,
                request.Diagnostics,
                request.OutputRoutes,
                request.PipelineRuntimePackage.PassRuntimeFactories,
                request.PipelineRuntimePackage.ProductRuntimeFactories,
                request.SourceResources,
                request.ActorResources,
                request.GraphBatchOptions,
                request.GraphBatchResource);
            return Float32PassExecutionBackend.Create(backendRequest);
        }

        static void Validate(Float32SimulationSessionCompositionRequest request)
        {
            SimulationExecutionTargetManifest target = Float32SimulationTarget.Manifest.ExecutionTarget;
            SimulationSessionSourceDescriptor source = request.Source;
            SimulationWorldSolverDefinitionDescriptor solver = request.SolverDefinition;
            if (request.TickRate != request.CharacterRuntime.TickRate ||
                request.CharacterRuntime.NumericProfile != target.NumericProfile ||
                !request.CharacterRuntime.OperationSetVersion.Equals(target.OperationSetVersion))
                throw Failure("float32_character_runtime_identity_mismatch", "Float32 Character Runtime does not match the numeric execution target.");
            if (!request.Backend.Identity.Equals(Float32PassExecutionBackend.Descriptor.Identity))
                throw Failure("backend_target_mismatch", "Float32 Composer requires the canonical Float32 Pass Backend.");
            if (!source.NumericProfileId.Equals(target.NumericProfile.Id) ||
                !source.TargetAbiVersion.Equals(target.NumericProfile.AbiVersion) ||
                !solver.NumericProfileId.Equals(target.NumericProfile.Id) ||
                !solver.TargetAbiVersion.Equals(target.NumericProfile.AbiVersion))
                throw Failure("composition_target_abi_mismatch", "Character Runtime, Session Source, and World Solver Target ABIs do not match.");
            if (!CanonicalTextValue.Equals(source.RequiredBackendId, request.Backend.BackendId, StringComparison.Ordinal) ||
                !source.RequiredPipelineId.Equals(request.PipelineRuntimePackage.Pipeline.PipelineId))
                throw Failure("source_composition_mismatch", "Session Source requires another Float32 Backend or Pipeline.");
            WorldCapability requiredCapabilities = request.CharacterRuntime.RequiredWorldCapabilities |
                source.RequiredSolverCapabilities;
            if ((solver.Capabilities & requiredCapabilities) != requiredCapabilities)
                throw Failure("solver_capability_missing", "World Solver lacks a capability required by the Character Runtime or Session Source.");
            if ((solver.Features & request.RequiredSolverFeatures) != request.RequiredSolverFeatures)
                throw Failure("solver_feature_missing", "World Solver lacks a feature required by the Session Composition.");
            ICharacterWorldSolver runtime = request.Solver;
            if (!runtime.Descriptor.NumericProfile.Id.Equals(solver.NumericProfileId) ||
                !runtime.Descriptor.NumericProfile.AbiVersion.Equals(solver.TargetAbiVersion) ||
                !runtime.Descriptor.ImplementationId.Equals(solver.ImplementationId) ||
                !CanonicalTextValue.Equals(runtime.Descriptor.Version, solver.ImplementationVersion, StringComparison.Ordinal) ||
                runtime.Descriptor.Capabilities != solver.Capabilities ||
                runtime.Descriptor.Features != solver.Features)
                throw Failure("solver_runtime_identity_mismatch", "World Solver runtime does not match its descriptor.");
        }

        public static SimulationComponentIdentity BuildSnapshotCodecIdentity(
            SimulationExecutionTargetManifest target,
            SimulationExecutionBackendDescriptor backend)
        {
            return new SimulationComponentIdentity(
                SimulationComponentRole.SnapshotCodec,
                SnapshotCodecId,
                "2",
                StableHash.Compute(
                    SnapshotCodecId,
                    target.NumericProfile.Id.Value,
                    target.NumericProfile.AbiVersion.Value.ToString(CultureInfo.InvariantCulture),
                    backend.Identity.ToString()));
        }

        static IReadOnlyList<SimulationPortDescriptor> PortDescriptors(SimulationRuntimePortSet ports)
        {
            var values = new SimulationPortDescriptor[ports.Ports.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = ports.Ports[i].Descriptor;
            return values;
        }

        static SimulationSessionCompositionException PipelineFailure(
            IReadOnlyList<SimulationPipelineCompileError> errors)
        {
            var details = new string[errors.Count];
            for (int i = 0; i < errors.Count; i++)
                details[i] = errors[i].ToString();
            return Failure("pipeline_compile_failed", string.Join(" | ", details));
        }

        static SimulationSessionCompositionException Failure(string code, string message) =>
            new SimulationSessionCompositionException(new SimulationSessionFailure(
                SimulationSessionFailureStage.Composition,
                code,
                message,
                Float32PassExecutionBackend.BackendId));
    }
}
