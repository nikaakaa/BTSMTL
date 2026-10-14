using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public enum SimulationPipelineCompileErrorCode : byte
    {
        BackendCatalogMismatch = 1,
        UnknownPassFactory = 2,
        UnsupportedPassVersion = 3,
        FactoryPhaseMismatch = 4,
        PassConfigurationMismatch = 5,
        UnknownProduct = 6,
        ProductContractMismatch = 7,
        IllegalProductPhase = 8,
        MissingExecutionPlanProducer = 9,
        DuplicateExecutionPlanProducer = 10,
        MissingExclusiveProducer = 11,
        DuplicateExclusiveProducer = 12,
        ProductUseBeforeProduce = 13,
        InvalidAppendOrdering = 14,
        UnconsumedRequiredProduct = 15,
        ProductDependencyCycle = 16,
        MissingSourcePort = 17,
        SourcePortMismatch = 18,
        NumericProfileMismatch = 19,
        TargetAbiMismatch = 20,
        BackendMismatch = 21,
        SolverCapabilityMismatch = 22,
        ExecutionSupportMismatch = 23,
        DeterministicSupportMismatch = 24,
        InvalidPassStateOwnership = 25,
        SnapshotCapabilityMismatch = 26,
        FactoryCapabilityMismatch = 27,
        MissingSourceRequiredPass = 28,
        UnconsumedSourceRequiredPort = 29
    }

    public sealed class SimulationPipelineCompileError
    {
        public SimulationPipelineCompileError(
            SimulationPipelineCompileErrorCode code,
            string message,
            SimulationPipelinePassId passId = default,
            SimulationPipelineProductId productId = default,
            string componentIdentity = "")
        {
            if (!Enum.IsDefined(typeof(SimulationPipelineCompileErrorCode), code))
                throw new ArgumentOutOfRangeException(nameof(code));
            Code = code;
            Message = SimulationIdentity.Require(message, nameof(message));
            PassId = passId;
            ProductId = productId;
            ComponentIdentity = componentIdentity ?? string.Empty;
        }

        public SimulationPipelineCompileErrorCode Code { get; }
        public string Message { get; }
        public SimulationPipelinePassId PassId { get; }
        public SimulationPipelineProductId ProductId { get; }
        public string ComponentIdentity { get; }
        public override string ToString() => $"{Code}: {Message}";
    }

    public sealed class CompiledSimulationPipelinePass
    {
        internal CompiledSimulationPipelinePass(
            SimulationPipelinePassDescriptor descriptor,
            SimulationPipelinePassFactoryDescriptor factory,
            int globalIndex,
            int phaseIndex)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            if (globalIndex < 0 || phaseIndex < 0)
                throw new ArgumentOutOfRangeException();
            GlobalIndex = globalIndex;
            PhaseIndex = phaseIndex;
        }

        public SimulationPipelinePassDescriptor Descriptor { get; }
        public SimulationPipelinePassFactoryDescriptor Factory { get; }
        public int GlobalIndex { get; }
        public int PhaseIndex { get; }
    }

    public sealed class CompiledSimulationPipelinePlan
    {
        readonly ReadOnlyCollection<CompiledSimulationPipelinePass> m_Passes;
        readonly ReadOnlyCollection<SimulationPipelineProductContract> m_Products;

        internal CompiledSimulationPipelinePlan(
            SimulationPipelineDescriptor descriptor,
            SimulationPipelineIdentity identity,
            SimulationComponentIdentity backend,
            SimulationPipelineExecutionSupport requiredExecutionSupport,
            bool deterministic,
            StableHash planHash,
            IEnumerable<CompiledSimulationPipelinePass> passes,
            IEnumerable<SimulationPipelineProductContract> products)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            if (!identity.IsValid || !backend.IsValid || backend.Role != SimulationComponentRole.ExecutionBackend ||
                !planHash.IsValid)
            {
                throw new ArgumentException("Compiled Pipeline plan identity is incomplete.");
            }
            Identity = identity;
            Backend = backend;
            RequiredExecutionSupport = requiredExecutionSupport;
            Deterministic = deterministic;
            PlanHash = planHash;
            var passValues = passes == null ? new List<CompiledSimulationPipelinePass>() : new List<CompiledSimulationPipelinePass>(passes);
            var productValues = products == null ? new List<SimulationPipelineProductContract>() : new List<SimulationPipelineProductContract>(products);
            m_Passes = passValues.AsReadOnly();
            m_Products = productValues.AsReadOnly();
            LaunchIdentity = new SimulationCompiledPipelinePlanIdentity(identity, planHash, passValues.Count);
        }

        public SimulationPipelineDescriptor Descriptor { get; }
        public SimulationPipelineIdentity Identity { get; }
        public SimulationComponentIdentity Backend { get; }
        public SimulationPipelineExecutionSupport RequiredExecutionSupport { get; }
        public bool Deterministic { get; }
        public StableHash PlanHash { get; }
        public SimulationCompiledPipelinePlanIdentity LaunchIdentity { get; }
        public IReadOnlyList<CompiledSimulationPipelinePass> Passes => m_Passes;
        public IReadOnlyList<SimulationPipelineProductContract> Products => m_Products;
    }

    public sealed class SimulationPipelineCompilationResult
    {
        readonly ReadOnlyCollection<SimulationPipelineCompileError> m_Errors;

        internal SimulationPipelineCompilationResult(
            CompiledSimulationPipelinePlan plan,
            IEnumerable<SimulationPipelineCompileError> errors)
        {
            Plan = plan;
            m_Errors = (errors == null
                ? new List<SimulationPipelineCompileError>()
                : new List<SimulationPipelineCompileError>(errors)).AsReadOnly();
            if ((plan != null) == (m_Errors.Count != 0))
                throw new ArgumentException("Pipeline compilation result must contain either a plan or errors.");
        }

        public CompiledSimulationPipelinePlan Plan { get; }
        public IReadOnlyList<SimulationPipelineCompileError> Errors => m_Errors;
        public bool IsValid => Plan != null && m_Errors.Count == 0;
    }
}
