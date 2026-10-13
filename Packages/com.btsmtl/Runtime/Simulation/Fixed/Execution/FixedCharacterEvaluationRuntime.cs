using MotionContributionBuffer = ThirdPersonSimulation.MotionContributionBuffer<ThirdPersonSimulation.Fixed.FixedScalar, ThirdPersonSimulation.Fixed.FixedVector2, ThirdPersonSimulation.Fixed.FixedVector3, ThirdPersonSimulation.Fixed.FixedSimulationMath>;
using ResolvedGameplayMotion = ThirdPersonSimulation.ResolvedGameplayMotion<ThirdPersonSimulation.Fixed.FixedScalar, ThirdPersonSimulation.Fixed.FixedVector2, ThirdPersonSimulation.Fixed.FixedVector3>;
using SimulationMotionContribution = ThirdPersonSimulation.SimulationMotionContribution<ThirdPersonSimulation.Fixed.FixedScalar, ThirdPersonSimulation.Fixed.FixedVector2, ThirdPersonSimulation.Fixed.FixedVector3, ThirdPersonSimulation.Fixed.FixedSimulationMath>;
using ResolvedMotionChannel = ThirdPersonSimulation.ResolvedMotionChannel<ThirdPersonSimulation.Fixed.FixedScalar, ThirdPersonSimulation.Fixed.FixedVector2, ThirdPersonSimulation.Fixed.FixedVector3, ThirdPersonSimulation.Fixed.FixedSimulationMath>;
using CharacterMotionResolver = ThirdPersonSimulation.CharacterMotionResolver<ThirdPersonSimulation.Fixed.FixedScalar, ThirdPersonSimulation.Fixed.FixedVector2, ThirdPersonSimulation.Fixed.FixedVector3, ThirdPersonSimulation.Fixed.FixedYaw, ThirdPersonSimulation.Fixed.FixedSimulationMath>;
using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    internal static class FixedCharacterEvaluationRuntime
    {
        [ThirdPersonPerformance.Instrumentation.PerformanceProbe("simulation.character.evaluate")]
        public static FixedCharacterEvaluationResult Evaluate(
            SimulationActorBinding actor,
            in FixedCharacterEvaluationInput evaluation,
            out CharacterWorldSolveRequest worldRequest)
        {
#if UNITY_EDITOR
            long stageStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            FixedCharacterRuntimeState sourceState = evaluation.State;
            SimulationTick tick = evaluation.Tick;
            SimulationInput input = evaluation.Input;
            SimulationIngress[] ingress = evaluation.Ingress;
            int ingressCount = evaluation.IngressCount;
            WorldBodyState beforeBody = evaluation.Body;
            bool diagnosticsEnabled = evaluation.DiagnosticsEnabled;
            bool captureValues = evaluation.CaptureValues;
            bool captureControlFlow = evaluation.CaptureControlFlow;
            bool captureBlackboard = evaluation.CaptureBlackboard;
            bool captureMotion = evaluation.CaptureMotion;
            bool applyBlackboardCommands = evaluation.ApplyBlackboardCommands;
            FixedCombatEvaluation combat = evaluation.Combat;

            FixedGameplayEffectRuntimeCatalog effectCatalog = actor.AbilityInstallations.GameplayEffectCatalog;
            actor.EvaluationOutput.BeginMotionWarpState(actor.MotionWarpPages, sourceState.MotionWarpStates);
            var roleState = actor.RuntimeState.Restart(
                sourceState,
                tick,
                evaluation.TickRate,
                effectCatalog,
                actor.InputRequestState,
                actor.ActionState,
                actor.ControlState,
                actor.EventSequenceState,
                actor.HandleAllocatorState,
                actor.GameplayEffectState,
                actor.EquipmentState,
                actor.EvaluationOutput.MotionWarpStates);
            FixedAbilityInvocationRuntime[] invocations = actor.Invocations;
            int invocationCount = invocations.Length;
            Dictionary<CharacterSkillId, IFixedAbilityActivationPort> actionRuntimes = actor.ActionRuntimes;
            FixedGameplayEffectExecutionScratch sharedEffectScratch = actor.EffectExecutionScratch;
            FixedGameplayEffectOperationRuntime gameplayEffects = null;
            if (actor.EvaluationOutput.IngressAdmission.Accepted)
                sharedEffectScratch.CommandsThisTick = (int)actor.EvaluationOutput.IngressAdmission.RequestedCommands;
            FixedAbilityExecutionWorkspace[] workspaces = actor.Workspaces;
            List<AbilityTimelineAdvancePending> timelineAdvances = actor.TimelineAdvances;
            List<AbilityTimelineStopPending> timelineStops = actor.TimelineStops;
            List<AbilityTimelineLogicMotion> timelineLogicMotion = actor.TimelineLogicMotion;
            FixedCharacterEvaluationOutput evaluationOutput = actor.EvaluationOutput;
            evaluationOutput.BindValueTraces(workspaces);
            List<SimulationTraceRecord> trace = evaluationOutput.Trace;
            List<SimulationTraceRecord> characterTrace = evaluationOutput.CharacterTrace;
            Dictionary<ulong, FixedAbilityInvocationRuntime> actionOwners = actor.ActionOwnerByInstanceId;
            FixedAbilityExecutionInput abilityInput = actor.AbilityExecutionInput;
#if UNITY_EDITOR
            long stageFinished = System.Diagnostics.Stopwatch.GetTimestamp();
            evaluationOutput.EvaluationStateElapsedStopwatchTicks = stageFinished - stageStarted;
            stageStarted = stageFinished;
#endif
            try
            {
                actor.CombatQueries.Clear();
                evaluationOutput.BeginDiagnostics(diagnosticsEnabled || captureMotion);
                abilityInput.Begin(input, ingress, ingressCount);
                ApplyReactionTargets(actor.ActorId, roleState.ActionState, ingress, ingressCount);
                actionOwners.Clear();
                ReadOnlySpan<FixedActionInstanceState> sourceActions = sourceState.ActionInstances;
                for (int i = 0; i < sourceActions.Length; i++)
                {
                    FixedActionInstanceState action = sourceActions[i];
                    actionOwners.Add(action.InstanceId, actor.GetInvocation(action.SkillId));
                }
                var bodyFacts = new FixedAbilityBodyFacts(actor.ActorId, beforeBody);
                MotionContributionBuffer motionContributions = actor.MotionContributions;
                motionContributions.Clear();
                FixedCharacterControlMotionRuntime controlMotion = actor.ControlMotion;
                controlMotion.Begin(
                    abilityInput,
                    bodyFacts,
                    tick,
                    evaluation.TickRate,
                    combat.ActionTimeScale);
                FixedCharacterTraceSink characterTraceSink = actor.CharacterTraceSink;
                characterTraceSink.Bind(evaluationOutput);
                characterTraceSink.Begin(
                    actor.ActorId,
                    tick,
                    diagnosticsEnabled,
                    captureMotion);
                for (int i = 0; i < actor.AbilityInstallations.Installations.Count; i++)
                {
                    FixedGameplayAbilityExecutionInstallation installation = actor.AbilityInstallations.Installations[i];
                    FixedAbilityInvocationRuntime invocation = invocations[i];
                    invocation.Begin(new FixedAbilityInvocationContext(
                        actor.ActorId,
                        tick,
                        abilityInput,
                        bodyFacts,
                        roleState.BindAbility(installation.Identity.AbilityId),
                        roleState,
                        roleState.InputRequests,
                        roleState.ActionState,
                        roleState.HandleAllocatorState,
                        roleState.EventSequenceState,
                        roleState.GameplayEffectState,
                        roleState.EquipmentState,
                        actor.CombatQueries,
                        combat));
                    if (gameplayEffects == null && invocation.HasGameplayEffects)
                    {
                        gameplayEffects = invocation.GameplayEffects;
                        gameplayEffects.BeginEvaluation();
                    }
                    invocation.BeginEvaluation(diagnosticsEnabled, captureMotion, captureValues, captureControlFlow, captureBlackboard);
                    actionRuntimes.Add(invocation.AbilityId, invocation.Actions);
                }

                roleState.InputRequests.ApplyRequests(input.Requests);

#if UNITY_EDITOR
                long inputStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                evaluationOutput.EvaluationInputsPrepareElapsedStopwatchTicks = inputStarted - stageStarted;
#endif
                bool effectAdvanced = false;
                for (int i = 0; i < invocationCount; i++)
                {
                    FixedAbilityInvocationRuntime invocation = invocations[i];
#if UNITY_EDITOR
                    inputStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                    ApplyIngress(invocation, ingress, ingressCount, actionOwners);
                    if (!effectAdvanced && invocation.HasGameplayEffects)
                    {
                        ApplyGameplayEffectIngress(invocation, ingress, ingressCount);
                        invocation.AdvanceGameplayEffects();
                        effectAdvanced = true;
                    }
#if UNITY_EDITOR
                    long inputFinished = System.Diagnostics.Stopwatch.GetTimestamp();
                    evaluationOutput.EvaluationInputsIngressElapsedStopwatchTicks += inputFinished - inputStarted;
                    inputStarted = inputFinished;
#endif
                    invocation.ApplyInputBindings();
#if UNITY_EDITOR
                    evaluationOutput.EvaluationInputsBindingsElapsedStopwatchTicks +=
                        System.Diagnostics.Stopwatch.GetTimestamp() - inputStarted;
#endif
                }

                if (!effectAdvanced)
                    RequireNoGameplayEffectIngress(ingress, ingressCount);

#if UNITY_EDITOR
                inputStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                for (int index = 0; index < invocationCount; index++)
                    invocations[index].ApplyBlackboardInputs(applyBlackboardCommands);

#if UNITY_EDITOR
                stageFinished = System.Diagnostics.Stopwatch.GetTimestamp();
                evaluationOutput.EvaluationInputsBlackboardElapsedStopwatchTicks = stageFinished - inputStarted;
                evaluationOutput.EvaluationInputsElapsedStopwatchTicks = stageFinished - stageStarted;
                stageStarted = stageFinished;
#endif
                FixedCharacterControlRuntime control = evaluation.Control;
                control.Begin(
                    roleState,
                    tick,
                    bodyFacts,
                    combat.ActionTimeScale);
                control.PrepareDecision();
                FixedAbilityActivationRuleRuntime rules = actor.ActivationRules;
                rules.BeforeAbility(control, roleState.InputRequests, roleState.ActionState);
                control.Tick(rules.BlockMovingTurn);
                controlMotion.CopyContributionsTo(motionContributions);
#if UNITY_EDITOR
                stageFinished = System.Diagnostics.Stopwatch.GetTimestamp();
                evaluationOutput.EvaluationControlElapsedStopwatchTicks = stageFinished - stageStarted;
                stageStarted = stageFinished;
#endif

#if UNITY_EDITOR
                long abilitiesStarted = stageStarted;
#endif
                for (int i = 0; i < invocationCount; i++)
                {
                    FixedAbilityInvocationRuntime invocation = invocations[i];
                    invocation.Tick();
#if UNITY_EDITOR
                    stageFinished = System.Diagnostics.Stopwatch.GetTimestamp();
                    evaluationOutput.EvaluationAbilitiesTickElapsedStopwatchTicks += stageFinished - stageStarted;
                    stageStarted = stageFinished;
#endif
                    invocation.TransferMotionContributionsTo(motionContributions);
#if UNITY_EDITOR
                    stageFinished = System.Diagnostics.Stopwatch.GetTimestamp();
                    evaluationOutput.EvaluationAbilitiesMotionElapsedStopwatchTicks += stageFinished - stageStarted;
                    stageStarted = stageFinished;
#endif
                }

                control.ApplyMovementIntent(rules.AfterAbility());
#if UNITY_EDITOR
                stageFinished = System.Diagnostics.Stopwatch.GetTimestamp();
                evaluationOutput.EvaluationAbilitiesRulesElapsedStopwatchTicks = stageFinished - stageStarted;
                evaluationOutput.EvaluationAbilitiesElapsedStopwatchTicks = stageFinished - abilitiesStarted;
                stageStarted = stageFinished;
#endif

                for (int i = 0; i < timelineAdvances.Count; i++)
                {
                    actor.TimelineMotionReader.CopyPendingMotion(
                        timelineAdvances[i].RuntimeHandle,
                        timelineLogicMotion);
                    AppendTimelineMotion(motionContributions, timelineLogicMotion);
                }

                ResolvedGameplayMotion gameplayMotion = ResolveMotion(
                    motionContributions.Values,
                    motionContributions.Count,
                    beforeBody.Yaw,
                    invocations,
                    invocationCount,
                    characterTraceSink);
#if UNITY_EDITOR
                stageFinished = System.Diagnostics.Stopwatch.GetTimestamp();
                evaluationOutput.EvaluationMotionElapsedStopwatchTicks = stageFinished - stageStarted;
                stageStarted = stageFinished;
#endif
                for (int i = 0; i < invocationCount; i++)
                {
                    FixedAbilityInvocationRuntime invocation = invocations[i];
                    invocation.Complete(evaluationOutput);
                    invocation.Accept(roleState);
                }
                trace.AddRange(characterTrace);
                FixedScalar tickDelta = FixedScalar.One / FixedScalar.FromInt64(evaluation.TickRate);
                BodyMotionPrepareResult bodyMotion = CharacterBodyMotionRuntime.Prepare(
                    actor.ActorId,
                    tick,
                    beforeBody,
                    gameplayMotion,
                    actor.BodyMotionBinding,
                    tickDelta);
                WorldCapability requiredCapabilities = evaluation.RequiredWorldCapabilities |
                    actor.BodyMotionBinding.RequiredWorldCapability;
                worldRequest = new CharacterWorldSolveRequest(
                    evaluation.NumericProfile,
                    actor.ActorId,
                    new WorldRequestId(actor.ActorId, tick, 1),
                    tick,
                    beforeBody,
                    bodyMotion.Motion,
                    bodyMotion.Plan,
                    requiredCapabilities);
                FixedCharacterRuntimeState candidateState = roleState.Commit();
                evaluationOutput.RetainStatePages(candidateState);
                evaluationOutput.RetainOutputText();
                roleState.Dispose();
                FixedCharacterEvaluationResult result = actor.EvaluationResult.Reset(
                    tick,
                    candidateState,
                    evaluationOutput,
                    timelineAdvances,
                    timelineStops);
                actor.RotateEvaluationOutput();
                actor.ClearActionRuntimes();
                actor.ClearWorkspaces();
                actor.ClearTimelineTransfers();
                actor.ClearTimelineMotionScratches();
                abilityInput.Clear();
#if UNITY_EDITOR
                stageFinished = System.Diagnostics.Stopwatch.GetTimestamp();
                evaluationOutput.EvaluationCommitElapsedStopwatchTicks = stageFinished - stageStarted;
#endif
                return result;
            }
            catch
            {
                DiscardTimelineAdvances(actor.TimelineRuntime, timelineAdvances);
                DiscardTimelineStops(actor.TimelineRuntime, timelineStops);
                for (int i = 0; i < invocationCount; i++)
                    invocations[i].Abort();
                actor.ClearActionRuntimes();
                actor.ClearWorkspaces();
                actor.ClearTimelineTransfers();
                actor.ClearTimelineMotionScratches();
                evaluationOutput.Clear();
                abilityInput.Clear();
                actor.ControlMotion.ClearContributions();
                actor.MotionContributions.Clear();
                roleState.Dispose();
                throw;
            }
            finally
            {
                gameplayEffects?.EndEvaluation();
                sharedEffectScratch.Reset();
            }
        }

        static void DiscardTimelineStops(
            IAbilityTimelineRuntime timelineRuntime,
            IReadOnlyList<AbilityTimelineStopPending> stops)
        {
            if (timelineRuntime == null)
                return;
            for (int i = 0; i < stops.Count; i++)
            {
                if (!stops[i].IsValid)
                    throw new InvalidOperationException("Fixed Character evaluation has an empty Timeline stop.");
                timelineRuntime.DiscardStop(stops[i]);
            }
        }

        static void DiscardTimelineAdvances(
            IAbilityTimelineRuntime timelineRuntime,
            IReadOnlyList<AbilityTimelineAdvancePending> advances)
        {
            if (timelineRuntime == null)
                return;
            for (int i = 0; i < advances.Count; i++)
            {
                if (!advances[i].IsValid)
                    throw new InvalidOperationException("Fixed Character evaluation has an empty Timeline advance.");
                timelineRuntime.Discard(advances[i]);
            }
        }

        static void ApplyIngress(
            FixedAbilityInvocationRuntime invocation,
            SimulationIngress[] ingress,
            int ingressCount,
            Dictionary<ulong, FixedAbilityInvocationRuntime> actionOwners)
        {
            for (int i = 0; i < ingressCount; i++)
            {
                SimulationIngress value = ingress[i];
                if (value.Header.Kind != SimulationIngressKind.ActionLifecycle ||
                    !actionOwners.TryGetValue(value.ActionLifecycle.ActionInstanceId, out FixedAbilityInvocationRuntime owner) ||
                    !ReferenceEquals(owner, invocation))
                    continue;
                invocation.ApplyActionIngress(value);
            }
        }

        static void ApplyGameplayEffectIngress(
            FixedAbilityInvocationRuntime invocation,
            SimulationIngress[] ingress,
            int ingressCount)
        {
            for (int i = 0; i < ingressCount; i++)
                if (ingress[i].Header.Kind != SimulationIngressKind.ActionLifecycle &&
                    ingress[i].Header.Kind != SimulationIngressKind.ActionEvent &&
                    (ingress[i].Header.Kind != SimulationIngressKind.GameplayResult || ingress[i].GameplayResult.HasEffect))
                    invocation.ApplyGameplayEffectIngress(ingress[i]);
        }

        static void RequireNoGameplayEffectIngress(SimulationIngress[] ingress, int ingressCount)
        {
            for (int i = 0; i < ingressCount; i++)
                if (ingress[i].Header.Kind != SimulationIngressKind.ActionLifecycle &&
                    ingress[i].Header.Kind != SimulationIngressKind.ActionEvent &&
                    (ingress[i].Header.Kind != SimulationIngressKind.GameplayResult || ingress[i].GameplayResult.HasEffect))
                    throw new InvalidOperationException(
                        "Fixed Character evaluation received Gameplay Effect ingress without an installed Gameplay Effect service.");
        }

        internal static (bool Found, EquipmentActionContext Context) ReadEquipmentActionContext(
            FixedAbilityInvocationRuntime[] invocations,
            EquipmentActionRouteId route)
        {
            for (int i = 0; i < invocations.Length; i++)
            {
                IEquipmentActionContextReader equipment = invocations[i].Equipment;
                if (equipment == null || !equipment.HasActionRoute(route))
                    continue;
                return equipment.TryReadActionContext(route, out EquipmentActionContext context)
                    ? (true, context)
                    : (false, default);
            }
            return (false, default);
        }

        static void ApplyReactionTargets(
            ActorId actorId,
            IFixedActionRuntimeStatePort actions,
            SimulationIngress[] ingress,
            int ingressCount)
        {
            for (int i = 0; i < ingressCount; i++)
            {
                SimulationIngress value = ingress[i];
                if (value.Header.Kind != SimulationIngressKind.GameplayResult || value.GameplayResult.HasEffect)
                    continue;
                CombatHitResult hit = value.GameplayResult.Hit;
                if (hit.TargetActor != actorId ||
                    hit.Disposition != CombatHitDisposition.Parried && hit.Disposition != CombatHitDisposition.Evaded)
                    continue;
                if (!actions.TryFindActionInstanceIndex(hit.TargetActionInstanceId, out int index))
                    continue;
                FixedActionInstanceState action = actions.GetActionInstances()[index];
                if (action.IsActive)
                    actions.ReplaceActionInstanceAt(index, action.WithTargetSnapshot(hit.SourceTargetSnapshot));
            }
        }

        static ResolvedGameplayMotion ResolveMotion(
            SimulationMotionContribution[] contributions,
            int contributionCount,
            FixedYaw bodyYaw,
            FixedAbilityInvocationRuntime[] invocations,
            int invocationCount,
            FixedCharacterTraceSink trace)
        {
            ResolvedMotionChannel locomotion = CharacterMotionResolver.ResolveChannel(
                contributions,
                contributionCount,
                bodyYaw,
                SimulationMotionChannel.Locomotion);
            ResolvedMotionChannel action = CharacterMotionResolver.ResolveChannel(
                contributions,
                contributionCount,
                bodyYaw,
                SimulationMotionChannel.Action);
            ResolvedMotionChannel gameplayResult = CharacterMotionResolver.ResolveChannel(
                contributions,
                contributionCount,
                bodyYaw,
                SimulationMotionChannel.GameplayResult);
            for (int i = 0; i < invocationCount; i++)
                invocations[i].ApplyTimelineMotionWarps(ref action);
            trace.AddMotionChannel(locomotion);
            trace.AddMotionChannel(action);
            trace.AddMotionChannel(gameplayResult);
            ResolvedGameplayMotion motion = CharacterMotionResolver.Compose(locomotion, action, gameplayResult);
            ref readonly ResolvedMotionChannel source = ref (gameplayResult.TraceSource.IsValid ? ref gameplayResult :
                ref (action.TraceSource.IsValid ? ref action : ref locomotion));
            if (source.TraceSource.IsValid)
                trace.AddResolvedMotion(motion, source);
            return motion;
        }

        static void AppendTimelineMotion(
            MotionContributionBuffer contributions,
            IReadOnlyList<AbilityTimelineLogicMotion> timelineMotion)
        {
            for (int i = 0; i < timelineMotion.Count; i++)
            {
                AbilityTimelineLogicMotion value = timelineMotion[i];
                contributions.Append(new SimulationMotionContribution(
                    value.Source,
                    value.SourceIdentity,
                    value.AbilityId,
                    value.SourceGeneration,
                    new FixedVector3(value.DisplacementX, value.DisplacementY, value.DisplacementZ),
                    value.YawDegrees,
                    FixedVector2.Zero,
                    (SimulationMotionContributionSpace)value.Space,
                    value.Weight,
                    value.Priority,
                    (SimulationMotionChannel)value.Channel,
                    (SimulationMotionBlendMode)value.BlendMode,
                    value.ConsumeLowerChannels,
                    default,
                    default));
            }
        }

}
}
