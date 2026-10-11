using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class FixedLocalSingleStepSchedulePassRuntimeFactory : IFixedPipelinePassRuntimeFactory, ISimulationScheduleCapacity
    {
        internal static readonly GameplayEffectScheduleCapacity Capacity = new GameplayEffectScheduleCapacity(1, 0, 0, 0, 0, 0);
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFixedLocalPipelinePassContracts.CreateFactoryDescriptor(
                StandardFixedLocalPipelinePassContracts.LocalSingleStepSchedule);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;
        public GameplayEffectScheduleCapacity GameplayEffectCapacity => Capacity;
        public int MaximumStepCount => Capacity.MaximumStepCount;

        public IFixedCompiledPipelinePassRuntime Create(FixedPipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new FixedLocalSingleStepScheduleReadPorts(
                context.Products.BindExclusiveReader<FixedCanonicalInputBatch>(SimulationPipelineProducts.CanonicalInputs),
                context.Products.BindExclusiveReader<FixedTypedIngressBatch>(SimulationPipelineProducts.TypedIngress),
                context.BindTargetPort<IFixedCharacterRuntimePort>(FixedPipelineRuntimePortIds.CharacterRuntime));
            var writes = new FixedLocalSingleStepScheduleWritePorts(
                context.Products.BindExclusiveWriter<SimulationSessionExecutionPlan<FixedSimulationStep>>(
                    SimulationPipelineProducts.ExecutionPlan));
            return new FixedSchedulePassRuntimeAdapter<FixedLocalSingleStepScheduleReadPorts, FixedLocalSingleStepScheduleWritePorts>(
                new FixedLocalSingleStepSchedulePassRuntime(
                    context.Pass.Descriptor,
                    reads.CharacterRuntime.Runtime.RosterDescriptor.Actors),
                reads,
                writes);
        }
    }

    public sealed class FixedLocalSingleStepSchedulePassRuntime :
        FixedPipelinePassRuntimeBase,
        ISimulationExecutionPlanSchedulePassRuntime<FixedLocalSingleStepScheduleReadPorts, FixedLocalSingleStepScheduleWritePorts>
    {
        readonly SimulationPipelineStepSourceMapping[] m_SourceMappings = new SimulationPipelineStepSourceMapping[1];
        readonly FixedSimulationStep[] m_Steps =
            new FixedSimulationStep[FixedLocalSingleStepSchedulePassRuntimeFactory.Capacity.MaximumStepCount];
        public GameplayEffectScheduleCapacity GameplayEffectCapacity => FixedLocalSingleStepSchedulePassRuntimeFactory.Capacity;
        public int MaximumStepCount => GameplayEffectCapacity.MaximumStepCount;

        public FixedLocalSingleStepSchedulePassRuntime(
            SimulationPipelinePassDescriptor descriptor,
            IReadOnlyList<ActorId> actors)
            : base(descriptor)
        {
            m_Steps[0] = new FixedSimulationStep(actors);
        }

        public void Execute(
            SimulationPipelineScheduleContext context,
            FixedLocalSingleStepScheduleReadPorts readPorts,
            FixedLocalSingleStepScheduleWritePorts writePorts)
        {
            RequireExecution();
            writePorts.ExecutionPlan.Write(Build(
                context,
                readPorts.CanonicalInputs.Read(),
                readPorts.TypedIngress.Read(),
                readPorts.CharacterRuntime));
        }

        SimulationSessionExecutionPlan<FixedSimulationStep> Build(
            SimulationPipelineScheduleContext context,
            FixedCanonicalInputBatch canonical,
            FixedTypedIngressBatch typed,
            IFixedCharacterRuntimePort characterRuntime)
        {
            if (!canonical.IsValid || !typed.IsValid || characterRuntime == null)
                throw new ArgumentNullException("Fixed Local single-step Schedule input is missing.");
            if (context.Source.Kind != SimulationTickSourceKind.LocalLogic || !canonical.Source.Equals(context.Source) ||
                canonical.Inputs.Count != characterRuntime.Runtime.Roster.Count)
            {
                throw new InvalidOperationException("Fixed Local single-step input batch does not match the outer Tick or locked roster.");
            }
            IReadOnlyList<ActorId> actorIds = characterRuntime.Runtime.RosterDescriptor.Actors;
            for (int i = 0; i < actorIds.Count; i++)
            {
                if (!canonical.Inputs[i].ActorId.Equals(actorIds[i]))
                    throw new InvalidOperationException("Fixed Local input Actor order does not match the locked roster.");
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
            return SimulationSessionExecutionPlan<FixedSimulationStep>.FromOwnedCollections(
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

        static bool IsReplayInput(FixedCanonicalInputBatch canonical)
        {
            for (int i = 0; i < canonical.Inputs.Count; i++)
            {
                if (!FixedCharacterInputTraceModule.IsReplayInput(canonical.Inputs[i].Value.Input))
                    return false;
            }
            return true;
        }
    }

    public sealed class FixedLocalSingleStepScheduleReadPorts : ISimulationPipelineReadPortSet
    {
        public FixedLocalSingleStepScheduleReadPorts(
            IReadOnlySimulationPipelineProductPort<FixedCanonicalInputBatch> canonicalInputs,
            IReadOnlySimulationPipelineProductPort<FixedTypedIngressBatch> typedIngress,
            IFixedCharacterRuntimePort characterRuntime)
        {
            CanonicalInputs = canonicalInputs ?? throw new ArgumentNullException(nameof(canonicalInputs));
            TypedIngress = typedIngress ?? throw new ArgumentNullException(nameof(typedIngress));
            CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
        }

        public IReadOnlySimulationPipelineProductPort<FixedCanonicalInputBatch> CanonicalInputs { get; }
        public IReadOnlySimulationPipelineProductPort<FixedTypedIngressBatch> TypedIngress { get; }
        public IFixedCharacterRuntimePort CharacterRuntime { get; }
    }

    public sealed class FixedLocalSingleStepScheduleWritePorts : ISimulationPipelineWritePortSet
    {
        public FixedLocalSingleStepScheduleWritePorts(
            IExclusiveSimulationPipelineProductWriter<SimulationSessionExecutionPlan<FixedSimulationStep>> executionPlan)
        {
            ExecutionPlan = executionPlan ?? throw new ArgumentNullException(nameof(executionPlan));
        }

        public IExclusiveSimulationPipelineProductWriter<SimulationSessionExecutionPlan<FixedSimulationStep>> ExecutionPlan { get; }
    }
}
