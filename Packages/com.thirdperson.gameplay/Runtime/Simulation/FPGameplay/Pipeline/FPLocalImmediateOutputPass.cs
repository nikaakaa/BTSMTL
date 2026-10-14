using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    public sealed class FPLocalImmediateOutputPassRuntimeFactory : IFPPipelinePassRuntimeFactory
    {
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFPLocalPipelinePassContracts.CreateFactoryDescriptor(
                StandardFPLocalPipelinePassContracts.LocalImmediateOutput);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;

        public IFPCompiledPipelinePassRuntime Create(FPPipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new FPLocalImmediateOutputReadPorts(
                context.Products.BindAppendReader<SimulationActorTickResult>(SimulationPipelineProducts.FinalizedStepResult));
            var writes = new FPLocalImmediateOutputWritePorts(
                context.Products.BindExclusiveWriter<SimulationPipelineOutputDispositionSet>(SimulationPipelineProducts.OutputDispositionSet));
            IFPCharacterRuntimePort characterRuntime = context.BindTargetPort<IFPCharacterRuntimePort>(
                FPPipelineRuntimePortIds.CharacterRuntime);
            int outputCapacity = 0;
            for (int actor = 0; actor < characterRuntime.Runtime.Roster.Count; actor++)
                outputCapacity = checked(outputCapacity + characterRuntime.Runtime.Roster[actor].GameplayOutputCapacity);
            return new FPEgressPassRuntimeAdapter<FPLocalImmediateOutputReadPorts, FPLocalImmediateOutputWritePorts>(
                new FPLocalImmediateOutputPassRuntime(context.Pass.Descriptor, outputCapacity),
                reads,
                writes);
        }
    }

    public sealed class FPLocalImmediateOutputPassRuntime :
        FPPipelinePassRuntimeBase,
        ISimulationEgressPassRuntime<FPLocalImmediateOutputReadPorts, FPLocalImmediateOutputWritePorts>
    {
        readonly SimulationOutputDispositionBuffer m_Dispositions;

        public FPLocalImmediateOutputPassRuntime(SimulationPipelinePassDescriptor descriptor, int outputCapacity)
            : base(descriptor)
        {
            m_Dispositions = new SimulationOutputDispositionBuffer(outputCapacity);
        }

        public void Execute(
            SimulationPipelineEgressContext context,
            FPLocalImmediateOutputReadPorts readPorts,
            FPLocalImmediateOutputWritePorts writePorts)
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

    public sealed class FPLocalImmediateOutputReadPorts : ISimulationPipelineReadPortSet
    {
        public FPLocalImmediateOutputReadPorts(
            IReadOnlySimulationPipelineAppendPort<SimulationActorTickResult> results)
        {
            Results = results ?? throw new ArgumentNullException(nameof(results));
        }

        public IReadOnlySimulationPipelineAppendPort<SimulationActorTickResult> Results { get; }
    }

    public sealed class FPLocalImmediateOutputWritePorts : ISimulationPipelineWritePortSet
    {
        public FPLocalImmediateOutputWritePorts(
            IExclusiveSimulationPipelineProductWriter<SimulationPipelineOutputDispositionSet> dispositions)
        {
            Dispositions = dispositions ?? throw new ArgumentNullException(nameof(dispositions));
        }

        public IExclusiveSimulationPipelineProductWriter<SimulationPipelineOutputDispositionSet> Dispositions { get; }
    }
}
