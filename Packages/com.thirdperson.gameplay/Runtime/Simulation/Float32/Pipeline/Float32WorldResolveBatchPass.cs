using System;

namespace ThirdPersonSimulation
{
    public sealed class Float32WorldResolveBatchPassRuntimeFactory : IFloat32PipelinePassRuntimeFactory
    {
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFloat32PipelinePassContracts.CreateFactoryDescriptor(
                StandardFloat32PipelinePassContracts.WorldResolveBatch);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;

        public IFloat32CompiledPipelinePassRuntime Create(Float32PipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new Float32WorldResolveBatchReadPorts(
                context.Products.BindExclusiveReader<WorldSolveBatchRequest>(SimulationPipelineProducts.WorldSolveBatchRequest),
                context.BindSolverPort<IFloat32WorldSolverRuntimePort>(Float32PipelineRuntimePortIds.WorldSolver),
                context.BindDiagnosticsPort<IFloat32DiagnosticsRuntimePort>(Float32PipelineRuntimePortIds.Diagnostics));
            var writes = new Float32WorldResolveBatchWritePorts(
                context.Products.BindExclusiveWriter<WorldSolveBatchResult>(SimulationPipelineProducts.WorldSolveBatchResult));
            return new Float32StepPassRuntimeAdapter<Float32WorldResolveBatchReadPorts, Float32WorldResolveBatchWritePorts>(
                new Float32WorldResolveBatchPassRuntime(context.Pass.Descriptor),
                reads,
                writes);
        }
    }

    public sealed class Float32WorldResolveBatchPassRuntime :
        Float32PipelinePassRuntimeBase,
        ISimulationStepPassRuntime<Float32WorldResolveBatchReadPorts, Float32WorldResolveBatchWritePorts>
    {
        public Float32WorldResolveBatchPassRuntime(SimulationPipelinePassDescriptor descriptor)
            : base(descriptor)
        {
        }

        public void Execute(
            SimulationPipelineStepTransactionContext context,
            Float32WorldResolveBatchReadPorts readPorts,
            Float32WorldResolveBatchWritePorts writePorts)
        {
            RequireExecution();
            WorldSolveBatchRequest request = readPorts.Request.Read();
            if (request.Tick != context.Tick)
                throw new InvalidOperationException("World Resolve Pass request belongs to another Step.");
            ICharacterWorldSolver solver = readPorts.Solver.Solver;
            WorldSolveBatchResult result = solver.ResolveBatch(request, readPorts.Diagnostics.Sink) ??
                throw new InvalidOperationException("World Solver returned no batch result.");
            if (result.Tick != context.Tick || !ReferenceEquals(result.Request, request) ||
                !result.SolverId.Equals(solver.Descriptor.ImplementationId) ||
                !CanonicalTextValue.Equals(result.SolverVersion, solver.Descriptor.Version, StringComparison.Ordinal) ||
                result.Results.Count != request.Requests.Count)
            {
                throw new InvalidOperationException("World Solver returned a batch for another request, Tick or Solver identity.");
            }
            writePorts.Result.Write(result);
        }
    }

    public sealed class Float32WorldResolveBatchReadPorts : ISimulationPipelineReadPortSet
    {
        public Float32WorldResolveBatchReadPorts(
            IReadOnlySimulationPipelineProductPort<WorldSolveBatchRequest> request,
            IFloat32WorldSolverRuntimePort solver,
            IFloat32DiagnosticsRuntimePort diagnostics)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Solver = solver ?? throw new ArgumentNullException(nameof(solver));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public IReadOnlySimulationPipelineProductPort<WorldSolveBatchRequest> Request { get; }
        public IFloat32WorldSolverRuntimePort Solver { get; }
        public IFloat32DiagnosticsRuntimePort Diagnostics { get; }
    }

    public sealed class Float32WorldResolveBatchWritePorts : ISimulationPipelineWritePortSet
    {
        public Float32WorldResolveBatchWritePorts(
            IExclusiveSimulationPipelineProductWriter<WorldSolveBatchResult> result)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
        }

        public IExclusiveSimulationPipelineProductWriter<WorldSolveBatchResult> Result { get; }
    }
}
