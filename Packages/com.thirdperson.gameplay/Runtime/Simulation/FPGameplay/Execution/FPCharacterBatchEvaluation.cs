using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;
using ThirdPersonPerformance.Instrumentation;

namespace ThirdPersonSimulation.FPGameplay
{
    public sealed class FPCharacterBatchEvaluation : IGraphBatchWork
    {
        readonly FPCharacterRuntime m_Characters;
        readonly FPCharacterEvaluationResult[] m_Evaluations;
        readonly FPCharacterEvaluationInput[] m_EvaluationInputs;
        readonly IReadOnlyList<SimulationActorBinding> m_Roster;
        readonly Exception[] m_Failures;
#if UNITY_EDITOR
        readonly long[] m_InputPreparationElapsedStopwatchTicks;
#endif
        readonly GraphBatchExecution m_BatchExecution;
        readonly CharacterWorldSolveRequest[] m_Requests;
        readonly SimulationIngress[][] m_Ingress;
        readonly int[] m_IngressCounts;
        readonly int[] m_CommandCapacities;
        readonly long[] m_RequestedIngressEntries;
        readonly long[] m_RequestedGameplayCommands;
        readonly GameplayCommandBatchAdmission[] m_IngressAdmissions;
        readonly FPCharacterEvaluationResultBatch m_EvaluationBatch;
        readonly WorldSolveBatchRequest m_WorldBatch;
        readonly FPCombatRuntime m_Combat;

        public FPCharacterBatchEvaluation(
            FPCharacterRuntime characters,
            FPCombatRuntime combat,
            GraphBatchExecution batchExecution)
        {
            m_Characters = characters;
            IReadOnlyList<SimulationActorBinding> roster = characters.Roster;
            m_Combat = combat;
            int actorCount = roster.Count;
            m_Roster = roster;
            m_BatchExecution = batchExecution;
            m_EvaluationInputs = new FPCharacterEvaluationInput[actorCount];
            m_Failures = new Exception[actorCount];
#if UNITY_EDITOR
            m_InputPreparationElapsedStopwatchTicks = new long[actorCount];
#endif
            m_Evaluations = new FPCharacterEvaluationResult[actorCount];
            m_Requests = new CharacterWorldSolveRequest[actorCount];
            m_Ingress = new SimulationIngress[actorCount][];
            m_IngressCounts = new int[actorCount];
            m_CommandCapacities = new int[actorCount];
            m_RequestedIngressEntries = new long[actorCount];
            m_RequestedGameplayCommands = new long[actorCount];
            m_IngressAdmissions = new GameplayCommandBatchAdmission[actorCount];
            m_EvaluationBatch = new FPCharacterEvaluationResultBatch(actorCount);
            m_WorldBatch = new WorldSolveBatchRequest(actorCount, combat.LocalEnemyCount);
            for (int i = 0; i < actorCount; i++)
            {
                FPGameplayEffectRuntimeCatalog catalog = roster[i].AbilityInstallations.GameplayEffectCatalog;
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

        public void Evaluate(
            in SimulationWorldStateSet state,
            FPSimulationStep step,
            IReadOnlyList<WorldKinematicBodySample> kinematicBodies,
            ISimulationDiagnosticsSink diagnostics,
            out FPCharacterEvaluationResultBatch evaluations,
            out WorldSolveBatchRequest worldRequests)
        {
            if (!state.IsValid || state.Actors.Count != m_Roster.Count)
                throw new InvalidOperationException("角色求值基线与已锁定 roster 不匹配。");

            try
            {
                m_Combat.AdvanceTimeSlows();
                PrepareIngress(step);
                for (int i = 0; i < m_Evaluations.Length; i++)
                {
                    SimulationActorBinding actor = m_Characters.Roster[i];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (step.ExecutionKind == SimulationPipelineStepExecutionKind.Forward && m_IngressAdmissions[i].Accepted)
                        m_IngressCounts[i] = m_Characters.CopySimulatedAttackIngress(
                            i, state.Actors[i].State, state, step.Tick, m_Combat, m_Ingress[i], m_IngressCounts[i]);
#endif
                    if (!state.Actors[i].ActorId.Equals(actor.ActorId) ||
                        !state.WorldState.Bodies[i].ActorId.Equals(actor.ActorId) ||
                        !step.Inputs[i].ActorId.Equals(actor.ActorId))
                        throw new InvalidOperationException("角色求值的 Actor 顺序与已锁定 roster 不匹配。");
                    actor.EvaluationOutput.IngressAdmission = m_IngressAdmissions[i];
#if UNITY_EDITOR
                    long inputPreparationStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                    actor.CaptureAvailabilityFrameEvents(m_Ingress[i], m_IngressCounts[i]);
                    for (int ability = 0; ability < actor.Invocations.Length; ability++)
                        actor.Invocations[ability].PrepareEvaluationInputs();
#if UNITY_EDITOR
                    m_InputPreparationElapsedStopwatchTicks[i] =
                        System.Diagnostics.Stopwatch.GetTimestamp() - inputPreparationStarted;
#endif
                    SimulationInput input = step.Inputs[i].Value.Input;
                    if (state.Actors[i].State.NumericProfile != m_Characters.NumericProfile ||
                        input.NumericProfile != m_Characters.NumericProfile)
                        throw new InvalidOperationException("角色求值输入的数值目标与已准备内容不同。");
                    m_EvaluationInputs[i] = new FPCharacterEvaluationInput(
                        m_Characters, actor, state.Actors[i].State, step.Tick, input,
                        m_Ingress[i], m_IngressCounts[i], state.WorldState.Bodies[i],
                        diagnostics.IsActorEnabled(actor.ActorId),
                        diagnostics is ISimulationValueTraceInterest valueInterest &&
                        valueInterest.IsValueCaptureRequested(actor.ActorId),
                        diagnostics is ISimulationControlTraceInterest controlInterest &&
                        controlInterest.IsControlCaptureRequested(actor.ActorId),
                        diagnostics is ISimulationBlackboardTraceInterest blackboardInterest &&
                        blackboardInterest.IsBlackboardCaptureRequested(actor.ActorId),
                        diagnostics.IsCaptureRequested(actor.ActorId, SimulationDiagnosticChannel.Motion),
                        step.ExecutionKind == SimulationPipelineStepExecutionKind.Forward,
                        m_Combat.PrepareEvaluation(i));
                }
                m_BatchExecution.Execute(this, m_Evaluations.Length);
                for (int i = 0; i < m_Failures.Length; i++)
                    if (m_Failures[i] != null)
                    {
                        m_BatchExecution.ReportFailure(i, m_Failures[i]);
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(m_BatchExecution.LastFailure).Throw();
                    }
                for (int i = 0; i < m_Evaluations.Length; i++)
                {
#if UNITY_EDITOR
                    m_Evaluations[i].Output.EvaluationInputSnapshotPrepareElapsedStopwatchTicks =
                        m_InputPreparationElapsedStopwatchTicks[i];
#endif
                    m_Combat.AdoptEvaluation(i);
                    for (int ability = 0; ability < m_Roster[i].Invocations.Length; ability++)
                        m_Roster[i].Invocations[ability].AdoptEvaluationInputs();
                }
                evaluations = m_EvaluationBatch.Reset(step.Tick, m_Evaluations);
                worldRequests = m_WorldBatch.Reset(step.Tick, state.WorldState, m_Requests, kinematicBodies);
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
#if UNITY_EDITOR
                    FPCharacterEvaluationOutput inputTimingOutput = m_Evaluations[i]?.Output;
                    long inputDiscardStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                    m_Evaluations[i] = null;
                    m_EvaluationInputs[i] = default;
                    for (int ability = 0; ability < m_Roster[i].Invocations.Length; ability++)
                        m_Roster[i].Invocations[ability].DiscardEvaluationInputs();
#if UNITY_EDITOR
                    if (inputTimingOutput != null)
                        inputTimingOutput.EvaluationInputSnapshotDiscardElapsedStopwatchTicks =
                            System.Diagnostics.Stopwatch.GetTimestamp() - inputDiscardStarted;
#endif
                    m_Failures[i] = null;
                    m_Requests[i] = default;
                    Array.Clear(m_Ingress[i], 0, m_IngressCounts[i]);
                    m_IngressCounts[i] = 0;
                    m_RequestedIngressEntries[i] = 0;
                    m_RequestedGameplayCommands[i] = 0;
                    m_IngressAdmissions[i] = default;
                }
            }
        }

        void IGraphBatchWork.Compute(int instanceIndex)
        {
            try
            {
                m_Evaluations[instanceIndex] = FPCharacterEvaluationRuntime.Evaluate(
                    m_Roster[instanceIndex], in m_EvaluationInputs[instanceIndex], out m_Requests[instanceIndex]);
            }
            catch (Exception exception)
            {
                m_Failures[instanceIndex] = exception;
            }
        }

        PerformanceInstrumentationContext IGraphBatchWork.GetInstrumentationContext(int instanceIndex)
        {
            CanonicalTextValue actorIdentity = m_Roster[instanceIndex].ActorId.Value;
            using CanonicalTextReadLease read = actorIdentity.AcquireRead();
            return new(0, m_EvaluationInputs[instanceIndex].Tick.Value,
                PerformanceInstrumentationIdentity.Hash64(actorIdentity.Span), 0, 0,
                PerformanceInstrumentationContextFlags.LogicTick | PerformanceInstrumentationContextFlags.Actor);
        }

        void PrepareIngress(FPSimulationStep step)
        {
            for (int i = 0; i < step.Ingress.Count; i++)
            {
                int actorIndex = m_Characters.GetActorIndex(step.Ingress[i].ActorId);
                m_RequestedIngressEntries[actorIndex]++;
                m_RequestedGameplayCommands[actorIndex] += step.Ingress[i].Value.GameplayCommandCount;
            }
            for (int actorIndex = 0; actorIndex < m_Ingress.Length; actorIndex++)
            {
                SimulationActorBinding actor = m_Characters.Roster[actorIndex];
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
                int actorIndex = m_Characters.GetActorIndex(step.Ingress[i].ActorId);
                if (m_IngressAdmissions[actorIndex].Accepted)
                    m_Ingress[actorIndex][m_IngressCounts[actorIndex]++] = step.Ingress[i].Value;
            }
            for (int actorIndex = 0; actorIndex < m_Ingress.Length; actorIndex++)
                if (m_IngressAdmissions[actorIndex].Accepted)
                    m_IngressCounts[actorIndex] = m_Combat.CopyPendingIngress(actorIndex,
                        m_Ingress[actorIndex], m_IngressCounts[actorIndex]);
        }
    }

}
