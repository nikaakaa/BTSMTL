using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class FixedAbilityEvaluatePassRuntimeFactory : IFixedPipelinePassRuntimeFactory
    {
        static readonly SimulationPipelinePassFactoryDescriptor s_Descriptor =
            StandardFixedPipelinePassContracts.CreateFactoryDescriptor(
                StandardFixedPipelinePassContracts.AbilityEvaluate);

        public SimulationPipelinePassFactoryDescriptor Descriptor => s_Descriptor;

        public IFixedCompiledPipelinePassRuntime Create(FixedPipelinePassRuntimeFactoryContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var reads = new FixedAbilityEvaluateReadPorts(
                context.Products.BindExclusiveReader<FixedCanonicalInputBatch>(SimulationPipelineProducts.CanonicalInputs),
                context.BindTargetPort<IFixedCharacterRuntimePort>(FixedPipelineRuntimePortIds.CharacterRuntime),
                context.BindTargetPort<IFixedWorkingStateReadPort>(FixedPipelineRuntimePortIds.WorkingState),
                context.BindTargetPort<IFixedCombatRuntimePort>(FixedPipelineRuntimePortIds.Combat),
                context.BindDiagnosticsPort<IFixedDiagnosticsRuntimePort>(FixedPipelineRuntimePortIds.Diagnostics));
            var writes = new FixedAbilityEvaluateWritePorts(
                context.Products.BindExclusiveWriter<FixedCharacterEvaluationResultBatch>(SimulationPipelineProducts.CharacterEvaluationResults),
                context.Products.BindExclusiveWriter<WorldSolveBatchRequest>(SimulationPipelineProducts.WorldSolveBatchRequest));
            return new FixedStepPassRuntimeAdapter<FixedAbilityEvaluateReadPorts, FixedAbilityEvaluateWritePorts>(
                new FixedAbilityEvaluatePassRuntime(context.Pass.Descriptor, reads.CharacterRuntime.Runtime.Roster, reads.Combat.Combat),
                reads,
                writes);
        }
    }

    public sealed class FixedAbilityEvaluatePassRuntime :
        FixedPipelinePassRuntimeBase,
        ISimulationStepPassRuntime<FixedAbilityEvaluateReadPorts, FixedAbilityEvaluateWritePorts>
    {
        readonly FixedCharacterEvaluationResult[] m_Evaluations;
        readonly CharacterWorldSolveRequest[] m_Requests;
        readonly SimulationIngress[][] m_Ingress;
        readonly int[] m_IngressCounts;
        readonly int[] m_CommandCapacities;
        readonly long[] m_RequestedIngressEntries;
        readonly long[] m_RequestedGameplayCommands;
        readonly GameplayCommandBatchAdmission[] m_IngressAdmissions;
        readonly FixedCharacterEvaluationResultBatch m_EvaluationBatch;
        readonly WorldSolveBatchRequest m_WorldBatch;
        readonly FixedCombatRuntime m_Combat;

        public FixedAbilityEvaluatePassRuntime(
            SimulationPipelinePassDescriptor descriptor,
            IReadOnlyList<SimulationActorBinding> roster,
            FixedCombatRuntime combat)
            : base(descriptor)
        {
            m_Combat = combat;
            int actorCount = roster.Count;
            if (actorCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(actorCount));
            m_Evaluations = new FixedCharacterEvaluationResult[actorCount];
            m_Requests = new CharacterWorldSolveRequest[actorCount];
            m_Ingress = new SimulationIngress[actorCount][];
            m_IngressCounts = new int[actorCount];
            m_CommandCapacities = new int[actorCount];
            m_RequestedIngressEntries = new long[actorCount];
            m_RequestedGameplayCommands = new long[actorCount];
            m_IngressAdmissions = new GameplayCommandBatchAdmission[actorCount];
            m_EvaluationBatch = new FixedCharacterEvaluationResultBatch(actorCount);
            m_WorldBatch = new WorldSolveBatchRequest(actorCount, combat.LocalEnemyCount);
            for (int i = 0; i < actorCount; i++)
            {
                FixedGameplayEffectRuntimeCatalog catalog = roster[i].AbilityInstallations.GameplayEffectCatalog;
                int commands = catalog == null ? 0 : catalog.ActorCapacity.CommandsPerTick;
                m_CommandCapacities[i] = commands;
                int entries = checked(commands + combat.ResultIngressCapacity);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                entries = checked(entries + roster[i].SimulatedIncomingAttackResults.Length);
#endif
                m_Ingress[i] = new SimulationIngress[entries];
                roster[i].PrepareAvailabilityIngressCapacity(m_Ingress[i].Length);
            }
        }

        public void Execute(
            SimulationPipelineStepTransactionContext context,
            FixedAbilityEvaluateReadPorts readPorts,
            FixedAbilityEvaluateWritePorts writePorts)
        {
            RequireExecution();
            SimulationWorldStateSet state = readPorts.WorkingState.Current;
            if (!state.IsValid)
                throw new InvalidOperationException("Ability Evaluate Pass has no working state.");
            FixedSimulationStep step = readPorts.WorkingState.Step ??
                throw new InvalidOperationException("Ability Evaluate Pass has no current Step.");
            if (step.Tick != context.Tick || state.Actors.Count != readPorts.CharacterRuntime.Runtime.Roster.Count ||
                state.Actors.Count != m_Evaluations.Length)
                throw new InvalidOperationException("Ability Evaluate Pass Step does not match the working roster.");

            try
            {
                m_Combat.AdvanceTimeSlows();
                PrepareIngress(step, readPorts.CharacterRuntime);
                for (int i = 0; i < m_Evaluations.Length; i++)
                {
                    SimulationActorBinding actor = readPorts.CharacterRuntime.Runtime.Roster[i];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (step.ExecutionKind == SimulationPipelineStepExecutionKind.Forward && m_IngressAdmissions[i].Accepted)
                        m_IngressCounts[i] = readPorts.CharacterRuntime.Runtime.CopySimulatedAttackIngress(
                            i, state.Actors[i].State, state, step.Tick, m_Combat, m_Ingress[i], m_IngressCounts[i]);
#endif
                    if (!state.Actors[i].ActorId.Equals(actor.ActorId) ||
                        !state.WorldState.Bodies[i].ActorId.Equals(actor.ActorId) ||
                        !step.Inputs[i].ActorId.Equals(actor.ActorId))
                        throw new InvalidOperationException("Ability Evaluate Pass Actor order does not match the locked roster.");
                    actor.EvaluationOutput.IngressAdmission = m_IngressAdmissions[i];
                    SimulationInput input = step.Inputs[i].Value.Input;
                    CharacterWorldSolveRequest worldRequest;
                    m_Evaluations[i] = FixedCharacterEvaluationRuntime.Evaluate(
                        readPorts.CharacterRuntime.Runtime,
                        actor,
                        state.Actors[i].State,
                        step.Tick,
                        input,
                        m_Ingress[i],
                        m_IngressCounts[i],
                        state.WorldState.Bodies[i],
                        readPorts.Combat.Combat,
                        readPorts.Diagnostics.Sink.IsEnabled,
                        readPorts.Diagnostics.Sink.IsCaptureRequested(actor.ActorId, SimulationDiagnosticChannel.Motion),
                        readPorts.Diagnostics.Sink is ISimulationValueTraceInterest valueInterest &&
                        valueInterest.IsValueCaptureRequested(actor.ActorId),
                        readPorts.Diagnostics.Sink is ISimulationControlTraceInterest controlInterest &&
                        controlInterest.IsControlCaptureRequested(actor.ActorId),
                        readPorts.Diagnostics.Sink is ISimulationBlackboardTraceInterest blackboardInterest &&
                        blackboardInterest.IsBlackboardCaptureRequested(actor.ActorId),
                        step.ExecutionKind == SimulationPipelineStepExecutionKind.Forward,
                        out worldRequest);
                    m_Requests[i] = worldRequest;
                }
                writePorts.CharacterEvaluationResults.Write(m_EvaluationBatch.Reset(step.Tick, m_Evaluations));
                writePorts.WorldBatch.Write(m_WorldBatch.Reset(step.Tick, state.WorldState, m_Requests, readPorts.Inputs.Read().KinematicBodies));
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

        void PrepareIngress(FixedSimulationStep step, IFixedCharacterRuntimePort runtime)
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
                m_Combat.CountPendingIngress(actorIndex, out int pendingEntries, out int pendingCommands);
                m_RequestedIngressEntries[actorIndex] += pendingEntries;
                m_RequestedGameplayCommands[actorIndex] += pendingCommands;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (step.ExecutionKind == SimulationPipelineStepExecutionKind.Forward)
                {
                    byte simulated = actor.SimulatedIncomingAttacks;
                    m_RequestedIngressEntries[actorIndex] += (simulated & 1) + ((simulated >> 1) & 1);
                }
#endif
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
            for (int actorIndex = 0; actorIndex < m_Ingress.Length; actorIndex++)
                if (m_IngressAdmissions[actorIndex].Accepted)
                    m_IngressCounts[actorIndex] = m_Combat.CopyPendingIngress(actorIndex,
                        m_Ingress[actorIndex], m_IngressCounts[actorIndex]);
        }
    }

    public sealed class FixedAbilityEvaluateReadPorts : ISimulationPipelineReadPortSet
    {
        public FixedAbilityEvaluateReadPorts(
            IReadOnlySimulationPipelineProductPort<FixedCanonicalInputBatch> inputs,
            IFixedCharacterRuntimePort characterRuntime,
            IFixedWorkingStateReadPort workingState,
            IFixedCombatRuntimePort combat,
            IFixedDiagnosticsRuntimePort diagnostics)
        {
            Inputs = inputs;
            CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            WorkingState = workingState ?? throw new ArgumentNullException(nameof(workingState));
            Combat = combat;
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public IFixedCharacterRuntimePort CharacterRuntime { get; }
        public IReadOnlySimulationPipelineProductPort<FixedCanonicalInputBatch> Inputs { get; }
        public IFixedWorkingStateReadPort WorkingState { get; }
        public IFixedCombatRuntimePort Combat { get; }
        public IFixedDiagnosticsRuntimePort Diagnostics { get; }
    }

    public sealed class FixedAbilityEvaluateWritePorts : ISimulationPipelineWritePortSet
    {
        public FixedAbilityEvaluateWritePorts(
            IExclusiveSimulationPipelineProductWriter<FixedCharacterEvaluationResultBatch> characterEvaluationResults,
            IExclusiveSimulationPipelineProductWriter<WorldSolveBatchRequest> worldBatch)
        {
            CharacterEvaluationResults = characterEvaluationResults ?? throw new ArgumentNullException(nameof(characterEvaluationResults));
            WorldBatch = worldBatch ?? throw new ArgumentNullException(nameof(worldBatch));
        }

        public IExclusiveSimulationPipelineProductWriter<FixedCharacterEvaluationResultBatch> CharacterEvaluationResults { get; }
        public IExclusiveSimulationPipelineProductWriter<WorldSolveBatchRequest> WorldBatch { get; }
    }
}
