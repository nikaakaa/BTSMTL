using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public sealed class Float32AbilityEvaluatePassRuntimeFactory : IFloat32PipelinePassRuntimeFactory
    {
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFloat32PipelinePassContracts.CreateFactoryDescriptor(
                StandardFloat32PipelinePassContracts.AbilityEvaluate);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;

        public IFloat32CompiledPipelinePassRuntime Create(Float32PipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new Float32AbilityEvaluateReadPorts(
                context.BindTargetPort<IFloat32CharacterRuntimePort>(Float32PipelineRuntimePortIds.CharacterRuntime),
                context.BindTargetPort<IFloat32WorkingStateReadPort>(Float32PipelineRuntimePortIds.WorkingState),
                context.BindDiagnosticsPort<IFloat32DiagnosticsRuntimePort>(Float32PipelineRuntimePortIds.Diagnostics));
            var writes = new Float32AbilityEvaluateWritePorts(
                context.Products.BindExclusiveWriter<Float32CharacterEvaluationResultBatch>(SimulationPipelineProducts.CharacterEvaluationResults),
                context.Products.BindExclusiveWriter<WorldSolveBatchRequest>(SimulationPipelineProducts.WorldSolveBatchRequest));
            return new Float32StepPassRuntimeAdapter<Float32AbilityEvaluateReadPorts, Float32AbilityEvaluateWritePorts>(
                new Float32AbilityEvaluatePassRuntime(context.Pass.Descriptor, reads.CharacterRuntime.Runtime.Roster),
                reads,
                writes);
        }
    }

    public sealed class Float32AbilityEvaluatePassRuntime :
        Float32PipelinePassRuntimeBase,
        ISimulationStepPassRuntime<Float32AbilityEvaluateReadPorts, Float32AbilityEvaluateWritePorts>
    {
        readonly Float32CharacterEvaluationResult[] m_Evaluations;
        readonly CharacterWorldSolveRequest[] m_Requests;
        readonly SimulationIngress[][] m_Ingress;
        readonly int[] m_IngressCounts;
        readonly int[] m_CommandCapacities;
        readonly long[] m_RequestedIngressEntries;
        readonly long[] m_RequestedGameplayCommands;
        readonly GameplayCommandBatchAdmission[] m_IngressAdmissions;
        readonly Float32CharacterEvaluationResultBatch m_EvaluationBatch;
        readonly WorldSolveBatchRequest m_WorldBatch;

        public Float32AbilityEvaluatePassRuntime(
            SimulationPipelinePassDescriptor descriptor,
            IReadOnlyList<SimulationActorBinding> roster)
            : base(descriptor)
        {
            int actorCount = roster.Count;
            if (actorCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(roster));
            m_Evaluations = new Float32CharacterEvaluationResult[actorCount];
            m_Requests = new CharacterWorldSolveRequest[actorCount];
            m_Ingress = new SimulationIngress[actorCount][];
            m_IngressCounts = new int[actorCount];
            m_CommandCapacities = new int[actorCount];
            m_RequestedIngressEntries = new long[actorCount];
            m_RequestedGameplayCommands = new long[actorCount];
            m_IngressAdmissions = new GameplayCommandBatchAdmission[actorCount];
            m_EvaluationBatch = new Float32CharacterEvaluationResultBatch(actorCount);
            m_WorldBatch = new WorldSolveBatchRequest(actorCount);
            for (int i = 0; i < actorCount; i++)
            {
                Float32GameplayEffectRuntimeCatalog catalog = roster[i].AbilityInstallations.GameplayEffectCatalog;
                int commands = catalog == null ? 0 : catalog.ActorCapacity.CommandsPerTick;
                m_CommandCapacities[i] = commands;
                m_Ingress[i] = new SimulationIngress[commands];
                roster[i].PrepareAvailabilityIngressCapacity(m_Ingress[i].Length);
            }
        }

        public void Execute(
            SimulationPipelineStepTransactionContext context,
            Float32AbilityEvaluateReadPorts readPorts,
            Float32AbilityEvaluateWritePorts writePorts)
        {
            RequireExecution();
            SimulationWorldStateSet state = readPorts.WorkingState.Current ??
                throw new InvalidOperationException("Ability Evaluate Pass has no working state.");
            Float32SimulationStep step = readPorts.WorkingState.Step ??
                throw new InvalidOperationException("Ability Evaluate Pass has no current Step.");
            if (step.Tick != context.Tick || state.Actors.Count != readPorts.CharacterRuntime.Runtime.Roster.Count ||
                state.Actors.Count != m_Evaluations.Length)
                throw new InvalidOperationException("Ability Evaluate Pass Step does not match the working roster.");

            try
            {
                PrepareIngress(step, readPorts.CharacterRuntime);
                for (int i = 0; i < m_Evaluations.Length; i++)
                {
                    SimulationActorBinding actor = readPorts.CharacterRuntime.Runtime.Roster[i];
                    if (!state.Actors[i].ActorId.Equals(actor.ActorId) ||
                        !state.WorldState.Bodies[i].ActorId.Equals(actor.ActorId) ||
                        !step.Inputs[i].ActorId.Equals(actor.ActorId))
                        throw new InvalidOperationException("Ability Evaluate Pass Actor order does not match the locked roster.");
                    actor.EvaluationOutput.IngressAdmission = m_IngressAdmissions[i];
                    SimulationInput input = step.Inputs[i].Value.Input;
                    CharacterWorldSolveRequest worldRequest;
                    m_Evaluations[i] = Float32CharacterEvaluationRuntime.Evaluate(
                        readPorts.CharacterRuntime.Runtime,
                        actor,
                        state.Actors[i].State,
                        step.Tick,
                        input,
                        m_Ingress[i],
                        m_IngressCounts[i],
                        state.WorldState.Bodies[i],
                        readPorts.Diagnostics.Sink.IsEnabled,
                        readPorts.Diagnostics.Sink is ISimulationValueTraceInterest valueInterest &&
                        valueInterest.IsValueCaptureRequested(actor.ActorId),
                        readPorts.Diagnostics.Sink is ISimulationControlTraceInterest controlInterest &&
                        controlInterest.IsControlCaptureRequested(actor.ActorId),
                        readPorts.Diagnostics.Sink is ISimulationBlackboardTraceInterest blackboardInterest &&
                        blackboardInterest.IsBlackboardCaptureRequested(actor.ActorId),
                        out worldRequest);
                    m_Requests[i] = worldRequest;
                }
                writePorts.CharacterEvaluationResults.Write(m_EvaluationBatch.Reset(step.Tick, m_Evaluations));
                writePorts.WorldBatch.Write(m_WorldBatch.Reset(
                    step.Tick,
                    state.WorldState,
                    m_Requests,
                    step.ObservedWorldConstraints));
            }
            catch
            {
                for (int i = 0; i < m_Evaluations.Length; i++)
                    m_Evaluations[i]?.DiscardUnconsumed();
                throw;
            }
            finally
            {
                for (int i = 0; i < m_Evaluations.Length; i++)
                {
                    m_Evaluations[i] = null;
                    m_Requests[i] = default;
                    Array.Clear(m_Ingress[i], 0, m_IngressCounts[i]);
                    m_IngressCounts[i] = 0;
                    m_RequestedIngressEntries[i] = 0;
                    m_RequestedGameplayCommands[i] = 0;
                    m_IngressAdmissions[i] = default;
                }
            }
        }

        void PrepareIngress(Float32SimulationStep step, IFloat32CharacterRuntimePort runtime)
        {
            for (int i = 0; i < step.Ingress.Count; i++)
            {
                int actorIndex = runtime.Runtime.GetActorIndex(step.Ingress[i].ActorId);
                m_RequestedIngressEntries[actorIndex]++;
                m_RequestedGameplayCommands[actorIndex] += step.Ingress[i].Value.GameplayCommandCount;
            }
            for (int actorIndex = 0; actorIndex < m_Ingress.Length; actorIndex++)
            {
                SimulationActorBinding actor = runtime.Runtime.Roster[actorIndex];
                m_IngressAdmissions[actorIndex] = new GameplayCommandBatchAdmission(actor.ActorId, step.Tick.Value,
                    m_RequestedIngressEntries[actorIndex], m_RequestedGameplayCommands[actorIndex],
                    m_Ingress[actorIndex].Length, m_CommandCapacities[actorIndex], SimulationExecutionSource.FromGameplayEffect());
            }
            for (int i = 0; i < step.Ingress.Count; i++)
            {
                int actorIndex = runtime.Runtime.GetActorIndex(step.Ingress[i].ActorId);
                if (m_IngressAdmissions[actorIndex].Accepted)
                    m_Ingress[actorIndex][m_IngressCounts[actorIndex]++] = step.Ingress[i].Value;
            }
        }
    }

    public sealed class Float32AbilityEvaluateReadPorts : ISimulationPipelineReadPortSet
    {
        public Float32AbilityEvaluateReadPorts(
            IFloat32CharacterRuntimePort characterRuntime,
            IFloat32WorkingStateReadPort workingState,
            IFloat32DiagnosticsRuntimePort diagnostics)
        {
            CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            WorkingState = workingState ?? throw new ArgumentNullException(nameof(workingState));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public IFloat32CharacterRuntimePort CharacterRuntime { get; }
        public IFloat32WorkingStateReadPort WorkingState { get; }
        public IFloat32DiagnosticsRuntimePort Diagnostics { get; }
    }

    public sealed class Float32AbilityEvaluateWritePorts : ISimulationPipelineWritePortSet
    {
        public Float32AbilityEvaluateWritePorts(
            IExclusiveSimulationPipelineProductWriter<Float32CharacterEvaluationResultBatch> characterEvaluationResults,
            IExclusiveSimulationPipelineProductWriter<WorldSolveBatchRequest> worldBatch)
        {
            CharacterEvaluationResults = characterEvaluationResults ?? throw new ArgumentNullException(nameof(characterEvaluationResults));
            WorldBatch = worldBatch ?? throw new ArgumentNullException(nameof(worldBatch));
        }

        public IExclusiveSimulationPipelineProductWriter<Float32CharacterEvaluationResultBatch> CharacterEvaluationResults { get; }
        public IExclusiveSimulationPipelineProductWriter<WorldSolveBatchRequest> WorldBatch { get; }
    }
}
