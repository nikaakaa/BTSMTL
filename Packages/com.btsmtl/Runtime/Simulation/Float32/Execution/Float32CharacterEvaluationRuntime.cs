using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal static class Float32CharacterEvaluationRuntime
    {
        [ThirdPersonPerformance.Instrumentation.PerformanceProbe("simulation.character.evaluate")]
        public static Float32CharacterEvaluationResult Evaluate(
            SimulationActorBinding actor,
            in Float32CharacterEvaluationInput evaluation,
            out CharacterWorldSolveRequest worldRequest)
        {
            Float32CharacterRuntimeState sourceState = evaluation.State;
            SimulationTick tick = evaluation.Tick;
            SimulationInput input = evaluation.Input;
            SimulationIngress[] ingress = evaluation.Ingress;
            int ingressCount = evaluation.IngressCount;
            WorldBodyState beforeBody = evaluation.Body;
            bool diagnosticsEnabled = evaluation.DiagnosticsEnabled;
            bool captureValues = evaluation.CaptureValues;
            bool captureControlFlow = evaluation.CaptureControlFlow;
            bool captureBlackboard = evaluation.CaptureBlackboard;

            Float32GameplayEffectRuntimeCatalog effectCatalog = actor.AbilityInstallations.GameplayEffectCatalog;
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
            Float32AbilityInvocationRuntime[] invocations = actor.Invocations;
            int invocationCount = invocations.Length;
            Dictionary<CharacterSkillId, IFloat32AbilityActivationPort> actionRuntimes = actor.ActionRuntimes;
            Float32GameplayEffectExecutionScratch sharedEffectScratch = actor.EffectExecutionScratch;
            Float32GameplayEffectOperationRuntime gameplayEffects = null;
            if (actor.EvaluationOutput.IngressAdmission.Accepted)
                sharedEffectScratch.CommandsThisTick = (int)actor.EvaluationOutput.IngressAdmission.RequestedCommands;
            Float32AbilityExecutionWorkspace[] workspaces = actor.Workspaces;
            List<AbilityTimelineAdvancePending> timelineAdvances = actor.TimelineAdvances;
            List<AbilityTimelineStopPending> timelineStops = actor.TimelineStops;
            List<AbilityTimelineLogicMotion> timelineLogicMotion = actor.TimelineLogicMotion;
            Float32CharacterEvaluationOutput evaluationOutput = actor.EvaluationOutput;
            evaluationOutput.BindValueTraces(workspaces);
            List<SimulationTraceRecord> trace = evaluationOutput.Trace;
            List<SimulationTraceRecord> characterTrace = evaluationOutput.CharacterTrace;
            Dictionary<ulong, Float32AbilityInvocationRuntime> actionOwners = actor.ActionOwnerByInstanceId;
            Float32AbilityExecutionInput abilityInput = actor.AbilityExecutionInput;
            try
            {
                evaluationOutput.BeginDiagnostics(diagnosticsEnabled);
                abilityInput.Begin(input, ingress, ingressCount);
                actionOwners.Clear();
                for (int i = 0; i < sourceState.ActionInstances.Length; i++)
                {
                    Float32ActionInstanceState action = sourceState.ActionInstances[i];
                    actionOwners.Add(action.InstanceId, actor.GetInvocation(action.SkillId));
                }
                var bodyFacts = new Float32AbilityBodyFacts(actor.ActorId, beforeBody);
                Float32MotionContributionScratch motionContributions = actor.MotionContributions;
                motionContributions.Begin();
                Float32CharacterControlMotionRuntime controlMotion = actor.ControlMotion;
                controlMotion.Begin(
                    abilityInput,
                    bodyFacts,
                    tick,
                    evaluation.TickRate);
                Float32CharacterTraceSink characterTraceSink = actor.CharacterTraceSink;
                characterTraceSink.Bind(evaluationOutput);
                characterTraceSink.Begin(
                    evaluation.NumericProfile,
                    actor.GameplayContentHash,
                    actor.ActorId,
                    tick,
                    diagnosticsEnabled);
                for (int i = 0; i < actor.AbilityInstallations.Installations.Count; i++)
                {
                    Float32GameplayAbilityExecutionInstallation installation = actor.AbilityInstallations.Installations[i];
                    Float32AbilityInvocationRuntime invocation = invocations[i];
                    invocation.Begin(new Float32AbilityInvocationContext(
                        actor.ActorId,
                        tick,
                        abilityInput,
                        bodyFacts,
                        roleState.BindAbility(installation.Identity, installation.Layout, installation.Data),
                        roleState,
                        roleState.InputRequests,
                        roleState.ActionState,
                        roleState.HandleAllocatorState,
                        roleState.EventSequenceState,
                        roleState.GameplayEffectState,
                        roleState.EquipmentState));
                    if (gameplayEffects == null && invocation.HasGameplayEffects)
                    {
                        gameplayEffects = invocation.GameplayEffects;
                        gameplayEffects.BeginEvaluation();
                    }
                    invocation.BeginEvaluation(diagnosticsEnabled, captureValues, captureControlFlow, captureBlackboard);
                    actionRuntimes.Add(invocation.AbilityId, invocation.Actions);
                }

                roleState.InputRequests.ApplyRequests(input.Requests);

                bool effectAdvanced = false;
                for (int i = 0; i < invocationCount; i++)
                {
                    Float32AbilityInvocationRuntime invocation = invocations[i];
                    ApplyIngress(invocation, ingress, ingressCount, actionOwners);
                    if (!effectAdvanced && invocation.HasGameplayEffects)
                    {
                        ApplyGameplayEffectIngress(invocation, ingress, ingressCount);
                        invocation.AdvanceGameplayEffects();
                        effectAdvanced = true;
                    }
                    invocation.ApplyInputBindings();
                }

                if (!effectAdvanced)
                    RequireNoGameplayEffectIngress(ingress, ingressCount);

                Float32CharacterControlRuntime control = evaluation.Control;
                control.Begin(
                    roleState,
                    tick,
                    bodyFacts);
                control.PrepareDecision();
                Float32AbilityActivationRuleRuntime rules = actor.ActivationRules;
                rules.BeforeAbility(control, roleState.InputRequests, roleState.ActionState);
                control.Tick(rules.BlockMovingTurn);
                controlMotion.CopyContributionsTo(motionContributions);

                for (int i = 0; i < invocationCount; i++)
                {
                    Float32AbilityInvocationRuntime invocation = invocations[i];
                    invocation.Tick();
                    invocation.CopyMotionContributionsTo(motionContributions);
                }

                control.ApplyMovementIntent(rules.AfterAbility());

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
                for (int i = 0; i < invocationCount; i++)
                {
                    Float32AbilityInvocationRuntime invocation = invocations[i];
                    invocation.Complete(evaluationOutput);
                    invocation.Accept(roleState);
                }
                for (int index = 0; index < characterTrace.Count; index++)
                    characterTrace[index].Retain();
                trace.AddRange(characterTrace);
                Float32Scalar tickDelta = Float32Scalar.One / Float32Scalar.FromInt64(evaluation.TickRate);
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
                Float32CharacterRuntimeState candidateState = roleState.Commit();
                evaluationOutput.RetainStatePages(candidateState);
                roleState.Dispose();
                Float32CharacterEvaluationResult result = actor.EvaluationResult.Reset(
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
                    throw new InvalidOperationException("Float32 Character evaluation has an empty Timeline stop.");
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
                    throw new InvalidOperationException("Float32 Character evaluation has an empty Timeline advance.");
                timelineRuntime.Discard(advances[i]);
            }
        }

        static void ApplyIngress(
            Float32AbilityInvocationRuntime invocation,
            SimulationIngress[] ingress,
            int ingressCount,
            Dictionary<ulong, Float32AbilityInvocationRuntime> actionOwners)
        {
            for (int i = 0; i < ingressCount; i++)
            {
                SimulationIngress value = ingress[i];
                if (value.Header.Kind != SimulationIngressKind.ActionLifecycle ||
                    !actionOwners.TryGetValue(value.ActionLifecycle.ActionInstanceId, out Float32AbilityInvocationRuntime owner) ||
                    !ReferenceEquals(owner, invocation))
                    continue;
                invocation.ApplyActionIngress(value);
            }
        }

        static void ApplyGameplayEffectIngress(
            Float32AbilityInvocationRuntime invocation,
            SimulationIngress[] ingress,
            int ingressCount)
        {
            for (int i = 0; i < ingressCount; i++)
                if (ingress[i].Header.Kind != SimulationIngressKind.ActionLifecycle &&
                    ingress[i].Header.Kind != SimulationIngressKind.ActionEvent)
                    invocation.ApplyGameplayEffectIngress(ingress[i]);
        }

        static void RequireNoGameplayEffectIngress(SimulationIngress[] ingress, int ingressCount)
        {
            for (int i = 0; i < ingressCount; i++)
                if (ingress[i].Header.Kind != SimulationIngressKind.ActionLifecycle &&
                    ingress[i].Header.Kind != SimulationIngressKind.ActionEvent)
                    throw new InvalidOperationException(
                        "Float32 Character evaluation received Gameplay Effect ingress without an installed Gameplay Effect service.");
        }

        internal static (bool Found, EquipmentActionContext Context) ReadEquipmentActionContext(
            Float32AbilityInvocationRuntime[] invocations,
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

        static ResolvedGameplayMotion ResolveMotion(
            SimulationMotionContribution[] contributions,
            int contributionCount,
            Float32Yaw bodyYaw,
            Float32AbilityInvocationRuntime[] invocations,
            int invocationCount,
            Float32CharacterTraceSink trace)
        {
            ResolvedMotionChannel locomotion = Float32CharacterMotionResolver.ResolveChannel(
                contributions,
                contributionCount,
                bodyYaw,
                SimulationMotionChannel.Locomotion);
            ResolvedMotionChannel action = Float32CharacterMotionResolver.ResolveChannel(
                contributions,
                contributionCount,
                bodyYaw,
                SimulationMotionChannel.Action);
            ResolvedMotionChannel gameplayResult = Float32CharacterMotionResolver.ResolveChannel(
                contributions,
                contributionCount,
                bodyYaw,
                SimulationMotionChannel.GameplayResult);
            for (int i = 0; i < invocationCount; i++)
                invocations[i].ApplyTimelineMotionWarps(ref action);
            trace.AddMotionChannel(locomotion);
            trace.AddMotionChannel(action);
            trace.AddMotionChannel(gameplayResult);
            ResolvedGameplayMotion motion = Float32CharacterMotionResolver.Compose(locomotion, action, gameplayResult);
            ref readonly ResolvedMotionChannel source = ref (gameplayResult.TraceSource.IsValid ? ref gameplayResult :
                ref (action.TraceSource.IsValid ? ref action : ref locomotion));
            if (source.TraceSource.IsValid)
                trace.AddResolvedMotion(motion, source);
            return motion;
        }

        static void AppendTimelineMotion(
            Float32MotionContributionScratch contributions,
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
                    new Float32Vector3(
                        Float32Scalar.FromSingle(value.DisplacementX.ToSingle()),
                        Float32Scalar.FromSingle(value.DisplacementY.ToSingle()),
                        Float32Scalar.FromSingle(value.DisplacementZ.ToSingle())),
                    Float32Scalar.FromSingle(value.YawDegrees.ToSingle()),
                    Float32Vector2.Zero,
                    (SimulationMotionContributionSpace)value.Space,
                    Float32Scalar.FromSingle(value.Weight.ToSingle()),
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
