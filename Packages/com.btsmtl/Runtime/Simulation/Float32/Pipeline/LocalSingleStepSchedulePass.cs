using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public sealed class LocalSingleStepSchedulePassRuntimeFactory : IFloat32PipelinePassRuntimeFactory, ISimulationScheduleCapacity
    {
        internal static readonly GameplayEffectScheduleCapacity Capacity = new GameplayEffectScheduleCapacity(1, 0, 0, 0, 0, 0);
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFloat32PipelinePassContracts.CreateFactoryDescriptor(
                StandardFloat32PipelinePassContracts.LocalSingleStepSchedule);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;
        public GameplayEffectScheduleCapacity GameplayEffectCapacity => Capacity;
        public int MaximumStepCount => Capacity.MaximumStepCount;

        public IFloat32CompiledPipelinePassRuntime Create(Float32PipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new LocalSingleStepScheduleReadPorts(
                context.Products.BindExclusiveReader<Float32CanonicalInputBatch>(SimulationPipelineProducts.CanonicalInputs),
                context.Products.BindExclusiveReader<Float32TypedIngressBatch>(SimulationPipelineProducts.TypedIngress),
                context.BindTargetPort<IFloat32CharacterRuntimePort>(Float32PipelineRuntimePortIds.CharacterRuntime));
            var writes = new LocalSingleStepScheduleWritePorts(
                context.Products.BindExclusiveWriter<SimulationSessionExecutionPlan<Float32SimulationStep>>(
                    SimulationPipelineProducts.ExecutionPlan));
            return new Float32SchedulePassRuntimeAdapter<LocalSingleStepScheduleReadPorts, LocalSingleStepScheduleWritePorts>(
                new LocalSingleStepSchedulePassRuntime(
                    context.Pass.Descriptor,
                    reads.CharacterRuntime.Runtime.RosterDescriptor.Actors),
                reads,
                writes);
        }
    }

    public sealed class LocalSingleStepSchedulePassRuntime :
        Float32PipelinePassRuntimeBase,
        ISimulationExecutionPlanSchedulePassRuntime<LocalSingleStepScheduleReadPorts, LocalSingleStepScheduleWritePorts>
    {
        readonly SimulationPipelineStepSourceMapping[] m_SourceMappings = new SimulationPipelineStepSourceMapping[1];
        readonly Float32SimulationStep[] m_Steps =
            new Float32SimulationStep[LocalSingleStepSchedulePassRuntimeFactory.Capacity.MaximumStepCount];
        readonly ObservedWorldConstraintFrame m_ObservedWorldConstraints = new ObservedWorldConstraintFrame();
        public GameplayEffectScheduleCapacity GameplayEffectCapacity => LocalSingleStepSchedulePassRuntimeFactory.Capacity;
        public int MaximumStepCount => GameplayEffectCapacity.MaximumStepCount;

        public LocalSingleStepSchedulePassRuntime(
            SimulationPipelinePassDescriptor descriptor,
            IReadOnlyList<ActorId> actors)
            : base(descriptor)
        {
            m_Steps[0] = new Float32SimulationStep(actors);
        }

        public void Execute(
            SimulationPipelineScheduleContext context,
            LocalSingleStepScheduleReadPorts readPorts,
            LocalSingleStepScheduleWritePorts writePorts)
        {
            RequireExecution();
            writePorts.ExecutionPlan.Write(Build(
                context,
                readPorts.CanonicalInputs.Read(),
                readPorts.TypedIngress.Read(),
                readPorts.CharacterRuntime));
        }

        SimulationSessionExecutionPlan<Float32SimulationStep> Build(
            SimulationPipelineScheduleContext context,
            Float32CanonicalInputBatch canonical,
            Float32TypedIngressBatch typed,
            IFloat32CharacterRuntimePort characterRuntime)
        {
            if (!canonical.IsValid || !typed.IsValid || characterRuntime == null)
                throw new ArgumentNullException("Single-step Schedule input is missing.");
            if (context.Source.Kind != SimulationTickSourceKind.LocalLogic || !canonical.Source.Equals(context.Source) ||
                canonical.Inputs.Count != characterRuntime.Runtime.Roster.Count)
            {
                throw new InvalidOperationException("Single-step Schedule input batch does not match the outer Tick or locked roster.");
            }
            IReadOnlyList<ActorId> actorIds = characterRuntime.Runtime.RosterDescriptor.Actors;
            for (int i = 0; i < actorIds.Count; i++)
            {
                if (!canonical.Inputs[i].ActorId.Equals(actorIds[i]))
                    throw new InvalidOperationException("Single-step Schedule input Actor order does not match the locked roster.");
            }
            var tick = new SimulationTick(checked(context.CurrentCompletedTick + 1));
            bool replay = IsReplayInput(canonical);
            m_ObservedWorldConstraints.ResetEmpty(tick);
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
                typed.Ingress,
                m_ObservedWorldConstraints);
            m_SourceMappings[0] = new SimulationPipelineStepSourceMapping(
                context.Source.ClockId,
                context.Source.ClockId,
                context.Source.Kind);
            return SimulationSessionExecutionPlan<Float32SimulationStep>.FromOwnedArrays(
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

        static bool IsReplayInput(Float32CanonicalInputBatch canonical)
        {
            for (int i = 0; i < canonical.Inputs.Count; i++)
            {
                if (!Float32CharacterInputTraceModule.IsReplayInput(canonical.Inputs[i].Value.Input))
                    return false;
            }
            return true;
        }
    }

    public sealed class LocalSingleStepScheduleReadPorts : ISimulationPipelineReadPortSet
    {
        public LocalSingleStepScheduleReadPorts(
            IReadOnlySimulationPipelineProductPort<Float32CanonicalInputBatch> canonicalInputs,
            IReadOnlySimulationPipelineProductPort<Float32TypedIngressBatch> typedIngress,
            IFloat32CharacterRuntimePort characterRuntime)
        {
            CanonicalInputs = canonicalInputs ?? throw new ArgumentNullException(nameof(canonicalInputs));
            TypedIngress = typedIngress ?? throw new ArgumentNullException(nameof(typedIngress));
            CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
        }

        public IReadOnlySimulationPipelineProductPort<Float32CanonicalInputBatch> CanonicalInputs { get; }
        public IReadOnlySimulationPipelineProductPort<Float32TypedIngressBatch> TypedIngress { get; }
        public IFloat32CharacterRuntimePort CharacterRuntime { get; }
    }

    public sealed class LocalSingleStepScheduleWritePorts : ISimulationPipelineWritePortSet
    {
        public LocalSingleStepScheduleWritePorts(
            IExclusiveSimulationPipelineProductWriter<SimulationSessionExecutionPlan<Float32SimulationStep>> executionPlan)
        {
            ExecutionPlan = executionPlan ?? throw new ArgumentNullException(nameof(executionPlan));
        }

        public IExclusiveSimulationPipelineProductWriter<SimulationSessionExecutionPlan<Float32SimulationStep>> ExecutionPlan { get; }
    }
}
