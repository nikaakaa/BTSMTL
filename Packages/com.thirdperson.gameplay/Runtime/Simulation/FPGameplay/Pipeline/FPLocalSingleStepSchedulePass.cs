using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    public sealed class FPLocalSingleStepSchedulePassRuntimeFactory : IFPPipelinePassRuntimeFactory, ISimulationScheduleCapacity
    {
        internal static readonly GameplayEffectScheduleCapacity Capacity = new GameplayEffectScheduleCapacity(1, 0, 0, 0, 0, 0);
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFPLocalPipelinePassContracts.CreateFactoryDescriptor(
                StandardFPLocalPipelinePassContracts.LocalSingleStepSchedule);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;
        public GameplayEffectScheduleCapacity GameplayEffectCapacity => Capacity;
        public int MaximumStepCount => Capacity.MaximumStepCount;

        public IFPCompiledPipelinePassRuntime Create(FPPipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new FPLocalSingleStepScheduleReadPorts(
                context.Products.BindExclusiveReader<FPCanonicalInputBatch>(SimulationPipelineProducts.CanonicalInputs),
                context.Products.BindExclusiveReader<FPTypedIngressBatch>(SimulationPipelineProducts.TypedIngress),
                context.BindTargetPort<IFPCharacterRuntimePort>(FPPipelineRuntimePortIds.CharacterRuntime));
            var writes = new FPLocalSingleStepScheduleWritePorts(
                context.Products.BindExclusiveWriter<SimulationSessionExecutionPlan<FPSimulationStep>>(
                    SimulationPipelineProducts.ExecutionPlan));
            return new FPSchedulePassRuntimeAdapter<FPLocalSingleStepScheduleReadPorts, FPLocalSingleStepScheduleWritePorts>(
                new FPLocalSingleStepSchedulePassRuntime(
                    context.Pass.Descriptor,
                    reads.CharacterRuntime.Runtime.RosterDescriptor.Actors),
                reads,
                writes);
        }
    }

    public sealed class FPLocalSingleStepSchedulePassRuntime :
        FPPipelinePassRuntimeBase,
        ISimulationExecutionPlanSchedulePassRuntime<FPLocalSingleStepScheduleReadPorts, FPLocalSingleStepScheduleWritePorts>
    {
        readonly SimulationPipelineStepSourceMapping[] m_SourceMappings = new SimulationPipelineStepSourceMapping[1];
        readonly FPSimulationStep[] m_Steps =
            new FPSimulationStep[FPLocalSingleStepSchedulePassRuntimeFactory.Capacity.MaximumStepCount];
        public GameplayEffectScheduleCapacity GameplayEffectCapacity => FPLocalSingleStepSchedulePassRuntimeFactory.Capacity;
        public int MaximumStepCount => GameplayEffectCapacity.MaximumStepCount;

        public FPLocalSingleStepSchedulePassRuntime(
            SimulationPipelinePassDescriptor descriptor,
            IReadOnlyList<ActorId> actors)
            : base(descriptor)
        {
            m_Steps[0] = new FPSimulationStep(actors);
        }

        public void Execute(
            SimulationPipelineScheduleContext context,
            FPLocalSingleStepScheduleReadPorts readPorts,
            FPLocalSingleStepScheduleWritePorts writePorts)
        {
            RequireExecution();
            writePorts.ExecutionPlan.Write(Build(
                context,
                readPorts.CanonicalInputs.Read(),
                readPorts.TypedIngress.Read(),
                readPorts.CharacterRuntime));
        }

        SimulationSessionExecutionPlan<FPSimulationStep> Build(
            SimulationPipelineScheduleContext context,
            FPCanonicalInputBatch canonical,
            FPTypedIngressBatch typed,
            IFPCharacterRuntimePort characterRuntime)
        {
            if (!canonical.IsValid || !typed.IsValid || characterRuntime == null)
                throw new ArgumentNullException("FP Local single-step Schedule input is missing.");
            if (context.Source.Kind != SimulationTickSourceKind.LocalLogic || !canonical.Source.Equals(context.Source) ||
                canonical.Inputs.Count != characterRuntime.Runtime.Roster.Count)
            {
                throw new InvalidOperationException("FP Local single-step input batch does not match the outer Tick or locked roster.");
            }
            IReadOnlyList<ActorId> actorIds = characterRuntime.Runtime.RosterDescriptor.Actors;
            for (int i = 0; i < actorIds.Count; i++)
            {
                if (!canonical.Inputs[i].ActorId.Equals(actorIds[i]))
                    throw new InvalidOperationException("FP Local input Actor order does not match the locked roster.");
            }
            var tick = new SimulationTick(checked(context.CurrentCompletedTick + 1));
            bool replay = IsReplayInput(canonical);
            m_Steps[0].Reset(
                tick,
                new SimulationPipelineStepProvenance(
                    replay
                        ? SimulationPipelineStepExecutionKind.Replay
                        : SimulationPipelineStepExecutionKind.Forward,
                    context.Source,
                    context.Source.SourceTick,
                    replay ? canonical.Inputs[0].Value.Input.InputSourceIdentity : string.Empty),
                canonical.Inputs,
                typed.Ingress);
            m_SourceMappings[0] = new SimulationPipelineStepSourceMapping(
                context.Source.ClockId,
                context.Source.ClockId,
                context.Source.Kind);
            return SimulationSessionExecutionPlan<FPSimulationStep>.FromOwnedCollections(
                SimulationSessionExecutionPlanStatus.Executable,
                context.Source,
                characterRuntime.Runtime.GameplayContentHash,
                context.Pipeline.Hash,
                characterRuntime.Runtime.RosterDescriptor,
                m_SourceMappings,
                null,
                m_Steps,
                SimulationSessionPlanRequirement.WorkingState |
                SimulationSessionPlanRequirement.OutputDisposition);
        }

        static bool IsReplayInput(FPCanonicalInputBatch canonical)
        {
            for (int i = 0; i < canonical.Inputs.Count; i++)
            {
                if (!FPCharacterInputTraceModule.IsReplayInput(canonical.Inputs[i].Value.Input))
                    return false;
            }
            return true;
        }
    }

    public sealed class FPLocalSingleStepScheduleReadPorts : ISimulationPipelineReadPortSet
    {
        public FPLocalSingleStepScheduleReadPorts(
            IReadOnlySimulationPipelineProductPort<FPCanonicalInputBatch> canonicalInputs,
            IReadOnlySimulationPipelineProductPort<FPTypedIngressBatch> typedIngress,
            IFPCharacterRuntimePort characterRuntime)
        {
            CanonicalInputs = canonicalInputs ?? throw new ArgumentNullException(nameof(canonicalInputs));
            TypedIngress = typedIngress ?? throw new ArgumentNullException(nameof(typedIngress));
            CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
        }

        public IReadOnlySimulationPipelineProductPort<FPCanonicalInputBatch> CanonicalInputs { get; }
        public IReadOnlySimulationPipelineProductPort<FPTypedIngressBatch> TypedIngress { get; }
        public IFPCharacterRuntimePort CharacterRuntime { get; }
    }

    public sealed class FPLocalSingleStepScheduleWritePorts : ISimulationPipelineWritePortSet
    {
        public FPLocalSingleStepScheduleWritePorts(
            IExclusiveSimulationPipelineProductWriter<SimulationSessionExecutionPlan<FPSimulationStep>> executionPlan)
        {
            ExecutionPlan = executionPlan ?? throw new ArgumentNullException(nameof(executionPlan));
        }

        public IExclusiveSimulationPipelineProductWriter<SimulationSessionExecutionPlan<FPSimulationStep>> ExecutionPlan { get; }
    }
}
