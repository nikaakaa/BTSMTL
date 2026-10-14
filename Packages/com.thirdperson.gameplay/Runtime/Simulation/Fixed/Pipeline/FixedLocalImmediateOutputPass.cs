using System;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class FixedLocalImmediateOutputPassRuntimeFactory : IFixedPipelinePassRuntimeFactory
    {
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFixedLocalPipelinePassContracts.CreateFactoryDescriptor(
                StandardFixedLocalPipelinePassContracts.LocalImmediateOutput);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;

        public IFixedCompiledPipelinePassRuntime Create(FixedPipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new FixedLocalImmediateOutputReadPorts(
                context.Products.BindAppendReader<SimulationActorTickResult>(SimulationPipelineProducts.FinalizedStepResult));
            var writes = new FixedLocalImmediateOutputWritePorts(
                context.Products.BindExclusiveWriter<SimulationPipelineOutputDispositionSet>(SimulationPipelineProducts.OutputDispositionSet));
            IFixedCharacterRuntimePort characterRuntime = context.BindTargetPort<IFixedCharacterRuntimePort>(
                FixedPipelineRuntimePortIds.CharacterRuntime);
            int outputCapacity = 0;
            for (int actor = 0; actor < characterRuntime.Runtime.Roster.Count; actor++)
                outputCapacity = checked(outputCapacity + characterRuntime.Runtime.Roster[actor].GameplayOutputCapacity);
            return new FixedEgressPassRuntimeAdapter<FixedLocalImmediateOutputReadPorts, FixedLocalImmediateOutputWritePorts>(
                new FixedLocalImmediateOutputPassRuntime(context.Pass.Descriptor, outputCapacity),
                reads,
                writes);
        }
    }

    public sealed class FixedLocalImmediateOutputPassRuntime :
        FixedPipelinePassRuntimeBase,
        ISimulationEgressPassRuntime<FixedLocalImmediateOutputReadPorts, FixedLocalImmediateOutputWritePorts>
    {
        readonly SimulationOutputDispositionBuffer m_Dispositions;

        public FixedLocalImmediateOutputPassRuntime(SimulationPipelinePassDescriptor descriptor, int outputCapacity)
            : base(descriptor)
        {
            m_Dispositions = new SimulationOutputDispositionBuffer(outputCapacity);
        }

        public void Execute(
            SimulationPipelineEgressContext context,
            FixedLocalImmediateOutputReadPorts readPorts,
            FixedLocalImmediateOutputWritePorts writePorts)
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

    public sealed class FixedLocalImmediateOutputReadPorts : ISimulationPipelineReadPortSet
    {
        public FixedLocalImmediateOutputReadPorts(
            IReadOnlySimulationPipelineAppendPort<SimulationActorTickResult> results)
        {
            Results = results ?? throw new ArgumentNullException(nameof(results));
        }

        public IReadOnlySimulationPipelineAppendPort<SimulationActorTickResult> Results { get; }
    }

    public sealed class FixedLocalImmediateOutputWritePorts : ISimulationPipelineWritePortSet
    {
        public FixedLocalImmediateOutputWritePorts(
            IExclusiveSimulationPipelineProductWriter<SimulationPipelineOutputDispositionSet> dispositions)
        {
            Dispositions = dispositions ?? throw new ArgumentNullException(nameof(dispositions));
        }

        public IExclusiveSimulationPipelineProductWriter<SimulationPipelineOutputDispositionSet> Dispositions { get; }
    }
}
