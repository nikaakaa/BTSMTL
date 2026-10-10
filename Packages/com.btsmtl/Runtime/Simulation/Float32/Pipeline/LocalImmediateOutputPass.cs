using System;

namespace ThirdPersonSimulation
{
    public sealed class LocalImmediateOutputPassRuntimeFactory : IFloat32PipelinePassRuntimeFactory
    {
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFloat32PipelinePassContracts.CreateFactoryDescriptor(
                StandardFloat32PipelinePassContracts.LocalImmediateOutput);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;

        public IFloat32CompiledPipelinePassRuntime Create(Float32PipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new LocalImmediateOutputReadPorts(
                context.Products.BindAppendReader<SimulationActorTickResult>(SimulationPipelineProducts.FinalizedStepResult));
            var writes = new LocalImmediateOutputWritePorts(
                context.Products.BindExclusiveWriter<SimulationPipelineOutputDispositionSet>(SimulationPipelineProducts.OutputDispositionSet));
            IFloat32CharacterRuntimePort characterRuntime = context.BindTargetPort<IFloat32CharacterRuntimePort>(
                Float32PipelineRuntimePortIds.CharacterRuntime);
            int outputCapacity = 0;
            for (int actor = 0; actor < characterRuntime.Runtime.Roster.Count; actor++)
                outputCapacity = checked(outputCapacity + characterRuntime.Runtime.Roster[actor].GameplayOutputCapacity);
            return new Float32EgressPassRuntimeAdapter<LocalImmediateOutputReadPorts, LocalImmediateOutputWritePorts>(
                new LocalImmediateOutputPassRuntime(context.Pass.Descriptor, outputCapacity),
                reads,
                writes);
        }
    }

    public sealed class LocalImmediateOutputPassRuntime :
        Float32PipelinePassRuntimeBase,
        ISimulationEgressPassRuntime<LocalImmediateOutputReadPorts, LocalImmediateOutputWritePorts>
    {
        readonly SimulationOutputDispositionBuffer m_Dispositions;

        public LocalImmediateOutputPassRuntime(SimulationPipelinePassDescriptor descriptor, int outputCapacity)
            : base(descriptor)
        {
            m_Dispositions = new SimulationOutputDispositionBuffer(outputCapacity);
        }

        public void Execute(
            SimulationPipelineEgressContext context,
            LocalImmediateOutputReadPorts readPorts,
            LocalImmediateOutputWritePorts writePorts)
        {
            RequireExecution();
            m_Dispositions.Begin();
            for (int i = 0; i < readPorts.Results.Count; i++)
            {
                SimulationActorTickResult result = readPorts.Results.Get(i).Value;
                for (int eventIndex = 0; eventIndex < result.GameplayFacts.Count; eventIndex++)
                {
                    m_Dispositions.Add(new SimulationOutputDisposition(
                        result.GameplayFacts[eventIndex].Header.EventId,
                        result.GameplayFacts[eventIndex].Header.ActorId,
                        SimulationOutputDispositionKind.Publish));
                }
                for (int eventIndex = 0; eventIndex < result.PresentationCommands.Count; eventIndex++)
                {
                    m_Dispositions.Add(new SimulationOutputDisposition(
                        result.PresentationCommands[eventIndex].Header.EventId,
                        result.PresentationCommands[eventIndex].Header.ActorId,
                        SimulationOutputDispositionKind.Publish));
                }
            }
            writePorts.Dispositions.Write(SimulationPipelineOutputDispositionSet.FromBufferedDispositions(
                context.TransactionIdentity,
                m_Dispositions));
        }

        protected override void OnDispose() => m_Dispositions.Begin();
    }

    public sealed class LocalImmediateOutputReadPorts : ISimulationPipelineReadPortSet
    {
        public LocalImmediateOutputReadPorts(
            IReadOnlySimulationPipelineAppendPort<SimulationActorTickResult> results)
        {
            Results = results ?? throw new ArgumentNullException(nameof(results));
        }

        public IReadOnlySimulationPipelineAppendPort<SimulationActorTickResult> Results { get; }
    }

    public sealed class LocalImmediateOutputWritePorts : ISimulationPipelineWritePortSet
    {
        public LocalImmediateOutputWritePorts(
            IExclusiveSimulationPipelineProductWriter<SimulationPipelineOutputDispositionSet> dispositions)
        {
            Dispositions = dispositions ?? throw new ArgumentNullException(nameof(dispositions));
        }

        public IExclusiveSimulationPipelineProductWriter<SimulationPipelineOutputDispositionSet> Dispositions { get; }
    }
}
